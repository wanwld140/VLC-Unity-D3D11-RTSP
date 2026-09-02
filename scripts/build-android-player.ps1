[CmdletBinding()]
param(
    [ValidateSet('Demo', 'Smoke')]
    [string]$Target = 'Demo',
    [string]$UnityPath,
    [string]$UnityVersion,
    [switch]$AllowVersionMismatch,
    [switch]$NormalizeChinaVersionForUpgrade
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
. (Join-Path $PSScriptRoot 'unity-editor.ps1')
$selection = Resolve-UnityEditor -RepoRoot $repoRoot -UnityPath $UnityPath `
    -UnityVersion $UnityVersion -AllowVersionMismatch:$AllowVersionMismatch
$selection.ProjectVersion = Prepare-UnityChinaVersionUpgrade `
    -RepoRoot $repoRoot -ProjectVersion $selection.ProjectVersion `
    -TargetVersion $selection.Version `
    -NormalizeChinaVersionForUpgrade:$NormalizeChinaVersionForUpgrade
$UnityPath = $selection.Path
Write-Host "Using Unity $($selection.Version) (project: $($selection.ProjectVersion))."

$method = if ($Target -eq 'Smoke') {
    'VlcD3D11Rtsp.Editor.VlcProjectBuilder.BatchBuildAndroidSmoke'
} else {
    'VlcD3D11Rtsp.Editor.VlcProjectBuilder.BatchBuildAndroidDemo'
}
$logPath = Join-Path $repoRoot "Build\unity-android-$($Target.ToLowerInvariant()).log"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

$arguments = @(
    '-batchmode', '-quit',
    '-buildTarget', 'Android',
    '-projectPath', $repoRoot,
    '-executeMethod', $method,
    '-logFile', $logPath
)
$exitCode = Invoke-UnityEditorBatch -UnityPath $UnityPath -Arguments $arguments
if ($exitCode -ne 0) {
    throw "Unity Android $Target build failed with exit code $exitCode. See $logPath"
}

$outputName = if ($Target -eq 'Smoke') {
    'VlcRtspAndroidSmoke.apk'
} else {
    'VlcRtspAndroidDemo.apk'
}
$outputPath = Join-Path $repoRoot "Build\Android\$outputName"
if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
    throw "Unity reported success but the APK was not found: $outputPath"
}
Write-Host "Unity Android $Target APK built successfully: $outputPath"
Write-Host "Log: $logPath"
