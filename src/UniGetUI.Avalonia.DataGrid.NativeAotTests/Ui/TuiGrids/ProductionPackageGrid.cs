// Exact production fragment; inert wrapper/scaffold described in README.md.
using System.Linq.Expressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UniGetUI.Tui.Infrastructure;

namespace TuiGridFixture;

internal sealed class ProductionPackageGrid
{
    private readonly PackagePageKind _kind;
    public DataGrid Grid { get; }
    public int SortField { get; private set; }
    public bool SortAscending { get; private set; } = true;
    public List<(int Field, bool Ascending)> RecordedSorts { get; } = [];
    public ProductionPackageGrid(PackagePageKind kind)
    {
        _kind = kind;
        Grid = BuildGrid();
    }
    // Recording scaffold only: deliberately does not simulate package-engine sorting.
    private void SetSort(int field, bool ascending)
    {
        RecordedSorts.Add((field, ascending));
        SortField = field;
        SortAscending = ascending;
    }
    private DataGrid BuildGrid()
    {
        var grid = new DataGrid
        {
            MinColumnWidth = 3,
            AutoGenerateColumns = false,
            Background = Brushes.Transparent,
            BorderBrush = TuiPalette.Frame,
            CanUserSortColumns = true,
            CanUserReorderColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // Compiled (lambda) bindings, not name-based ones: they need no reflection, so they survive trimming and NativeAOT.
        void Column(string header, Expression<Func<PackageRow, string>> property, double width, int? sortField = null)
        {
            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = DataGridTextBinding.Create(property),
                IsReadOnly = true,
                Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
                CanUserSort = sortField is not null,
                Tag = sortField,
            };
            grid.Columns.Add(column);
        }

        Column("Sel", r => r.Check, 4);
        Column(CoreTools.Translate("Package Name"), r => r.Name, 0, 0);
        Column(CoreTools.Translate("Package ID"), r => r.Id, 30, 1);
        Column(CoreTools.Translate("Version"), r => r.Version, 12, 2);
        if (_kind == PackagePageKind.Updates)
            Column(CoreTools.Translate("New version"), r => r.NewVersion, 12, 3);
        Column(CoreTools.Translate("Source"), r => r.Source, 18, 4);
        if (_kind != PackagePageKind.Installed && Settings.Get(Settings.K.ShowInstallerHostColumn))
            Column(CoreTools.Translate("Installer host"), r => r.InstallerHost, 18);
        if (_kind != PackagePageKind.Installed && Settings.Get(Settings.K.ShowDownloadSizeColumn))
            Column(CoreTools.Translate("Download size"), r => r.DownloadSize, 15);
        Column("", r => r.State, 6);

        // Column-header clicks drive the page's own (persisted) sort instead of the grid's.
        grid.Sorting += (_, e) =>
        {
            e.Handled = true;
            if (e.Column.Tag is int field) SetSort(field, field == SortField ? !SortAscending : true);
        };
        return grid;
    }
}
