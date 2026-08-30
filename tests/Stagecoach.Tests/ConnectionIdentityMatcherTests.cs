using Stagecoach.Core;
using Stagecoach.Infrastructure.Orchestration;

namespace Stagecoach.Tests;

public sealed class ConnectionIdentityMatcherTests
{
    [Fact]
    public void Select_PrefersMachineOverDomainAndTenant()
    {
        var tenant = Profile("tenant");
        var domain = Profile("domain");
        var machineProfile = Profile("machine");
        var machine = Machine("corp.example.com");
        var path = machine.AccessPaths[0];
        var mappings = new[]
        {
            Map(tenant, MappingScopeKind.Tenant, path.TenantId),
            Map(domain, MappingScopeKind.Domain, "corp.example.com"),
            Map(machineProfile, MappingScopeKind.Machine, machine.ResourceId),
        };

        var selected = ConnectionIdentityMatcher.Select(machine, path, [tenant, domain, machineProfile], mappings, false);

        Assert.Equal(machineProfile.Id, selected?.Id);
    }

    [Fact]
    public void Select_SeparatesRelayMappings()
    {
        var desktop = Profile("desktop");
        var relay = Profile("relay");
        var machine = Machine("corp.example.com");
        var mappings = new[]
        {
            Map(desktop, MappingScopeKind.Domain, "corp.example.com"),
            Map(relay, MappingScopeKind.Domain, "corp.example.com") with { IsRelayIdentity = true },
        };

        Assert.Equal(desktop.Id, ConnectionIdentityMatcher.Select(machine, machine.AccessPaths[0], [desktop, relay], mappings, false)?.Id);
        Assert.Equal(relay.Id, ConnectionIdentityMatcher.Select(machine, machine.AccessPaths[0], [desktop, relay], mappings, true)?.Id);
    }

    private static ConnectionIdentityProfile Profile(string name) =>
        new(Guid.NewGuid(), name, ConnectionIdentityKind.ActiveDirectory, $"CORP\\{name}", null, null);

    private static ConnectionIdentityMapping Map(ConnectionIdentityProfile profile, MappingScopeKind kind, string value) =>
        new(Guid.NewGuid(), profile.Id, kind, value, 0);

    private static MachineRecord Machine(string domain)
    {
        var path = new AzureAccessPath(Guid.NewGuid(), "tenant", "subscription", ConnectionRouteKind.ArcRdp, ReadinessState.Ready, "ready", IsPreferred: true);
        return new MachineRecord("/subscriptions/test/resourceGroups/rg/providers/Microsoft.HybridCompute/machines/vm1", "vm1",
            MachineKind.ArcServer, OperatingSystemKind.Windows, "Windows", "rg", "eastus", "running", "connected",
            null, null, null, domain, new Dictionary<string, string>(), [path], DateTimeOffset.UtcNow);
    }
}
