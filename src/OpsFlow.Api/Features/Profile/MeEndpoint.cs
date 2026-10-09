using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Features.Profile;

public sealed record MeResponse(int Id, string Email, string DisplayName, UserRole Role, int? ManagerId);

public static class MeEndpoint
{
    public static void MapMeEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/me", GetMeAsync)
            .WithName("GetMe")
            .WithTags("Auth")
            .WithSummary("The authenticated user's profile.");

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> GetMeAsync(
        ClaimsPrincipal principal,
        OpsFlowDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = principal.ToActor().UserId;
        var me = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new MeResponse(u.Id, u.Email, u.DisplayName, u.Role, u.ManagerId))
            .SingleOrDefaultAsync(cancellationToken);

        return me is null
            ? Problems.Create(StatusCodes.Status404NotFound, "user.not_found", "Not found", "The user no longer exists.")
            : TypedResults.Ok(me);
    }
}
