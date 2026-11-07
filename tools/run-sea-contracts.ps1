# The open sea contracts against the real Sea on an isolated dedicated server (SelfTestSeaContracts.cs).
# -ModBuild: a Ships build folder with native/ShipsSea.dll (bin/Release/mod); -SeaPackage: a Sea 0.6.0-pre.1+ zip.
param(
    [string]$RunName = ('sea-contracts-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$ModBuild = '',
    [Parameter(Mandatory)][string]$SeaPackage
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
$cfg.WorldConfig.SaveFileLocation = "$run/Saves/sea-contracts.vcdbs"
$cfg.WorldConfig.WorldName = 'Ships sea contracts check'
# mostly ocean (vanilla's landcover, land kept at spawn), so the comparisons run where there are waves
$cfg.WorldConfig.WorldConfiguration | Add-Member -NotePropertyName landcover -NotePropertyValue '0.3' -Force
$cfg | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath "$run/serverconfig.json" -Encoding UTF8
@{ Enabled = $true; Shutdown = $true; Only = 'sea-contracts'; Ship = 'caravel' } | ConvertTo-Json | Set-Content -LiteralPath "$run/ModConfig/ships-selftest.json" -Encoding UTF8
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
        if ((Get-Date) -gt $deadline) { throw 'Sea contracts self-test exceeded 12 minutes.' }
    }
} finally {
    # Only the process started above, never another job's server.
    if (!$server.HasExited) { Stop-Process -Id $server.Id -Force; $server.WaitForExit() }
}
Get-Content -LiteralPath "$run/Logs/server-main.log" | Select-String -Pattern 'SELFTEST|Exception|Error'
Write-Output "Server exited: $run"
if (!(Select-String -LiteralPath "$run/Logs/server-main.log" -Pattern 'SELFTEST ALL OK' -Quiet)) { throw 'Self-test did not finish ALL OK.' }
