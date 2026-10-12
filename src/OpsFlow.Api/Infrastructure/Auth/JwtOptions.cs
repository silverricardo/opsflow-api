using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace OpsFlow.Api.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32; // HS256 needs a key of at least 256 bits.

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Secret. Supplied by environment, user secrets or a secret store — never committed.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    public bool HasStrongEnoughKey() => Encoding.UTF8.GetByteCount(SigningKey) >= MinimumKeyBytes;
}
