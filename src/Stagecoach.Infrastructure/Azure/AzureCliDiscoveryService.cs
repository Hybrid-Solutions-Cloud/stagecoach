using System.Diagnostics;
using System.Text.Json;
using Stagecoach.Core.Interfaces;
using Stagecoach.Core.Models;

namespace Stagecoach.Infrastructure.Azure;

public class AzureCliDiscoveryService : IDiscoveryService
{
    public async Task<IReadOnlyList<StagecoachIdentity>> GetIdentitiesAsync(CancellationToken cancellationToken = default)
    {
        var json = await RunAzAsync("account list -o json", cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<StagecoachIdentity>();

        using var doc = JsonDocument.Parse(json);
        var identities = new Dictionary<string, StagecoachIdentity>();

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var user = element.GetProperty("user").GetProperty("name").GetString() ?? "Unknown";
            var tenantId = element.GetProperty("tenantId").GetString() ?? "";
            var subId = element.GetProperty("id").GetString() ?? "";
            var subName = element.GetProperty("name").GetString() ?? "";
            var isDefault = element.TryGetProperty("isDefault", out var isDefProp) && isDefProp.GetBoolean();

            if (!identities.TryGetValue(user, out var identity))
            {
                identity = new StagecoachIdentity { AccountName = user };
                identities[user] = identity;
            }

            var tenant = identity.Tenants.FirstOrDefault(t => t.TenantId == tenantId);
            if (tenant == null)
            {
                tenant = new StagecoachTenant
                {
                    TenantId = tenantId,
                    TenantName = tenantId
                };
                identity.Tenants.Add(tenant);
            }

            tenant.Subscriptions.Add(new StagecoachSubscription
            {
                SubscriptionId = subId,
                SubscriptionName = subName,
                IsDefault = isDefault
            });
        }

        return identities.Values.ToList();
    }

    public async Task<IReadOnlyList<StagecoachMachine>> DiscoverEstateAsync(IEnumerable<string>? tenantIds = null, CancellationToken cancellationToken = default)
    {
        var kql = @"
Resources
| where type =~ 'microsoft.compute/virtualmachines' or type =~ 'microsoft.hybridcompute/machines'
| extend kind = iff(type =~ 'microsoft.compute/virtualmachines', 'AzureVM', 'ArcServer'),
         osType = coalesce(tostring(properties.storageProfile.osDisk.osType), tostring(properties.osType), tostring(properties.osName)),
         osName = coalesce(tostring(properties.osName), tostring(properties.osProfile.computerName), name),
         powerState = coalesce(tostring(properties.extended.instanceView.powerState.displayStatus), tostring(properties.status)),
         agentStatus = tostring(properties.status),
         domainName = coalesce(tostring(properties.domainName), '')
| project id, name, resourceGroup, subscriptionId, tenantId, location, kind, osType, osName, powerState, agentStatus, domainName, tags
";

        var machines = new List<StagecoachMachine>();
        var command = $"graph query -q \"{kql.Replace("\r\n", " ").Replace("\n", " ")}\" -o json";
        var json = await RunAzAsync(command, cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return machines;

        using var doc = JsonDocument.Parse(json);
        var dataArray = doc.RootElement.TryGetProperty("data", out var dataProp) ? dataProp : doc.RootElement;

        if (dataArray.ValueKind != JsonValueKind.Array) return machines;

        foreach (var el in dataArray.EnumerateArray())
        {
            var id = el.GetProperty("id").GetString() ?? "";
            var name = el.GetProperty("name").GetString() ?? "";
            var rg = el.GetProperty("resourceGroup").GetString() ?? "";
            var sub = el.GetProperty("subscriptionId").GetString() ?? "";
            var ten = el.GetProperty("tenantId").GetString() ?? "";
            var loc = el.GetProperty("location").GetString() ?? "";
            var kindStr = el.GetProperty("kind").GetString() ?? "AzureVM";
            var osType = el.TryGetProperty("osType", out var osT) ? osT.GetString() ?? "Windows" : "Windows";
            var osName = el.TryGetProperty("osName", out var osN) ? osN.GetString() ?? "" : "";
            var power = el.TryGetProperty("powerState", out var pw) ? pw.GetString() ?? "Unknown" : "Unknown";
            var agent = el.TryGetProperty("agentStatus", out var ag) ? ag.GetString() ?? "" : "";
            var domain = el.TryGetProperty("domainName", out var dm) ? dm.GetString() ?? "" : "";

            var kind = kindStr == "ArcServer" ? TargetKind.ArcServer : TargetKind.AzureVM;
            var domType = string.IsNullOrWhiteSpace(domain) || domain.Equals("WORKGROUP", StringComparison.OrdinalIgnoreCase) || domain.Equals(name, StringComparison.OrdinalIgnoreCase)
                ? DomainType.Workgroup
                : DomainType.ActiveDirectory;

            var tags = new Dictionary<string, string>();
            if (el.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in tagsProp.EnumerateObject())
                {
                    tags[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            machines.Add(new StagecoachMachine
            {
                Id = id,
                Name = name,
                ResourceGroup = rg,
                SubscriptionId = sub,
                TenantId = ten,
                Location = loc,
                Kind = kind,
                OsType = osType,
                OsName = osName,
                PowerState = power,
                AgentStatus = agent,
                DomainName = domain,
                DomainType = domType,
                Tags = tags
            });
        }

        return machines;
    }

    public async Task<bool> TriggerInteractiveLoginAsync(string? tenantId = null, string? usernameHint = null, CancellationToken cancellationToken = default)
    {
        var args = "login";
        if (!string.IsNullOrWhiteSpace(tenantId)) args += $" --tenant {tenantId}";
        if (!string.IsNullOrWhiteSpace(usernameHint)) args += $" --username {usernameHint}";

        var psi = new ProcessStartInfo
        {
            FileName = "az",
            Arguments = args,
            UseShellExecute = true
        };

        var process = Process.Start(psi);
        if (process == null) return false;

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
    }

    private static async Task<string> RunAzAsync(string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "az",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return string.Empty;

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return output;
    }
}
