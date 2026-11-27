// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Device check-in codes for returns (T1): 80-bit random code given once at hand-out, stored only as a SHA-256 hash, compared in constant time (~100%)
// Human Modifications: None yet
// Verification: ReturnTests (T1 cases), AuditTrailTests
// Confidence: High

using System.Security.Cryptography;
using System.Text;

namespace ResourceHub.Api.Security;

/// <summary>
/// The device token identifies a loan and is visible to the borrower in their allocation view, so
/// it cannot prove the device came back. The check-in code goes on the device's label when it is
/// handed out (it is in the admin-only allocate response and nowhere else), so presenting it at
/// return shows the device itself is at the desk.
/// </summary>
public static class CheckInCodes
{
    public static string New() => "CI-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10));

    public static string Hash(string code) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    public static bool Matches(string presented, string? storedHash)
    {
        if (storedHash is null || storedHash.Length != 64)
        {
            return false;
        }
        var expected = Convert.FromHexString(storedHash);
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
