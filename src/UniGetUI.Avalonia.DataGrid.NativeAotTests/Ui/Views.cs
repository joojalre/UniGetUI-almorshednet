using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
namespace Acceptance;

public sealed class App : Application { public override void Initialize() => AvaloniaXamlLoader.Load(this); }
public sealed partial class PackagesView : UserControl { public PackagesView() => AvaloniaXamlLoader.Load(this); }
public sealed partial class HistoryView : UserControl { public HistoryView() => AvaloniaXamlLoader.Load(this); internal HistoryView(GridModel model) { DataContext = model; AvaloniaXamlLoader.Load(this); } }
public sealed partial class DesktopView : UserControl { public DesktopView() => AvaloniaXamlLoader.Load(this); internal DesktopView(GridModel model) { DataContext = model; AvaloniaXamlLoader.Load(this); } }
public sealed partial class StartMenuView : UserControl { public StartMenuView() => AvaloniaXamlLoader.Load(this); internal StartMenuView(GridModel model) { DataContext = model; AvaloniaXamlLoader.Load(this); } }
