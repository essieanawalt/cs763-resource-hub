// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Barebones API with a health check, Docker and tests" (PA1 prompt 01)
// AI Contribution: Generated the controller and health check (~98%)
// Human Modifications: Removed a comment pointing to the PA1 spec (2026-11-14)
// Verification: dotnet build and dotnet test after the edit
// Confidence: High

using Microsoft.AspNetCore.Mvc;
using ResourceHub.Api.Data;

namespace ResourceHub.Api.Controllers;

[ApiController]
[Route("api/v1/health")]
// Classic constructor instead of a primary constructor: Semgrep's C# parser
// cannot parse primary constructors and would skip part of this file.
public class HealthController : ControllerBase
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    private readonly HubDbContext db;
    private readonly ILogger<HealthController> logger;

    public HealthController(HubDbContext db, ILogger<HealthController> logger)
    {
        this.db = db;
        this.logger = logger;
    }

    // 200 when PostgreSQL is reachable, 503 when not.
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        bool reachable;
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(CheckTimeout);
            try
            {
                reachable = await db.Database.CanConnectAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                reachable = false;
            }
        }

        if (reachable)
        {
            return Ok(new { status = "ok", database = "ok" });
        }

        logger.LogWarning("Health check failed: database unreachable");
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            new { status = "unhealthy", database = "unreachable" });
    }
}
