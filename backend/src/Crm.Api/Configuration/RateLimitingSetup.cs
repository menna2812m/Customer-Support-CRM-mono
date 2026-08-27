using System.Globalization;
using System.Threading.RateLimiting;
using Crm.Api.Common.Errors;
using Crm.Application.Common;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Crm.Api.Configuration;

/// <summary>
/// Throttling for the endpoints anybody can reach (spec FR-036, FR-037).
///
/// Three of this feature's endpoints are necessarily anonymous, which makes them the only doors in
/// the application that can be knocked on indefinitely. Limiting them is what keeps a credential
/// hunt or a redirect flood from becoming an outage for everybody else.
///
/// Built as a reusable capability rather than as three special cases: policies are named, come
/// from configuration, and are attached with the framework's <c>[EnableRateLimiting]</c> attribute,
/// so a later feature throttles an endpoint by annotating it instead of by editing this file.
/// </summary>
public static class RateLimitingSetup
{
    public static IServiceCollection AddCrmRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteRejectionAsync;

            foreach (var name in CrmRateLimitPolicies.All)
            {
                // The partition is resolved per request, so the limits are read from configuration
                // when they are first used rather than while services are being registered - the
                // same lazy-read rule the rest of this application follows.
                limiter.AddPolicy(name, context => Partition(context, name));
            }
        });

        return services;
    }

    /// <summary>
    /// One bucket per caller per policy. Partitioning by source address is what makes the limit a
    /// defence rather than a denial of service: one abusive client must not consume the allowance
    /// of an entire office behind the same application.
    /// </summary>
    private static RateLimitPartition<string> Partition(HttpContext context, string policyName)
    {
        var options = context.RequestServices
            .GetRequiredService<IOptionsMonitor<RateLimitingOptions>>()
            .CurrentValue;

        if (!options.Enabled)
        {
            return RateLimitPartition.GetNoLimiter($"{policyName}:disabled");
        }

        var policy = options.Policies.TryGetValue(policyName, out var configured)
            ? configured
            : new RateLimitPolicyOptions();

        // A missing address means a request that did not arrive over a socket we can attribute -
        // a test host, or a misconfigured proxy. One shared bucket is the conservative reading.
        var source = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{policyName}:{source}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = policy.PermitLimit,
                Window = TimeSpan.FromSeconds(policy.WindowSeconds),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
    }

    /// <summary>
    /// A throttled request answers with the same contract as every other refusal, plus the one
    /// header that tells a client when to come back instead of hammering (spec FR-037).
    /// </summary>
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var window)
            ? window
            : TimeSpan.FromSeconds(60);

        httpContext.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        await ErrorContractSetup.WriteProblemAsync(
            httpContext,
            StatusCodes.Status429TooManyRequests,
            ErrorCodes.RateLimited,
            "Too many requests. Wait for the period named in the Retry-After header and try again.");
    }
}

/// <summary>
/// The policy names this feature applies. Constants rather than literals so a misspelling on an
/// endpoint is a build error instead of an endpoint that is silently unthrottled.
/// </summary>
public static class CrmRateLimitPolicies
{
    /// <summary>Starting a handshake and returning from the provider.</summary>
    public const string SignIn = "auth-sign-in";

    /// <summary>Exchanging the renewal cookie for an access credential.</summary>
    public const string Session = "auth-session";

    public static IReadOnlyList<string> All { get; } = [SignIn, Session];
}
