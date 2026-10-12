namespace OpsFlow.Api.Infrastructure.Correlation;

/// <summary>Holds the correlation ID of the current scope (HTTP request, startup task, …).</summary>
public sealed class CorrelationContext
{
    public const string HeaderName = "X-Correlation-Id";

    public string CorrelationId { get; set; } = "none";
}
