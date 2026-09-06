using System.Text.Json;
using Stagecoach.Core;
using Stagecoach.Infrastructure.Azure;

namespace Stagecoach.Tests;

/// <summary>
/// What <c>az ssh arc</c> connects through is the machine's Microsoft.HybridConnectivity endpoint,
/// not any particular extension.
/// <para>
/// Readiness used to require a <c>WindowsOpenSSH</c> Arc extension, so a machine that connects
/// perfectly well from a terminal was reported as "Windows OpenSSH/Arc SSH readiness was not
/// detected". Recent Windows Server ships OpenSSH itself: the extension is one way to arrive at SSH
/// and never a requirement. Taken from a real machine that had the endpoint and no such extension.
/// </para>
/// </summary>
public sealed class ArcSshReadinessTests
{
    private const string MachineId =
        "/subscriptions/s/resourceGroups/rg/providers/Microsoft.HybridCompute/machines/mgt-sdr-jmp-01";

    [Fact]
    public void AConnectedArcMachineWithAnSshEndpointIsReachable()
    {
        var machines = ResourceGraphDiscoveryService.Correlate(
            Guid.NewGuid(),
            [
                Machine(),
                Endpoint(),
            ],
            DateTimeOffset.UtcNow);

        var machine = Assert.Single(machines);
        var arcRdp = Assert.Single(machine.AccessPaths, path => path.Route == ConnectionRouteKind.ArcRdp);

        // Not MissingPrerequisite. The endpoint is not what proves this — Resource Graph never
        // returns one — but a machine that has one must certainly not be reported as unusable.
        Assert.Equal(ReadinessState.InteractionRequired, arcRdp.Readiness);
    }

    [Fact]
    public void AConnectedArcMachineIsReachableWithNoExtensionsAtAll()
    {
        // Resource Graph does not expose hybrid connectivity endpoints, and recent Windows Server
        // ships OpenSSH itself, so neither can be used as evidence. A connected agent is the whole
        // prerequisite; anything further is settled by attempting the connection.
        var machines = ResourceGraphDiscoveryService.Correlate(
            Guid.NewGuid(), [Machine()], DateTimeOffset.UtcNow);

        var machine = Assert.Single(machines);
        var arcRdp = Assert.Single(machine.AccessPaths, path => path.Route == ConnectionRouteKind.ArcRdp);
        Assert.Equal(ReadinessState.InteractionRequired, arcRdp.Readiness);
        Assert.DoesNotContain("was not detected", arcRdp.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no arc ssh endpoint", arcRdp.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnArcMachineWhoseAgentIsDisconnectedIsReportedOffline()
    {
        var machines = ResourceGraphDiscoveryService.Correlate(
            Guid.NewGuid(), [Machine(status: "Disconnected")], DateTimeOffset.UtcNow);

        var machine = Assert.Single(machines);
        var arcRdp = Assert.Single(machine.AccessPaths, path => path.Route == ConnectionRouteKind.ArcRdp);
        Assert.Equal(ReadinessState.Offline, arcRdp.Readiness);
    }

    private static ResourceGraphDiscoveryService.ArgResource Machine(string status = "Connected") => new(
        MachineId, "mgt-sdr-jmp-01", "microsoft.hybridcompute/machines", "t", "s", "rg", "eastus",
        null, default, default, Props($$"""{"osName":"windows","status":"{{status}}"}"""));

    private static ResourceGraphDiscoveryService.ArgResource Endpoint() => new(
        $"{MachineId}/providers/Microsoft.HybridConnectivity/endpoints/default", "default",
        "microsoft.hybridconnectivity/endpoints", "t", "s", "rg", "eastus",
        null, default, default, Props("""{"provisioningState":"Succeeded","type":"default"}"""));

    private static JsonElement Props(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
