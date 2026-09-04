using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stagecoach.App.Security;
using Stagecoach.Core;
using Stagecoach.Infrastructure.Storage;

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
    private readonly TaskCompletionSource<PassphraseRemovalOutcome> _result = new();
    private int _attempts;

    public PassphraseRemovalWindow() : this(null) { }

    /// <summary>
    /// Takes the store so the whole removal — unwrap, rewrap, drop the passphrase from the record —
    /// happens here as one step. Splitting it across this window and the caller left a gap where a
    /// crash between the two writes produced a key and a record that disagreed, and the next launch
    /// could not be recovered from inside the application.
    /// </summary>
    public PassphraseRemovalWindow(EncryptedSqliteMetadataStore? store)
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
                try
                {
                    Remove(store, entropy);
                    _result.TrySetResult(PassphraseRemovalOutcome.Removed);
                    Close();
                    return;
                }
                catch (Exception exception)
                {
                    // The passphrase was right but the removal could not be completed — the key is
                    // wrapped with something else, or the record could not be written. Either way
                    // this must report, not crash the window it is the only way past.
                    CrashLog.Record("Passphrase removal", exception);
                    error.Text = exception is CryptographicException
                        ? "That passphrase is correct, but the database key on this machine is protected " +
                          "with something else and cannot be unwrapped. Use Start fresh — your machines " +
                          "are rediscovered on the next scan."
                        : $"The passphrase could not be removed: {exception.Message}";
                    error.IsVisible = true;
                    return;
                }
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
            _result.TrySetResult(PassphraseRemovalOutcome.Cancelled);
            Close();
        };

        reset.Click += (_, _) =>
        {
            try
            {
                LocalState.StartFresh();
                _result.TrySetResult(PassphraseRemovalOutcome.StartedFresh);
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
        Closed += (_, _) => _result.TrySetResult(PassphraseRemovalOutcome.Cancelled);
    }

    public Task<PassphraseRemovalOutcome> Result => _result.Task;

    /// <summary>
    /// Unwraps the key with the passphrase entropy, rewraps it under Windows protection alone, and
    /// only then drops the passphrase from the record. The record is written last on purpose: if
    /// this is interrupted, the record still says a passphrase exists, and the next launch finds a
    /// key that already needs none — which <see cref="TryRemoveWithoutPassphrase"/> then settles.
    /// </summary>
    private static void Remove(EncryptedSqliteMetadataStore? store, byte[] entropy)
    {
        if (store is not null)
        {
            store.UseAdditionalEntropy(entropy);
            store.RewrapKey(null);
        }

        AppOwner.CompletePassphraseRemoval();
    }

    /// <summary>
    /// Settles an interrupted removal without asking for anything. When the key already opens with
    /// no entropy, the passphrase was removed and only the record is stale, so it is finished
    /// silently. Returns false when the key really is still wrapped with a passphrase.
    /// </summary>
    public static bool TryRemoveWithoutPassphrase(EncryptedSqliteMetadataStore store)
    {
        try
        {
            store.UseAdditionalEntropy(null);
            store.RewrapKey(null);
            AppOwner.CompletePassphraseRemoval();
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
