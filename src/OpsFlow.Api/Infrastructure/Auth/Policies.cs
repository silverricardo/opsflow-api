using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Infrastructure.Auth;

/// <summary>
/// Endpoint-level role policies — the first of three authorization layers (design.md §5). Resource
/// visibility and domain rules still apply after these.
/// </summary>
public static class Policies
{
    public const string AdminOnly = nameof(AdminOnly);
    public const string FinanceOnly = nameof(FinanceOnly);
    public const string Approver = nameof(Approver);

    public static IServiceCollection AddOpsFlowPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminOnly, p => p.RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(FinanceOnly, p => p.RequireRole(nameof(UserRole.Finance)))
            .AddPolicy(Approver, p => p.RequireRole(nameof(UserRole.Manager), nameof(UserRole.Finance), nameof(UserRole.Director)));
        return services;
    }
}
