param([ValidateSet('Native','Gallery','EditMode','PlayMode','Resources','Validator','PlayerScripts','Extended','All')][string]$Suite='All')
$ErrorActionPreference='Stop'
$projectPath=Split-Path -Parent $PSScriptRoot
$runDirectory=Join-Path $PSScriptRoot ('Results/'+[DateTime]::Now.ToString('yyyy-MM-dd_HH-mm-ss_fff'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
. (Join-Path $PSScriptRoot 'UnityCli.ps1')
. (Join-Path $PSScriptRoot 'TestReport.ps1')
$selectedSuites=if($Suite -eq 'All'){@('Validator','Resources','PlayerScripts','EditMode','PlayMode','Gallery')}else{@($Suite)}
$contexts=@([pscustomobject]@{name='Runner setup';status='Running';error='';data=$null})
foreach($name in $selectedSuites){$contexts+=[pscustomobject]@{name=$name;status='Queued';error='';data=$null}}
$currentContext=$contexts[0];$failure=$null;$tickChanged=$false;$saveBackedUp=$false;$baselineStored=$false
Write-RunReport $runDirectory $contexts
Start-Transcript -LiteralPath (Join-Path $runDirectory 'runner.log') -Force | Out-Null
try {
    $command=Get-Command unity -ErrorAction SilentlyContinue
    $cli=if($command){$command.Source}else{Join-Path $env:LOCALAPPDATA 'Microsoft/WindowsApps/unity.exe'}
    if(-not (Test-Path -LiteralPath $cli)){throw 'Unity CLI not found. Install it, then open this project in Unity.'}
    Write-Host "Target project: $projectPath"
    Write-Host "Results: $runDirectory"
    Write-Host "PowerShell $($PSVersionTable.PSVersion); native argument transport enabled."
    function Wait-Editor {
        $deadline=[DateTime]::UtcNow.AddMinutes(5)
        do {
            $raw=Invoke-UnityProcess $cli @('status','--until-ready','--timeout','45','--project-path',$projectPath,'--format','json')
            if($raw.Code -eq 0){return}
            $ready=$raw.Output | ConvertFrom-Json
            $titles=@($ready.data.instances | ForEach-Object {$_.blockedBy.dialogs} | ForEach-Object {$_.title})
            if(@($ready.errors | Where-Object code -eq 'STATUS_BLOCKED_BY_DIALOG').Count -and $titles.Count -gt 0 -and @($titles | Where-Object {$_ -notmatch '^(Running managed callbacks|Hold on\.{0,3}|Reloading Domain|Compiling Scripts|Importing assets)(?: \(busy for [0-9:.hms ]+\))?\.{0,3}$'}).Count -eq 0){Start-Sleep -Milliseconds 750}
            else {throw "Editor is not ready. Open this project in Unity or inspect its prompt. $($raw.Output) $($raw.Error)"}
        } while([DateTime]::UtcNow -lt $deadline)
        throw 'Unity is still showing reload progress after five minutes.'
    }
    function Invoke-Editor([string[]]$CommandArgs) {
        $arguments=@('command','--timeout','300','--project-path',$projectPath,'--caller','plugin','--skill','unity-cli','--format','json')+$CommandArgs
        $raw=Invoke-UnityProcess $cli $arguments
        # A domain reload can drop the connection mid-command; wait for the Editor and retry once.
        if($raw.Code -ne 0 -and ($raw.Output -match 'closed the connection|EDITOR_NOT_RUNNING|EDITOR_NOT_READY')){
            Wait-Editor
            $raw=Invoke-UnityProcess $cli $arguments
        }
        if($raw.Code -ne 0){throw "Unity command failed: $($CommandArgs[0]) $($CommandArgs[1]). $($raw.Output) $($raw.Error)"}
        $result=$raw.Output | ConvertFrom-Json
        if($result.success -eq $false){throw ($result | ConvertTo-Json -Depth 10)}
        return $result
    }
    # Extended: run one registered verifier (Tests/Extended.json) and return its pass/fail and report.
    function Invoke-ExtendedVerifier($Entry,[string]$Directory) {
        $started=[DateTime]::UtcNow
        $reportPath=if($Entry.report){Join-Path $projectPath $Entry.report}else{$null}
        $timeout=if($Entry.timeout){[int]$Entry.timeout}else{180}
        $result=[pscustomobject]@{name=$Entry.name;passed=$false;error='';report=$null}
        try {
            if($Entry.kind -eq 'python'){
                $python=(Get-Command python -ErrorAction SilentlyContinue),(Get-Command py -ErrorAction SilentlyContinue) | Where-Object {$_} | Select-Object -First 1
                if(-not $python){throw 'Python not found on PATH.'}
                $run=Invoke-UnityProcess $python.Source (@((Join-Path $projectPath $Entry.script))+@($Entry.args))
                Set-Content -LiteralPath (Join-Path $Directory (($Entry.name -replace '[^A-Za-z0-9]+','-')+'.log')) -Value @($run.Output,$run.Error) -Encoding UTF8
                if($run.Code -ne 0){$tail=(([string]$run.Error+' '+[string]$run.Output) -replace '\s+',' ').Trim();throw "Exit code $($run.Code): $($tail.Substring([Math]::Max(0,$tail.Length-300)))"}
            } else {
                $state=Invoke-Editor @('editor','status')
                if($state.playMode -ne 'stopped'){Invoke-Editor @('editor.playmode','stop') | Out-Null;Wait-Editor}
                # Verifiers that enter Play mode reload the domain; the CLI reply may be lost, so the report file is the source of truth.
                try {Invoke-Editor @('script','eval',[string]$Entry.start) | Out-Null} catch {Write-Host "  start reply lost for $($Entry.name): $($_.Exception.Message.Substring(0,[Math]::Min(120,$_.Exception.Message.Length)))"}
                $limit=[DateTime]::UtcNow.AddSeconds($timeout)
                while(-not ((Test-Path -LiteralPath $reportPath) -and (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -gt $started)){
                    if([DateTime]::UtcNow -gt $limit){throw "No report within $timeout seconds ($($Entry.report))."}
                    Start-Sleep -Milliseconds 1000
                }
                Start-Sleep -Milliseconds 800
            }
            if($reportPath -and (Test-Path -LiteralPath $reportPath)){
                $stamp=(Get-Item -LiteralPath $reportPath).LastWriteTimeUtc
                if($Entry.kind -eq 'editor' -or $stamp -gt $started){
                    Copy-Item -LiteralPath $reportPath -Destination (Join-Path $Directory (Split-Path -Leaf $reportPath)) -Force
                    $result.report=Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
                    if($result.report.passed -ne $true){throw "Report says passed=$($result.report.passed)$(if($result.report.error){': '+(([string]$result.report.error -replace '\s+',' ').Substring(0,[Math]::Min(300,([string]$result.report.error).Length)))})."}
                }
            }
            $result.passed=$true
        } catch {$result.error=$_.Exception.Message}
        finally {
            if($Entry.kind -eq 'editor'){
                try {Wait-Editor;if((Invoke-Editor @('editor','status')).playMode -ne 'stopped'){Invoke-Editor @('editor.playmode','stop') | Out-Null;Wait-Editor}} catch {Write-Host "  could not stop Play after $($Entry.name)"}
            }
        }
        return $result
    }
    Wait-Editor
    $deadline=[DateTime]::UtcNow.AddSeconds(90)
    do {
        $initial=Invoke-Editor @('editor','status');$compile=Invoke-Editor @('script','recompile_status')
        if(-not $initial.compiling -and $compile.status -ne 'compiling'){break}
        if([DateTime]::UtcNow -gt $deadline){throw 'Unity is still compiling. Wait for imports to finish.'}
        Start-Sleep -Milliseconds 500;Wait-Editor
    } while($true)
    $wasPlaying=$initial.playMode -eq 'playing'
    if($initial.playMode -eq 'paused'){throw 'Resume or stop paused Play mode before running tests.'}
    $tickState=Invoke-Editor @('script','eval','return Ecaty.Editor.RunnerSupport.AutoTickState();')
    $saveInfo=Invoke-Editor @('script','eval','return Ecaty.Editor.RunnerSupport.GallerySavePath();')
    $gallerySavePath=[string]$saveInfo.result;$hadGallerySave=Test-Path -LiteralPath $gallerySavePath
    [byte[]]$gallerySaveBytes=$null
    if($hadGallerySave){$gallerySaveBytes=[IO.File]::ReadAllBytes($gallerySavePath)}
    $saveBackedUp=$true
    try {
    if($wasPlaying){
        Invoke-Editor @('script','eval','return Ecaty.Editor.RunnerSupport.CaptureGalleryArrangement();') | Out-Null
        $baselineStored=$true
    }
    Invoke-Editor @('editor','set_auto_tick') | Out-Null;$tickChanged=$true
    # Refresh saved source while the original Play/save/tick state is protected.
    Invoke-Editor @('script','recompile') | Out-Null
    Start-Sleep -Milliseconds 750;Wait-Editor
    $compileDeadline=[DateTime]::UtcNow.AddMinutes(3)
    do {
        $currentCompile=Invoke-Editor @('script','recompile_status')
        if($currentCompile.failed -or $currentCompile.compilationFailed){throw "Compilation failed: $($currentCompile.errors -join '; ')"}
        if($currentCompile.status -ne 'compiling'){break}
        if([DateTime]::UtcNow -gt $compileDeadline){throw 'Compilation exceeded three minutes.'}
        Start-Sleep -Milliseconds 500;Wait-Editor
    } while($true)

    $currentContext.status='Passed';Write-RunReport $runDirectory $contexts
        foreach($current in $selectedSuites){
            $currentContext=$contexts | Where-Object name -eq $current | Select-Object -First 1
            $currentContext.status='Running';Write-RunReport $runDirectory $contexts
            if($current -eq 'Validator' -or $current -eq 'Resources'){
                $code=if($current -eq 'Validator'){'return Ecaty.Editor.AssetValidation.Run();'}else{'return Ecaty.Editor.ResourceVerification.Run();'}
                $result=Invoke-Editor @('script','eval',$code);$currentContext.data=$result.result
                $file=if($current -eq 'Validator'){'validation.json'}else{'resources.json'}
                $result.result | ConvertTo-Json -Depth 25 | Set-Content -LiteralPath (Join-Path $runDirectory $file) -Encoding UTF8
                if(-not $result.result.passed){throw "$current failed. See $file in the report folder."}
                $currentContext.status=if($result.result.skipped){'Skipped'}else{'Passed'}
            } elseif($current -eq 'PlayerScripts'){
                $state=Invoke-Editor @('editor','status')
                if($state.playMode -ne 'stopped'){Invoke-Editor @('editor.playmode','stop') | Out-Null;Wait-Editor}
                $directory=Join-Path $runDirectory 'PlayerScripts';$literal=ConvertTo-Json -InputObject $directory -Compress
                $result=Invoke-Editor @('script','eval',"return Ecaty.Editor.PlayerCodeVerification.Run($literal);",'--','--timeout','240')
                $currentContext.data=$result.result
                $result.result | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $runDirectory 'player-scripts.json') -Encoding UTF8
                if(-not $result.result.passed){throw 'Player script compilation failed.'}
                $currentContext.status='Passed'
            } elseif($current -eq 'Extended'){
                $directory=Join-Path $runDirectory 'Extended';New-Item -ItemType Directory -Path $directory -Force | Out-Null
                $manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Extended.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                $state=Invoke-Editor @('editor','status')
                if($state.playMode -ne 'stopped'){Invoke-Editor @('editor.playmode','stop') | Out-Null;Wait-Editor}
                $outcomes=@()
                foreach($entry in $manifest.verifiers){
                    $outcome=Invoke-ExtendedVerifier $entry $directory;$outcomes+=$outcome
                    Write-Host "  $($outcome.name): $(if($outcome.passed){'passed'}else{'FAILED'})"
                    $currentContext.data=[pscustomobject]@{checks=@($outcomes | ForEach-Object {[pscustomobject]@{name=$_.name+$(if($_.error){': '+$_.error}else{''});passed=$_.passed}})}
                    Write-RunReport $runDirectory $contexts
                }
                $outcomes | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'extended.json') -Encoding UTF8
                $failedNames=@($outcomes | Where-Object {-not $_.passed} | ForEach-Object {$_.name})
                if($failedNames.Count){throw "Extended failed: $($failedNames -join ', ')."}
                $currentContext.status='Passed'
            } elseif($current -eq 'Gallery'){
                $state=Invoke-Editor @('editor','status')
                if($state.playMode -ne 'playing'){Invoke-Editor @('editor','set_auto_tick') | Out-Null;Invoke-Editor @('editor.playmode','start') | Out-Null;Wait-Editor}
                $directory=Join-Path $runDirectory 'Gallery';$literal=ConvertTo-Json -InputObject $directory -Compress
                $result=Invoke-Editor @('script','eval',"return await Ecaty.Editor.GalleryScenarioSuite.Run($literal);")
                $currentContext.data=$result.result
                if(-not $result.result.passed){throw "Gallery failed: $($result.result.failure)"}
                $currentContext.status='Passed'
                Write-Host "Gallery: $($result.result.checks.Count) checks passed."
            } else {
                $state=Invoke-Editor @('editor','status')
                if($state.playMode -ne 'stopped'){Invoke-Editor @('editor.playmode','stop') | Out-Null;Wait-Editor}
                $mode=if($current -eq 'PlayMode'){'playmode'}else{'editor'}
                $filter=if($current -eq 'Native'){'NativeGalleryTests'}else{'Ecaty.Tests'}
                $run=Invoke-Editor @('test','run','--','--mode',$mode,'--filter',$filter,'--async-tests','true')
                $statusFile=Join-Path $projectPath $run.StatusPath;$deadline=[DateTime]::UtcNow.AddMinutes(3)
                do {
                    Start-Sleep -Milliseconds 700;$test=$null
                    if(Test-Path -LiteralPath $statusFile){try{$test=Get-Content -LiteralPath $statusFile -Raw -Encoding UTF8 | ConvertFrom-Json}catch [System.ArgumentException]{}catch [IO.IOException]{}}
                    if($null -eq $test){$test=[pscustomobject]@{status='pending'}}
                    if([DateTime]::UtcNow -gt $deadline){throw 'Tests exceeded three minutes; inspect the Unity Test Runner.'}
                } while($test.status -in @('running','pending','inprogress','in_progress'))
                Start-Sleep -Milliseconds 1500;Wait-Editor
                $currentContext.data=$test
                $test | ConvertTo-Json -Depth 25 | Set-Content -LiteralPath (Join-Path $runDirectory ($current.ToLower()+'.json')) -Encoding UTF8
                $test | ConvertTo-Json -Depth 25 | Set-Content -LiteralPath (Join-Path $projectPath ('Verification/'+$current.ToLower()+'-test-results.json')) -Encoding UTF8
                if($test.status -ne 'completed' -or $test.summary.total -le 0 -or $test.summary.failed -gt 0 -or $test.success -eq $false){throw "$current failed or ran zero tests."}
                $currentContext.status='Passed'
            }
            Write-Host "$current`: $($currentContext.status)";Write-RunReport $runDirectory $contexts
        }
    } finally {
        # Preserve the primary failure and always restore file bytes, even if Editor cleanup fails.
        try {
            if($tickChanged){
                Wait-Editor;$state=Invoke-Editor @('editor','status')
                if($wasPlaying -and $state.playMode -ne 'playing'){Invoke-Editor @('editor','set_auto_tick') | Out-Null;Invoke-Editor @('editor.playmode','start') | Out-Null;Wait-Editor}
                elseif(-not $wasPlaying -and $state.playMode -ne 'stopped'){Invoke-Editor @('editor.playmode','stop') | Out-Null;Wait-Editor}
                if($baselineStored){Invoke-Editor @('script','eval','return Ecaty.Editor.RunnerSupport.RestoreGalleryArrangement();') | Out-Null}
                Invoke-Editor @('editor','set_auto_tick','--','--enabled',([string]$tickState.result.enabled).ToLowerInvariant(),'--interval-ms',([string]$tickState.result.interval)) | Out-Null
            }
        } finally {
            if($saveBackedUp){if($hadGallerySave){[IO.File]::WriteAllBytes($gallerySavePath,$gallerySaveBytes)}elseif(Test-Path -LiteralPath $gallerySavePath){Remove-Item -LiteralPath $gallerySavePath}}
        }
    }
} catch {
    $failure=$_.Exception.Message;$currentContext.status='Failed';$currentContext.error=$failure
    Write-Host ('Test run failed: '+$failure) -ForegroundColor Red
} finally {
    foreach($context in $contexts){if($context.status -eq 'Queued'){$context.status='Skipped';$context.error='Not run because an earlier stage failed.'}}
    Write-RunReport $runDirectory $contexts
    # Compact, agent-readable summary of the newest run: one line per stage plus failing check names only.
    try {
        $lines=@("Run: $(Split-Path -Leaf $runDirectory)  Suite: $Suite  Result: $(if($failure){'FAILED'}else{'PASSED'})")
        foreach($context in $contexts){
            $detail=''
            $d=$context.data
            if($d -and $d.checks){$bad=@($d.checks | Where-Object {-not $_.passed});$detail="$(@($d.checks).Count) checks, $($bad.Count) failed";foreach($b in $bad){$detail+="`n    FAIL: $($b.name)"}}
            elseif($d -and $d.summary){$detail="$($d.summary.total) tests, $($d.summary.failed) failed";foreach($b in @($d.results | Where-Object {$_.Status -eq 'Failed'})){$msg=(([string]$b.Message) -replace '\s+',' ').Trim();$detail+="`n    FAIL: $($b.FullName): "+$msg.Substring(0,[Math]::Min(200,$msg.Length))}}
            elseif($d -and $null -ne $d.validations){$detail="$($d.validations) validations"}
            $line="  $($context.name): $($context.status)"+$(if($detail){" ($detail)"}else{''})
            if($context.error){$flat=(($context.error -replace '\s+',' ').Trim());$line+="`n    ERROR: "+$flat.Substring(0,[Math]::Min(400,$flat.Length))}
            $lines+=$line
        }
        $lines+="Report: $runDirectory/index.html"
        Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Results/latest.txt') -Value $lines -Encoding UTF8
    } catch { Write-Host "latest.txt not written: $($_.Exception.Message)" }
    Write-Host "Report: $runDirectory/index.html"
    Stop-Transcript | Out-Null
}
if($failure){exit 1}
