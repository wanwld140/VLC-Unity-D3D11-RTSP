[CmdletBinding()]
param(
    [ValidateSet('Demo', 'Smoke')]
    [string]$Target = 'Demo',
    [string]$UnityPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
if (-not $UnityPath) {
    $preferred = 'C:\Program Files\Unity\Hub\Editor\2021.3.28f1c1\Editor\Unity.exe'
    if (Test-Path -LiteralPath $preferred) {
        $UnityPath = $preferred
    }
    else {
        $candidate = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor' -Filter Unity.exe -Recurse -File |
            Where-Object { $_.FullName -match '2021\.3' } |
            Select-Object -First 1
        if ($candidate) { $UnityPath = $candidate.FullName }
    }
}
if (-not $UnityPath -or -not (Test-Path -LiteralPath $UnityPath)) {
    throw 'Unity 2021.3 editor was not found. Pass -UnityPath explicitly.'
}

$method = if ($Target -eq 'Smoke') {
    'VlcD3D11Rtsp.Editor.VlcProjectBuilder.BatchBuildWindowsSmoke'
} else {
    'VlcD3D11Rtsp.Editor.VlcProjectBuilder.BatchBuildWindowsDemo'
}
$logPath = Join-Path $repoRoot "Build\unity-$($Target.ToLowerInvariant()).log"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null

$arguments = @(
    '-batchmode', '-quit',
    '-projectPath', $repoRoot,
    '-executeMethod', $method,
    '-logFile', $logPath
)
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Unity $Target build failed with exit code $($process.ExitCode). See $logPath"
}
Write-Host "Unity $Target player built successfully. Log: $logPath"
