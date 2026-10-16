param([string]$Repository = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path)
$ErrorActionPreference = 'Stop'
$expected = @{
 'src/UniGetUI.Tui/Infrastructure/DataGridList.cs'='4B07ECA80370E91CD8D6B10E7B12CBD3B952548E82EED70A08EDA035D3F7276E'
 'src/UniGetUI.Tui/Infrastructure/DataGridTextBinding.cs'='0EC159E71A0723463BE44AA21B9614662690B27EC2D73509896118377C51CF23'
 'src/UniGetUI.Tui/Views/Pages/PackageListPage.cs'='563A3AF3D78ED5E03632E3A3E7C9898BA6AF9BE1F5B87EBE092F436C6C9356E8'
 'src/UniGetUI.Tui/Views/Pages/HistoryPage.cs'='60B8DCE941C6967E113FB51618466A0134CFC16DEF9009C70AFDD2AE2BDAA858'
 'src/UniGetUI.Tui/Views/Pages/OperationsPage.cs'='DDD91DD371905AED4C276C633D15BE1BC4188FACDB5976DA9B9099653A8A6F0D'
}
foreach($path in $expected.Keys){if((Get-FileHash -LiteralPath (Join-Path $Repository $path) -Algorithm SHA256).Hash -ne $expected[$path]){throw "Production changed: $path"}}
$utf8=[Text.UTF8Encoding]::new($false)
$header=@'
// Exact production fragment; inert wrapper/scaffold described in README.md.
using System.Linq.Expressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UniGetUI.Tui.Infrastructure;

namespace TuiGridFixture;

'@
$package=[IO.File]::ReadAllText((Join-Path $Repository 'src/UniGetUI.Tui/Views/Pages/PackageListPage.cs'))
$start=$package.IndexOf('    private DataGrid BuildGrid()', [StringComparison]::Ordinal)
if($start -lt 0){throw 'BuildGrid start absent'}
$body=$package.IndexOf('{',$start);$depth=1;$end=$body+1
while($depth -gt 0 -and $end -lt $package.Length){if($package[$end] -eq '{'){$depth++};if($package[$end] -eq '}'){$depth--};$end++}
$packageFragment=$package.Substring($start,$end-$start)
$packagePrefix=@'
internal sealed class ProductionPackageGrid
{
    private readonly PackagePageKind _kind;
    public DataGrid Grid { get; }
    public int SortField { get; private set; }
    public bool SortAscending { get; private set; } = true;
    public List<(int Field, bool Ascending)> RecordedSorts { get; } = [];
    public ProductionPackageGrid(PackagePageKind kind)
    {
        _kind = kind;
        Grid = BuildGrid();
    }
    // Recording scaffold only: deliberately does not simulate package-engine sorting.
    private void SetSort(int field, bool ascending)
    {
        RecordedSorts.Add((field, ascending));
        SortField = field;
        SortAscending = ascending;
    }

'@
$sources=@(@{Path='src/UniGetUI.Tui/Views/Pages/PackageListPage.cs';Text=$package;Start=$start;Fragment=$packageFragment;Output='ProductionPackageGrid.cs'})
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ProductionPackageGrid.cs'),$header+$packagePrefix+$packageFragment+"`n}`n",$utf8)
foreach($spec in @(@('HistoryPage.cs','        _grid = new DataGrid','        _grid.AddHandler','ProductionHistoryGrid','_grid'),@('OperationsPage.cs','        _queue = new DataGrid','        _queue.SelectionChanged','ProductionOperationsGrid','_queue'))){
 $path='src/UniGetUI.Tui/Views/Pages/'+$spec[0];$text=[IO.File]::ReadAllText((Join-Path $Repository $path));$s=$text.IndexOf($spec[1],[StringComparison]::Ordinal);$e=$text.IndexOf($spec[2],$s,[StringComparison]::Ordinal)
 if($s -lt 0 -or $e -lt $s){throw ('Extraction marker missing '+$path)}
 $fragment=$text.Substring($s,$e-$s);$output=$spec[3]+'.cs'
 $prefix='internal sealed class '+$spec[3]+"`n{`n    private readonly DataGrid "+$spec[4]+";`n    public DataGrid Grid => "+$spec[4]+";`n    public "+$spec[3]+"()`n    {`n"
 [IO.File]::WriteAllText((Join-Path $PSScriptRoot $output),$header+$prefix+$fragment+"    }`n}`n",$utf8)
 $sources+=@{Path=$path;Text=$text;Start=$s;Fragment=$fragment;Output=$output}
}
$manifest=@($sources|ForEach-Object{@{source=$_.Path;sourceSha256=$expected[$_.Path];startLine=($_.Text.Substring(0,$_.Start)-split "`n").Count;lineCount=($_.Fragment-split "`n").Count;fragmentSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($_.Fragment)));output=$_.Output;outputSha256=(Get-FileHash (Join-Path $PSScriptRoot $_.Output) -Algorithm SHA256).Hash;boundary='Exact fragment; surrounding inert wrapper is fixture-only'}})
$helpers=@($expected.Keys|Where-Object{$_ -like '*Infrastructure*'}|Sort-Object|ForEach-Object{@{source=$_;sourceSha256=$expected[$_];mode='Link real production helper in test project; no copied implementation'}})
@{sourceRevision=(git -C $Repository rev-parse HEAD);fragments=$manifest;helpers=$helpers;tested=$false}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'production-extracts.json') -Encoding utf8
