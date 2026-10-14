# Upstream integration verification

The integration preserves fork fixes while incorporating upstream changes. It repairs queue priority insertion around manager-gate waiters so running operations stay within the global limit; adds typed item metadata and explicit read-only string bindings for new Catalog/TUI grids; and uses four pinned maintained dependency projects to resolve the TUI NativeAOT reflection paths.

Local verification is separate from remote CI, merging, release publication and installed application acceptance. No claim about another architecture follows from win-x64 results.

## Earlier integration verification

The following results describe the integration before the later PR review corrections. They remain historical evidence; modified application code requires fresh publication and exact-head CI. The eight-grid evidence remains reusable only while its 111 recorded inputs match.

| Gate | Earlier local result |
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

## PR review corrections

Scheduled TUI updates now await terminal operation cleanup, suppress duplicate loader launches and retain ownership while timed-out work is still running. Late cancellation retains the attempted schedule timestamp so the same occurrence is not immediately relaunched. Redirected terminal input/output is rejected on every platform, and exported scripts select the host shell.

Option dialogs use strict persistence paths for installation options, automatic updates, ignored versions and force-kill settings. A failed write or uncached malformed read keeps approval pending and leaves target options unchanged. Each successful write publishes its cache afterward; the multi-file save is not an atomic transaction, so a later failure can leave earlier writes persisted for a recoverable retry. Clipboard corrections and source provenance are documented separately.

Local focused validation covers 61 distinct review regression cases: 58 passed in the first runtime run, and the three operation cases passed after correcting their inert fixture metadata. This is an explicit aggregate, not one full-suite invocation. Installation-option factory tests passed 32 cases on each of the two target frameworks (64 total). Frozen input manifests and the original failures are retained with the local evidence. The workflow now preserves TRX results and readable failure diagnostics on every run. Both final CI-equivalent whitespace and code-style checks passed with all 1,525 recorded inputs unchanged.

At that review checkpoint, current-head full CI, fresh full-application NativeAOT publication, native clipboard registration, final-output SQLite and published fake-data terminal acceptance were separate gates before merge; their later results remain bound to the accepted prior commit. Other runtime identifiers, real macOS/WSL clipboard services and installed desktop UI are not established by the Windows sandbox tests.

## Fixed upstream follow-up

This follow-up integrates upstream `95695108b227a31c4f618483a03b37049c856674` from accepted fork main `82ae2815f6c6a925397eabd7cb251ada7d3a28b9`. Its five upstream commits add macOS glass styling/native menus, preserve package-list scroll position during procedural updates, avoid restart prompts while loading unchanged text settings, preserve escaped GitHub login redirect parameters and improve Swedish translations.

A regression exposed mutable download-size/checkbox keys invalidating the new binary insertion order. Existing rows are now reconciled with stable moves before insertion. The maintained DataGrid processes plain single-item moves through its view and row slots, preserving selection/current identity; transformed and range moves deliberately use the existing refresh fallback. The independently reviewed library delta is recorded separately from the original reflection-free patch, with unchanged original bytes preserved.

Local Release JIT regression passed 1,102 assertions, including actual displayed ordering, 400-row scroll/selection continuity, moves across the viewport in both directions and equal-value/duplicate-occurrence currency. This managed run does not establish NativeAOT or remote CI acceptance. A separate Debug run exposed an existing focused-row/source-replacement assertion and reproduced it with the new incremental prelude omitted; the failure remains recorded, not suppressed. The maintained acceptance runner uses Release JIT and NativeAOT.

Fresh exact-head CI, the two new upstream test classes, formatting/translation verification, full eight-grid NativeAOT parity, application NativeAOT, final-output SQLite and published fake-data terminal acceptance are recorded as separate gates for this follow-up. Earlier immutable artifacts are not relabeled for the new source. macOS native UI/ABI and other unexecuted runtime identifiers remain unverified by Windows acceptance.
