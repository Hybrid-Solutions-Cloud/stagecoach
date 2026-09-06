namespace Stagecoach.App;

/// <summary>
/// Keeps one Stagecoach per Windows session, and raises the running one instead of starting another.
/// <para>
/// Two copies are never what anyone wants here: both open the same encrypted database, both run
/// their own background sync, and each owns helper processes the other knows nothing about — so
/// closing one can leave sessions behind that nothing is tracking. Launching again from the Start
/// menu, the desktop, or the installer's finish dialog should simply bring the window forward.
/// </para>
/// <para>
/// Scoped to the logon session with the <c>Local\</c> prefix rather than <c>Global\</c>, because on
/// a machine several people are signed in to at once — a terminal server, which is exactly where
/// Stagecoach gets used — each of them is entitled to their own instance and their own estate.
/// </para>
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\Stagecoach.SingleInstance";
    private const string ActivationEventName = @"Local\Stagecoach.Activate";

    private static Mutex? _mutex;

    /// <summary>
    /// True when this process is the only Stagecoach in the session and may carry on starting.
    /// False when another already holds it, in which case the caller should signal and exit.
    /// </summary>
    public static bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (createdNew) return true;

            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // If the guard itself cannot be created, start anyway. Refusing to run because a
            // synchronisation object could not be opened would be a far worse failure than two
            // windows.
            CrashLog.Record("Single-instance guard", exception);
            return true;
        }
    }

    /// <summary>Asks the running instance to show itself. Never throws; there may be nothing there.</summary>
    public static void SignalExisting()
    {
        try
        {
            using var activation = EventWaitHandle.OpenExisting(ActivationEventName);
            activation.Set();
        }
        catch (Exception exception) when (
            exception is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            // The other instance is starting or stopping. Exiting quietly is still correct.
        }
    }

    /// <summary>
    /// Watches for another launch and runs <paramref name="onActivate"/> when one happens. The
    /// callback is invoked on a background thread; the caller marshals to the interface.
    /// </summary>
    public static void ListenForActivation(Action onActivate)
    {
        EventWaitHandle activation;
        try
        {
            activation = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            CrashLog.Record("Single-instance activation listener", exception);
            return;
        }

        var thread = new Thread(() =>
        {
            using (activation)
            {
                while (true)
                {
                    try
                    {
                        activation.WaitOne();
                        onActivate();
                    }
                    catch (Exception exception)
                    {
                        CrashLog.Record("Single-instance activation", exception);
                        return;
                    }
                }
            }
        })
        {
            IsBackground = true,
            Name = "Stagecoach activation listener",
        };
        thread.Start();
    }

    public static void Release()
    {
        try
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch (Exception exception) when (exception is ApplicationException or ObjectDisposedException)
        {
        }
        finally
        {
            _mutex = null;
        }
    }
}
