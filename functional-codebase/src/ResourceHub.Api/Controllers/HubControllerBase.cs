// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the shared controller base: caller lookup and ProblemDetails mapping (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResourceHub.Api.Security;
using ResourceHub.Api.Services;

namespace ResourceHub.Api.Controllers;

// Every hub endpoint needs a known user in X-User-Id (see UserHeaderAuthenticationHandler).
[ApiController]
[Authorize]
[Produces("application/json")]
public abstract class HubControllerBase : ControllerBase
{
    protected Caller CurrentCaller => Caller.From(User);

    protected ActionResult Problem(ServiceError error)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(
            HttpContext, statusCode: error.Status, title: error.Title, detail: error.Detail);
        foreach (var (key, value) in error.Extensions)
        {
            problem.Extensions[key] = value;
        }
        return new ObjectResult(problem) { StatusCode = error.Status };
    }
}
