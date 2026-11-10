$ErrorActionPreference = 'Stop'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskAssets = Join-Path $taskProject 'Packages/com.ecaty.ai-agent/Runtime/Diagnostics/Resources/EcatyAgentTrace'
$taskHtml = [IO.File]::ReadAllText((Join-Path $taskAssets 'index.html.txt'))
$taskCss = [IO.File]::ReadAllText((Join-Path $taskAssets 'viewer.css.txt'))
$taskJs = [IO.File]::ReadAllText((Join-Path $taskAssets 'viewer.js.txt'))
$taskHtml = $taskHtml.Replace('<link rel="stylesheet" href="/viewer.css">', '<style>' + $taskCss + '</style>')
$taskHtml = $taskHtml.Replace('<script defer src="/viewer.js"></script>', '<script>' + $taskJs + '</script>')
$taskFolder = Join-Path $taskProject 'Logs/TraceViewer'
[IO.Directory]::CreateDirectory($taskFolder) | Out-Null
$taskOutput = Join-Path $taskFolder 'agent-trace-viewer.html'
[IO.File]::WriteAllText($taskOutput, $taskHtml)
Start-Process -FilePath $taskOutput | Out-Null
Write-Output 'Opened offline Agent Trace. Import a JSONL log; no Unity process or AI account is required.'
