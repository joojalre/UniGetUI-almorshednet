# Maintained DataGrid source

This component is based on [Avalonia.Controls.DataGrid 12.0.0](https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid/tree/3d2cf024d6f15d4e770f655246185ad417f93f51), commit `3d2cf024d6f15d4e770f655246185ad417f93f51`. The tag response, archive URL/SHA256, original file hashes and original build metadata are recorded in `provenance/`. The upstream archive itself is not required to build this repository.

`review/reflection-free-datagrid.patch` records the source changes against that exact revision. `review/final-source-manifest.json` records the maintained file hashes. Unchanged source and theme files retain their original bytes; repository formatting excludes this directory. Run `scripts/verify-datagrid-provenance.ps1` from the repository root after changing or importing this component, and review/update the patch and manifest deliberately.

The local variant retains the original DataGrid, collection engine and Fluent/Simple theme paths. It adds `IDataGridItemMetadataProvider` and `DataGridItemMetadata` so an empty collection still supplies its row type and an optional typed item factory. Application collections retain their existing identity and notifications. The application supplies no item factory, so adding rows through DataGrid remains unavailable.

Legacy reflection helpers and their call sites are excluded during C# compilation. `DATAGRID_LEGACY_REFLECTION` is rejected by both the shared MSBuild contract and a source `#error`. There is no supported runtime switch to restore those helpers. Use explicit template columns, compiled bindings, typed comparers and typed grouping. Automatic columns, reflection-based property sorting/grouping, missing item metadata and reflection-based default conversion fail explicitly. Whole-item `IComparable` sorting, including null ordering, works in both JIT and NativeAOT.

The project is adapted to portable `net10.0` and Avalonia `12.0.4`, matching the application. It explicitly overrides inherited multi-targeting, assembly metadata and ReadyToRun settings. `PackageId` and `AssemblyName` both remain `Avalonia.Controls.DataGrid`, version `12.0.0`, so a platform theme's transitive package dependency resolves to this single project producer. It is not published as a NuGet package.

The 160-byte public key is extracted from the pinned upstream `Directory.Build.props` value; its SHA256 is `34d062cd8b0bbdf600f86c7857ec9c1a776bea9be9b94665ef9b6a939de4f518`. Public-key-only delay signing preserves the assembly identity used by Avalonia's XAML compiler and friend assemblies. The modified binary is not an upstream-signed release. No private signing key is included.

Original copyright and license headers are retained. See `THIRD_PARTY_NOTICES.md`, `provenance/licence.md` and `provenance/Microsoft-Public-License.txt`. The application copies these notices into `ThirdPartyNotices/Avalonia.Controls.DataGrid/` for build and publish outputs.

Acceptance lives in `../UniGetUI.Avalonia.DataGrid.NativeAotTests/` and runs two isolated processes (`boundaries` and `ui`) against this project in JIT and NativeAOT. The fixtures use headless drawing and inert models; they do not start the installed application, contact package managers, access user databases or create shortcuts. Windows x64 acceptance does not establish runtime acceptance on the other release RIDs.
