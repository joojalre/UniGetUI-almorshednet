using Avalonia.Threading;
using UniGetUI.Avalonia.Infrastructure;
using UniGetUI.Avalonia.ViewModels.Pages.SettingsPages;
using UniGetUI.Core.Data;
using UniGetUI.Core.Tools.Scheduling;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.PackageLoader;
using UniGetUI.PackageOperations;

namespace UniGetUI.Core.Tools.Tests;

public sealed class MaintenanceSchedulerTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), "UniGetUI-Scheduler-" + Guid.NewGuid());
    private readonly TestClock _clock = new();
    private readonly UpgradablePackagesLoader _updates = new();

    public MaintenanceSchedulerTests()
    {
        CoreData.TEST_DataDirectoryOverride = Path.Combine(_testRoot, "Data");
        Directory.CreateDirectory(CoreData.UniGetUIUserConfigurationDirectory);
        MaintenanceScheduler.Clock = _clock;
        UpgradablePackagesLoader.Instance = _updates;
        InstalledPackagesLoader.Instance = new() { new object() };
        InstalledPackagesLoader.Instance.LastLoadFinishedUtc = _clock.GetUtcNow().UtcDateTime;
        MaintenanceScheduler.InstallUpdatesAsync = () => Task.CompletedTask;
        Dispatcher.UIThread.BeforeInvoke = null;
        BackupViewModel.LocalBackup = () => Task.FromResult(true);
        BackupViewModel.CloudBackup = () => Task.FromResult(true);

        // The linked scheduler uses an inert DispatcherTimer: no background timer is started.
        MaintenanceScheduler.Start();
        foreach (var kind in MaintenanceTasks.All)
            Configure(kind, enabled: false);
    }

    public void Dispose()
    {
        foreach (var kind in MaintenanceTasks.All)
            Configure(kind, enabled: false);
        MaintenanceScheduler.InstallUpdatesAsync = null;
        Dispatcher.UIThread.BeforeInvoke = null;
        MaintenanceScheduler.Clock = TimeProvider.System;
        UpgradablePackagesLoader.Instance = null;
        InstalledPackagesLoader.Instance = null;
        CoreData.TEST_DataDirectoryOverride = null;
        Directory.Delete(_testRoot, true);
    }

    [Fact]
    public async Task InstallTaskWaitsForEveryLaunchedOperationAndRetriesTheirFailure()
    {
        var kind = MaintenanceTaskKind.InstallUpdates;
        Configure(kind);
        DateTime previous = _clock.GetUtcNow().UtcDateTime.AddDays(-1);
        MaintenanceScheduleStore.SetLastRun(kind, previous);
        var first = new TestOperation();
        var second = new TestOperation();
        int launches = 0;
        MaintenanceScheduler.InstallUpdatesAsync = () =>
        {
            launches++;
            return MaintenanceScheduler.AwaitInstallOperationsAsync([
                MaintenanceScheduler.AwaitInstallOperationAsync(first),
                MaintenanceScheduler.AwaitInstallOperationAsync(second)]);
        };

        Task run = MaintenanceScheduler.RunAsync(kind);
        try
        {
            Assert.False(run.IsCompleted);
            Assert.True(MaintenanceScheduler.IsInstallingUpdates);
            Assert.False(MaintenanceScheduler.IsAutoInstallDue());
            await MaintenanceScheduler.RunAsync(kind);
            Assert.Equal(1, launches);
            Assert.Equal(1, _updates.ReloadCount);

            first.Complete(OperationStatus.Failed);
            Assert.False(run.IsCompleted);
            Assert.Null(MaintenanceScheduleStore.GetLastFailure(kind));
        }
        finally
        {
            first.Complete(OperationStatus.Failed);
            second.Complete(OperationStatus.Succeeded);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.False(MaintenanceScheduler.IsInstallingUpdates);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, MaintenanceScheduleStore.GetLastFailure(kind));
        Assert.Equal(previous, MaintenanceScheduleStore.GetLastRun(kind));
        MaintenanceScheduler.InstallUpdatesAsync = () => { launches++; return Task.CompletedTask; };
        _clock.Advance(TimeSpan.FromMinutes(14));
        MaintenanceScheduler.Evaluate();
        Assert.Equal(1, launches);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await EvaluateAndWaitForTaskAsync(kind);
        Assert.Equal(2, launches);
        Assert.Null(MaintenanceScheduleStore.GetLastFailure(kind));
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, MaintenanceScheduleStore.GetLastRun(kind));
    }

    [Fact]
    public async Task SuccessWaitsForOperationCleanupBeforeClearingPreviousFailure()
    {
        var kind = MaintenanceTaskKind.InstallUpdates;
        Configure(kind);
        MaintenanceScheduleStore.SetLastFailure(kind, _clock.GetUtcNow().UtcDateTime.AddMinutes(-1));
        var operation = new TestOperation { Status = OperationStatus.Succeeded };
        MaintenanceScheduler.InstallUpdatesAsync = () => MaintenanceScheduler.AwaitInstallOperationAsync(operation);

        Task run = MaintenanceScheduler.RunAsync(kind);
        try
        {
            Assert.False(run.IsCompleted);
            Assert.NotNull(MaintenanceScheduleStore.GetLastFailure(kind));
        }
        finally
        {
            operation.Complete(OperationStatus.Succeeded);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Null(MaintenanceScheduleStore.GetLastFailure(kind));
    }

    [Fact]
    public async Task UserCanceledInstallerDoesNotScheduleAnAutomaticRetry()
    {
        var kind = MaintenanceTaskKind.InstallUpdates;
        Configure(kind);
        int launches = 0;
        var operation = new TestOperation();
        operation.Complete(OperationStatus.Canceled);
        MaintenanceScheduler.InstallUpdatesAsync = () =>
        {
            launches++;
            return MaintenanceScheduler.AwaitInstallOperationAsync(operation);
        };

        await MaintenanceScheduler.RunAsync(kind);
        Assert.NotNull(MaintenanceScheduleStore.GetLastFailure(kind));
        _clock.Advance(TimeSpan.FromMinutes(30));
        MaintenanceScheduler.Evaluate();
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task InstallerTaskExceptionIsReportedAsFailureAndRetried()
    {
        var kind = MaintenanceTaskKind.InstallUpdates;
        Configure(kind);
        int launches = 0;
        var operation = new TestOperation();
        operation.Fail(new InvalidOperationException("inert installer task failed"));
        MaintenanceScheduler.InstallUpdatesAsync = () =>
        {
            launches++;
            return MaintenanceScheduler.AwaitInstallOperationAsync(operation);
        };

        await MaintenanceScheduler.RunAsync(kind);
        Assert.NotNull(MaintenanceScheduleStore.GetLastFailure(kind));
        _clock.Advance(TimeSpan.FromMinutes(15));
        await EvaluateAndWaitForTaskAsync(kind);
        Assert.Equal(2, launches);
        Assert.NotNull(MaintenanceScheduleStore.GetLastFailure(kind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedCanceledAndFailedInstallersDoNotRetryEitherPackage(bool canceledFirst)
    {
        var kind = MaintenanceTaskKind.InstallUpdates;
        Configure(kind);
        var canceled = new TestOperation();
        var failed = new TestOperation();
        int launches = 0;
        MaintenanceScheduler.InstallUpdatesAsync = () =>
        {
            launches++;
            return MaintenanceScheduler.AwaitInstallOperationsAsync([
                MaintenanceScheduler.AwaitInstallOperationAsync(canceled),
                MaintenanceScheduler.AwaitInstallOperationAsync(failed)]);
        };

        Task run = MaintenanceScheduler.RunAsync(kind);
        try
        {
            if (canceledFirst) canceled.Complete(OperationStatus.Canceled);
            else failed.Complete(OperationStatus.Failed);
            Assert.False(run.IsCompleted);
            Assert.True(MaintenanceScheduler.IsInstallingUpdates);
        }
        finally
        {
            canceled.Complete(OperationStatus.Canceled);
            failed.Complete(OperationStatus.Failed);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.NotNull(MaintenanceScheduleStore.GetLastFailure(kind));
        _clock.Advance(TimeSpan.FromMinutes(15));
        MaintenanceScheduler.Evaluate();
        Assert.Equal(1, launches);
        Assert.False(MaintenanceScheduler.IsInstallingUpdates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisablingDuringRefreshOrBeforeUiDispatchPreventsInstallerLaunch(bool waitForDispatcher)
    {
        var kind = MaintenanceTaskKind.InstallUpdates;
        Configure(kind);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (waitForDispatcher) Dispatcher.UIThread.BeforeInvoke = () => pending.Task;
        else _updates.Reload = () => pending.Task;
        int launches = 0;
        MaintenanceScheduler.InstallUpdatesAsync = () => { launches++; return Task.CompletedTask; };

        Task run = MaintenanceScheduler.RunAsync(kind);
        try
        {
            Assert.False(run.IsCompleted);
            Configure(kind, enabled: false);
        }
        finally
        {
            pending.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(0, launches);
        Assert.NotNull(MaintenanceScheduleStore.GetLastFailure(kind));
        Assert.False(MaintenanceScheduler.IsInstallingUpdates);
        Configure(kind);
        _clock.Advance(TimeSpan.FromMinutes(15));
        MaintenanceScheduler.Evaluate();
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task ExplicitRunNowMayInstallWhenTheScheduleWasAlreadyDisabled()
    {
        var kind = MaintenanceTaskKind.InstallUpdates;
        Configure(kind, enabled: false);
        int launches = 0;
        MaintenanceScheduler.InstallUpdatesAsync = () => { launches++; return Task.CompletedTask; };

        await MaintenanceScheduler.RunAsync(kind);

        Assert.Equal(1, launches);
        Assert.Null(MaintenanceScheduleStore.GetLastFailure(kind));
    }

    [Fact]
    public async Task InstallerCallbackIsNotCalledWhenUpdateCheckReportsFailure()
    {
        Configure(MaintenanceTaskKind.InstallUpdates);
        _updates.LastLoadReportedFailures = true;
        int launches = 0;
        MaintenanceScheduler.InstallUpdatesAsync = () => { launches++; return Task.CompletedTask; };

        await MaintenanceScheduler.RunAsync(MaintenanceTaskKind.InstallUpdates);

        Assert.Equal(0, launches);
        Assert.NotNull(MaintenanceScheduleStore.GetLastFailure(MaintenanceTaskKind.InstallUpdates));
    }

    [Fact]
    public async Task InstallTaskWaitsForAnExistingUpdateCheckBeforeLaunching()
    {
        Configure(MaintenanceTaskKind.InstallUpdates);
        var loading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _updates.IsLoading = true;
        _updates.CurrentLoad = loading.Task;
        int launches = 0;
        MaintenanceScheduler.InstallUpdatesAsync = () => { launches++; return Task.CompletedTask; };

        Task run = MaintenanceScheduler.RunAsync(MaintenanceTaskKind.InstallUpdates);
        try
        {
            Assert.False(run.IsCompleted);
            Assert.Equal(0, launches);
            Assert.Equal(1, _updates.WaitCount);
            Assert.Equal(0, _updates.ReloadCount);
        }
        finally
        {
            loading.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(1, launches);
    }

    [Theory]
    [InlineData(MaintenanceTaskKind.LocalBackup)]
    [InlineData(MaintenanceTaskKind.CloudBackup)]
    public async Task AtAppStartFailureRetriesAfterFifteenMinutesAndStopsAtTheRetryLimit(MaintenanceTaskKind kind)
    {
        Configure(kind);
        int attempts = 0;
        SetBackup(kind, () => { attempts++; return Task.FromResult(false); });

        MaintenanceScheduler.Evaluate();
        Assert.Equal(0, attempts);
        await MaintenanceScheduler.RunAsync(kind);
        Assert.Equal(1, attempts);
        _clock.Advance(TimeSpan.FromMinutes(14));
        MaintenanceScheduler.Evaluate();
        Assert.Equal(1, attempts);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await EvaluateAndWaitForTaskAsync(kind);
        Assert.Equal(2, attempts);
        _clock.Advance(TimeSpan.FromMinutes(15));
        await EvaluateAndWaitForTaskAsync(kind);
        Assert.Equal(3, attempts);
        _clock.Advance(TimeSpan.FromHours(1));
        MaintenanceScheduler.Evaluate();
        Assert.Equal(3, attempts);
        Assert.NotNull(MaintenanceScheduleStore.GetLastFailure(kind));
    }

    [Fact]
    public async Task DueRetryDoesNotLaunchAgainWhileThePreviousRetryIsBusy()
    {
        var kind = MaintenanceTaskKind.LocalBackup;
        Configure(kind);
        int attempts = 0;
        SetBackup(kind, () => { attempts++; return Task.FromResult(false); });
        await MaintenanceScheduler.RunAsync(kind);
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetBackup(kind, () => { attempts++; return pending.Task; });
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished(object? sender, MaintenanceTaskKind finishedKind)
        {
            if (finishedKind == kind) finished.TrySetResult();
        }
        MaintenanceScheduler.TaskFinished += OnFinished;
        try
        {
            _clock.Advance(TimeSpan.FromMinutes(15));
            MaintenanceScheduler.Evaluate();
            Assert.Equal(2, attempts);
            _clock.Advance(TimeSpan.FromMinutes(15));
            MaintenanceScheduler.Evaluate();
            Assert.Equal(2, attempts);
            Assert.False(finished.Task.IsCompleted);
        }
        finally
        {
            pending.TrySetResult(true);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            MaintenanceScheduler.TaskFinished -= OnFinished;
        }
        Assert.Null(MaintenanceScheduleStore.GetLastFailure(kind));
        MaintenanceScheduler.Evaluate();
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task DisablingAnAtAppStartScheduleClearsItsPendingRetry()
    {
        var kind = MaintenanceTaskKind.LocalBackup;
        Configure(kind);
        int attempts = 0;
        SetBackup(kind, () => { attempts++; return Task.FromResult(false); });
        await MaintenanceScheduler.RunAsync(kind);
        Configure(kind, enabled: false);
        _clock.Advance(TimeSpan.FromMinutes(15));
        MaintenanceScheduler.Evaluate();
        Configure(kind);
        MaintenanceScheduler.Evaluate();
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task TimeBasedRetryStillHonorsItsGraceWindow()
    {
        var kind = MaintenanceTaskKind.LocalBackup;
        Configure(kind, frequency: ScheduleFrequency.Daily, graceMinutes: 10);
        int attempts = 0;
        SetBackup(kind, () => { attempts++; return Task.FromResult(false); });
        await MaintenanceScheduler.RunAsync(kind);
        _clock.Advance(TimeSpan.FromMinutes(15));
        MaintenanceScheduler.Evaluate();
        Assert.Equal(1, attempts);
    }

    private static async Task EvaluateAndWaitForTaskAsync(MaintenanceTaskKind kind)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished(object? sender, MaintenanceTaskKind finishedKind)
        {
            if (finishedKind == kind) finished.TrySetResult();
        }

        MaintenanceScheduler.TaskFinished += OnFinished;
        try
        {
            // Evaluate launches work without awaiting it. Wait for its final state before
            // inspecting settings, advancing the test clock, or removing temporary data.
            MaintenanceScheduler.Evaluate();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            MaintenanceScheduler.TaskFinished -= OnFinished;
        }
    }

    private void Configure(
        MaintenanceTaskKind kind,
        bool enabled = true,
        ScheduleFrequency? frequency = null,
        int graceMinutes = -1)
    {
        MaintenanceScheduleStore.Set(kind, new()
        {
            Enabled = enabled,
            Frequency = frequency ?? MaintenanceTasks.GetDefaultFrequency(kind),
            StartMinutes = (int)_clock.GetLocalNow().TimeOfDay.TotalMinutes,
            GraceMinutes = graceMinutes,
        });
    }

    private static void SetBackup(MaintenanceTaskKind kind, Func<Task<bool>> callback)
    {
        if (kind is MaintenanceTaskKind.LocalBackup)
            BackupViewModel.LocalBackup = callback;
        else
            BackupViewModel.CloudBackup = callback;
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(new DateTime(2099, 1, 5, 9, 0, 0, DateTimeKind.Local));
        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class TestOperation : AbstractOperation
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task MainThread() => _completed.Task;
        public void Fail(Exception error) => _completed.TrySetException(error);
        public void Complete(OperationStatus status)
        {
            Status = status;
            _completed.TrySetResult();
        }
    }
}
