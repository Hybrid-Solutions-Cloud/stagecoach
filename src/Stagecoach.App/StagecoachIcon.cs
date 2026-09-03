using Avalonia.Controls;
using Avalonia.Platform;

namespace Stagecoach.App;

/// <summary>
/// Loads the Stagecoach wheel used for the window and notification-area icons.
/// The executable's own Win32 icon comes from <c>Assets/stagecoach.ico</c> via
/// <c>ApplicationIcon</c>; this loads the 256px PNG, because a window icon wants one
/// well-defined bitmap rather than whichever frame an .ico decoder happens to pick.
/// </summary>
internal static class StagecoachIcon
{
    private static readonly Uri Source = new("avares://Stagecoach.App/Assets/stagecoach.png");

    public static WindowIcon Create()
    {
        using var stream = AssetLoader.Open(Source);
        return new WindowIcon(stream);
    }
}
