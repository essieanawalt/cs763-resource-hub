// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: JWT validation tests: every required good and bad token case, plus identity never taken from headers or body (CWE-290) (~100%)
// Human Modifications: None yet
// Verification: dotnet test against PostgreSQL 18
// Confidence: High

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

/// <summary>One fixture: a ticket owned by Dr. Chen that the token cases try to read.</summary>
public sealed class TokenFixture : HubApiFactory
{
    public Guid ChenTicket { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        if (TestDb.BaseConnectionString is null)
        {
            return;
        }
        var ticket = await new Scenario(CreateClient()).SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });
        ChenTicket = ticket.Id();
    }
}

public class AuthTokenTests(TokenFixture fx) : IClassFixture<TokenFixture>
{
    private readonly HttpClient _client = fx.CreateClient();
    private const string Catalog = "/api/v1/assets";

    private string ChenUrl => $"/api/v1/assets/tickets/{fx.ChenTicket}";
    private static Dictionary<string, object?> ChenClaims(TimeSpan? lifetime = null) =>
        TestTokens.Claims(Ids.InstructorChen.ToString(), [TestTokens.FacultyRole], lifetime);

    private Task<HttpResponseMessage> GetWithToken(string url, string? token) =>
        _client.SendAsync(HttpMethod.Get, url, token: token);

    private async Task AssertRejected(string token)
    {
        var response = await GetWithToken(ChenUrl, token);
        await response.AssertStatusAsync(HttpStatusCode.Unauthorized);
        var catalog = await GetWithToken(Catalog, token);
        await catalog.AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    // ---- accepted ----

    [DbFact]
    public async Task Valid_token_is_accepted()
    {
        var response = await GetWithToken(ChenUrl, TestTokens.Sign(ChenClaims()));
        await response.AssertStatusAsync(HttpStatusCode.OK);
        Assert.Equal(fx.ChenTicket, (await response.JsonAsync()).Id());
    }

    [DbFact]
    public async Task Another_users_valid_token_is_accepted_but_refused_this_users_records()
    {
        var okafor = TestTokens.For(Ids.InstructorOkafor);

        // Authenticated: the catalog is open to any valid caller.
        await (await GetWithToken(Catalog, okafor)).AssertStatusAsync(HttpStatusCode.OK);

        // ...but Dr. Chen's ticket does not exist as far as Prof. Okafor can tell.
        await (await GetWithToken(ChenUrl, okafor)).AssertStatusAsync(HttpStatusCode.NotFound);
        var put = await _client.SendAsync(HttpMethod.Put, ChenUrl, new
        {
            courseId = Ids.CourseCs501,
            items = new[] { new { assetId = Ids.AssetLaptop, quantity = 2 } },
        }, token: okafor);
        await put.AssertStatusAsync(HttpStatusCode.NotFound);
        await (await _client.SendAsync(HttpMethod.Delete, ChenUrl, token: okafor)).AssertStatusAsync(HttpStatusCode.NotFound);
    }

    [DbFact]
    public async Task Audience_array_that_includes_ours_is_accepted()
    {
        var claims = ChenClaims();
        claims["aud"] = new[] { "some-other-api", TestTokens.Audience };
        await (await GetWithToken(ChenUrl, TestTokens.Sign(claims))).AssertStatusAsync(HttpStatusCode.OK);
    }

    [DbFact]
    public async Task Token_expired_30_seconds_ago_is_inside_the_60_second_skew()
    {
        var claims = ChenClaims();
        claims["exp"] = DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeSeconds();
        claims["nbf"] = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        await (await GetWithToken(ChenUrl, TestTokens.Sign(claims))).AssertStatusAsync(HttpStatusCode.OK);
    }

    [DbFact]
    public async Task Token_without_nbf_is_accepted()
    {
        var claims = ChenClaims();
        claims.Remove("nbf");
        await (await GetWithToken(ChenUrl, TestTokens.Sign(claims))).AssertStatusAsync(HttpStatusCode.OK);
    }

    [DbFact]
    public async Task Token_signed_with_the_second_jwks_key_is_accepted_by_its_kid()
    {
        var token = TestTokens.Sign(ChenClaims(), TestTokens.Header(kid: TestTokens.SecondKid), TestTokens.SecondKey);
        await (await GetWithToken(ChenUrl, token)).AssertStatusAsync(HttpStatusCode.OK);
    }

    // ---- missing or rejected: 401 ----

    [DbFact]
    public async Task Missing_token_is_401() => await AssertRejectedNoToken();

    private async Task AssertRejectedNoToken()
    {
        await (await _client.SendAsync(HttpMethod.Get, ChenUrl)).AssertStatusAsync(HttpStatusCode.Unauthorized);
        await (await _client.SendAsync(HttpMethod.Get, Catalog)).AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [DbFact]
    public async Task Expired_token_is_401()
    {
        var claims = ChenClaims();
        claims["iat"] = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds();
        claims["nbf"] = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds();
        claims["exp"] = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        await AssertRejected(TestTokens.Sign(claims));
    }

    [DbFact]
    public async Task Token_expired_by_two_minutes_is_401()
    {
        var claims = ChenClaims();
        claims["nbf"] = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        claims["exp"] = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds();
        await AssertRejected(TestTokens.Sign(claims));
    }

    [DbFact]
    public async Task Token_not_yet_valid_is_401()
    {
        var claims = ChenClaims(TimeSpan.FromHours(1));
        claims["nbf"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        await AssertRejected(TestTokens.Sign(claims));
    }

    [DbFact]
    public async Task Token_without_exp_is_401()
    {
        var claims = ChenClaims();
        claims.Remove("exp");
        await AssertRejected(TestTokens.Sign(claims));
    }

    [DbFact]
    public async Task Wrong_issuer_is_401()
    {
        var claims = ChenClaims();
        claims["iss"] = "http://localhost:9001";
        await AssertRejected(TestTokens.Sign(claims));
    }

    [DbFact]
    public async Task Missing_issuer_is_401()
    {
        var claims = ChenClaims();
        claims.Remove("iss");
        await AssertRejected(TestTokens.Sign(claims));
    }

    [DbFact]
    public async Task Wrong_audience_is_401()
    {
        var claims = ChenClaims();
        claims["aud"] = "cs763-other-api";
        await AssertRejected(TestTokens.Sign(claims));
    }

    [DbFact]
    public async Task Audience_array_without_ours_is_401()
    {
        var claims = ChenClaims();
        claims["aud"] = new[] { "a", "b" };
        await AssertRejected(TestTokens.Sign(claims));
    }

    [DbTheory]
    [InlineData("none")]
    [InlineData("None")]
    [InlineData("NONE")]
    public async Task Alg_none_is_401(string alg) => await AssertRejected(TestTokens.Unsigned(ChenClaims(), alg));

    [DbFact]
    public async Task Hs256_signed_with_the_public_key_is_401()
    {
        var claims = ChenClaims();
        var key = TestTokens.SigningKey;
        // Every common encoding of the public key an attacker might use as an HMAC secret.
        var secrets = new[]
        {
            Encoding.ASCII.GetBytes(key.ExportSubjectPublicKeyInfoPem()),
            Encoding.ASCII.GetBytes(key.ExportRSAPublicKeyPem()),
            key.ExportSubjectPublicKeyInfo(),
            key.ExportParameters(false).Modulus!,
            JsonSerializer.SerializeToUtf8Bytes(TestTokens.Jwk(key, TestTokens.Kid)),
        };
        foreach (var secret in secrets)
        {
            await AssertRejected(TestTokens.SignHs256(claims, secret));
        }
    }

    [DbTheory]
    [InlineData("RS384")]
    [InlineData("RS512")]
    [InlineData("PS256")]
    public async Task Algorithm_not_on_the_allowlist_is_401_even_with_the_right_key(string alg)
    {
        var (hash, padding) = alg switch
        {
            "RS384" => (HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1),
            "RS512" => (HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1),
            _ => (HashAlgorithmName.SHA256, RSASignaturePadding.Pss),
        };
        var token = TestTokens.Sign(ChenClaims(), TestTokens.Header(alg), TestTokens.SigningKey, hash, padding);
        await AssertRejected(token);
    }

    [DbFact]
    public async Task Hs512_and_es256_headers_are_401()
    {
        await AssertRejected(TestTokens.SignHs256(ChenClaims(), RandomNumberGenerator.GetBytes(64)).Replace(
            TestTokens.Encode(TestTokens.Header("HS256")), TestTokens.Encode(TestTokens.Header("HS512"))));
        var es = TestTokens.Sign(ChenClaims(), TestTokens.Header("ES256"));
        await AssertRejected(es);
    }

    [DbFact]
    public async Task Bad_signature_is_401()
    {
        // Our kid, someone else's key.
        await AssertRejected(TestTokens.Sign(ChenClaims(), key: TestTokens.AttackerKey));

        // Right key, last signature byte flipped.
        var token = TestTokens.Sign(ChenClaims());
        var parts = token.Split('.');
        var sig = Convert.FromBase64String(Pad(parts[2].Replace('-', '+').Replace('_', '/')));
        sig[^1] ^= 0x01;
        await AssertRejected($"{parts[0]}.{parts[1]}.{TestTokens.B64Url(sig)}");
    }

    [DbFact]
    public async Task Token_signed_by_one_jwks_key_but_naming_the_other_kid_is_401()
    {
        var token = TestTokens.Sign(ChenClaims(), TestTokens.Header(kid: TestTokens.SecondKid), TestTokens.SigningKey);
        await AssertRejected(token);
    }

    [DbFact]
    public async Task Payload_changed_after_signing_is_401()
    {
        var token = TestTokens.Sign(ChenClaims());
        var parts = token.Split('.');

        // Promote the caller to admin and keep the original signature.
        var forged = ChenClaims();
        forged["roles"] = new[] { TestTokens.AdminRole };
        await AssertRejected($"{parts[0]}.{TestTokens.Encode(forged)}.{parts[2]}");

        // Swap the subject to another user.
        var other = ChenClaims();
        other["sub"] = Ids.InstructorOkafor.ToString();
        await AssertRejected($"{parts[0]}.{TestTokens.Encode(other)}.{parts[2]}");
    }

    [DbFact]
    public async Task Unknown_kid_is_401()
    {
        await AssertRejected(TestTokens.Sign(ChenClaims(), TestTokens.Header(kid: "not-in-the-jwks")));
        await AssertRejected(TestTokens.Sign(ChenClaims(), TestTokens.Header(kid: null)));
    }

    [DbFact]
    public async Task Jku_header_pointing_elsewhere_is_ignored_and_401()
    {
        foreach (var kid in new[] { "attacker-key", TestTokens.Kid })
        {
            var header = TestTokens.Header(kid: kid);
            header["jku"] = "https://attacker.example/.well-known/jwks.json";
            header["x5u"] = "https://attacker.example/cert.pem";
            await AssertRejected(TestTokens.Sign(ChenClaims(), header, TestTokens.AttackerKey));
        }
    }

    [DbFact]
    public async Task Embedded_jwk_header_is_ignored_and_401()
    {
        var header = TestTokens.Header(kid: "attacker-key");
        header["jwk"] = TestTokens.Jwk(TestTokens.AttackerKey, "attacker-key");
        await AssertRejected(TestTokens.Sign(ChenClaims(), header, TestTokens.AttackerKey));
    }

    [DbTheory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("a.b.c")]
    [InlineData("a.b.c.d.e")]
    [InlineData("eyJhbGciOiJSUzI1NiJ9.!!!.###")]
    [InlineData("....")]
    public async Task Malformed_token_is_401(string token) => await AssertRejected(token);

    [DbFact]
    public async Task Malformed_json_in_header_or_payload_is_401()
    {
        var good = TestTokens.Sign(ChenClaims()).Split('.');
        var junk = TestTokens.B64Url(Encoding.UTF8.GetBytes("{not json"));
        await AssertRejected($"{junk}.{good[1]}.{good[2]}");
        await AssertRejected($"{good[0]}.{junk}.{good[2]}");
    }

    [DbFact]
    public async Task Basic_scheme_or_empty_bearer_is_401()
    {
        using var basic = new HttpRequestMessage(HttpMethod.Get, Catalog);
        basic.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:admin")));
        await (await _client.SendAsync(basic)).AssertStatusAsync(HttpStatusCode.Unauthorized);

        using var empty = new HttpRequestMessage(HttpMethod.Get, Catalog);
        empty.Headers.TryAddWithoutValidation("Authorization", "Bearer ");
        await (await _client.SendAsync(empty)).AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [DbFact]
    public async Task Missing_or_non_guid_sub_is_401()
    {
        var noSub = ChenClaims();
        noSub.Remove("sub");
        await AssertRejected(TestTokens.Sign(noSub));

        var textSub = ChenClaims();
        textSub["sub"] = "dr-chen";
        await AssertRejected(TestTokens.Sign(textSub));
    }

    [DbFact]
    public async Task Roles_that_are_not_a_json_array_are_401()
    {
        var claims = ChenClaims();
        claims["roles"] = "admin";
        await AssertRejected(TestTokens.Sign(claims));

        claims["roles"] = new object[] { "faculty_instructor", 7 };
        await AssertRejected(TestTokens.Sign(claims));
    }

    [DbFact]
    public async Task Token_lifetime_validation_uses_exp_not_iat()
    {
        // A token issued "in the future" by a skewed clock is still valid if nbf/exp allow it.
        var claims = ChenClaims();
        claims["iat"] = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds();
        await (await GetWithToken(ChenUrl, TestTokens.Sign(claims))).AssertStatusAsync(HttpStatusCode.OK);
    }

    // ---- CWE-290: identity only from the verified token ----

    [DbFact]
    public async Task X_user_id_header_alone_is_401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Catalog);
        request.Headers.Add("X-User-Id", Ids.Admin.ToString());
        await (await _client.SendAsync(request)).AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [DbFact]
    public async Task X_user_id_header_does_not_change_the_caller()
    {
        // Prof. Okafor's token plus an X-User-Id naming Dr. Chen: still Prof. Okafor.
        using var read = new HttpRequestMessage(HttpMethod.Get, ChenUrl);
        read.Headers.Authorization = new("Bearer", TestTokens.For(Ids.InstructorOkafor));
        read.Headers.Add("X-User-Id", Ids.InstructorChen.ToString());
        await (await _client.SendAsync(read)).AssertStatusAsync(HttpStatusCode.NotFound);

        // A faculty token plus an X-User-Id naming the admin cannot create assets.
        using var create = new HttpRequestMessage(HttpMethod.Post, Catalog)
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                name = "Sneaky",
                type = "voucher",
                category = "Cloud",
                departmentProgram = "CS",
                balance = 10,
            }),
        };
        create.Headers.Authorization = new("Bearer", TestTokens.For(Ids.InstructorChen));
        create.Headers.Add("X-User-Id", Ids.Admin.ToString());
        await (await _client.SendAsync(create)).AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    [DbFact]
    public async Task Identity_and_roles_in_query_or_body_are_ignored()
    {
        var response = await _client.SendAsync(HttpMethod.Post,
            $"/api/v1/assets/tickets?sub={Ids.InstructorOkafor}&roles=admin&userId={Ids.InstructorOkafor}",
            $$"""
            {"courseId":"{{Ids.CourseCs501}}","requesterId":"{{Ids.InstructorOkafor}}","sub":"{{Ids.InstructorOkafor}}","roles":["admin"],
             "items":[{"assetId":"{{Ids.AssetLaptop}}","quantity":1}]}
            """,
            Ids.InstructorChen);

        await response.AssertStatusAsync(HttpStatusCode.Created);
        Assert.Equal(Ids.InstructorChen, (await response.JsonAsync()).Id("requesterId"));

        // Roles in the query string do not unlock admin actions.
        var asAdmin = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets?roles=admin", new
        {
            name = "Sneaky",
            type = "voucher",
            category = "Cloud",
            departmentProgram = "CS",
            balance = 10,
        }, Ids.InstructorChen);
        await asAdmin.AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    [DbFact]
    public async Task Rejected_token_response_does_not_explain_why()
    {
        var claims = ChenClaims();
        claims["iss"] = "http://evil";
        var response = await GetWithToken(Catalog, TestTokens.Sign(claims));
        await response.AssertStatusAsync(HttpStatusCode.Unauthorized);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        Assert.DoesNotContain("issuer", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IDX", challenge);
    }

    [Fact]
    public async Task Health_check_stays_public()
    {
        await using var factory = new UnreachableDbFactory();
        var response = await factory.CreateClient().GetAsync("/api/v1/health");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string Pad(string s) => s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
}
