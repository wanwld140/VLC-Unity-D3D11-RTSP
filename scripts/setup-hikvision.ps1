[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$SdkPath = $env:HIKVISION_SDK_PATH,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$sdkVersion = 'V6.1.9.48_build20230410_win64'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$destination = Join-Path $repoRoot 'External\HikvisionWindows'
$requiredFiles = @(
    'HCNetSDK.dll',
    'HCCore.dll',
    'PlayCtrl.dll',
    'SuperRender.dll',
    'AudioRender.dll',
    'MP_Render.dll',
    'YUVProcess.dll',
    'libcrypto-1_1-x64.dll',
    'libssl-1_1-x64.dll',
    'hlog.dll',
    'hpr.dll',
    'zlib1.dll'
)

function Assert-PathInsideRepository([string]$PathToCheck) {
    $resolved = [IO.Path]::GetFullPath($PathToCheck)
    $prefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the repository: $resolved"
    }
}

function Get-PeMachine([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) { return 0 }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { return 0 }
        return $reader.ReadUInt16()
    }
    finally {
        $stream.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($SdkPath)) {
    throw 'Pass -SdkPath <CH-HCNetSDK...win64> or set HIKVISION_SDK_PATH.'
}

$sdkRoot = [IO.Path]::GetFullPath($SdkPath)
if (-not (Test-Path -LiteralPath $sdkRoot -PathType Container)) {
    throw "Hikvision SDK directory does not exist: $sdkRoot"
}

function Test-LibraryRoot([string]$Candidate) {
    return (Test-Path -LiteralPath (Join-Path $Candidate 'HCNetSDK.dll') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $Candidate 'PlayCtrl.dll') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $Candidate 'HCNetSDKCom') -PathType Container)
}

if (Test-LibraryRoot $sdkRoot) {
    $libraryRoot = $sdkRoot
} else {
    $libraryRoot = Get-ChildItem -LiteralPath $sdkRoot -Directory -Recurse |
        Where-Object { Test-LibraryRoot $_.FullName } |
        Sort-Object { $_.FullName.Length } |
        Select-Object -First 1 -ExpandProperty FullName
    if ([string]::IsNullOrWhiteSpace($libraryRoot)) {
        throw 'SdkPath must point to the official Win64 SDK root or runtime directory.'
    }
}
$libraryRoot = [IO.Path]::GetFullPath($libraryRoot)

foreach ($file in $requiredFiles) {
    $source = Join-Path $libraryRoot $file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Official SDK is missing required runtime file: $file"
    }
    if ((Get-PeMachine $source) -ne 0x8664) {
        throw "Expected an x64 PE DLL but found another architecture: $file"
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $libraryRoot 'HCNetSDKCom') -PathType Container)) {
    throw 'Official SDK is missing the required HCNetSDKCom directory.'
}

Assert-PathInsideRepository $destination
if (Test-Path -LiteralPath $destination) {
    if (-not $Force) {
        throw 'External/HikvisionWindows already exists. Pass -Force to replace it.'
    }
    Remove-Item -LiteralPath $destination -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $destination | Out-Null

# Official demos keep root DLLs and HCNetSDKCom as siblings. Copy every root
# DLL so model-specific components are not accidentally omitted.
Copy-Item -Path (Join-Path $libraryRoot '*.dll') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $libraryRoot 'HCNetSDKCom') `
    -Destination $destination -Recurse -Force
foreach ($pattern in @('*.json', '*.dat', '*.zip')) {
    Get-ChildItem -LiteralPath $libraryRoot -Filter $pattern -File -ErrorAction SilentlyContinue |
        Copy-Item -Destination $destination -Force
}

$files = Get-ChildItem -LiteralPath $destination -Recurse -File |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($destination.Length + 1).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
            bytes = $_.Length
        }
    }
$manifest = [ordered]@{
    package = 'Hikvision HCNetSDK / PlayCtrl Windows x64'
    version = $sdkVersion
    sourcePath = $sdkRoot
    installedUtc = [DateTime]::UtcNow.ToString('o')
    files = @($files)
}
$manifest | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $destination 'DEPENDENCY_MANIFEST.json') -Encoding UTF8

Write-Host "Hikvision runtime ready: $destination"
Write-Host "Version: $sdkVersion"
Write-Host "Files: $($files.Count)"
