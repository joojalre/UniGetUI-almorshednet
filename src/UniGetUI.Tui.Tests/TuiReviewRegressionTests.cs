using NUnit.Framework;
using UniGetUI.Core.Data;
using UniGetUI.Core.SettingsEngine;
using UniGetUI.Core.Tools.Scheduling;
using UniGetUI.PackageEngine.Classes.Packages.Classes;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.PackageClasses;
using UniGetUI.PackageEngine.PackageLoader;
using UniGetUI.PackageOperations;
using UniGetUI.Tui.Infrastructure;
using UniGetUI.Tui.Views.Dialogs;
using NAssert = NUnit.Framework.Assert;

namespace UniGetUI.Tui.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class TuiReviewRegressionTests : TuiE2ETestBase
{
    [TestCase("success")]
    [TestCase("failure")]
    [TestCase("cancel")]
    public async Task ScheduledUpdatesAwaitTerminalResultAndSuppressLoadEventLaunch(string result)
    {
        await ResetAsync(TuiPageIds.Updates);
        const MaintenanceTaskKind kind = MaintenanceTaskKind.InstallUpdates;
        var original = TuiMaintenanceScheduler.InstallUpdatesAsync;
        var schedule = MaintenanceScheduleStore.Get(kind).Clone();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        TuiMaintenanceScheduler.InstallUpdatesAsync = () => { calls++; entered.TrySetResult(); return finish.Task; };
        Task? run = null;
        try
        {
            MaintenanceScheduleStore.Set(kind, new MaintenanceTaskSchedule { Enabled = true, Frequency = ScheduleFrequency.AfterEveryUpdateCheck });
            MaintenanceScheduleStore.SetLastFailure(kind, DateTime.UtcNow.AddHours(-1));
            run = await OnUi(() => TuiMaintenanceScheduler.RunAsync(kind));
            await entered.Task.WaitAsync(DefaultTimeout);
            NAssert.That(run.IsCompleted, Is.False);
            NAssert.That(TuiMaintenanceScheduler.IsInstallingUpdates, Is.True);
            NAssert.That(MaintenanceScheduleStore.GetLastFailure(kind), Is.Not.Null, "no premature success");
            await (await OnUi(() => TuiUpdatesPolicy.OnUpdatesLoadedAsync(firstLoad: false)));
            await (await OnUi(() => TuiMaintenanceScheduler.RunAsync(kind)));
            NAssert.That(calls, Is.EqualTo(1));
            NAssert.That(await OnUi(TuiOperationRegistry.Snapshot), Is.Empty, "load callback must not enqueue a second batch");
            if (result == "failure") finish.TrySetException(new IOException("controlled installer failure"));
            else if (result == "cancel") finish.TrySetCanceled();
            else finish.TrySetResult();
            await run.WaitAsync(DefaultTimeout);
            NAssert.That(TuiMaintenanceScheduler.IsInstallingUpdates, Is.False);
            NAssert.That(MaintenanceScheduleStore.GetLastFailure(kind) is null, Is.EqualTo(result == "success"));
            NAssert.That(TuiMaintenanceScheduler.HasRetry(kind), Is.EqualTo(result == "failure"));
        }
        finally
        {
            finish.TrySetResult();
            if (run is not null) await run.WaitAsync(DefaultTimeout);
            TuiMaintenanceScheduler.InstallUpdatesAsync = original;
            MaintenanceScheduleStore.Set(kind, schedule);
        }
    }

    [TestCase("success")]
    [TestCase("failure")]
    [TestCase("cancel")]
    [TestCase("reconfigured-failure")]
    public async Task TimedOutInstallRetainsOwnershipUntilOriginalWorkEnds(string result)
    {
        await ResetAsync(TuiPageIds.Updates);
        const MaintenanceTaskKind kind = MaintenanceTaskKind.InstallUpdates;
        var original = TuiMaintenanceScheduler.InstallUpdatesAsync;
        var timeout = TuiMaintenanceScheduler.TaskTimeout;
        var schedule = MaintenanceScheduleStore.Get(kind).Clone();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        TuiMaintenanceScheduler.InstallUpdatesAsync = () => { calls++; entered.TrySetResult(); return finish.Task; };
        TuiMaintenanceScheduler.TaskTimeout = TimeSpan.FromSeconds(2);
        try
        {
            MaintenanceScheduleStore.Set(kind, new MaintenanceTaskSchedule
            {
                Enabled = true,
                Frequency = ScheduleFrequency.Daily,
                StartMinutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute,
            });
            DateTime previousRun = DateTime.UtcNow.AddDays(-2);
            MaintenanceScheduleStore.SetLastRun(kind, previousRun);
            Task run = await OnUi(() => TuiMaintenanceScheduler.RunAsync(kind));
            await entered.Task.WaitAsync(DefaultTimeout);
            await run.WaitAsync(DefaultTimeout);
            NAssert.That(TuiMaintenanceScheduler.IsInstallingUpdates, Is.True);
            NAssert.That(MaintenanceScheduleStore.GetLastFailure(kind), Is.Not.Null);
            await TuiMaintenanceScheduler.RunAsync(kind);
            NAssert.That(calls, Is.EqualTo(1), "timeout must not permit an overlapping installer batch");
            DateTime? attemptedRun = MaintenanceScheduleStore.GetLastRun(kind);
            NAssert.That(attemptedRun, Is.GreaterThan(previousRun));
            NAssert.That(TuiMaintenanceScheduler.HasRetry(kind), Is.False, "no retry while original work is owned");
            if (result == "reconfigured-failure")
                MaintenanceScheduleStore.Set(kind, new MaintenanceTaskSchedule { Enabled = false });
            if (result.Contains("failure", StringComparison.Ordinal)) finish.TrySetException(new IOException("late installer failure"));
            else if (result == "cancel") finish.TrySetCanceled();
            else finish.TrySetResult();
            await WaitUntilAsync(() => !TuiMaintenanceScheduler.IsInstallingUpdates, "late work to release ownership");
            NAssert.That(TuiMaintenanceScheduler.HasRetry(kind), Is.EqualTo(result == "failure"));
            NAssert.That(MaintenanceScheduleStore.GetLastRun(kind), Is.EqualTo(result == "failure" ? previousRun : attemptedRun));
            if (result is "success" or "cancel")
            {
                var occurrence = MaintenanceScheduleStore.Get(kind);
                occurrence.ConfiguredAtUtc = previousRun;
                NAssert.That(ScheduleEvaluator.IsDue(occurrence, previousRun, DateTime.Now), Is.True,
                    "the rolled-back occurrence would be due");
                NAssert.That(ScheduleEvaluator.IsDue(occurrence, attemptedRun, DateTime.Now), Is.False,
                    "completed or manually canceled occurrence must not immediately relaunch");
            }
            NAssert.That(MaintenanceScheduleStore.GetLastFailure(kind), Is.Not.Null, "late success does not erase timeout failure");
        }
        finally
        {
            finish.TrySetResult();
            await WaitUntilAsync(() => !TuiMaintenanceScheduler.IsInstallingUpdates, "owned work to finish");
            TuiMaintenanceScheduler.InstallUpdatesAsync = original;
            TuiMaintenanceScheduler.TaskTimeout = timeout;
            MaintenanceScheduleStore.Set(kind, schedule);
        }
    }

    [TestCase(OperationVeredict.Success)]
    [TestCase(OperationVeredict.Failure)]
    [TestCase(OperationVeredict.Canceled)]
    public async Task ScheduledOperationWaitsForCleanupAndPropagatesVerdict(OperationVeredict verdict)
    {
        await ResetAsync(TuiPageIds.Operations);
        using var operation = new ControlledOperation();
        Task run = TuiPackageActions.AwaitUpdateOperationAsync(operation);
        try
        {
            await operation.Entered.Task.WaitAsync(DefaultTimeout);
            NAssert.That(operation.Runs, Is.EqualTo(1));
            operation.Result.TrySetResult(verdict);
            await operation.CleanupEntered.Task.WaitAsync(DefaultTimeout);
            NAssert.That(run.IsCompleted, Is.False, "terminal status precedes terminal cleanup");
            operation.ReleaseCleanup.TrySetResult();
            if (verdict == OperationVeredict.Success) await run.WaitAsync(DefaultTimeout);
            else if (verdict == OperationVeredict.Canceled)
                NAssert.ThrowsAsync<OperationCanceledException>(async () => await run.WaitAsync(DefaultTimeout));
            else NAssert.ThrowsAsync<InvalidOperationException>(async () => await run.WaitAsync(DefaultTimeout));
        }
        finally
        {
            operation.Result.TrySetResult(OperationVeredict.Canceled);
            operation.ReleaseCleanup.TrySetResult();
            try { await run.WaitAsync(DefaultTimeout); } catch (Exception) { }
            await OnUi(() => TuiOperationRegistry.Remove(operation));
        }
    }

    [Test]
    public async Task MixedCanceledFailedBatchDrainsEveryOperationBeforeReportingCancellation()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task batch = TuiPackageActions.AwaitUpdateOperationsAsync([
            Task.FromException(new IOException("failure")), Task.FromCanceled(new CancellationToken(true)), pending.Task]);
        NAssert.That(batch.IsCompleted, Is.False);
        pending.SetResult();
        NAssert.ThrowsAsync<OperationCanceledException>(async () => await batch);
    }

    [Test]
    public async Task OptionsWriteFailureKeepsDialogOpenAndCanBeRetriedWithoutLaunching()
    {
        await ResetAsync(TuiPageIds.Updates);
        var package = UpgradablePackagesLoader.Instance.Packages.First();
        var options = await InstallOptionsFactory.LoadForPackageAsync(package);
        await InstallOptionsFactory.SaveForPackageOrThrowAsync(options, package);
        string path = Directory.GetFiles(CoreData.UniGetUIInstallationOptionsDirectory, "PackageOptions.*")
            .Single(file => Path.GetFileName(file).Contains("." + package.Id + "_", StringComparison.Ordinal));
        NAssert.That(Path.GetFullPath(path).StartsWith(Path.GetFullPath(Env.SandboxDirectory), StringComparison.OrdinalIgnoreCase), Is.True);
        byte[] original = await File.ReadAllBytesAsync(path);
        File.Delete(path);
        Directory.CreateDirectory(path); // deterministic write failure, including administrator runs
        Task<bool>? answer = null;
        var errors = new List<TuiNotification>();
        void Notification(TuiNotification notification) { if (notification.Severity == TuiNotificationSeverity.Error) errors.Add(notification); }
        TuiNotifications.NotificationRaised += Notification;
        try
        {
            answer = await OnUi(() => InstallOptionsDialog.ShowForPackageAsync(package, OperationType.Update));
            await WaitUntilAsync(() => TuiModal.Top is InstallOptionsDialog { StateLoaded: true }, "options dialog");
            var dialog = await OnUi(() => (InstallOptionsDialog)TuiModal.Top!);
            await (await OnUi(() => dialog.SaveAsync(proceed: true)));
            NAssert.That(answer.IsCompleted, Is.False, "caller must not receive approval after failed persistence");
            NAssert.That(await OnUi(() => dialog.IsClosed), Is.False);
            NAssert.That(errors, Has.Count.EqualTo(1));
            NAssert.That(await OnUi(TuiOperationRegistry.Snapshot), Is.Empty);
            Directory.Delete(path);
            await File.WriteAllBytesAsync(path, original);
            await (await OnUi(() => dialog.SaveAsync(proceed: true)));
            NAssert.That(await answer.WaitAsync(DefaultTimeout), Is.True);
        }
        finally
        {
            TuiNotifications.NotificationRaised -= Notification;
            await OnUi(TuiModal.CloseAll);
            if (Directory.Exists(path)) Directory.Delete(path);
            await File.WriteAllBytesAsync(path, original);
        }
    }

    [TestCase(Settings.K.AutoUpdatedPackages, "Automatically update this package")]
    [TestCase(Settings.K.IgnoredPackageUpdates, "Ignore future updates for this package")]
    [TestCase(Settings.K.KillProcessesThatRefuseToDie, "Try to kill the processes that refuse to close when requested to")]
    public async Task RequiredSettingWriteFailureKeepsApprovalPendingAndCachedValueUnchanged(Settings.K key, string field)
    {
        await ResetAsync(TuiPageIds.Updates);
        var package = UpgradablePackagesLoader.Instance.Packages.First();
        string id = AutoUpdatesDatabase.GetIdForPackage(package);
        AutoUpdatesDatabase.RemoveOrThrow(id);
        IgnoredUpdatesDatabase.RemoveOrThrow(id);
        Settings.SetOrThrow(Settings.K.KillProcessesThatRefuseToDie, false);
        string path = Path.Combine(CoreData.UniGetUIUserConfigurationDirectory, key.ToString()
            + (key == Settings.K.KillProcessesThatRefuseToDie ? "" : ".json"));
        NAssert.That(Path.GetFullPath(path).StartsWith(Path.GetFullPath(Env.SandboxDirectory), StringComparison.OrdinalIgnoreCase), Is.True);
        byte[]? original = File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
        Task<bool>? answer = null;
        try
        {
            answer = await OnUi(() => InstallOptionsDialog.ShowForPackageAsync(package, OperationType.Update));
            await WaitUntilAsync(() => TuiModal.Top is InstallOptionsDialog { StateLoaded: true }, "options dialog");
            await FocusFieldAsync(field);
            await Key(global::Avalonia.Input.Key.Space);
            var dialog = await OnUi(() => (InstallOptionsDialog)TuiModal.Top!);
            if (File.Exists(path)) File.Delete(path);
            Directory.CreateDirectory(path);
            await (await OnUi(() => dialog.SaveAsync(proceed: true)));
            NAssert.That(answer.IsCompleted, Is.False);
            NAssert.That(await OnUi(() => dialog.IsClosed), Is.False);
            NAssert.That(await OnUi(TuiOperationRegistry.Snapshot), Is.Empty);
            bool cached = key switch
            {
                Settings.K.AutoUpdatedPackages => AutoUpdatesDatabase.IsAutoUpdated(id),
                Settings.K.IgnoredPackageUpdates => IgnoredUpdatesDatabase.HasUpdatesIgnored(id),
                _ => Settings.Get(Settings.K.KillProcessesThatRefuseToDie),
            };
            NAssert.That(cached, Is.False, "a failed write must not publish the attempted value in cache");
            Directory.Delete(path);
            if (original is not null) await File.WriteAllBytesAsync(path, original);
            await (await OnUi(() => dialog.SaveAsync(proceed: true)));
            NAssert.That(await answer.WaitAsync(DefaultTimeout), Is.True, "same dialog can retry after storage recovers");
        }
        finally
        {
            await OnUi(TuiModal.CloseAll);
            if (Directory.Exists(path)) Directory.Delete(path);
            if (original is not null) await File.WriteAllBytesAsync(path, original);
            else if (File.Exists(path)) File.Delete(path);
            AutoUpdatesDatabase.RemoveOrThrow(id);
            IgnoredUpdatesDatabase.RemoveOrThrow(id);
            Settings.SetOrThrow(Settings.K.KillProcessesThatRefuseToDie, false);
        }
    }

    [Test]
    public async Task UncheckedIgnoredOptionDoesNotApproveUnreadableUncachedState()
    {
        await ResetAsync(TuiPageIds.Updates);
        var package = UpgradablePackagesLoader.Instance.Packages.First();
        IgnoredUpdatesDatabase.RemoveOrThrow(IgnoredUpdatesDatabase.GetIgnoredIdForPackage(package));
        string path = Path.Combine(CoreData.UniGetUIUserConfigurationDirectory, "IgnoredPackageUpdates.json");
        NAssert.That(Path.GetFullPath(path).StartsWith(Path.GetFullPath(Env.SandboxDirectory), StringComparison.OrdinalIgnoreCase), Is.True);
        byte[]? original = File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
        // Test-only cache eviction models a first read after external storage corruption.
        var cache = (System.Collections.Concurrent.ConcurrentDictionary<Settings.K, Dictionary<object, object?>>)
            typeof(Settings).GetField("_dictionarySettings", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        try
        {
            Task<bool> answer = await OnUi(() => InstallOptionsDialog.ShowForPackageAsync(package, OperationType.Update));
            await WaitUntilAsync(() => TuiModal.Top is InstallOptionsDialog { StateLoaded: true }, "options dialog");
            var dialog = await OnUi(() => (InstallOptionsDialog)TuiModal.Top!);
            cache.TryRemove(Settings.K.IgnoredPackageUpdates, out _);
            await File.WriteAllTextAsync(path, "{invalid-json");
            await (await OnUi(() => dialog.SaveAsync(proceed: true)));
            NAssert.That(answer.IsCompleted, Is.False);
            NAssert.That(await OnUi(() => dialog.IsClosed), Is.False);
            NAssert.That(await OnUi(TuiOperationRegistry.Snapshot), Is.Empty);
            NAssert.That(await File.ReadAllTextAsync(path), Is.EqualTo("{invalid-json"));
            NAssert.That(cache.ContainsKey(Settings.K.IgnoredPackageUpdates), Is.False);
            await File.WriteAllTextAsync(path, "{}");
            await (await OnUi(() => dialog.SaveAsync(proceed: true)));
            NAssert.That(await answer.WaitAsync(DefaultTimeout), Is.True);
        }
        finally
        {
            await OnUi(TuiModal.CloseAll);
            if (original is not null) await File.WriteAllBytesAsync(path, original);
            else if (File.Exists(path)) File.Delete(path);
            cache.TryRemove(Settings.K.IgnoredPackageUpdates, out _);
        }
    }

    private sealed class ControlledOperation : AbstractOperation
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<OperationVeredict> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Runs { get; private set; }
        public ControlledOperation() : base(queue_enabled: false)
        {
            Metadata.Title = "controlled scheduled update";
            Metadata.Status = "running";
            Metadata.OperationInformation = "inert test operation";
            Metadata.SuccessTitle = "completed";
            Metadata.SuccessMessage = "completed";
            Metadata.FailureTitle = "failed";
            Metadata.FailureMessage = "failed";
        }
        protected override void ApplyRetryAction(string retryMode) { }
        protected override Task<OperationVeredict> PerformOperation() { Runs++; Entered.TrySetResult(); return Result.Task; }
        protected override void OnRunCompleted() { CleanupEntered.TrySetResult(); ReleaseCleanup.Task.GetAwaiter().GetResult(); }
        public override Task<Uri> GetOperationIcon() => Task.FromResult(new Uri("https://example.invalid/icon"));
    }
}
