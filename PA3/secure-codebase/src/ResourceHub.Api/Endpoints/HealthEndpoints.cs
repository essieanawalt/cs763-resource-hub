// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: GET /api/v1/health: 200 when the database is reachable, 503 when not; stays public (AllowAnonymous) under the deny-by-default policy (~100%)
// Human Modifications: None yet
// Verification: HealthTests, AuthTokenTests.Health_check_stays_public
// Confidence: High

using ResourceHub.Api.Data;

namespace ResourceHub.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/health", async (HubDbContext db, CancellationToken ct) =>
        {
            bool reachable;
            try
            {
                reachable = await db.Database.CanConnectAsync(ct);
            }
            catch (Exception)
            {
                reachable = false;
            }

            return reachable
                ? Results.Ok(new { status = "healthy", database = "reachable" })
                : Results.Json(new { status = "unhealthy", database = "unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();   // the only public endpoint
    }
}
