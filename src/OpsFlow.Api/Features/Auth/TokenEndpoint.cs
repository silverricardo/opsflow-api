using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.RateLimiting;
using OpsFlow.Api.Infrastructure.Validation;
using OpsFlow.Api.Persistence;
using OpsFlow.Domain.Users;

namespace OpsFlow.Api.Features.Auth;

public sealed record TokenRequest(string? Email, string? Password);

public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public sealed class TokenRequestValidator : AbstractValidator<TokenRequest>
{
    public TokenRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

public static partial class TokenEndpoint
{
    private static readonly User DummyUser = new("nobody@invalid", "nobody", UserRole.Employee, null, DateTimeOffset.UnixEpoch);

    // Unknown e-mails are verified against this hash so that response time does not reveal which accounts exist.
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<User>().HashPassword(DummyUser, Guid.NewGuid().ToString()));

    public static void MapTokenEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/auth/token", IssueTokenAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.TokenPolicy)
            .WithValidation<TokenRequest>()
            .WithName("IssueToken")
            .WithTags("Auth")
            .WithSummary("Exchange e-mail and password for a short-lived access token.");

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> IssueTokenAsync(
        TokenRequest body,
        OpsFlowDbContext db,
        IPasswordHasher<User> passwordHasher,
        TokenIssuer tokenIssuer,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(TokenEndpoint));
        var email = User.NormalizeEmail(body.Email!);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            passwordHasher.VerifyHashedPassword(DummyUser, DummyHash.Value, body.Password!);
            LogUnknownAccount(logger);
            return InvalidCredentials();
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, body.Password!);
        if (verification == PasswordVerificationResult.Failed || !user.IsActive)
        {
            LogFailedSignIn(logger, user.Id);
            return InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(passwordHasher.HashPassword(user, body.Password!));
            await db.SaveChangesAsync(cancellationToken);
        }

        var token = tokenIssuer.Issue(user.Id, user.Email, user.Role);
        LogSignedIn(logger, user.Id);
        return TypedResults.Ok(new TokenResponse(token.AccessToken, "Bearer", token.ExpiresAt));
    }

    // Same response for unknown e-mail, wrong password and inactive account.
    private static ProblemHttpResult InvalidCredentials() =>
        Problems.Create(
            StatusCodes.Status401Unauthorized,
            ErrorCodes.InvalidCredentials,
            "Invalid credentials",
            "The e-mail or password is incorrect.");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed sign-in attempt for an unknown account.")]
    private static partial void LogUnknownAccount(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed sign-in attempt for user {UserId}.")]
    private static partial void LogFailedSignIn(ILogger logger, int userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in.")]
    private static partial void LogSignedIn(ILogger logger, int userId);
}
