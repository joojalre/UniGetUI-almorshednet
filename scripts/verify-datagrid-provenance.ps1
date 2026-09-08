param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))

$ErrorActionPreference = 'Stop'
$componentRoot = Join-Path $RepositoryRoot 'src/ExternalLibraries.Avalonia.DataGrid'
$componentRoot = [IO.Path]::GetFullPath($componentRoot)
$manifestPath = Join-Path $componentRoot 'review/final-source-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.UpstreamCommit -ne '3d2cf024d6f15d4e770f655246185ad417f93f51') {
    throw 'Unexpected DataGrid upstream revision.'
}

$expectedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $manifest.Files) {
    $path = [IO.Path]::GetFullPath((Join-Path $componentRoot $file.Path))
    if (-not $path.StartsWith($componentRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Manifest path escapes the component: $($file.Path)"
    }
    if (-not $expectedPaths.Add($file.Path)) { throw "Duplicate manifest path: $($file.Path)" }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing DataGrid file: $($file.Path)" }
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actualHash -ne $file.Sha256) { throw "DataGrid provenance mismatch: $($file.Path)" }
}

# Only source-controlled files are enumerated; build output is intentionally outside the manifest.
$trackedFiles = & git -C $RepositoryRoot ls-files --cached --others --exclude-standard -- 'src/ExternalLibraries.Avalonia.DataGrid'
if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate DataGrid source files.' }
foreach ($trackedPath in $trackedFiles) {
    $relativePath = $trackedPath.Substring('src/ExternalLibraries.Avalonia.DataGrid/'.Length)
    if ($relativePath -ne 'review/final-source-manifest.json' -and -not $expectedPaths.Contains($relativePath)) {
        throw "DataGrid file is missing from the manifest: $relativePath"
    }
}

$keyPath = Join-Path $componentRoot 'provenance/avalonia.public.snk'
if ((Get-Item -LiteralPath $keyPath).Length -ne 160 -or
    (Get-FileHash -LiteralPath $keyPath -Algorithm SHA256).Hash -ne '34D062CD8B0BBDF600F86C7857EC9C1A776BEA9BE9B94665EF9B6A939DE4F518') {
    throw 'The DataGrid key must be the pinned public-only upstream key.'
}
Write-Output "Verified $($manifest.Files.Count) maintained DataGrid files and the pinned public key."
