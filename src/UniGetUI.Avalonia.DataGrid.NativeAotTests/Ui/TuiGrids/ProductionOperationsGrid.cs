// Exact production fragment; inert wrapper/scaffold described in README.md.
using System.Linq.Expressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UniGetUI.Tui.Infrastructure;

namespace TuiGridFixture;

internal sealed class ProductionOperationsGrid
{
    private readonly DataGrid _queue;
    public DataGrid Grid => _queue;
    public ProductionOperationsGrid()
    {
        _queue = new DataGrid
        {
            MinColumnWidth = 3,
            AutoGenerateColumns = false,
            Background = Brushes.Transparent,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _queue.Columns.Add(new DataGridTextColumn { Header = "", Binding = DataGridTextBinding.Create<QueueRow>(r => r.State), IsReadOnly = true, Width = new DataGridLength(3), CanUserSort = true, CustomSortComparer = Comparer<QueueRow>.Create((a, b) => StringComparer.CurrentCulture.Compare(a.State, b.State)) });
        _queue.Columns.Add(new DataGridTextColumn { Header = CoreTools.Translate("Operation"), Binding = DataGridTextBinding.Create<QueueRow>(r => r.Title), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star), CanUserSort = true, CustomSortComparer = Comparer<QueueRow>.Create((a, b) => StringComparer.CurrentCulture.Compare(a.Title, b.Title)) });
        _queue.Columns.Add(new DataGridTextColumn { Header = CoreTools.Translate("Status"), Binding = DataGridTextBinding.Create<QueueRow>(r => r.Status), IsReadOnly = true, Width = new DataGridLength(16), CanUserSort = true, CustomSortComparer = Comparer<QueueRow>.Create((a, b) => StringComparer.CurrentCulture.Compare(a.Status, b.Status)) });
    }
}
