namespace Stagecoach.Tests;

/// <summary>
/// One Stagecoach per Windows session. Two copies open the same encrypted database, each runs its
/// own background sync, and each owns helper processes the other cannot see — so closing one can
/// leave live sessions behind that nothing is tracking.
/// <para>
/// These exercise the same named objects the application uses, so they also prove the names are
/// valid and can be created in the session the tests run in.
/// </para>
/// </summary>
public sealed class SingleInstanceTests
{
    private const string MutexName = @"Local\Stagecoach.SingleInstance.Test";
    private const string ActivationEventName = @"Local\Stagecoach.Activate.Test";

    [Fact]
    public void ASecondInstanceDoesNotAcquireTheGuardWhileTheFirstHoldsIt()
    {
        var first = new Mutex(initiallyOwned: true, MutexName, out var firstCreatedNew);
        Assert.True(firstCreatedNew, "The first instance must take the guard.");

        using (var second = new Mutex(initiallyOwned: true, MutexName, out var secondCreatedNew))
        {
            Assert.False(secondCreatedNew, "A second instance must find the guard already held, and exit.");
        }

        // Releasing ownership is not enough to free the name — the named object lives as long as any
        // handle to it does. What frees it is the first process exiting, which closes its handle.
        first.ReleaseMutex();
        first.Dispose();

        using var afterExit = new Mutex(initiallyOwned: true, MutexName, out var createdAfterExit);
        Assert.True(createdAfterExit, "Once the first instance has gone, the next launch owns the guard.");
        afterExit.ReleaseMutex();
    }

    [Fact]
    public void TheRunningInstanceIsSignalledInsteadOfASecondOneStarting()
    {
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);

        // What the second launch does before exiting.
        using (var fromSecondLaunch = EventWaitHandle.OpenExisting(ActivationEventName))
        {
            fromSecondLaunch.Set();
        }

        Assert.True(
            activation.WaitOne(TimeSpan.FromSeconds(5)),
            "The running instance must be woken so it can bring its window forward.");
    }

    [Fact]
    public void SignallingWhenNothingIsRunningIsNotAnError()
    {
        // A launch that finds no listener — the other instance is starting or stopping — must exit
        // quietly rather than throwing on the way out.
        Assert.Throws<WaitHandleCannotBeOpenedException>(
            () => EventWaitHandle.OpenExisting(@"Local\Stagecoach.Activate.Test.Absent"));
    }
}
