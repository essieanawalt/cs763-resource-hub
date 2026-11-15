// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the service result and error types (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

namespace ResourceHub.Api.Services;

// An expected failure, turned into a ProblemDetails response by the controller.
public sealed record ServiceError(int Status, string Title, string Detail)
{
    public IDictionary<string, object?> Extensions { get; init; } = new Dictionary<string, object?>();

    public static ServiceError BadRequest(string detail) =>
        new(StatusCodes.Status400BadRequest, "Invalid request", detail);

    public static ServiceError Forbidden(string detail) =>
        new(StatusCodes.Status403Forbidden, "Forbidden", detail);

    public static ServiceError NotFound(string what) =>
        new(StatusCodes.Status404NotFound, "Not found", $"{what} was not found.");

    public static ServiceError Conflict(string detail) =>
        new(StatusCodes.Status409Conflict, "Conflict", detail);
}

public sealed class Result<T>
{
    private Result(T? value, ServiceError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public ServiceError? Error { get; }

    public static implicit operator Result<T>(T value) => new(value, null);
    public static implicit operator Result<T>(ServiceError error) => new(default, error);
}

// For operations that return nothing on success.
public sealed record Done
{
    public static readonly Done Value = new();
}
