using Crm.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Crm.Api.Common.Security;

/// <summary>
/// Materializes a policy for each permission and population combination on demand, so that adding
/// an endpoint never means registering a policy by hand (spec FR-024, AR-004).
///
/// Falls back to the default provider for any policy name it does not recognise.
/// </summary>
public sealed class CrmAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    private readonly AuthorizationOptions _options = options.Value;

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);

        var existing = await base.GetPolicyAsync(policyName);
        if (existing is not null)
        {
            return existing;
        }

        if (policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];

            return Register(
                policyName,
                new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission))
                    .Build());
        }

        if (policyName.StartsWith(RequirePopulationAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var populations = policyName[RequirePopulationAttribute.PolicyPrefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Enum.Parse<CallerPopulation>(value, ignoreCase: true))
                .ToList();

            return Register(
                policyName,
                new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PopulationRequirement(populations))
                    .Build());
        }

        return null;
    }

    private AuthorizationPolicy Register(string policyName, AuthorizationPolicy policy)
    {
        // Cache it so the reflection-free path is used for every later request.
        _options.AddPolicy(policyName, policy);
        return policy;
    }
}
