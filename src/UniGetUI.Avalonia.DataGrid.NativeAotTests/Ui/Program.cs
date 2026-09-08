using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProductionExtract;
using Sorter = ProductionExtract.ObservablePackageCollection.Sorter;
using CoreVersion = UniGetUI.Core.Tools.CoreTools.Version;
#if PATCHED_DATAGRID
using MetadataRows = UniGetUI.Avalonia.Models.DataGridObservableCollection<Acceptance.FixtureRow>;
#endif

namespace Acceptance;

internal static class Program
{
    private static int _checks;
    private static string _scenario = "startup";
    [STAThread]
    public static int Run(string[] args)
    {
        Console.WriteLine($"MODE\tAOT={!RuntimeFeature.IsDynamicCodeSupported}\tPatched={Patched}");
        try
        {
            Check("runtime-mode", RuntimeFeature.IsDynamicCodeSupported != args.Contains("--expect-aot"));
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
#if PATCHED_DATAGRID
            var metadata=((Avalonia.Collections.IDataGridItemMetadataProvider)new MetadataRows()).ItemMetadata;
            Check("production-helper-row-type",metadata.ItemType==typeof(FixtureRow));
            Check("production-helper-no-factory",metadata.NewItemFactory is null);
            Check("production-helper-static-metadata",ReferenceEquals(metadata,((Avalonia.Collections.IDataGridItemMetadataProvider)new MetadataRows()).ItemMetadata));
#endif
            ComparerContracts();
            RunGrid("Packages");
            RunGrid("History");
            RunGrid("Desktop");
            RunGrid("StartMenu");
            Console.WriteLine($"RESULT\tPASS\tchecks={_checks}\tbackend=headless\tproduction-logic=exact-extracts");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RESULT\tFAIL\tchecks={_checks}\tscenario={_scenario}\t{ex.GetType().FullName}\t{ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static bool Patched =>
#if PATCHED_DATAGRID
        true;
#else
        false;
#endif

    private static FixtureRow Low(string id = "low") => new(id, "alpha", "aid", "a-source", 2, 3, "Add", "Failed", 1);
    private static FixtureRow High(string id = "high") => new(id, "zeta", "zid", "z-source", 10, 11, "Remove", "Succeeded", 2);
    private static FixtureRow Nulls() => new("nulls", null!, null!, null!, -1, -1, null!, null!, 0) { VersionChange = null!, SourceLabel = null! };

    private static void ComparerContracts()
    {
        _scenario = "production-comparer-contracts";
        var low = Low(); var high = High(); var equal = Low("equal"); var nulls = Nulls();
        foreach (var sorter in new[] { Sorter.Checked, Sorter.Name, Sorter.Id, Sorter.Version, Sorter.NewVersion, Sorter.Source })
        {
            var compare = ObservablePackageCollection.GetColumnComparer(sorter);
            low.IsChecked = true; equal.IsChecked = true; high.IsChecked = false;
            Check($"package-{sorter}-ordered", compare.Compare(low, high) < 0);
            Check($"package-{sorter}-equal", compare.Compare(low, equal) == 0);
            if (sorter != Sorter.Checked) Check($"package-{sorter}-null-value", compare.Compare(nulls, low) < 0);
            Check($"package-{sorter}-null-row", compare.Compare(null, low) < 0 && compare.Compare(low, null) > 0 && compare.Compare(null, null) == 0);
        }
        foreach (var key in new[] { "kind", "package", "version", "source", "status", "date" })
        {
            var compare = new OperationHistoryRowComparer(key);
            // History version is deliberately lexical, unlike normalized package versions.
            int expected = key == "version" ? 1 : -1;
            Check($"history-{key}-ordered", Math.Sign(compare.Compare(low, high)) == expected);
            Check($"history-{key}-equal", compare.Compare(low, equal) == 0);
            Check($"history-{key}-null-value", compare.Compare(nulls, low) < 0);
            Check($"history-{key}-null-row", compare.Compare(null, low) == 0);
        }
        Check("history-unknown-key", new OperationHistoryRowComparer("unknown").Compare(low, high) == 0);
        Check("version-extra-segment", CoreVersion.FromSegments(new[] { 1, 2, 3, 4, 1 }) < CoreVersion.FromSegments(new[] { 1, 2, 3, 4, 2 }));
        Check("version-trailing-zero", CoreVersion.FromSegments(new[] { 1, 2, 3, 4, 0 }) == new CoreVersion(1, 2, 3, 4));
    }

    private static void RunGrid(string kind)
    {
        _scenario = kind;
        var model = new GridModel(); var high = High(); var low = Low(); var equal = Low("equal");
        high.ExistsOnDisk = false; high.CanStopTracking = false;
        model.Rows.Add(high); model.Rows.Add(low); model.Rows.Add(equal); model.Rows.Add(Nulls());
        UserControl view = kind switch { "Packages" => new PackagesView(), "History" => new HistoryView(model), "Desktop" => new DesktopView(model), _ => new StartMenuView(model) };
        if (kind == "Packages") view.DataContext = model;
        var grid = view.FindControl<DataGrid>("RowsGrid") ?? throw new InvalidOperationException("missing grid");
        foreach (var col in grid.Columns)
        {
            if (col.Tag is not string key) continue;
            col.CanUserSort = true;
            col.CustomSortComparer = kind == "Packages" ? PackageComparer(key) : new OperationHistoryRowComparer(key);
        }
        var window = new Window { Width = 1300, Height = 450, Content = view };
        window.Show(); Flush(window);
        Check("realized-four", Rows(window).Length == 4);
        Check("explicit-template-columns", !grid.AutoGenerateColumns && grid.Columns.All(c => c is DataGridTemplateColumn));
        Check("template-no-edit-mode", grid.Columns.All(c => c.IsReadOnly));
        Check("untagged-columns-not-sortable", grid.Columns.Where(c => c.Tag is null).All(c => !c.CanUserSort));
        Check("null-binding-rendered", Rows(window).Any(r => ReferenceEquals(r.DataContext, model.Rows[3])));
        var disabled = Cell<Button>(window, high, "row-open"); Click(window, disabled); Flush(window);
        Check("disabled-command-inert", !disabled.IsEnabled && high.Actions.Count == 0);
        foreach (var col in grid.Columns.Where(c => c.Tag is string))
        {
            col.Sort(ListSortDirection.Ascending); Flush(window);
            Check($"sort-{col.Tag}-ascending", IsOrdered(grid, col.CustomSortComparer!, 1));
            col.Sort(ListSortDirection.Descending); Flush(window);
            Check($"sort-{col.Tag}-descending", IsOrdered(grid, col.CustomSortComparer!, -1));
            Check($"sort-{col.Tag}-retains-identities", Items(grid).ToHashSet().SetEquals(model.Rows));
            col.ClearSort(); Flush(window);
        }
        if (kind is "Packages" or "History")
        {
            var sortable = grid.Columns.First(c => c.Tag is string);
            var header = window.GetVisualDescendants().OfType<DataGridColumnHeader>().First(c => Equals(c.Content, sortable.Header));
            Click(window, header); Flush(window);
            Check("mouse-header-sort", grid.CollectionView.SortDescriptions.Count > 0);
            sortable.ClearSort(); Flush(window);
        }
        else Check("sorting-disabled", !grid.CanUserSortColumns);
        var first = model.Rows[0]; grid.ScrollIntoView(first, grid.Columns[0]); Flush(window);
        Click(window, TextFor(window, first, "Name")); Flush(window);
        Check("pointer-selection", ReferenceEquals(grid.SelectedItem, first));
        Check("template-edit-transaction-rejected", !grid.BeginEdit());
        grid.Focus(); window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); Flush(window);
        Check("keyboard-down-selection", grid.SelectedItem is FixtureRow next && !ReferenceEquals(next, first));
        if (kind == "Packages")
        {
            grid.SelectedItems.Clear(); grid.SelectedItems.Add(high); grid.SelectedItems.Add(low); Flush(window);
            Check("extended-selection", grid.SelectedItems.Count == 2);
        }
        var target = low; grid.ScrollIntoView(target, grid.Columns[0]); Flush(window);
        if (kind != "History")
        {
            var box = Cell<CheckBox>(window, target, "row-check");
            target.IsChecked = false; Flush(window); Click(window, box); Flush(window);
            Check("pointer-checkbox-model", target.IsChecked);
            box.Focus(); window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None); Flush(window);
            Check("keyboard-checkbox-model", !target.IsChecked);
            target.IsChecked = true; Flush(window); Check("model-checkbox", box.IsChecked == true);
        }
        var button = Cell<Button>(window, target, "row-open");
        Click(window, button); Flush(window); Check("pointer-record-command", target.Actions.SequenceEqual(new[] { "open:" + target.Identity }));
        button.Focus(); window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None); Flush(window);
        Check("keyboard-record-command", target.Actions.Count == 2);
        Check("commands-no-other-row", model.Rows.Where(r => !ReferenceEquals(r, target)).All(r => r.Actions.Count == 0));
        if (kind is "Desktop" or "StartMenu")
        {
            var remove = Cell<Button>(window, target, "row-remove"); Click(window, remove); Flush(window);
            Check("record-remove-only", target.Actions.Last() == "remove:" + target.Identity && model.Rows.Contains(target));
        }
        if (kind == "Packages")
        {
            target.Package.Source = new SourceValue("replaced-source"); Flush(window);
            Check("replace-nested-source", TextFor(window, target, "Source").Text == "replaced-source");
            target.Package.Source.AsString_DisplayName = "changed-source"; Flush(window);
            Check("nested-source-notification", TextFor(window, target, "Source").Text == "changed-source");
        }
        var column = grid.Columns[1]; column.IsVisible = false; Flush(window);
        Check("hide-column-visual", !window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Classes.Contains("value-Name")));
        column.IsVisible = true; column.Width = new DataGridLength(210); Flush(window); Check("resize-column", Math.Abs(column.ActualWidth - 210) < 1);
        double beforeX = TextFor(window, target, "Name").TranslatePoint(default, window)!.Value.X;
        int original = column.DisplayIndex; column.DisplayIndex = grid.Columns.Count - 1; Flush(window);
        double afterX = TextFor(window, target, "Name").TranslatePoint(default, window)!.Value.X;
        Check("reorder-column-visual", column.DisplayIndex == grid.Columns.Count - 1 && afterX > beforeX + 100);
        column.DisplayIndex = original; Flush(window);
        Check("column-changes-retain-identity", Items(grid).ToHashSet().SetEquals(model.Rows));
        var old = model.Rows; model.Rows = new MetadataRows { Low("replacement") }; Flush(window);
        Check("items-source-replacement", Items(grid).Single().Identity == "replacement");
        old.Add(High("detached")); Flush(window); Check("old-source-detached", Items(grid).Count == 1);
        model.Rows.Clear(); Flush(window); Check("empty-reset", Items(grid).Count == 0 && Rows(window).Length == 0);
        model.Rows.Add(Low("restored")); Flush(window); Check("repopulate", Rows(window).Single().DataContext == model.Rows[0]);
        Virtualization(window, grid, model);
        window.Close(); Dispatcher.UIThread.RunJobs();
    }

    private static void Virtualization(Window window, DataGrid grid, GridModel model)
    {
        var many = new MetadataRows(); for (int i = 0; i < 400; i++) many.Add(new FixtureRow("row" + i, "Name" + i, "id" + i, "source" + i, i, i + 1));
        model.Rows = many; Flush(window);
        var initial = Rows(window); Check("virtualization-bounded-initial", initial.Length > 0 && initial.Length < 50);
        var target = many[350]; grid.ScrollIntoView(target, grid.Columns[0]); Flush(window);
        var far = Rows(window); Check("scroll-far-row", far.Any(r => ReferenceEquals(r.DataContext, target)));
        Check("virtualization-bounded-far", far.Length > 0 && far.Length < 50);
        Check("virtualization-row-identity", far.All(r => r.DataContext is FixtureRow item && many.Contains(item)));
        grid.SelectedItem = target; Flush(window); Check("far-selection", ReferenceEquals(grid.SelectedItem, target));
        var text = TextFor(window, target, "Name"); Check("far-text-identity", text.Text == target.Name);
        if (_scenario != "History") { var box = Cell<CheckBox>(window, target, "row-check"); Click(window, box); Flush(window); Check("far-checkbox-identity", target.IsChecked && many.Where(r => !ReferenceEquals(r, target)).All(r => !r.IsChecked)); }
        grid.ScrollIntoView(many[0], grid.Columns[0]); Flush(window);
        Check("scroll-back-row", Rows(window).Any(r => ReferenceEquals(r.DataContext, many[0])));
        Check("scroll-back-text", TextFor(window, many[0], "Name").Text == "Name0");
        double rowZeroBefore = TextFor(window, many[0], "Name").TranslatePoint(default, window)!.Value.Y;
        var center = grid.TranslatePoint(new Point(grid.Bounds.Width / 2, grid.Bounds.Height / 2), window)!.Value;
        window.MouseWheel(center, new Vector(0, -6)); Flush(window);
        var rowZeroText = window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.IsEffectivelyVisible && t.Classes.Contains("value-Name") && ReferenceEquals(t.DataContext, many[0]));
        Check("wheel-moves-content", rowZeroText is null || rowZeroText.TranslatePoint(default, window)!.Value.Y < rowZeroBefore);
        Check("wheel-retains-valid-rows", Rows(window).All(r => r.DataContext is FixtureRow item && many.Contains(item)));
    }

    private static IComparer PackageComparer(string key) => ObservablePackageCollection.GetColumnComparer(key switch { "Name" => Sorter.Name, "Id" => Sorter.Id, "Version" => Sorter.Version, "NewVersion" => Sorter.NewVersion, "Source" => Sorter.Source, _ => throw new InvalidOperationException(key) });
    private static List<FixtureRow> Items(DataGrid grid) => ((IEnumerable)grid.CollectionView).Cast<FixtureRow>().ToList();
    private static bool IsOrdered(DataGrid grid, IComparer comparer, int direction) { var items = Items(grid); return items.Zip(items.Skip(1)).All(p => comparer.Compare(p.First, p.Second) * direction <= 0); }
    private static DataGridRow[] Rows(Window window) => window.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsEffectivelyVisible && r.DataContext is FixtureRow).ToArray();
    private static T Cell<T>(Window window, FixtureRow row, string cls) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.IsEffectivelyVisible && c.Classes.Contains(cls) && ReferenceEquals(c.DataContext, row));
    private static TextBlock TextFor(Window window, FixtureRow row, string key) => Cell<TextBlock>(window, row, "value-" + key);
    private static void Click(Window window, Control control) { var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? throw new InvalidOperationException("detached control"); window.MouseMove(p); window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left); }
    private static void Flush(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Check(string name, bool value) { if (!value) throw new InvalidOperationException(name); _checks++; Console.WriteLine($"CHECK\tPASS\t{_scenario}\t{name}"); }
}
