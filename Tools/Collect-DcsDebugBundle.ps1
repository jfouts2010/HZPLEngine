[CmdletBinding()]
param(
    [string]$Issue = "",
    [string]$MissionTime = "",
    [string]$SavedGamesPath = "",
    [string]$DocumentsPath = "",
    [string]$LocalAppDataPath = "",
    [string]$OutputDirectory = "",
    [switch]$NonInteractive,
    [switch]$NoOpenFolder
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Get-LatestMatchingFile {
    param(
        [string[]]$Roots,
        [string]$Filter,
        [string]$NamePattern = "*"
    )

    $matches = @(
        foreach ($root in $Roots) {
            if ([string]::IsNullOrWhiteSpace($root) -or
                -not (Test-Path -LiteralPath $root -PathType Container)) {
                continue
            }

            Get-ChildItem -LiteralPath $root -File -Recurse -Filter $Filter `
                -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like $NamePattern }
        }
    )

    return $matches |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Add-BundleFile {
    param(
        [System.IO.FileInfo]$Source,
        [string]$DestinationName,
        [string]$Role,
        [string]$StagingDirectory,
        [System.Collections.ArrayList]$CollectedFiles
    )

    if ($null -eq $Source -or -not $Source.Exists) {
        return
    }

    $destination = Join-Path $StagingDirectory $DestinationName
    Copy-Item -LiteralPath $Source.FullName -Destination $destination
    $copied = Get-Item -LiteralPath $destination
    $hash = Get-FileHash -LiteralPath $destination -Algorithm SHA256

    [void]$CollectedFiles.Add([ordered]@{
        role = $Role
        file = $copied.Name
        originalFileName = $Source.Name
        sourceLastWriteUtc = $Source.LastWriteTimeUtc.ToString("o")
        bytes = $copied.Length
        sha256 = $hash.Hash.ToLowerInvariant()
    })
}

try {
    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    $userProfile = [Environment]::GetFolderPath("UserProfile")

    if ([string]::IsNullOrWhiteSpace($SavedGamesPath)) {
        $SavedGamesPath = Join-Path $userProfile "Saved Games"
    }
    if ([string]::IsNullOrWhiteSpace($DocumentsPath)) {
        $DocumentsPath = [Environment]::GetFolderPath("MyDocuments")
    }
    if ([string]::IsNullOrWhiteSpace($LocalAppDataPath)) {
        $LocalAppDataPath = [Environment]::GetFolderPath("LocalApplicationData")
    }
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $repositoryRoot "DebugCaptures"
    }

    if (-not $NonInteractive) {
        if ([string]::IsNullOrWhiteSpace($Issue)) {
            $Issue = Read-Host "Briefly describe what went wrong (optional)"
        }
        if ([string]::IsNullOrWhiteSpace($MissionTime)) {
            $MissionTime = Read-Host "Approximate DCS mission time, such as 00:42:15 (optional)"
        }
    }

    if (-not (Test-Path -LiteralPath $SavedGamesPath -PathType Container)) {
        throw "Saved Games was not found at '$SavedGamesPath'."
    }

    $dcsRoots = @(
        Get-ChildItem -LiteralPath $SavedGamesPath -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "DCS" -or $_.Name -like "DCS.*" }
    )
    if ($dcsRoots.Count -eq 0) {
        throw "No DCS Saved Games folder was found below '$SavedGamesPath'."
    }

    $trackRoots = @($dcsRoots | ForEach-Object { Join-Path $_.FullName "Tracks" })
    $track = Get-LatestMatchingFile -Roots $trackRoots -Filter "*.trk"

    # A crash track is useful as a fallback when the debrief screen could not save one.
    if ($null -eq $track) {
        $crashTrackRoot = Join-Path ([IO.Path]::GetTempPath()) "DCS"
        $track = Get-LatestMatchingFile -Roots @($crashTrackRoot) -Filter "LastMissionTrack.trk"
    }

    $activeDcsRoot = $null
    if ($null -ne $track) {
        $activeDcsRoot = $dcsRoots |
            Where-Object {
                $track.FullName.StartsWith(
                    $_.FullName,
                    [StringComparison]::OrdinalIgnoreCase)
            } |
            Select-Object -First 1
    }

    if ($null -eq $activeDcsRoot) {
        $activeDcsRoot = $dcsRoots |
            Sort-Object {
                $logPath = Join-Path $_.FullName "Logs\dcs.log"
                if (Test-Path -LiteralPath $logPath -PathType Leaf) {
                    (Get-Item -LiteralPath $logPath).LastWriteTimeUtc
                }
                else {
                    [DateTime]::MinValue
                }
            } -Descending |
            Select-Object -First 1
    }

    $missionCandidates = @(
        foreach ($dcsRoot in $dcsRoots) {
            $missionRoot = Join-Path $dcsRoot.FullName "Missions"
            if (Test-Path -LiteralPath $missionRoot -PathType Container) {
                Get-ChildItem -LiteralPath $missionRoot -File -Recurse -Filter "*.miz" `
                    -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -like "HZPL*.miz" }
            }
        }
    )

    $mission = $null
    if ($missionCandidates.Count -gt 0) {
        $eligibleMissions = $missionCandidates
        if ($null -ne $track) {
            $eligibleMissions = @(
                $missionCandidates |
                Where-Object {
                    $_.LastWriteTimeUtc -le $track.LastWriteTimeUtc.AddMinutes(5)
                }
            )
            if ($eligibleMissions.Count -eq 0) {
                $eligibleMissions = $missionCandidates
            }
        }

        $mission = $eligibleMissions |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
    }

    $dcsLog = $null
    if ($null -ne $activeDcsRoot) {
        $dcsLogPath = Join-Path $activeDcsRoot.FullName "Logs\dcs.log"
        if (Test-Path -LiteralPath $dcsLogPath -PathType Leaf) {
            $dcsLog = Get-Item -LiteralPath $dcsLogPath
        }
    }

    $tacviewRoots = @(
        Join-Path $DocumentsPath "Tacview"
        foreach ($dcsRoot in $dcsRoots) {
            Join-Path $dcsRoot.FullName "Tacview"
        }
    )
    $tacview = Get-LatestMatchingFile -Roots $tacviewRoots -Filter "*.acmi"

    $unityEditorLog = $null
    $unityEditorLogPath = Join-Path $LocalAppDataPath "Unity\Editor\Editor.log"
    if (Test-Path -LiteralPath $unityEditorLogPath -PathType Leaf) {
        $unityEditorLog = Get-Item -LiteralPath $unityEditorLogPath
    }

    $unityPlayerLog = $null
    $localLow = Join-Path (Split-Path -Parent $LocalAppDataPath) "LocalLow"
    $unityPlayerCandidates = @(
        Get-ChildItem -Path (Join-Path $localLow "*\HZPLEngine\Player.log") `
            -File -ErrorAction SilentlyContinue
    )
    if ($unityPlayerCandidates.Count -gt 0) {
        $unityPlayerLog = $unityPlayerCandidates |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
    }

    $warnings = New-Object System.Collections.ArrayList
    if ($null -eq $mission) {
        [void]$warnings.Add("No HZPL .miz mission was found.")
    }
    if ($null -eq $track) {
        [void]$warnings.Add("No DCS .trk was found. Save the track on the DCS debrief screen before collecting.")
    }
    if ($null -eq $dcsLog) {
        [void]$warnings.Add("No dcs.log was found for the selected DCS profile.")
    }
    if ($null -eq $tacview) {
        [void]$warnings.Add("No Tacview .acmi was found; Tacview is optional.")
    }
    if ($null -eq $unityEditorLog -and $null -eq $unityPlayerLog) {
        [void]$warnings.Add("No Unity Editor.log or HZPLEngine Player.log was found.")
    }

    $bundleTimestamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
    $bundleSuffix = [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $bundleId = "HZPL-DCS-$bundleTimestamp-$bundleSuffix"
    $stagingDirectory = Join-Path ([IO.Path]::GetTempPath()) $bundleId
    $zipPath = Join-Path $OutputDirectory "$bundleId.zip"

    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    try {
        $collectedFiles = New-Object System.Collections.ArrayList
        Add-BundleFile -Source $mission -DestinationName "mission.miz" `
            -Role "exported-mission" -StagingDirectory $stagingDirectory `
            -CollectedFiles $collectedFiles
        Add-BundleFile -Source $track -DestinationName "mission-track.trk" `
            -Role "dcs-track" -StagingDirectory $stagingDirectory `
            -CollectedFiles $collectedFiles
        Add-BundleFile -Source $dcsLog -DestinationName "dcs.log" `
            -Role "dcs-log" -StagingDirectory $stagingDirectory `
            -CollectedFiles $collectedFiles
        Add-BundleFile -Source $tacview -DestinationName "tacview.acmi" `
            -Role "tacview-recording" -StagingDirectory $stagingDirectory `
            -CollectedFiles $collectedFiles
        Add-BundleFile -Source $unityEditorLog -DestinationName "unity-editor.log" `
            -Role "unity-editor-log" -StagingDirectory $stagingDirectory `
            -CollectedFiles $collectedFiles
        Add-BundleFile -Source $unityPlayerLog -DestinationName "unity-player.log" `
            -Role "unity-player-log" -StagingDirectory $stagingDirectory `
            -CollectedFiles $collectedFiles

        $gitRevision = $null
        $gitDirty = $null
        try {
            $revisionOutput = & git -C $repositoryRoot rev-parse HEAD 2>$null
            if ($LASTEXITCODE -eq 0) {
                $gitRevision = ($revisionOutput | Select-Object -First 1).Trim()
                $statusOutput = @(& git -C $repositoryRoot status --porcelain 2>$null)
                if ($LASTEXITCODE -eq 0) {
                    $gitDirty = $statusOutput.Count -gt 0
                }
            }
        }
        catch {
            # Git metadata is helpful but not required to collect the simulator files.
        }

        $manifest = [ordered]@{
            bundleFormatVersion = 1
            bundleId = $bundleId
            collectedAtUtc = [DateTime]::UtcNow.ToString("o")
            issue = $Issue
            approximateMissionTime = $MissionTime
            dcsProfile = if ($null -ne $activeDcsRoot) { $activeDcsRoot.Name } else { $null }
            hzplGitRevision = $gitRevision
            hzplWorkingTreeDirty = $gitDirty
            files = @($collectedFiles)
            warnings = @($warnings)
            note = "The current HZPL exporter does not yet write its simulator-neutral export snapshot as a sidecar file."
        }
        $manifestPath = Join-Path $stagingDirectory "manifest.json"
        $manifest | ConvertTo-Json -Depth 6 |
            Set-Content -LiteralPath $manifestPath -Encoding UTF8

        $summaryLines = @(
            "HZPL DCS diagnostic bundle"
            ""
            "Bundle: $bundleId"
            "Issue: $Issue"
            "Approximate mission time: $MissionTime"
            ""
            "Warnings:"
        )
        if ($warnings.Count -eq 0) {
            $summaryLines += "- None"
        }
        else {
            $summaryLines += @($warnings | ForEach-Object { "- $_" })
        }
        $summaryLines += @(
            ""
            "Attach this ZIP to the Codex task; its manifest identifies and verifies each collected file."
        )
        $summaryLines | Set-Content -LiteralPath (Join-Path $stagingDirectory "README.txt") `
            -Encoding UTF8

        Compress-Archive -Path (Join-Path $stagingDirectory "*") `
            -DestinationPath $zipPath -CompressionLevel Optimal
    }
    finally {
        if (Test-Path -LiteralPath $stagingDirectory) {
            Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
        }
    }

    Write-Host ""
    Write-Host "Diagnostic bundle created:" -ForegroundColor Green
    Write-Host $zipPath
    if ($warnings.Count -gt 0) {
        Write-Host ""
        Write-Host "Notes:" -ForegroundColor Yellow
        foreach ($warning in $warnings) {
            Write-Host "- $warning"
        }
    }

    if (-not $NoOpenFolder) {
        Start-Process explorer.exe -ArgumentList ('"{0}"' -f $OutputDirectory)
    }
}
catch {
    Write-Host ""
    Write-Host "Could not create the DCS diagnostic bundle:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
