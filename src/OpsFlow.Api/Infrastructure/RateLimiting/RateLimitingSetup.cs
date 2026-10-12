using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Errors;

namespace OpsFlow.Api.Infrastructure.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Token endpoint: attempts per minute per client IP (brute-force protection).</summary>
    public int TokenPermitLimit { get; set; } = 5;

    /// <summary>Global token bucket per user (or IP when anonymous): burst size.</summary>
    public int GlobalTokenLimit { get; set; } = 100;

    /// <summary>Global token bucket: tokens added per second.</summary>
    public int GlobalTokensPerSecond { get; set; } = 50;
}

public static class RateLimitingSetup
{
    public const string TokenPolicy = "token";

    public static IServiceCollection AddOpsFlowRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName);

        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteRejectionAsync;

            limiter.AddPolicy(TokenPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientIp(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Options(http).TokenPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var options = Options(http);
                var userId = http.User.FindUserId();
                return RateLimitPartition.GetTokenBucketLimiter(
                    userId is null ? $"ip:{ClientIp(http)}" : $"user:{userId}",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = options.GlobalTokenLimit,
                        TokensPerPeriod = options.GlobalTokensPerSecond,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
        });
    }

    // Behind a reverse proxy this requires the forwarded-headers middleware (see docs/deployment).
    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitingOptions Options(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Extensions = { ["code"] = ErrorCodes.RateLimitExceeded },
            },
        });
    }
}
