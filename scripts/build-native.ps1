[CmdletBinding()]
param(
    [switch]$DebugBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$sourceRoot = Join-Path $repoRoot 'Native~\VLCUnityPlugin'
$sdkRoot = Join-Path $repoRoot 'External\VLCUnityWindows\Plugins\sdk'
$outputRoot = Join-Path $repoRoot 'Build\Native'
$stagingRoot = Join-Path $outputRoot 'staging'
$pluginOutput = Join-Path $repoRoot 'Assets\Plugins\x86_64\VLCUnityPlugin.dll'
$stagedPluginOutput = Join-Path $stagingRoot 'VLCUnityPlugin.dll'
$pdbOutput = Join-Path $outputRoot 'VLCUnityPlugin.pdb'
$importLibraryOutput = Join-Path $outputRoot 'VLCUnityPlugin.lib'

function Assert-PluginReplaceable {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $stream = $null
    try {
        $stream = [IO.File]::Open(
            $Path,
            [IO.FileMode]::Open,
            [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None)
    }
    catch {
        throw "VLCUnityPlugin.dll is in use and cannot be replaced: $Path. Close every Unity Editor and VLC Unity player that has this project loaded, then retry. The existing plug-in was not changed."
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $sdkRoot 'include\vlc\vlc.h'))) {
    throw 'LibVLC SDK is missing. Run scripts/setup-dependencies.ps1 first.'
}

$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vsWhere)) {
    throw 'Visual Studio Installer vswhere.exe was not found.'
}
$vsInstall = & $vsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsInstall) { throw 'Visual Studio C++ x64 tools were not found.' }
$vsDevCmd = Join-Path $vsInstall 'Common7\Tools\VsDevCmd.bat'
if (-not (Test-Path -LiteralPath $vsDevCmd)) { throw "VsDevCmd.bat was not found: $vsDevCmd" }

Assert-PluginReplaceable -Path $pluginOutput
New-Item -ItemType Directory -Force -Path $outputRoot,$stagingRoot,(Split-Path -Parent $pluginOutput) | Out-Null
$optimization = if ($DebugBuild) { '/Od /Zi /D_DEBUG' } else { '/O2 /DNDEBUG' }
$responsePath = Join-Path $outputRoot 'build.rsp'
$sources = @(
    'Log.cpp',
    'RenderAPI.cpp',
    'RenderAPIRegistry.cpp',
    'RenderingPlugin.cpp',
    'RenderAPI_D3D11.cpp'
) | ForEach-Object { '"' + (Join-Path $sourceRoot $_) + '"' }

$response = @(
    '/nologo', '/utf-8', '/std:c++14', '/EHsc', '/MD', $optimization,
    '/DWIN32_LEAN_AND_MEAN', '/D_CRT_SECURE_NO_WARNINGS',
    ('/I"' + $sourceRoot + '"'),
    ('/I"' + (Join-Path $sdkRoot 'include') + '"')
) + $sources + @(
    '/link', '/DLL', ('/OUT:"' + $stagedPluginOutput + '"'), ('/PDB:"' + $pdbOutput + '"'),
    ('/IMPLIB:"' + $importLibraryOutput + '"'),
    '/Brepro',
    '/DELAYLOAD:libvlc.dll', 'delayimp.lib',
    ('"' + (Join-Path $sdkRoot 'lib\libvlc.lib') + '"'),
    'd3d11.lib', 'dxgi.lib', 'd3d12.lib'
)
# Keep /link and all linker switches on the same logical command line. cl.exe
# treats a newline after /link inside a response file as the end of its linker
# switch forwarding scope.
$response -join ' ' | Set-Content -LiteralPath $responsePath -Encoding ASCII

$command = 'call "' + $vsDevCmd + '" -arch=x64 -host_arch=x64 && cd /d "' +
           $outputRoot + '" && cl @"' + $responsePath + '"'
& $env:ComSpec /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw "Native build failed with exit code $LASTEXITCODE." }

$dumpbin = Get-Command dumpbin.exe -ErrorAction SilentlyContinue
if (-not $dumpbin) {
    $dumpCommand = 'call "' + $vsDevCmd + '" -arch=x64 -host_arch=x64 && dumpbin /exports "' + $stagedPluginOutput + '"'
    $exports = & $env:ComSpec /d /s /c $dumpCommand
}
else {
    $exports = & $dumpbin.Source /exports $stagedPluginOutput
}

$requiredExports = @(
    'libvlc_unity_bridge_api_version',
    'libvlc_unity_set_next_media_player_rendering_mode',
    'libvlc_unity_media_player_new',
    'libvlc_unity_get_texture',
    'libvlc_unity_has_native_renderer',
    'GetRenderEventFunc',
    'UnityPluginLoad'
)
foreach ($required in $requiredExports) {
    if (-not ($exports -match [Regex]::Escape($required))) {
        throw "Native plug-in is missing required export: $required"
    }
}

Assert-PluginReplaceable -Path $pluginOutput
try {
    Copy-Item -LiteralPath $stagedPluginOutput -Destination $pluginOutput -Force
}
catch {
    throw "Native build passed and the staged DLL remains at $stagedPluginOutput, but the Unity plug-in could not be replaced. Close every Unity Editor and VLC Unity player that has this project loaded, then retry. The existing plug-in may still be in use."
}

$stagedHash = (Get-FileHash -LiteralPath $stagedPluginOutput -Algorithm SHA256).Hash
$hash = (Get-FileHash -LiteralPath $pluginOutput -Algorithm SHA256).Hash
if ($hash -ne $stagedHash) {
    throw 'The installed native plug-in hash does not match the verified staged DLL.'
}
Write-Host "Native bridge ready: $pluginOutput"
Write-Host "SHA-256: $hash"
