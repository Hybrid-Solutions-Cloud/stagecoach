using Stagecoach.Core;

namespace Stagecoach.Infrastructure.Orchestration;

public static class ConnectionIdentityMatcher
{
    public static ConnectionIdentityProfile? Select(
        MachineRecord machine,
        AzureAccessPath accessPath,
        IReadOnlyList<ConnectionIdentityProfile> profiles,
        IReadOnlyList<ConnectionIdentityMapping> mappings,
        bool relayIdentity)
    {
        var enabled = profiles.Where(item => item.IsEnabled).ToDictionary(item => item.Id);
        return mappings
            .Where(item => item.IsRelayIdentity == relayIdentity && enabled.ContainsKey(item.ConnectionIdentityId))
            .Where(item => IsMatch(item, machine, accessPath))
            .OrderByDescending(item => Specificity(item.ScopeKind))
            .ThenByDescending(item => item.Priority)
            .Select(item => enabled[item.ConnectionIdentityId])
            .FirstOrDefault();
    }

    private static bool IsMatch(ConnectionIdentityMapping mapping, MachineRecord machine, AzureAccessPath path) =>
        mapping.ScopeKind switch
        {
            MappingScopeKind.Machine => EqualsValue(mapping.MatchValue, machine.ResourceId) || EqualsValue(mapping.MatchValue, machine.Name),
            MappingScopeKind.Domain => EqualsValue(mapping.MatchValue, machine.DomainName),
            MappingScopeKind.ResourceGroup => EqualsValue(mapping.MatchValue, machine.ResourceGroup),
            MappingScopeKind.Subscription => EqualsValue(mapping.MatchValue, path.SubscriptionId),
            MappingScopeKind.Tenant => EqualsValue(mapping.MatchValue, path.TenantId),
            MappingScopeKind.Tag => MatchTag(mapping.MatchValue, machine.Tags),
            _ => false,
        };

    private static bool MatchTag(string expression, IReadOnlyDictionary<string, string> tags)
    {
        var separator = expression.IndexOf('=');
        if (separator <= 0) return false;
        var key = expression[..separator].Trim();
        var value = expression[(separator + 1)..].Trim();
        return tags.TryGetValue(key, out var actual) && EqualsValue(value, actual);
    }

    private static bool EqualsValue(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim().TrimEnd('/'), right.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static int Specificity(MappingScopeKind kind) => kind switch
    {
        MappingScopeKind.Machine => 600,
        MappingScopeKind.Tag => 500,
        MappingScopeKind.Domain => 400,
        MappingScopeKind.ResourceGroup => 300,
        MappingScopeKind.Subscription => 200,
        MappingScopeKind.Tenant => 100,
        _ => 0,
    };
}
