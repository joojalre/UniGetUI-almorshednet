using System.Collections;
using System.ComponentModel;
using Avalonia.Collections;
using IPackage = Acceptance.PackageValue;
using PackageWrapper = Acceptance.FixtureRow;

namespace ProductionExtract;

public sealed class ObservablePackageCollection : AvaloniaList<PackageWrapper>
{
    public enum Sorter
    {
        Checked,
        Name,
        Id,
        Version,
        NewVersion,
        Source,
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

    public void SortBy(Sorter sorter) => CurrentSorter = sorter;

    public void SetSortDirection(bool ascending) => _ascending = ascending;

    /// <summary>Returns <paramref name="items"/> in the current sort order.</summary>
    public IEnumerable<PackageWrapper> ApplyToList(IEnumerable<PackageWrapper> items)
    {
        var comparer = Comparer<PackageWrapper>.Create((a, b) => Compare(a, b, CurrentSorter));
        return _ascending ? items.OrderBy(w => w, comparer) : items.OrderByDescending(w => w, comparer);
    }

    // Shared by the "Order by" menu (ApplyToList) and the DataGrid's native column sorting
    // (GetColumnComparer) so both order identically. Versions compare semantically via
    // CoreTools.Version; a string key can't be used there because that struct has no ToString
    // override, so its key would be a constant type name that never sorts.
    private static int Compare(PackageWrapper a, PackageWrapper b, Sorter sorter) => sorter switch
    {
        Sorter.Version => a.Package.NormalizedVersion.CompareTo(b.Package.NormalizedVersion),
        Sorter.NewVersion => a.Package.NormalizedNewVersion.CompareTo(b.Package.NormalizedNewVersion),
        _ => string.Compare(GetSortKey(a, sorter), GetSortKey(b, sorter), StringComparison.OrdinalIgnoreCase),
    };

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
