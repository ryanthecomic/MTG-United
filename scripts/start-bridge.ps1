# Starts the Forge bridge in this window so you can see its log. The Unity client connects to it on port 7777.
param(
    [int]$Port = 7777,
    [switch]$VerboseLog
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$jar = Join-Path $Root "bridge\target\forge-bridge-all.jar"
$forgeGui = Join-Path $Root "forge\forge-gui"
if (-not (Test-Path $jar)) { Write-Host "Bridge not built yet. Run scripts\setup.bat first." -ForegroundColor Red; exit 1 }
if (-not (Test-Path (Join-Path $forgeGui "res"))) { Write-Host "Forge not downloaded yet. Run scripts\setup.bat first." -ForegroundColor Red; exit 1 }

$javaArgs = @("-Xmx3g", "-jar", $jar, "--forge-gui", $forgeGui, "--port", $Port)
if ($VerboseLog) { $javaArgs += "--verbose" }
& java @javaArgs
