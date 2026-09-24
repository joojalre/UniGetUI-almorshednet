# DataGrid JIT and NativeAOT acceptance

This console tests the maintained DataGrid project directly. It deliberately does not reference or start the application. The application collection helper is linked from its real source file. The four UI fixtures use inert package, history and shortcut models with compiled XAML, the application's comparer logic and a record-only command implementation.

Run from the repository root:

```powershell
./scripts/verify-datagrid-provenance.ps1
./scripts/verify-datagrid-aot.ps1 -OutputDirectory <fresh-evidence-directory> -RuntimeIdentifier win-x64
```

This is an executable acceptance suite, not a VSTest assembly: `dotnet test` does not execute it. The script runs `boundaries` and `ui` in separate processes in JIT and NativeAOT, checks their exits and assertions, compares corresponding results and retains logs and hashes. Native commands require `--expect-aot`; a JIT fallback fails the runtime assertion.

The boundary suite has 38 assertions covering explicit/compiled template columns, empty and populated sources, notifications, sorting, checkbox binding, selection, item factories, typed grouping and explicit rejection of unsupported reflection requests. The UI suite has 316 assertions across four grids. Each grid starts empty, displays its first added row with the expected identity and compiled binding, and later clears a selected row and repopulates. Coverage also includes 400-row virtualization, sorting and null/equal/nested values, source changes and column visibility/resize/reorder.

Keyboard checks verify exact Down/Up row selection, Home/End column movement, platform command modifier + Home/End row and column endpoints (Control on Windows/Linux, Command on macOS), page movement and endpoint clamping, retained current column and grid focus. Large-page expectations use the visible rows presenter's height and the fixture's fixed row height. Tab and Shift+Tab verify specific template control focus and unchanged selection before any sorting/recycling; visual traversal order after row recycling is not assumed to equal collection order. Pointer/Space checkbox changes, two-way model binding and inert commands remain covered after sorting. The actual template columns reject edit transactions; this suite does not invent editable text cells for these read-only columns. Changes to copied comparer/version regions must be reflected in the fixture and pass the source-drift check.

Each command initializes Avalonia once with headless drawing. This verifies control state and input behavior, not rendered pixels, operating-system accessibility, real package operations, network services, user databases or installed shortcuts. The Windows CI job establishes win-x64 acceptance; the other release architectures require their own compatible host/toolchain and runtime checks.
