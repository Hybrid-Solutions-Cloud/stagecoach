using Stagecoach.Core;
using Stagecoach.Infrastructure.Azure;

namespace Stagecoach.Tests;

/// <summary>
/// Azure Resource Graph answers only for the tenant the current token belongs to. Subscriptions from
/// any other tenant are dropped from the request — silently, when they are mixed in with valid ones.
/// <para>
/// Discovery therefore has to query one tenant at a time, selecting a subscription in that tenant
/// first so the CLI holds a token for it. Sending every subscription in one request meant an account
/// with access across several tenants only ever saw the machines in whichever tenant happened to be
/// active: one operator had five machines and Stagecoach showed one.
/// </para>
/// </summary>
public sealed class CrossTenantDiscoveryTests
{
    [Fact]
    public async Task EachTenantIsSelectedAndQueriedSeparately()
    {
        var cli = new RecordingCli();
        var discovery = new ResourceGraphDiscoveryService(cli);
        var identity = new AzureIdentityProfile(
            Guid.NewGuid(), "Lab", "operator@example.com", "C:\\isolated", AuthenticationState.Ready, DateTimeOffset.UtcNow);

        var subscriptions = new[]
        {
            new SubscriptionScope(identity.Id, "tenant-a", "sub-a1", "A1", "Enabled", true),
            new SubscriptionScope(identity.Id, "tenant-a", "sub-a2", "A2", "Enabled", true),
            new SubscriptionScope(identity.Id, "tenant-b", "sub-b1", "B1", "Enabled", true),
        };

        await discovery.DiscoverAsync(identity, subscriptions, TestContext.Current.CancellationToken);

        // One "account set" per tenant, naming a subscription that belongs to it.
        var activations = cli.Calls.Where(call => call.Args.Count > 1 && call.Args[0] == "account").ToArray();
        Assert.Equal(2, activations.Length);
        Assert.Contains(activations, call => call.Args.Contains("sub-a1"));
        Assert.Contains(activations, call => call.Args.Contains("sub-b1"));

        // And one query per tenant, never a single request mixing both tenants' subscriptions.
        var queries = cli.Calls.Where(call => call.Args.Count > 1 && call.Args[0] == "graph").ToArray();
        Assert.Equal(2, queries.Length);
        foreach (var query in queries)
        {
            var named = query.Args.Where(argument => argument.StartsWith("sub-", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(named);
            Assert.True(
                named.All(name => name.StartsWith("sub-a", StringComparison.Ordinal)) ||
                named.All(name => name.StartsWith("sub-b", StringComparison.Ordinal)),
                "A single Resource Graph request must never mix subscriptions from two tenants.");
        }
    }

    [Fact]
    public async Task OneTenantFailingDoesNotLoseTheOthers()
    {
        var cli = new RecordingCli { FailQueriesForSubscriptionPrefix = "sub-b" };
        var discovery = new ResourceGraphDiscoveryService(cli);
        var identity = new AzureIdentityProfile(
            Guid.NewGuid(), "Lab", "operator@example.com", "C:\\isolated", AuthenticationState.Ready, DateTimeOffset.UtcNow);

        var result = await discovery.DiscoverAsync(
            identity,
            [
                new SubscriptionScope(identity.Id, "tenant-a", "sub-a1", "A1", "Enabled", true),
                new SubscriptionScope(identity.Id, "tenant-b", "sub-b1", "B1", "Enabled", true),
            ],
            TestContext.Current.CancellationToken);

        // The working tenant still produced a result, and the failure is reported rather than hidden.
        Assert.Contains(result.SafeWarnings, warning => warning.Contains("tenant-b", StringComparison.Ordinal));
    }

    private sealed class RecordingCli : IAzureCliRunner
    {
        public List<(string Config, IReadOnlyList<string> Args)> Calls { get; } = [];
        public string? FailQueriesForSubscriptionPrefix { get; init; }

        public Task<CommandResult> RunAsync(
            string azureConfigDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Calls.Add((azureConfigDirectory, arguments));
            if (arguments.Count > 0 && arguments[0] == "graph" && FailQueriesForSubscriptionPrefix is { } prefix &&
                arguments.Any(argument => argument.StartsWith(prefix, StringComparison.Ordinal)))
            {
                return Task.FromResult(new CommandResult(1, string.Empty, "ERROR: NoValidSubscriptionsInQueryRequest"));
            }

            return Task.FromResult(new CommandResult(0, """{"data":[]}""", string.Empty));
        }

        public Task<CommandResult> RunInteractiveAsync(
            string azureConfigDirectory, IReadOnlyList<string> arguments,
            IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IManagedCommand> StartBackgroundAsync(
            string azureConfigDirectory, IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
