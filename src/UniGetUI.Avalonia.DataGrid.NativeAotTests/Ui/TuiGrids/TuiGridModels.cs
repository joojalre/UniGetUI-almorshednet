using Avalonia.Media;

namespace TuiGridFixture;

// Inert display rows only. No managers, processes, history stores, settings files
// or operation engine are constructed. Property names match the exact extracts.
internal sealed record PackageRow(string Check, string Name, string Id, string Version,
    string NewVersion, string Source, string InstallerHost, string DownloadSize, string State);
internal sealed record HistoryRow(string When, string Kind, string Package, string Manager, string Status, string Versions);
internal sealed record QueueRow(string State, string Title, string Status);
internal enum PackagePageKind { Discover, Updates, Installed, Bundles }
internal static class CoreTools
{
    public static string Translate(string text) => text;
}
internal static class TuiPalette
{
    public static IBrush Frame => Brushes.Gray;
}
internal static class Settings
{
    public enum K { ShowInstallerHostColumn, ShowDownloadSizeColumn }
    public static bool OptionalColumns { get; set; }
    public static bool Get(K _) => OptionalColumns;
}
