using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stagecoach.App.Security;
using Stagecoach.Core;

namespace Stagecoach.App.Views;

/// <summary>
/// First-run setup: choose the account that owns this installation and set the passphrase that
/// protects its database. Runs before anything reads the store.
/// </summary>
public partial class OwnerSetupWindow : Window
{
    private readonly TaskCompletionSource<byte[]?> _result = new();
    private readonly IAzureCliRunner _cli;
    private string? _entraUpn;

    public OwnerSetupWindow() : this(new Infrastructure.Azure.AzureCliRunner()) { }

    public OwnerSetupWindow(IAzureCliRunner cli)
    {
        _cli = cli;
        InitializeComponent();

        var windowsChoice = this.FindControl<RadioButton>("WindowsChoice")!;
        var entraChoice = this.FindControl<RadioButton>("EntraChoice")!;
        var windowsText = this.FindControl<TextBlock>("WindowsAccountText")!;
        var entraText = this.FindControl<TextBlock>("EntraAccountText")!;
        var entraSignIn = this.FindControl<Button>("EntraSignInButton")!;
        var passphrase = this.FindControl<TextBox>("PassphraseBox")!;
        var confirm = this.FindControl<TextBox>("ConfirmBox")!;
        var error = this.FindControl<TextBlock>("ErrorText")!;
        var finish = this.FindControl<Button>("FinishButton")!;
        var quit = this.FindControl<Button>("QuitButton")!;

        windowsText.Text = AppOwner.CurrentWindowsAccount().Name;

        entraSignIn.Click += async (_, _) =>
        {
            entraSignIn.IsEnabled = false;
            entraText.Text = "Signing in…";
            try
            {
                _entraUpn = await SignInOwnerAsync();
                entraText.Text = _entraUpn is null ? "Sign-in did not complete." : _entraUpn;
            }
            catch (Exception exception)
            {
                CrashLog.Record("Owner Entra sign-in", exception);
                entraText.Text = "Sign-in failed. See the error log.";
            }
            finally
            {
                entraSignIn.IsEnabled = true;
            }
        };

        finish.Click += (_, _) =>
        {
            error.IsVisible = false;
            var kind = entraChoice.IsChecked == true ? AppOwnerKind.EntraAccount : AppOwnerKind.WindowsAccount;
            var secret = passphrase.Text ?? string.Empty;

            if (!string.Equals(secret, confirm.Text ?? string.Empty, StringComparison.Ordinal))
            {
                Fail(error, "The two passphrases do not match.");
                return;
            }

            try
            {
                var entropy = AppOwner.Configure(
                    kind,
                    kind == AppOwnerKind.EntraAccount ? _entraUpn ?? string.Empty : AppOwner.CurrentWindowsAccount().Name,
                    secret,
                    _entraUpn);
                _result.TrySetResult(entropy);
                Close();
            }
            catch (InvalidOperationException exception)
            {
                Fail(error, exception.Message);
            }
        };

        quit.Click += (_, _) =>
        {
            _result.TrySetResult(null);
            Close();
        };

        Opened += (_, _) => { windowsChoice.IsChecked = true; passphrase.Focus(); };
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public Task<byte[]?> Result => _result.Task;

    /// <summary>
    /// Signs in to the owning Entra account in its own isolated profile, kept apart from the
    /// connected identities so the two can never be confused for one another.
    /// </summary>
    private async Task<string?> SignInOwnerAsync()
    {
        var directory = AppOwner.EntraOwnerConfigDirectory;
        Directory.CreateDirectory(directory);
        var login = await _cli.RunInteractiveAsync(
            directory, ["login", "--allow-no-subscriptions", "--output", "json"]);
        if (!login.Succeeded) return null;

        var account = await _cli.RunAsync(directory, ["account", "show", "--output", "json"]);
        if (!account.Succeeded) return null;

        using var document = System.Text.Json.JsonDocument.Parse(account.StandardOutput);
        return document.RootElement.TryGetProperty("user", out var user) &&
               user.TryGetProperty("name", out var name)
            ? name.GetString()
            : null;
    }

    private static void Fail(TextBlock error, string message)
    {
        error.Text = message;
        error.IsVisible = true;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
