using System.ComponentModel;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Acceptance;

public sealed partial class CatalogView : UserControl
{
    private readonly SoftwareCatalogViewModel _viewModel;
    public int HandledSortEvents { get; private set; }
    public CatalogView() : this(new SoftwareCatalogViewModel()) { }
    public CatalogView(SoftwareCatalogViewModel model)
    {
        _viewModel = model;
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
        PackageList = this.FindControl<DataGrid>("PackageList")!;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SoftwareCatalogViewModel.SortField) or nameof(SoftwareCatalogViewModel.SortAscending)) UpdateSortArrows();
        };
        foreach (var col in PackageList.Columns)
        {
            if (col.Tag is not string tag || !Enum.TryParse<CatalogSortField>(tag, out var colField)) continue;
            col.CanUserSort = true;
            col.CustomSortComparer = System.Collections.Generic.Comparer<CatalogTileViewModel>.Create(
                (a, b) => SoftwareCatalogViewModel.CompareTiles(colField, a, b));
        }
        SetupSortArrows();
        PackageList.Sorting += (_, e) =>
        {
            e.Handled = true;
            if (e.Column.Tag is string tag && Enum.TryParse<CatalogSortField>(tag, out var field))
                _viewModel.ToggleSort(field);
            HandledSortEvents++;
        };
    }

    private void SetupSortArrows()
    {
        PackageList.AttachedToVisualTree += (_, _) => UpdateSortArrows();
        PackageList.PropertyChanged += (_, e) =>
        {
            if (e.Property == DataGrid.ItemsSourceProperty) UpdateSortArrows();
        };
        UpdateSortArrows();
    }

    private void UpdateSortArrows()
    {
        var view = PackageList.CollectionView;
        if (view is null) return;
        var direction = _viewModel.SortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;
        var active = PackageList.Columns.FirstOrDefault(c => c.Tag as string == _viewModel.SortField.ToString());
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            if (active?.CustomSortComparer is { } comparer)
                view.SortDescriptions.Add(DataGridSortDescription.FromComparer(comparer, direction));
        }
    }
}
