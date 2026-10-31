param(
    [string]$RunName = ('wave-sway-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$ModBuild = '',
    [string]$SeaPackage = 'C:\VS mods\Sea\.claude\worktrees\codex-storm-waves\release\sea-0.4.7-pre.10.zip'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$ModBuild) { $ModBuild = Join-Path $root 'bin/Debug/mod' }
$run = Join-Path $root ('testserver/' + $RunName)
if (Test-Path -LiteralPath $run) { throw "Use a fresh RunName: $run" }
$vs = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Vintagestory'
New-Item -ItemType Directory -Path "$run/Mods/Hansa", "$run/Mods/Sea", "$run/ModConfig" -Force | Out-Null
Copy-Item -Path "$ModBuild/*" -Destination "$run/Mods/Hansa" -Recurse
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($SeaPackage, "$run/Mods/Sea")
# Read the existing test template; all resulting configs, saves and logs stay in this worktree.
$cfg = Get-Content -LiteralPath 'C:/VS mods/Hansa/testserver/serverconfig.json' -Raw | ConvertFrom-Json
$cfg.Port = 42597
$cfg.Upnp = $false
$cfg.AdvertiseServer = $false
$cfg.PassTimeWhenEmpty = $true
$cfg.ModPaths = @('Mods', "$run/Mods")
$cfg.WorldConfig.SaveFileLocation = "$run/Saves/wave-sway.vcdbs"
$cfg.WorldConfig.WorldName = 'Hansa wave sway regression'
$cfg | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath "$run/serverconfig.json" -Encoding UTF8
@{ Enabled = $true; Shutdown = $true; Only = 'wave-sway'; Ship = 'caravel' } | ConvertTo-Json | Set-Content -LiteralPath "$run/ModConfig/hansa-selftest.json" -Encoding UTF8
@{ WaveScale = 1; StormHeightScale = 1; SeaWindFrom = 0; SeaWindStrength = 0.5 } | ConvertTo-Json | Set-Content -LiteralPath "$run/ModConfig/sea.json" -Encoding UTF8
$deadline = (Get-Date).AddMinutes(15)
while ([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains 42597) {
    if ((Get-Date) -gt $deadline) { throw 'Shared port 42597 did not become free; no server started.' }
    Start-Sleep -Seconds 2
}
Write-Output "Starting isolated server: $run"
$server = Start-Process -FilePath "$vs/VintagestoryServer.exe" -WorkingDirectory $vs -ArgumentList @('--dataPath', ('"' + $run + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$run/console.log" -RedirectStandardError "$run/console-error.log"
try {
    $deadline = (Get-Date).AddMinutes(12)
    while (!$server.WaitForExit(1000)) {
        if ((Get-Date) -gt $deadline) { throw 'Wave sway self-test exceeded 12 minutes.' }
    }
} finally {
    # Only the process started above, never another job's server.
    if (!$server.HasExited) { Stop-Process -Id $server.Id -Force; $server.WaitForExit() }
}
Get-Content -LiteralPath "$run/Logs/server-main.log" | Select-String -Pattern 'SELFTEST|Exception|Error'
Write-Output "Server exited; traces: $run/Logs/wave-sway"
if (!(Select-String -LiteralPath "$run/Logs/server-main.log" -Pattern 'SELFTEST ALL OK' -Quiet)) { throw 'Self-test did not finish ALL OK.' }
