<#
Starts the MagnaFlow daemons on this machine — this is what the "MagnaFlow"
shortcut runs. Idempotent: anything already running is left alone (mf-watch
additionally holds its own per-project lockfile, so a double start is refused
by the tool itself too).

 - mf-cockpit: dashboard + chat, reads %APPDATA%\MagnaFlow\magnaflow.yml,
   serves http://localhost:5210
 - mf-watch:  polls $Project\docs\prompts\ for status: ready commands and
   dispatches mf-worker

Gotcha this script guards against: pre-install (Debug) builds ran under their
assembly names (MagnaFlow.MfCockpit / MagnaFlow.MfWatch). A leftover old
cockpit keeps port 5210 — the browser then talks to THAT process (with its
old, already-loaded config) while a freshly started cockpit dies silently on
the bind conflict, making every config change appear to do nothing. So: old
builds are stopped here, and a port held by anything else aborts the start
with a clear warning instead.

Each daemon runs minimized in its own console window — bring it up from the
taskbar to see live output; close the window to stop that daemon.
#>
param(
    # The project mf-watch watches. The cockpit's project list comes from
    # magnaflow.yml instead — this only steers the watcher.
    [string]$Project = 'C:\GIT\wozzol2',
    # Where install.ps1 put the tools (this script is copied next to them).
    [string]$InstallDir = $PSScriptRoot,
    # Cockpit URL to open; keep in sync with cockpit.port in magnaflow.yml.
    [string]$CockpitUrl = 'http://localhost:5210'
)

$ErrorActionPreference = 'Stop'

# --- retire old-named (pre-install Debug) daemons ----------------------------
foreach ($old in @('MagnaFlow.MfCockpit', 'MagnaFlow.MfWatch')) {
    $p = Get-Process $old -ErrorAction SilentlyContinue
    if ($p) {
        Write-Host "old build '$old' still running (pid $($p.Id -join ', ')) - stopping it"
        $p | Stop-Process -Force
    }
}
Start-Sleep -Milliseconds 500

# --- mf-cockpit ---------------------------------------------------------------
if (Get-Process mf-cockpit -ErrorAction SilentlyContinue) {
    Write-Host 'mf-cockpit already running'
} else {
    $port = [int]([uri]$CockpitUrl).Port
    $conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($conn) {
        $owner = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
        Write-Warning ("port $port is already in use by '$($owner.ProcessName)' (pid $($conn.OwningProcess), " +
            "path: $($owner.Path)) - NOT starting mf-cockpit. Stop that process first, then run this again.")
    } else {
        Start-Process (Join-Path $InstallDir 'mf-cockpit\mf-cockpit.exe') `
            -WorkingDirectory (Join-Path $InstallDir 'mf-cockpit') -WindowStyle Minimized
        Write-Host 'mf-cockpit started'
    }
}

# --- mf-watch -------------------------------------------------------------------
if (Get-Process mf-watch -ErrorAction SilentlyContinue) {
    Write-Host 'mf-watch already running'
} else {
    Start-Process (Join-Path $InstallDir 'mf-watch\mf-watch.exe') `
        -ArgumentList '--project', $Project -WindowStyle Minimized
    Write-Host "mf-watch started on $Project"
}

Start-Sleep -Seconds 2
Start-Process $CockpitUrl
