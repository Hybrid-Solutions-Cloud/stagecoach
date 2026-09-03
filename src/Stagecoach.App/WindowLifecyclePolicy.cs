using Avalonia.Controls;

namespace Stagecoach.App;

/// <summary>
/// Pure window-lifecycle decisions, kept out of the view so they can be tested directly.
/// Stagecoach owns live RDP/SSH helper processes, so exiting is never incidental.
/// </summary>
public static class WindowLifecyclePolicy
{
    public static bool ShouldHideOnMinimize(bool minimizeToNotificationArea, WindowState state) =>
        minimizeToNotificationArea && state == WindowState.Minimized;

    /// <summary>
    /// Closing the window never tears down live sessions. With sessions running the app always
    /// returns to the notification area, whatever the configured close behaviour says.
    /// </summary>
    public static bool ShouldExitOnClose(bool exitOnClose, int activeSessionCount) =>
        exitOnClose && activeSessionCount == 0;

    /// <summary>True when an explicit Exit needs the operator to confirm losing live sessions.</summary>
    public static bool RequiresExitConfirmation(int activeSessionCount) => activeSessionCount > 0;

    public static string DescribeTrayStatus(int activeSessionCount, bool isBusy, string statusMessage)
    {
        if (isBusy) return "Stagecoach — working";
        return activeSessionCount switch
        {
            0 => $"Stagecoach — {statusMessage}",
            1 => "Stagecoach — 1 session running",
            _ => $"Stagecoach — {activeSessionCount} sessions running",
        };
    }
}
