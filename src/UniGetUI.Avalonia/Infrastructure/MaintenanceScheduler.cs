using Avalonia.Threading;
using UniGetUI.Avalonia.ViewModels.Pages.SettingsPages;
using UniGetUI.Core.Logging;
using UniGetUI.Core.Tools.Scheduling;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.PackageLoader;
using UniGetUI.PackageOperations;

namespace UniGetUI.Avalonia.Infrastructure;

internal static class MaintenanceScheduler
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TaskTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan InstalledListMaxAge = TimeSpan.FromMinutes(15);
    private const int MaxRetriesPerOccurrence = 2;

    private static readonly HashSet<MaintenanceTaskKind> RunningTasks = [];
    private static readonly Dictionary<MaintenanceTaskKind, RetryState> Retries = [];
    private static readonly object RetryLock = new();

    private static DispatcherTimer? _timer;
    private static System.Timers.Timer? _headlessTimer;
    private static bool _started;
    private static bool _isHeadless;
    private static volatile bool _updatesWereLoaded;

    internal static TimeProvider Clock { get; set; } = TimeProvider.System;
    internal static Func<Task>? InstallUpdatesAsync { get; set; }

    public static bool IsInstallingUpdates
    {
        get
        {
            lock (RunningTasks)
                return RunningTasks.Contains(MaintenanceTaskKind.InstallUpdates);
        }
    }

    private sealed record RetryState(int Attempts, DateTime NextAttemptLocal);

    public static event EventHandler<MaintenanceTaskKind>? TaskFinished;

    public static void Start()
    {
        if (_started) return;
        _started = true;

        WatchUpdateLoads();
        MaintenanceScheduleStore.Changed += (_, kind) => ClearRetries(kind);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TickInterval };
        _timer.Tick += (_, _) => Evaluate();
        _timer.Start();
    }

    public static void StartHeadless()
    {
        if (_started) return;
        _started = true;
        _isHeadless = true;
        _updatesWereLoaded = true;

        WatchUpdateLoads();
        MaintenanceScheduleStore.Changed += (_, kind) => ClearRetries(kind);

        _headlessTimer = new System.Timers.Timer(TickInterval.TotalMilliseconds) { AutoReset = true };
        _headlessTimer.Elapsed += (_, _) => Evaluate();
        _headlessTimer.Start();
        Logger.ImportantInfo("The maintenance scheduler is running headless, only update checks are scheduled");
    }

    public static bool IsAutoInstallDue()
    {
        var schedule = MaintenanceScheduleStore.Get(MaintenanceTaskKind.InstallUpdates);
        return !IsInstallingUpdates && schedule.Enabled
            && schedule.Frequency is ScheduleFrequency.AfterEveryUpdateCheck;
    }

    public static bool ShouldRunAtAppStart(MaintenanceTaskKind kind)
    {
        var schedule = MaintenanceScheduleStore.Get(kind);
        return schedule.Enabled && schedule.Frequency is ScheduleFrequency.AtAppStart;
    }

    public static async Task RunAsync(MaintenanceTaskKind kind)
    {
        lock (RunningTasks)
        {
            if (!RunningTasks.Add(kind))
                return;
        }

        DateTime? previousRun = MaintenanceScheduleStore.GetLastRun(kind);
        bool abandoned = false;

        try
        {
            MaintenanceScheduleStore.SetLastRun(kind, Clock.GetUtcNow().UtcDateTime);
            Logger.ImportantInfo($"Running the maintenance task \"{MaintenanceTasks.GetId(kind)}\"");

            Task work = ExecuteAsync(kind);
            if (!await TaskCompletion.CompletesWithin(work, TaskTimeout))
            {
                abandoned = true;
                ObserveAbandoned(work, kind);
                throw new TimeoutException(
                    $"The maintenance task \"{MaintenanceTasks.GetId(kind)}\" did not finish within {TaskTimeout.TotalMinutes:0} minutes"
                );
            }

            ClearRetries(kind);
            MaintenanceScheduleStore.ClearLastFailure(kind);
        }
        catch (OperationCanceledException ex)
        {
            Logger.Warn($"The maintenance task \"{MaintenanceTasks.GetId(kind)}\" was canceled");
            Logger.Warn(ex.Message);
            MaintenanceScheduleStore.SetLastFailure(kind, Clock.GetUtcNow().UtcDateTime);
            // A user-canceled installer must not be launched again by a pending retry.
            ClearRetries(kind);
        }
        catch (Exception ex)
        {
            Logger.Error($"The maintenance task \"{MaintenanceTasks.GetId(kind)}\" failed");
            Logger.Error(ex);
            MaintenanceScheduleStore.SetLastFailure(kind, Clock.GetUtcNow().UtcDateTime);
            ScheduleRetry(kind, previousRun);
        }
        finally
        {
            if (!abandoned)
            {
                lock (RunningTasks)
                    RunningTasks.Remove(kind);
            }

            if (_isHeadless)
                TaskFinished?.Invoke(null, kind);
            else
                Dispatcher.UIThread.Post(() => TaskFinished?.Invoke(null, kind));
        }
    }

    private static async Task ExecuteAsync(MaintenanceTaskKind kind)
    {
        switch (kind)
        {
            case MaintenanceTaskKind.CheckForUpdates:
                await ReloadUpdatesAsync();
                break;

            case MaintenanceTaskKind.InstallUpdates:
                var installUpdates = InstallUpdatesAsync
                    ?? throw new InvalidOperationException("The scheduled update handler is unavailable");
                bool enabledWhenStarted = MaintenanceScheduleStore.Get(kind).Enabled;
                await ReloadUpdatesAsync();
                // Loader events only notify the page. This callback owns the installers and
                // must remain awaited until their terminal results and cleanup are known.
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    // Honor disabling an in-flight schedule, including while waiting for the
                    // UI thread. An explicit Run now that started disabled is still allowed.
                    if (enabledWhenStarted && !MaintenanceScheduleStore.Get(kind).Enabled)
                        throw new OperationCanceledException("The scheduled package updates were disabled");
                    await installUpdates();
                });
                break;

            case MaintenanceTaskKind.LocalBackup:
                await PrepareInstalledPackagesForBackupAsync();
                if (!await BackupViewModel.DoLocalBackupStatic())
                    throw new InvalidOperationException("The local backup did not complete, see the log for details");
                break;

            case MaintenanceTaskKind.CloudBackup:
                await PrepareInstalledPackagesForBackupAsync();
                if (!await BackupViewModel.DoCloudBackupStatic())
                    throw new InvalidOperationException("The cloud backup did not complete, see the log for details");
                break;
        }
    }

    internal static async Task AwaitInstallOperationAsync(AbstractOperation operation)
    {
        await operation.MainThread();
        if (operation.Status is OperationStatus.Canceled)
            throw new OperationCanceledException("A scheduled package update was canceled");
        if (operation.Status is not OperationStatus.Succeeded)
            throw new InvalidOperationException("A scheduled package update failed, see the operation log for details");
    }

    internal static async Task AwaitInstallOperationsAsync(IReadOnlyList<Task> operations)
    {
        try
        {
            await Task.WhenAll(operations);
        }
        catch (Exception) when (operations.Any(operation => operation.IsCanceled))
        {
            // WhenAll otherwise prioritizes a fault over cancellation in a mixed batch.
            // Retrying that whole batch would relaunch packages the user just canceled.
            throw new OperationCanceledException("A scheduled package update was canceled; the batch will not be retried");
        }
    }

    private static void ObserveAbandoned(Task work, MaintenanceTaskKind kind)
    {
        _ = work.ContinueWith(
            finished =>
            {
                lock (RunningTasks)
                    RunningTasks.Remove(kind);

                Logger.Warn(
                    $"The abandoned maintenance task \"{MaintenanceTasks.GetId(kind)}\" ended with {finished.Exception?.GetBaseException().Message ?? "no result"}"
                );
            },
            TaskScheduler.Default
        );
    }

    private static void WatchUpdateLoads()
    {
        if (UpgradablePackagesLoader.Instance is not { } loader)
            return;

        _updatesWereLoaded |= loader.IsLoaded;
        loader.FinishedLoading += (_, _) =>
        {
            _updatesWereLoaded = true;
            MaintenanceScheduleStore.SetLastRun(MaintenanceTaskKind.CheckForUpdates, Clock.GetUtcNow().UtcDateTime);
        };
    }

    internal static void Evaluate()
    {
        DateTime now = Clock.GetLocalNow().DateTime;

        foreach (var kind in MaintenanceTasks.All)
        {
            try
            {
                if (_isHeadless && kind is not MaintenanceTaskKind.CheckForUpdates)
                    continue;

                if (!IsReadyFor(kind))
                    continue;

                var schedule = MaintenanceScheduleStore.Get(kind);
                if (!schedule.Enabled)
                    continue;

                if (TryGetPendingRetry(kind, out var retry))
                {
                    if (retry.NextAttemptLocal > now)
                        continue;

                    if (ScheduleEvaluator.IsTimeBased(schedule.Frequency)
                        && !ScheduleEvaluator.IsWithinGrace(schedule, now))
                    {
                        ClearRetries(kind);
                        continue;
                    }
                }
                else if (!IsClockDriven(schedule.Frequency)
                    || !ScheduleEvaluator.IsDue(schedule, MaintenanceScheduleStore.GetLastRun(kind), now))
                {
                    continue;
                }

                _ = RunAsync(kind);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }
    }

    private static bool IsClockDriven(ScheduleFrequency frequency)
        => frequency is ScheduleFrequency.Interval || ScheduleEvaluator.IsTimeBased(frequency);

    private static bool IsReadyFor(MaintenanceTaskKind kind) => kind switch
    {
        MaintenanceTaskKind.CheckForUpdates or MaintenanceTaskKind.InstallUpdates => _updatesWereLoaded,
        _ => InstalledPackagesLoader.Instance is { IsLoaded: true },
    };

    private static bool TryGetPendingRetry(MaintenanceTaskKind kind, out RetryState retry)
    {
        lock (RetryLock)
            return Retries.TryGetValue(kind, out retry!);
    }

    private static void ClearRetries(MaintenanceTaskKind kind)
    {
        lock (RetryLock)
            Retries.Remove(kind);
    }

    private static void ScheduleRetry(MaintenanceTaskKind kind, DateTime? previousRun)
    {
        int attempts;
        lock (RetryLock)
        {
            attempts = (Retries.TryGetValue(kind, out var retry) ? retry.Attempts : 0) + 1;

            if (attempts > MaxRetriesPerOccurrence)
            {
                Retries.Remove(kind);
                Logger.Warn($"The maintenance task \"{MaintenanceTasks.GetId(kind)}\" keeps failing, no further retries until its next occurrence");
                return;
            }

            Retries[kind] = new RetryState(attempts, Clock.GetLocalNow().DateTime + RetryDelay);
        }

        if (previousRun is { } stamp)
            MaintenanceScheduleStore.SetLastRun(kind, stamp);
        else
            MaintenanceScheduleStore.ClearLastRun(kind);

        Logger.Warn($"Retrying the maintenance task \"{MaintenanceTasks.GetId(kind)}\" in {RetryDelay.TotalMinutes:0} minutes (attempt {attempts} of {MaxRetriesPerOccurrence})");
    }

    private static async Task ReloadUpdatesAsync()
    {
        if (UpgradablePackagesLoader.Instance is not { } loader)
            throw new InvalidOperationException("The update list is unavailable, see the log for details");

        if (loader.IsLoading)
            await loader.WaitForCurrentLoadAsync();
        else
            await loader.ReloadPackages();

        if (loader.LastLoadReportedFailures)
            throw new InvalidOperationException("The update check reported failures, see the log for details");
    }

    private static async Task PrepareInstalledPackagesForBackupAsync()
    {
        if (InstalledPackagesLoader.Instance is not { } loader)
            throw new InvalidOperationException("The installed package list is unavailable, see the log for details");

        if (loader.IsLoading)
            await loader.WaitForCurrentLoadAsync();
        else if (!loader.IsLoaded || IsInstalledListStale(loader))
            await loader.ReloadPackages();

        if (!loader.Any())
            throw new InvalidOperationException("The installed package list is empty, refusing to overwrite the previous backup");

        if (loader.LastLoadReportedFailures)
            throw new InvalidOperationException("A package manager failed to list its packages, refusing to overwrite the previous backup with an incomplete list");
    }

    private static bool IsInstalledListStale(InstalledPackagesLoader loader)
        => loader.LastLoadFinishedUtc is not { } finished
            || Clock.GetUtcNow().UtcDateTime - finished > InstalledListMaxAge;
}
