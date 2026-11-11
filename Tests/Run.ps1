param([ValidateSet('Native','Gallery','EditMode','PlayMode','Resources','Validator','PlayerScripts','All')][string]$Suite='All')
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
        if($raw.Code -ne 0 -and $raw.Output -match 'EDITOR_NOT_RUNNING|EDITOR_NOT_READY' -and ($CommandArgs[0..1] -join ' ') -in @('editor status','script recompile_status','test status')){
            Wait-Editor
            $raw=Invoke-UnityProcess $cli $arguments
        }
        if($raw.Code -ne 0){throw "Unity command failed: $($CommandArgs[0]) $($CommandArgs[1]). $($raw.Output) $($raw.Error)"}
        $result=$raw.Output | ConvertFrom-Json
        if($result.success -eq $false){throw ($result | ConvertTo-Json -Depth 10)}
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
    $tickState=Invoke-Editor @('script','eval','return new {enabled=UnityEditor.SessionState.GetBool("Unity.Pipeline.AutoTick.Enabled",false),interval=UnityEditor.SessionState.GetInt("Unity.Pipeline.AutoTick.IntervalMs",16)};')
    $saveInfo=Invoke-Editor @('script','eval','return System.IO.Path.Combine(UnityEngine.Application.persistentDataPath,"GalleryPrototype","tree-arrangement.json");')
    $gallerySavePath=[string]$saveInfo.result;$hadGallerySave=Test-Path -LiteralPath $gallerySavePath
    [byte[]]$gallerySaveBytes=$null
    if($hadGallerySave){$gallerySaveBytes=[IO.File]::ReadAllBytes($gallerySavePath)}
    $saveBackedUp=$true
    try {
    if($wasPlaying){
        Invoke-Editor @('script','eval','var g=UnityEngine.Object.FindAnyObjectByType<Ecaty.CrystalWorldGallery>();if(g){if(g.IsDragging)throw new System.InvalidOperationException("Finish or cancel the held world before running tests.");UnityEditor.SessionState.SetString("WorldTests.OriginalArrangement",g.CaptureArrangement());g.SaveArrangement();}return true;') | Out-Null
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
                if($baselineStored){Invoke-Editor @('script','eval','var snapshot=UnityEditor.SessionState.GetString("WorldTests.OriginalArrangement","");var g=UnityEngine.Object.FindAnyObjectByType<Ecaty.CrystalWorldGallery>();if(g&&!string.IsNullOrEmpty(snapshot))g.RestoreArrangement(snapshot);UnityEditor.SessionState.EraseString("WorldTests.OriginalArrangement");return true;') | Out-Null}
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
            elseif($d -and $d.summary){$detail="$($d.summary.total) tests, $($d.summary.failed) failed"}
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
