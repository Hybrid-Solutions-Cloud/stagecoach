using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stagecoach.App.Security;
using Stagecoach.Core;

namespace Stagecoach.App.Views;

/// <summary>
/// Shown before anything reads the database. Verifies the owning account the way that account was
/// configured — Windows Hello for a Windows owner, falling back to a Windows credential prompt where
/// Hello cannot prompt, and an Entra sign-in for an Entra owner.
/// <para>
/// There is nothing to type that Stagecoach invented. The database is protected by Windows for the
/// owning Windows account; this window is the presence check on top of that, the same shape Vault
/// Prospector uses.
/// </para>
/// </summary>
public partial class UnlockWindow : Window
{
    private readonly TaskCompletionSource<bool> _result = new();
    private readonly IAzureCliRunner _cli;
    private bool _verifying;

    public UnlockWindow() : this(new Infrastructure.Azure.AzureCliRunner()) { }

    public UnlockWindow(IAzureCliRunner cli)
    {
        _cli = cli;
        InitializeComponent();

        var ownerText = this.FindControl<TextBlock>("OwnerText")!;
        var methodText = this.FindControl<TextBlock>("MethodText")!;
        var verifyButton = this.FindControl<Button>("VerifyButton")!;
        var verifyStatus = this.FindControl<TextBlock>("VerifyStatus")!;
        var quit = this.FindControl<Button>("QuitButton")!;
        var reset = this.FindControl<Button>("ResetButton")!;

        var owner = AppOwner.Current;
        ownerText.Text = owner is null ? "Unlock Stagecoach" : owner.DisplayName;

        if (owner?.Kind == AppOwnerKind.EntraAccount)
        {
            methodText.Text = "Sign in to the Entra account that owns this installation.";
            verifyButton.Content = "Sign in with Microsoft";
            verifyButton.Click += async (_, _) => await GuardAsync(
                verifyButton, () => VerifyEntraAsync(verifyStatus, owner));
        }
        else
        {
            methodText.Text = "Verify with Windows to open your machines.";
            verifyButton.Content = "Verify with Windows";
            verifyButton.Click += async (_, _) => await GuardAsync(
                verifyButton, () => VerifyWindowsAsync(verifyStatus));
        }

        quit.Click += (_, _) =>
        {
            _result.TrySetResult(false);
            Close();
        };

        reset.Click += (_, _) =>
        {
            try
            {
                LocalState.StartFresh();
                verifyStatus.IsVisible = true;
                verifyStatus.Text = "Local state removed. Close and reopen Stagecoach to set it up again.";
            }
            catch (Exception exception)
            {
                CrashLog.Record("Start fresh", exception);
                verifyStatus.IsVisible = true;
                verifyStatus.Text = "Some local files are in use and could not be removed. Close Stagecoach and try again.";
            }
        };

        // The verification prompt is the whole screen, so raise it without making the operator click
        // a button first. The button stays for a second attempt.
        Opened += async (_, _) => await GuardAsync(
            verifyButton,
            () => owner?.Kind == AppOwnerKind.EntraAccount
                ? VerifyEntraAsync(verifyStatus, owner)
                : VerifyWindowsAsync(verifyStatus));

        Closed += (_, _) => _result.TrySetResult(false);
    }

    public Task<bool> Result => _result.Task;

    private async Task GuardAsync(Button button, Func<Task> work)
    {
        if (_verifying) return;
        _verifying = true;
        button.IsEnabled = false;
        try
        {
            await work();
        }
        finally
        {
            _verifying = false;
            button.IsEnabled = true;
        }
    }

    private async Task VerifyWindowsAsync(TextBlock status)
    {
        status.IsVisible = true;

        // Checked before any prompt: a presence check only proves the current Windows user is there,
        // so a different user on this machine would otherwise pass someone else's gate.
        if (AppOwner.Current is { Kind: AppOwnerKind.WindowsAccount } && !AppOwner.CurrentWindowsAccountIsOwner())
        {
            status.Text =
                $"This installation belongs to {AppOwner.Current?.DisplayName}. " +
                $"You are signed in to Windows as {AppOwner.CurrentWindowsAccount().Name}.";
            return;
        }

        status.Text = "Waiting for Windows…";
        try
        {
            var hello = new WindowsHelloVerifier(() => TryGetPlatformHandle()?.Handle ?? 0);
            var result = await hello.VerifyAsync("Unlock Stagecoach");

            // Hello cannot prompt over RDP and is not enrolled everywhere. Windows can still verify
            // the operator with their own credentials, which is what Prospector falls back to.
            if (WindowsHelloVerifier.ShouldFallBackToCredentials(result))
            {
                status.Text = WindowsHelloVerifier.Describe(result);
                var credentials = new WindowsCredentialVerifier(() => TryGetPlatformHandle()?.Handle ?? 0);
                result = await credentials.VerifyAsync("Unlock Stagecoach");
            }

            if (result == UserVerificationResult.Verified)
            {
                _result.TrySetResult(true);
                Close();
                return;
            }

            status.Text = WindowsHelloVerifier.Describe(result);
        }
        catch (Exception exception)
        {
            CrashLog.Record("Windows verification", exception);
            status.Text = "Windows could not be asked to verify you. See the error log.";
        }
    }

    private async Task VerifyEntraAsync(TextBlock status, AppOwnerRecord owner)
    {
        status.IsVisible = true;
        status.Text = "Signing in…";
        try
        {
            var directory = AppOwner.EntraOwnerConfigDirectory;
            Directory.CreateDirectory(directory);
            var login = await _cli.RunInteractiveAsync(
                directory, ["login", "--allow-no-subscriptions", "--output", "json"]);
            if (!login.Succeeded)
            {
                status.Text = "Sign-in did not complete.";
                return;
            }

            var account = await _cli.RunAsync(directory, ["account", "show", "--output", "json"]);
            using var document = System.Text.Json.JsonDocument.Parse(account.StandardOutput);
            var signedIn = document.RootElement.TryGetProperty("user", out var user) &&
                           user.TryGetProperty("name", out var name)
                ? name.GetString() ?? string.Empty
                : string.Empty;

            if (AppOwner.EntraAccountIsOwner(signedIn))
            {
                _result.TrySetResult(true);
                Close();
                return;
            }

            status.Text = $"{signedIn} does not own this installation. It belongs to {owner.EntraUserPrincipalName}.";
        }
        catch (Exception exception)
        {
            CrashLog.Record("Owner Entra verification", exception);
            status.Text = "Sign-in failed. See the error log.";
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
