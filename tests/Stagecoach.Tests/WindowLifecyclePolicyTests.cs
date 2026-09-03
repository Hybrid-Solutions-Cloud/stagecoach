using Avalonia.Controls;
using Stagecoach.App;

namespace Stagecoach.Tests;

public sealed class WindowLifecyclePolicyTests
{
    [Theory]
    [InlineData(true, WindowState.Minimized, true)]
    [InlineData(false, WindowState.Minimized, false)]
    [InlineData(true, WindowState.Normal, false)]
    [InlineData(true, WindowState.Maximized, false)]
    public void ShouldHideOnMinimize_OnlyHidesWhenMinimizedAndEnabled(
        bool minimizeToNotificationArea, WindowState state, bool expected) =>
        Assert.Equal(expected, WindowLifecyclePolicy.ShouldHideOnMinimize(minimizeToNotificationArea, state));

    [Fact]
    public void ShouldExitOnClose_ExitsWhenConfiguredAndNoSessionsAreRunning() =>
        Assert.True(WindowLifecyclePolicy.ShouldExitOnClose(exitOnClose: true, activeSessionCount: 0));

    [Fact]
    public void ShouldExitOnClose_KeepsRunningWhileSessionsAreLive() =>
        Assert.False(WindowLifecyclePolicy.ShouldExitOnClose(exitOnClose: true, activeSessionCount: 1));

    [Fact]
    public void ShouldExitOnClose_NeverExitsWhenCloseBehaviorIsNotificationArea() =>
        Assert.False(WindowLifecyclePolicy.ShouldExitOnClose(exitOnClose: false, activeSessionCount: 0));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    public void RequiresExitConfirmation_TracksLiveSessions(int sessions, bool expected) =>
        Assert.Equal(expected, WindowLifecyclePolicy.RequiresExitConfirmation(sessions));

    [Fact]
    public void DescribeTrayStatus_ReportsSessionCountAheadOfStatusText()
    {
        Assert.Equal("Stagecoach — 2 sessions running",
            WindowLifecyclePolicy.DescribeTrayStatus(2, isBusy: false, "idle"));
        Assert.Equal("Stagecoach — 1 session running",
            WindowLifecyclePolicy.DescribeTrayStatus(1, isBusy: false, "idle"));
        Assert.Equal("Stagecoach — idle",
            WindowLifecyclePolicy.DescribeTrayStatus(0, isBusy: false, "idle"));
        Assert.Equal("Stagecoach — working",
            WindowLifecyclePolicy.DescribeTrayStatus(3, isBusy: true, "idle"));
    }
}
