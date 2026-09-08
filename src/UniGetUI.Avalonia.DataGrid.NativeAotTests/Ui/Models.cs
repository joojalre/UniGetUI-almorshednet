using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Collections;
using CoreVersion = UniGetUI.Core.Tools.CoreTools.Version;
#if PATCHED_DATAGRID
using MetadataRows = UniGetUI.Avalonia.Models.DataGridObservableCollection<Acceptance.FixtureRow>;
#endif

namespace Acceptance;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class SourceValue(string value) : Observable
{
    private string _value = value;
    public string AsString_DisplayName { get => _value; set { _value = value; Changed(nameof(AsString_DisplayName)); } }
}

public sealed class PackageValue(string name, string id, SourceValue source, CoreVersion version, CoreVersion newVersion) : Observable
{
    public string Name { get; } = name;
    public string Id { get; } = id;
    public CoreVersion NormalizedVersion { get; } = version;
    public CoreVersion NormalizedNewVersion { get; } = newVersion;
    public string VersionString => $"{NormalizedVersion.Major}.{NormalizedVersion.Minor}";
    public string NewVersionString => $"{NormalizedNewVersion.Major}.{NormalizedNewVersion.Minor}";
    private SourceValue _source = source;
    public SourceValue Source { get => _source; set { _source = value; Changed(nameof(Source)); } }
}

public sealed class RecordCommand(Action<object?> action) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action(parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

// These rows are deliberately inert stand-ins. No production wrapper/shortcut/history constructor runs.
public sealed class FixtureRow : Observable
{
    public FixtureRow(string identity, string name, string packageId, string source, int version, int next, string kind = "Install", string status = "Succeeded", long dateTicks = 1)
    {
        Identity = identity;
        Package = new(name, packageId, new(source), new CoreVersion(version), new CoreVersion(next));
        KindLabel = kind; StatusLabel = status; Timestamp = new DateTime(dateTicks, DateTimeKind.Utc);
        VersionChange = Package.VersionString; SourceLabel = source;
        OpenCommand = new RecordCommand(_ => Actions.Add("open:" + Identity));
        RemoveCommand = new RecordCommand(_ => Actions.Add("remove:" + Identity));
    }
    public string Identity { get; }
    public PackageValue Package { get; }
    public string Name => Package.Name;
    public string TargetName => Package.Name;
    public string KindLabel { get; }
    public string VersionChange { get; set; }
    public string SourceLabel { get; set; }
    public string StatusLabel { get; }
    public DateTime Timestamp { get; }
    public string Path => "inert://shortcut/" + Identity;
    public string Location => "Synthetic folder " + Identity;
    public bool ExistsOnDisk { get; set; } = true; // Static fixture data, never File.Exists.
    public bool CanStopTracking { get; set; } = true;
    private bool _checked;
    public bool IsChecked { get => _checked; set { _checked = value; Changed(nameof(IsChecked)); Changed(nameof(IsDeletable)); } }
    public bool IsDeletable { get => IsChecked; set => IsChecked = value; }
    public List<string> Actions { get; } = [];
    public ICommand OpenCommand { get; }
    public ICommand RemoveCommand { get; }
}

#if !PATCHED_DATAGRID
public sealed class MetadataRows : ObservableCollection<FixtureRow> { }
#endif

internal sealed class GridModel : Observable
{
    private MetadataRows _rows = [];
    public MetadataRows Rows { get => _rows; set { _rows = value; Changed(nameof(Rows)); } }
}
