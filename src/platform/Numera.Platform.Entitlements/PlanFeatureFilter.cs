using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;

namespace Numera.Platform.Entitlements;

/// <summary>
/// A <see cref="Microsoft.FeatureManagement"/> custom feature filter that enables a
/// feature only when the current tenant's plan grants the required
/// <see cref="Capability"/>. The capability is supplied as the filter's
/// <c>Capability</c> parameter, so an endpoint decorated
/// <c>[FeatureGate("EInvoicing")]</c> — with the <c>EInvoicing</c> feature configured to
/// use this filter with <c>Capability = EInvoicing</c> — is enabled only for tenants
/// whose plan includes that capability.
/// </summary>
/// <remarks>
/// <para>
/// This filter holds NO plan logic of its own: it delegates entirely to
/// <see cref="IEntitlementService.HasCapabilityAsync"/>, which reads the tenant's
/// persisted plan (server-authoritative). The <c>auth &#8743; tenant</c> parts of the guard
/// are enforced upstream (authentication + tenancy middleware set
/// <see cref="Numera.Platform.Tenancy.ICurrentTenant"/>); this adds the
/// <c>&#8743; entitlement</c> part.
/// </para>
/// <para>
/// DI registration is performed by the Api host (plan 01-06), not here:
/// <code>
/// services.AddScoped&lt;IEntitlementService, EntitlementService&gt;();
/// services.AddFeatureManagement()
///         .AddFeatureFilter&lt;PlanFeatureFilter&gt;();
/// // appsettings feature config, e.g.:
/// // "FeatureManagement": {
/// //   "EInvoicing": { "EnabledFor": [ { "Name": "Plan", "Parameters": { "Capability": "EInvoicing" } } ] }
/// // }
/// </code>
/// </para>
/// </remarks>
[FilterAlias("Plan")]
public sealed class PlanFeatureFilter : IFeatureFilter
{
    private readonly IEntitlementService _entitlements;

    /// <summary>Creates the filter for the current DI scope.</summary>
    public PlanFeatureFilter(IEntitlementService entitlements)
    {
        _entitlements = entitlements;
    }

    /// <summary>
    /// Reads the required <see cref="Capability"/> from the filter parameters and returns
    /// whether the current tenant's plan grants it. An unknown/missing capability
    /// parameter disables the feature (deny by default) rather than throwing.
    /// </summary>
    public async Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var raw = context.Parameters?["Capability"];
        if (!Enum.TryParse<Capability>(raw, ignoreCase: true, out var capability))
        {
            // Misconfigured or absent capability -> deny by default.
            return false;
        }

        return await _entitlements.HasCapabilityAsync(capability).ConfigureAwait(false);
    }
}
