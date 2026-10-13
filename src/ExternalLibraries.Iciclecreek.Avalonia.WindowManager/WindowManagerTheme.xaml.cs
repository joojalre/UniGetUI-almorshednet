using System;
using global::Avalonia.Markup.Xaml;
using global::Avalonia.Styling;

namespace Iciclecreek.Avalonia.WindowManager;

public class WindowManagerTheme : Styles
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SimpleTheme"/> class.
    /// </summary>
    /// <param name="sp">The parent's service provider.</param>
    public WindowManagerTheme(IServiceProvider? sp = null)
    {
        AvaloniaXamlLoader.Load(sp, this);
    }
}
