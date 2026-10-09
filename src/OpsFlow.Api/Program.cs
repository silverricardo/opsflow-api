using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpsFlow.Api.Features.Auth;
using OpsFlow.Api.Features.Profile;
using OpsFlow.Api.Features.Requests;
using OpsFlow.Api.Infrastructure;
using OpsFlow.Api.Infrastructure.Auth;
using OpsFlow.Api.Infrastructure.Correlation;
using OpsFlow.Api.Infrastructure.Health;
using OpsFlow.Api.Infrastructure.Http;
using OpsFlow.Api.Persistence.Seeding;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.AddOpsFlow();

var app = builder.Build();

// Order matters: correlation first so that error handling and request logs carry the ID.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diagnostics, http) =>
    diagnostics.Set("UserId", http.User.FindUserId()));
app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false, // process is up; no dependencies checked
    ResponseWriter = HealthResponseWriter.WriteAsync,
}).AllowAnonymous().DisableRateLimiting();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(ServiceRegistration.ReadinessTag),
    ResponseWriter = HealthResponseWriter.WriteAsync,
}).AllowAnonymous().DisableRateLimiting();

if (app.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.WithTitle("OpsFlow API")).AllowAnonymous();
}

var api = app.MapGroup("/api/v1");
api.MapTokenEndpoint();
api.MapMeEndpoint();
api.MapRequestEndpoints();

await app.InitializeDatabaseAsync();
await app.RunAsync();
