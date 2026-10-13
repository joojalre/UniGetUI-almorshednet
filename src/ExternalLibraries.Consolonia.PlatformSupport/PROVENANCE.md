# Maintained Consolonia.PlatformSupport

Upstream: https://github.com/consolonia/consolonia
Commit: 5c317423e7fe9896d9a4fab412b3fba8c7ef1e7b
Original package/assembly version: 12.0.3.14

This is a modified UniGetUI maintained source build, not an upstream release. Original copyright headers and LICENSE are retained. Exact import hashes, archive URLs/hashes, the original native-error patch, subsequent formatting/style deltas and clipboard review corrections are recorded in [the maintained dependency provenance](../../docs/third-party/tui-nativeaot/README.md). No private signing material is included.

The clipboard follow-up uses a constant PowerShell script with base64 standard input for WSL, a bounded dedicated process path, typed Avalonia wrapper construction and explicit macOS UTF-8 marshalling. Its exact five-file patch and hashes are in `review-clipboard.patch` and `review-clipboard-delta.json` at the linked provenance directory. Original XClip/Bash process behavior is retained.

The isolated ten-check JIT/NativeAOT dependency probe predates the clipboard follow-up. Current managed clipboard contracts and a fresh published native registration probe have distinct scopes; full application publication and real platform clipboard behavior are separate acceptance gates. See the linked provenance and integration summary for the validation boundaries.
