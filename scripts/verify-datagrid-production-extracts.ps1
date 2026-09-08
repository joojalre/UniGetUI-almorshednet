param(
 [Parameter(Mandatory=$true)][string]$RepositoryRoot,
 [string]$FixtureRoot=$PSScriptRoot,
 [string]$OutputPath
)
$ErrorActionPreference='Stop'
# Read-only comparison; no regeneration or automatic acceptance of drift.
# Fixed regions preserve their exact source text apart from line endings and exterior whitespace.
function Get-Block([string]$Text,[string]$Marker){
 $start=$Text.IndexOf($Marker,[StringComparison]::Ordinal)
 if($start -lt 0 -or $Text.IndexOf($Marker,$start+1,[StringComparison]::Ordinal) -ge 0){throw "Expected unique source marker: $Marker"}
 $open=$Text.IndexOf('{',$start)
 if($open -lt 0){throw "Missing block: $Marker"}
 $depth=0
 for($i=$open;$i -lt $Text.Length;$i++){
  if($Text[$i] -eq '{'){$depth++}
  elseif($Text[$i] -eq '}'){$depth--;if($depth -eq 0){return $Text.Substring($start,$i-$start+1)}}
 }
 throw "Unbalanced block: $Marker"
}
function Get-Hash([string]$Text){[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text.Replace("`r`n","`n").Trim())))}
$specs=@(
 @{Name='package-sorter-enum';Source='src/UniGetUI.Avalonia/Models/PackageCollections.cs';Fixture='ProductionPackageSort.cs';Marker='public enum Sorter';Tail=$false},
 @{Name='package-comparison-methods';Source='src/UniGetUI.Avalonia/Models/PackageCollections.cs';Fixture='ProductionPackageSort.cs';Marker='public sealed class ObservablePackageCollection :';Tail=$true},
 @{Name='history-comparer';Source='src/UniGetUI.Avalonia/ViewModels/Pages/LogPages/OperationHistoryRowViewModel.cs';Fixture='ProductionHistorySort.cs';Marker='internal sealed class OperationHistoryRowComparer';Tail=$false},
 @{Name='numeric-version-struct';Source='src/UniGetUI.Core.Tools/Tools.cs';Fixture='ProductionVersion.cs';Marker='public struct Version :';Tail=$false}
)
$records=@(foreach($spec in $specs){
 $source=Get-Content -LiteralPath (Join-Path $RepositoryRoot $spec.Source) -Raw
 $fixture=Get-Content -LiteralPath (Join-Path $FixtureRoot $spec.Fixture) -Raw
 $source=Get-Block $source $spec.Marker
 $fixture=Get-Block $fixture $spec.Marker
 if($spec.Tail){
  $marker='private static int Compare(PackageWrapper a, PackageWrapper b, Sorter sorter)'
  $index=$source.IndexOf($marker,[StringComparison]::Ordinal);if($index -lt 0){throw 'Production comparison marker missing'};$source=$source.Substring($index)
  $index=$fixture.IndexOf($marker,[StringComparison]::Ordinal);if($index -lt 0){throw 'Fixture comparison marker missing'};$fixture=$fixture.Substring($index)
 }
 $sourceHash=Get-Hash $source;$fixtureHash=Get-Hash $fixture
 [ordered]@{Name=$spec.Name;Source=$spec.Source;Fixture=$spec.Fixture;NormalizedProductionSha256=$sourceHash;NormalizedFixtureSha256=$fixtureHash;Matches=($sourceHash -ceq $fixtureHash)}
})
$receipt=[ordered]@{VerifiedUtc=[DateTime]::UtcNow.ToString('o');Scope='Exact comparer/enum/version regions; collection metadata scaffolding excluded';Regions=$records;AllMatch=(@($records|Where-Object{-not $_.Matches}).Count -eq 0)}
$json=$receipt|ConvertTo-Json -Depth 6
if($OutputPath){$json|Set-Content -LiteralPath $OutputPath -Encoding utf8}
$json
if(-not $receipt.AllMatch){throw 'Production sorting/version source drift requires review and fixture update'}
