using System.Text.Json;
using Stagecoach.Core;

namespace Stagecoach.Infrastructure.Azure;

public sealed class ResourceGraphDiscoveryService(IAzureCliRunner cli) : IEstateDiscoveryService
{
    private const string Query = """
        Resources
        | where type in~ (
            'microsoft.compute/virtualmachines',
            'microsoft.compute/virtualmachines/extensions',
            'microsoft.hybridcompute/machines',
            'microsoft.hybridcompute/machines/extensions',
            'microsoft.azurestackhci/virtualmachineinstances',
            'microsoft.network/networkinterfaces',
            'microsoft.network/publicipaddresses',
            'microsoft.network/virtualnetworks/virtualnetworkpeerings',
            'microsoft.network/bastionhosts',
            'microsoft.hybridconnectivity/endpoints')
        | project id, name, type, tenantId, subscriptionId, resourceGroup, location, kind, tags, sku, properties
        """;

    /// <summary>
    /// The query as a single line. On Windows the Azure CLI is <c>az.cmd</c>, a batch file, so the
    /// command line is re-parsed by cmd.exe — and a newline inside an argument truncates it there.
    /// Passing the query with line breaks silently dropped the <c>where</c> clause, so Resource
    /// Graph returned every resource in scope instead of the ten types wanted, paged through
    /// hundreds of thousands of rows, and eventually failed.
    /// </summary>
    private static readonly string SingleLineQuery = string.Join(
        ' ',
        Query.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public async Task<DiscoveryResult> DiscoverAsync(
        AzureIdentityProfile identity,
        IReadOnlyList<SubscriptionScope> subscriptions,
        CancellationToken cancellationToken = default)
    {
        var selected = subscriptions
            .Where(item => item.IsEnabled && string.Equals(item.State, "Enabled", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.SubscriptionId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0)
            return new DiscoveryResult(identity.Id, [], DateTimeOffset.UtcNow, ["No enabled subscriptions are selected."]);

        var resources = new List<ArgResource>();
        var warnings = new List<string>();
        foreach (var batch in selected.Chunk(100))
        {
            string? skipToken = null;
            do
            {
                // The KQL goes in --graph-query. "--query" is Azure CLI's *global* JMESPath output
                // filter, so passing the query there failed every single run with
                // "invalid jmespath_type value" and the estate could never populate.
                var arguments = new List<string> { "graph", "query", "--graph-query", SingleLineQuery, "--first", "1000", "--output", "json", "--subscriptions" };
                arguments.AddRange(batch);
                if (!string.IsNullOrWhiteSpace(skipToken))
                {
                    arguments.Add("--skip-token");
                    arguments.Add(skipToken);
                }

                var result = await cli.RunAsync(identity.AzureConfigDirectory, arguments, cancellationToken);
                if (!result.Succeeded)
                {
                    var detail = result.StandardError
                        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .LastOrDefault(line => line.Length > 0 && !line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase));
                    throw new InvalidOperationException(
                        "Azure Resource Graph discovery failed for this identity." +
                        (detail is null ? string.Empty : $" Azure CLI reported: {detail}") +
                        " Review its selected subscriptions and permissions.");
                }
                (var page, skipToken) = ParsePage(result.StandardOutput);
                resources.AddRange(page);
            } while (!string.IsNullOrWhiteSpace(skipToken));
        }

        var machines = Correlate(identity.Id, resources, DateTimeOffset.UtcNow, warnings);
        return new DiscoveryResult(identity.Id, machines, DateTimeOffset.UtcNow, warnings);
    }

    internal static (IReadOnlyList<ArgResource> Resources, string? SkipToken) ParsePage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var data = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("data", out var dataProperty) ? dataProperty : default;
        if (data.ValueKind != JsonValueKind.Array)
            throw new JsonException("Azure Resource Graph returned an unexpected response shape.");

        var resources = new List<ArgResource>();
        foreach (var item in data.EnumerateArray())
        {
            var id = GetString(item, "id");
            var type = GetString(item, "type");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type)) continue;
            resources.Add(new ArgResource(
                id, GetString(item, "name") ?? string.Empty, type,
                GetString(item, "tenantId") ?? string.Empty,
                GetString(item, "subscriptionId") ?? string.Empty,
                GetString(item, "resourceGroup") ?? string.Empty,
                GetString(item, "location") ?? string.Empty,
                GetString(item, "kind"),
                item.TryGetProperty("tags", out var tags) ? tags.Clone() : default,
                item.TryGetProperty("sku", out var sku) ? sku.Clone() : default,
                item.TryGetProperty("properties", out var properties) ? properties.Clone() : default));
        }

        var token = root.ValueKind == JsonValueKind.Object
            ? GetString(root, "skip_token") ?? GetString(root, "$skipToken") ?? GetString(root, "skipToken")
            : null;
        return (resources, token);
    }

    internal static IReadOnlyList<MachineRecord> Correlate(
        Guid identityId,
        IReadOnlyList<ArgResource> resources,
        DateTimeOffset discoveredAt,
        ICollection<string>? warnings = null)
    {
        var publicIps = resources
            .Where(item => TypeIs(item, "microsoft.network/publicipaddresses"))
            .ToDictionary(item => Normalize(item.Id), item => JsonPathString(item.Properties, "ipAddress"), StringComparer.OrdinalIgnoreCase);
        var nicsByVm = BuildNicIndex(resources, publicIps);
        var peerings = BuildPeeringIndex(resources);
        var bastions = BuildBastions(resources);
        var extensions = resources.Where(item => item.Type.EndsWith("/extensions", StringComparison.OrdinalIgnoreCase)).ToArray();
        var localChildren = resources.Where(item => TypeIs(item, "microsoft.azurestackhci/virtualmachineinstances"))
            .ToDictionary(item => ParentHybridMachineId(item.Id), StringComparer.OrdinalIgnoreCase);

        var results = new List<MachineRecord>();
        foreach (var resource in resources.Where(IsMachineResource))
        {
            if (TypeIs(resource, "microsoft.hybridcompute/machines") && localChildren.ContainsKey(Normalize(resource.Id)))
                continue;

            var kind = TypeIs(resource, "microsoft.compute/virtualmachines")
                ? MachineKind.AzureVm
                : TypeIs(resource, "microsoft.azurestackhci/virtualmachineinstances")
                    ? MachineKind.AzureLocalVm
                    : MachineKind.ArcServer;
            var canonicalId = kind == MachineKind.AzureLocalVm ? ParentHybridMachineId(resource.Id) : Normalize(resource.Id);
            var parent = kind == MachineKind.AzureLocalVm
                ? resources.FirstOrDefault(item => string.Equals(Normalize(item.Id), canonicalId, StringComparison.OrdinalIgnoreCase))
                : null;
            var effective = parent ?? resource;
            var name = kind == MachineKind.AzureLocalVm ? ResourceName(canonicalId) : effective.Name;
            var osName = FirstNonEmpty(
                JsonPathString(resource.Properties, "osName"),
                JsonPathString(effective.Properties, "osName"),
                JsonPathString(resource.Properties, "storageProfile", "osDisk", "osType"),
                JsonPathString(resource.Properties, "osDisk", "osType"),
                JsonPathString(effective.Properties, "storageProfile", "osDisk", "osType")) ?? "Unknown";
            var os = ParseOperatingSystem(osName, JsonPathString(effective.Properties, "osType"));
            var network = nicsByVm.GetValueOrDefault(canonicalId);
            var tags = ReadTags(effective.Tags);
            var domain = FindDomain(tags);
            var powerState = FirstNonEmpty(
                JsonPathString(resource.Properties, "provisioningState"),
                JsonPathString(effective.Properties, "extended", "instanceView", "powerState", "displayStatus"),
                JsonPathString(effective.Properties, "status")) ?? "Unknown";
            var agentState = JsonPathString(effective.Properties, "status") ?? string.Empty;
            var paths = BuildAccessPaths(identityId, effective, kind, os, network, bastions, peerings, extensions, agentState);
            if (paths.Count == 0)
            {
                warnings?.Add($"{name}: no supported connection route was discovered.");
                paths.Add(new AzureAccessPath(identityId, effective.TenantId, effective.SubscriptionId,
                    os == OperatingSystemKind.Linux ? ConnectionRouteKind.DirectSsh : ConnectionRouteKind.DirectRdp,
                    ReadinessState.Unsupported, "No direct, Bastion, or Arc route was discovered."));
            }

            results.Add(new MachineRecord(
                canonicalId, name, kind, os, osName, effective.ResourceGroup, effective.Location,
                powerState, agentState, network?.PrivateIp, network?.PublicIp, network?.VirtualNetworkId,
                domain, tags, MarkPreferred(paths), discoveredAt));
        }
        return results.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static List<AzureAccessPath> BuildAccessPaths(
        Guid identityId,
        ArgResource machine,
        MachineKind kind,
        OperatingSystemKind os,
        NicInfo? network,
        IReadOnlyList<BastionInfo> bastions,
        IReadOnlyDictionary<string, HashSet<string>> peerings,
        IReadOnlyList<ArgResource> extensions,
        string agentState)
    {
        var paths = new List<AzureAccessPath>();
        if (kind is MachineKind.ArcServer or MachineKind.AzureLocalVm)
        {
            var connected = agentState.Contains("connected", StringComparison.OrdinalIgnoreCase);
            var hasSsh = HasExtension(machine.Id, extensions, "WindowsOpenSSH") || os == OperatingSystemKind.Linux;
            var readiness = !connected ? ReadinessState.Offline : hasSsh ? ReadinessState.InteractionRequired : ReadinessState.MissingPrerequisite;
            var reason = !connected ? "Azure Arc agent is not connected."
                : hasSsh ? "Arc relay is available; target authentication may be required."
                : "Windows OpenSSH/Arc SSH readiness was not detected.";
            if (os == OperatingSystemKind.Windows)
                paths.Add(new AzureAccessPath(identityId, machine.TenantId, machine.SubscriptionId,
                    ConnectionRouteKind.ArcRdp, readiness, reason));
            paths.Add(new AzureAccessPath(identityId, machine.TenantId, machine.SubscriptionId,
                ConnectionRouteKind.ArcSsh, readiness, reason));
            return paths;
        }

        var bastion = FindBastion(network?.VirtualNetworkId, bastions, peerings);
        if (bastion is not null)
        {
            if (os == OperatingSystemKind.Windows)
            {
                paths.Add(new AzureAccessPath(identityId, machine.TenantId, machine.SubscriptionId,
                    ConnectionRouteKind.BastionTunnelRdp,
                    bastion.SupportsTunneling ? ReadinessState.Ready : ReadinessState.MissingPrerequisite,
                    bastion.SupportsTunneling ? $"Reachable through Bastion {bastion.Name}." : "Bastion native-client tunneling is not enabled or the SKU is unsupported.",
                    bastion.Id));
                paths.Add(new AzureAccessPath(identityId, machine.TenantId, machine.SubscriptionId,
                    ConnectionRouteKind.BastionRdp,
                    bastion.SupportsTunneling ? ReadinessState.InteractionRequired : ReadinessState.MissingPrerequisite,
                    "Microsoft Entra/MFA behavior is evaluated when the native RDP client starts.", bastion.Id));
            }
            else if (os == OperatingSystemKind.Linux)
            {
                paths.Add(new AzureAccessPath(identityId, machine.TenantId, machine.SubscriptionId,
                    ConnectionRouteKind.BastionSsh,
                    bastion.SupportsTunneling ? ReadinessState.InteractionRequired : ReadinessState.MissingPrerequisite,
                    bastion.SupportsTunneling ? $"Reachable through Bastion {bastion.Name}." : "Bastion native-client tunneling is unavailable.",
                    bastion.Id));
            }
        }

        if (!string.IsNullOrWhiteSpace(network?.PublicIp) || !string.IsNullOrWhiteSpace(network?.PrivateIp))
        {
            var hasPublicAddress = !string.IsNullOrWhiteSpace(network?.PublicIp);
            paths.Add(new AzureAccessPath(identityId, machine.TenantId, machine.SubscriptionId,
                os == OperatingSystemKind.Linux ? ConnectionRouteKind.DirectSsh : ConnectionRouteKind.DirectRdp,
                hasPublicAddress ? ReadinessState.Ready : ReadinessState.InteractionRequired,
                hasPublicAddress
                    ? "A public IP address is available; network policy is validated at connection time."
                    : "A private address is available; workstation VPN, ExpressRoute, peering, and firewall reachability are validated at connection time."));
        }
        return paths;
    }

    private static IReadOnlyList<AzureAccessPath> MarkPreferred(List<AzureAccessPath> paths)
    {
        var preferred = paths
            .OrderBy(item => item.Readiness == ReadinessState.Ready ? 0 : item.Readiness == ReadinessState.InteractionRequired ? 1 : 2)
            .ThenBy(item => RouteRank(item.Route))
            .First();
        return paths.Select(item => item == preferred ? item with { IsPreferred = true } : item).ToArray();
    }

    private static int RouteRank(ConnectionRouteKind route) => route switch
    {
        ConnectionRouteKind.BastionTunnelRdp => 0,
        ConnectionRouteKind.ArcRdp => 1,
        ConnectionRouteKind.BastionRdp => 2,
        ConnectionRouteKind.BastionSsh => 3,
        ConnectionRouteKind.ArcSsh => 4,
        _ => 5,
    };

    private static Dictionary<string, NicInfo> BuildNicIndex(IEnumerable<ArgResource> resources, IReadOnlyDictionary<string, string?> publicIps)
    {
        var result = new Dictionary<string, NicInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var nic in resources.Where(item => TypeIs(item, "microsoft.network/networkinterfaces")))
        {
            var vmId = JsonPathString(nic.Properties, "virtualMachine", "id");
            if (string.IsNullOrWhiteSpace(vmId)) continue;
            var configurations = JsonPath(nic.Properties, "ipConfigurations");
            if (configurations.ValueKind != JsonValueKind.Array) continue;
            foreach (var configuration in configurations.EnumerateArray())
            {
                var properties = JsonPath(configuration, "properties");
                var privateIp = JsonPathString(properties, "privateIPAddress");
                var subnetId = JsonPathString(properties, "subnet", "id");
                var publicId = JsonPathString(properties, "publicIPAddress", "id");
                var publicIp = !string.IsNullOrWhiteSpace(publicId) ? publicIps.GetValueOrDefault(Normalize(publicId)) : null;
                result[Normalize(vmId)] = new NicInfo(privateIp, publicIp, VirtualNetworkFromSubnet(subnetId));
                break;
            }
        }
        return result;
    }

    private static Dictionary<string, HashSet<string>> BuildPeeringIndex(IEnumerable<ArgResource> resources)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var peering in resources.Where(item => TypeIs(item, "microsoft.network/virtualnetworks/virtualnetworkpeerings")))
        {
            if (!string.Equals(JsonPathString(peering.Properties, "peeringState"), "Connected", StringComparison.OrdinalIgnoreCase)) continue;
            var local = ParentResourceId(peering.Id, "/virtualNetworkPeerings/");
            var remote = JsonPathString(peering.Properties, "remoteVirtualNetwork", "id");
            if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(remote)) continue;
            AddEdge(result, Normalize(local), Normalize(remote));
            AddEdge(result, Normalize(remote), Normalize(local));
        }
        return result;
    }

    private static List<BastionInfo> BuildBastions(IEnumerable<ArgResource> resources)
    {
        var result = new List<BastionInfo>();
        foreach (var bastion in resources.Where(item => TypeIs(item, "microsoft.network/bastionhosts")))
        {
            var configs = JsonPath(bastion.Properties, "ipConfigurations");
            string? vnet = null;
            if (configs.ValueKind == JsonValueKind.Array)
            {
                var first = configs.EnumerateArray().FirstOrDefault();
                vnet = VirtualNetworkFromSubnet(JsonPathString(first, "properties", "subnet", "id"));
            }
            var sku = JsonPathString(bastion.Sku, "name") ?? string.Empty;
            var tunneling = JsonPathBool(bastion.Properties, "enableTunneling") == true &&
                !sku.Equals("Basic", StringComparison.OrdinalIgnoreCase) &&
                !sku.Equals("Developer", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(vnet)) result.Add(new BastionInfo(Normalize(bastion.Id), bastion.Name, vnet, tunneling));
        }
        return result;
    }

    private static BastionInfo? FindBastion(string? vnet, IReadOnlyList<BastionInfo> bastions, IReadOnlyDictionary<string, HashSet<string>> peerings)
    {
        if (string.IsNullOrWhiteSpace(vnet)) return null;
        var normalized = Normalize(vnet);
        return bastions.FirstOrDefault(item => string.Equals(item.VirtualNetworkId, normalized, StringComparison.OrdinalIgnoreCase))
            ?? bastions.FirstOrDefault(item => peerings.GetValueOrDefault(normalized)?.Contains(item.VirtualNetworkId) == true);
    }

    private static bool HasExtension(string resourceId, IEnumerable<ArgResource> extensions, string value) =>
        extensions.Any(item => item.Id.StartsWith(resourceId + "/", StringComparison.OrdinalIgnoreCase) &&
            (item.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
             (JsonPathString(item.Properties, "type")?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false)));

    private static bool IsMachineResource(ArgResource resource) =>
        TypeIs(resource, "microsoft.compute/virtualmachines") ||
        TypeIs(resource, "microsoft.hybridcompute/machines") ||
        TypeIs(resource, "microsoft.azurestackhci/virtualmachineinstances");

    private static OperatingSystemKind ParseOperatingSystem(params string?[] values)
    {
        var joined = string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (joined.Contains("windows", StringComparison.OrdinalIgnoreCase)) return OperatingSystemKind.Windows;
        if (joined.Contains("linux", StringComparison.OrdinalIgnoreCase)) return OperatingSystemKind.Linux;
        return OperatingSystemKind.Unknown;
    }

    private static IReadOnlyDictionary<string, string> ReadTags(JsonElement tags)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tags.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in tags.EnumerateObject()) result[property.Name] = property.Value.ToString();
        return result;
    }

    private static string? FindDomain(IReadOnlyDictionary<string, string> tags)
    {
        foreach (var key in new[] { "stagecoach-domain", "ad-domain", "domain", "Domain" })
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        return null;
    }

    private static JsonElement JsonPath(JsonElement element, params string[] path)
    {
        foreach (var segment in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element)) return default;
        }
        return element;
    }

    private static string? JsonPathString(JsonElement element, params string[] path)
    {
        var value = JsonPath(element, path);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : value.ToString();
    }

    private static bool? JsonPathBool(JsonElement element, params string[] path)
    {
        var value = JsonPath(element, path);
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    }

    private static string? GetString(JsonElement element, string property) => JsonPathString(element, property);
    private static bool TypeIs(ArgResource resource, string type) => string.Equals(resource.Type, type, StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string value) => value.Trim().TrimEnd('/').ToLowerInvariant();
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? VirtualNetworkFromSubnet(string? subnetId) =>
        string.IsNullOrWhiteSpace(subnetId) ? null : ParentResourceId(subnetId, "/subnets/") is { } value ? Normalize(value) : null;

    private static string ParentHybridMachineId(string resourceId) =>
        Normalize(ParentResourceId(resourceId, "/providers/microsoft.azurestackhci/virtualmachineinstances/") ?? resourceId);

    private static string? ParentResourceId(string value, string delimiter)
    {
        var index = value.IndexOf(delimiter, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? null : value[..index];
    }

    private static string ResourceName(string resourceId)
    {
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? resourceId : parts[^1];
    }

    private static void AddEdge(Dictionary<string, HashSet<string>> graph, string source, string target)
    {
        if (!graph.TryGetValue(source, out var values)) graph[source] = values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        values.Add(target);
    }

    internal sealed record ArgResource(
        string Id, string Name, string Type, string TenantId, string SubscriptionId,
        string ResourceGroup, string Location, string? Kind, JsonElement Tags, JsonElement Sku, JsonElement Properties);

    private sealed record NicInfo(string? PrivateIp, string? PublicIp, string? VirtualNetworkId);
    private sealed record BastionInfo(string Id, string Name, string VirtualNetworkId, bool SupportsTunneling);
}
