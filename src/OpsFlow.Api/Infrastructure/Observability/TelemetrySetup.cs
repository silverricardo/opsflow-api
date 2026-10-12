using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OpsFlow.Api.Infrastructure.Observability;

public static class TelemetrySetup
{
    public const string ServiceName = "opsflow-api";
    public const string OtlpEndpointSetting = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Traces (incoming HTTP, outgoing HTTP, SQL) and metrics (HTTP, runtime, business), exported over OTLP
    /// only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set — e.g. the Aspire dashboard locally, or Azure
    /// Monitor / Grafana / any OTLP collector in production. Serilog already writes the trace and span IDs
    /// on every log event, so logs and traces can be joined.
    /// </summary>
    public static IServiceCollection AddOpsFlowTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<OpsFlowMetrics>();

        var builder = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = http => !http.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(OpsFlowMetrics.MeterName));

        if (!string.IsNullOrWhiteSpace(configuration[OtlpEndpointSetting]))
        {
            builder.UseOtlpExporter();
        }

        return services;
    }
}
