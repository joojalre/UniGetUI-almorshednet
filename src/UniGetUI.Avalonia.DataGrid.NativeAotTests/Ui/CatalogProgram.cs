using System.Collections;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Acceptance;

internal static partial class Program
{
    private static CatalogTileViewModel CatalogRow(string name, string id, string manager = "Manager", string source = "Source")
    {
        var row = new CatalogTileViewModel(new CatalogEntry(name, id, manager, source));
        row.UpdateState(false, null, true, false);
        return row;
    }

    private static void RunCatalogGrid()
    {
        _scenario = "Catalog";
        var model = new SoftwareCatalogViewModel();
        var view = new CatalogView(model);
        var grid = view.FindControl<DataGrid>("PackageList") ?? throw new InvalidOperationException("missing Catalog grid");
        var window = new Window { Width = 1300, Height = 450, Content = view };
        window.Show(); Flush(window);
        // A failed binding can swallow the setter exception. Retry the identical source directly
        // only on that failure path so the regression receipt retains its underlying diagnostic.
        if (grid.CollectionView is null) grid.ItemsSource = model.Packages;
        Check("bound-collection-view", grid.CollectionView is not null);
        Check("initial-empty-items", CatalogItems(grid).Count == 0);
        Check("initial-empty-rows", CatalogRows(window).Length == 0);
        Check("initial-empty-selection", grid.SelectedItem is null && grid.SelectedItems.Count == 0);
        var high = CatalogRow("zeta", "aid", "A-manager", "Z-source");
        var low = CatalogRow("alpha", "zid", "Z-manager", "A-source");
        var equal = CatalogRow("alpha", "zid", "Z-manager", "A-source");
        var middle = CatalogRow("middle", "mid", "M-manager", "M-source");
        model.Packages.Add(high); Flush(window);
        Check("first-add-identity", ReferenceEquals(CatalogItems(grid).Single(), high) && ReferenceEquals(CatalogRows(window).Single().DataContext, high));
        Check("first-add-compiled-name", CatalogCell<TextBlock>(window, high, "catalog-name").Text == high.Entry.Name);
        Check("first-add-compiled-id", CatalogCell<TextBlock>(window, high, "catalog-id").Text == high.Entry.Id);
        Check("first-add-compiled-source", CatalogCell<TextBlock>(window, high, "catalog-manager").Text == high.Entry.ManagerName && CatalogCell<TextBlock>(window, high, "catalog-source").Text == high.Entry.Source);
        Check("first-add-catalog", CatalogCell<TextBlock>(window, high, "catalog-catalog").Text == high.CatalogName);
        Check("initial-fallback-icon", CatalogCell<TextBlock>(window, high, "catalog-fallback-icon").IsVisible);
        using var icon = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        high.IconBitmap = icon; Flush(window);
        Check("custom-icon-binding", ReferenceEquals(CatalogCell<Image>(window, high, "catalog-icon").Source, icon));
        Check("custom-icon-hides-fallback", !CatalogCell<TextBlock>(window, high, "catalog-fallback-icon", visibleOnly: false).IsVisible);
        high.IconBitmap = null; Flush(window);
        Check("cleared-icon-restores-fallback", CatalogCell<TextBlock>(window, high, "catalog-fallback-icon").IsVisible && !CatalogCell<Image>(window, high, "catalog-icon", visibleOnly: false).IsVisible);
        model.Packages.Add(low); model.Packages.Add(equal); model.Packages.Add(middle); Flush(window);
        Check("five-readonly-template-columns", !grid.AutoGenerateColumns && grid.IsReadOnly && grid.Columns.Count == 5 && grid.Columns.All(c => c is DataGridTemplateColumn && c.IsReadOnly));
        Check("three-sort-tags", grid.Columns.Take(3).Select(c => c.Tag).SequenceEqual(new[] { "Name", "Id", "Source" }));
        Check("catalog-status-unsortable", grid.Columns.Skip(3).All(c => c.Tag is null && !c.CanUserSort));
        Check("no-checkbox", !window.GetVisualDescendants().OfType<CheckBox>().Any());
        Check("realized-four", CatalogRows(window).Length == 4);
        var lowButton = CatalogCell<Button>(window, low, "catalog-install");
        Check("parent-install-command-binding", ReferenceEquals(lowButton.Command, model.InstallCommand) && ReferenceEquals(lowButton.CommandParameter, low));
        Click(window, lowButton); Flush(window);
        Check("pointer-parent-command-row", model.RecordedInstalls.SequenceEqual(new[] { low }));
        lowButton.Focus(); Key(window, PhysicalKey.Space);
        Check("keyboard-parent-command-row", model.RecordedInstalls.SequenceEqual(new[] { low, low }));
        model.RecordedInstalls.Clear();
        var ordered = CatalogItems(grid);
        // Tab follows template child traversal; sorting may give collection order a different layout.
        var tabButtons = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("catalog-install")).ToArray();
        var firstButton = tabButtons[0];
        var nextButton = tabButtons[1];
        firstButton.Focus(); Flush(window);
        var selected = grid.SelectedItem;
        Check("tab-start-focus", ReferenceEquals(window.FocusManager?.GetFocusedElement(), firstButton));
        Key(window, PhysicalKey.Tab);
        Check("tab-next-row-command", ReferenceEquals(window.FocusManager?.GetFocusedElement(), nextButton) && ReferenceEquals(grid.SelectedItem, selected));
        Key(window, PhysicalKey.Tab, RawInputModifiers.Shift);
        Check("tab-previous-row-command", ReferenceEquals(window.FocusManager?.GetFocusedElement(), firstButton) && ReferenceEquals(grid.SelectedItem, selected));
        Check("tab-no-install", model.RecordedInstalls.Count == 0);
        foreach (var state in new[] { "unknown", "unavailable", "pending", "busy", "installed", "ready" })
        {
            low.UpdateState(state == "installed", state == "unavailable" ? "Unavailable fixture" : null, state != "unknown", state == "pending", state == "busy");
            Flush(window);
            Check($"state-{state}-visibility", lowButton.IsVisible == low.ShowInstall && CatalogCell<TextBlock>(window, low, "catalog-installed", visibleOnly: false).IsVisible == low.IsInstalled);
            Check($"state-{state}-enabled", lowButton.IsEnabled == low.CanInstall);
            Check($"state-{state}-tooltip", Equals(ToolTip.GetTip(lowButton), low.StatusText));
            Check($"state-{state}-opacity", CatalogCell<TextBlock>(window, low, "catalog-name").Opacity == low.TitleOpacity);
            if (state != "installed" && state != "ready")
            {
                Click(window, lowButton); Flush(window);
                Check($"state-{state}-command-inert", model.RecordedInstalls.Count == 0);
            }
        }
        Check("state-opacity-restored", CatalogCell<TextBlock>(window, low, "catalog-name").Opacity == 1);
        foreach (var col in grid.Columns.Take(3))
        {
            var field = Enum.Parse<CatalogSortField>((string)col.Tag!);
            // A new field begins ascending; clicking the active field toggles direction.
            for (int click = 0; click < 2; click++)
            {
                var wasActive = model.SortField == field;
                var oldDirection = model.SortAscending;
                var beforeEvents = view.HandledSortEvents;
                var header = window.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => Equals(h.Content, col.Header));
                Click(window, header); Flush(window);
                Check($"sort-{field}-{click}-handled", view.HandledSortEvents == beforeEvents + 1);
                Check($"sort-{field}-{click}-toggle", model.SortField == field && model.SortAscending == (wasActive ? !oldDirection : true));
                var items = CatalogItems(grid);
                var direction = model.SortAscending ? 1 : -1;
                Check($"sort-{field}-{click}-ordered", items.Zip(items.Skip(1)).All(p => SoftwareCatalogViewModel.CompareTiles(field, p.First, p.Second) * direction <= 0));
                Check($"sort-{field}-{click}-identities", items.ToHashSet().SetEquals(new[] { high, low, equal, middle }));
                Check($"sort-{field}-{click}-arrow-description", grid.CollectionView.SortDescriptions.Count == 1 && grid.CollectionView.SortDescriptions[0].Direction == (model.SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending));
            }
        }
        ordered = CatalogItems(grid);
        Click(window, CatalogCell<TextBlock>(window, ordered[0], "catalog-name")); Flush(window);
        Check("pointer-selection", ReferenceEquals(grid.SelectedItem, ordered[0]));
        Check("template-edit-rejected", !grid.BeginEdit());
        grid.Focus(); Key(window, PhysicalKey.ArrowDown);
        Check("keyboard-down", ReferenceEquals(grid.SelectedItem, ordered[1]));
        Key(window, PhysicalKey.ArrowUp);
        Check("keyboard-up", ReferenceEquals(grid.SelectedItem, ordered[0]));
        Key(window, PhysicalKey.End);
        Check("keyboard-end-column", ReferenceEquals(grid.CurrentColumn, grid.Columns.Last()) && ReferenceEquals(grid.SelectedItem, ordered[0]));
        Key(window, PhysicalKey.Home);
        Check("keyboard-home-column", ReferenceEquals(grid.CurrentColumn, grid.Columns.First()) && ReferenceEquals(grid.SelectedItem, ordered[0]));
        Key(window, PhysicalKey.PageDown);
        Check("keyboard-page-down", ReferenceEquals(grid.SelectedItem, ordered[^1]));
        Key(window, PhysicalKey.PageUp);
        Check("keyboard-page-up", ReferenceEquals(grid.SelectedItem, ordered[0]));
        var modifiers = (RawInputModifiers)window.GetPlatformSettings()!.HotkeyConfiguration.CommandModifiers;
        Key(window, PhysicalKey.End, modifiers);
        Check("keyboard-end", ReferenceEquals(grid.SelectedItem, ordered[^1]));
        Key(window, PhysicalKey.Home, modifiers);
        Check("keyboard-home", ReferenceEquals(grid.SelectedItem, ordered[0]));
        var originalCollection = model.Packages;
        model.Refresh([]); Flush(window);
        Check("clear-empty-items-rows", CatalogItems(grid).Count == 0 && CatalogRows(window).Length == 0);
        Check("clear-selection", grid.SelectedItem is null && grid.SelectedItems.Count == 0);
        model.Refresh(new[] { low, high }); Flush(window);
        Check("refresh-same-collection", ReferenceEquals(originalCollection, model.Packages));
        Check("refresh-repopulates-identities", CatalogItems(grid).ToHashSet().SetEquals(new[] { low, high }));
        Check("refresh-retains-sort", grid.CollectionView.SortDescriptions.Count == 1 && model.SortField == CatalogSortField.Source && !model.SortAscending);
        var many = Enumerable.Range(0, 400).Select(i => CatalogRow($"Name{i:D3}", $"id{i:D3}", "Manager", $"source{i:D3}")).ToArray();
        model.SortField = CatalogSortField.Name; model.SortAscending = true; model.Refresh(many); Flush(window);
        Check("virtualization-bounded-initial", CatalogRows(window).Length > 0 && CatalogRows(window).Length < 50);
        grid.ScrollIntoView(many[350], grid.Columns[0]); Flush(window);
        Check("virtualization-far-identity", CatalogRows(window).Any(r => ReferenceEquals(r.DataContext, many[350])) && CatalogRows(window).Length < 50);
        Check("virtualization-far-binding", CatalogCell<TextBlock>(window, many[350], "catalog-name").Text == many[350].Entry.Name);
        grid.SelectedItem = many[350]; Flush(window);
        Check("virtualization-far-selection", ReferenceEquals(grid.SelectedItem, many[350]));
        Click(window, CatalogCell<Button>(window, many[350], "catalog-install")); Flush(window);
        Check("virtualization-command-identity", model.RecordedInstalls.SequenceEqual(new[] { many[350] }));
        many[350].UpdateState(false, null, true, true); Flush(window);
        var recycledButton = CatalogCell<Button>(window, many[350], "catalog-install");
        Click(window, recycledButton); Flush(window);
        Check("virtualization-pending-command-inert", !recycledButton.IsEnabled && model.RecordedInstalls.SequenceEqual(new[] { many[350] }));
        grid.ScrollIntoView(many[0], grid.Columns[0]); Flush(window);
        Check("virtualization-return-binding", CatalogCell<TextBlock>(window, many[0], "catalog-name").Text == many[0].Entry.Name);
        Check("virtualization-preserves-all", CatalogItems(grid).SequenceEqual(many));
        window.Close(); Dispatcher.UIThread.RunJobs();
    }

    private static List<CatalogTileViewModel> CatalogItems(DataGrid grid) => ((IEnumerable)grid.CollectionView).Cast<CatalogTileViewModel>().ToList();
    private static DataGridRow[] CatalogRows(Window window) => window.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsEffectivelyVisible && r.DataContext is CatalogTileViewModel).ToArray();
    private static T CatalogCell<T>(Window window, CatalogTileViewModel row, string name, bool visibleOnly = true) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => (!visibleOnly || c.IsEffectivelyVisible) && c.Classes.Contains(name) && ReferenceEquals(c.DataContext, row));
}
