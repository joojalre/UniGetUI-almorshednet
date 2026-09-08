// Only the scheduler source is linked into this test project. These inert adapters keep
// its tests independent of the app, package managers, installers, backups and UI loop.
namespace Avalonia.Threading
{
    internal enum DispatcherPriority { Background }

    internal sealed class DispatcherTimer(DispatcherPriority priority)
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick;
        public void Start() { _ = priority; }
        public void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
    }

    internal sealed class Dispatcher
    {
        public static Dispatcher UIThread { get; } = new();
        public Func<Task>? BeforeInvoke { get; set; }
        public void Post(Action action) => action();
        public async Task InvokeAsync(Func<Task> action)
        {
            if (BeforeInvoke is { } beforeInvoke) await beforeInvoke();
            await action();
        }
    }
}

namespace UniGetUI.Avalonia.ViewModels.Pages.SettingsPages
{
    internal static class BackupViewModel
    {
        public static Func<Task<bool>> LocalBackup { get; set; } = () => Task.FromResult(true);
        public static Func<Task<bool>> CloudBackup { get; set; } = () => Task.FromResult(true);
        public static Task<bool> DoLocalBackupStatic() => LocalBackup();
        public static Task<bool> DoCloudBackupStatic() => CloudBackup();
    }
}

namespace UniGetUI.PackageEngine.PackageLoader
{
    internal sealed class UpgradablePackagesLoader
    {
        public static UpgradablePackagesLoader? Instance { get; set; }
        public bool IsLoaded { get; set; } = true;
        public bool IsLoading { get; set; }
        public bool LastLoadReportedFailures { get; set; }
        public int ReloadCount { get; private set; }
        public int WaitCount { get; private set; }
        public Func<Task> Reload { get; set; } = () => Task.CompletedTask;
        public Task CurrentLoad { get; set; } = Task.CompletedTask;
        public event EventHandler? FinishedLoading;

        public async Task ReloadPackages()
        {
            ReloadCount++;
            await Reload();
            FinishedLoading?.Invoke(this, EventArgs.Empty);
        }

        public Task WaitForCurrentLoadAsync()
        {
            WaitCount++;
            return CurrentLoad;
        }
    }

    internal sealed class InstalledPackagesLoader : List<object>
    {
        public static InstalledPackagesLoader? Instance { get; set; }
        public bool IsLoaded { get; set; } = true;
        public bool IsLoading { get; set; }
        public bool LastLoadReportedFailures { get; set; }
        public DateTime? LastLoadFinishedUtc { get; set; }
        public Task ReloadPackages() => Task.CompletedTask;
        public Task WaitForCurrentLoadAsync() => Task.CompletedTask;
    }
}

namespace UniGetUI.PackageOperations
{
    internal abstract class AbstractOperation
    {
        public UniGetUI.PackageEngine.Enums.OperationStatus Status { get; set; }
        public abstract Task MainThread();
    }
}
