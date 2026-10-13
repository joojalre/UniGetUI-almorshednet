using Avalonia.Collections;

namespace UniGetUI.Tui.Infrastructure;

/// <summary>Retains list replacement semantics while identifying rows even when the list is empty.</summary>
internal sealed class DataGridList<T> : List<T>, IDataGridItemMetadataProvider
{
    private static readonly DataGridItemMetadata ItemTypeMetadata = new(typeof(T));

    public DataGridList() { }

    public DataGridList(IEnumerable<T> items) : base(items) { }

    DataGridItemMetadata IDataGridItemMetadataProvider.ItemMetadata => ItemTypeMetadata;
}
