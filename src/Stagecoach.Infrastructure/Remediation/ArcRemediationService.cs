using Stagecoach.Core;

namespace Stagecoach.Infrastructure.Remediation;

public sealed class ArcRemediationService(IAzureCliRunner cli) : IArcRemediationService
{
    private const string Operation = "Create or update the Microsoft.Azure.OpenSSH WindowsOpenSSH Arc extension (automatic minor upgrades enabled).";

    public RemediationAction PreviewOpenSshInstallation(MachineRecord machine, AzureAccessPath accessPath)
    {
        if (machine.Kind is not (MachineKind.ArcServer or MachineKind.AzureLocalVm) || !machine.IsWindows)
            throw new InvalidOperationException("OpenSSH remediation is only available for Windows Arc-enabled machines.");
        return new RemediationAction(
            "arc-windows-openssh",
            $"Prepare {machine.Name} for Arc SSH/RDP",
            "This Azure write deploys the Microsoft WindowsOpenSSH Arc extension. It does not change role assignments, firewall rules, or stored credentials.",
            machine.ResourceId,
            accessPath.IdentityId,
            [Operation],
            RequiresAzureWrite: true);
    }

    public async Task ApplyOpenSshInstallationAsync(
        RemediationAction action,
        MachineRecord machine,
        AzureAccessPath accessPath,
        AzureIdentityProfile identity,
        CancellationToken cancellationToken = default)
    {
        if (action.Id != "arc-windows-openssh" || action.TargetResourceId != machine.ResourceId ||
            action.AzureIdentityId != identity.Id || accessPath.IdentityId != identity.Id)
            throw new InvalidOperationException("The remediation confirmation no longer matches the selected machine and identity.");

        var result = await cli.RunAsync(identity.AzureConfigDirectory,
        [
            "connectedmachine", "extension", "create",
            "--subscription", accessPath.SubscriptionId,
            "--resource-group", machine.ResourceGroup,
            "--machine-name", machine.Name,
            "--location", machine.Location,
            "--name", "WindowsOpenSSH",
            "--publisher", "Microsoft.Azure.OpenSSH",
            "--type", "WindowsOpenSSH",
            "--type-handler-version", "3.0.1.0",
            "--auto-upgrade-minor-version", "true",
            "--enable-auto-upgrade", "true",
            "--output", "none",
        ], cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException("Azure did not complete the WindowsOpenSSH extension deployment. Verify Contributor access, Arc agent health, and extension policy.");
    }
}
