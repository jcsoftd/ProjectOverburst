param(
    [Parameter(Mandatory=$true)][string]$Directory,
    [ValidateSet('CurrentRun','HistoricalSnapshots')][string]$Scope='CurrentRun'
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $Directory).Path
$expected=@('checkpoint','entry-pending','entry-active','transfer-before','transfer-after','settlement-before','settlement-after')
$boundaries=@{'checkpoint'='after-checkpoint';'entry-pending'='after-write';'entry-active'='after-write';'transfer-before'='before-write';'transfer-after'='after-write';'settlement-before'='before-write';'settlement-after'='after-write'}
$checks=[Collections.Generic.List[object]]::new()
function Check([bool]$Pass,[string]$Label,[string]$CaseName) {
    $checks.Add(@{case=$CaseName;label=$Label;pass=$Pass})
    if (!$Pass) { throw "$CaseName - $Label" }
}
function Has-Item($Snapshot,[string]$Id) { return @($Snapshot.items | Where-Object instanceId -eq $Id).Count -eq 1 }
$result=[ordered]@{status='RUNNING';scope=$Scope;checks=$checks;source='Independent assertions on exported first/second recovered snapshots, without invoking production recovery functions';buildMatch='NOT_RUN';processStartEvidence='NOT_RECORDED_LEGACY';trueWriteInProgress=$null;cases=@()}
try {
    $processResult=Get-Content -LiteralPath (Join-Path $root 'process-results.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $result.trueWriteInProgress=$processResult.trueWriteInProgress
    $processStatus=if($processResult.processStatus){$processResult.processStatus}else{$processResult.status}
    Check ($processStatus -ceq 'PASS_SCOPED') 'Full process result required; partial cases/restarts cannot enter full gate' 'manifest'
    $names=@($processResult.cases | ForEach-Object case)
    Check ($names.Count -eq 7 -and @($names | Sort-Object -Unique).Count -eq 7 -and @($names | Where-Object {$expected -cnotcontains $_}).Count -eq 0) 'Exact expected set of seven case names, each once' 'manifest'
    if ($Scope -eq 'CurrentRun') {
        Check ($processResult.sha256 -match '^[A-Fa-f0-9]{64}$' -and $processResult.managedAssemblySha256 -match '^[A-Fa-f0-9]{64}$') 'Recorded executable and managed assembly hashes are mandatory' 'build'
        $exe=$processResult.executable
        $dll=Join-Path (Split-Path $exe) 'OVERBURST_Data/Managed/Assembly-CSharp.dll'
        $result.managedAssemblySha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
        Check ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -eq $processResult.sha256) 'Player executable still matches the recorded run' 'build'
        Check ($result.managedAssemblySha256 -eq $processResult.managedAssemblySha256) 'Managed assembly still matches the recorded run' 'build'
        $result.buildMatch='PASS_RECORDED_RUN_ONLY'
    }
    foreach ($case in $processResult.cases) {
        $name=$case.case
        $dir=Join-Path $root $name
        $seed=Get-Content -LiteralPath (Join-Path $dir 'seed.json') -Raw | ConvertFrom-Json
        $first=Get-Content -LiteralPath (Join-Path $dir 'verify-1-verify-result.json') -Raw | ConvertFrom-Json
        $second=Get-Content -LiteralPath (Join-Path $dir 'verify-2-verify-result.json') -Raw | ConvertFrom-Json
        $s=$first.snapshot
        $kill=Get-Content -LiteralPath (Join-Path $dir 'kill.json') -Raw | ConvertFrom-Json
        $ready=Get-Content -LiteralPath (Join-Path $dir 'crash-ready.json') -Raw | ConvertFrom-Json
        Check ($case.status -ceq 'PASS' -and $case.restartCount -eq 2 -and $case.revisionStable -eq $true -and $case.liveInventoryProjection -eq $true) 'Case completed exactly two restarts and live projection' $name
        Check ($seed.caseName -ceq $name -and $ready.caseName -ceq $name -and $kill.case -ceq $name -and $first.caseName -ceq $name -and $second.caseName -ceq $name) 'Manifest, seed, ready, kill and both restart labels agree' $name
        Check ($case.boundary -ceq $boundaries[$name] -and $ready.boundary -ceq $boundaries[$name] -and $kill.boundary -ceq $boundaries[$name] -and $ready.status -ceq 'READY_FOR_OS_KILL') 'Manifest, ready and kill use the expected case boundary' $name
        Check ($first.repeatedRestart -eq $false -and $second.repeatedRestart -eq $true -and $first.revision -eq $second.revision) 'Distinct first and second recovered restart evidence' $name
        if ($processResult.ownedProcessReceipts -eq $true) {
            $start=Get-Content -LiteralPath (Join-Path $dir 'crash-start.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            Check ($start.case -ceq $name -and $start.mode -ceq 'crash' -and $start.attempt -ceq 'crash' -and $start.pid -eq $ready.pid -and $start.pid -eq $kill.pid -and $start.pid -gt 0 -and $start.executable -eq $processResult.executable) 'Same-run owned start, ready and kill identify the same Player' $name
            foreach ($attempt in 1,2) {
                $receipt=Get-Content -LiteralPath (Join-Path $dir "verify-$attempt-process.json") -Raw -Encoding UTF8 | ConvertFrom-Json
                Check ($receipt.case -ceq $name -and $receipt.mode -ceq 'verify' -and $receipt.attempt -ceq "verify-$attempt" -and $receipt.pid -gt 0 -and $receipt.executable -eq $processResult.executable -and $receipt.exitCode -eq 0 -and $receipt.completed -eq $true -and $receipt.startedUtc -and $receipt.exitedUtc) "Owned restart $attempt started and exited successfully" $name
            }
            $result.processStartEvidence='PASS_RECORDED_STARTS'
        }
        Check ($kill.pid -eq $ready.pid -and $kill.pid -gt 0 -and $kill.normalExitRequested -eq $false -and $kill.status -ceq 'KILLED') 'Owned Player PID was OS-killed at the marked boundary without normal quit' $name
        Check ($first.status -ceq 'PASS' -and $second.status -ceq 'PASS') 'Both actual Player restarts passed' $name
        Check ($first.actualInventoryProjection -eq $true -and $second.actualInventoryProjection -eq $true) 'Actual booted inventory IDs and quantities matched persisted account' $name
        Check ($second.repeatedRestart -eq $true -and $second.interrupted -eq $false) 'Second restart did not apply recovery again' $name
        Check (($s | ConvertTo-Json -Depth 100 -Compress) -ceq ($second.snapshot | ConvertTo-Json -Depth 100 -Compress)) 'Entire recovered account is stable across restart, including revision and reward ledger' $name
        $ids=@($s.items | ForEach-Object instanceId)
        Check (($ids | Sort-Object -Unique).Count -eq $ids.Count) 'No duplicated item identities' $name
        $owners=@($s.inventory | Where-Object {$_}) + @($s.weapons | Where-Object {$_}) + @($s.gear | Where-Object {$_}) + @($s.bags | Where-Object {$_}) + @($s.flasks | Where-Object {$_})
        foreach ($tab in $s.stashTabs) { $owners+=@($tab.slots | Where-Object {$_}) }
        foreach ($merchant in $s.merchants) { $owners+=@($merchant.stock | Where-Object {$_}); $owners+=@($merchant.currency | Where-Object {$_}) }
        if ($s.elementalGemInstanceId) { $owners+= $s.elementalGemInstanceId }
        Check (($owners | Sort-Object -Unique).Count -eq $owners.Count -and $owners.Count -eq $ids.Count -and @($owners | Where-Object {$_ -notin $ids}).Count -eq 0) 'Every stored item has exactly one valid owner' $name
        $transferred=$name -in @('transfer-after','settlement-before','settlement-after')
        $stashItem=@($s.items | Where-Object instanceId -eq $seed.stashSeedId)
        Check ($stashItem.Count -eq 1 -and $stashItem[0].count -eq $(if ($transferred) {12} else {5})) 'Stash stack equals durable boundary quantity (5 or 12)' $name
        Check ($s.stashTabs[0].slots -contains $seed.stashSeedId) 'Transferred stack remains in stash ownership' $name
        $mapExpected=$name -in @('checkpoint','entry-pending')
        Check ((Has-Item $s $seed.mapId) -eq $mapExpected) 'Map remains before activation and is consumed once after activation' $name
        if ($mapExpected) { Check ($s.inventory -contains $seed.mapId) 'Unconsumed map is still owned in inventory' $name }
        if ($name -eq 'checkpoint') {
            Check ($s.experience -eq $seed.snapshot.experience+7 -and $s.bossClearCount -eq $seed.snapshot.bossClearCount) 'Saved XP checkpoint restored exactly once without extra rewards' $name
        } else {
            Check ($s.run.phase -eq $(if ($name -eq 'settlement-after') {3} else {4})) 'Interrupted runs settle as Failed; committed extraction remains Extracted' $name
            Check (@($s.run.transferredObjects).Count -eq $(if ($transferred) {1} else {0})) 'Transfer ledger contains zero or one committed identity' $name
            if ($name -like 'transfer-*' -or $name -like 'settlement-*') {
                Check (!(Has-Item $s $seed.transferId)) 'Merged or failed-run source identity is not duplicated' $name
                $extracted=$name -eq 'settlement-after'
                Check ((Has-Item $s $seed.lostId) -eq $extracted) 'Untransferred run loot follows existing failure/extraction policy' $name
                if ($extracted) { $loot=@($s.items | Where-Object instanceId -eq $seed.lostId)[0]; Check ($loot.count -eq 3 -and !$loot.originRunId -and $s.inventory -contains $seed.lostId) 'Extracted loot has correct quantity and permanent inventory ownership' $name }
            }
            $rewarded=$name -like 'settlement-*'
            Check ($s.experience -eq $seed.snapshot.experience+$(if ($rewarded) {11} else {0})) 'Event XP is neither omitted nor granted twice' $name
            Check ($s.bossClearCount -eq $seed.snapshot.bossClearCount+$(if ($rewarded) {1} else {0})) 'Boss reward count is neither omitted nor doubled' $name
            Check (@($s.run.rewardedEncounters).Count -eq $(if ($rewarded) {1} else {0})) 'One-time event ledger preserved' $name
        }
        $result.cases+=@{case=$name;status='PASS';pid=$kill.pid;restartCount=2;revision=$first.revision;itemCount=$ids.Count;stashCount=$stashItem[0].count}
    }
    $result.status=if($Scope -eq 'CurrentRun'){'PASS_SCOPED'}else{'PASS_HISTORICAL_SNAPSHOTS'}
    $result.checkCount=$checks.Count
} catch { $result.status='FAIL'; $result.error=$_.Exception.Message; throw }
finally { $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $root 'independent-contract-result.json') -Encoding utf8 }
$result.status+': '+$checks.Count+' independent assertions ('+$Scope+')'
