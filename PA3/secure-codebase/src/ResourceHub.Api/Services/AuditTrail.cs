// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Append-only audit trail (T2): successful changes are written in the same transaction; refused or failed writes are recorded by an endpoint filter (~100%)
// Human Modifications: None yet
// Verification: AuditTrailTests (dotnet test against PostgreSQL 18)
// Confidence: High

using Microsoft.AspNetCore.Http.HttpResults;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Services;

public static class AuditOutcome
{
    public const string Succeeded = "succeeded";
    public const string Denied = "denied";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
    public const string Failed = "failed";

    public static string FromStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest or StatusCodes.Status422UnprocessableEntity => Invalid,
        StatusCodes.Status403Forbidden => Denied,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status409Conflict => Conflict,
        _ => Failed,
    };
}

/// <summary>
/// Adds an audit row to the current unit of work. Callers save it together with the change it
/// describes, so a committed change always has its audit row. Never put secrets in the detail.
/// </summary>
public sealed class AuditTrail
{
    private readonly HubDbContext _db;
    private readonly TimeProvider _clock;

    public AuditTrail(HubDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public void Record(Caller caller, string action, string entityType, Guid? entityId, string outcome = AuditOutcome.Succeeded, string? detail = null) =>
        _db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredAt = _clock.GetUtcNow(),
            ActorSub = caller.Sub,
            ActorRoles = string.Join(",", caller.RoleNames.Order(StringComparer.Ordinal)),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Outcome = outcome,
            Detail = detail is { Length: > 2000 } ? detail[..2000] : detail,
        });
}

public static class AuditFilter
{
    /// <summary>
    /// Records a write that ended in 4xx/5xx (refused, not found, conflict, invalid). Successful
    /// writes are audited by the handler inside its transaction.
    /// </summary>
    public static RouteHandlerBuilder Audited(this RouteHandlerBuilder route, string action, string entityType, string? idRouteValue = null) =>
        route.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            if (result is not IStatusCodeHttpResult { StatusCode: >= 400 and var status })
            {
                return result;
            }

            var http = context.HttpContext;
            try
            {
                var db = http.RequestServices.GetRequiredService<HubDbContext>();
                db.ChangeTracker.Clear();   // anything the handler left unsaved was rolled back
                var caller = await Callers.ResolveAsync(http, db, http.RequestAborted);
                Guid? entityId = idRouteValue is not null && Guid.TryParse(http.Request.RouteValues[idRouteValue]?.ToString(), out var id) ? id : null;
                var detail = result is ProblemHttpResult problem ? problem.ProblemDetails.Title : $"HTTP {status}";
                http.RequestServices.GetRequiredService<AuditTrail>()
                    .Record(caller, action, entityType, entityId, AuditOutcome.FromStatus(status), detail);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ResourceHub.Audit")
                    .LogError(ex, "Could not record a {Outcome} audit event for {Action}", AuditOutcome.FromStatus(status), action);
            }
            return result;
        });
}
