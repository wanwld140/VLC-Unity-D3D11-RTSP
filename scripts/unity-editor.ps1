Set-StrictMode -Version Latest

function Get-UnityProjectVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $versionFile = Join-Path $RepoRoot 'ProjectSettings\ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $versionFile)) {
        throw "Unity project version file was not found: $versionFile"
    }

    $versionLine = Get-Content -LiteralPath $versionFile |
        Where-Object { $_ -match '^m_EditorVersion:\s*(\S+)\s*$' } |
        Select-Object -First 1
    if (-not $versionLine -or $versionLine -notmatch '^m_EditorVersion:\s*(\S+)\s*$') {
        throw "Unable to read m_EditorVersion from $versionFile"
    }

    return $Matches[1]
}

function ConvertTo-UnityVersionInfo {
    [CmdletBinding()]
    param([string]$Version)

    if ([string]::IsNullOrWhiteSpace($Version) -or
        $Version -notmatch '^(\d+)\.(\d+)\.(\d+)([abfp])(\d+)(?:c\d+)?$') {
        return $null
    }

    return [PSCustomObject]@{
        Text = $Version
        Major = [int]$Matches[1]
        Minor = [int]$Matches[2]
        Patch = [int]$Matches[3]
        Channel = $Matches[4]
        Build = [int]$Matches[5]
        SortKey = [Version]::Parse(
            $Matches[1] + '.' + $Matches[2] + '.' + $Matches[3] + '.' + $Matches[5])
    }
}

function Get-UnityEditorVersionFromPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$UnityPath
    )

    $resolved = [IO.Path]::GetFullPath($UnityPath)
    $editorDirectory = Split-Path -Parent $resolved
    $versionDirectory = Split-Path -Parent $editorDirectory
    $candidates = @(
        (Split-Path -Leaf $versionDirectory),
        (Get-Item -LiteralPath $resolved).VersionInfo.ProductVersion,
        (Get-Item -LiteralPath $resolved).VersionInfo.FileVersion
    )

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            $candidate -match '(\d+\.\d+\.\d+[abfp]\d+(?:c\d+)?)') {
            return $Matches[1]
        }
    }

    return $null
}

function Get-InstalledUnityEditors {
    [CmdletBinding()]
    param(
        [string]$HubRoot = (Join-Path $env:ProgramFiles 'Unity\Hub\Editor')
    )

    if (-not (Test-Path -LiteralPath $HubRoot)) { return }

    foreach ($directory in Get-ChildItem -LiteralPath $HubRoot -Directory) {
        $path = Join-Path $directory.FullName 'Editor\Unity.exe'
        if (-not (Test-Path -LiteralPath $path)) { continue }

        [PSCustomObject]@{
            Version = $directory.Name
            Path = $path
            Info = ConvertTo-UnityVersionInfo $directory.Name
        }
    }
}

function Resolve-UnityEditor {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [string]$UnityPath,
        [string]$UnityVersion,
        [switch]$AllowVersionMismatch
    )

    $projectVersion = Get-UnityProjectVersion -RepoRoot $RepoRoot
    $projectInfo = ConvertTo-UnityVersionInfo $projectVersion
    $installed = @(Get-InstalledUnityEditors)
    $selected = $null

    if (-not [string]::IsNullOrWhiteSpace($UnityPath)) {
        $resolvedPath = [IO.Path]::GetFullPath($UnityPath)
        if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
            throw "Unity editor was not found at the explicit path: $resolvedPath"
        }

        $selected = [PSCustomObject]@{
            Version = Get-UnityEditorVersionFromPath -UnityPath $resolvedPath
            Path = $resolvedPath
            Info = $null
        }
        $selected.Info = ConvertTo-UnityVersionInfo $selected.Version
    }
    elseif (-not [string]::IsNullOrWhiteSpace($UnityVersion)) {
        $selected = $installed |
            Where-Object { $_.Version -eq $UnityVersion } |
            Select-Object -First 1
        if ($null -eq $selected) {
            $available = if ($installed.Count -gt 0) {
                ($installed | ForEach-Object { $_.Version } | Sort-Object) -join ', '
            }
            else { 'none' }
            throw "Unity $UnityVersion is not installed under Unity Hub. Installed versions: $available. Pass -UnityPath for a custom location."
        }
    }
    else {
        $selected = $installed |
            Where-Object { $_.Version -eq $projectVersion } |
            Select-Object -First 1

        if ($null -eq $selected -and $null -ne $projectInfo) {
            $selected = $installed |
                Where-Object {
                    $null -ne $_.Info -and
                    $_.Info.Major -eq $projectInfo.Major -and
                    $_.Info.Minor -eq $projectInfo.Minor
                } |
                Sort-Object @{ Expression = { $_.Info.SortKey }; Descending = $true } |
                Select-Object -First 1
        }

        if ($null -eq $selected) {
            $available = if ($installed.Count -gt 0) {
                ($installed | ForEach-Object { $_.Version } | Sort-Object) -join ', '
            }
            else { 'none' }
            throw "Unity $projectVersion required by ProjectSettings/ProjectVersion.txt was not found. Installed Hub versions: $available. Install the project version, pass -UnityVersion, or pass -UnityPath. Cross-version use also requires -AllowVersionMismatch."
        }
    }

    if ([string]::IsNullOrWhiteSpace($selected.Version)) {
        if (-not $AllowVersionMismatch) {
            throw "The version of $($selected.Path) could not be determined. Pass -AllowVersionMismatch only if you intentionally accept possible project changes."
        }
        Write-Warning "Unity editor version could not be determined for $($selected.Path)."
    }
    elseif ($selected.Version -eq $projectVersion) {
        # Exact project version; no warning is needed.
    }
    elseif ($null -ne $projectInfo -and $null -ne $selected.Info -and
            $selected.Info.Major -eq $projectInfo.Major -and
            $selected.Info.Minor -eq $projectInfo.Minor) {
        Write-Warning "Using Unity $($selected.Version) for a project pinned to $projectVersion. This is a same-stream patch change and Unity may update ProjectVersion.txt."
    }
    elseif (-not $AllowVersionMismatch) {
        throw "Unity $($selected.Version) does not match project version $projectVersion. Cross-version opening can rewrite project files. Re-run with -AllowVersionMismatch only after committing or copying the project."
    }
    else {
        Write-Warning "Using Unity $($selected.Version) for a project pinned to $projectVersion because -AllowVersionMismatch was supplied. Unity may upgrade or rewrite project files."
    }

    return [PSCustomObject]@{
        Path = $selected.Path
        Version = $selected.Version
        ProjectVersion = $projectVersion
    }
}

function Prepare-UnityChinaVersionUpgrade {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$ProjectVersion,
        [AllowNull()]
        [AllowEmptyString()]
        [string]$TargetVersion,
        [switch]$NormalizeChinaVersionForUpgrade
    )

    $targetInfo = ConvertTo-UnityVersionInfo $TargetVersion
    if ($null -eq $targetInfo -or $targetInfo.Major -lt 6000 -or
        $ProjectVersion -eq $TargetVersion -or
        $ProjectVersion -notmatch '^(\d+\.\d+\.\d+[abfp]\d+)c\d+$') {
        return $ProjectVersion
    }

    $normalizedVersion = $Matches[1]
    if (-not $NormalizeChinaVersionForUpgrade) {
        throw "Unity $TargetVersion cannot import the China-editor version marker $ProjectVersion. Re-run with -NormalizeChinaVersionForUpgrade together with -AllowVersionMismatch. The script backs up ProjectVersion.txt under Build before changing only the c-suffix marker."
    }

    $versionFile = Join-Path $RepoRoot 'ProjectSettings\ProjectVersion.txt'
    $content = [IO.File]::ReadAllText($versionFile)
    if (-not $content.Contains($ProjectVersion)) {
        throw "ProjectVersion.txt changed while preparing the Unity upgrade. No file was modified."
    }

    $backupDirectory = Join-Path $RepoRoot 'Build\UnityUpgradeBackup'
    [IO.Directory]::CreateDirectory($backupDirectory) | Out-Null
    $backupName = 'ProjectVersion-' +
        [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff') + '-' +
        [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.txt'
    $backupPath = Join-Path $backupDirectory $backupName
    [IO.File]::Copy($versionFile, $backupPath, $false)

    $updated = $content.Replace($ProjectVersion, $normalizedVersion)
    $utf8WithoutBom = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($versionFile, $updated, $utf8WithoutBom)
    Write-Warning "Normalized $ProjectVersion to $normalizedVersion for Unity $TargetVersion. Backup: $backupPath"
    return $normalizedVersion
}

function Invoke-UnityEditorBatch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$UnityPath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    # PowerShell 7 Start-Process -Wait can wait for Unity's long-lived helper
    # descendants after Unity.exe itself has exited. WaitForExit targets only the
    # editor process whose exit code represents this batch invocation.
    $process = Start-Process -FilePath $UnityPath -ArgumentList $Arguments `
        -PassThru -WindowStyle Hidden
    $process.WaitForExit()
    return $process.ExitCode
}
