# MTG United setup: gets the Forge rules engine ready and builds the bridge the Unity client talks to.
# The Forge tab inside MTG United runs this script for you; you can also run it from the repository root:
#
#   powershell -ExecutionPolicy Bypass -File scripts\setup.ps1                      install (downloads + builds Forge)
#   powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Clean               reinstall from scratch
#   powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -ForgeInstall "D:\Games\Forge"
#                                                                                   use a Forge you already have
#   powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Uninstall [-KeepData]
#
# Re-running is safe; it skips what is already done. The chosen setup is written to mtgunited-forge.json.

param(
    [switch]$Clean,
    [string]$ForgeInstall = "",
    [switch]$Uninstall,
    [switch]$KeepData
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$ForgeDir = Join-Path $Root "forge"
$ExtDir = Join-Path $Root "forge-ext"          # assets folder used with an existing Forge (links to its res)
$ToolsDir = Join-Path $Root ".tools"
$Config = Join-Path $Root "mtgunited-forge.json"
# Forge commit the bridge was written against. Change both together if you update Forge.
$ForgeCommit = "87cb532e9f088958ff367d293dc98076a091f9cb"
$MavenVersion = "3.9.9"
$GsonVersion = "2.13.2"

function Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Fail($msg) { Write-Host ""; Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }
function Save-Config($obj) { $obj | ConvertTo-Json | Set-Content -Path $Config -Encoding UTF8 }

# Removes a directory junction without touching what it points to.
function Remove-Link($path) { if (Test-Path $path) { cmd /c rmdir "$path" | Out-Null } }

# ================================================================= uninstall
if ($Uninstall) {
    Step "Uninstalling the MTG United Forge setup"
    Remove-Link (Join-Path $ExtDir "res")
    if (Test-Path $ExtDir) { Remove-Item -Recurse -Force $ExtDir }
    if (Test-Path $ForgeDir) {
        if ($KeepData) {
            # Keep the bridge's data and log (forge\mtgunited-data); remove everything else.
            Get-ChildItem $ForgeDir -Force | Where-Object { $_.Name -ne "mtgunited-data" } | Remove-Item -Recurse -Force
        } else {
            Remove-Item -Recurse -Force $ForgeDir
        }
    }
    if (-not $KeepData) {
        $data = Join-Path $Root "mtgunited-data"
        if (Test-Path $data) { Remove-Item -Recurse -Force $data }
    }
    $target = Join-Path $Root "bridge\target"
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    if (Test-Path $ToolsDir) { Remove-Item -Recurse -Force $ToolsDir }
    if (Test-Path $Config) { Remove-Item -Force $Config }
    Step "Uninstalled. A Forge you pointed MTG United at was not touched."
    exit 0
}

# ================================================================= prerequisites
Step "Checking Java"
$java = Get-Command java -ErrorAction SilentlyContinue
if (-not $java) { Fail "Java 17 or newer is required. Install it with:  winget install EclipseAdoptium.Temurin.21.JDK   then restart MTG United." }
# java prints its version on stderr; going through cmd avoids PowerShell 5 treating that as an error.
$javaVersionLine = (cmd /c "java -version 2>&1" | Select-Object -First 1)
if ($javaVersionLine -match '"(\d+)') {
    $javaMajor = [int]$Matches[1]
    if ($javaMajor -lt 17) { Fail "Found Java $javaMajor. Forge needs Java 17+. Install: winget install EclipseAdoptium.Temurin.21.JDK" }
    Write-Host "Java $javaMajor OK"
}
if (-not (Get-Command javac -ErrorAction SilentlyContinue)) { Fail "Found a Java runtime but not the JDK (javac). Install: winget install EclipseAdoptium.Temurin.21.JDK" }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ================================================================= existing Forge
if ($ForgeInstall -ne "") {
    Step "Using the Forge already installed at $ForgeInstall"
    if (-not (Test-Path (Join-Path $ForgeInstall "res"))) { Fail "That folder has no 'res' folder. Pick the folder that contains forge.exe (or the Forge .jar) and 'res'." }
    $forgeJar = Get-ChildItem -Path $ForgeInstall -Filter "*jar-with-dependencies*.jar" -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $forgeJar) { $forgeJar = Get-ChildItem -Path $ForgeInstall -Filter "forge-gui-desktop*.jar" -File -ErrorAction SilentlyContinue | Select-Object -First 1 }
    if (-not $forgeJar) { Fail "Couldn't find Forge's program file (forge-gui-desktop-...-jar-with-dependencies.jar) in that folder." }
    Write-Host "Forge program: $($forgeJar.Name)"

    # Gson (the bridge's JSON library), downloaded once.
    New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
    $gson = Join-Path $ToolsDir "gson-$GsonVersion.jar"
    if (-not (Test-Path $gson)) {
        Step "Downloading Gson $GsonVersion"
        Invoke-WebRequest -Uri "https://repo1.maven.org/maven2/com/google/code/gson/gson/$GsonVersion/gson-$GsonVersion.jar" -OutFile $gson -UseBasicParsing
    }

    # Compile the bridge against this Forge (no Forge download or build needed).
    Step "Building the MTG United bridge against your Forge"
    $classes = Join-Path $Root "bridge\target\ext-classes"
    if (Test-Path $classes) { Remove-Item -Recurse -Force $classes }
    New-Item -ItemType Directory -Force -Path $classes | Out-Null
    $sources = Get-ChildItem -Recurse -Path (Join-Path $Root "bridge\src\main\java") -Filter *.java | ForEach-Object { '"' + $_.FullName.Replace('\', '/') + '"' }
    $argFile = Join-Path $Root "bridge\target\sources.txt"
    Set-Content -Path $argFile -Value $sources -Encoding ASCII
    & javac -encoding UTF-8 -proc:none -nowarn -d $classes -cp "$($forgeJar.FullName);$gson" "@$argFile"
    if ($LASTEXITCODE -ne 0) { Fail "The bridge doesn't compile against this Forge version. Use 'Install Forge' in the Forge tab instead (it downloads the version MTG United was made for)." }
    $extJar = Join-Path $Root "bridge\target\forge-bridge-ext.jar"
    & jar cf $extJar -C $classes .
    if ($LASTEXITCODE -ne 0) { Fail "Packaging the bridge failed." }

    # Our own assets folder: links to Forge's card data, keeps the bridge's settings out of your Forge folder.
    Step "Linking Forge's card data"
    New-Item -ItemType Directory -Force -Path $ExtDir | Out-Null
    Remove-Link (Join-Path $ExtDir "res")
    cmd /c mklink /J "$(Join-Path $ExtDir 'res')" "$(Join-Path $ForgeInstall 'res')" | Out-Null
    if (-not (Test-Path (Join-Path $ExtDir "res"))) { Fail "Couldn't link $ForgeInstall\res." }

    Save-Config @{ mode = "existing"; forgeDir = $ForgeInstall; forgeJar = $forgeJar.FullName; gson = $gson; bridgeJar = $extJar; assets = $ExtDir }
    Step "Done! MTG United will use your Forge at $ForgeInstall"
    exit 0
}

# ================================================================= bundled Forge (download + build)
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Fail "Git is not installed (GitHub Desktop's Git isn't on PATH). Install Git for Windows: winget install Git.Git" }

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
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        Expand-Archive -Path $zip -DestinationPath $ToolsDir -Force
        Remove-Item $zip
    }
}
Write-Host "Maven: $Mvn"

if ($Clean -and (Test-Path $ForgeDir)) {
    Step "Removing old Forge checkout (keeping MTG United's Forge data)"
    Get-ChildItem $ForgeDir -Force | Where-Object { $_.Name -ne "mtgunited-data" } | Remove-Item -Recurse -Force
    Get-ChildItem $ToolsDir -Filter "forge-installed-*" -ErrorAction SilentlyContinue | Remove-Item -Force
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
    # Update: if the pinned version changed (new MTG United release), move the checkout to it.
    Push-Location $ForgeDir
    $head = (git rev-parse HEAD 2>$null)
    if ($head -ne $ForgeCommit) {
        Step "Updating Forge to $($ForgeCommit.Substring(0,10))"
        git fetch --depth 1 origin $ForgeCommit
        if ($LASTEXITCODE -ne 0) { Pop-Location; Fail "git fetch failed." }
        git checkout -q FETCH_HEAD
    } else {
        Write-Host "Forge already downloaded (up to date)."
    }
    Pop-Location
}

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

Step "Building the MTG United bridge"
Push-Location (Join-Path $Root "bridge")
& $Mvn -B -U -q package
$code = $LASTEXITCODE
Pop-Location
if ($code -ne 0) { Fail "Bridge build failed (see output above). Please send the error to whoever is helping you." }

$jar = Join-Path $Root "bridge\target\forge-bridge-all.jar"
Save-Config @{ mode = "bundled"; forgeDir = $ForgeDir; bridgeJar = $jar; assets = (Join-Path $ForgeDir "forge-gui"); forgeCommit = $ForgeCommit }
Step "Done!"
Write-Host "Bridge: $jar"
Write-Host "Open MTG United and press Play; the client starts the bridge for you."
