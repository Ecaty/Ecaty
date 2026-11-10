function ConvertTo-ReportText($Value){return [Net.WebUtility]::HtmlEncode([string]$Value)}
function Write-RunReport([string]$Folder,$Contexts) {
    $captions=@{'01-gallery'='Gallery';'02-held-ghost'='Gallery - drag preview';'03-swap-preview'='Gallery - swap destination';'04-inside-world'='World - entry';'05-exit-feedback'='World - exit transition';'06-top-view'='Gallery - overhead';'07-final-gallery'='Gallery - restored';'08-native-world'='World - native simulation';'09-saved-world-gallery'='Gallery - saved world';'failure-state'='Failure context'}
    $contextsArray=@($Contexts)
    $failed=@($contextsArray | Where-Object {$_.status -eq 'Failed'}).Count -gt 0
    $done=@($contextsArray | Where-Object {$_.status -in @('Queued','Running')}).Count -eq 0
    $title=if($failed){'Failed'}elseif($done){'Passed'}else{'Running'}
    $model=[ordered]@{status=$title;generated=[DateTime]::Now.ToString('s');contexts=$contextsArray}
    [IO.File]::WriteAllText((Join-Path $Folder 'run.json'),($model | ConvertTo-Json -Depth 30))
    $html=New-Object Text.StringBuilder
    [void]$html.Append('<!doctype html><meta charset="utf-8"><title>Test run</title><style>body{font:16px system-ui;background:#17282d;color:#e0eee9;max-width:1100px;margin:28px auto;padding:0 20px}a{color:#b5d5ca}details{background:#203338;padding:14px;margin:12px 0;border-radius:10px}summary{cursor:pointer;font-weight:600}.Passed,.success{color:#9cddad}.Failed,.error{color:#ffac9c}.Skipped,.Queued,.Running{color:#d6c89a}.error-panel{border-left:4px solid #ffac9c;padding:12px;background:#432f30}.success-panel{border-left:4px solid #9cddad;padding-left:12px}pre{white-space:pre-wrap;overflow-wrap:anywhere}figure{margin:24px 0}img{max-width:100%;border-radius:8px}figcaption{margin-bottom:8px}button{cursor:pointer;margin-right:8px;padding:8px}li{margin:5px 0}</style><h1 class="')
    [void]$html.Append($title).Append('">Test run: ').Append($title).Append('</h1><p><a href="runner.log">Runner log</a> · <a href="run.json">JSON</a></p><p><button onclick="document.querySelectorAll(''details'').forEach(d=&gt;d.open=true)">Expand contexts</button><button onclick="document.querySelectorAll(''details'').forEach(d=&gt;d.open=false)">Collapse contexts</button></p>')
    foreach($context in $contextsArray){
        [void]$html.Append('<details open><summary class="').Append($context.status).Append('">').Append((ConvertTo-ReportText $context.name)).Append(' · ').Append($context.status).Append('</summary>')
        if($context.error){[void]$html.Append('<div class="error-panel"><strong>Error</strong><pre>').Append((ConvertTo-ReportText $context.error)).Append('</pre></div>')}
        $data=$context.data
        if($data){
            $checks=@($data.checks)
            if($data.summary){[void]$html.Append('<p class="success">').Append($data.summary.passed).Append(' passed; ').Append($data.summary.failed).Append(' failed.</p>')}
            if($data.assets){[void]$html.Append('<p>').Append($data.assets.Count).Append(' configuration assets; ').Append($data.validations).Append(' validations.</p>')}
            if($data.results){foreach($issue in $data.results){if($issue.severity -and $issue.severity -ne 'Valid'){[void]$html.Append('<pre class="error">').Append((ConvertTo-ReportText ($issue.asset+' '+$issue.path+': '+$issue.message))).Append('</pre>')}}}
            $errors=@($checks | Where-Object {$_.passed -eq $false})
            $success=@($checks | Where-Object {$_.passed -eq $true})
            if($errors.Count){[void]$html.Append('<div class="error-panel"><strong>Failed checks</strong><ul>');foreach($check in $errors){[void]$html.Append('<li>').Append((ConvertTo-ReportText ($check.name+$check.resource))).Append('</li>')};[void]$html.Append('</ul></div>')}
            if($success.Count){[void]$html.Append('<div class="success-panel"><strong>Passed checks (').Append($success.Count).Append(')</strong><ul>');foreach($check in $success){[void]$html.Append('<li>').Append((ConvertTo-ReportText ($check.name+$check.resource))).Append('</li>')};[void]$html.Append('</ul></div>')}
            foreach($picture in @($data.images)){if(-not $picture){continue};$caption=$captions[[string]$picture.name];if(-not $caption){$caption=$picture.name};$file='Gallery/'+[Uri]::EscapeDataString([string]$picture.file);[void]$html.Append('<figure><figcaption>').Append((ConvertTo-ReportText $caption)).Append('</figcaption><a href="').Append($file).Append('"><img loading="lazy" src="').Append($file).Append('"></a></figure>')}
        }
        [void]$html.Append('</details>')
    }
    [IO.File]::WriteAllText((Join-Path $Folder 'index.html'),$html.ToString())
    $relative=(Split-Path -Leaf $Folder)+'/index.html'
    [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $Folder) 'Latest.html'),('<!doctype html><meta http-equiv="refresh" content="0;url='+$relative+'"><a href="'+$relative+'">Latest test run</a>'))
}
