using System;

#if DATAGRID_LEGACY_REFLECTION
#error The maintained DataGrid variant must exclude legacy reflection in every build.
#endif

namespace Avalonia.Controls;

// The local maintained variant excludes legacy reflection at C# compilation.
// Typed templates, collections, comparers and grouping keep the original control engine.
internal static class DataGridReflectionFeatures
{
#if DATAGRID_LEGACY_REFLECTION
    internal static bool IsEnabled => true;
#else
    internal static bool IsEnabled => false;
#endif

    internal static InvalidOperationException Unsupported(string operation) => new(
        $"DataGrid reflection is disabled: {operation}. Supply IDataGridItemMetadataProvider, explicit template columns, typed comparers/grouping, or an explicit AOT-safe converter as appropriate.");
}
