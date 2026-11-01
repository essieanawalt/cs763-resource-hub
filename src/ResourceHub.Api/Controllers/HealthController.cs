// AI Contribution Weight: [100%] AI Generated / [0%] Human Refactored
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

    // Spec §6: 200 when PostgreSQL is reachable, 503 when not. No X-User-Id needed.
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
