using System.Collections.ObjectModel;
using Avalonia.Collections;

namespace UniGetUI.Avalonia.Models;

/// <summary>Provides the row type for template-only DataGrids, including an empty source.</summary>
internal sealed class DataGridObservableCollection<T> : ObservableCollection<T>, IDataGridItemMetadataProvider
{
    private static readonly DataGridItemMetadata _itemMetadata = new(typeof(T));

    DataGridItemMetadata IDataGridItemMetadataProvider.ItemMetadata => _itemMetadata;
}
