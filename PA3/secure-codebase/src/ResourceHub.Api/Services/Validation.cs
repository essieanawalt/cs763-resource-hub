// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Input validation for asset bodies, plus shared limits; PA3 adds control-character rejection and device token / check-in code checks (~100%)
// Human Modifications: None yet
// Verification: AssetCrudTests, TicketSubmitTests, WriteValidationTests, ReturnTests
// Confidence: High

using System.Text.RegularExpressions;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Services;

/// <summary>Collects field errors and turns them into a 400 validation problem.</summary>
public sealed class Errors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }
        list.Add(message);
    }

    public IResult ToResult() =>
        Results.ValidationProblem(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
}

public static partial class Validation
{
    public const int MaxUnits = 100_000;
    public const int MaxLoanDays = 365;
    public const int MaxKeys = 10_000;
    public const decimal MaxCredits = 10_000_000_000m;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,99}$")]
    private static partial Regex LicenseKeyPattern();

    /// <summary>Device tokens (HW-...) and check-in codes (CI-...): letters, digits and dashes, at most 40.</summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,39}$")]
    private static partial Regex DeviceCodePattern();

    /// <summary>A required device token or check-in code; null (with an error added) when missing or malformed.</summary>
    public static string? DeviceCode(Errors errors, string field, string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            errors.Add(field, "Required.");
            return null;
        }
        if (!DeviceCodePattern().IsMatch(trimmed))
        {
            errors.Add(field, "1 to 40 letters, digits or dashes.");
            return null;
        }
        return trimmed;
    }

    public static string? Text(Errors errors, string field, string? value, int maxLength, bool required = true)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (required)
            {
                errors.Add(field, "Required.");
            }
            return null;
        }
        if (trimmed.Length > maxLength)
        {
            errors.Add(field, $"At most {maxLength} characters.");
        }
        if (trimmed.Any(ch => char.IsControl(ch) && ch is not ('\n' or '\r' or '\t')))
        {
            errors.Add(field, "Control characters are not allowed.");
        }
        return trimmed;
    }

    /// <summary>Positive, at most two decimal places, below the cap.</summary>
    public static void Money(Errors errors, string field, decimal? value)
    {
        if (value is null)
        {
            errors.Add(field, "Required.");
        }
        else if (value <= 0m || value > MaxCredits)
        {
            errors.Add(field, $"Must be greater than 0 and at most {MaxCredits}.");
        }
        else if (decimal.Round(value.Value, 2) != value.Value)
        {
            errors.Add(field, "At most two decimal places.");
        }
    }

    public static void LoanDays(Errors errors, string field, int? value, bool required)
    {
        if (value is null)
        {
            if (required)
            {
                errors.Add(field, "Required for physical assets.");
            }
        }
        else if (value < 1 || value > MaxLoanDays)
        {
            errors.Add(field, $"Must be between 1 and {MaxLoanDays} days.");
        }
    }

    /// <summary>Checks an asset body; returns the cleaned text fields and keys.</summary>
    public static (string Name, string Category, string Department, List<string> Keys) Asset(Errors errors, AssetRequest body)
    {
        var name = Text(errors, "name", body.Name, 200) ?? "";
        var category = Text(errors, "category", body.Category, 100) ?? "";
        var department = Text(errors, "departmentProgram", body.DepartmentProgram, 200) ?? "";
        var keys = new List<string>();

        switch (body.Type)
        {
            case null:
                errors.Add("type", "Required: physical, license or voucher.");
                break;

            case AssetType.Physical:
                if (body.Units is null || body.Units < 1 || body.Units > MaxUnits)
                {
                    errors.Add("units", $"Physical assets need between 1 and {MaxUnits} units.");
                }
                LoanDays(errors, "defaultLoanDays", body.DefaultLoanDays, required: true);
                NotAllowed(errors, "licenseKeys", body.LicenseKeys is not null, "physical");
                NotAllowed(errors, "balance", body.Balance is not null, "physical");
                break;

            case AssetType.License:
                if (body.LicenseKeys is null || body.LicenseKeys.Count == 0)
                {
                    errors.Add("licenseKeys", "License assets need at least one key.");
                }
                else if (body.LicenseKeys.Count > MaxKeys)
                {
                    errors.Add("licenseKeys", $"At most {MaxKeys} keys.");
                }
                else
                {
                    keys = body.LicenseKeys.Select(k => k?.Trim() ?? "").ToList();
                    if (keys.Any(k => !LicenseKeyPattern().IsMatch(k)))
                    {
                        errors.Add("licenseKeys", "Keys are 1 to 100 letters, digits or dashes, starting with a letter or digit.");
                    }
                    if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
                    {
                        errors.Add("licenseKeys", "Keys must be unique.");
                    }
                }
                NotAllowed(errors, "units", body.Units is not null, "license");
                NotAllowed(errors, "defaultLoanDays", body.DefaultLoanDays is not null, "license");
                NotAllowed(errors, "balance", body.Balance is not null, "license");
                break;

            case AssetType.Voucher:
                Money(errors, "balance", body.Balance);
                NotAllowed(errors, "units", body.Units is not null, "voucher");
                NotAllowed(errors, "defaultLoanDays", body.DefaultLoanDays is not null, "voucher");
                NotAllowed(errors, "licenseKeys", body.LicenseKeys is not null, "voucher");
                break;
        }

        return (name, category, department, keys);
    }

    private static void NotAllowed(Errors errors, string field, bool present, string type)
    {
        if (present)
        {
            errors.Add(field, $"Does not apply to {type} assets.");
        }
    }
}
