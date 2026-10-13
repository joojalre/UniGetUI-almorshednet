// Exact production fragment; inert wrapper/scaffold described in README.md.
using System.Linq.Expressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UniGetUI.Tui.Infrastructure;

namespace TuiGridFixture;

internal sealed class ProductionHistoryGrid
{
    private readonly DataGrid _grid;
    public DataGrid Grid => _grid;
    public ProductionHistoryGrid()
    {
        _grid = new DataGrid
        {
            MinColumnWidth = 3,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            Background = Brushes.Transparent,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        // Compiled (lambda) bindings: no reflection, so they survive trimming and NativeAOT.
        void Col(string header, Expression<Func<HistoryRow, string>> prop, Func<HistoryRow, string> value, double width) => _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = DataGridTextBinding.Create(prop),
            IsReadOnly = true,
            CanUserSort = true,
            CustomSortComparer = Comparer<HistoryRow>.Create((a, b) => StringComparer.CurrentCulture.Compare(value(a), value(b))),
            Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        Col(CoreTools.Translate("Date"), r => r.When, r => r.When, 17);
        Col(CoreTools.Translate("Operation"), r => r.Kind, r => r.Kind, 11);
        Col(CoreTools.Translate("Package"), r => r.Package, r => r.Package, 0);
        Col(CoreTools.Translate("Manager"), r => r.Manager, r => r.Manager, 12);
        Col(CoreTools.Translate("Status"), r => r.Status, r => r.Status, 10);
        Col(CoreTools.Translate("Version"), r => r.Versions, r => r.Versions, 20);
    }
}
