// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: The caller comes only from the verified token (sub, roles); course/lab association is looked up from seeded users. Replaces the X-User-Id header (CWE-290) (~100%)
// Human Modifications: None yet
// Verification: AuthTokenTests, TicketAuthorizationTests, ActiveAllocationTests
// Confidence: High

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Data;
using ResourceHub.Api.Security;

namespace ResourceHub.Api.Services;

/// <summary>Token roles (spec: admin = IT director and administrator; faculty_instructor = course instructor and lab advisor).</summary>
public static class Roles
{
    public const string Admin = "admin";
    public const string Faculty = "faculty_instructor";
}

/// <summary>
/// The authenticated caller. <see cref="Id"/> is the token's sub and <see cref="RoleNames"/> its
/// roles claim; nothing here is ever read from the body, query string or other headers.
/// </summary>
public sealed record Caller(Guid Id, IReadOnlySet<string> RoleNames, Guid? CourseId, Guid? ResearchLabId)
{
    public string Sub => Id.ToString("D");

    public bool IsAdmin => RoleNames.Contains(Roles.Admin);

    public bool IsFaculty => RoleNames.Contains(Roles.Faculty);

    /// <summary>A faculty member's own course or research lab, from the seeded user data.</summary>
    public bool IsAssociatedWith(Guid? courseId, Guid? researchLabId) =>
        IsFaculty && ((courseId is not null && courseId == CourseId) || (researchLabId is not null && researchLabId == ResearchLabId));
}

public static class Callers
{
    private const string ItemKey = "resourcehub.caller";

    /// <summary>
    /// Builds the caller from the validated principal. Every endpoint except health requires an
    /// authenticated user (fallback policy), so a request without a valid token never gets here.
    /// </summary>
    public static async Task<Caller> ResolveAsync(HttpContext http, HubDbContext db, CancellationToken ct)
    {
        if (http.Items.TryGetValue(ItemKey, out var cached) && cached is Caller known)
        {
            return known;
        }

        var user = http.User;
        var sub = user.FindFirstValue(JwtAuthentication.SubClaim);
        if (user.Identity?.IsAuthenticated != true || !Guid.TryParse(sub, out var id))
        {
            // Unreachable while the fallback policy is in place; refuse rather than guess.
            throw new InvalidOperationException("Endpoint reached without an authenticated caller.");
        }

        var roles = user.FindAll(JwtAuthentication.RolesClaim).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        var association = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new { u.CourseId, u.ResearchLabId })
            .FirstOrDefaultAsync(ct);

        var caller = new Caller(id, roles, association?.CourseId, association?.ResearchLabId);
        http.Items[ItemKey] = caller;
        return caller;
    }

    /// <summary>403 unless the caller holds one of <paramref name="roles"/>.</summary>
    public static IResult? RequireRole(Caller caller, params string[] roles) =>
        roles.Any(caller.RoleNames.Contains)
            ? null
            : Problems.Forbidden($"This action needs one of these roles: {string.Join(", ", roles)}.");
}

/// <summary>RFC 7807 problem responses used across the endpoints.</summary>
public static class Problems
{
    public static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);

    public static IResult Conflict(string title, string? detail = null, IDictionary<string, object?>? extensions = null) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title, detail: detail, extensions: extensions);

    public static IResult Forbidden(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not allowed.", detail: detail);

    public static IResult Unprocessable(string title, string? detail = null) =>
        Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: title, detail: detail);

    public static IResult BadRequest(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
