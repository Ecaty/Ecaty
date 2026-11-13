<#
Sync-WorktreeSubmodules.ps1

What it is for: an agent session works in a Git worktree under .claude/worktrees/<name> and
commits inside that worktree's submodules (Native/Core, Native/App, Packages/com.runtime.flecs,
Packages/com.runtime.ai-agent). Each worktree has private submodule clones, so those commits do
not exist in the main checkout's submodule repositories. This script copies them across, so the
worktree branch can be merged or tested in the main checkout without fetching by hand.

When to run it: before merging or testing a worktree branch in the main checkout.

What it does: for every top-level submodule initialised in both the worktree and the main
checkout, it checks whether the worktree submodule's HEAD commit exists in the main checkout's
submodule repository; if not, it fetches the commit from the worktree submodule's Git dir into a
local branch of the same name (only when that is a fast-forward and the branch is not checked
out there), otherwise into refs/worktree-sync/<worktree-name>/... It prints one row per submodule.

Safety: it never pushes and never changes any working tree or HEAD. It only adds objects and refs
to the main checkout's submodule repositories. Exit code 1 if any row failed.

Usage: Tools\Sync-WorktreeSubmodules.ps1 [-Worktree <path>] [-WhatIf]
#>
param(
    [string]$Worktree,
    [switch]$WhatIf
)
$ErrorActionPreference = 'Stop'

function Invoke-Git {
    # Runs git, returns @{ Code; Out } without PowerShell turning stderr into terminating errors.
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & git @args 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $saved
    }
    return @{ Code = $code; Out = (($lines | Out-String).Trim()) }
}

if ([string]::IsNullOrWhiteSpace($Worktree)) {
    $r = Invoke-Git rev-parse --show-toplevel
    if ($r.Code -ne 0) { throw 'Not inside a Git repository; pass -Worktree <path>.' }
    $Worktree = $r.Out
}
$Worktree = [IO.Path]::GetFullPath($Worktree)
$r = Invoke-Git -C $Worktree rev-parse --show-toplevel
if ($r.Code -ne 0) { throw "Not a Git worktree: $Worktree" }
$Worktree = [IO.Path]::GetFullPath($r.Out)
$wtName = Split-Path -Leaf $Worktree

$r = Invoke-Git -C $Worktree rev-parse --path-format=absolute --git-common-dir
if ($r.Code -ne 0) { throw "Cannot find the common Git dir: $($r.Out)" }
$main = [IO.Path]::GetFullPath((Split-Path -Parent $r.Out))
if ($main.TrimEnd('\','/') -ieq $Worktree.TrimEnd('\','/')) { throw 'This is the main checkout, not a worktree.' }

$gm = Join-Path $Worktree '.gitmodules'
if (!(Test-Path -LiteralPath $gm)) { throw "No .gitmodules in $Worktree" }
$r = Invoke-Git config --file $gm --get-regexp '\.path$'
$paths = @()
foreach ($line in ($r.Out -split "`n")) {
    if ($line -match '^\S+\.path\s+(.+?)\s*$') { $paths += $Matches[1] }
}

Write-Output "Worktree: $Worktree"
Write-Output "Main:     $main"
if ($WhatIf) { Write-Output '(WhatIf: report only, nothing is fetched)' }

function Test-Initialised([string]$dir) {
    if (!(Test-Path -LiteralPath (Join-Path $dir '.git'))) { return $false }
    $x = Invoke-Git -C $dir rev-parse --show-toplevel
    if ($x.Code -ne 0) { return $false }
    return ([IO.Path]::GetFullPath($x.Out).TrimEnd('\','/') -ieq [IO.Path]::GetFullPath($dir).TrimEnd('\','/'))
}

$rows = @()
$failed = $false
foreach ($path in $paths) {
    $wtSub = Join-Path $Worktree $path
    $mainSub = Join-Path $main $path
    $short = '-'
    $branch = '-'
    $result = ''
    try {
        if (!(Test-Initialised $wtSub)) { $result = 'skipped: not initialised in worktree'; continue }
        if (!(Test-Initialised $mainSub)) { $result = 'skipped: not initialised in main checkout'; continue }

        $sha = (Invoke-Git -C $wtSub rev-parse HEAD).Out
        $short = $sha.Substring(0, [Math]::Min(9, $sha.Length))
        $b = Invoke-Git -C $wtSub symbolic-ref --quiet --short HEAD
        if ($b.Code -eq 0 -and $b.Out) { $branch = $b.Out } else { $branch = '(detached)' }
        $gitDir = (Invoke-Git -C $wtSub rev-parse --absolute-git-dir).Out

        if ((Invoke-Git -C $mainSub cat-file -e "$sha^{commit}").Code -eq 0) { $result = 'present'; continue }

        # Decide the destination ref.
        $syncRef = $null
        $note = ''
        if ($branch -ne '(detached)') {
            $branchRef = "refs/heads/$branch"
            $syncRef = "refs/worktree-sync/$wtName/$branch"
            $head = Invoke-Git -C $mainSub symbolic-ref --quiet HEAD
            $exists = (Invoke-Git -C $mainSub rev-parse --verify --quiet $branchRef).Code -eq 0
            if ($head.Code -eq 0 -and $head.Out -eq $branchRef) {
                $dest = $syncRef; $note = " (branch $branch is checked out in main)"
            } elseif (!$exists) {
                $dest = $branchRef
            } else {
                $dest = $branchRef   # re-decided after the fetch: needs fast-forward
            }
        } else {
            $syncRef = 'refs/worktree-sync/' + $wtName + '/' + ($path -replace '[\/]', '-')
            $dest = $syncRef
        }

        if ($WhatIf) { $result = "would fetch into $dest$note"; continue }

        # Always land the commit under the sync ref first (safe), then fast-forward the branch if allowed.
        $f = Invoke-Git -C $mainSub fetch --quiet $gitDir "+HEAD:$syncRef"
        if ($f.Code -ne 0) { throw "fetch failed: $($f.Out)" }
        $final = $syncRef
        if ($dest -eq "refs/heads/$branch" -and $branch -ne '(detached)') {
            $old = Invoke-Git -C $mainSub rev-parse --verify --quiet $dest
            $ok = $true
            if ($old.Code -eq 0) {
                $ok = (Invoke-Git -C $mainSub merge-base --is-ancestor $old.Out $sha).Code -eq 0
                if (!$ok) { $note = " (local $branch is not an ancestor; not moved)" }
            }
            if ($ok) {
                $u = if ($old.Code -eq 0) { Invoke-Git -C $mainSub update-ref $dest $sha $old.Out } else { Invoke-Git -C $mainSub update-ref $dest $sha '' }
                if ($u.Code -ne 0) { throw "update-ref failed: $($u.Out)" }
                Invoke-Git -C $mainSub update-ref -d $syncRef | Out-Null
                $final = $dest
            }
        }
        if ((Invoke-Git -C $mainSub cat-file -e "$sha^{commit}").Code -ne 0) { throw 'commit still missing after fetch' }
        $result = "fetched into $final$note"
    } catch {
        $result = "FAILED: $($_.Exception.Message)"
        $failed = $true
    } finally {
        $rows += [pscustomobject]@{ Path = $path; Commit = $short; Branch = $branch; Result = $result }
    }
}

$rows | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Output
if ($failed) { exit 1 }
exit 0
