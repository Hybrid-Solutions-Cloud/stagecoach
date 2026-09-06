using Avalonia;

namespace Stagecoach.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // One Stagecoach per Windows session. Two copies open the same encrypted database, each runs
        // its own background sync, and each owns helper processes the other cannot see — so closing
        // one can strand live sessions. A second launch raises the first window instead.
        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.SignalExisting();
            return;
        }


        // No explicit AppUserModelID. Declaring one makes the taskbar resolve the button's icon and
        // pinning identity through a Start menu shortcut carrying the same ID; with no such
        // shortcut — and never for the portable ZIP — the taskbar falls back to the generic
        // application icon even though the executable carries its own. Without it the taskbar uses
        // the window and executable icons, which are correct.
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstance.Release();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
