using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Stagecoach.App.Views;

/// <summary>
/// Shown before anything else when an unlock passphrase is configured. Returns the entropy that
/// unwraps the metadata key, or null if the operator quit.
/// </summary>
public partial class UnlockWindow : Window
{
    private readonly TaskCompletionSource<byte[]?> _result = new();
    private int _attempts;

    public UnlockWindow()
    {
        InitializeComponent();

        var passphrase = this.FindControl<TextBox>("PassphraseBox")!;
        var error = this.FindControl<TextBlock>("ErrorText")!;
        var unlock = this.FindControl<Button>("UnlockButton")!;
        var quit = this.FindControl<Button>("QuitButton")!;

        unlock.Click += (_, _) =>
        {
            var entropy = AppLock.TryUnlock(passphrase.Text ?? string.Empty);
            if (entropy is not null)
            {
                _result.TrySetResult(entropy);
                Close();
                return;
            }

            _attempts++;
            error.Text = _attempts >= 3
                ? "That passphrase is not correct. There is no recovery: if it is lost, remove the local state folder and start again."
                : "That passphrase is not correct.";
            error.IsVisible = true;
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

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
