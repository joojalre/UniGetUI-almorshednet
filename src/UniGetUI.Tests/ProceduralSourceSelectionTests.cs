using System.Collections.Specialized;
using Avalonia;
using Avalonia.Headless;
using UniGetUI.Avalonia.ViewModels.Pages;
using UniGetUI.Core.Data;
using UniGetUI.PackageEngine.Classes.Manager;
using UniGetUI.PackageEngine.Enums;
using UniGetUI.PackageEngine.Interfaces;
using UniGetUI.PackageEngine.Managers.ScoopManager;
using UniGetUI.PackageEngine.PackageClasses;
using UniGetUI.PackageEngine.PackageLoader;
using CoreSettings = UniGetUI.Core.SettingsEngine.Settings;

namespace UniGetUI.Tests;

public sealed class ProceduralSourceSelectionTests : IDisposable
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.StartNew(typeof(Application));
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), nameof(ProceduralSourceSelectionTests), Guid.NewGuid().ToString("N"));

    public ProceduralSourceSelectionTests()
    {
        CoreData.TEST_DataDirectoryOverride = Path.Combine(_testRoot, "Data");
        Directory.CreateDirectory(CoreData.UniGetUIUserConfigurationDirectory);
        CoreSettings.ResetSettings();
    }

    public void Dispose()
    {
        _session.Dispose();
        CoreSettings.ResetSettings();
        CoreData.TEST_DataDirectoryOverride = null;
        Directory.Delete(_testRoot, recursive: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FirstVirtualSourceBatchDoesNotRefilterOrDuplicateRows(bool existingRow) => _session.Dispatch(() =>
    {
        var manager = new Scoop();
        var loader = new TestLoader();
        var page = new PackagesPageViewModel(new PackagesPageData
        {
            Loader = loader,
            PageName = "procedural-source-test",
            PageTitle = "Test",
            IconName = "search",
            PageRole = OperationType.Uninstall,
            NoPackages_BackgroundText = "Empty",
            NoMatches_BackgroundText = "No matches",
            NoPackages_SubtitleText_Base = "{0} packages",
            MainSubtitle_StillLoading = "Loading",
        });
        var regular = new ManagerSource(manager, "regular", new Uri("https://example.test/feed"));
        var local = new ManagerSource(manager, "local", new Uri("file:///synthetic"), isVirtualManager: true);
        var existing = new Package("Middle", "test.middle", "1", regular, manager);
        PackageWrapper? retained = null;
        if (existingRow)
        {
            loader.Emit([existing], []);
            retained = Assert.Single(page.FilteredPackages);
            retained.IsChecked = true;
        }
        var first = new Package("Alpha", "test.alpha", "1", local, manager);
        var last = new Package("Zulu", "test.zulu", "1", local, manager);
        var actions = new List<NotifyCollectionChangedAction>();
        page.FilteredPackages.CollectionChanged += (_, args) => actions.Add(args.Action);
        loader.Emit([last, first], []);

        Assert.Equal(existingRow ? 3 : 2, page.FilteredPackages.Count);
        Assert.Equal(existingRow ? ["Alpha", "Middle", "Zulu"] : ["Alpha", "Zulu"],
            page.FilteredPackages.Select(row => row.Package.Name));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
        Assert.Equal(2, actions.Count(action => action == NotifyCollectionChangedAction.Add));
        if (retained is not null)
        {
            Assert.Same(retained, page.FilteredPackages[1]);
            Assert.True(retained.IsChecked);
        }
        var localRoot = Assert.Single(page.SourceNodes, node => node.Children.Any(child => child.Source == "local"));
        Assert.True(localRoot.IsSelected);
        Assert.All(localRoot.Children, child => Assert.True(child.IsSelected));

        actions.Clear();
        loader.Emit([], []);
        Assert.Empty(actions);
        var replacement = new Package("Beta", "test.beta", "1", local, manager);
        loader.Emit([replacement], [first]);
        Assert.Equal(existingRow ? ["Beta", "Middle", "Zulu"] : ["Beta", "Zulu"],
            page.FilteredPackages.Select(row => row.Package.Name));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
        // User source filtering must resume after the procedural batch finishes.
        localRoot.IsSelected = false;
        Assert.Equal(existingRow ? 1 : 0, page.FilteredPackages.Count);
        localRoot.IsSelected = true;
        Assert.Equal(existingRow ? 3 : 2, page.FilteredPackages.Count);
        foreach (var wrapper in page.FilteredPackages) wrapper.Dispose();
    }, CancellationToken.None);

    private sealed class TestLoader() : AbstractPackageLoader([], "procedural-source-test", true, true, false, false)
    {
        public void Emit(IReadOnlyList<IPackage> added, IReadOnlyList<IPackage> removed)
            => InvokePackagesChangedEvent(true, added, removed);
        protected override IReadOnlyList<IPackage> LoadPackagesFromManager(IPackageManager manager) => [];
        protected override Task<bool> IsPackageValid(IPackage package) => Task.FromResult(true);
        protected override Task WhenAddingPackage(IPackage package) => Task.CompletedTask;
    }
}
