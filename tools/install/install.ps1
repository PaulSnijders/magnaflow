#Requires -Version 5.1
<#
MagnaFlow one-time (re-runnable) machine install.

What it does, in order:
  1. Publishes the four tools (Release) to $InstallDir\.staging\<tool>\, renamed
     to their canonical short names (mf-run.exe, mf-worker.exe, mf-watch.exe,
     mf-cockpit.exe) — so the bare names in configs ("mf-run", "claude"-style
     seams) resolve once the folders are on PATH. The daemons keep running
     meanwhile; a failed publish removes the staging folder and stops the script
     with nothing stopped or replaced. Only then are the daemons stopped and each
     $InstallDir\<tool>\ swapped for its staged copy.
  2. Seeds the machine config (magnaflow.yml) into %APPDATA%\MagnaFlow\ — the
     one location every tool checks regardless of where its binary lives.
     Only happens the first time, when that file doesn't exist yet: once it's
     there, it's the user's live config (projects added via the cockpit, hand
     edits, etc.) and re-running install.ps1 for a tool update must never
     clobber it.
  3. Adds the four tool folders to the *user* PATH.
  4. Copies start-magnaflow.ps1 + the MagnaFlow icon next to the tools and
     creates "MagnaFlow" shortcuts in the Start menu and on the desktop.

Usage:
  powershell -ExecutionPolicy Bypass -File C:\GIT\magnaflow\tools\install\install.ps1

Re-run any time — this is also the update path: after tool code changes or a
machine-config edit, one run publishes, stops the daemons (a running exe is
locked and holds old code/config in memory anyway), swaps in the new build,
re-syncs, and restarts them automatically.

Requires the .NET 10 SDK (the tools target net10.0).
#>
param(
    # Where the published tools land. Keep it out of the git working copies.
    [string]$InstallDir = 'C:\Tools\MagnaFlow',
    # Source of the per-machine config, used only to seed %APPDATA%\MagnaFlow\magnaflow.yml
    # the first time it doesn't exist yet; the wozzol2 repo carries Paul's copy.
    [string]$MachineConfig = 'C:\GIT\wozzol2\.magnaflow\magnaflow.yml'
)

$ErrorActionPreference = 'Stop'
# This script lives at <repo>\tools\install\, so the repo root is two up.
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

Write-Host "== MagnaFlow install (repo: $repoRoot -> $InstallDir) =="

# --- 1. publish the tools into staging (daemons keep running) ----------------
$tools = @(
    @{ Name = 'mf-run';     Csproj = 'tools\mf-run\src\MagnaFlow.MfRun\MagnaFlow.MfRun.csproj' },
    @{ Name = 'mf-worker';  Csproj = 'tools\worker-controller\src\MagnaFlow.WorkerController\MagnaFlow.WorkerController.csproj' },
    @{ Name = 'mf-watch';   Csproj = 'tools\mf-watch\src\MagnaFlow.MfWatch\MagnaFlow.MfWatch.csproj' },
    @{ Name = 'mf-cockpit'; Csproj = 'tools\mf-cockpit\src\MagnaFlow.MfCockpit\MagnaFlow.MfCockpit.csproj' }
)
$stagingDir = Join-Path $InstallDir '.staging'
# A leftover from an interrupted run would otherwise mix into this one.
if (Test-Path $stagingDir) { Remove-Item -Recurse -Force $stagingDir }
foreach ($t in $tools) {
    $out = Join-Path $stagingDir $t.Name
    Write-Host "-- publishing $($t.Name) -> $out"
    dotnet publish (Join-Path $repoRoot $t.Csproj) -c Release -o $out "-p:AssemblyName=$($t.Name)" --nologo
    if ($LASTEXITCODE -ne 0) {
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $stagingDir
        Write-Host "error: dotnet publish failed for $($t.Name) - nothing was stopped, the installed tools in $InstallDir are untouched" -ForegroundColor Red
        exit 1
    }
}

# The cockpit build copies a default mf-cockpit.yml next to its binary (legacy
# pre-fase-7 config). Remove it from the install so %APPDATA%\MagnaFlow\
# magnaflow.yml is unambiguously the config in use.
Remove-Item -ErrorAction SilentlyContinue (Join-Path $stagingDir 'mf-cockpit\mf-cockpit.yml')

# --- 1b. stop running daemons (restarted again at the end), swap in the build -
# Only now that every publish succeeded. Two reasons: Windows locks the exe of
# a running process, so replacing it would fail — and a running daemon keeps
# its old code and config in memory anyway, so an update only lands after a
# restart. Old pre-install builds ran under their assembly names, hence the
# extra two entries.
$daemonNames = @('mf-cockpit', 'mf-watch', 'MagnaFlow.MfCockpit', 'MagnaFlow.MfWatch')
$wasRunning = [bool](Get-Process -Name $daemonNames -ErrorAction SilentlyContinue)

# Project roots of the running watchers, read from their --project argument. The
# restart below brings every one of them back — start-magnaflow.ps1 only knows
# its own -Project, so watchers toggled on per project from the cockpit would
# otherwise stay down after an update.
function Get-WatchedProjects {
    $roots = @()
    foreach ($p in Get-CimInstance Win32_Process -Filter "Name='mf-watch.exe' OR Name='MagnaFlow.MfWatch.exe'") {
        if ($p.CommandLine -match '--project\s+(?:"([^"]+)"|(\S+))') {
            $root = if ($Matches[1]) { $Matches[1] } else { $Matches[2] }
            $roots += $root.TrimEnd('\', '/')
        } else {
            Write-Warning "mf-watch (pid $($p.ProcessId)) runs without --project; it will not be restarted: $($p.CommandLine)"
        }
    }
    return @($roots | Sort-Object -Unique)
}
$watchedProjects = Get-WatchedProjects
if ($wasRunning) {
    Write-Host '-- stopping running MagnaFlow daemons (restarted after the install)'
    Stop-Process -Name $daemonNames -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

foreach ($t in $tools) {
    $target = Join-Path $InstallDir $t.Name
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Move-Item (Join-Path $stagingDir $t.Name) $target
}
Remove-Item -Recurse -Force $stagingDir
Write-Host "-- installed into ${InstallDir}: $(($tools | ForEach-Object { $_.Name }) -join ', ')"

# --- 2. machine config -> %APPDATA%\MagnaFlow (seeded once, never overwritten) --
$appDataDir = Join-Path $env:APPDATA 'MagnaFlow'
New-Item -ItemType Directory -Force -Path $appDataDir | Out-Null
$configTarget = Join-Path $appDataDir 'magnaflow.yml'
if (Test-Path $configTarget) {
    Write-Host "-- machine config already exists, leaving it as-is: $configTarget"
} else {
    Copy-Item $MachineConfig $configTarget
    Write-Host "-- machine config seeded: $MachineConfig -> $configTarget"
}

# --- 3. user PATH ------------------------------------------------------------
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$parts = @($userPath -split ';' | Where-Object { $_ -ne '' })
$added = @()
foreach ($t in $tools) {
    $dir = Join-Path $InstallDir $t.Name
    if ($parts -notcontains $dir) { $parts += $dir; $added += $dir }
}
if ($added.Count -gt 0) {
    [Environment]::SetEnvironmentVariable('Path', ($parts -join ';'), 'User')
    Write-Host "-- added to user PATH: $($added -join ', ')"
    Write-Host "   (new terminals pick this up; already-open ones do not)"
} else {
    Write-Host "-- PATH already up to date"
}

# --- 4. start script, icon + shortcuts ----------------------------------------
Copy-Item (Join-Path $PSScriptRoot 'start-magnaflow.ps1') $InstallDir -Force
$startScript = Join-Path $InstallDir 'start-magnaflow.ps1'

# Square MagnaFlow logo for the shortcuts (assets/ in the repo).
$iconSource = Join-Path $repoRoot 'assets\magnaflow-logo.ico'
$iconTarget = Join-Path $InstallDir 'magnaflow.ico'
Copy-Item $iconSource $iconTarget -Force

$ws = New-Object -ComObject WScript.Shell
$shortcutDirs = @(
    [Environment]::GetFolderPath('Desktop'),
    (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs')
)
foreach ($dir in $shortcutDirs) {
    $lnk = $ws.CreateShortcut((Join-Path $dir 'MagnaFlow.lnk'))
    $lnk.TargetPath = 'powershell.exe'
    $lnk.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$startScript`""
    $lnk.WorkingDirectory = $InstallDir
    $lnk.IconLocation = "$iconTarget,0"
    $lnk.WindowStyle = 7   # minimized
    $lnk.Save()
    Write-Host "-- shortcut: $(Join-Path $dir 'MagnaFlow.lnk')"
}

Write-Host ''
if ($wasRunning) {
    Write-Host '-- restarting daemons with the fresh build'
    & $startScript
    # start-magnaflow.ps1 restarted its own -Project watcher (if none ran); bring
    # back the rest the same way the cockpit toggle starts them: hidden, with
    # --project <root> and the project as working directory.
    $running = Get-WatchedProjects
    foreach ($root in $watchedProjects) {
        if ($running -contains $root) { continue }   # -contains is case-insensitive
        Start-Process (Join-Path $InstallDir 'mf-watch\mf-watch.exe') `
            -ArgumentList '--project', "`"$root`"" -WorkingDirectory $root -WindowStyle Hidden
        Write-Host "mf-watch restarted on $root"
    }
    Write-Host '== Done. Cockpit + watchers restarted on the new version.'
} else {
    Write-Host '== Done. Click the MagnaFlow shortcut (desktop or Start menu) to start'
    Write-Host '   the cockpit + watcher; the browser opens on http://localhost:5210.'
}
