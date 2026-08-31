[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2021.3.28f1c1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw 'Unity 2021.3 editor was not found. Pass -UnityPath explicitly.'
}
$logPath = Join-Path $repoRoot 'Build\editor-tests.log'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null
$arguments = @(
    '-batchmode', '-quit',
    '-projectPath', $repoRoot,
    '-executeMethod', 'VlcD3D11Rtsp.Editor.VlcEditorSelfTests.BatchRun',
    '-logFile', $logPath
)
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Unity editor self-tests failed with exit code $($process.ExitCode). See $logPath"
}
Write-Host "Unity editor self-tests passed. Log: $logPath"
