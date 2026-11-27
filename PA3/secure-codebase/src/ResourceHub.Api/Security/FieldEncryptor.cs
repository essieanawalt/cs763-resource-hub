// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: AES-256-GCM field encryption for license key values with a random nonce per value and the row id as associated data; the key comes from a secret file (FIELD_ENCRYPTION_KEY_FILE) or, for tests and local runs, FIELD_ENCRYPTION_KEY (CWE-526) (~100%)
// Human Modifications: None yet
// Verification: FieldEncryptorTests (including the secret-file cases), LicenseKeyProtectionTests, ComposeStackTests.Field_key_is_read_from_the_secret_file
// Confidence: High

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace ResourceHub.Api.Security;

/// <summary>
/// Encrypts one sensitive field value (a software license key) before it is stored.
/// Stored form: <c>v1.</c> + base64(nonce[12] || ciphertext || tag[16]). The row id is bound
/// in as associated data, so a ciphertext copied onto another row does not decrypt.
/// </summary>
public sealed class FieldEncryptor
{
    public const string ConfigKey = "FIELD_ENCRYPTION_KEY";

    /// <summary>Path of a file holding the base64 key (a Docker secret, e.g. /run/secrets/field_encryption_key).</summary>
    public const string FileConfigKey = "FIELD_ENCRYPTION_KEY_FILE";

    /// <summary>A base64 32-byte key is 44 characters; anything far larger is not a key file.</summary>
    private const int MaxKeyFileBytes = 1024;
    private const string Prefix = "v1.";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    private FieldEncryptor(byte[] key) => _key = key;

    /// <summary>
    /// Reads the key from the file named by FIELD_ENCRYPTION_KEY_FILE or, if that is not set, from
    /// FIELD_ENCRYPTION_KEY. Setting both is refused, so it is never unclear which key is in use.
    /// Error messages name the setting and never contain the key or the file's content.
    /// </summary>
    public static FieldEncryptor FromConfiguration(IConfiguration configuration)
    {
        var value = configuration[ConfigKey];
        var file = configuration[FileConfigKey];
        var hasValue = !string.IsNullOrWhiteSpace(value);
        var hasFile = !string.IsNullOrWhiteSpace(file);

        if (hasValue && hasFile)
        {
            throw new InvalidOperationException($"Both {ConfigKey} and {FileConfigKey} are set; set only one ({FileConfigKey} in containers).");
        }
        return hasFile ? FromBase64(ReadKeyFile(file!), FileConfigKey) : FromBase64(value, ConfigKey);
    }

    /// <summary>Takes the base64 value of FIELD_ENCRYPTION_KEY; anything but 32 random-looking bytes is refused.</summary>
    public static FieldEncryptor FromBase64(string? base64) => FromBase64(base64, ConfigKey);

    private static FieldEncryptor FromBase64(string? base64, string source)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new InvalidOperationException(source == ConfigKey
                ? $"Neither {FileConfigKey} nor {ConfigKey} is set. Give a base64-encoded 32-byte key (for example: openssl rand -base64 32)."
                : $"The file named by {source} is empty.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{source}: the key is not valid base64.");
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException($"{source}: the key must decode to exactly 32 bytes (AES-256); it decodes to {key.Length}.");
        }
        if (key.All(b => b == key[0]))
        {
            throw new InvalidOperationException($"{source}: the key looks like a placeholder (every byte is the same).");
        }

        return new FieldEncryptor(key);
    }

    private static string ReadKeyFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new InvalidOperationException($"{FileConfigKey} names a file that does not exist: {path}");
            }
            if (info.Length > MaxKeyFileBytes)
            {
                throw new InvalidOperationException($"{FileConfigKey} names a file larger than {MaxKeyFileBytes} bytes; it should hold only the base64 key.");
            }
            return File.ReadAllText(path, Encoding.ASCII);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException($"{FileConfigKey} could not be read ({ex.GetType().Name}): {path}");
        }
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
