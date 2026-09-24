#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Jit', 'Native', 'All')][string]$Phase = 'All',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputDirectory,
    [string]$JitEvidenceDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath($RepositoryRoot)
$project = Join-Path $repo 'src/UniGetUI.Avalonia.DataGrid.NativeAotTests/UniGetUI.Avalonia.DataGrid.NativeAotTests.csproj'
$guard = Join-Path $repo 'src/DataGrid.AotContract.targets'
$assemblyName = 'UniGetUI.Avalonia.DataGrid.NativeAotTests'
if (!(Test-Path -LiteralPath $project -PathType Leaf) -or !(Test-Path -LiteralPath $guard -PathType Leaf)) { throw 'Acceptance project or shared DataGrid.AotContract.targets is missing.' }
if ($RuntimeIdentifier -notmatch '^(win|linux|linux-musl|osx)-[a-z0-9]+$') { throw 'Use a concrete supported host RID.' }
$platform = $RuntimeIdentifier.Split('-')[-1]
if ($platform -notin @('x64', 'arm64')) { throw 'The repository supports x64 or arm64 platforms.' }
if (($IsWindows -and !$RuntimeIdentifier.StartsWith('win-')) -or ($IsMacOS -and !$RuntimeIdentifier.StartsWith('osx-')) -or ($IsLinux -and !$RuntimeIdentifier.StartsWith('linux-'))) { throw 'This script executes the result: the RID must match the current host OS.' }
if ($Phase -eq 'Native' -and !$JitEvidenceDirectory) { throw 'Native requires -JitEvidenceDirectory from a successful Jit run for parity comparison.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo ('artifacts/datagrid-aot/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Output directory already exists; refusing overwrite: $output" }
[IO.Directory]::CreateDirectory($output) | Out-Null
$logDirectory = Join-Path $output 'logs'
[IO.Directory]::CreateDirectory($logDirectory) | Out-Null
$commands = [Collections.Generic.List[object]]::new()
$runs = [ordered]@{}
$artifacts = [ordered]@{}
$inputs = @()
$compared = $false
$failure = $null
$common = @('/m:1', '/nodeReuse:false', '-p:UseSharedCompilation=false', ('-p:Platform=' + $platform), '-p:SelfContained=true', '-nologo')
$strict = @('-p:ILLinkTreatWarningsAsErrors=true', '-p:IlcTreatWarningsAsErrors=true', '-p:TrimmerSingleWarn=false', '-p:SuppressTrimAnalysisWarnings=false', '-p:SuppressAotAnalysisWarnings=false')

function Get-Sha([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Invoke-Logged([string]$Name, [string]$File, [string[]]$Arguments, [string]$ExpectedRejection = '') {
    $log = Join-Path $logDirectory ($Name + '.log')
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.WorkingDirectory = $repo
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($credentialName in @('UNIGETUI_GITHUB_CLIENT_ID', 'UNIGETUI_GITHUB_CLIENT_SECRET', 'UNIGETUI_OPENSEARCH_USERNAME', 'UNIGETUI_OPENSEARCH_PASSWORD', 'GH_TOKEN', 'GITHUB_TOKEN')) { [void]$start.Environment.Remove($credentialName) }
    $start.Environment['MSBUILDDISABLENODEREUSE'] = '1'
    $start.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $started = [DateTime]::UtcNow.ToString('o')
    $code = -1
    $text = ''
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $text = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        $code = $process.ExitCode
    } catch {
        $text += $_.Exception.Message
        throw
    } finally {
        [IO.File]::WriteAllText($log, $text)
        $record = [ordered]@{ Name = $Name; File = $File; Arguments = $Arguments; StartedUtc = $started; FinishedUtc = [DateTime]::UtcNow.ToString('o'); ExitCode = $code; Log = ('logs/' + $Name + '.log'); LogSha256 = (Get-Sha $log); ExpectedRejection = $ExpectedRejection; CredentialVariablesRemoved = 6 }
        $commands.Add($record)
        $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $logDirectory ($Name + '.json')) -Encoding utf8
        $process.Dispose()
    }
    if ($text -match '\bIL\d{4}\b') { throw "$Name surfaced an IL diagnostic; see $log" }
    if ($ExpectedRejection) {
        if ($code -eq 0 -or $text -notmatch [regex]::Escape($ExpectedRejection)) { throw "$Name did not fail through the expected shared guard; see $log" }
    } elseif ($code -ne 0) { throw "$Name exited $code; see $log" }
    Write-Host "$Name : expected outcome verified (exit $code)"
    return $log
}

function Get-Inputs {
    $pathComparer = if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
    $paths = [Collections.Generic.HashSet[string]]::new($pathComparer)
    $queue = [Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($project)
    [void]$paths.Add($guard)
    [void]$paths.Add($PSCommandPath)
    foreach ($name in @('global.json', 'NuGet.Config', 'src/Directory.Build.props', 'src/Directory.Build.targets', 'src/Directory.Packages.props', 'src/.editorconfig')) {
        $path = Join-Path $repo $name
        if (Test-Path -LiteralPath $path -PathType Leaf) { [void]$paths.Add($path) }
    }
    $visited = [Collections.Generic.HashSet[string]]::new($pathComparer)
    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if (!$visited.Add($current)) { continue }
        $directory = Split-Path -Parent $current
        foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File) {
            if ($file.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $file.Extension -in @('.cs', '.axaml', '.xaml', '.csproj', '.props', '.targets', '.snk')) {
                if ($file.Extension -eq '.snk' -and $file.Name -notin @('publicsigning.snk', 'avalonia.public.snk')) { throw 'Only the pinned public signing key may be hashed as an input.' }
                [void]$paths.Add($file.FullName)
            }
        }
        [xml]$xml = Get-Content -LiteralPath $current -Raw
        foreach ($item in $xml.SelectNodes("//*[local-name()='Compile' or local-name()='ProjectReference'][@Include]")) {
            $include = $item.GetAttribute('Include').Replace('\', [IO.Path]::DirectorySeparatorChar)
            if ($include -match '[$*?]') { throw "Input hashing requires explicit Compile/ProjectReference paths: $include" }
            $resolved = [IO.Path]::GetFullPath((Join-Path $directory $include))
            if (![IO.Path]::GetRelativePath($repo, $resolved).StartsWith('..') -and (Test-Path -LiteralPath $resolved -PathType Leaf)) {
                [void]$paths.Add($resolved)
                if ($item.LocalName -eq 'ProjectReference') { $queue.Enqueue($resolved) }
            } else { throw "Input must resolve inside the repository: $include" }
        }
    }
    @($paths | Sort-Object | ForEach-Object { [ordered]@{ Path = [IO.Path]::GetRelativePath($repo, $_).Replace('\', '/'); Sha256 = Get-Sha $_ } })
}

function Read-Run([string]$Path, [string]$Scenario, [bool]$Native) {
    $text = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n").TrimEnd([char[]]"`r`n")
    if ($text -match '\bIL\d{4}\b') { throw "IL diagnostic in runtime log $Path" }
    $expected = if ($Scenario -eq 'boundaries') { 38 } else { 316 }
    $pattern = if ($Scenario -eq 'boundaries') { '(?m)^CHECK\tPASS\t[^\t\r\n]+$' } else { '(?m)^CHECK\tPASS\t[^\t\r\n]+\t[^\t\r\n]+$' }
    $checks = @([regex]::Matches($text, $pattern) | ForEach-Object Value)
    $allChecks = @([regex]::Matches($text, '(?m)^CHECK\t[^\r\n]*$'))
    $summary = @([regex]::Matches($text, '(?m)^RESULT\t[^\r\n]*$') | ForEach-Object Value)
    $expectedSummary = if ($Scenario -eq 'boundaries') { "RESULT`tPASS`tchecks=38`tbackend=headless-fake-drawing`tinput=programmatic-control-properties" } else { "RESULT`tPASS`tchecks=316`tbackend=headless`tproduction-logic=exact-extracts" }
    if ($checks.Count -ne $expected -or $allChecks.Count -ne $expected -or $summary.Count -ne 1 -or $summary[0] -cne $expectedSummary) { throw "Invalid $Scenario checks or summary in $Path" }
    if (@($checks | Where-Object { $_ -match '\truntime-mode$' }).Count -ne 1) { throw "Missing unique runtime-mode assertion in $Path" }
    if ($Scenario -eq 'boundaries') {
        $flag = if ($Native) { 'False' } else { 'True' }
        $mode = "RUNTIME`tIsDynamicCodeSupported=$flag`tIsDynamicCodeCompiled=$flag"
        if (@([regex]::Matches($text, '(?m)^RUNTIME\t[^\r\n]*$') | ForEach-Object Value).Count -ne 1 -or !($text.Split("`n") -ccontains $mode)) { throw "Incorrect native/JIT runtime flags in $Path" }
        $normalized = $text.Replace($mode, "RUNTIME`tIsDynamicCodeSupported=<mode>`tIsDynamicCodeCompiled=<mode>")
    } else {
        $flag = if ($Native) { 'True' } else { 'False' }
        $mode = "MODE`tAOT=$flag`tPatched=True"
        if (@([regex]::Matches($text, '(?m)^MODE\t[^\r\n]*$') | ForEach-Object Value).Count -ne 1 -or !($text.Split("`n") -ccontains $mode)) { throw "Incorrect patched/native UI mode in $Path" }
        $normalized = $text.Replace($mode, "MODE`tAOT=<mode>`tPatched=True")
    }
    [ordered]@{ Checks = $expected; Summary = $summary[0]; NormalizedText = $normalized; LogSha256 = (Get-Sha $Path) }
}

function Get-Binary([string]$Path, [bool]$Native) {
    $result = [ordered]@{ Path = [IO.Path]::GetRelativePath($output, $Path).Replace('\', '/'); Sha256 = Get-Sha $Path; Bytes = (Get-Item -LiteralPath $Path).Length; WindowsX64NativePeVerified = $false }
    if ($Native -and $IsWindows -and $RuntimeIdentifier -eq 'win-x64') {
        $stream = [IO.File]::OpenRead($Path)
        $reader = [IO.BinaryReader]::new($stream)
        try {
            $mz = $reader.ReadUInt16(); $stream.Position = 0x3c; $pe = $reader.ReadUInt32(); $stream.Position = $pe
            $signature = $reader.ReadUInt32(); $machine = $reader.ReadUInt16(); $stream.Position = $pe + 24
            $magic = $reader.ReadUInt16(); $stream.Position = $pe + 24 + 112 + 14 * 8; $clr = $reader.ReadUInt32()
            if ($mz -ne 0x5a4d -or $signature -ne 0x4550 -or $machine -ne 0x8664 -or $magic -ne 0x20b -or $clr -ne 0) { throw 'Published win-x64 executable failed native PE verification.' }
            $result.WindowsX64NativePeVerified = $true
        } finally { $reader.Dispose(); $stream.Dispose() }
    }
    $result
}

try {
    $inputs = @(Get-Inputs)
    $guardProject = Join-Path $output 'guard-probe.proj'
    $escapedGuard = [Security.SecurityElement]::Escape($guard)
    [IO.File]::WriteAllText($guardProject, "<Project><Import Project=`"$escapedGuard`"/><Target Name=`"Probe`" DependsOnTargets=`"ValidateDataGridAotContract`"><Message Importance=`"high`" Text=`"DATAGRID_GUARD_PROBE_PASSED`"/></Target></Project>")
    [void](Invoke-Logged 'guard-default' 'dotnet' (@('msbuild', $guardProject, '-target:Probe') + $common))
    $guardCases = @(
        @('legacy', 'DefineConstants=DEBUG%3BDATAGRID_LEGACY_REFLECTION%3BTRACE', 'DATAGRID_LEGACY_REFLECTION is not supported'),
        @('nowarn', 'NoWarn=IL2026', 'Trim and NativeAOT diagnostics must not be suppressed'),
        @('downgrade', 'WarningsNotAsErrors=IL3050', 'Trim and NativeAOT diagnostics must not be suppressed'),
        @('linker', 'ILLinkTreatWarningsAsErrors=false', 'requires unsuppressed trim and NativeAOT'),
        @('compiler', 'IlcTreatWarningsAsErrors=false', 'requires unsuppressed trim and NativeAOT'),
        @('trim-suppression', 'SuppressTrimAnalysisWarnings=true', 'requires unsuppressed trim and NativeAOT'),
        @('aot-suppression', 'SuppressAotAnalysisWarnings=true', 'requires unsuppressed trim and NativeAOT'),
        @('single-warning', 'TrimmerSingleWarn=true', 'requires unsuppressed trim and NativeAOT')
    )
    foreach ($case in $guardCases) { [void](Invoke-Logged ('guard-' + $case[0]) 'dotnet' (@('msbuild', $guardProject, '-target:Probe', ('-p:' + $case[1])) + $common) $case[2]) }
    if ($Phase -in @('Jit', 'All')) {
        $jitOutput = Join-Path $output 'jit'
        [void](Invoke-Logged 'jit-restore' 'dotnet' (@('restore', $project, '-r', $RuntimeIdentifier, '-p:Configuration=Release', '-p:PublishAot=false') + $common + $strict))
        [void](Invoke-Logged 'jit-build' 'dotnet' (@('build', $project, '-c', 'Release', '-f', 'net10.0', '-r', $RuntimeIdentifier, '--no-restore', '-p:PublishAot=false', '-p:PublishReadyToRun=false', '-o', $jitOutput) + $common + $strict))
        $jitDll = Join-Path $jitOutput ($assemblyName + '.dll')
        $artifacts.JitHarness = Get-Binary $jitDll $false
        $artifacts.JitDataGrid = Get-Binary (Join-Path $jitOutput 'Avalonia.Controls.DataGrid.dll') $false
        foreach ($scenario in @('boundaries', 'ui')) {
            $log = Invoke-Logged ('jit-' + $scenario) 'dotnet' @($jitDll, $scenario)
            $runs['jit-' + $scenario] = Read-Run $log $scenario $false
        }
    } else {
        $priorDirectory = [IO.Path]::GetFullPath($JitEvidenceDirectory)
        $prior = Get-Content -LiteralPath (Join-Path $priorDirectory 'summary.json') -Raw | ConvertFrom-Json -AsHashtable
        if ($prior.Status -ne 'Passed' -or $prior.RuntimeIdentifier -ne $RuntimeIdentifier) { throw 'Prior JIT receipt is unsuccessful or uses a different RID.' }
        if (($prior.Inputs | ConvertTo-Json -Depth 6 -Compress) -cne ($inputs | ConvertTo-Json -Depth 6 -Compress)) { throw 'Prior JIT inputs differ from current inputs.' }
        foreach ($scenario in @('boundaries', 'ui')) {
            $run = Read-Run (Join-Path $priorDirectory ('logs/jit-' + $scenario + '.log')) $scenario $false
            if ($run.LogSha256 -cne $prior.Runs['jit-' + $scenario].LogSha256) { throw 'Prior JIT log hash mismatch.' }
            $runs['jit-' + $scenario] = $run
        }
    }
    if ($Phase -in @('Native', 'All')) {
        $nativeOutput = Join-Path $output 'native'
        [void](Invoke-Logged 'native-restore' 'dotnet' (@('restore', $project, '-r', $RuntimeIdentifier, '-p:Configuration=Release', '-p:PublishAot=true', '-p:PublishTrimmed=true', '-p:TrimMode=full') + $common + $strict))
        [void](Invoke-Logged 'native-publish' 'dotnet' (@('publish', $project, '-c', 'Release', '-f', 'net10.0', '-r', $RuntimeIdentifier, '--self-contained', 'true', '--no-restore', '-p:PublishAot=true', '-p:PublishTrimmed=true', '-p:TrimMode=full', '-o', $nativeOutput) + $common + $strict))
        $suffix = if ($IsWindows) { '.exe' } else { '' }
        $nativeExe = Join-Path $nativeOutput ($assemblyName + $suffix)
        $artifacts.NativeHarness = Get-Binary $nativeExe $true
        $targetLog = Invoke-Logged 'native-input-path' 'dotnet' (@('msbuild', $project, '-getProperty:TargetPath', '-p:Configuration=Release', '-p:TargetFramework=net10.0', ('-p:RuntimeIdentifier=' + $RuntimeIdentifier), '-p:PublishAot=true') + $common + $strict)
        $targetPath = [IO.File]::ReadAllText($targetLog).Trim()
        if (![IO.Path]::IsPathFullyQualified($targetPath) -or [IO.Path]::GetRelativePath($repo, $targetPath).StartsWith('..') -or !(Test-Path -LiteralPath $targetPath -PathType Leaf)) { throw 'Managed NativeAOT input path did not resolve inside the repository.' }
        $inputDirectory = Join-Path $output 'native-input'
        [IO.Directory]::CreateDirectory($inputDirectory) | Out-Null
        foreach ($inputPath in @($targetPath, (Join-Path (Split-Path -Parent $targetPath) 'Avalonia.Controls.DataGrid.dll'))) {
            $copy = Join-Path $inputDirectory ([IO.Path]::GetFileName($inputPath))
            Copy-Item -LiteralPath $inputPath -Destination $copy -ErrorAction Stop
            if ((Get-Sha $copy) -cne (Get-Sha $inputPath)) { throw 'Managed NativeAOT input copy hash mismatch.' }
            $artifacts['NativeInput-' + [IO.Path]::GetFileName($inputPath)] = Get-Binary $copy $false
        }
        foreach ($scenario in @('boundaries', 'ui')) {
            $log = Invoke-Logged ('native-' + $scenario) $nativeExe @($scenario, '--expect-aot')
            $runs['native-' + $scenario] = Read-Run $log $scenario $true
            if ($runs['jit-' + $scenario].NormalizedText -cne $runs['native-' + $scenario].NormalizedText) { throw "$scenario JIT/native output differs beyond runtime-mode values." }
        }
        $compared = $true
    }
    if (($inputs | ConvertTo-Json -Depth 6 -Compress) -cne (@(Get-Inputs) | ConvertTo-Json -Depth 6 -Compress)) { throw 'Source inputs changed during acceptance.' }
} catch {
    $failure = $_.Exception.Message
    throw
} finally {
    $compactRuns = [ordered]@{}
    foreach ($name in $runs.Keys) { $compactRuns[$name] = [ordered]@{ Checks = $runs[$name].Checks; Summary = $runs[$name].Summary; LogSha256 = $runs[$name].LogSha256 } }
    [ordered]@{ Status = $(if ($failure) { 'Failed' } else { 'Passed' }); Phase = $Phase; RuntimeIdentifier = $RuntimeIdentifier; FinishedUtc = [DateTime]::UtcNow.ToString('o'); Failure = $failure; JitNativeCompared = $compared; Inputs = $inputs; Runs = $compactRuns; Artifacts = $artifacts; Commands = $commands; Cleanup = 'None' } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding utf8
    Write-Host "Acceptance receipt: $(Join-Path $output 'summary.json')"
}
