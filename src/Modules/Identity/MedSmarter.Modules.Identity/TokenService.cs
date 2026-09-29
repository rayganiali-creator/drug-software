using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MedSmarter.BuildingBlocks;
using MedSmarter.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MedSmarter.Modules.Identity;

/// <summary>
/// Access tokens: HS256 JWT with only sub, sid, jti and times. Refresh tokens: 256-bit random opaque values,
/// stored only as SHA-256. Roles and permissions are deliberately NOT in the token (they are resolved per request).
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly AuthOptions _options;
    private readonly IClock _clock;
    private readonly SymmetricSecurityKey _key;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _validation;

    public TokenService(IOptions<AuthOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;
        var material = string.IsNullOrEmpty(_options.SigningKey)
            ? RandomNumberGenerator.GetBytes(48) // dev only: ephemeral, never logged, never persisted
            : Encoding.UTF8.GetBytes(_options.SigningKey);
        if (material.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be at least 32 characters.");
        }

        _key = new SymmetricSecurityKey(material);
        _validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(15),
            LifetimeValidator = (nbf, exp, _, _) => exp is not null && exp > _clock.UtcNow.UtcDateTime && (nbf is null || nbf <= _clock.UtcNow.UtcDateTime.AddSeconds(15)),
        };
    }

    public AccessTokenResult IssueAccessToken(Guid userId, Guid sessionId)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                ["sid"] = sessionId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        };
        return new AccessTokenResult(_handler.CreateToken(descriptor), expires);
    }

    public AccessTokenClaims? ValidateAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
        {
            return null;
        }

        var result = _handler.ValidateTokenAsync(token, _validation).GetAwaiter().GetResult();
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            return null;
        }

        var sub = jwt.Subject;
        if (!Guid.TryParse(sub, out var userId) || !jwt.TryGetPayloadValue<string>("sid", out var sid) || !Guid.TryParse(sid, CultureInfo.InvariantCulture, out var sessionId))
        {
            return null;
        }

        return new AccessTokenClaims(userId, sessionId, new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero));
    }

    public (string Token, string Hash) NewRefreshToken()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (token, HashRefreshToken(token));
    }

    public string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
