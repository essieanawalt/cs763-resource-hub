// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the X-User-Id authentication handler, role names and claim helpers (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: Medium

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Security;

// STAND-IN, NOT REAL AUTHENTICATION. The caller names a mock user in the X-User-Id header and
// is trusted to be that user. It exists so role rules can be enforced and tested until the
// Project Portal provides real identity (for example a signed token from an identity provider).
public sealed class UserHeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "UserHeader";
    public const string HeaderName = "X-User-Id";

    private readonly HubDbContext db;

    public UserHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        HubDbContext db)
        : base(options, logger, encoder)
    {
        this.db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return AuthenticateResult.NoResult();
        }

        if (values.Count != 1 || !Guid.TryParse(values[0], out var userId))
        {
            return AuthenticateResult.Fail($"{HeaderName} must be a single user id.");
        }

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.Fail("Unknown user.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, Roles.For(user.Role)),
        };
        if (user.CourseId is { } courseId)
        {
            claims.Add(new Claim(HubClaims.CourseId, courseId.ToString()));
        }
        if (user.ResearchLabId is { } labId)
        {
            claims.Add(new Claim(HubClaims.ResearchLabId, labId.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

public static class Roles
{
    public const string CourseInstructor = "course_instructor";
    public const string LabAdvisor = "lab_advisor";
    public const string ItDirector = "it_director";
    public const string Administrator = "administrator";

    public const string Requesters = CourseInstructor + "," + LabAdvisor;
    public const string Staff = ItDirector + "," + Administrator;

    public static string For(UserRole role) => role switch
    {
        UserRole.CourseInstructor => CourseInstructor,
        UserRole.LabAdvisor => LabAdvisor,
        UserRole.ItDirector => ItDirector,
        UserRole.Administrator => Administrator,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}

public static class HubClaims
{
    public const string CourseId = "hub:course_id";
    public const string ResearchLabId = "hub:research_lab_id";
}

// The caller as the services see it, built from the authenticated principal.
public sealed record Caller(Guid UserId, string Role, Guid? CourseId, Guid? ResearchLabId)
{
    public bool IsStaff => Role is Roles.ItDirector or Roles.Administrator;
    public bool IsAdministrator => Role == Roles.Administrator;
    public bool IsItDirector => Role == Roles.ItDirector;
    public bool IsRequester => Role is Roles.CourseInstructor or Roles.LabAdvisor;

    // Requesters only see their own course's or lab's tickets and allocations; staff see all.
    public bool CanSeeGroup(Guid? courseId, Guid? researchLabId) =>
        IsStaff
        || (CourseId is not null && CourseId == courseId)
        || (ResearchLabId is not null && ResearchLabId == researchLabId);

    public static Caller From(ClaimsPrincipal principal)
    {
        var id = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Caller is not authenticated."));
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        Guid? course = Guid.TryParse(principal.FindFirstValue(HubClaims.CourseId), out var c) ? c : null;
        Guid? lab = Guid.TryParse(principal.FindFirstValue(HubClaims.ResearchLabId), out var l) ? l : null;
        return new Caller(id, role, course, lab);
    }
}
