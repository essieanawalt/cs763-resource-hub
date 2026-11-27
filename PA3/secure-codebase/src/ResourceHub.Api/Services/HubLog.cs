// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: Source-generated log messages for the endpoints and the allocation engine (CA1873): arguments are passed as plain values and formatted only when the level is on; messages carry ids only, never tokens, codes or keys (CWE-532) (~100%)
// Human Modifications: None yet
// Verification: dotnet build with CA1873 as a warning (0 warnings); AuditTrailTests, ReturnTests and LicenseKeyProtectionTests read the captured logs
// Confidence: High

namespace ResourceHub.Api.Services;

/// <summary>Every application log line. The caller's sub is logged as the token's GUID.</summary>
internal static partial class HubLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Asset {AssetId} ({Type}) created by {Sub}")]
    public static partial void AssetCreated(this ILogger log, Guid assetId, Domain.AssetType type, Guid sub);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Asset {AssetId} updated by {Sub}")]
    public static partial void AssetUpdated(this ILogger log, Guid assetId, Guid sub);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Asset {AssetId} deleted by {Sub}")]
    public static partial void AssetDeleted(this ILogger log, Guid assetId, Guid sub);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Device {DeviceId} of asset {AssetId} returned by {Sub} (overdue: {Overdue})")]
    public static partial void DeviceReturned(this ILogger log, Guid deviceId, Guid assetId, Guid sub, bool overdue);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning, Message = "Return of device {DeviceId} of asset {AssetId} refused: check-in code did not match (by {Sub})")]
    public static partial void DeviceReturnRefused(this ILogger log, Guid deviceId, Guid assetId, Guid sub);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Ticket {TicketId} submitted by {Sub} with {ItemCount} item(s)")]
    public static partial void TicketSubmitted(this ILogger log, Guid ticketId, Guid sub, int itemCount);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "Ticket {TicketId} content updated by {Sub}")]
    public static partial void TicketContentUpdated(this ILogger log, Guid ticketId, Guid sub);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Information, Message = "Ticket {TicketId} moved from {From} to {To} by {Sub}")]
    public static partial void TicketMoved(this ILogger log, Guid ticketId, Domain.TicketStatus from, Domain.TicketStatus to, Guid sub);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Information, Message = "Pending ticket {TicketId} deleted by {Sub}")]
    public static partial void PendingTicketDeleted(this ILogger log, Guid ticketId, Guid sub);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "Ticket {TicketId} allocated by {Sub}: {AssetCount} asset(s), {DeviceCount} device(s), {KeyCount} key(s), {Credits} credits")]
    public static partial void TicketAllocated(this ILogger log, Guid ticketId, Guid sub, int assetCount, int deviceCount, int keyCount, decimal credits);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Warning, Message = "Allocation of ticket {TicketId} failed for lack of stock on {ShortCount} asset(s)")]
    public static partial void AllocationShort(this ILogger log, Guid ticketId, int shortCount);
}
