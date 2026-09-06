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

        // Not MissingPrerequisite: the endpoint is the prerequisite, and it is there.
        Assert.Equal(ReadinessState.InteractionRequired, arcRdp.Readiness);
        Assert.Contains("endpoint", arcRdp.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithoutAnEndpointTheMachineIsStillOfferedRatherThanDeclaredUnusable()
    {
        var machines = ResourceGraphDiscoveryService.Correlate(
            Guid.NewGuid(), [Machine()], DateTimeOffset.UtcNow);

        var machine = Assert.Single(machines);
        var arcRdp = Assert.Single(machine.AccessPaths, path => path.Route == ConnectionRouteKind.ArcRdp);
        Assert.Equal(ReadinessState.MissingPrerequisite, arcRdp.Readiness);

        // The wording no longer claims to know that SSH is absent — only that no endpoint was seen.
        Assert.DoesNotContain("readiness was not detected", arcRdp.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static ResourceGraphDiscoveryService.ArgResource Machine() => new(
        MachineId, "mgt-sdr-jmp-01", "microsoft.hybridcompute/machines", "t", "s", "rg", "eastus",
        null, default, default, Props("""{"osName":"windows","status":"Connected"}"""));

    private static ResourceGraphDiscoveryService.ArgResource Endpoint() => new(
        $"{MachineId}/providers/Microsoft.HybridConnectivity/endpoints/default", "default",
        "microsoft.hybridconnectivity/endpoints", "t", "s", "rg", "eastus",
        null, default, default, Props("""{"provisioningState":"Succeeded","type":"default"}"""));

    private static JsonElement Props(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
