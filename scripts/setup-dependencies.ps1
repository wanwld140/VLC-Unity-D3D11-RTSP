[CmdletBinding()]
param(
    [string]$PackagePath,
    [switch]$IncludeOptionalGplPlugins,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$packageVersion = '4.0.0-alpha-20260831'
$packageFileName = "videolan.libvlc.windows.$packageVersion.nupkg"
$packageUri = "https://f.feedz.io/videolan/preview/nuget/v3/packages/videolan.libvlc.windows/$packageVersion/$packageFileName"
$expectedSha256 = '6982B57F7703368062002EDE57854F4C076C5D705522139FC4380E0FFA981697'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$cacheRoot = Join-Path $repoRoot '.cache\dependencies'
$cachedPackage = Join-Path $cacheRoot $packageFileName
$extractRoot = Join-Path $cacheRoot "libvlc-$packageVersion"
$destination = Join-Path $repoRoot 'External\VLCUnityWindows\Plugins'

function Assert-PathInsideRepository([string]$PathToCheck) {
    $resolved = [IO.Path]::GetFullPath($PathToCheck)
    $prefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the repository: $resolved"
    }
}

Assert-PathInsideRepository $cacheRoot
Assert-PathInsideRepository $extractRoot
Assert-PathInsideRepository $destination
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null

if ($PackagePath) {
    $sourcePackage = [IO.Path]::GetFullPath($PackagePath)
    if (-not (Test-Path -LiteralPath $sourcePackage -PathType Leaf)) {
        throw "PackagePath does not exist: $sourcePackage"
    }
    Copy-Item -LiteralPath $sourcePackage -Destination $cachedPackage -Force
}
elseif (-not (Test-Path -LiteralPath $cachedPackage -PathType Leaf) -or $Force) {
    Write-Host "Downloading pinned LibVLC $packageVersion..."
    Invoke-WebRequest -Uri $packageUri -OutFile $cachedPackage -UseBasicParsing
}

$actualHash = (Get-FileHash -LiteralPath $cachedPackage -Algorithm SHA256).Hash.ToUpperInvariant()
if ($actualHash -ne $expectedSha256) {
    throw "LibVLC package SHA-256 mismatch. Expected $expectedSha256, got $actualHash."
}

if ((Test-Path -LiteralPath $extractRoot) -and $Force) {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force
}
if (-not (Test-Path -LiteralPath $extractRoot)) {
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($cachedPackage, $extractRoot)
}

$packageRuntime = Join-Path $extractRoot 'build\x64'
if (-not (Test-Path -LiteralPath (Join-Path $packageRuntime 'libvlc.dll'))) {
    throw 'Pinned package did not contain build/x64/libvlc.dll.'
}

if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -Path (Join-Path $packageRuntime '*') -Destination $destination -Recurse -Force

if (-not $IncludeOptionalGplPlugins) {
    $denyListPath = Join-Path $PSScriptRoot 'gpl-plugin-denylist.txt'
    foreach ($relativePath in Get-Content -LiteralPath $denyListPath) {
        $entry = $relativePath.Trim()
        if (-not $entry -or $entry.StartsWith('#')) { continue }
        $target = Join-Path $destination ($entry -replace '/', [IO.Path]::DirectorySeparatorChar)
        Assert-PathInsideRepository $target
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            Remove-Item -LiteralPath $target -Force
        }
    }
}

$manifest = [ordered]@{
    package = 'VideoLAN.LibVLC.Windows'
    version = $packageVersion
    source = $packageUri
    sha256 = $expectedSha256
    optionalGplPluginsIncluded = [bool]$IncludeOptionalGplPlugins
    installedUtc = [DateTime]::UtcNow.ToString('o')
}
$manifestPath = Join-Path (Split-Path -Parent $destination) 'DEPENDENCY_MANIFEST.json'
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "LibVLC runtime ready: $destination"
Write-Host "Package SHA-256: $actualHash"
