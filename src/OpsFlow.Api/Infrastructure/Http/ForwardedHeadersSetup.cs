using Microsoft.AspNetCore.HttpOverrides;

namespace OpsFlow.Api.Infrastructure.Http;

public static class ForwardedHeadersSetup
{
    public const string EnabledSetting = "ForwardedHeaders:Enabled";

    /// <summary>
    /// Behind a TLS-terminating proxy (Azure Container Apps, App Service, an ingress controller) the client IP
    /// and scheme arrive in X-Forwarded-For / X-Forwarded-Proto. Without this, rate limiting would put every
    /// client in the proxy's partition. Only enable it when the app is reachable exclusively through the proxy:
    /// otherwise a client could spoof its IP. Only the nearest hop is trusted (ForwardLimit = 1).
    /// </summary>
    public static IServiceCollection AddOpsFlowForwardedHeaders(this IServiceCollection services) =>
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            // Platform proxies have no fixed address; the network boundary (ingress only) is what is trusted.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

    public static IApplicationBuilder UseOpsFlowForwardedHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Configuration.GetValue<bool>(EnabledSetting) ? app.UseForwardedHeaders() : app;
    }
}
