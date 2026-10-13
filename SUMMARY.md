# Upstream integration verification

The integration preserves fork fixes while incorporating upstream changes. It repairs queue priority insertion around manager-gate waiters so running operations stay within the global limit; adds typed item metadata and explicit read-only string bindings for new Catalog/TUI grids; and uses four pinned maintained dependency projects to resolve the TUI NativeAOT reflection paths.

Local verification is separate from remote CI, merging, release publication and installed application acceptance. No claim about another architecture follows from win-x64 results.

| Gate | Local result |
| --- | --- |
| Queue priority regression | Both insertion paths reproduced the cap violation before the fix; focused operation tests passed after it |
| Actual TUI tests | 232 passed, zero failed; one diagnostic `DumpPages` helper was not executed. These recorded binaries precede the separately reviewed dependency style corrections; final native artifacts are checked afterward |
| Repository whitespace and style | Passed full CI-equivalent checks after reviewed corrections |
| Translation checks | Passed all 60 language files and boundary-order verification |
| Eight DataGrid configurations | Passed: JIT/NativeAOT parity for 38 boundary checks and 1,068 UI checks per mode, with zero IL diagnostics |
| Full application NativeAOT | Passed win-x64 publish with IL warnings treated as errors; verified native compiler/output binding, the five maintained producers and all 13 notices |
| Final published SQLite | Passed against the actual final-output DLL, loaded version 3.53.3: Arabic write/read and transaction rollback in an isolated in-memory database |
| Remote exact-head CI, installed desktop UI and other RIDs | Not established by these local checks |

The DataGrid suite contains five desktop configurations and three TUI configurations. The Catalog fixture includes real sorting callbacks and parent command binding with inert actions; TUI package fixtures cover four page variants with optional columns off/on. Actual TUI tests complement headless tests with real fake-data page refresh, selected identities and package-engine sorting. Terminal-host/published-application behavior is a separate runtime gate.

Read [DataGrid acceptance and its limits](src/UniGetUI.Avalonia.DataGrid.NativeAotTests/README.md) and [maintained dependency provenance](docs/third-party/tui-nativeaot/README.md). Whitespace/encoding changes have a token comparison; token-changing style corrections have a separate reviewed patch. Original failed/partial test receipts remain historical evidence and are not relabeled as current success.
