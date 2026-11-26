// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: AES-256-GCM field encryption for license key values with a random nonce per value and the row id as associated data (~100%)
// Human Modifications: None yet
// Verification: FieldEncryptorTests and LicenseKeyProtectionTests (dotnet test against PostgreSQL 18)
// Confidence: High

using System.Security.Cryptography;
using System.Text;

namespace ResourceHub.Api.Security;

/// <summary>
/// Encrypts one sensitive field value (a software license key) before it is stored.
/// Stored form: <c>v1.</c> + base64(nonce[12] || ciphertext || tag[16]). The row id is bound
/// in as associated data, so a ciphertext copied onto another row does not decrypt.
/// </summary>
public sealed class FieldEncryptor
{
    public const string ConfigKey = "FIELD_ENCRYPTION_KEY";
    private const string Prefix = "v1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    private FieldEncryptor(byte[] key) => _key = key;

    /// <summary>Takes the base64 value of FIELD_ENCRYPTION_KEY; anything but 32 random-looking bytes is refused.</summary>
    public static FieldEncryptor FromBase64(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new InvalidOperationException($"{ConfigKey} is not set. Give a base64-encoded 32-byte key (for example: openssl rand -base64 32).");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{ConfigKey} is not valid base64.");
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException($"{ConfigKey} must decode to exactly 32 bytes (AES-256); it decodes to {key.Length}.");
        }
        if (key.All(b => b == key[0]))
        {
            throw new InvalidOperationException($"{ConfigKey} looks like a placeholder (every byte is the same).");
        }

        return new FieldEncryptor(key);
    }

    public string Encrypt(string plaintext, Guid rowId)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + plain.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize, plain.Length), output.AsSpan(NonceSize + plain.Length, TagSize), Aad(rowId));
        return Prefix + Convert.ToBase64String(output);
    }

    /// <summary>Throws <see cref="CryptographicException"/> if the value was altered, moved to another row or made with another key.</summary>
    public string Decrypt(string stored, Guid rowId)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Value is not in the expected encrypted format.");
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(stored[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Encrypted value is not valid base64.", ex);
        }
        if (data.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Encrypted value is too short.");
        }

        var length = data.Length - NonceSize - TagSize;
        var plain = new byte[length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize, length), data.AsSpan(NonceSize + length, TagSize), plain, Aad(rowId));
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] Aad(Guid rowId) => Encoding.ASCII.GetBytes("license_keys:" + rowId.ToString("D"));
}
