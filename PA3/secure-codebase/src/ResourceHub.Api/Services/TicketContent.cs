// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Validates ticket content (spec 6.7) and the requester's right to ask for that course or lab; the course/lab check now uses the token caller's seeded association (~100%)
// Human Modifications: None yet
// Verification: TicketSubmitTests, TicketAuthorizationTests, WriteValidationTests
// Confidence: High

using Microsoft.EntityFrameworkCore;
using ResourceHub.Api.Contracts;
using ResourceHub.Api.Data;
using ResourceHub.Api.Domain;

namespace ResourceHub.Api.Services;

public sealed record ValidTicketContent(
    Guid? CourseId, Guid? ResearchLabId, Guid? ProjectId, string? Notes, List<TicketItem> Items);

public static class TicketContent
{
    public const int MaxItems = 20;
    public const int MaxQuantity = 1_000;

    /// <summary>Returns the cleaned content, or a 400/403 result.</summary>
    public static async Task<(ValidTicketContent? Content, IResult? Error)> ValidateAsync(
        HubDbContext db, Caller requester, TicketRequest body, CancellationToken ct)
    {
        var errors = new Errors();

        if (body.Status is not null || body.Reason is not null)
        {
            errors.Add("status", "Status changes are a separate PUT on the ticket and cannot be combined with content.");
        }
        if ((body.CourseId is null) == (body.ResearchLabId is null))
        {
            errors.Add("courseId", "Give exactly one of courseId or researchLabId.");
        }
        var notes = Validation.Text(errors, "notes", body.Notes, 2000, required: false);

        var items = new List<TicketItem>();
        if (body.Items is null || body.Items.Count == 0)
        {
            errors.Add("items", "Request at least one asset.");
        }
        else if (body.Items.Count > MaxItems)
        {
            errors.Add("items", $"At most {MaxItems} assets per ticket.");
        }
        else
        {
            var ids = body.Items.Where(i => i.AssetId is not null).Select(i => i.AssetId!.Value).ToList();
            if (ids.Count != ids.Distinct().Count())
            {
                errors.Add("items", "Each asset can appear only once; combine the quantities.");
            }
            var assets = await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);

            for (var n = 0; n < body.Items.Count; n++)
            {
                var item = body.Items[n];
                var field = $"items[{n}]";
                if (item.AssetId is null || !assets.TryGetValue(item.AssetId.Value, out var asset))
                {
                    errors.Add($"{field}.assetId", "Unknown asset.");
                    continue;
                }

                if (asset.Type == AssetType.Voucher)
                {
                    Validation.Money(errors, $"{field}.amount", item.Amount);
                    if (item.Quantity is not null)
                    {
                        errors.Add($"{field}.quantity", "Voucher assets take an amount, not a quantity.");
                    }
                }
                else
                {
                    if (item.Quantity is null || item.Quantity < 1 || item.Quantity > MaxQuantity)
                    {
                        errors.Add($"{field}.quantity", $"Must be between 1 and {MaxQuantity}.");
                    }
                    if (item.Amount is not null)
                    {
                        errors.Add($"{field}.amount", "Physical and license assets take a quantity, not an amount.");
                    }
                }

                if (asset.Type == AssetType.Physical)
                {
                    Validation.LoanDays(errors, $"{field}.loanDays", item.LoanDays, required: false);
                }
                else if (item.LoanDays is not null)
                {
                    errors.Add($"{field}.loanDays", "Only physical assets have a loan period.");
                }

                items.Add(new TicketItem
                {
                    Id = Guid.NewGuid(),
                    AssetId = asset.Id,
                    Quantity = item.Quantity,
                    Amount = item.Amount,
                    LoanDays = item.LoanDays,
                });
            }
        }

        if (body.ProjectId is not null)
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == body.ProjectId, ct);
            if (project is null)
            {
                errors.Add("projectId", "Unknown project.");
            }
            else if (project.CourseId != body.CourseId || project.ResearchLabId != body.ResearchLabId)
            {
                errors.Add("projectId", "The project belongs to a different course or research lab.");
            }
        }

        if (errors.Any)
        {
            return (null, errors.ToResult());
        }

        // Faculty ask only for a course or lab they are associated with in the seeded data.
        if (!requester.IsAssociatedWith(body.CourseId, body.ResearchLabId))
        {
            return (null, Problems.Forbidden("Faculty may request only for a course or research lab they are associated with."));
        }

        return (new ValidTicketContent(body.CourseId, body.ResearchLabId, body.ProjectId, notes, items), null);
    }
}
