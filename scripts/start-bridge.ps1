# Starts the Forge bridge in this window so you can see its log. The Unity client connects to it on port 7777.
# Works with either setup chosen in the Forge tab (MTG United's Forge, or your own Forge).
param(
    [int]$Port = 7777,
    [switch]$VerboseLog
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $Root "mtgunited-forge.json"
if (Test-Path $configPath) {
    $cfg = Get-Content $configPath -Raw | ConvertFrom-Json
} else {
    $cfg = [pscustomobject]@{ mode = "bundled"; bridgeJar = (Join-Path $Root "bridge\target\forge-bridge-all.jar"); assets = (Join-Path $Root "forge\forge-gui") }
}
if (-not (Test-Path $cfg.bridgeJar)) { Write-Host "Bridge not built yet. Set up Forge in MTG United's Forge tab (or run scripts\setup.bat)." -ForegroundColor Red; exit 1 }
if (-not (Test-Path (Join-Path $cfg.assets "res"))) { Write-Host "Forge's card data wasn't found. Set up Forge in the Forge tab." -ForegroundColor Red; exit 1 }

if ($cfg.mode -eq "existing") {
    $cp = "$($cfg.bridgeJar);$($cfg.gson);$($cfg.forgeJar)"
    $javaArgs = @("-Xmx3g", "-cp", $cp, "mtgunited.bridge.BridgeMain", "--forge-gui", $cfg.assets, "--port", $Port)
} else {
    $javaArgs = @("-Xmx3g", "-jar", $cfg.bridgeJar, "--forge-gui", $cfg.assets, "--port", $Port)
}
if ($VerboseLog) { $javaArgs += "--verbose" }
& java @javaArgs
