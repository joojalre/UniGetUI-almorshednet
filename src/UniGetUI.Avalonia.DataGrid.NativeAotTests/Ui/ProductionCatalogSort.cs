namespace Acceptance;

public enum CatalogSortField { Name, Id, Source }

public sealed partial class SoftwareCatalogViewModel
{
    public void ToggleSort(CatalogSortField field)
    {
        if (SortField == field) SortAscending = !SortAscending;
        else
        {
            _sortAscending = true;
            OnPropertyChanged(nameof(SortAscending));
            SortField = field;
        }
    }

    private static string SortKey(CatalogSortField field, CatalogTileViewModel t) => field switch
    {
        CatalogSortField.Id => t.Entry.Id,
        CatalogSortField.Source => t.Entry.ManagerName + "/" + t.Entry.Source,
        _ => t.Entry.Name,
    };

    public static int CompareTiles(CatalogSortField field, CatalogTileViewModel a, CatalogTileViewModel b)
        => StringComparer.CurrentCultureIgnoreCase.Compare(SortKey(field, a), SortKey(field, b));

    private CatalogTileViewModel[] SortTiles(CatalogTileViewModel[] tiles)
    {
        var field = SortField;
        string Key(CatalogTileViewModel t) => SortKey(field, t);
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        return (SortAscending ? tiles.OrderBy(Key, comparer) : tiles.OrderByDescending(Key, comparer)).ToArray();
    }
}
