# Maintained TUI dependencies

The TUI uses pinned source projects for four dependency assemblies so its Windows console error paths, handle conversion and managed-window closing can compile with NativeAOT. The projects preserve the original assembly names, versions and resource paths. They are modified local builds, not upstream releases.

| Project | Upstream pin | Purpose |
| --- | --- | --- |
| Consolonia.PlatformSupport | [Consolonia 5c317423](https://github.com/consolonia/consolonia/tree/5c317423e7fe9896d9a4fab412b3fba8c7ef1e7b), 12.0.3.14 | Windows/Unix console and clipboard implementations |
| Consolonia.GuiCS | Same Consolonia pin, 12.0.3.14 | Console input implementation formerly embedded in the PlatformSupport package |
| Vanara.PInvoke.Shared | [Vanara 3da05fe4](https://github.com/dahall/Vanara/tree/3da05fe4d87bc5de96527fad5c9b7e6058690deb), 4.0.4 | Shared handles and native types, retaining the original public-key identity |
| Iciclecreek.Avalonia.WindowManager | 4e57389db02f32f4572146afb7e37304fe7db667, 5.0.1 | Managed windows and themes |

The original import hashes, pinned archive URLs and licenses are recorded in `import-manifest.json`. `candidate-source-manifest.json` describes the independently reviewed source import before subsequent integration changes. `minimal-source.patch` records the behavioral patches against those pinned sources. The per-project `PROVENANCE.md` and licenses are copied to distinct `ThirdPartyNotices` directories in build and publish outputs, including GuiCS's separate MIT notice.

Five native error call sites now immediately capture `Marshal.GetLastPInvokeError()` and throw `Win32Exception`. The native error code and failing operation remain available; the exception subtype is explicitly Win32Exception rather than the previous HRESULT helper's possible subtype mapping. The handle converter uses `typeof(IHandle).IsAssignableFrom(sourceType)` for the nongeneric interface instead of a reflection walker. Managed-window closing binds directly to Avalonia's constructor through a typed `UnsafeAccessor`, preserving close reason, programmatic state and cancellation.

The projects retain Windows and Unix implementation source. No reflection fallback, blanket IL suppression, package-cache modification or feature removal is part of this integration. Vanara.Core and Kernel32 remain the pinned 4.0.4 packages; the other Consolonia packages remain 12.0.3.14. Shared uses public-key-only delay signing for compatibility with those precompiled references and is not an upstream-signed release.

An isolated win-x64 dependency probe passed ten managed and ten NativeAOT assertions before import. That probe covered error conversion, handle conversion and typed closing-event construction; it does not establish acceptance of the complete application, terminal rendering or other runtime identifiers. The final application must pass its unsuppressed NativeAOT publish and metadata verification, DataGrid tests, actual fake-data TUI tests and the repository's style gates. Restore and output checks must retain a single producer for each replaced assembly.

Integration also applies the repository's whitespace and code-style rules. `integration-formatting.patch` and `integration-formatting-delta.json` record the derived formatting; the original ANSI `HRESULT.Values.cs` comments are explicitly decoded as Windows-1252 so eight em-dashes and nine nonbreaking spaces survive conversion to UTF-8. A Roslyn comparison verified exact token kinds/text across all per-file conditional-symbol combinations in the 117 formatting changes (131 parse pairs, at most four combinations per file). This checks executable tokens/directives, not universal equivalence of comments, source positions, instrumentation or every future build configuration.

`integration-style.patch` and `integration-style-delta.json` separately record modifier order, readonly fields, import order and guarded UI expression corrections. These token-changing edits are distinct from the whitespace proof and were independently reviewed. The three Unity/Xamarin constants remain under `#if !GUICS` to preserve their existing alternate callers. Native struct private fields retain their order, primitive types and sequential layout. Current native compilation and runtime acceptance remain required after these changes. `integration-documentation-delta.json` records the four project provenance-link updates.

## Clipboard review corrections

`review-clipboard.patch` records the subsequent five-file PlatformSupport correction against the integration commit identified in `review-clipboard-delta.json`. That manifest distinguishes Git blob bytes from the exact working bytes used by local validation; the original import, minimal patch and formatting/style records remain historical stages.

WSL clipboard text is now base64 data on standard input to a constant PowerShell script, never interpolated into executable script text. Its dedicated process path drains both output pipes and bounds input, output and the shared deadline; the original XClip/Bash process path is preserved. Clipboard wrapper construction uses the pinned Avalonia constructor through a typed `UnsafeAccessor`. macOS pasteboard string imports and returned strings explicitly use UTF-8. The test assembly alone receives internal access for isolated contract fixtures.

Managed contract tests exercise inert child processes, hostile and Unicode text, pipe limits/deadlines, wrapper ownership and the UTF-8 declarations. The earlier ten-check dependency NativeAOT probe predates these clipboard changes and does not validate them. Current-source native acceptance must construct the wrapper through public `UseClipboard` registration and publish the full application with the strict IL/compiler/notice checks. Registration tests do not establish real WSL/macOS clipboard behavior; those platform runtime checks remain separate.
