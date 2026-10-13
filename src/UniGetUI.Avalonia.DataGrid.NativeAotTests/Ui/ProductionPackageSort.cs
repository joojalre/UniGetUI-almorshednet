using System.Collections;
using System.ComponentModel;
using Avalonia.Collections;
using IPackage = Acceptance.PackageValue;
using PackageWrapper = Acceptance.FixtureRow;

namespace ProductionExtract;

public sealed class ObservablePackageCollection : AvaloniaList<PackageWrapper>
#if PATCHED_DATAGRID
    , IDataGridItemMetadataProvider
#endif
{
#if PATCHED_DATAGRID
    private static readonly DataGridItemMetadata _itemMetadata = new(typeof(PackageWrapper));

    DataGridItemMetadata IDataGridItemMetadataProvider.ItemMetadata => _itemMetadata;
#endif

    public enum Sorter
    {
        Checked,
        Name,
        Id,
        Version,
        NewVersion,
        Source,
        DownloadSize,
    }

    public Sorter CurrentSorter { get; private set; } = Sorter.Name;
    private bool _ascending = true;

    /// <summary>Fires when any wrapper's IsChecked changes, or when items are added/removed.</summary>
    public event EventHandler? SelectionStateChanged;

    public ObservablePackageCollection()
    {
        CollectionChanged += OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PackageWrapper w in e.OldItems) w.PropertyChanged -= OnWrapperPropertyChanged;
        if (e.NewItems is not null)
            foreach (PackageWrapper w in e.NewItems) w.PropertyChanged += OnWrapperPropertyChanged;
        SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWrapperPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PackageWrapper.IsChecked))
            SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns the tri-state value for a "select-all" checkbox: true=all, false=none, null=some.</summary>
    public bool? GetSelectionState()
    {
        if (Count == 0) return false;
        int checkedCount = 0;
        foreach (var w in this) if (w.IsChecked) checkedCount++;
        if (checkedCount == 0) return false;
        if (checkedCount == Count) return true;
        return null;
    }

    public List<IPackage> GetPackages() =>
        this.Select(w => w.Package).ToList();

    public List<IPackage> GetCheckedPackages() =>
        this.Where(w => w.IsChecked).Select(w => w.Package).ToList();

    public void SelectAll()
    {
        foreach (var w in this) w.IsChecked = true;
    }

    public void ClearSelection()
    {
        foreach (var w in this) w.IsChecked = false;
    }

    public void ApplyChanges(IEnumerable<PackageWrapper> removed, IEnumerable<PackageWrapper> added)
    {
        var comparer = GetOrderComparer();
        // Checkbox state and asynchronously resolved sizes can change after the last sort.
        // Restore the binary-insertion invariant with moves, preserving row identity and selection.
        for (int i = 1; i < Count; i++)
        {
            if (comparer.Compare(this[i - 1], this[i]) <= 0) continue;
            var ordered = this.OrderBy(w => w, comparer).ToArray();
            for (int destination = 0; destination < ordered.Length; destination++)
            {
                if (ReferenceEquals(this[destination], ordered[destination])) continue;
                Move(IndexOfReference(this, ordered[destination]), destination);
            }
            break;
        }
        ApplyChanges(this, removed, added, comparer);
    }

    internal static void ApplyChanges<T>(IList<T> list, IEnumerable<T> removed, IEnumerable<T> added, IComparer<T> comparer)
    {
        foreach (var item in removed)
        {
            int index = IndexOfReference(list, item);
            if (index >= 0) list.RemoveAt(index);
        }

        foreach (var item in added)
        {
            int low = 0, high = list.Count;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (comparer.Compare(list[mid], item) <= 0) low = mid + 1;
                else high = mid;
            }
            list.Insert(low, item);
        }
    }

    private static int IndexOfReference<T>(IList<T> list, T item)
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item)) return i;
        return -1;
    }

    public void SortBy(Sorter sorter) => CurrentSorter = sorter;

    public void SetSortDirection(bool ascending) => _ascending = ascending;

    /// <summary>Returns <paramref name="items"/> in the current sort order.</summary>
    public IEnumerable<PackageWrapper> ApplyToList(IEnumerable<PackageWrapper> items)
        => items.OrderBy(w => w, GetOrderComparer());

    private IComparer<PackageWrapper> GetOrderComparer()
    {
        Sorter sorter = CurrentSorter;
        bool ascending = _ascending;

        return Comparer<PackageWrapper>.Create((a, b) =>
        {
            if (sorter is Sorter.DownloadSize)
            {
                int resolved = (a.DownloadSizeBytes > 0 ? 0 : 1).CompareTo(b.DownloadSizeBytes > 0 ? 0 : 1);
                if (resolved != 0) return resolved;
            }
            return ascending ? Compare(a, b, sorter) : Compare(b, a, sorter);
        });
    }

    // Shared by the "Order by" menu (ApplyToList) and the DataGrid's native column sorting
    // (GetColumnComparer) so both order identically. Versions compare semantically via
    // CoreTools.Version; a string key can't be used there because that struct has no ToString
    // override, so its key would be a constant type name that never sorts.
    private static int Compare(PackageWrapper a, PackageWrapper b, Sorter sorter) => sorter switch
    {
        Sorter.Version => a.Package.NormalizedVersion.CompareTo(b.Package.NormalizedVersion),
        Sorter.DownloadSize => DownloadSizeSortKey(a).CompareTo(DownloadSizeSortKey(b)),
        Sorter.NewVersion => a.Package.NormalizedNewVersion.CompareTo(b.Package.NormalizedNewVersion),
        _ => string.Compare(GetSortKey(a, sorter), GetSortKey(b, sorter), StringComparison.OrdinalIgnoreCase),
    };

    private static long DownloadSizeSortKey(PackageWrapper wrapper)
        => wrapper.DownloadSizeBytes > 0 ? wrapper.DownloadSizeBytes : long.MaxValue;

    private static string GetSortKey(PackageWrapper w, Sorter sorter) => sorter switch
    {
        Sorter.Checked => w.IsChecked ? "0" : "1",
        Sorter.Name => w.Package.Name,
        Sorter.Id => w.Package.Id,
        Sorter.Source => w.Package.Source.AsString_DisplayName,
        _ => w.Package.Name,
    };

    // Comparer for the DataGrid's native column sorting. Reflection-free (unlike SortMemberPath),
    // so it keeps working under full-trim NativeAOT release builds — see issue #5103.
    public static IComparer GetColumnComparer(Sorter sorter) =>
        Comparer<PackageWrapper>.Create((a, b) => Compare(a, b, sorter));
}
