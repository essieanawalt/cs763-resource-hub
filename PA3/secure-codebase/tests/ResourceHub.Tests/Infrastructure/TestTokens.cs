// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Throwaway RSA key pair, test JWKS file and a hand-rolled JWT builder for valid and hostile tokens (~100%)
// Human Modifications: None yet
// Verification: Used by every API test; AuthTokenTests covers each token case
// Confidence: High

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ResourceHub.Tests.Infrastructure;

/// <summary>
/// Signs test tokens with a key pair created in memory for this test run. The public half is
/// written to a JWKS file that the API under test reads through JWT_JWKS_PATH. No real key
/// files are used or looked for.
/// </summary>
public static class TestTokens
{
    public const string Issuer = "http://localhost:9000";
    public const string Audience = "cs763-api";
    public const string Kid = "test-signing-key-1";
    public const string SecondKid = "test-signing-key-2";
    public const string AdminRole = "admin";
    public const string FacultyRole = "faculty_instructor";

    /// <summary>The key the JWKS publishes as <see cref="Kid"/>.</summary>
    public static RSA SigningKey { get; } = NewKey();

    /// <summary>Also in the JWKS, as <see cref="SecondKid"/>.</summary>
    public static RSA SecondKey { get; } = NewKey();

    /// <summary>An attacker's key: never in the JWKS.</summary>
    public static RSA AttackerKey { get; } = NewKey();

    /// <summary>Serialises use of the RSA objects: test classes run in parallel.</summary>
    private static readonly object KeyLock = new();

    /// <summary>
    /// RSA.Create generates the key lazily on first use; if two test classes trigger that at once,
    /// the JWKS and the signatures can end up from different keys. Generate it here, in the
    /// (thread-safe) static initialiser, by exporting the private parameters once.
    /// </summary>
    private static RSA NewKey()
    {
        var key = RSA.Create(2048);
        _ = key.ExportParameters(includePrivateParameters: true);
        return key;
    }

    private static readonly Lazy<string> Jwks = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"rh-test-jwks-{Guid.NewGuid():N}.json");
        WriteJwks(path);
        return path;
    });

    /// <summary>Path of the JWKS file holding the public keys (written once per test run).</summary>
    public static string JwksPath => Jwks.Value;

    public static void WriteJwks(string path)
    {
        var keys = new[] { Jwk(SigningKey, Kid), Jwk(SecondKey, SecondKid) };
        File.WriteAllText(path, JsonSerializer.Serialize(new { keys }));
    }

    /// <summary>Roles the seeded users hold in their tokens (IT director and administrator are both admin).</summary>
    public static string[] RolesFor(Guid user) =>
        user == Ids.Admin || user == Ids.ItDirector ? [AdminRole]
        : user == Ids.InstructorChen || user == Ids.InstructorOkafor || user == Ids.AdvisorFerreira || user == Ids.AdvisorRaman ? [FacultyRole]
        : [];

    public static string For(Guid user) => For(user, RolesFor(user));

    public static string For(Guid user, string[] roles) => Sign(Claims(user.ToString(), roles));

    /// <summary>Standard valid claims; tests change entries to build broken tokens.</summary>
    public static Dictionary<string, object?> Claims(string sub, string[] roles, TimeSpan? lifetime = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Dictionary<string, object?>
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = sub,
            ["roles"] = roles,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.AddSeconds(-5).ToUnixTimeSeconds(),
            ["exp"] = now.Add(lifetime ?? TimeSpan.FromMinutes(10)).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
    }

    public static Dictionary<string, object?> Header(string alg = "RS256", string? kid = Kid)
    {
        var header = new Dictionary<string, object?> { ["alg"] = alg, ["typ"] = "JWT" };
        if (kid is not null)
        {
            header["kid"] = kid;
        }
        return header;
    }

    /// <summary>RS256 with <see cref="SigningKey"/> unless told otherwise.</summary>
    public static string Sign(
        Dictionary<string, object?> payload, Dictionary<string, object?>? header = null, RSA? key = null,
        HashAlgorithmName? hash = null, RSASignaturePadding? padding = null)
    {
        header ??= Header();
        var signingInput = Encode(header) + "." + Encode(payload);
        byte[] signature;
        lock (KeyLock)
        {
            signature = (key ?? SigningKey).SignData(
                Encoding.ASCII.GetBytes(signingInput), hash ?? HashAlgorithmName.SHA256, padding ?? RSASignaturePadding.Pkcs1);
        }
        return signingInput + "." + B64Url(signature);
    }

    /// <summary>HMAC-SHA256 with any secret (the "public key as HMAC secret" confusion attack).</summary>
    public static string SignHs256(Dictionary<string, object?> payload, byte[] secret, string? kid = Kid)
    {
        var signingInput = Encode(Header("HS256", kid)) + "." + Encode(payload);
        var signature = HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(signingInput));
        return signingInput + "." + B64Url(signature);
    }

    /// <summary>alg "none" with an empty signature part.</summary>
    public static string Unsigned(Dictionary<string, object?> payload, string alg = "none") =>
        Encode(Header(alg, Kid)) + "." + Encode(payload) + ".";

    public static string Encode(object value) => B64Url(JsonSerializer.SerializeToUtf8Bytes(value));

    public static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Public JWK of a key, as published in the JWKS.</summary>
    public static Dictionary<string, object?> Jwk(RSA key, string kid)
    {
        RSAParameters p;
        lock (KeyLock)
        {
            p = key.ExportParameters(false);
        }
        return new Dictionary<string, object?>
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["alg"] = "RS256",
            ["kid"] = kid,
            ["n"] = B64Url(p.Modulus!),
            ["e"] = B64Url(p.Exponent!),
        };
    }
}
