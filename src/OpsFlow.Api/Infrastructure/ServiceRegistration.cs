using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpsFlow.Api.Infrastructure.Health;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Correlation;
using OpsFlow.Api.Infrastructure.Errors;
using OpsFlow.Api.Infrastructure.OpenApi;
using OpsFlow.Api.Infrastructure.RateLimiting;
using OpsFlow.Api.Persistence;
using OpsFlow.Api.Persistence.Seeding;

namespace OpsFlow.Api.Infrastructure;

public static class ServiceRegistration
{
    public const string ReadinessTag = "ready";
    public const string ConnectionStringName = "OpsFlow";

    public static IServiceCollection AddOpsFlow(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<CorrelationContext>();

        services.ConfigureHttpJsonOptions(options =>
        {
            var json = options.SerializerOptions;
            json.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            json.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            json.NumberHandling = JsonNumberHandling.Strict;
        });

        services.AddDbContext<OpsFlowDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' is not configured (ConnectionStrings__{ConnectionStringName}).");
            }

            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
        });

        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName);
        services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName);
        services.AddScoped<DemoDataSeeder>();

        services.AddValidatorsFromAssemblyContaining<OpsFlowDbContext>(ServiceLifetime.Singleton);
        services.AddOpsFlowProblemDetails();
        services.AddOpsFlowAuthentication();
        services.AddOpsFlowRateLimiting();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", HealthStatus.Unhealthy, [ReadinessTag], DatabaseHealthCheck.Timeout);
        services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

        return services;
    }
}
