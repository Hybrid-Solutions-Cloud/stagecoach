using Stagecoach.Core;
using Stagecoach.Infrastructure.Readiness;

namespace Stagecoach.Tests;

/// <summary>
/// Stagecoach runs the Azure CLI hidden, with its input redirected. Any command the CLI decides to
/// ask a question about therefore fails with "EOF when reading a line" — no prompt is ever seen, and
/// the error names nothing that went wrong.
/// <para>
/// This has now caused three separate defects: the account picker
/// (<c>core.login_experience_v2</c>) broke the owner sign-in, and the extension-install prompt
/// (<c>extension.use_dynamic_install</c>) made <c>az network bastion</c> fail on a machine whose CLI
/// supported it perfectly well — which is why connecting through Bastion silently did nothing.
/// </para>
/// <para>
/// Every profile Stagecoach runs commands in must be configured never to prompt.
/// </para>
/// </summary>
public sealed class AzureCliPromptSafetyTests
{
    [Fact]
    public async Task ReadinessConfiguresItsProfileToNeverPrompt()
    {
        var cli = new RecordingCli();
        var readiness = new WorkstationReadinessService(cli);

        await readiness.InspectAsync(TestContext.Current.CancellationToken);

        var config = Assert.Single(cli.Calls, call => call.Args.Count > 1 && call.Args[0] == "config");
        AssertNeverPrompts(config.Args);
    }

    [Fact]
    public async Task PreparingExtensionsConfiguresTheProfileAndInstallsBastion()
    {
        var cli = new RecordingCli();
        var readiness = new WorkstationReadinessService(cli);

        await readiness.PrepareCliExtensionsAsync(TestContext.Current.CancellationToken);

        var config = cli.Calls.FirstOrDefault(call => call.Args.Count > 1 && call.Args[0] == "config");
        Assert.NotEqual(default, config);
        AssertNeverPrompts(config.Args);

        // Installed deliberately, rather than relying on a dynamic install at the moment somebody is
        // trying to connect through Bastion.
        var installed = cli.Calls
            .Where(call => call.Args.Count > 1 && call.Args[0] == "extension")
            .SelectMany(call => call.Args)
            .ToList();
        Assert.Contains("bastion", installed);
        Assert.Contains("ssh", installed);
        Assert.Contains("resource-graph", installed);
    }

    private static void AssertNeverPrompts(IReadOnlyList<string> arguments)
    {
        Assert.Contains("extension.use_dynamic_install=yes_without_prompt", arguments);
        Assert.Contains("extension.dynamic_install_allow_preview=false", arguments);
        Assert.Contains("core.only_show_errors=true", arguments);
    }

    private sealed class RecordingCli : IAzureCliRunner
    {
        public List<(string Config, IReadOnlyList<string> Args)> Calls { get; } = [];

        public Task<CommandResult> RunAsync(
            string azureConfigDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Calls.Add((azureConfigDirectory, arguments));
            // "version" is parsed as JSON by the caller, so give it something valid to parse.
            var output = arguments.Count > 0 && arguments[0] == "version" ? "{\"azure-cli\":\"2.85.0\"}" : "{}";
            return Task.FromResult(new CommandResult(0, output, string.Empty));
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
