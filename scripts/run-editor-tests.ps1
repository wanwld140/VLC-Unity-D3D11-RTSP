[CmdletBinding()]
param(
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
$logPath = Join-Path $repoRoot 'Build\editor-tests.log'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null
$arguments = @(
    '-batchmode', '-quit',
    '-projectPath', $repoRoot,
    '-executeMethod', 'VlcD3D11Rtsp.Editor.VlcEditorSelfTests.BatchRun',
    '-logFile', $logPath
)
$exitCode = Invoke-UnityEditorBatch -UnityPath $UnityPath -Arguments $arguments
if ($exitCode -ne 0) {
    throw "Unity editor self-tests failed with exit code $exitCode. See $logPath"
}
Write-Host "Unity editor self-tests passed. Log: $logPath"
