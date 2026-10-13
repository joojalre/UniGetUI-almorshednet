using System.Collections;
using System.ComponentModel;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.VisualTree;
using NUnit.Framework;
using UniGetUI.Core.SettingsEngine;
using UniGetUI.PackageEngine.Operations.History;
using UniGetUI.Tui.Infrastructure;
using UniGetUI.Tui.Views.Pages;
using NAssert = NUnit.Framework.Assert;

namespace UniGetUI.Tui.Tests;

[TestFixture, NonParallelizable]
internal sealed class TuiDataGridContractTests : TuiE2ETestBase
{
    [TestCase(TuiPageIds.Discover)]
    [TestCase(TuiPageIds.Updates)]
    [TestCase(TuiPageIds.Installed)]
    [TestCase(TuiPageIds.Bundles)]
    public async Task PackageVariants_OptionalColumnsRemainTypedAndReadOnly(string pageId)
    {
        bool installer = Settings.Get(Settings.K.ShowInstallerHostColumn);
        bool size = Settings.Get(Settings.K.ShowDownloadSizeColumn);
        try
        {
            await OnUi(() =>
            {
                Settings.Set(Settings.K.ShowInstallerHostColumn, true);
                Settings.Set(Settings.K.ShowDownloadSizeColumn, true);
            });
            await ResetAsync(pageId);
            await OnUi(() =>
            {
                var grid = Grid();
                AssertMetadata(grid, typeof(PackageRow), empty: pageId is TuiPageIds.Discover or TuiPageIds.Bundles);
                var headers = grid.Columns.Select(c => c.Header?.ToString()).ToArray();
                NAssert.That(headers.Contains("New version"), Is.EqualTo(pageId == TuiPageIds.Updates));
                NAssert.That(headers.Contains("Installer host"), Is.EqualTo(pageId != TuiPageIds.Installed));
                NAssert.That(headers.Contains("Download size"), Is.EqualTo(pageId != TuiPageIds.Installed));
            });
        }
        finally
        {
            await OnUi(() =>
            {
                Settings.Set(Settings.K.ShowInstallerHostColumn, installer);
                Settings.Set(Settings.K.ShowDownloadSizeColumn, size);
            });
        }
    }

    [Test]
    public async Task ActualFakeOperationAndHistory_RefreshPreservesSelectedIdentity()
    {
        bool maintain = Settings.Get(Settings.K.MaintainSuccessfulInstalls);
        try
        {
            await ResetAsync(TuiPageIds.Updates);
            await OnUi(() => Settings.Set(Settings.K.MaintainSuccessfulInstalls, true));
            await EnterPage();
            await FilterPackagesAsync("Contoso Editor");
            await Type("u");
            await WaitForOperationsToFinishAsync();
            await WaitUntilAsync(() => OperationHistoryStore.GetAll().Count == 1, "one fake operation history record");
            await OnUi(() => Window.NavigateTo(TuiPageIds.Operations));
            await UITest.WaitRendered();
            await OnUi(() =>
            {
                var page = (OperationsPage)Window.CurrentPage;
                var grid = Grid();
                NAssert.That(page.Rows, Is.Not.Empty);
                var operation = page.Rows[0].Operation;
                grid.SelectedItem = page.Rows[0];
                var oldRows = grid.ItemsSource;
                page.Reload();
                NAssert.That(grid.ItemsSource, Is.Not.SameAs(oldRows));
                NAssert.That(((OperationsPage.QueueRow)grid.SelectedItem!).Operation, Is.SameAs(operation));
                NAssert.That(page.SelectedOperation, Is.SameAs(operation));
                Window.UpdateLayout();
                NAssert.That(grid.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == page.Rows[0].Title), Is.True);
            });
            await OnUi(() => Window.NavigateTo(TuiPageIds.History));
            await UITest.WaitRendered();
            await OnUi(() =>
            {
                var history = (HistoryPage)Window.CurrentPage;
                var grid = Grid();
                var record = history.Rows.Single().Record;
                grid.SelectedItem = history.Rows[0];
                var oldRows = grid.ItemsSource;
                history.Refresh();
                NAssert.That(grid.ItemsSource, Is.Not.SameAs(oldRows));
                NAssert.That(((HistoryPage.HistoryRow)grid.SelectedItem!).Record.Id, Is.EqualTo(record.Id));
                Window.UpdateLayout();
                NAssert.That(grid.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == history.Rows[0].Package), Is.True);
                OperationHistoryStore.Clear();
                history.Refresh();
                AssertMetadata(grid, typeof(HistoryPage.HistoryRow), empty: true);
                NAssert.That(grid.SelectedItem, Is.Null);
                OperationHistoryStore.Add(record);
                history.Refresh();
                NAssert.That(history.Rows.Single().Record.Id, Is.EqualTo(record.Id));
                AssertMetadata(grid, typeof(HistoryPage.HistoryRow), empty: false);
            });
        }
        finally
        {
            await OnUi(() => Settings.Set(Settings.K.MaintainSuccessfulInstalls, maintain));
        }
    }

    [Test]
    public async Task PackageGrid_EmptyAndPopulatedSourcesRetainTypedSorting()
    {
        await ResetAsync(TuiPageIds.Discover);
        await OnUi(() => AssertMetadata(Grid(), typeof(PackageRow), empty: true));
        await ResetAsync(TuiPageIds.Installed);
        var page = await OnUi(() => (PackageListPage)Window.CurrentPage);
        var grid = await OnUi(Grid);
        int originalField = await OnUi(() => page.SortField);
        bool originalDirection = await OnUi(() => page.SortAscending);
        await OnUi(() =>
        {
            AssertMetadata(grid, typeof(PackageRow), empty: false);
            NAssert.That(grid.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == page.VisibleRows[0].Name), Is.True);
        });
        try
        {
            var columns = await OnUi(() => grid.Columns.Where(c => c.CanUserSort).ToArray());
            foreach (var column in columns)
            {
                int field = (int)column.Tag!;
                for (int click = 0; click < 2; click++)
                {
                    bool direction = await OnUi(() => page.SortField == field ? !page.SortAscending : true);
                    await OnUi(() => column.Sort(ListSortDirection.Ascending));
                    await UITest.WaitRendered();
                    await OnUi(() =>
                    {
                        NAssert.That(page.SortField, Is.EqualTo(field));
                        NAssert.That(page.SortAscending, Is.EqualTo(direction));
                        NAssert.That(((IEnumerable)grid.CollectionView).Cast<PackageRow>(), Is.EqualTo(page.VisibleRows));
                    });
                }
            }
            await OnUi(() => NAssert.That(grid.BeginEdit(), Is.False));
        }
        finally
        {
            var original = await OnUi(() => grid.Columns.Single(c => c.Tag is int field && field == originalField));
            if (await OnUi(() => page.SortField != originalField))
            {
                await OnUi(() => original.Sort(ListSortDirection.Ascending));
                await UITest.WaitRendered();
            }
            if (await OnUi(() => page.SortAscending != originalDirection))
            {
                await OnUi(() => original.Sort(ListSortDirection.Ascending));
                await UITest.WaitRendered();
            }
        }
    }

    [Test]
    public async Task HistoryGrid_EmptyThenPopulatedSupportsEveryHeader()
    {
        await ResetAsync(TuiPageIds.History);
        var a = new HistoryPage.HistoryRow(null!, "2026-10-11", "Install", "middle", "z-manager", "Running", "3 to 4");
        var b = new HistoryPage.HistoryRow(null!, "2026-10-12", "Update", "alpha", "a-manager", "Succeeded", "2 to 3");
        var c = new HistoryPage.HistoryRow(null!, "2026-10-13", "Uninstall", "zeta", "m-manager", "Failed", "1 to 2");
        await AssertStringSortColumns(new[] { a, b, c }, new[]
        {
            new[] { a, b, c }, new[] { a, c, b }, new[] { b, a, c },
            new[] { b, c, a }, new[] { c, a, b }, new[] { c, b, a },
        }, "middle", "alpha");
    }

    [Test]
    public async Task OperationsGrid_EmptyThenPopulatedSupportsEveryHeader()
    {
        await ResetAsync(TuiPageIds.Operations);
        var a = new OperationsPage.QueueRow(null!, "a", "Update zeta", "Running");
        var b = new OperationsPage.QueueRow(null!, "m", "Install alpha", "Succeeded");
        var c = new OperationsPage.QueueRow(null!, "z", "Remove middle", "Queued");
        await AssertStringSortColumns(new[] { a, b, c }, new[]
        {
            new[] { a, b, c }, new[] { b, c, a }, new[] { c, a, b },
        }, "Update zeta", "Install alpha");
    }

    private static DataGrid Grid() => Window.CurrentPage.GetVisualDescendants().OfType<DataGrid>().Single();

    private static void AssertMetadata(DataGrid grid, Type rowType, bool empty)
    {
        NAssert.That(grid.ItemsSource, Is.InstanceOf<IDataGridItemMetadataProvider>());
        var metadata = ((IDataGridItemMetadataProvider)grid.ItemsSource).ItemMetadata;
        NAssert.That(metadata.ItemType, Is.EqualTo(rowType));
        NAssert.That(metadata.NewItemFactory, Is.Null);
        NAssert.That(grid.CollectionView.IsEmpty, Is.EqualTo(empty));
        NAssert.That(grid.AutoGenerateColumns, Is.False);
        NAssert.That(grid.IsReadOnly, Is.True);
        // Reading these properties used to enter the reflection-only column inference path.
        foreach (var column in grid.Columns)
        {
            NAssert.That(column.IsReadOnly, Is.True);
            _ = column.CanUserSort;
        }
    }

    private async Task AssertStringSortColumns<T>(T[] rows, T[][] expectedAscending, string firstText, string secondText) where T : class
    {
        var grid = await OnUi(Grid);
        var original = await OnUi(() => grid.ItemsSource);
        try
        {
            await OnUi(() =>
            {
                AssertMetadata(grid, typeof(T), empty: true);
                grid.ItemsSource = new DataGridList<T>(rows.Reverse());
            });
            await UITest.WaitRendered();
            await OnUi(() =>
            {
                AssertMetadata(grid, typeof(T), empty: false);
                var text = grid.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                NAssert.That(text, Does.Contain(firstText));
                NAssert.That(text, Does.Contain(secondText));
                NAssert.That(grid.Columns.Count, Is.EqualTo(expectedAscending.Length));
            });
            for (int index = 0; index < expectedAscending.Length; index++)
            {
                var column = await OnUi(() => grid.Columns[index]);
                await OnUi(() =>
                {
                    NAssert.That(column.CanUserSort, Is.True);
                    NAssert.That(column.CustomSortComparer, Is.Not.Null);
                    column.Sort(ListSortDirection.Ascending);
                });
                await UITest.WaitRendered();
                await OnUi(() =>
                {
                    NAssert.That(((IEnumerable)grid.CollectionView).Cast<T>(), Is.EqualTo(expectedAscending[index]));
                    NAssert.That(grid.CollectionView.SortDescriptions.Single().Direction, Is.EqualTo(ListSortDirection.Ascending));
                    column.Sort(ListSortDirection.Descending);
                });
                await UITest.WaitRendered();
                await OnUi(() =>
                {
                    NAssert.That(((IEnumerable)grid.CollectionView).Cast<T>(), Is.EqualTo(expectedAscending[index].Reverse()));
                    NAssert.That(grid.CollectionView.SortDescriptions.Single().Direction, Is.EqualTo(ListSortDirection.Descending));
                    column.ClearSort();
                });
                await UITest.WaitRendered();
            }
            await OnUi(() =>
            {
                grid.SelectedItem = rows[0];
                NAssert.That(grid.SelectedItem, Is.SameAs(rows[0]));
                NAssert.That(grid.BeginEdit(), Is.False);
                grid.ItemsSource = new DataGridList<T>();
                AssertMetadata(grid, typeof(T), empty: true);
                NAssert.That(grid.SelectedItem, Is.Null);
            });
        }
        finally
        {
            await OnUi(() => grid.ItemsSource = original);
            await UITest.WaitRendered();
        }
    }
}
