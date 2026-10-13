param(
    [string]$Repository = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path,
    [string]$FixtureDirectory = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
# This verifier reads source and metadata only. It never regenerates fixtures,
# writes files, builds, restores, runs tests or changes the recorded hashes.
$utf8 = [Text.UTF8Encoding]::new($false)
$manifest = Get-Content -LiteralPath (Join-Path $FixtureDirectory 'production-extracts.json') -Raw | ConvertFrom-Json
$inputs = @()
foreach ($entry in @($manifest.fragments) + @($manifest.helpers)) {
    $actual = (Get-FileHash -LiteralPath (Join-Path $Repository $entry.source) -Algorithm SHA256).Hash
    if ($actual -ne $entry.sourceSha256) { throw "Production source differs: $($entry.source)" }
    $inputs += @{path=$entry.source;sha256=$actual}
}
foreach ($entry in $manifest.fragments) {
    $text = [IO.File]::ReadAllText((Join-Path $Repository $entry.source))
    switch ([IO.Path]::GetFileName($entry.source)) {
        'PackageListPage.cs' {
            $start=$text.IndexOf('    private DataGrid BuildGrid()', [StringComparison]::Ordinal)
            if($start -lt 0){throw 'Package BuildGrid marker missing'}
            $body=$text.IndexOf('{',$start);$depth=1;$end=$body+1
            while($depth -gt 0 -and $end -lt $text.Length){if($text[$end] -eq '{'){$depth++};if($text[$end] -eq '}'){$depth--};$end++}
            if($depth -ne 0){throw 'Package BuildGrid boundary invalid'}
        }
        'HistoryPage.cs' {
            $start=$text.IndexOf('        _grid = new DataGrid',[StringComparison]::Ordinal)
            if($start -lt 0){throw 'History grid marker missing'}
            $end=$text.IndexOf('        _grid.AddHandler',$start,[StringComparison]::Ordinal)
        }
        'OperationsPage.cs' {
            $start=$text.IndexOf('        _queue = new DataGrid',[StringComparison]::Ordinal)
            if($start -lt 0){throw 'Operations grid marker missing'}
            $end=$text.IndexOf('        _queue.SelectionChanged',$start,[StringComparison]::Ordinal)
        }
        default { throw "Unexpected extraction source: $($entry.source)" }
    }
    if($end -le $start){throw "Invalid extraction bounds: $($entry.source)"}
    $fragment=$text.Substring($start,$end-$start)
    $fragmentHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($fragment)))
    if($fragmentHash -ne $entry.fragmentSha256){throw "Production fragment differs: $($entry.source)"}
    $output=Join-Path $FixtureDirectory $entry.output
    if((Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash -ne $entry.outputSha256){throw "Fixture output differs: $($entry.output)"}
    $generated=[IO.File]::ReadAllText($output)
    if([regex]::Matches($generated,[regex]::Escape($fragment)).Count -ne 1){throw "Exact production fragment not present once in $($entry.output)"}
}
@{passed=$true;readOnly=$true;productionInputs=$inputs;fragmentCount=$manifest.fragments.Count;helperCount=$manifest.helpers.Count;runtimeRequirement='Include productionInputs and the fixture manifest hash in the JIT/NativeAOT runtime receipt.'} | ConvertTo-Json -Depth 5
