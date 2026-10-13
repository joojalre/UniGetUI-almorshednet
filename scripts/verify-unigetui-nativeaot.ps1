param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$PublishLog,
    [Parameter(Mandatory)][string]$NativePropertiesPath,
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'
if (Select-String -LiteralPath $PublishLog -Pattern '\bIL\d{4}\b' -Quiet) {
    throw 'NativeAOT publish surfaced an IL diagnostic; inspect the complete publish log.'
}
$executable = Join-Path $OutputDirectory 'UniGetUI.exe'
$properties = (Get-Content -LiteralPath $NativePropertiesPath -Raw | ConvertFrom-Json).Properties
if ($properties.PublishAot -ne 'true' -or $properties.NativeCompilationDuringPublish -ne 'true') {
    throw 'The evaluated publish must enable NativeAOT compilation.'
}
$projectDirectory = [IO.Path]::GetFullPath($properties.MSBuildProjectDirectory)
function Resolve-NativePath([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { throw 'NativeAOT build metadata is empty; restore with PublishAot=true first.' }
    $value = $Value.Trim('"')
    return [IO.Path]::GetFullPath($value, $projectDirectory)
}
$nativeBinary = Resolve-NativePath $properties.NativeBinary
$nativeObject = Resolve-NativePath $properties.NativeObject
$nativeIntermediate = Resolve-NativePath $properties.NativeIntermediateOutputPath
$ilcResponse = Join-Path $nativeIntermediate ($properties.TargetName + '.ilc.rsp')
$linkResponse = Join-Path $nativeIntermediate 'link.rsp'
$ilcOutputs = @(Get-Content -LiteralPath $ilcResponse | Where-Object { $_.StartsWith('-o:') })
$linkOutputs = @(Get-Content -LiteralPath $linkResponse | Where-Object { $_.StartsWith('/OUT:', [StringComparison]::OrdinalIgnoreCase) })
if ($ilcOutputs.Count -ne 1 -or $linkOutputs.Count -ne 1 -or
    (Resolve-NativePath $ilcOutputs[0].Substring(3)) -ne $nativeObject -or
    (Resolve-NativePath $linkOutputs[0].Substring(5)) -ne $nativeBinary) {
    throw 'ILC and native linker output paths do not match the evaluated build.'
}
$linkedObjects = @(Get-Content -LiteralPath $linkResponse | Where-Object { $_.Trim('"').EndsWith('.obj', [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Resolve-NativePath $_ })
if ($linkedObjects -notcontains $nativeObject) { throw 'The native linker did not consume the ILC-produced object.' }
$nativeBinaryHash = (Get-FileHash -LiteralPath $nativeBinary -Algorithm SHA256).Hash
$executableHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
if ($nativeBinaryHash -ne $executableHash) { throw 'Published UniGetUI.exe does not match the native linker output.' }
$compilerEvidence = @{
    NativeBinarySha256 = $nativeBinaryHash
    NativeObjectSha256 = (Get-FileHash -LiteralPath $nativeObject -Algorithm SHA256).Hash
    IlcResponseSha256 = (Get-FileHash -LiteralPath $ilcResponse -Algorithm SHA256).Hash
    LinkResponseSha256 = (Get-FileHash -LiteralPath $linkResponse -Algorithm SHA256).Hash
    EvaluatedPropertiesSha256 = (Get-FileHash -LiteralPath $NativePropertiesPath -Algorithm SHA256).Hash
    PublishedBinaryMatchesNativeLinkOutput = $true
}

# PE headers alone also accept a normal .NET apphost; the compiler/link binding above is required.
$stream = [IO.File]::OpenRead($executable)
$reader = [IO.BinaryReader]::new($stream)
try {
    if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'UniGetUI.exe is not a Windows PE file.' }
    $stream.Position = 0x3C
    $peOffset = $reader.ReadInt32()
    $stream.Position = $peOffset
    if ($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x8664) {
        throw 'Expected the win-x64 UniGetUI native executable.'
    }
    $stream.Position = $peOffset + 24
    if ($reader.ReadUInt16() -ne 0x020B) { throw 'Expected PE32+ format.' }
    $stream.Position = $peOffset + 24 + 112 + (14 * 8)
    if ($reader.ReadUInt32() -ne 0 -or $reader.ReadUInt32() -ne 0) {
        throw 'UniGetUI.exe has a CLR directory; this is not the expected NativeAOT artifact.'
    }
}
finally {
    $reader.Dispose()
}

$assetsPath = Join-Path $RepositoryRoot 'src/UniGetUI.Avalonia/obj/project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
$dataGrid = @($assets.libraries.Keys | Where-Object { $_ -like 'Avalonia.Controls.DataGrid/*' })
if ($dataGrid.Count -ne 1 -or $dataGrid[0] -ne 'Avalonia.Controls.DataGrid/12.0.0' -or
    $assets.libraries[$dataGrid[0]].type -ne 'project') {
    throw 'The app must resolve exactly one project-produced DataGrid 12.0.0.'
}
$maintainedDependencies = [ordered]@{
    'Consolonia.PlatformSupport' = '12.0.3.14'
    'Consolonia.GuiCS' = '12.0.3.14'
    'Vanara.PInvoke.Shared' = '4.0.4'
    'Iciclecreek.Avalonia.WindowManager' = '5.0.1'
}
$dependencyProducers = foreach ($name in $maintainedDependencies.Keys) {
    $identity = "$name/$($maintainedDependencies[$name])"
    $matches = @($assets.libraries.Keys | Where-Object { $_.Split('/')[0] -eq $name })
    if ($matches.Count -ne 1 -or $matches[0] -ne $identity -or $assets.libraries[$identity].type -ne 'project') {
        throw "Expected exactly one maintained project producer: $identity"
    }
    foreach ($targetName in $assets.targets.Keys) {
        $target = $assets.targets[$targetName]
        $matches = @($target.Keys | Where-Object { $_.Split('/')[0] -eq $name })
        if ($matches.Count -ne 1 -or $matches[0] -ne $identity -or $target[$identity].type -ne 'project') {
            throw "Maintained dependency producer mismatch in ${targetName}: $identity"
        }
        foreach ($library in $target.Values) {
            if ($library.type -ne 'package') { continue }
            foreach ($kind in @('compile', 'runtime', 'runtimeTargets')) {
                if (-not $library.ContainsKey($kind)) { continue }
                foreach ($asset in $library[$kind].Keys) {
                    if ([IO.Path]::GetFileName($asset) -eq "$name.dll") {
                        throw "Package still supplies a duplicate maintained assembly: $name ($asset)"
                    }
                }
            }
        }
    }
    $references = @(Get-Content -LiteralPath $ilcResponse | Where-Object { $_.StartsWith('-r:') } |
        ForEach-Object { Resolve-NativePath $_.Substring(3) } |
        Where-Object { [IO.Path]::GetFileName($_) -eq "$name.dll" })
    if ($references.Count -ne 1) { throw "ILC must consume exactly one maintained assembly: $name" }
    $relativeInput = [IO.Path]::GetRelativePath((Join-Path $RepositoryRoot 'src'), $references[0])
    if ($relativeInput.StartsWith('..') -or [IO.Path]::IsPathRooted($relativeInput)) {
        throw "ILC maintained assembly input must come from this repository's source build: $name"
    }
    $assembly = [Reflection.AssemblyName]::GetAssemblyName($references[0])
    $expectedVersion = [version]$maintainedDependencies[$name]
    if ($expectedVersion.Revision -lt 0) { $expectedVersion = [version]"$expectedVersion.0" }
    $tokenBytes = $assembly.GetPublicKeyToken()
    $token = if ($tokenBytes) { [Convert]::ToHexString($tokenBytes).ToLowerInvariant() } else { '' }
    $expectedToken = if ($name -eq 'Vanara.PInvoke.Shared') { 'c37e4080322237fa' } else { '' }
    if ($assembly.Name -ne $name -or $assembly.Version -ne $expectedVersion -or
        ($token -ne $expectedToken)) {
        throw "Maintained assembly identity mismatch: $name"
    }
    @{ Identity = $identity; Producer = 'project'; IlcInput = $references[0];
        AssemblyVersion = $assembly.Version.ToString(); PublicKeyToken = $token;
        IlcInputSha256 = (Get-FileHash -LiteralPath $references[0] -Algorithm SHA256).Hash }
}
if (-not $assets.libraries.ContainsKey('SQLitePCLRaw.lib.e_sqlite3/2.1.13')) {
    throw 'The app did not resolve the verified SQLite native package.'
}
$sqlite = Join-Path $OutputDirectory 'e_sqlite3.dll'
$sqliteHash = (Get-FileHash -LiteralPath $sqlite -Algorithm SHA256).Hash
if ($sqliteHash -ne 'B7385D722C83FB52142A00477A726723745916D22A555711EE89834C1111FB2E') {
    throw 'Published SQLite DLL does not match the verified win-x64 2.1.13 asset.'
}

$notices = @(
    'THIRD_PARTY_NOTICES.md', 'PROVENANCE.md', 'provenance/licence.md',
    'provenance/Microsoft-Public-License.txt', 'provenance/additional-license-source.json'
)
$noticeHashes = foreach ($relative in $notices) {
    $source = Join-Path $RepositoryRoot "src/ExternalLibraries.Avalonia.DataGrid/$relative"
    $published = Join-Path $OutputDirectory "ThirdPartyNotices/Avalonia.Controls.DataGrid/$relative"
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $publishedHash = (Get-FileHash -LiteralPath $published -Algorithm SHA256).Hash
    if ($sourceHash -ne $publishedHash) { throw "Published DataGrid notice mismatch: $relative" }
    @{ Path = $relative; Sha256 = $publishedHash }
}

$dependencyNotices = foreach ($name in $maintainedDependencies.Keys) {
    foreach ($relative in @('LICENSE', 'PROVENANCE.md')) {
        $source = Join-Path $RepositoryRoot "src/ExternalLibraries.$name/$relative"
        $publishedRelative = "ThirdPartyNotices/$name/$relative"
        $published = Join-Path $OutputDirectory $publishedRelative
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $publishedHash = (Get-FileHash -LiteralPath $published -Algorithm SHA256).Hash
        if ($sourceHash -ne $publishedHash) { throw "Published maintained dependency notice mismatch: $publishedRelative" }
        @{ Path = $publishedRelative; Sha256 = $publishedHash }
    }
}

$receiptPath = Join-Path (Split-Path $PublishLog -Parent) 'app-artifact-verification.json'
@{
    VerifiedUtc = [DateTime]::UtcNow.ToString('o')
    RuntimeIdentifier = 'win-x64'
    ExecutableSha256 = $executableHash
    NativePeWithoutClrDirectory = $true
    CompilerEvidence = $compilerEvidence
    PublishLogSha256 = (Get-FileHash -LiteralPath $PublishLog -Algorithm SHA256).Hash
    AssetsSha256 = (Get-FileHash -LiteralPath $assetsPath -Algorithm SHA256).Hash
    DataGridProducer = $dataGrid[0]
    MaintainedDependencyProducers = @($dependencyProducers)
    MaintainedDependencyNotices = @($dependencyNotices)
    SQLiteSha256 = $sqliteHash
    Notices = @($noticeHashes)
    Boundary = 'Published artifact verified; installed application and user state were not accessed.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptPath -Encoding utf8
Write-Output "Verified win-x64 NativeAOT executable, single DataGrid and four maintained dependency producers, SQLite DLL and $($notices.Count + $dependencyNotices.Count) notices."
