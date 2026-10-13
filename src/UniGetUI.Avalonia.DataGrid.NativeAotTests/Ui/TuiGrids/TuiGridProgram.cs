using System.Collections;
using System.ComponentModel;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using TuiGridFixture;
using UniGetUI.Tui.Infrastructure;

namespace Acceptance;

internal static partial class Program
{
    private static void RunTuiGrids()
    {
        foreach (PackagePageKind kind in Enum.GetValues<PackagePageKind>())
            foreach (bool optional in new[] { false, true })
            {
                Settings.OptionalColumns = optional;
                var fixture = new ProductionPackageGrid(kind);
                _scenario = $"TUI-Packages-{kind}-optional-{optional}";
                var high = new PackageRow("[ ]", "zeta", "z.id", "9", "10", "z-source", "z-host", "9 KB", "ready");
                var low = new PackageRow("[x]", "alpha", "a.id", "1", "2", "a-source", "a-host", "1 KB", "idle");
                int count = 6 + (kind == PackagePageKind.Updates ? 1 : 0) + (optional && kind != PackagePageKind.Installed ? 2 : 0);
                Check("exact-package-column-count", fixture.Grid.Columns.Count == count);
                RunTuiGrid(fixture.Grid, high, low, (row, index) => TuiPackageValues(row, kind, optional)[index], () =>
                {
                    var window = (Window)TopLevel.GetTopLevel(fixture.Grid)!;
                    foreach (var column in fixture.Grid.Columns.Where(column => column.Tag is int))
                    {
                        int field = (int)column.Tag!;
                        for (int click = 0; click < 2; click++)
                        {
                            int calls = fixture.RecordedSorts.Count;
                            bool ascending = field == fixture.SortField ? !fixture.SortAscending : true;
                            TuiClickHeader(window, fixture.Grid, column);
                            Check("package-header-recorded-field", fixture.RecordedSorts.Count == calls + 1 && fixture.RecordedSorts[^1].Field == field);
                            Check("package-header-recorded-direction", fixture.RecordedSorts[^1].Ascending == ascending);
                            Check("package-header-handled-no-grid-sort", fixture.Grid.CollectionView.SortDescriptions.Count == 0);
                        }
                    }
                    Check("package-sort-has-no-reflection-comparer", fixture.Grid.Columns.All(column => column.CustomSortComparer is null));
                });
            }
        _scenario = "TUI-History";
        var history = new ProductionHistoryGrid();
        // Each field has a different permutation of the same three identities.
        // These expected orders are independent of the production comparers.
        var historyA = new HistoryRow("2026-10-11", "Install", "middle", "z-manager", "Running", "3 → 4");
        var historyB = new HistoryRow("2026-10-12", "Update", "alpha", "a-manager", "Succeeded", "2 → 3");
        var historyC = new HistoryRow("2026-10-13", "Uninstall", "zeta", "m-manager", "Failed", "1 → 2");
        RunTuiGrid(history.Grid,
            historyA, historyB,
            (row, index) => new[] { row.When, row.Kind, row.Package, row.Manager, row.Status, row.Versions }[index],
            () => TuiComparerHeaders(history.Grid, new[]
            {
                new[] { historyA, historyB, historyC }, // Date
                new[] { historyA, historyC, historyB }, // Operation
                new[] { historyB, historyA, historyC }, // Package
                new[] { historyB, historyC, historyA }, // Manager
                new[] { historyC, historyA, historyB }, // Status
                new[] { historyC, historyB, historyA }, // Version
            }), historyC);
        _scenario = "TUI-Operations";
        var operations = new ProductionOperationsGrid();
        var queueA = new QueueRow("a", "Update zeta", "Running");
        var queueB = new QueueRow("m", "Install alpha", "Succeeded");
        var queueC = new QueueRow("z", "Remove middle", "Queued");
        RunTuiGrid(operations.Grid,
            queueA, queueB,
            (row, index) => new[] { row.State, row.Title, row.Status }[index],
            () => TuiComparerHeaders(operations.Grid, new[]
            {
                new[] { queueA, queueB, queueC }, // State
                new[] { queueB, queueC, queueA }, // Operation
                new[] { queueC, queueA, queueB }, // Status
            }), queueC);
    }

    private static string[] TuiPackageValues(PackageRow row, PackagePageKind kind, bool optional)
    {
        var values = new List<string> { row.Check, row.Name, row.Id, row.Version };
        if (kind == PackagePageKind.Updates) values.Add(row.NewVersion);
        values.Add(row.Source);
        if (optional && kind != PackagePageKind.Installed) { values.Add(row.InstallerHost); values.Add(row.DownloadSize); }
        values.Add(row.State);
        return [.. values];
    }

    private static void RunTuiGrid<T>(DataGrid grid, T high, T low, Func<T, int, string> display, Action sortHeaders, T? third = null) where T : class
    {
        var empty = new DataGridList<T>();
        var metadata = ((IDataGridItemMetadataProvider)empty).ItemMetadata;
        Check("real-helper-empty-type", metadata.ItemType == typeof(T) && metadata.NewItemFactory is null);
        Check("real-helper-static-metadata", ReferenceEquals(metadata, ((IDataGridItemMetadataProvider)new DataGridList<T>()).ItemMetadata));
        grid.ItemsSource = empty;
        var window = new Window { Width = 1500, Height = 400, Content = grid };
        window.Show(); Flush(window);
        Check("empty-valid-view", grid.CollectionView is not null && TuiItems<T>(grid).Count == 0);
        Check("empty-no-selection", grid.SelectedItem is null && grid.SelectedItems.Count == 0);
        Check("all-columns-explicit-readonly", !grid.AutoGenerateColumns && grid.IsReadOnly && grid.Columns.All(column => column is DataGridTextColumn && column.IsReadOnly));
        var rows = new DataGridList<T>(third is null ? new[] { high, low } : new[] { high, low, third });
        // Production Refresh/RebuildQueue replaces a list, it does not mutate an observable list.
        grid.ItemsSource = rows; Flush(window);
        Check("replacement-collection-identity", ReferenceEquals(grid.ItemsSource, rows));
        Check("empty-to-populated-identities", TuiItems<T>(grid).SequenceEqual(rows));
        TuiRenderedCells(window, grid, rows, display);
        grid.SelectedItem = low; grid.CurrentColumn = grid.Columns[0]; grid.Focus(); Flush(window);
        Check("explicit-selection-identity", ReferenceEquals(grid.SelectedItem, low));
        Key(window, PhysicalKey.ArrowUp);
        Check("keyboard-up-identity", ReferenceEquals(grid.SelectedItem, high));
        Key(window, PhysicalKey.ArrowDown);
        Check("keyboard-down-identity", ReferenceEquals(grid.SelectedItem, low));
        Check("readonly-begin-edit-rejected", !grid.BeginEdit());
        Key(window, PhysicalKey.F2);
        Check("readonly-no-text-editor", !window.GetVisualDescendants().OfType<TextBox>().Any(text => text.IsEffectivelyVisible));
        sortHeaders();
        Check("sorting-preserves-row-identities", TuiItems<T>(grid).ToHashSet().SetEquals(rows));
        foreach (var column in grid.Columns) column.ClearSort();
        var replacement = new DataGridList<T>(new[] { low });
        grid.ItemsSource = replacement; Flush(window);
        // Page-specific identity matching/store queries are deliberately outside this display fixture.
        grid.SelectedItem = low; Flush(window);
        Check("one-row-replacement-identity", ReferenceEquals(grid.ItemsSource, replacement) && TuiItems<T>(grid).SequenceEqual(replacement));
        Check("replacement-selection-identity", ReferenceEquals(grid.SelectedItem, low));
        TuiRenderedCells(window, grid, replacement, display);
        grid.ItemsSource = new DataGridList<T>(); Flush(window);
        Check("replacement-empty-valid-view", grid.CollectionView is not null && TuiItems<T>(grid).Count == 0);
        Check("replacement-empty-selection-cleared", grid.SelectedItem is null && grid.SelectedItems.Count == 0);
        window.Close(); Flush(window);
    }

    private static void TuiRenderedCells<T>(Window window, DataGrid grid, IReadOnlyList<T> rows, Func<T, int, string> display) where T : class
    {
        foreach (T row in rows)
            for (int column = 0; column < grid.Columns.Count; column++)
            {
                grid.ScrollIntoView(row, grid.Columns[column]); Flush(window);
                var realized = window.GetVisualDescendants().OfType<DataGridRow>().Single(item => item.IsEffectivelyVisible && ReferenceEquals(item.DataContext, row));
                var content = grid.Columns[column].GetCellContent(realized);
                Check($"rendered-cell-{column}", content is TextBlock text && text.Text == display(row, column));
            }
    }

    private static void TuiComparerHeaders<T>(DataGrid grid, IReadOnlyList<T[]> expectedAscending) where T : class
    {
        var window = (Window)TopLevel.GetTopLevel(grid)!;
        Check("independent-order-per-header", grid.Columns.Count == expectedAscending.Count);
        for (int index = 0; index < grid.Columns.Count; index++)
        {
            var column = grid.Columns[index];
            Check("production-column-typed-comparer", column.CustomSortComparer is not null && column.CanUserSort);
            column.ClearSort(); Flush(window);
            TuiClickHeader(window, grid, column);
            Check($"header-{index}-ascending-direction", grid.CollectionView.SortDescriptions.Count == 1 && grid.CollectionView.SortDescriptions[0].Direction == ListSortDirection.Ascending);
            Check($"header-{index}-ascending-independent-identities", TuiIdentityOrder(grid, expectedAscending[index]));
            TuiClickHeader(window, grid, column);
            Check($"header-{index}-descending-direction", grid.CollectionView.SortDescriptions.Count == 1 && grid.CollectionView.SortDescriptions[0].Direction == ListSortDirection.Descending);
            Check($"header-{index}-descending-independent-identities", TuiIdentityOrder(grid, expectedAscending[index].Reverse().ToArray()));
            column.ClearSort(); Flush(window);
        }
    }

    private static void TuiClickHeader(Window window, DataGrid grid, DataGridColumn column)
    {
        grid.ScrollIntoView(grid.SelectedItem, column); Flush(window);
        var header = window.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(item => item.IsEffectivelyVisible && ReferenceEquals(item.Content, column.Header));
        Click(window, header); Flush(window);
    }

    private static List<T> TuiItems<T>(DataGrid grid) => ((IEnumerable)grid.CollectionView).Cast<T>().ToList();
    private static bool TuiIdentityOrder<T>(DataGrid grid, IReadOnlyList<T> expected) where T : class
    {
        object[] items = ((IEnumerable)grid.CollectionView).Cast<object>().ToArray();
        return items.Length == expected.Count && items.Zip(expected).All(pair => ReferenceEquals(pair.First, pair.Second));
    }
}
