using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace Iciclecreek.Avalonia.WindowManager
{
    internal static class WindowClosingEventArgsFactory
    {
        // Avalonia 12.0.4: internal WindowClosingEventArgs(WindowCloseReason, bool).
        [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
        internal static extern WindowClosingEventArgs Create(WindowCloseReason reason, bool isProgrammatic);
    }
}
