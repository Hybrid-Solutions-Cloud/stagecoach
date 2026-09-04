using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stagecoach.App.Security;
using Stagecoach.Core;

namespace Stagecoach.App.Views;

/// <summary>
/// Shown before anything reads the database. Verifies the owning account the way that account was
/// configured — Windows Hello for a Windows owner, an Entra sign-in for an Entra owner — and then
/// takes the passphrase, which is what actually unwraps the metadata key.
/// </summary>
public partial class UnlockWindow : Window
{
    private readonly TaskCompletionSource<byte[]?> _result = new();
    private readonly IAzureCliRunner _cli;
    private bool _identityVerified;
    private int _attempts;

    public UnlockWindow() : this(new Infrastructure.Azure.AzureCliRunner()) { }

    public UnlockWindow(IAzureCliRunner cli)
    {
        _cli = cli;
        InitializeComponent();

        var ownerText = this.FindControl<TextBlock>("OwnerText")!;
        var methodText = this.FindControl<TextBlock>("MethodText")!;
        var verifyPanel = this.FindControl<StackPanel>("VerifyPanel")!;
        var verifyButton = this.FindControl<Button>("VerifyButton")!;
        var verifyStatus = this.FindControl<TextBlock>("VerifyStatus")!;
        var passphrase = this.FindControl<TextBox>("PassphraseBox")!;
        var error = this.FindControl<TextBlock>("ErrorText")!;
        var unlock = this.FindControl<Button>("UnlockButton")!;
        var quit = this.FindControl<Button>("QuitButton")!;

        var owner = AppOwner.Current;
        ownerText.Text = owner is null ? "Unlock Stagecoach" : owner.DisplayName;

        if (owner?.Kind == AppOwnerKind.EntraAccount)
        {
            methodText.Text = "Sign in to the Entra account that owns this installation, then enter your passphrase.";
            verifyButton.Content = "Sign in with Microsoft";
            verifyButton.Click += async (_, _) => await VerifyEntraAsync(verifyButton, verifyStatus, owner);
        }
        else if (owner?.Kind == AppOwnerKind.WindowsAccount)
        {
            methodText.Text = "Verify with Windows, then enter your passphrase.";
            verifyButton.Content = "Verify with Windows Hello";
            verifyButton.Click += async (_, _) => await VerifyWindowsAsync(verifyButton, verifyStatus);
        }
        else
        {
            verifyPanel.IsVisible = false;
            methodText.Text = "Enter your passphrase.";
            _identityVerified = true;
        }

        unlock.Click += (_, _) =>
        {
            error.IsVisible = false;

            // The passphrase is the cryptographic gate. Hello and the Entra sign-in prove who is
            // present; neither yields key material, so both are required rather than either.
            if (!_identityVerified && owner is not null)
            {
                Fail(error, owner.Kind == AppOwnerKind.EntraAccount
                    ? "Sign in to the owning Entra account first."
                    : "Verify with Windows first, or use the passphrase if Hello is unavailable.");
                if (VerificationIsOptional()) _identityVerified = true;
                return;
            }

            var entropy = AppOwner.TryPassphrase(passphrase.Text ?? string.Empty);
            if (entropy is not null)
            {
                _result.TrySetResult(entropy);
                Close();
                return;
            }

            _attempts++;
            Fail(error, _attempts >= 3
                ? "That passphrase is not correct. There is no recovery: if it is lost, the local state folder must be removed and the accounts connected again."
                : "That passphrase is not correct.");
            passphrase.Text = string.Empty;
            passphrase.Focus();
        };

        quit.Click += (_, _) =>
        {
            _result.TrySetResult(null);
            Close();
        };

        Opened += (_, _) => passphrase.Focus();
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public Task<byte[]?> Result => _result.Task;

    /// <summary>
    /// Hello cannot prompt in a remote session, and may not be enrolled at all. In those cases the
    /// passphrase alone has to be enough, or the operator would be locked out of their own data.
    /// </summary>
    private bool VerificationIsOptional() => WindowsHelloVerifier.IsRemoteSession;

    private async Task VerifyWindowsAsync(Button button, TextBlock status)
    {
        status.IsVisible = true;
        if (!AppOwner.CurrentWindowsAccountIsOwner())
        {
            status.Text =
                $"This installation belongs to {AppOwner.Current?.DisplayName}. " +
                $"You are signed in to Windows as {AppOwner.CurrentWindowsAccount().Name}.";
            return;
        }

        button.IsEnabled = false;
        status.Text = "Waiting for Windows…";
        try
        {
            var verifier = new WindowsHelloVerifier(() => TryGetPlatformHandle()?.Handle ?? 0);
            var result = await verifier.VerifyAsync("Unlock Stagecoach");
            _identityVerified = result == UserVerificationResult.Verified;
            status.Text = WindowsHelloVerifier.Describe(result);

            // Not being able to prompt must not become a lockout; fall through to the passphrase.
            if (result is UserVerificationResult.NotConfigured
                or UserVerificationResult.DisabledByPolicy
                or UserVerificationResult.RemoteSessionUnavailable
                or UserVerificationResult.Unavailable)
                _identityVerified = true;
        }
        catch (Exception exception)
        {
            CrashLog.Record("Windows Hello verification", exception);
            status.Text = "Windows could not be asked to verify you. Use the passphrase.";
            _identityVerified = true;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async Task VerifyEntraAsync(Button button, TextBlock status, AppOwnerRecord owner)
    {
        button.IsEnabled = false;
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

            _identityVerified = AppOwner.EntraAccountIsOwner(signedIn);
            status.Text = _identityVerified
                ? $"Verified as {signedIn}."
                : $"{signedIn} does not own this installation. It belongs to {owner.EntraUserPrincipalName}.";
        }
        catch (Exception exception)
        {
            CrashLog.Record("Owner Entra verification", exception);
            status.Text = "Sign-in failed. See the error log.";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static void Fail(TextBlock error, string message)
    {
        error.Text = message;
        error.IsVisible = true;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
