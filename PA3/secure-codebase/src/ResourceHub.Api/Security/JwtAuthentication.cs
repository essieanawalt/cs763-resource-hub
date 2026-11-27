// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Local JWT validation: RS256 only, keys only from the JWKS file chosen by kid, exp/nbf/iss/aud checks with 60 s skew, strict sub and roles claims (~100%)
// Human Modifications: None yet
// Verification: AuthTokenTests (every token case in the brief) and ComposeStackTests
// Confidence: High

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ResourceHub.Api.Security;

public static class JwtAuthentication
{
    public const string JwksPathKey = "JWT_JWKS_PATH";
    public const string IssuerKey = "JWT_ISSUER";
    public const string AudienceKey = "JWT_AUDIENCE";
    public const string SubClaim = "sub";
    public const string RolesClaim = "roles";
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(60);
    private const int MinRsaKeyBits = 2048;

    /// <summary>
    /// Bearer tokens are verified in-process against the public keys in the JWKS file. Nothing is
    /// fetched over the network: no Authority, no metadata address, and the token's jku, x5u and
    /// jwk headers are never read.
    /// </summary>
    public static IServiceCollection AddLocalJwtAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var issuer = Required(config, IssuerKey);
        var audience = Required(config, AudienceKey);
        var keys = LoadJwks(Required(config, JwksPathKey));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = null;
                options.MetadataAddress = null!;
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;   // the 401 does not say which check failed
                options.SaveToken = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    TryAllIssuerSigningKeys = false,
                    // Only a key from our JWKS whose kid equals the token's kid. No kid, no key.
                    IssuerSigningKeyResolver = (_, _, kid, _) =>
                        kid is not null && keys.TryGetValue(kid, out var key) ? [key] : [],
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,       // a string, or an array that contains it
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = MaxClockSkew,
                    NameClaimType = SubClaim,
                    RoleClaimType = RolesClaim,
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var principal = StrictPrincipal(context.SecurityToken as JsonWebToken);
                        if (principal is null)
                        {
                            context.Fail("Token claims are not in the expected form.");
                        }
                        else
                        {
                            context.Principal = principal;
                        }
                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    /// <summary>
    /// Rebuilds the caller from the verified payload: sub must be a GUID (the seeded user ids) and
    /// roles, when present, a JSON array of strings. Every other claim is dropped.
    /// </summary>
    private static ClaimsPrincipal? StrictPrincipal(JsonWebToken? token)
    {
        if (token is null)
        {
            return null;
        }

        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(token.EncodedPayload));
        var root = payload.RootElement;
        if (!root.TryGetProperty(SubClaim, out var sub) || sub.ValueKind != JsonValueKind.String
            || !Guid.TryParse(sub.GetString(), out var subject))
        {
            return null;
        }

        var claims = new List<Claim> { new(SubClaim, subject.ToString("D")) };
        if (root.TryGetProperty(RolesClaim, out var roles))
        {
            if (roles.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            foreach (var role in roles.EnumerateArray())
            {
                if (role.ValueKind != JsonValueKind.String)
                {
                    return null;
                }
                claims.Add(new Claim(RolesClaim, role.GetString()!));
            }
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme, SubClaim, RolesClaim));
    }

    /// <summary>Reads RSA signing keys from the JWKS file, by kid. Private key material is never loaded.</summary>
    public static IReadOnlyDictionary<string, SecurityKey> LoadJwks(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"{JwksPathKey} points to a file that does not exist: {path}");
        }

        var set = new JsonWebKeySet(File.ReadAllText(path));
        var keys = new Dictionary<string, SecurityKey>(StringComparer.Ordinal);
        foreach (var jwk in set.Keys)
        {
            var usable = jwk.Kty == JsonWebAlgorithmsKeyTypes.RSA
                         && !string.IsNullOrEmpty(jwk.Kid)
                         && (string.IsNullOrEmpty(jwk.Use) || jwk.Use == JsonWebKeyUseNames.Sig)
                         && (string.IsNullOrEmpty(jwk.Alg) || jwk.Alg == SecurityAlgorithms.RsaSha256)
                         && !string.IsNullOrEmpty(jwk.N) && !string.IsNullOrEmpty(jwk.E);
            if (!usable)
            {
                continue;
            }

            var parameters = new System.Security.Cryptography.RSAParameters
            {
                Modulus = Base64UrlEncoder.DecodeBytes(jwk.N),
                Exponent = Base64UrlEncoder.DecodeBytes(jwk.E),
            };
            if (parameters.Modulus.Length * 8 < MinRsaKeyBits)
            {
                continue;
            }
            if (!keys.TryAdd(jwk.Kid, new RsaSecurityKey(parameters) { KeyId = jwk.Kid }))
            {
                throw new InvalidOperationException($"The JWKS file has more than one key with kid '{jwk.Kid}'.");
            }
        }

        if (keys.Count == 0)
        {
            throw new InvalidOperationException($"The JWKS file at {JwksPathKey} has no usable RS256 signing keys.");
        }
        return keys;
    }

    private static string Required(IConfiguration config, string key) =>
        config[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"{key} is not configured.");
}
