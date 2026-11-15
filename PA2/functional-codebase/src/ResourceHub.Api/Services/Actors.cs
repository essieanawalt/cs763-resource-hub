// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Resolves the calling mock user from X-User-Id and checks their role (~100%)
// Human Modifications: None (review only)
// Verification: Role tests in AssetCrudTests, TicketTests, AllocationTests; reviewed by me 2026-11-15: auth out of scope note interesting, as is X-User-id
// Confidence: Medium (stand-in for real authentication, which is out of scope)

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Services;

public sealed record Actor(Guid Id, string Name, UserRole Role, Guid? CourseId, Guid? ResearchLabId);

/// <summary>
/// Authentication is out of scope, so callers identify themselves with one of the seeded
/// mock users in the X-User-Id header. Missing or unknown ids get 401; the wrong role gets 403.
/// </summary>
public static class Actors
{
    public const string Header = "X-User-Id";

    public static async Task<(Actor? Actor, IResult? Error)> RequireAsync(
        HttpContext http, HubDbContext db, CancellationToken ct, params UserRole[] allowed)
    {
        var raw = http.Request.Headers[Header].ToString();
        if (!Guid.TryParse(raw, out var userId))
        {
            return (null, Problems.Unauthorized($"Send the id of a known user in the {Header} header."));
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return (null, Problems.Unauthorized($"The {Header} header does not name a known user."));
        }

        var actor = new Actor(user.Id, user.Name, user.Role, user.CourseId, user.ResearchLabId);
        if (allowed.Length > 0 && !allowed.Contains(user.Role))
        {
            return (actor, Problems.Forbidden($"This action needs one of these roles: {string.Join(", ", allowed)}."));
        }

        return (actor, null);
    }
}

/// <summary>RFC 7807 problem responses used across the endpoints.</summary>
public static class Problems
{
    public static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);

    public static IResult Conflict(string title, string? detail = null, IDictionary<string, object?>? extensions = null) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title, detail: detail, extensions: extensions);

    public static IResult Unauthorized(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unknown caller.", detail: detail);

    public static IResult Forbidden(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not allowed.", detail: detail);

    public static IResult BadRequest(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
