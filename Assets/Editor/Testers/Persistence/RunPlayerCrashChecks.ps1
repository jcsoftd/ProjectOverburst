param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$DungeonScene='DiamondDungeon01',
    [ValidateRange(1,2)][int]$RestartCount=2,
    [ValidateSet('checkpoint','entry-pending','entry-active','transfer-before','transfer-after','settlement-before','settlement-after')][string[]]$Cases=@('checkpoint','entry-pending','entry-active','transfer-before','transfer-after','settlement-before','settlement-after')
)
$ErrorActionPreference = 'Stop'
$knownCases=@('checkpoint','entry-pending','entry-active','transfer-before','transfer-after','settlement-before','settlement-after')
if ($Cases.Count -eq 0 -or @($Cases | Sort-Object -Unique).Count -ne $Cases.Count -or @($Cases | Where-Object {$knownCases -cnotcontains $_}).Count -gt 0) { throw 'Cases must be canonical, nonempty and contain each selected case once.' }
$exeFull = (Resolve-Path -LiteralPath $Executable).Path
$outputFull = [IO.Path]::GetFullPath($OutputDirectory)
$allowed = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../../개인파일/코덱스산출')) + [IO.Path]::DirectorySeparatorChar
if (!$outputFull.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must stay under this goal artifact directory.' }
if (Test-Path -LiteralPath $outputFull) { throw 'Use a fresh output directory.' }
[IO.Directory]::CreateDirectory($outputFull) | Out-Null
$cases = $Cases
$ownedPlayers = [Collections.Generic.List[Diagnostics.Process]]::new()
$assembly=Join-Path (Split-Path $exeFull) 'OVERBURST_Data/Managed/Assembly-CSharp.dll'
$manifest = [ordered]@{status='RUNNING'; processStatus='RUNNING'; ownedProcessReceipts=$true; selectedCases=@($cases); requestedRestartCount=$RestartCount; executable=$exeFull; sha256=(Get-FileHash -LiteralPath $exeFull -Algorithm SHA256).Hash; managedAssemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash; cases=@(); trueWriteInProgress=@{status='NOT_RUN'; reason='FaultInjector before-write/after-write brackets are not evidence of interruption inside ES3 file write.'}}
function Save-Manifest { $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputFull 'process-results.json') -Encoding utf8 }
function Start-OwnedPlayer([string]$caseDir,[string]$caseName,[string]$mode,[string]$attempt) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $exeFull
    $info.UseShellExecute = $false
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.Arguments = '-screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile "' + (Join-Path $caseDir ($attempt + '-player.log')) + '"'
    $info.EnvironmentVariables['OVERBURST_SAVE_DIRECTORY'] = Join-Path $caseDir 'Account'
    $info.EnvironmentVariables['OVERBURST_SETTINGS_DIRECTORY'] = Join-Path $caseDir 'Settings'
    $info.EnvironmentVariables['OVERBURST_RECOVERY_QA_MODE'] = $mode
    $info.EnvironmentVariables['OVERBURST_RECOVERY_QA_OUTPUT'] = $caseDir
    $info.EnvironmentVariables['OVERBURST_RECOVERY_QA_CASE'] = $caseName
    $info.EnvironmentVariables['OVERBURST_RECOVERY_QA_SCENE'] = $DungeonScene
    $started = [Diagnostics.Process]::Start($info)
    $ownedPlayers.Add($started)
    [ordered]@{case=$caseName;mode=$mode;attempt=$attempt;pid=$started.Id;executable=$exeFull;startedUtc=$started.StartTime.ToUniversalTime().ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $caseDir ($attempt+'-start.json')) -Encoding utf8
    return $started
}
function Stop-OwnedPlayer([Diagnostics.Process]$process) {
    $process.Refresh()
    if ($process.HasExited) { throw 'Owned Player exited before force-kill boundary.' }
    if (![string]::Equals($process.MainModule.FileName,$exeFull,[StringComparison]::OrdinalIgnoreCase)) { throw 'PID executable no longer matches owned Player.' }
    $process.Kill()
    if (!$process.WaitForExit(15000)) { throw 'Owned Player did not exit after kill.' }
}
Save-Manifest
try {
    foreach ($case in $cases) {
        $dir = Join-Path $outputFull $case
        [IO.Directory]::CreateDirectory($dir) | Out-Null
        'KAN-26-20261005' | Set-Content -LiteralPath (Join-Path $dir 'qa-owner.txt') -Encoding utf8
        $process = Start-OwnedPlayer $dir $case 'crash' 'crash'
        $deadline = [DateTime]::UtcNow.AddSeconds(180)
        $readyPath = Join-Path $dir 'crash-ready.json'
        while (!(Test-Path -LiteralPath $readyPath)) {
            if ($process.HasExited) { throw "Case $case exited early; see owned Player log." }
            if ([DateTime]::UtcNow -gt $deadline) { Stop-OwnedPlayer $process; throw "Case $case timed out before boundary." }
            Start-Sleep -Milliseconds 100
        }
        $ready = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
        $expectedBoundary=if($case -eq 'checkpoint'){'after-checkpoint'}elseif($case.EndsWith('-before')){'before-write'}else{'after-write'}
        if ($ready.pid -ne $process.Id -or $ready.caseName -cne $case -or $ready.boundary -cne $expectedBoundary -or $ready.status -cne 'READY_FOR_OS_KILL') { throw 'Ready marker PID, case or boundary mismatch.' }
        Stop-OwnedPlayer $process
        $killRecord = [ordered]@{case=$case;status='KILLED';pid=$ready.pid;boundary=$ready.boundary;utc=[DateTime]::UtcNow.ToString('o');normalExitRequested=$false;files=@()}
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $dir 'Account') -File) {
            $killRecord.files += @{name=$file.Name;length=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
        }
        $killRecord | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $dir 'kill.json') -Encoding utf8
        for ($attempt=1; $attempt -le $RestartCount; $attempt++) {
            $verify = Start-OwnedPlayer $dir $case 'verify' "verify-$attempt"
            $verifyDeadline = [DateTime]::UtcNow.AddSeconds(180)
            while (!$verify.HasExited) {
                if ([DateTime]::UtcNow -gt $verifyDeadline) { Stop-OwnedPlayer $verify; throw "Case $case restart $attempt timed out." }
                Start-Sleep -Milliseconds 100
            }
            if ($verify.ExitCode -ne 0) { throw "Case $case restart $attempt failed: exit $($verify.ExitCode)." }
            $receipt=Get-Content -LiteralPath (Join-Path $dir ("verify-$attempt-start.json")) -Raw -Encoding UTF8 | ConvertFrom-Json
            $receipt | Add-Member -NotePropertyName exitedUtc -NotePropertyValue $verify.ExitTime.ToUniversalTime().ToString('o')
            $receipt | Add-Member -NotePropertyName exitCode -NotePropertyValue $verify.ExitCode
            $receipt | Add-Member -NotePropertyName completed -NotePropertyValue $true
            $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dir ("verify-$attempt-process.json")) -Encoding utf8
            foreach ($name in @('verify-result.json','preboot-verify.json')) {
                Copy-Item -LiteralPath (Join-Path $dir $name) -Destination (Join-Path $dir ("verify-$attempt-" + $name))
            }
        }
        $first = Get-Content -LiteralPath (Join-Path $dir 'verify-1-verify-result.json') -Raw | ConvertFrom-Json
        if ($first.status -cne 'PASS' -or $first.caseName -cne $case -or $first.repeatedRestart -ne $false -or $first.actualInventoryProjection -ne $true) { throw "Case $case first restart evidence mismatch." }
        if ($RestartCount -eq 2) {
            $second = Get-Content -LiteralPath (Join-Path $dir 'verify-2-verify-result.json') -Raw | ConvertFrom-Json
            if ($second.status -cne 'PASS' -or $second.caseName -cne $case -or $second.repeatedRestart -ne $true -or $second.interrupted -ne $false -or $second.actualInventoryProjection -ne $true) { throw "Case $case second restart evidence mismatch." }
            if ($first.revision -ne $second.revision) { throw "Case $case second restart unexpectedly changed revision." }
        }
        $manifest.cases += @{case=$case;status= $(if ($RestartCount -eq 2) {'PASS'} else {'PASS_FIRST_RESTART_SECOND_NOT_RUN'});boundary=$ready.boundary;restartCount=$RestartCount;revisionStable= $(if ($RestartCount -eq 2) {$true} else {$null});liveInventoryProjection=$true}
        Save-Manifest
        Write-Output ("PASS {0}: OS force kill + {1} restart(s) + ownership/reward/projection" -f $case,$RestartCount)
    }
    $manifest.processStatus= $(if ($RestartCount -eq 2 -and $cases.Count -eq 7) {'PASS_SCOPED'} else {'PASS_PARTIAL_CASES_OR_RESTARTS'})
    $manifest.status='CHECKING_INDEPENDENT_CONTRACTS'
    Save-Manifest
    if($manifest.processStatus -eq 'PASS_SCOPED') {
        & (Join-Path $PSScriptRoot 'ConfirmPlayerRecoveryContracts.ps1') -Directory $outputFull
        $independent=Get-Content -LiteralPath (Join-Path $outputFull 'independent-contract-result.json') -Raw | ConvertFrom-Json
        if($independent.status -ne 'PASS_SCOPED'){throw 'Independent recovery contracts failed.'}
        $manifest.status='PASS_SCOPED'
    } else { $manifest.status=$manifest.processStatus }
} catch {
    $manifest.status='FAIL'
    $manifest.error=$_.Exception.Message
    throw
} finally {
    foreach ($owned in $ownedPlayers) {
        try {
            $owned.Refresh()
            if (!$owned.HasExited) { Stop-OwnedPlayer $owned }
        } catch { Write-Warning ('Owned Player cleanup: ' + $_.Exception.Message) }
        finally { $owned.Dispose() }
    }
    Save-Manifest
}
