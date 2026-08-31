[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$failures = [Collections.Generic.List[string]]::new()

$requiredFiles = @(
    'LICENSE',
    'README.md',
    'Assets\VlcD3D11Rtsp\Runtime\VlcRtspPlayer.cs',
    'Assets\Plugins\Managed\LibVLCSharp.dll',
    'Native~\VLCUnityPlugin\RenderingPlugin.cpp',
    'scripts\setup-dependencies.ps1',
    'scripts\build-native.ps1'
)
foreach ($relative in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $relative))) {
        $failures.Add("Missing required file: $relative")
    }
}

$playerSource = Get-Content -Raw (Join-Path $repoRoot 'Assets\VlcD3D11Rtsp\Runtime\VlcRtspPlayer.cs')
if ($playerSource -notmatch 'CreateExternalTexture') {
    $failures.Add('Managed player does not create an external D3D11 texture.')
}
if ($playerSource -notmatch 'EnableHardwareDecoding = true') {
    $failures.Add('Managed player does not request hardware decoding for GPU mode.')
}
$nativeSource = Get-Content -Raw (Join-Path $repoRoot 'Native~\VLCUnityPlugin\RenderAPI_D3D11.cpp')
if ($nativeSource -notmatch 'libvlc_video_set_output_callbacks') {
    $failures.Add('Native D3D11 source does not register LibVLC output callbacks.')
}

$trackedRuntime = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'External') -Recurse -File -ErrorAction SilentlyContinue |
    Measure-Object -Property Length -Sum
if ($trackedRuntime.Count -gt 0) {
    Write-Host "Local untracked LibVLC runtime: $($trackedRuntime.Count) files, $($trackedRuntime.Sum) bytes."
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}
Write-Host 'Repository source checks passed.'
