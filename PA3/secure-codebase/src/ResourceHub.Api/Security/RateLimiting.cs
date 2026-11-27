// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Per-caller fixed-window rate limiting (T3): by verified sub when authenticated, by client IP otherwise (~100%)
// Human Modifications: None yet
// Verification: RateLimitTests; the full suite passes with the default limits
// Confidence: High

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ResourceHub.Api.Security;

public static class RateLimiting
{
    /// <summary>Requests per window for one authenticated caller (token sub).</summary>
    public const int DefaultPermitLimit = 300;

    /// <summary>Requests per window for one client IP without a valid token (health checks, bad tokens).</summary>
    public const int DefaultAnonymousPermitLimit = 200;

    public const int DefaultWindowSeconds = 60;

    public static IServiceCollection AddHubRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var permit = Positive(config.GetValue("RateLimiting:PermitLimit", DefaultPermitLimit), DefaultPermitLimit);
        var anonymous = Positive(config.GetValue("RateLimiting:AnonymousPermitLimit", DefaultAnonymousPermitLimit), DefaultAnonymousPermitLimit);
        var window = TimeSpan.FromSeconds(Positive(config.GetValue("RateLimiting:WindowSeconds", DefaultWindowSeconds), DefaultWindowSeconds));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : window;
                context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                return ValueTask.CompletedTask;
            };

            // Runs after authentication, so the partition is the verified sub; a forged or
            // invalid token falls into the (smaller) per-IP budget and cannot spend anyone else's.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var sub = http.User.Identity?.IsAuthenticated == true ? http.User.FindFirst(JwtAuthentication.SubClaim)?.Value : null;
                return sub is not null
                    ? RateLimitPartition.GetFixedWindowLimiter("sub:" + sub, _ => Window(permit, window))
                    : RateLimitPartition.GetFixedWindowLimiter("ip:" + http.Connection.RemoteIpAddress, _ => Window(anonymous, window));
            });
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permit, TimeSpan window) => new()
    {
        PermitLimit = permit,
        Window = window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static int Positive(int value, int fallback) => value > 0 ? value : fallback;
}
