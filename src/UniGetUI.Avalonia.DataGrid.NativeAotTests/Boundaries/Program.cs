using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DataGridAotRepro;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}

public sealed partial class GridWindow : Window
{
    public GridWindow() => AvaloniaXamlLoader.Load(this);
}

public sealed class GridModel
{
    public ObservableCollection<RowModel> Rows { get; } = new TypedRows<RowModel>();
}

public sealed class TypedRows<T> : ObservableCollection<T>, IDataGridItemMetadataProvider
{
    public TypedRows(Func<object>? factory = null) => ItemMetadata = new(typeof(T), factory);
    public DataGridItemMetadata ItemMetadata { get; }
}

public sealed class TypedRowGroup : DataGridGroupDescription
{
    public override object GroupKeyFromItem(object item, int level, CultureInfo culture) => ((RowModel)item).Enabled;
}

public sealed class RowModel(string name, bool enabled) : INotifyPropertyChanged
{
    private bool _enabled = enabled;
    public string Name { get; } = name;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class RowComparer : IComparer
{
    public int Calls { get; private set; }
    public int Compare(object? x, object? y)
    {
        Calls++;
        return StringComparer.Ordinal.Compare(((RowModel?)x)?.Name, ((RowModel?)y)?.Name);
    }
}

public sealed class ComparableRow(int value) : IComparable
{
    public int Value { get; } = value;
    public int CompareTo(object? other) => other is null ? 1 : Value.CompareTo(((ComparableRow)other).Value);
}

internal static class Program
{
    private static int _checks;

    [STAThread]
    public static int Run(string[] args)
    {
        Console.WriteLine($"RUNTIME\tIsDynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported}\tIsDynamicCodeCompiled={RuntimeFeature.IsDynamicCodeCompiled}");
        try
        {
            Check("runtime-mode", RuntimeFeature.IsDynamicCodeSupported != args.Contains("--expect-aot"));
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                .SetupWithoutStarting();
            var model = new GridModel();
            var zulu = new RowModel("Zulu", false);
            var alpha = new RowModel("Alpha", true);
            var mike = new RowModel("Mike", false);
            model.Rows.Add(zulu);
            model.Rows.Add(alpha);
            model.Rows.Add(mike);
            var window = new GridWindow { DataContext = model };
            var grid = window.FindControl<DataGrid>("RowsGrid") ?? throw new InvalidOperationException("Grid missing");
            var comparer = new RowComparer();
            grid.Columns[0].CustomSortComparer = comparer;
            window.Show();
            Flush(window);
            Check("explicit-columns", grid.Columns.Count == 2 && !grid.AutoGenerateColumns);
            Check("populated-collection", ((IEnumerable)grid.CollectionView).Cast<RowModel>().Count() == 3);
            Check("compiled-text-bindings", Names(window).Order().SequenceEqual(new[] { "Alpha", "Mike", "Zulu" }));
            Check("realized-rows", window.GetVisualDescendants().OfType<DataGridRow>().Count(r => r.DataContext is RowModel) == 3);

            grid.Columns[0].Sort(ListSortDirection.Ascending);
            Flush(window);
            Check("typed-sort-ascending", ((IEnumerable)grid.CollectionView).Cast<RowModel>().Select(r => r.Name).SequenceEqual(new[] { "Alpha", "Mike", "Zulu" }));
            Check("typed-comparer-invoked", comparer.Calls > 0);
            grid.Columns[0].Sort(ListSortDirection.Descending);
            Flush(window);
            Check("typed-sort-descending", ((IEnumerable)grid.CollectionView).Cast<RowModel>().Select(r => r.Name).SequenceEqual(new[] { "Zulu", "Mike", "Alpha" }));

            grid.SelectedItem = alpha;
            Flush(window);
            Check("selection-alpha", ReferenceEquals(grid.SelectedItem, alpha) && grid.SelectedItems.Count == 1);
            grid.SelectedItem = mike;
            Flush(window);
            Check("selection-mike", ReferenceEquals(grid.SelectedItem, mike));
            var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Classes.Contains("row-enabled") && ReferenceEquals(c.DataContext, mike));
            Check("editable-initial-binding", checkbox.IsChecked == false);
            checkbox.IsChecked = true;
            Flush(window);
            Check("editable-control-to-model", mike.Enabled);
            mike.Enabled = false;
            Flush(window);
            Check("editable-model-to-control", checkbox.IsChecked == false);

            model.Rows.Clear();
            Flush(window);
            Check("empty-collection", !((IEnumerable)grid.CollectionView).Cast<RowModel>().Any());
            Check("empty-no-realized-data", !window.GetVisualDescendants().OfType<DataGridRow>().Any(r => r.IsEffectivelyVisible && r.DataContext is RowModel));
            Check("empty-selection-cleared", grid.SelectedItem is null);
            model.Rows.Add(new RowModel("Beta", true));
            Flush(window);
            Check("repopulate-collection", ((IEnumerable)grid.CollectionView).Cast<RowModel>().Count() == 1);
            Console.WriteLine($"OBSERVED\trepopulate-visible-names={string.Join(",", Names(window))}");
            Check("repopulate-compiled-binding", Names(window).SequenceEqual(new[] { "Beta" }));
            TestBoundaries(grid, model);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Console.WriteLine($"RESULT\tPASS\tchecks={_checks}\tbackend=headless-fake-drawing\tinput=programmatic-control-properties");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RESULT\tFAIL\tchecks={_checks}\t{ex.GetType().FullName}\t{ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static void TestBoundaries(DataGrid grid, GridModel model)
    {
        Check("datagrid-compiled-theme-applied", grid.Template is not null &&
            grid.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.DataGridRowsPresenter>().Any());
        Check("items-source-identity", ReferenceEquals(grid.ItemsSource, model.Rows));
        Check("view-source-identity", ReferenceEquals(grid.CollectionView.SourceCollection, model.Rows));
        var originalView = (DataGridCollectionView)grid.CollectionView;
        Check("no-factory-cannot-add", !originalView.CanAddNew);
        Throws("no-factory-add-rejected", () => originalView.AddNew());

        var integers = new TypedRows<int> { 3, 1, 2 };
        var integerView = new DataGridCollectionView(integers);
        integerView.SortDescriptions.Add(DataGridSortDescription.FromPath("", ListSortDirection.Ascending));
        Check("whole-item-sort-jit-aot-parity", integerView.Cast<int>().SequenceEqual(new[] { 1, 2, 3 }));
        var comparableRows = new TypedRows<ComparableRow> { new(2), null!, new(1) };
        var comparableView = new DataGridCollectionView(comparableRows);
        comparableView.SortDescriptions.Add(DataGridSortDescription.FromPath("", ListSortDirection.Ascending));
        Check("whole-item-sort-null-order", comparableView.Cast<ComparableRow?>().Select(row => row?.Value).SequenceEqual(new int?[] { null, 1, 2 }));

        int factoryCalls = 0;
        var factoryRows = new TypedRows<RowModel>(() => new RowModel("Added-" + ++factoryCalls, true));
        var factoryView = new DataGridCollectionView(factoryRows);
        Check("typed-factory-can-add", factoryView.CanAddNew);
        var added = (RowModel)factoryView.AddNew();
        Check("typed-factory-insert-identity", factoryCalls == 1 && ReferenceEquals(factoryRows.Single(), added) && factoryView.IsAddingNew);
        factoryView.CommitNew();
        Check("typed-factory-commit", !factoryView.IsAddingNew && factoryRows.Count == 1 && ReferenceEquals(factoryRows[0], added));
        var cancelled = factoryView.AddNew();
        Check("typed-factory-second-insert", factoryCalls == 2 && factoryRows.Count == 2);
        factoryView.CancelNew();
        Check("typed-factory-cancel", !factoryView.IsAddingNew && factoryRows.Count == 1 && ReferenceEquals(factoryRows[0], added) && !factoryRows.Contains((RowModel)cancelled));

        var groupRows = new TypedRows<RowModel> { new("One", true), new("Two", false), new("Three", true) };
        var groupView = new DataGridCollectionView(groupRows);
        groupView.GroupDescriptions.Add(new TypedRowGroup());
        Check("typed-grouping-positive", groupView.Groups.Count == 2 && groupView.Groups.Cast<DataGridCollectionViewGroup>().Sum(g => g.ItemCount) == 3);
        groupRows.Add(new RowModel("Four", false));
        Check("typed-grouping-notifications", groupView.Groups.Count == 2 && groupView.Groups.Cast<DataGridCollectionViewGroup>().All(g => g.ItemCount == 2));
        Throws("path-grouping-rejected-empty", () => new DataGridPathGroupDescription("Name"));
        Throws("path-sorting-rejected-empty", () => DataGridSortDescription.FromPath("Name"));

        var missing = new DataGrid { AutoGenerateColumns = false };
        Throws("missing-metadata-rejected", () => missing.ItemsSource = new ObservableCollection<RowModel>());
        var auto = new DataGrid { AutoGenerateColumns = true, ItemsSource = new TypedRows<RowModel>() };
        var autoWindow = new Window { Width = 400, Height = 200, Content = auto };
        Throws("autocolumns-rejected-empty", () => { autoWindow.Show(); Flush(autoWindow); });
        autoWindow.Close();

        // Public bound-column conversion boundary, with no reflection lookup of the internal converter.
        var conversionBinding = new Avalonia.Data.CompiledBinding(new Avalonia.Data.CompiledBindingPathBuilder().Build());
        var column = new DataGridTextColumn { Binding = conversionBinding };
        Throws("default-convert-rejected", () => conversionBinding.Converter!.Convert("12", typeof(int), null, CultureInfo.InvariantCulture));
        Throws("default-convertback-rejected", () => conversionBinding.Converter!.ConvertBack("12", typeof(int), null, CultureInfo.InvariantCulture));
    }

    private static void Throws(string name, Action action)
    {
        try { action(); }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"EXPECTED\t{name}\t{ex.Message}");
            Check(name, true);
            return;
        }
        throw new InvalidOperationException($"Expected explicit rejection: {name}");
    }

    private static string[] Names(Window window) => window.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => t.Classes.Contains("row-name") && t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToArray();

    // Deterministic queue/layout barriers, no wall-clock sleeps. Fake drawing tests the visual tree,
    // layout and bindings; it intentionally does not claim pixel rendering or native OS input.
    private static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException($"Assertion failed: {name}");
        _checks++;
        Console.WriteLine($"CHECK\tPASS\t{name}");
    }
}
