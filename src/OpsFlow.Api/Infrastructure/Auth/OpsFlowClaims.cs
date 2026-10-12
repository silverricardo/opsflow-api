using System.Globalization;
using System.Security.Claims;
using OpsFlow.Domain.Requests;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Infrastructure.Auth;

/// <summary>Claim names used in OpsFlow tokens (registered JWT names; inbound claim mapping is disabled).</summary>
public static class OpsFlowClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Role = "role";
    public const string TokenId = "jti";

    public static int? FindUserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal?.FindFirstValue(Subject), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    /// <summary>Builds the domain actor from a validated token. Tokens are issued by us, so malformed claims are a bug.</summary>
    public static Actor ToActor(this ClaimsPrincipal principal)
    {
        var userId = principal.FindUserId()
            ?? throw new InvalidOperationException("The authenticated principal has no valid 'sub' claim.");

        if (!Enum.TryParse<UserRole>(principal.FindFirstValue(Role), ignoreCase: false, out var role))
        {
            throw new InvalidOperationException("The authenticated principal has no valid 'role' claim.");
        }

        return new Actor(userId, role);
    }
}
