using System.Text.Json;
using Stagecoach.Core;

namespace Stagecoach.App.Security;

/// <summary>
/// Signing in to the Microsoft Entra account that owns this installation.
/// <para>
/// This exists because the owner sign-in used to call <c>az login</c> directly while the account
/// sign-in that works went through <c>AzureCliIdentityService</c>. The difference that mattered is
/// <c>core.login_experience_v2=off</c>: without it the CLI presents an interactive account and
/// subscription picker that expects a console, and Stagecoach runs the CLI hidden with its output
/// redirected — so the sign-in never completed and the unlock screen simply sat there.
/// </para>
/// </summary>
public static class OwnerEntraSignIn
{
    /// <summary>
    /// Prepares the isolated profile the same way a connected account's profile is prepared. Must
    /// run before the first sign-in into a directory.
    /// </summary>
    public static async Task ConfigureAsync(
        IAzureCliRunner cli, string directory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        await cli.RunAsync(directory,
            ["config", "set", "core.login_experience_v2=off", "core.collect_telemetry=false",
             "core.only_show_errors=true", "core.no_color=true"], cancellationToken);
    }

    /// <summary>
    /// Signs in interactively, reporting anything the CLI says so a device code can be shown. Tries
    /// the browser first and falls back to a device code, which is the flow that always works in a
    /// remote session where a browser handoff may not.
    /// </summary>
    public static async Task<bool> SignInAsync(
        IAzureCliRunner cli,
        string directory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await ConfigureAsync(cli, directory, cancellationToken);

        var login = await cli.RunInteractiveAsync(
            directory, ["login", "--allow-no-subscriptions", "--output", "json"], progress, cancellationToken);
        if (login.Succeeded) return true;

        progress?.Report("Opening a browser did not work. Use the code below to sign in.");
        var deviceCode = await cli.RunInteractiveAsync(
            directory,
            ["login", "--allow-no-subscriptions", "--use-device-code", "--output", "json"],
            progress,
            cancellationToken);
        return deviceCode.Succeeded;
    }

    /// <summary>The account signed in to a profile, or null. Never interactive.</summary>
    public static async Task<string?> SignedInAccountAsync(
        IAzureCliRunner cli, string directory, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(directory)) return null;
            var account = await cli.RunAsync(directory, ["account", "show", "--output", "json"], cancellationToken);
            if (!account.Succeeded || string.IsNullOrWhiteSpace(account.StandardOutput)) return null;

            using var document = JsonDocument.Parse(account.StandardOutput);
            return document.RootElement.TryGetProperty("user", out var user) &&
                   user.TryGetProperty("name", out var name)
                ? name.GetString()
                : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
