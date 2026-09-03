using Avalonia;

namespace Stagecoach.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // No explicit AppUserModelID. Declaring one makes the taskbar resolve the button's icon and
        // pinning identity through a Start menu shortcut carrying the same ID; with no such
        // shortcut — and never for the portable ZIP — the taskbar falls back to the generic
        // application icon even though the executable carries its own. Without it the taskbar uses
        // the window and executable icons, which are correct.
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
