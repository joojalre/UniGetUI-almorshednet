using System.Collections.Specialized;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using ProductionExtract;
using Sorter = ProductionExtract.ObservablePackageCollection.Sorter;

namespace Acceptance;

internal static partial class Program
{
    private sealed record MoveIdentityRow(int Key);

    private static void IncrementalPackageContracts()
    {
        _scenario = "Packages-incremental-production-collection";
        var collection = new ObservablePackageCollection();
        var small = Low("small"); small.DownloadSizeBytes = 1024;
        var large = High("large"); large.DownloadSizeBytes = 4096;
        var unknown = Low("unknown");
        collection.SortBy(Sorter.DownloadSize);
        collection.ApplyChanges([], [unknown, large, small]);
        Check("incremental-size-ascending-unknown-last", collection.SequenceEqual(new[] { small, large, unknown }));
        collection.Clear();
        collection.SetSortDirection(false);
        collection.ApplyChanges([], [small, unknown, large]);
        Check("incremental-size-descending-unknown-last", collection.SequenceEqual(new[] { large, small, unknown }));
        var equal = High("equal"); equal.DownloadSizeBytes = 4096;
        collection.ApplyChanges([Low("absent")], [equal]);
        Check("incremental-equal-insert-stable", collection.SequenceEqual(new[] { large, equal, small, unknown }));
        collection.ApplyChanges([large], []);
        Check("incremental-removes-exact-identity", collection.SequenceEqual(new[] { equal, small, unknown }));

        collection.Clear();
        collection.SetSortDirection(true);
        small.DownloadSizeBytes = 100; large.DownloadSizeBytes = 200;
        collection.ApplyChanges([], [small, large, unknown]);
        unknown.DownloadSizeBytes = 50;
        var arriving = Low("arriving"); arriving.DownloadSizeBytes = 75;
        collection.ApplyChanges([], [arriving]);
        Check("incremental-delayed-size-reconciled", collection.SequenceEqual(new[] { unknown, arriving, small, large }));

        collection.Clear();
        collection.SortBy(Sorter.Checked);
        collection.ApplyChanges([], [small, large]);
        large.IsChecked = true;
        arriving.IsChecked = true;
        collection.ApplyChanges([], [arriving]);
        Check("incremental-changed-checkbox-reconciled", collection.SequenceEqual(new[] { large, arriving, small }));

        collection.Clear();
        collection.SortBy(Sorter.Name);
        collection.SetSortDirection(true);
        for (int i = 0; i < 400; i++)
            collection.Add(new FixtureRow("incremental" + i, $"Name{i:D3}", "id" + i, "source", i, i + 1));
        var view = new PackagesView { DataContext = new GridModel() };
        var grid = view.FindControl<DataGrid>("RowsGrid") ?? throw new InvalidOperationException("missing grid");
        grid.ItemsSource = collection;
        var window = new Window { Width = 1300, Height = 450, Content = view };
        window.Show(); Flush(window);
        try
        {
            var anchor = collection[200]; var peer = collection[201];
            grid.ScrollIntoView(anchor, grid.Columns[0]);
            grid.SelectedItems.Add(anchor); grid.SelectedItems.Add(peer); Flush(window);
            double beforeY = TextFor(window, anchor, "Name").TranslatePoint(default, window)!.Value.Y;
            Check("incremental-starts-scrolled", Rows(window).All(r => !ReferenceEquals(r.DataContext, collection[0])));
            Check("incremental-starts-multiselected", grid.SelectedItems.Count == 2);
            var notifications = new List<NotifyCollectionChangedAction>();
            collection.CollectionChanged += (_, args) => notifications.Add(args.Action);
            var removed = collection[300];
            var added = new FixtureRow("incremental-new", "Name300a", "new", "source", 1, 2);
            collection.ApplyChanges([removed], [added]); Flush(window);
            Check("incremental-no-reset-notification", notifications.SequenceEqual(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add }));
            Check("incremental-selection-survives", grid.SelectedItems.Count == 2 && grid.SelectedItems.Contains(anchor) && grid.SelectedItems.Contains(peer));
            Check("incremental-scroll-anchor-survives", Math.Abs(TextFor(window, anchor, "Name").TranslatePoint(default, window)!.Value.Y - beforeY) < 1);
            Check("incremental-rendered-anchor-identity", TextFor(window, anchor, "Name").Text == "Name200");
            Check("incremental-membership-updated", collection.Count == 400 && !Items(grid).Contains(removed) && Items(grid).Contains(added));
            Check("incremental-order-independent-neighbors", ReferenceEquals(collection[300], added) && collection[299].Name == "Name299" && collection[301].Name == "Name301");
            collection.SortBy(Sorter.DownloadSize);
            foreach (var row in collection) row.DownloadSizeBytes = int.Parse(row.Name.AsSpan(4, 3)) + 1;
            var delayed = collection[320]; delayed.DownloadSizeBytes = 251;
            notifications.Clear();
            collection.ApplyChanges([], []); Flush(window);
            Check("incremental-mutable-key-moves-without-reset", notifications.Contains(NotifyCollectionChangedAction.Move) && !notifications.Contains(NotifyCollectionChangedAction.Reset));
            Check("incremental-mutable-key-independent-order", ReferenceEquals(collection[251], delayed) && collection[250].Name == "Name250");
            Check("incremental-view-order-matches-source", Items(grid).SequenceEqual(collection));
            Check("incremental-move-selection-survives", grid.SelectedItems.Count == 2 && grid.SelectedItems.Contains(anchor) && grid.SelectedItems.Contains(peer));
            Check("incremental-move-scroll-anchor-survives", Math.Abs(TextFor(window, anchor, "Name").TranslatePoint(default, window)!.Value.Y - beforeY) < 1);
            grid.ScrollIntoView(delayed, grid.Columns[0]); Flush(window);
            Check("incremental-moved-row-rendered", TextFor(window, delayed, "Name").Text == delayed.Name);
            collection.ApplyChanges([anchor], []); Flush(window);
            Check("incremental-removed-selection-cleared", !grid.SelectedItems.Contains(anchor) && grid.SelectedItems.Contains(peer));
            Check("incremental-virtualization-bounded", Rows(window).Length > 0 && Rows(window).Length < 50);
            grid.SelectedItems.Clear(); grid.SelectedItems.Add(delayed);
            grid.CurrentColumn = grid.Columns[1]; grid.CollectionView.MoveCurrentTo(delayed); Flush(window);
            int selectionEvents = 0;
            grid.SelectionChanged += (_, _) => selectionEvents++;
            int originalIndex = collection.IndexOf(delayed);
            foreach (int destination in new[] { originalIndex - 70, originalIndex })
            {
                collection.Move(collection.IndexOf(delayed), destination); Flush(window);
                Check("move-selected-view-order-" + destination, Items(grid).SequenceEqual(collection));
                Check("move-selected-identity-" + destination, grid.SelectedItems.Count == 1 && ReferenceEquals(grid.SelectedItem, delayed) && selectionEvents == 0);
                Check("move-current-identity-" + destination, ReferenceEquals(grid.CollectionView.CurrentItem, delayed) && grid.CollectionView.CurrentPosition == destination);
            }

        }
        finally
        {
            window.Close();
        }
        var firstEqual = new MoveIdentityRow(1); var secondEqual = new MoveIdentityRow(1); var other = new MoveIdentityRow(2);
        var equalRows = new AvaloniaList<MoveIdentityRow> { firstEqual, secondEqual, other };
        var equalView = new DataGridCollectionView(equalRows);
        equalView.MoveCurrentToPosition(1);
        equalRows.Move(2, 0);
        Check("move-current-distinct-equal-identity", ReferenceEquals(equalView.CurrentItem, secondEqual) && equalView.CurrentPosition == 2);
        var duplicateRows = new AvaloniaList<MoveIdentityRow> { firstEqual, firstEqual, other };
        var duplicateView = new DataGridCollectionView(duplicateRows);
        duplicateView.MoveCurrentToPosition(1);
        duplicateRows.Move(2, 0);
        Check("move-current-duplicate-occurrence", ReferenceEquals(duplicateView.CurrentItem, firstEqual) && duplicateView.CurrentPosition == 2);
        var transformed = new ObservablePackageCollection();
        small.DownloadSizeBytes = 100; large.DownloadSizeBytes = 200;
        transformed.Add(small); transformed.Add(unknown); transformed.Add(large);
        var filtered = new DataGridCollectionView(transformed);
        filtered.Filter = item => !ReferenceEquals(item, unknown);
        transformed.Move(2, 0);
        Check("move-filtered-view-refreshes-correct-order", filtered.Cast<FixtureRow>().SequenceEqual(new[] { large, small }));
        filtered.SortDescriptions.Add(DataGridSortDescription.FromComparer(ObservablePackageCollection.GetColumnComparer(Sorter.Name)));
        transformed.Move(0, 2);
        Check("move-sorted-view-retains-sort", filtered.Cast<FixtureRow>().SequenceEqual(new[] { small, large }));
        var rangeSource = new ObservablePackageCollection();
        rangeSource.Add(small); rangeSource.Add(unknown); rangeSource.Add(large);
        var rangeView = new DataGridCollectionView(rangeSource);
        var rangeNotifications = new List<NotifyCollectionChangedAction>();
        rangeView.CollectionChanged += (_, args) => rangeNotifications.Add(args.Action);
        rangeSource.MoveRange(1, 2, 0);
        Check("move-range-view-refreshes-correct-order", rangeView.Cast<FixtureRow>().SequenceEqual(new[] { unknown, large, small }));
        Check("move-range-explicit-reset-fallback", rangeNotifications.SequenceEqual(new[] { NotifyCollectionChangedAction.Reset }));

    }
}
