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
    private Button? _continueButton;
    private Button? _switchOwnerButton;
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
        var continueButton = this.FindControl<Button>("ContinueButton")!;
        var switchOwner = this.FindControl<Button>("SwitchOwnerButton")!;
        var quit = this.FindControl<Button>("QuitButton")!;
        var reset = this.FindControl<Button>("ResetButton")!;

        _continueButton = continueButton;
        continueButton.Click += (_, _) =>
        {
            _result.TrySetResult(true);
            Close();
        };

        // The way out of an owner that can no longer be verified, without destroying anything. The
        // estate stays exactly where it is; only the record of who opens Stagecoach changes.
        switchOwner.Content = $"Use this Windows account instead ({AppOwner.CurrentWindowsAccount().Name})";
        switchOwner.Click += async (_, _) => await GuardAsync(
            switchOwner, () => SwitchToWindowsOwnerAsync(verifyStatus));

        var owner = AppOwner.Current;
        ownerText.Text = owner is null ? "Unlock Stagecoach" : owner.DisplayName;

        if (owner?.Kind == AppOwnerKind.EntraAccount)
        {
            methodText.Text = $"This installation is owned by {owner.EntraUserPrincipalName}.";
            verifyButton.Content = "Sign in with Microsoft";
            _switchOwnerButton = switchOwner;
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

        // Only checks that cannot prompt run on their own. An interactive sign-in launched
        // automatically is what made this screen feel like an endless login loop, so it now waits
        // for a deliberate click.
        Opened += async (_, _) => await GuardAsync(
            verifyButton,
            () => owner?.Kind == AppOwnerKind.EntraAccount
                ? VerifyEntraSilentlyAsync(verifyStatus, owner)
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
            OfferToContinue(result);
        }
        catch (Exception exception)
        {
            CrashLog.Record("Windows verification", exception);
            status.Text = "Windows could not be asked to verify you. See the error log.";
            OfferToContinue(UserVerificationResult.Unavailable);
        }
    }

    /// <summary>
    /// Offers a way in when Windows cannot actually run a check, rather than leaving a dead end.
    /// <para>
    /// On a Microsoft Entra joined machine the signed-in account is a cloud account, and
    /// <c>LogonUser</c> cannot validate one with a password — so a correct password and a wrong one
    /// look identical here. Combined with Windows Hello being unable to prompt inside a remote
    /// session, that would lock an operator out of a machine they are already signed in to.
    /// </para>
    /// <para>
    /// The Windows account has already been matched by SID, and what Stagecoach stores is encrypted
    /// with a key Windows releases only to that account — so anyone who could click this could read
    /// the same data anyway. The check is a speed bump, and it must not become a wall.
    /// </para>
    /// </summary>
    private void OfferToContinue(UserVerificationResult result)
    {
        if (result == UserVerificationResult.Canceled || _continueButton is null) return;
        _continueButton.Content = $"Continue as {AppOwner.CurrentWindowsAccount().Name}";
        _continueButton.IsVisible = true;
    }

    private async Task VerifyEntraAsync(TextBlock status, AppOwnerRecord owner)
    {
        status.IsVisible = true;
        try
        {
            var directory = AppOwner.EntraOwnerConfigDirectory;
            status.Text = "Signing in…";

            // Anything the CLI says goes straight to the screen — that is how a device code reaches
            // the operator instead of disappearing into a hidden process.
            var progress = new Progress<string>(line =>
            {
                if (!string.IsNullOrWhiteSpace(line)) status.Text = line.Trim();
            });

            if (!await OwnerEntraSignIn.SignInAsync(_cli, directory, progress))
            {
                status.Text = "Sign-in did not complete.";
                ShowSwitchOwner();
                return;
            }

            if (await SignedInOwnerAsync(directory))
            {
                await VerifyWindowsAsync(status);
                return;
            }

            status.Text =
                $"That account does not own this installation. It belongs to {owner.EntraUserPrincipalName}.";
            ShowSwitchOwner();
        }
        catch (Exception exception)
        {
            CrashLog.Record("Owner Entra verification", exception);
            status.Text = "Sign-in failed. See the error log.";
            ShowSwitchOwner();
        }
    }

    /// <summary>
    /// The checks that cannot prompt, run on open. Two of them, cheapest first: on a Microsoft Entra
    /// joined machine the Windows account <b>is</b> an Entra account and Windows already
    /// authenticated it to create this session; failing that, the isolated Azure CLI profile may
    /// already hold a valid sign-in for the owner. Either way there is nothing left to prove.
    /// </summary>
    private async Task VerifyEntraSilentlyAsync(TextBlock status, AppOwnerRecord owner)
    {
        try
        {
            if (AppOwner.CurrentWindowsUserPrincipalName() is { Length: > 0 } windowsUpn &&
                AppOwner.EntraAccountIsOwner(windowsUpn))
            {
                // Identity established without a sign-in, but that is not the same as being let in.
                // The owner account has to actually gate the application, so still ask Windows to
                // verify the person sitting here.
                await VerifyWindowsAsync(status);
                return;
            }

            status.IsVisible = true;
            status.Text = "Checking your sign-in…";
            if (await SignedInOwnerAsync(AppOwner.EntraOwnerConfigDirectory))
            {
                await VerifyWindowsAsync(status);
                return;
            }

            status.Text = $"Sign in as {owner.EntraUserPrincipalName} to open Stagecoach.";
            ShowSwitchOwner();
        }
        catch (Exception exception)
        {
            CrashLog.Record("Owner Entra check", exception);
            status.IsVisible = true;
            status.Text = "Your sign-in could not be checked.";
            ShowSwitchOwner();
        }
    }

    private void ShowSwitchOwner()
    {
        if (_switchOwnerButton is not null) _switchOwnerButton.IsVisible = true;
    }

    /// <summary>
    /// Hands this installation to the Windows account at the keyboard, after that account passes the
    /// same presence check a Windows owner faces. Nothing stored is touched — the database is
    /// already encrypted for this Windows account — so no machines, accounts or pins are lost.
    /// </summary>
    private async Task SwitchToWindowsOwnerAsync(TextBlock status)
    {
        status.IsVisible = true;
        status.Text = "Verifying with Windows…";
        try
        {
            var hello = new WindowsHelloVerifier(() => TryGetPlatformHandle()?.Handle ?? 0);
            var result = await hello.VerifyAsync("Take ownership of Stagecoach");
            if (WindowsHelloVerifier.ShouldFallBackToCredentials(result))
            {
                var credentials = new WindowsCredentialVerifier(() => TryGetPlatformHandle()?.Handle ?? 0);
                result = await credentials.VerifyAsync("Take ownership of Stagecoach");
            }

            // The same reasoning as continuing past an unverifiable prompt: Windows released the
            // database key to this account already, so a check it cannot perform must not lock it.
            if (result == UserVerificationResult.Canceled)
            {
                status.Text = "Cancelled.";
                return;
            }

            var account = AppOwner.CurrentWindowsAccount();
            AppOwner.Configure(AppOwnerKind.WindowsAccount, account.Name);
            _result.TrySetResult(true);
            Close();
        }
        catch (Exception exception)
        {
            CrashLog.Record("Switch owner to Windows account", exception);
            status.Text = "The owner could not be changed. See the error log.";
        }
    }

    /// <summary>
    /// Whether the owning Entra account is already signed in to its isolated profile. Never
    /// interactive: a failure here simply means a sign-in is needed.
    /// </summary>
    private async Task<bool> SignedInOwnerAsync(string directory) =>
        await OwnerEntraSignIn.SignedInAccountAsync(_cli, directory) is { Length: > 0 } signedIn &&
        AppOwner.EntraAccountIsOwner(signedIn);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
