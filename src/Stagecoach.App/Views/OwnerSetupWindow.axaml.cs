using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stagecoach.App.Security;
using Stagecoach.Core;

namespace Stagecoach.App.Views;

/// <summary>
/// First-run setup: choose the account that owns this installation. Runs before anything reads the
/// store. Nothing is typed here — the choice is a Windows account or an Entra account, and the
/// database is protected by Windows for that account either way.
/// </summary>
public partial class OwnerSetupWindow : Window
{
    private readonly TaskCompletionSource<bool> _result = new();
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
        var error = this.FindControl<TextBlock>("ErrorText")!;
        var finish = this.FindControl<Button>("FinishButton")!;
        var quit = this.FindControl<Button>("QuitButton")!;

        windowsText.Text = AppOwner.CurrentWindowsAccount().Name;

        // On a Microsoft Entra joined machine the Windows account is already an Entra account, so
        // choosing the Entra option needs no sign-in — Windows performed it to create this session.
        _entraUpn = AppOwner.CurrentWindowsUserPrincipalName();
        if (_entraUpn is { Length: > 0 }) entraText.Text = $"{_entraUpn} — already signed in to Windows";

        entraSignIn.Click += async (_, _) =>
        {
            entraSignIn.IsEnabled = false;
            entraText.Text = "Signing in…";
            try
            {
                // Surfaces a device code, rather than losing it inside the hidden CLI process.
                var progress = new Progress<string>(line =>
                {
                    if (!string.IsNullOrWhiteSpace(line)) entraText.Text = line.Trim();
                });
                _entraUpn = await SignInOwnerAsync(progress);
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

            try
            {
                AppOwner.Configure(
                    kind,
                    kind == AppOwnerKind.EntraAccount ? _entraUpn ?? string.Empty : AppOwner.CurrentWindowsAccount().Name,
                    _entraUpn);
                _result.TrySetResult(true);
                Close();
            }
            catch (InvalidOperationException exception)
            {
                Fail(error, exception.Message);
            }
        };

        quit.Click += (_, _) =>
        {
            _result.TrySetResult(false);
            Close();
        };

        Opened += (_, _) => windowsChoice.IsChecked = true;
        Closed += (_, _) => _result.TrySetResult(false);
    }

    public Task<bool> Result => _result.Task;

    /// <summary>
    /// Signs in to the owning Entra account in its own isolated profile, kept apart from the
    /// connected identities so the two can never be confused for one another.
    /// </summary>
    private async Task<string?> SignInOwnerAsync(IProgress<string>? progress = null)
    {
        var directory = AppOwner.EntraOwnerConfigDirectory;
        return await OwnerEntraSignIn.SignInAsync(_cli, directory, progress)
            ? await OwnerEntraSignIn.SignedInAccountAsync(_cli, directory)
            : null;
    }

    private static void Fail(TextBlock error, string message)
    {
        error.Text = message;
        error.IsVisible = true;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
