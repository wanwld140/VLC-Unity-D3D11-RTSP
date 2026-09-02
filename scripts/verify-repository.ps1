[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$failures = [Collections.Generic.List[string]]::new()

$requiredFiles = @(
    'LICENSE',
    'README.md',
    'THIRD_PARTY_NOTICES.md',
    'Assets\VlcD3D11Rtsp\Runtime\VlcRtspPlayer.cs',
    'Assets\VlcD3D11Rtsp\Runtime\HikvisionNative.cs',
    'Assets\VlcD3D11Rtsp\Runtime\HikvisionRtspPlayer.cs',
    'Assets\VlcD3D11Rtsp\Runtime\Resources\HikvisionYv12.shader',
    'Assets\Plugins\Managed\LibVLCSharp.dll',
    'Assets\Plugins\Android\VLCUnity\vlc-android-java.aar',
    'Assets\Plugins\Android\VLCUnity\arm64-v8a\libvlc.so',
    'Assets\Plugins\Android\VLCUnity\arm64-v8a\libVLCUnityPlugin.so',
    'Assets\Plugins\VLCUnityRuntime\AndroidTextureHelper.cs',
    'Assets\Plugins\VLCUnityRuntime\VLCAndroidInitialization.cs',
    'Assets\Plugins\VLCUnityRuntime\link.xml',
    'Assets\Plugins\VLCUnityRuntime\LICENSES\README.md',
    'Native~\VLCUnityPlugin\RenderingPlugin.cpp',
    'docs\ANDROID_SOURCE_INFO.json',
    'scripts\setup-dependencies.ps1',
    'scripts\setup-hikvision.ps1',
    'scripts\build-native.ps1',
    'scripts\build-unity-player.ps1',
    'scripts\build-android-player.ps1',
    'scripts\run-editor-tests.ps1',
    'scripts\unity-editor.ps1'
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
if ($playerSource -notmatch 'VLCUNITY_ANDROID' -or
    $playerSource -notmatch 'AndroidTextureHelper\.CreateNativeTexture' -or
    $playerSource -notmatch 'VlcActiveVideoPath\.AndroidNativeTexture') {
    $failures.Add('Managed player does not contain the Android native texture path.')
}
$cpuBufferSource = Get-Content -Raw (Join-Path $repoRoot 'Assets\VlcD3D11Rtsp\Runtime\VlcCpuVideoBuffer.cs')
if ($cpuBufferSource -notmatch 'UNITY_ANDROID' -or
    $cpuBufferSource -notmatch 'RV32') {
    $failures.Add('CPU callback buffer is not available to the Android player target.')
}
$nativeSource = Get-Content -Raw (Join-Path $repoRoot 'Native~\VLCUnityPlugin\RenderAPI_D3D11.cpp')
if ($nativeSource -notmatch 'libvlc_video_set_output_callbacks') {
    $failures.Add('Native D3D11 source does not register LibVLC output callbacks.')
}

$hikvisionSource = Get-Content -Raw (Join-Path $repoRoot 'Assets\VlcD3D11Rtsp\Runtime\HikvisionRtspPlayer.cs')
if ($hikvisionSource -notmatch 'NET_DVR_RealPlay_V40' -or
    $hikvisionSource -notmatch 'PlayM4_SetDecCallBackEx') {
    $failures.Add('Hikvision backend does not contain the official preview/decode path.')
}
$hikvisionSetup = Get-Content -Raw (Join-Path $repoRoot 'scripts\setup-hikvision.ps1')
if ($hikvisionSetup -notmatch 'V6\.1\.9\.48_build20230410_win64') {
    $failures.Add('Hikvision setup script does not pin the reviewed SDK version.')
}

$androidArtifacts = @(
    @{
        Path = 'Assets\Plugins\Managed\LibVLCSharp.dll'
        Sha256 = '260EC9F6DCFD5DFC57372D3B1B1167A44D62F3A068BCB1D8EED541D4F529275B'
    },
    @{
        Path = 'Assets\Plugins\Android\VLCUnity\vlc-android-java.aar'
        Sha256 = '864E5A261EA71F3FF4A8F63789F02008180BA0A448F96C6AC364CF0FA1BB315F'
    },
    @{
        Path = 'Assets\Plugins\Android\VLCUnity\arm64-v8a\libvlc.so'
        Sha256 = '89726D8B607C373CA9394D2564D00273CFC83F0D1469ECB999F9878EB5F8925E'
    },
    @{
        Path = 'Assets\Plugins\Android\VLCUnity\arm64-v8a\libVLCUnityPlugin.so'
        Sha256 = 'B4E52BC78C2967901CA8C46EFB43DCD3F0140E5064A91067199CBA643E7CE62D'
    }
)
foreach ($artifact in $androidArtifacts) {
    $artifactPath = Join-Path $repoRoot $artifact.Path
    if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) { continue }
    $actualHash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash
    if ($actualHash -ne $artifact.Sha256) {
        $failures.Add("Android dependency hash mismatch: $($artifact.Path)")
    }
}

$managedMeta = Get-Content -Raw (Join-Path $repoRoot 'Assets\Plugins\Managed\LibVLCSharp.dll.meta')
if ($managedMeta -notmatch '(?ms)Android:\s*Android.*?enabled:\s*1') {
    $failures.Add('LibVLCSharp managed plug-in is not enabled for Android.')
}
$projectSettings = Get-Content -Raw (Join-Path $repoRoot 'ProjectSettings\ProjectSettings.asset')
if ($projectSettings -notmatch 'AndroidMinSdkVersion:\s*29' -or
    $projectSettings -notmatch 'AndroidTargetArchitectures:\s*2' -or
    $projectSettings -notmatch '(?ms)scriptingBackend:\s*\r?\n\s+Android:\s*1') {
    $failures.Add('Android project settings are not API29/ARM64/IL2CPP.')
}
$androidBuilder = Get-Content -Raw (Join-Path $repoRoot 'Assets\VlcD3D11Rtsp\Editor\VlcProjectBuilder.cs')
if ($projectSettings -notmatch '(?ms)m_BuildTarget:\s*AndroidPlayer\s*\r?\n\s*m_APIs:\s*0b000000\s*\r?\n\s*m_Automatic:\s*0' -or
    $androidBuilder -notmatch 'BuildTarget\.Android,\s*new\[\]\s*\{\s*GraphicsDeviceType\.OpenGLES3\s*\}') {
    $failures.Add('Android graphics API is not pinned to the user-tested OpenGL ES 3 baseline.')
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$aarPath = Join-Path $repoRoot 'Assets\Plugins\Android\VLCUnity\vlc-android-java.aar'
if (Test-Path -LiteralPath $aarPath -PathType Leaf) {
    $archive = [IO.Compression.ZipFile]::OpenRead($aarPath)
    try {
        $manifestEntry = $archive.GetEntry('AndroidManifest.xml')
        if ($null -eq $manifestEntry) {
            $failures.Add('Android VLC AAR has no AndroidManifest.xml.')
        }
        else {
            $reader = [IO.StreamReader]::new($manifestEntry.Open())
            try { $aarManifest = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
            if ($aarManifest -notmatch 'android\.permission\.INTERNET') {
                $failures.Add('Android VLC AAR does not declare INTERNET permission.')
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

$externalRoot = Join-Path $repoRoot 'External'
# Fresh clones intentionally do not contain External/ until setup-dependencies.ps1 runs.
# Check the directory first so StrictMode does not turn the missing optional path into a null-property error.
$runtimeFiles = @(
    if (Test-Path -LiteralPath $externalRoot) {
        Get-ChildItem -LiteralPath $externalRoot -Recurse -File
    }
)
$runtimeFileCount = $runtimeFiles.Count
$runtimeByteCount = if ($runtimeFileCount -gt 0) {
    ($runtimeFiles | Measure-Object -Property Length -Sum).Sum
} else {
    0
}
if ($runtimeFileCount -gt 0) {
    Write-Host "Local untracked runtimes: $runtimeFileCount files, $runtimeByteCount bytes."
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}
Write-Host 'Repository source checks passed.'
