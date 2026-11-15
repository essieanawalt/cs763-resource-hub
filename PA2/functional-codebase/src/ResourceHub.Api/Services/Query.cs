// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Parsing of page/pageSize and enum query parameters (~100%)
// Human Modifications: None (review only)
// Verification: Invalid_query_parameters_are_400 theories in AssetQueryTests and TicketQueryTests; reviewed by me 2026-11-15: snake_case is an interesting choice that I've never used
// Confidence: High

using System.Text.Json;

namespace ResourceHub.Api.Services;

public static class Query
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Paging(Errors errors, int? page, int? pageSize)
    {
        var p = page ?? 1;
        var size = pageSize ?? DefaultPageSize;
        if (p < 1)
        {
            errors.Add("page", "Must be 1 or more.");
        }
        if (size < 1 || size > MaxPageSize)
        {
            errors.Add("pageSize", $"Must be between 1 and {MaxPageSize}.");
        }
        return (p, size);
    }

    /// <summary>Parses an enum value written the way the API writes it (snake_case, e.g. "physical").</summary>
    public static TEnum? Enum<TEnum>(Errors errors, string field, string? raw) where TEnum : struct, System.Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        foreach (var value in System.Enum.GetValues<TEnum>())
        {
            if (string.Equals(JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()), raw.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }
        var allowed = string.Join(", ", System.Enum.GetValues<TEnum>().Select(v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString())));
        errors.Add(field, $"Must be one of: {allowed}.");
        return null;
    }
}
