using System.Diagnostics.Metrics;
using OpsFlow.Domain.Requests;

namespace OpsFlow.Api.Infrastructure.Observability;

/// <summary>
/// Business metrics, next to the HTTP/runtime metrics from OpenTelemetry. They answer the operational
/// questions the platform metrics cannot: how many requests come in, and how approvals are decided.
/// </summary>
public sealed class OpsFlowMetrics
{
    public const string MeterName = "OpsFlow.Api";

    private readonly Counter<long> _requestsCreated;
    private readonly Counter<long> _decisions;
    private readonly Counter<long> _idempotentReplays;

    public OpsFlowMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _requestsCreated = meter.CreateCounter<long>("opsflow.requests.created", unit: "{request}", description: "Requests created.");
        _decisions = meter.CreateCounter<long>("opsflow.approval.decisions", unit: "{decision}", description: "Approval decisions recorded.");
        _idempotentReplays = meter.CreateCounter<long>("opsflow.idempotency.replays", unit: "{response}", description: "Responses replayed for a repeated Idempotency-Key.");
    }

    public void RequestCreated(RequestCategory category) =>
        _requestsCreated.Add(1, new KeyValuePair<string, object?>("category", category.ToString()));

    public void DecisionRecorded(Decision decision) =>
        _decisions.Add(1, new KeyValuePair<string, object?>("decision", decision.ToString()));

    public void IdempotentReplay() => _idempotentReplays.Add(1);
}
