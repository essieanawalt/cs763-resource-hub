// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Audit trail (T2) and kept ticket history (CWE-221) tests, plus device-token logging (CWE-532) tests (~100%)
// Human Modifications: None yet
// Verification: dotnet test against PostgreSQL 18
// Confidence: High

using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ResourceHub.Api.Domain;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class AuditTrailTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    private Task<List<AuditEvent>> AuditAsync(Guid entityId) =>
        factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().Where(a => a.EntityId == entityId).OrderBy(a => a.Seq).ToListAsync());

    // ---- CWE-221: deleting a ticket keeps its history ----

    [DbFact]
    public async Task Deleting_a_ticket_keeps_its_event_history_and_records_the_delete()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });
        await (await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/tickets/{ticket.Id()}", new
        {
            courseId = Ids.CourseCs501,
            items = new[] { new { assetId = Ids.AssetLaptop, quantity = 2 } },
        }, Ids.InstructorChen)).AssertStatusAsync(HttpStatusCode.OK);

        await (await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/tickets/{ticket.Id()}", user: Ids.InstructorChen))
            .AssertStatusAsync(HttpStatusCode.NoContent);

        // Gone from the API...
        await (await _client.GetAsync($"/api/v1/assets/tickets/{ticket.Id()}", Ids.Admin)).AssertStatusAsync(HttpStatusCode.NotFound);
        var list = await (await _client.GetAsync("/api/v1/assets/tickets?pageSize=100", Ids.Admin)).JsonAsync();
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), t => t.Id() == ticket.Id());

        // ...but the history is still in the database, with who deleted it.
        var events = await factory.WithDbAsync(db => db.TicketEvents.AsNoTracking().Where(e => e.TicketId == ticket.Id()).ToListAsync());
        events = events.OrderBy(e => e.OccurredAt).ThenBy(e => (int)e.Type).ToList();   // the type is stored as text, so order in memory
        Assert.Equal([TicketEventType.Submitted, TicketEventType.Updated, TicketEventType.Deleted], events.Select(e => e.Type).ToArray());
        Assert.All(events, e => Assert.Equal(Ids.InstructorChen, e.ActorId));
        var stored = await factory.WithDbAsync(db => db.Tickets.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == ticket.Id()));
        Assert.NotNull(stored.DeletedAt);

        var audit = await AuditAsync(ticket.Id());
        var delete = Assert.Single(audit, a => a.Action == "ticket.delete");
        Assert.Equal(Ids.InstructorChen.ToString(), delete.ActorSub);
        Assert.Equal("succeeded", delete.Outcome);
    }

    [DbFact]
    public async Task Event_and_audit_rows_cannot_be_updated_or_deleted_in_the_database()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });
        await using var connection = await factory.OpenRawAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM ticket_events WHERE \"TicketId\" = @id",
            "UPDATE ticket_events SET \"ActorId\" = NULL WHERE \"TicketId\" = @id",
            "DELETE FROM audit_events WHERE \"EntityId\" = @id",
            "UPDATE audit_events SET \"ActorSub\" = 'someone-else' WHERE \"EntityId\" = @id",
            "DELETE FROM tickets WHERE \"Id\" = @id",
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("id", ticket.Id());
            await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }
    }

    // ---- T2: every change records sub, time and outcome ----

    [DbFact]
    public async Task Ticket_lifecycle_is_audited_with_the_callers_sub()
    {
        var asset = await S.PhysicalAsync(units: 2);
        var ticket = await S.SubmitForCourseAsync(new { assetId = asset, quantity = 1 });
        await S.ApproveAsync(ticket.Id());
        await (await S.AllocateAsync(ticket.Id())).AssertStatusAsync(HttpStatusCode.OK);
        await (await S.SetStatusAsync(ticket.Id(), "closed", Ids.Admin)).AssertStatusAsync(HttpStatusCode.OK);

        var audit = await AuditAsync(ticket.Id());
        Assert.Equal(
            ["ticket.submit", "ticket.approve", "ticket.allocate", "ticket.close"],
            audit.Select(a => a.Action).ToArray());
        Assert.Equal(
            [Ids.InstructorChen.ToString(), Ids.ItDirector.ToString(), Ids.Admin.ToString(), Ids.Admin.ToString()],
            audit.Select(a => a.ActorSub).ToArray());
        Assert.All(audit, a => Assert.Equal("succeeded", a.Outcome));
        Assert.All(audit, a => Assert.Equal(factory.Clock.GetUtcNow(), a.OccurredAt));

        // The allocation also changed stock: the asset has its own audit row.
        var stock = await AuditAsync(asset);
        Assert.Contains(stock, a => a.Action == "asset.stock" && a.ActorSub == Ids.Admin.ToString() && a.Outcome == "succeeded");
    }

    [DbFact]
    public async Task Asset_changes_and_device_returns_are_audited()
    {
        var asset = await S.PhysicalAsync(units: 2);
        await (await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{asset}", new
        {
            name = "Renamed",
            type = "physical",
            category = "Test device",
            departmentProgram = "Computer Science",
            units = 3,
            defaultLoanDays = 14,
        }, Ids.Admin)).AssertStatusAsync(HttpStatusCode.OK);
        var result = await S.AllocatedTicketAsync(new { assetId = asset, quantity = 1 });
        var device = ReturnTests.Device(result, asset, 0);
        await (await _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{asset}/return",
            new { token = device.Token, checkInCode = device.CheckInCode }, Ids.Admin)).AssertStatusAsync(HttpStatusCode.OK);

        var spare = await S.VoucherAsync(5m);
        await (await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/{spare}", user: Ids.Admin)).AssertStatusAsync(HttpStatusCode.NoContent);

        var audit = await AuditAsync(asset);
        Assert.Equal(["asset.create", "asset.update", "asset.stock", "device.return"], audit.Select(a => a.Action).ToArray());
        Assert.All(audit, a => Assert.Equal(Ids.Admin.ToString(), a.ActorSub));
        var ret = audit.Single(a => a.Action == "device.return");
        Assert.DoesNotContain(device.Token, ret.Detail ?? "");
        Assert.DoesNotContain(device.CheckInCode, ret.Detail ?? "");

        var deleted = await AuditAsync(spare);
        Assert.Equal(["asset.create", "asset.delete"], deleted.Select(a => a.Action).ToArray());
    }

    [DbFact]
    public async Task Refused_and_failed_changes_are_audited_with_their_outcome()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });
        var asset = await S.PhysicalAsync(units: 1);
        var result = await S.AllocatedTicketAsync(new { assetId = asset, quantity = 1 });
        var device = ReturnTests.Device(result, asset, 0);

        await (await S.SetStatusAsync(ticket.Id(), "approved", Ids.InstructorChen)).AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/tickets/{ticket.Id()}", user: Ids.InstructorOkafor))
            .AssertStatusAsync(HttpStatusCode.NotFound);
        await (await S.AllocateAsync(ticket.Id())).AssertStatusAsync(HttpStatusCode.Conflict);
        await (await _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{asset}/return",
            new { token = device.Token, checkInCode = "CI-WRONG" }, Ids.Admin)).AssertStatusAsync(HttpStatusCode.UnprocessableEntity);

        var ticketAudit = await AuditAsync(ticket.Id());
        Assert.Contains(ticketAudit, a => a.Action == "ticket.update" && a.Outcome == "denied" && a.ActorSub == Ids.InstructorChen.ToString());
        Assert.Contains(ticketAudit, a => a.Action == "ticket.delete" && a.Outcome == "not_found" && a.ActorSub == Ids.InstructorOkafor.ToString());
        Assert.Contains(ticketAudit, a => a.Action == "ticket.allocate" && a.Outcome == "conflict" && a.ActorSub == Ids.Admin.ToString());

        var assetAudit = await AuditAsync(asset);
        var refused = Assert.Single(assetAudit, a => a.Action == "device.return");
        Assert.Equal("invalid", refused.Outcome);
        Assert.DoesNotContain("CI-WRONG", refused.Detail ?? "");
    }

    // ---- CWE-532: no device tokens, check-in codes or license keys in the log ----

    [DbFact]
    public async Task Device_token_and_secrets_never_reach_the_application_log()
    {
        var license = await S.LicenseAsync("LOGGED-KEY-NEVER");
        var asset = await S.PhysicalAsync(units: 1);
        var result = await S.AllocatedTicketAsync(new { assetId = asset, quantity = 1 }, new { assetId = license, quantity = 1 });
        var device = ReturnTests.Device(result, asset, 0);
        await (await _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{asset}/return",
            new { token = device.Token, checkInCode = device.CheckInCode }, Ids.Admin)).AssertStatusAsync(HttpStatusCode.OK);

        var lines = factory.Logs.Lines;
        Assert.Contains(lines, l => l.Contains("returned by", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(device.Token, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains(device.Token[3..], StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, l => l.Contains(device.CheckInCode, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("LOGGED-KEY-NEVER", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("eyJ", StringComparison.Ordinal)); // no bearer tokens either
    }
}
