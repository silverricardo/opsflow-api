using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Infrastructure.Auth;

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>Issues short-lived HS256 access tokens for the local demo identity provider (ADR-006).</summary>
public sealed class TokenIssuer(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedToken Issue(int userId, string email, UserRole role)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(jwt.AccessTokenLifetimeMinutes);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [OpsFlowClaims.Subject] = userId.ToString(CultureInfo.InvariantCulture),
                [OpsFlowClaims.Email] = email,
                [OpsFlowClaims.Role] = role.ToString(),
                [OpsFlowClaims.TokenId] = Guid.NewGuid().ToString("N"),
            },
        });

        return new IssuedToken(token, expiresAt);
    }
}
