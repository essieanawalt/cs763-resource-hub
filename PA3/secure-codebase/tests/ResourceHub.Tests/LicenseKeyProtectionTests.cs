// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: License key encryption at rest (AES-256-GCM) and decrypt-on-return rules (CWE-312), plus FieldEncryptor unit tests, including the key read from a secret file (FIELD_ENCRYPTION_KEY_FILE); CA1861 and CA1859 fixes (~100%)
// Human Modifications: None yet
// Verification: dotnet test against PostgreSQL 18 (secret-file tests failed to compile before FromConfiguration existed; pass after); dotnet build with CA1861/CA1859 as warnings
// Confidence: High

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResourceHub.Api.Domain;
using ResourceHub.Api.Security;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class LicenseKeyProtectionTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    private static readonly string[] UpdatedKeys = ["SAME-KEY-VALUE", "OTHER-KEY-1", "ADDED-KEY-2"];

    private static readonly Guid SharedLabAdvisor = Guid.Parse("48888888-0000-4000-8000-000000000001");

    [DbFact]
    public async Task Seeded_keys_are_stored_encrypted_and_never_in_plaintext()
    {
        var rows = await RawLicenseRowsAsync();

        var seeded = rows.Where(r => r.AssetId == Ids.AssetJetBrains).ToList();
        Assert.Equal(3, seeded.Count);
        Assert.All(rows, r => Assert.StartsWith("v1.", r.Ciphertext));
        Assert.All(rows, r => Assert.DoesNotContain("JBIDE", r.Ciphertext));
        Assert.Equal(seeded.Count, seeded.Select(r => r.Ciphertext).Distinct().Count());

        // No column anywhere still holds a plaintext key.
        await using var connection = await factory.OpenRawAsync();
        await using var columns = connection.CreateCommand();
        columns.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_name = 'license_keys'";
        var names = new List<string>();
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
        }
        Assert.Contains("KeyCiphertext", names);
        Assert.DoesNotContain("Key", names);

        Assert.False(await DatabaseContainsAsync("JBIDE-7K2M-Q9XA"));
    }

    [DbFact]
    public async Task Keys_from_create_and_update_are_encrypted_with_a_fresh_nonce_each()
    {
        var asset = await S.LicenseAsync("SAME-KEY-VALUE", "OTHER-KEY-1");
        var twin = await S.LicenseAsync("SAME-KEY-VALUE");
        var update = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{asset}", new
        {
            name = "Test license",
            type = "license",
            category = "Test license",
            departmentProgram = "Computer Science",
            licenseKeys = UpdatedKeys,
        }, Ids.Admin);
        await update.AssertStatusAsync(HttpStatusCode.OK);

        var rows = (await RawLicenseRowsAsync()).Where(r => r.AssetId == asset || r.AssetId == twin).ToList();
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.DoesNotContain("KEY", r.Ciphertext));
        Assert.Equal(rows.Count, rows.Select(r => r.Ciphertext).Distinct().Count());
        var nonces = rows.Select(r => Convert.ToBase64String(Convert.FromBase64String(r.Ciphertext[3..])[..12])).ToList();
        Assert.Equal(nonces.Count, nonces.Distinct().Count());

        // The stored values decrypt to the submitted keys with the configured key.
        var encryptor = FieldEncryptor.FromBase64(HubApiFactory.FieldKey);
        var plain = rows.Select(r => encryptor.Decrypt(r.Ciphertext, r.Id)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["ADDED-KEY-2", "OTHER-KEY-1", "SAME-KEY-VALUE", "SAME-KEY-VALUE"], plain);
        Assert.False(await DatabaseContainsAsync("ADDED-KEY-2"));
    }

    [DbFact]
    public async Task Admin_gets_issued_keys_decrypted_on_allocate_and_in_the_listing()
    {
        var asset = await S.LicenseAsync("ADMIN-SEES-1", "ADMIN-SEES-2");
        var result = await S.AllocatedTicketAsync(new { assetId = asset, quantity = 1 });

        var issued = Assert.Single(result.GetProperty("allocations").EnumerateArray()).GetProperty("licenseKeys")[0].GetString();
        Assert.StartsWith("ADMIN-SEES-", issued);

        var listing = await _client.GetAsync($"/api/v1/assets/allocations?courseId={Ids.CourseCs501}", Ids.Admin);
        Assert.Contains(issued!, await listing.Content.ReadAsStringAsync());
    }

    [DbFact]
    public async Task Requester_sees_their_key_but_an_associated_non_requester_does_not()
    {
        await AddLabAdvisorAsync(SharedLabAdvisor, Ids.LabEmbedded);
        var asset = await S.LicenseAsync("RAMAN-ONLY-1");
        var ticket = await S.SubmitAsync(Ids.AdvisorRaman, new
        {
            researchLabId = Ids.LabEmbedded,
            items = new[] { new { assetId = asset, quantity = 1 } },
        });
        await S.ApproveAsync(ticket.Id());
        await (await S.AllocateAsync(ticket.Id())).AssertStatusAsync(HttpStatusCode.OK);
        var url = $"/api/v1/assets/allocations?researchLabId={Ids.LabEmbedded}";

        var requester = await _client.GetAsync(url, Ids.AdvisorRaman);
        await requester.AssertStatusAsync(HttpStatusCode.OK);
        Assert.Contains("RAMAN-ONLY-1", await requester.Content.ReadAsStringAsync());

        // Same lab, so the allocation row is visible; not the requester, so the key value is not.
        var colleague = await _client.SendAsync(HttpMethod.Get, url, token: TestTokens.For(SharedLabAdvisor, [TestTokens.FacultyRole]));
        await colleague.AssertStatusAsync(HttpStatusCode.OK);
        var body = await colleague.JsonAsync();
        Assert.DoesNotContain("RAMAN-ONLY-1", body.GetRawText());
        var row = Assert.Single(body.GetProperty("allocations").EnumerateArray(), a => a.Id("assetId") == asset);
        Assert.False(row.TryGetProperty("licenseKeys", out _));
        Assert.Equal(1, body.GetProperty("summary").GetProperty("licenseKeysAssigned").GetInt32());

        // Another lab sees nothing at all.
        var outsider = await _client.GetAsync(url, Ids.AdvisorFerreira);
        Assert.DoesNotContain("RAMAN-ONLY-1", await outsider.Content.ReadAsStringAsync());
    }

    [DbFact]
    public async Task Unallocated_keys_are_not_returned_by_any_catalog_read()
    {
        var asset = await S.LicenseAsync("UNISSUED-SECRET-1");
        foreach (var user in new[] { Ids.Admin, Ids.InstructorChen, Ids.NoRoleUser })
        {
            var one = await _client.GetAsync($"/api/v1/assets/{asset}", user);
            var many = await _client.GetAsync("/api/v1/assets?type=license&pageSize=100", user);
            var search = await _client.GetAsync("/api/v1/assets?search=UNISSUED", user);
            Assert.DoesNotContain("UNISSUED-SECRET", await one.Content.ReadAsStringAsync());
            Assert.DoesNotContain("UNISSUED-SECRET", await many.Content.ReadAsStringAsync());
            Assert.Equal(0, (await search.JsonAsync()).GetProperty("total").GetInt32());
        }
    }

    [DbFact]
    public async Task Ciphertext_moved_to_another_row_does_not_decrypt()
    {
        var rows = (await RawLicenseRowsAsync()).Where(r => r.AssetId == Ids.AssetJetBrains).ToList();
        var encryptor = FieldEncryptor.FromBase64(HubApiFactory.FieldKey);

        Assert.StartsWith("JBIDE-", encryptor.Decrypt(rows[0].Ciphertext, rows[0].Id));
        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(rows[0].Ciphertext, rows[1].Id));
    }

    // ---- helpers ----

    private sealed record RawRow(Guid Id, Guid AssetId, string Ciphertext);

    private async Task<List<RawRow>> RawLicenseRowsAsync()
    {
        await using var connection = await factory.OpenRawAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\", \"AssetId\", \"KeyCiphertext\" FROM license_keys";
        var rows = new List<RawRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new RawRow(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
        }
        return rows;
    }

    /// <summary>Searches every text column of every table for <paramref name="needle"/>.</summary>
    private async Task<bool> DatabaseContainsAsync(string needle)
    {
        await using var connection = await factory.OpenRawAsync();
        var columns = new List<(string Table, string Column)>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = """
                SELECT table_name, column_name FROM information_schema.columns
                WHERE table_schema = 'public' AND data_type IN ('text', 'character varying')
                """;
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.GetString(1)));
            }
        }
        foreach (var (table, column) in columns)
        {
            await using var probe = connection.CreateCommand();
            // Names come from information_schema, not from input.
            probe.CommandText = $"SELECT EXISTS (SELECT 1 FROM \"{table}\" WHERE \"{column}\" LIKE '%' || @needle || '%')";
            probe.Parameters.AddWithValue("needle", needle);
            if ((bool)(await probe.ExecuteScalarAsync())!)
            {
                return true;
            }
        }
        return false;
    }

    private Task<int> AddLabAdvisorAsync(Guid id, Guid lab) => factory.WithDbAsync(async db =>
    {
        if (!await db.Users.AnyAsync(u => u.Id == id))
        {
            db.Users.Add(new User { Id = id, Name = "Test co-advisor", Role = UserRole.LabAdvisor, ResearchLabId = lab });
            await db.SaveChangesAsync();
        }
        return 0;
    });
}

public class FieldEncryptorTests
{
    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Round_trips_and_uses_a_fresh_nonce_each_time()
    {
        var encryptor = FieldEncryptor.FromBase64(Key);
        var row = Guid.NewGuid();

        var a = encryptor.Encrypt("JBIDE-7K2M-Q9XA", row);
        var b = encryptor.Encrypt("JBIDE-7K2M-Q9XA", row);

        Assert.NotEqual(a, b);
        Assert.StartsWith("v1.", a);
        Assert.Equal("JBIDE-7K2M-Q9XA", encryptor.Decrypt(a, row));
        Assert.Equal("JBIDE-7K2M-Q9XA", encryptor.Decrypt(b, row));
        // nonce (12) + ciphertext (15) + tag (16)
        Assert.Equal(12 + 15 + 16, Convert.FromBase64String(a[3..]).Length);
    }

    [Fact]
    public void Tampered_ciphertext_or_wrong_key_fails_authentication()
    {
        var encryptor = FieldEncryptor.FromBase64(Key);
        var row = Guid.NewGuid();
        var stored = encryptor.Encrypt("SECRET-1", row);

        var bytes = Convert.FromBase64String(stored[3..]);
        bytes[14] ^= 0x01;
        var tampered = "v1." + Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(tampered, row));
        var other = FieldEncryptor.FromBase64(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Assert.ThrowsAny<CryptographicException>(() => other.Decrypt(stored, row));
        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt("SECRET-1", row));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("change-me")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")] // 16 bytes: AES-128, not allowed
    public void Rejects_a_missing_or_wrong_size_key(string? key)
    {
        Assert.Throws<InvalidOperationException>(() => FieldEncryptor.FromBase64(key));
    }

    [Fact]
    public void All_zero_key_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => FieldEncryptor.FromBase64(Convert.ToBase64String(new byte[32])));
    }

    [Fact]
    public void Service_will_not_start_without_a_field_key()
    {
        using var factory = new NoFieldKeyFactory();
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("FIELD_ENCRYPTION_KEY", ex.ToString());
    }

    private class NoFieldKeyFactory : HubApiFactory
    {
        private const string Nowhere = "Host=127.0.0.1;Port=1;Database=nowhere;Username=nobody;Password=none;Timeout=2";

        protected override string ConnectionString => Nowhere;

        protected override string? OwnerConnectionString => null;

        protected override bool CreatesDatabase => false;

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("FIELD_ENCRYPTION_KEY", "");
        }
    }

    // ---- H3: the key comes from a secret file named by FIELD_ENCRYPTION_KEY_FILE ----

    private static IConfiguration KeyConfig(string? value, string? file) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [FieldEncryptor.ConfigKey] = value,
            [FieldEncryptor.FileConfigKey] = file,
        }).Build();

    private static string SecretFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rh-test-field-key-{Guid.NewGuid():N}");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Key_is_read_from_the_secret_file()
    {
        // Docker secret files usually end with a newline.
        var path = SecretFile(Key + "\n");
        try
        {
            var fromFile = FieldEncryptor.FromConfiguration(KeyConfig(null, path));
            var row = Guid.NewGuid();
            Assert.Equal("JBIDE-1", FieldEncryptor.FromBase64(Key).Decrypt(fromFile.Encrypt("JBIDE-1", row), row));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Plain_key_value_still_works_for_tests_and_local_runs()
    {
        var row = Guid.NewGuid();
        var encryptor = FieldEncryptor.FromConfiguration(KeyConfig(Key, null));
        Assert.Equal("K", FieldEncryptor.FromBase64(Key).Decrypt(encryptor.Encrypt("K", row), row));
    }

    [Fact]
    public void Missing_or_unreadable_secret_file_is_refused()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"rh-test-no-such-key-{Guid.NewGuid():N}");
        var ex = Assert.Throws<InvalidOperationException>(() => FieldEncryptor.FromConfiguration(KeyConfig(null, missing)));
        Assert.Contains(FieldEncryptor.FileConfigKey, ex.Message);

        var directory = Directory.CreateTempSubdirectory("rh-test-key-dir-").FullName;
        try
        {
            Assert.Throws<InvalidOperationException>(() => FieldEncryptor.FromConfiguration(KeyConfig(null, directory)));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Placeholder_or_short_key_in_the_secret_file_is_refused_without_echoing_it()
    {
        foreach (var content in new[] { "<generate: openssl rand -base64 32>", "AAAAAAAAAAAAAAAAAAAAAA==", "", Convert.ToBase64String(new byte[32]) })
        {
            var path = SecretFile(content);
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => FieldEncryptor.FromConfiguration(KeyConfig(null, path)));
                Assert.Contains(FieldEncryptor.FileConfigKey, ex.Message);
                if (content.Length > 0)
                {
                    Assert.DoesNotContain(content, ex.Message);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Setting_both_the_value_and_the_file_is_refused()
    {
        var path = SecretFile(Key);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => FieldEncryptor.FromConfiguration(KeyConfig(Key, path)));
            Assert.Contains(FieldEncryptor.FileConfigKey, ex.Message);
            Assert.DoesNotContain(Key, ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Oversized_secret_file_is_refused()
    {
        var path = SecretFile(Key + new string(' ', 8192));
        try
        {
            Assert.Throws<InvalidOperationException>(() => FieldEncryptor.FromConfiguration(KeyConfig(null, path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Service_reads_its_key_from_the_secret_file()
    {
        var path = SecretFile(HubApiFactory.FieldKey + "\n");
        try
        {
            using var factory = new SecretFileKeyFactory(path);
            var encryptor = factory.Services.GetRequiredService<FieldEncryptor>();
            var row = Guid.NewGuid();
            Assert.Equal("FROM-FILE", FieldEncryptor.FromBase64(HubApiFactory.FieldKey).Decrypt(encryptor.Encrypt("FROM-FILE", row), row));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class SecretFileKeyFactory(string keyFile) : NoFieldKeyFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);   // FIELD_ENCRYPTION_KEY is empty here
            builder.UseSetting(FieldEncryptor.FileConfigKey, keyFile);
        }
    }

    [Fact]
    public void Ciphertext_does_not_contain_plaintext_bytes()
    {
        var encryptor = FieldEncryptor.FromBase64(Key);
        var stored = Convert.FromBase64String(encryptor.Encrypt("AAAAAAAAAAAAAAAA", Guid.NewGuid())[3..]);
        Assert.DoesNotContain("AAAAAAAAAAAAAAAA", Encoding.ASCII.GetString(stored));
    }
}
