using OpsFlow.Api.Infrastructure.Correlation;

namespace OpsFlow.Api.Infrastructure.Errors;

public static class ProblemDetailsSetup
{
    /// <summary>
    /// Every problem response (domain errors, validation, status code pages, exceptions) gets
    /// <c>code</c>, <c>correlationId</c> and <c>instance</c>, so clients can rely on one shape (NFR-01).
    /// </summary>
    public static IServiceCollection AddOpsFlowProblemDetails(this IServiceCollection services) =>
        services
            .AddExceptionHandler<GlobalExceptionHandler>()
            .AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            {
                var problem = context.ProblemDetails;
                var status = problem.Status ?? context.HttpContext.Response.StatusCode;

                problem.Instance ??= context.HttpContext.Request.Path;
                problem.Extensions.TryAdd("code", ErrorCodes.ForStatus(status));
                problem.Extensions.Remove("traceId");
                problem.Extensions["correlationId"] =
                    context.HttpContext.RequestServices.GetRequiredService<CorrelationContext>().CorrelationId;
            });
}
