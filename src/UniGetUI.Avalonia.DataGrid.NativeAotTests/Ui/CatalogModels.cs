using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using ProductionExtract;

namespace Acceptance;

// Inert models keep Catalog's collection and binding shape without invoking managers or installers.
public sealed record CatalogEntry(string Name, string Id, string ManagerName, string Source);

public sealed class CatalogTileViewModel(CatalogEntry entry, string catalogName = "Fixture catalog") : Observable
{
    public CatalogEntry Entry { get; } = entry;
    public string CatalogName { get; } = catalogName;
    // Production resolves the manager's display name through PEInterface; fixtures have no managers.
    public string ManagerDisplayName => Entry.ManagerName;
    private Bitmap? _iconBitmap;
    public Bitmap? IconBitmap { get => _iconBitmap; set { _iconBitmap = value; Changed(nameof(IconBitmap)); Changed(nameof(HasCustomIcon)); } }
    public bool HasCustomIcon => IconBitmap is not null;
    public bool IsInstalled { get; private set; }
    public string? UnavailableReason { get; private set; }
    public bool InventoryKnown { get; private set; }
    public bool IsPending { get; private set; }
    public bool IsBusy { get; private set; }
    public bool ShowInstall => !IsInstalled;
    public bool CanInstall => !IsInstalled && UnavailableReason is null && InventoryKnown && !IsPending && !IsBusy;
    public double TitleOpacity => UnavailableReason is null ? 1 : 0.45;
    // Translation is deliberately omitted; state precedence matches production.
    public string StatusText => UnavailableReason ?? (IsBusy ? "Loading packages" : IsPending ? "Installing" : !InventoryKnown ? "Waiting for installed package information. Refresh Installed Packages if this persists." : "");
    public void UpdateState(bool installed, string? unavailableReason, bool inventoryKnown, bool pending, bool busy = false)
    {
        IsInstalled = installed; UnavailableReason = unavailableReason; InventoryKnown = inventoryKnown; IsPending = pending; IsBusy = busy;
        foreach (var name in new[] { nameof(IsInstalled), nameof(UnavailableReason), nameof(InventoryKnown), nameof(IsPending), nameof(IsBusy), nameof(ShowInstall), nameof(CanInstall), nameof(TitleOpacity), nameof(StatusText) }) Changed(name);
    }
}

public sealed partial class SoftwareCatalogViewModel : Observable
{
    public ObservableCollection<CatalogTileViewModel> Packages { get; } = new UniGetUI.Avalonia.Models.DataGridObservableCollection<CatalogTileViewModel>();
    public List<CatalogTileViewModel> RecordedInstalls { get; } = [];
    public ICommand InstallCommand { get; }
    private CatalogSortField _sortField = CatalogSortField.Name;
    private bool _sortAscending = true;
    public CatalogSortField SortField { get => _sortField; set { if (_sortField == value) return; _sortField = value; ApplyAvailabilityFilter(); OnPropertyChanged(nameof(SortField)); } }
    public bool SortAscending { get => _sortAscending; set { if (_sortAscending == value) return; _sortAscending = value; ApplyAvailabilityFilter(); OnPropertyChanged(nameof(SortAscending)); } }
    public SoftwareCatalogViewModel() => InstallCommand = new RecordCommand(value => RecordedInstalls.Add((CatalogTileViewModel)value!));
    private void OnPropertyChanged(string name) => Changed(name);
    // Sort persistence is a Settings write in production; fixtures keep no settings.
    private static void SaveSortDirection(bool _) { }
    private void ApplyAvailabilityFilter()
    {
        var ordered = SortTiles(Packages.ToArray());
        if (Packages.SequenceEqual(ordered)) return;
        Packages.Clear();
        foreach (var item in ordered) Packages.Add(item);
    }
    public void Refresh(IEnumerable<CatalogTileViewModel> rows)
    {
        var ordered = SortTiles(rows.ToArray());
        Packages.Clear();
        foreach (var row in ordered) Packages.Add(row);
    }
}
