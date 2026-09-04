using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stagecoach.App.Security;
using Stagecoach.Core;

namespace Stagecoach.App.Views;

public enum PassphraseRemovalOutcome
{
    /// <summary>The operator quit, or the window was closed. Nothing was changed.</summary>
    Cancelled,

    /// <summary>The passphrase was verified; the caller must rewrap the key with the entropy given.</summary>
    Removed,

    /// <summary>Local state was deleted. The application has to be restarted.</summary>
    StartedFresh,
}

/// <summary>
/// Shown once, to an installation that still carries a passphrase from the version that had one.
/// <para>
/// The database key was wrapped with entropy derived from that passphrase, so it cannot be dropped
/// silently — the key has to be unwrapped with it a final time and rewrapped without it. That is the
/// only reason this window exists, and it never appears again.
/// </para>
/// </summary>
public partial class PassphraseRemovalWindow : Window
{
    private readonly TaskCompletionSource<(PassphraseRemovalOutcome Outcome, byte[]? Entropy)> _result = new();
    private int _attempts;

    public PassphraseRemovalWindow()
    {
        InitializeComponent();

        var passphrase = this.FindControl<TextBox>("PassphraseBox")!;
        var error = this.FindControl<TextBlock>("ErrorText")!;
        var continueButton = this.FindControl<Button>("ContinueButton")!;
        var quit = this.FindControl<Button>("QuitButton")!;
        var reset = this.FindControl<Button>("ResetButton")!;

        continueButton.Click += (_, _) =>
        {
            error.IsVisible = false;
            var entropy = AppOwner.TryLegacyPassphrase(passphrase.Text ?? string.Empty);
            if (entropy is not null)
            {
                _result.TrySetResult((PassphraseRemovalOutcome.Removed, entropy));
                Close();
                return;
            }

            _attempts++;
            error.Text = _attempts >= 3
                ? "That passphrase is not correct. If it cannot be recovered, use Start fresh — the estate is rediscovered on the next scan."
                : "That passphrase is not correct.";
            error.IsVisible = true;
            passphrase.Text = string.Empty;
            passphrase.Focus();
        };

        quit.Click += (_, _) =>
        {
            _result.TrySetResult((PassphraseRemovalOutcome.Cancelled, null));
            Close();
        };

        reset.Click += (_, _) =>
        {
            try
            {
                LocalState.StartFresh();
                _result.TrySetResult((PassphraseRemovalOutcome.StartedFresh, null));
                Close();
            }
            catch (Exception exception)
            {
                CrashLog.Record("Start fresh", exception);
                error.Text = "Some local files are in use and could not be removed. Close Stagecoach and try again.";
                error.IsVisible = true;
            }
        };

        Opened += (_, _) => passphrase.Focus();
        Closed += (_, _) => _result.TrySetResult((PassphraseRemovalOutcome.Cancelled, null));
    }

    public Task<(PassphraseRemovalOutcome Outcome, byte[]? Entropy)> Result => _result.Task;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
