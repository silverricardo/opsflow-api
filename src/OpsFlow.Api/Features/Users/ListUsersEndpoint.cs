using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Features.Common;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Validation;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Features.Users;

public sealed record UserResponse(int Id, string Email, string DisplayName, UserRole Role, int? ManagerId, bool IsActive);

public static class ListUsersEndpoint
{
    public static void MapListUsersEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/users", ListAsync)
            .RequireAuthorization(Policies.AdminOnly)
            .WithValidation<PageQuery>()
            .WithName("ListUsers")
            .WithTags("Users")
            .WithSummary("Administrators: list users, e.g. to pick a new assignee for a step.");

    private static async Task<Ok<PagedResponse<UserResponse>>> ListAsync(
        [AsParameters] PageQuery page,
        UserRole? role,
        OpsFlowDbContext db,
        CancellationToken cancellationToken)
    {
        var users = db.Users.AsQueryable();
        if (role is { } r)
        {
            users = users.Where(u => u.Role == r);
        }

        var totalCount = await users.CountAsync(cancellationToken);
        var items = await users
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Id)
            .Skip(page.Skip)
            .Take(page.Size)
            .Select(u => new UserResponse(u.Id, u.Email, u.DisplayName, u.Role, u.ManagerId, u.IsActive))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(PagedResponse.Create(items, page, totalCount));
    }
}
