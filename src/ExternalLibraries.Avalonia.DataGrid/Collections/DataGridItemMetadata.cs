using System;

namespace Avalonia.Collections;

/// <summary>Supplies explicit item metadata without inspecting a collection or its row members.</summary>
public interface IDataGridItemMetadataProvider
{
    DataGridItemMetadata ItemMetadata { get; }
}

/// <summary>Stable type identity and an optional direct factory for collection-view AddNew.</summary>
public sealed class DataGridItemMetadata
{
    public DataGridItemMetadata(Type itemType, Func<object>? newItemFactory = null)
    {
        ArgumentNullException.ThrowIfNull(itemType);
        if (itemType.ContainsGenericParameters || itemType == typeof(void))
            throw new ArgumentException("A closed item type is required.", nameof(itemType));
        ItemType = itemType;
        NewItemFactory = newItemFactory;
    }

    public Type ItemType { get; }
    public Func<object>? NewItemFactory { get; }
}
