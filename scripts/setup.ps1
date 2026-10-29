# MTG United setup: downloads Forge (pinned version), builds its engine, then builds the bridge.
# Run from the repository root:   powershell -ExecutionPolicy Bypass -File scripts\setup.ps1
# Re-running is safe; it skips what is already done. Use -Clean to rebuild everything.

param(
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$ForgeDir = Join-Path $Root "forge"
$ToolsDir = Join-Path $Root ".tools"
# Forge commit the bridge was written against. Change both together if you update Forge.
$ForgeCommit = "87cb532e9f088958ff367d293dc98076a091f9cb"
$MavenVersion = "3.9.9"

function Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Fail($msg) { Write-Host ""; Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------- prerequisites
Step "Checking Git and Java"
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Fail "Git is not installed (GitHub Desktop's Git isn't on PATH). Install Git for Windows: winget install Git.Git" }

$java = Get-Command java -ErrorAction SilentlyContinue
if (-not $java) { Fail "Java 17 or newer is required. Install it with:  winget install EclipseAdoptium.Temurin.21.JDK   then open a new terminal." }
# java prints its version on stderr; going through cmd avoids PowerShell 5 treating that as an error.
$javaVersionLine = (cmd /c "java -version 2>&1" | Select-Object -First 1)
if ($javaVersionLine -match '"(\d+)') {
    $javaMajor = [int]$Matches[1]
    if ($javaMajor -lt 17) { Fail "Found Java $javaMajor. Forge needs Java 17+. Install: winget install EclipseAdoptium.Temurin.21.JDK" }
    Write-Host "Java $javaMajor OK"
}
if (-not (Get-Command javac -ErrorAction SilentlyContinue)) { Fail "Found a Java runtime but not the JDK (javac). Install: winget install EclipseAdoptium.Temurin.21.JDK" }

# ---------------------------------------------------------------- maven (downloaded locally if missing)
$mvn = Get-Command mvn -ErrorAction SilentlyContinue
if ($mvn) {
    $Mvn = "mvn"
} else {
    $MavenHome = Join-Path $ToolsDir "apache-maven-$MavenVersion"
    $Mvn = Join-Path $MavenHome "bin\mvn.cmd"
    if (-not (Test-Path $Mvn)) {
        Step "Downloading Maven $MavenVersion (build tool, stored in .tools\)"
        New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
        $zip = Join-Path $ToolsDir "maven.zip"
        $url = "https://archive.apache.org/dist/maven/maven-3/$MavenVersion/binaries/apache-maven-$MavenVersion-bin.zip"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        Expand-Archive -Path $zip -DestinationPath $ToolsDir -Force
        Remove-Item $zip
    }
}
Write-Host "Maven: $Mvn"

# ---------------------------------------------------------------- forge source
if ($Clean -and (Test-Path $ForgeDir)) {
    Step "Removing old Forge checkout"
    Remove-Item -Recurse -Force $ForgeDir
}
if (-not (Test-Path (Join-Path $ForgeDir ".git"))) {
    Step "Downloading Forge source at $($ForgeCommit.Substring(0,10)) (about 500 MB, mostly card scripts)"
    New-Item -ItemType Directory -Force -Path $ForgeDir | Out-Null
    Push-Location $ForgeDir
    git init -q
    git config core.longpaths true
    git remote add origin https://github.com/Card-Forge/forge.git
    git fetch --depth 1 origin $ForgeCommit
    if ($LASTEXITCODE -ne 0) { Pop-Location; Fail "git fetch failed." }
    git checkout -q FETCH_HEAD
    Pop-Location
} else {
    Write-Host "Forge already downloaded."
}

# ---------------------------------------------------------------- build forge engine
# Forge's poms use a ${revision} placeholder that is only resolved by the flatten plugin, which Forge
# runs at release time. We run it explicitly so the installed poms are usable by the bridge build.
$Flatten = "org.codehaus.mojo:flatten-maven-plugin:1.6.0:flatten"
$Marker = Join-Path $ToolsDir "forge-installed-$ForgeCommit"
if ($Clean -or -not (Test-Path $Marker)) {
    Step "Building the Forge engine (forge-core, forge-game, forge-ai, forge-gui). First build takes a few minutes."
    Push-Location $ForgeDir
    & $Mvn -B -q -N $Flatten install
    $code = $LASTEXITCODE
    if ($code -eq 0) {
        & $Mvn -B -q -pl forge-gui -am $Flatten install -DskipTests "-Dcheckstyle.skip=true"
        $code = $LASTEXITCODE
    }
    Pop-Location
    if ($code -ne 0) { Fail "Forge build failed (see output above)." }
    New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
    Set-Content -Path $Marker -Value "ok"
} else {
    Write-Host "Forge engine already built."
}

# ---------------------------------------------------------------- build bridge
Step "Building the MTG United bridge"
Push-Location (Join-Path $Root "bridge")
& $Mvn -B -U package
$code = $LASTEXITCODE
Pop-Location
if ($code -ne 0) { Fail "Bridge build failed (see output above). Please send the error to whoever is helping you." }

$jar = Join-Path $Root "bridge\target\forge-bridge-all.jar"
Step "Done!"
Write-Host "Bridge: $jar"
Write-Host ""
Write-Host "Next: open unity\MTGUnited in Unity Hub and press Play. The client starts the bridge for you."
Write-Host "Or start it yourself with:  scripts\start-bridge.bat"
