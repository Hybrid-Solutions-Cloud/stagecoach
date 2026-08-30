using System.Text.Json;
using Stagecoach.Core;
using Stagecoach.Infrastructure.Azure;

namespace Stagecoach.Tests;

public sealed class ResourceGraphDiscoveryTests
{
    [Fact]
    public void ParsePage_ReadsCliEnvelopeAndSkipToken()
    {
        const string json = """{"data":[{"id":"/vm/one","name":"one","type":"microsoft.compute/virtualmachines","tenantId":"t","subscriptionId":"s","resourceGroup":"rg","location":"eastus","properties":{}}],"$skipToken":"next"}""";
        var (resources, token) = ResourceGraphDiscoveryService.ParsePage(json);
        Assert.Single(resources);
        Assert.Equal("next", token);
    }

    [Fact]
    public void Correlate_AzureVmBehindStandardBastion_ProducesReadyTunnel()
    {
        var vmId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm1";
        var vnetId = "/subscriptions/s/resourceGroups/net/providers/Microsoft.Network/virtualNetworks/hub";
        var resources = new[]
        {
            Resource(vmId, "vm1", "microsoft.compute/virtualmachines", "rg", Props("""{"storageProfile":{"osDisk":{"osType":"Windows"}}}"""), Tags("""{"domain":"corp.example.com"}""")),
            Resource("/subscriptions/s/resourceGroups/rg/providers/Microsoft.Network/networkInterfaces/nic1", "nic1", "microsoft.network/networkinterfaces", "rg",
                Props("""{"virtualMachine":{"id":"__VM__"},"ipConfigurations":[{"properties":{"privateIPAddress":"10.0.0.4","subnet":{"id":"__VNET__/subnets/workload"}}}]}""".Replace("__VM__", vmId).Replace("__VNET__", vnetId))),
            Resource("/subscriptions/s/resourceGroups/net/providers/Microsoft.Network/bastionHosts/bastion1", "bastion1", "microsoft.network/bastionhosts", "net",
                Props("""{"enableTunneling":true,"ipConfigurations":[{"properties":{"subnet":{"id":"__VNET__/subnets/AzureBastionSubnet"}}}]}""".Replace("__VNET__", vnetId)), sku: Props("""{"name":"Standard"}""")),
        };

        var machine = Assert.Single(ResourceGraphDiscoveryService.Correlate(Guid.NewGuid(), resources, DateTimeOffset.UtcNow));
        Assert.Equal("corp.example.com", machine.DomainName);
        Assert.Equal("10.0.0.4", machine.PrivateIpAddress);
        Assert.Contains(machine.AccessPaths, path => path.Route == ConnectionRouteKind.BastionTunnelRdp && path.Readiness == ReadinessState.Ready && path.IsPreferred);
    }

    [Fact]
    public void Correlate_ConnectedWindowsArcWithoutOpenSsh_RequiresRemediation()
    {
        var arc = Resource("/subscriptions/s/resourceGroups/rg/providers/Microsoft.HybridCompute/machines/arc1", "arc1",
            "microsoft.hybridcompute/machines", "rg", Props("""{"osName":"Windows Server 2022","status":"Connected"}"""));
        var machine = Assert.Single(ResourceGraphDiscoveryService.Correlate(Guid.NewGuid(), [arc], DateTimeOffset.UtcNow));
        Assert.Equal(MachineKind.ArcServer, machine.Kind);
        Assert.All(machine.AccessPaths, path => Assert.Equal(ReadinessState.MissingPrerequisite, path.Readiness));
    }

    [Fact]
    public void Correlate_AzureLocalChild_DeduplicatesHybridParent()
    {
        var parentId = "/subscriptions/s/resourceGroups/rg/providers/Microsoft.HybridCompute/machines/local1";
        var parent = Resource(parentId, "local1", "microsoft.hybridcompute/machines", "rg", Props("""{"osName":"Windows Server 2025","status":"Connected"}"""));
        var child = Resource(parentId + "/providers/Microsoft.AzureStackHCI/virtualMachineInstances/default", "default",
            "microsoft.azurestackhci/virtualmachineinstances", "rg", Props("""{"provisioningState":"Succeeded"}"""));
        var machine = Assert.Single(ResourceGraphDiscoveryService.Correlate(Guid.NewGuid(), [parent, child], DateTimeOffset.UtcNow));
        Assert.Equal(MachineKind.AzureLocalVm, machine.Kind);
        Assert.Equal("local1", machine.Name);
    }

    [Fact]
    public void StableIdentityId_IsCaseInsensitiveAndDeterministic()
    {
        Assert.Equal(AzureCliIdentityService.StableIdentityId("ADMIN@EXAMPLE.COM"), AzureCliIdentityService.StableIdentityId(" admin@example.com "));
    }

    private static ResourceGraphDiscoveryService.ArgResource Resource(
        string id, string name, string type, string resourceGroup, JsonElement properties,
        JsonElement tags = default, JsonElement sku = default) =>
        new(id, name, type, "t", "s", resourceGroup, "eastus", null, tags, sku, properties);

    private static JsonElement Props(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static JsonElement Tags(string json) => Props(json);
}
