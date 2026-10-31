using Microsoft.AspNetCore.Mvc;
using ResourceHub.Api.Data;

namespace ResourceHub.Api.Controllers;

[ApiController]
[Route("api/v1/health")]
public class HealthController(HubDbContext db, ILogger<HealthController> logger) : ControllerBase
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

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
