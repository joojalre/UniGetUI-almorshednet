using OperationHistoryRowViewModel = Acceptance.FixtureRow;
namespace ProductionExtract;

internal sealed class OperationHistoryRowComparer : System.Collections.IComparer
{
    private readonly string _key;

    public OperationHistoryRowComparer(string key) => _key = key;

    public int Compare(object? x, object? y)
    {
        if (x is not OperationHistoryRowViewModel a || y is not OperationHistoryRowViewModel b)
            return 0;

        return _key switch
        {
            "kind" => string.Compare(a.KindLabel, b.KindLabel, StringComparison.OrdinalIgnoreCase),
            "package" => string.Compare(a.TargetName, b.TargetName, StringComparison.OrdinalIgnoreCase),
            "version" => string.Compare(a.VersionChange, b.VersionChange, StringComparison.OrdinalIgnoreCase),
            "source" => string.Compare(a.SourceLabel, b.SourceLabel, StringComparison.OrdinalIgnoreCase),
            "status" => string.Compare(a.StatusLabel, b.StatusLabel, StringComparison.OrdinalIgnoreCase),
            "date" => a.Timestamp.CompareTo(b.Timestamp),
            _ => 0,
        };
    }
}
