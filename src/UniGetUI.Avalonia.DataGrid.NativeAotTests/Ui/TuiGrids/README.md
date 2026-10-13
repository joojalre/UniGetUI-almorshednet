> Original isolated source handoff, retained for provenance. The fixtures are now integrated; current runtime results are recorded by the parent acceptance suite and its fresh receipts.

# TUI DataGrid fixture addon — source-only handoff

Prepared without builds or tests while the main writer owns the build slot. This is an addon to `UniGetUI.Avalonia.DataGrid.NativeAotTests`, not a copied test project or a second test runner. No integration-checkout files were changed.

## Small adoption surface

Copy these five C# files into `src/UniGetUI.Avalonia.DataGrid.NativeAotTests/Ui/TuiGrids/`:

- `ProductionPackageGrid.cs`
- `ProductionHistoryGrid.cs`
- `ProductionOperationsGrid.cs`
- `TuiGridModels.cs`
- `TuiGridProgram.cs`

Keep this README and `production-extracts.json` alongside the fixture. The test project includes .cs files recursively. Add the two real production helper links to its existing ItemGroup:

```xml
<Compile Include="../UniGetUI.Tui/Infrastructure/DataGridList.cs" Link="Models/TuiDataGridList.cs" />
<Compile Include="../UniGetUI.Tui/Infrastructure/DataGridTextBinding.cs" Link="Models/TuiDataGridTextBinding.cs" />
```

Add one `RunTuiGrids();` call to `Acceptance.Program.Run`, after `RunCatalogGrid();` and before the final PASS result. `TuiGridProgram.cs` extends the existing partial Program and reuses its Check/Flush/Click/Key methods, existing headless App/theme setup, runtime-mode check and final receipt. No new application services or test framework are added. Run the same existing `ui` JIT/NativeAOT gates after adoption, in the writer's slot.

## Production boundaries and provenance

`Extract-ProductionGrids.ps1` checks the five writer-published source hashes before writing only this addon folder. It extracts the complete PackageListPage.BuildGrid method, History's contiguous grid/column block, and Operations' contiguous queue/column block byte-for-byte as strings. `production-extracts.json` records source hashes, line locations, exact fragment hashes and generated-file hashes. The sourceRevision field is HEAD; the exact source hashes identify these currently uncommitted integration inputs. The two production helper files are linked directly rather than reimplemented.

The surrounding namespace, synthetic display records, identity translation, static palette/settings inputs and recording SetSort callback are explicitly fixture scaffolding. No real package, manager, process, operation engine, persisted settings or history store is constructed. The strings are valid inert display values, not mocks of execution behavior.

The Package SetSort callback only records field/direction and updates local toggle state. Tests prove the actual extracted Sorting handler handles header events and leaves grid SortDescriptions empty; they do not claim acceptance of production package-engine order, settings persistence or Refresh's filter logic. History/Operations use the exact extracted typed CustomSortComparer instances, but the test oracle never calls those comparers. Three distinct row identities have six different History field orderings and three different Operations field orderings. Each header has an independently specified ascending identity sequence, its reversed descending sequence, and an explicit SortDescriptions direction assertion. Wrong selector wiring cannot pass by reusing its own comparator as the oracle.

The assignment transition matches production Refresh/RebuildQueue: replace ItemsSource with a fresh real `DataGridList<T>`, including empty lists. Filtering, manager snapshots, selected operation/log execution and store-specific identity remapping are outside this fixture. Explicit fixture selection after replacement is labeled as such.

## Planned checks

- Package grid: Discover, Updates, Installed and Bundles, each with optional host/size columns disabled and enabled; exact column count and rendered cell strings.
- All three configurations: metadata from an empty real helper, static metadata identity/no factory, empty→populated→one-row replacement→empty transitions, rendered DataGridTextColumn TextBlocks, selection identity, Up/Down navigation, read-only BeginEdit rejection and no text editor after F2.
- Package header field/direction recording, handled event behavior and absence of grid-managed sort.
- History/Operations actual header clicks and typed ascending/descending comparer order; row identity preservation.

No runtime result is claimed. These checks still need compilation and JIT/NativeAOT execution. They complement the writer-owned actual TUI tests; headless Avalonia rendering does not establish terminal-host rendering, clipboard, real manager or operation behavior. This bounded addon renders two Package rows and three History/Operations rows before one-row replacement. Recycling across a large virtualized data set is not covered here; it must not be inferred from the unrelated existing harness virtualization checks.

## Read-only provenance gate

`Verify-ProductionGrids.ps1` performs no writes, generation, restore, build or execution. It checks all five full production input hashes, re-extracts each exact region in memory, checks the region hash, checks the generated fixture file hash and confirms the exact fragment occurs once inside its scaffold. It fails on drift rather than updating expected evidence. Run it before both JIT and NativeAOT acceptance. `Extract-ProductionGrids.ps1` is only an intentional maintenance operation after source review, never the routine verifier.

After copying the fixture and manifest into the main harness, integrate the read-only verifier into the existing verification script with the real repository/fixture paths. Its JSON output contains all five `productionInputs` hashes. Put this complete object and the fixture `addon-manifest.json` hash into each runtime receipt alongside its command, binary hash, output, runtime kind and exit code. A passing source verification alone is not a runtime result. Example receipt wiring (owned by the parent verification script):

```powershell
$sourceProof = & $tuiVerifier -Repository $repository -FixtureDirectory $tuiFixture | ConvertFrom-Json
$runtimeReceipt.ProductionInputs = $sourceProof.productionInputs
$runtimeReceipt.TuiFixtureManifestSha256 = (Get-FileHash (Join-Path $tuiFixture 'addon-manifest.json') -Algorithm SHA256).Hash
```

The source verifier can be rerun against the copied fixture without regenerating it. When legitimate source changes invalidate a hash, review the change and deliberately update the extract/provenance; do not make the verifier self-heal.
