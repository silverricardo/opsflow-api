using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace OpsFlow.Api.Infrastructure.Auth;

public sealed class SignInThrottleOptions
{
    public const string SectionName = "SignInThrottle";

    /// <summary>Failed attempts allowed per account within <see cref="WindowMinutes"/>.</summary>
    public int MaxFailures { get; set; } = 10;

    public int WindowMinutes { get; set; } = 15;
}

/// <summary>
/// Per-account limit on failed sign-ins, complementing the per-IP rate limit: an attacker spread over
/// many addresses still gets at most <c>MaxFailures</c> guesses per account per window. It applies to
/// unknown e-mails too, so it reveals nothing about which accounts exist. Trade-offs (documented): the
/// count is per instance (use a distributed cache when scaling out), and an attacker can temporarily
/// block sign-in for a known e-mail — a soft lockout that expires by itself.
/// </summary>
public sealed class SignInThrottle(IMemoryCache cache, IOptions<SignInThrottleOptions> options)
{
    private sealed class Counter
    {
        public int Failures;
    }

    public bool IsBlocked(string normalizedEmail) =>
        cache.TryGetValue(Key(normalizedEmail), out Counter? counter) && Volatile.Read(ref counter!.Failures) >= options.Value.MaxFailures;

    public void RecordFailure(string normalizedEmail)
    {
        var counter = cache.GetOrCreate(Key(normalizedEmail), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(options.Value.WindowMinutes);
            entry.Size = 1;
            return new Counter();
        })!;
        Interlocked.Increment(ref counter.Failures);
    }

    public void Reset(string normalizedEmail) => cache.Remove(Key(normalizedEmail));

    private static string Key(string normalizedEmail) => $"signin-failures:{normalizedEmail}";
}

public static class SignInThrottleSetup
{
    public static IServiceCollection AddOpsFlowSignInThrottle(this IServiceCollection services)
    {
        // Bounded so a flood of random e-mails cannot grow memory without limit.
        services.AddMemoryCache(options => options.SizeLimit = 100_000);
        services.AddOptions<SignInThrottleOptions>().BindConfiguration(SignInThrottleOptions.SectionName);
        services.AddSingleton<SignInThrottle>();
        return services;
    }
}
