> Original isolated source handoff, retained for provenance. The fixtures are now integrated; current runtime results are recorded by the parent acceptance suite and its fresh receipts.

# TUI grid addon checkpoint

Verified: five stable production input hashes; three exact configuration extracts; linked-helper adoption instructions; inert fixtures and bounded test design; five C# addon files only.

Pending: sole-writer adoption, compilation, JIT runtime and NativeAOT runtime. No build/test or integration-checkout write performed for this addon.

Coverage: four Package variants with optional columns, History and Operations; replacement metadata, displayed strings, header sorting, selection/navigation and read-only behavior. History/Operations use independent expected row-identity sequences with three rows and different per-field permutations, plus sort-direction assertions. Package sort callback is recording-only. Large-data virtualization is outside this addon. Existing harness and actual TUI suite remain writer-owned.

Evidence: production-extracts.json, addon-manifest.json and README.md. Verify-ProductionGrids.ps1 is a read-only gate emitting all five production input hashes for inclusion in each runtime receipt. It does not regenerate extracts. The earlier dependency NativeAOT acceptance in the parent candidate folder is separate evidence and does not prove this new addon.
