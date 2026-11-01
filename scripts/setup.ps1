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
    # Which Forge to build in bundled mode: "latest" (Forge's newest code, the default) or "tested" (the exact
    # version this MTG United release was tested with). "latest" falls back to "tested" if it doesn't build.
    [string]$ForgeVersion = "latest",
    [string]$ForgeInstall = "",
    [switch]$Uninstall,
    [switch]$KeepData
)

# MTG United reads this output as UTF-8 (so folders with accented letters are logged correctly).
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$ForgeDir = Join-Path $Root "forge"
$ExtDir = Join-Path $Root "forge-ext"          # assets folder used with an existing Forge (links to its res)
$ToolsDir = Join-Path $Root ".tools"
$Config = Join-Path $Root "mtgunited-forge.json"
# The Forge commit this MTG United release was tested with (the fallback when the newest Forge doesn't build).
$ForgeCommit = "87cb532e9f088958ff367d293dc98076a091f9cb"
$MavenVersion = "3.9.9"
$GsonVersion = "2.13.2"

function Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Fail($msg) { Write-Host ""; Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }
function Save-Config($obj) { $obj | ConvertTo-Json | Set-Content -Path $Config -Encoding UTF8 }

# The bridge never implements Forge's GUI interfaces directly (they change between Forge versions): it extends two
# small classes generated for the Forge in use (bridge/tools/CompatGen.java). Regenerate them for this classpath.
function Write-Compat($cp) {
    Step "Adapting the bridge to this Forge version"
    $genDir = Join-Path $ToolsDir "compatgen"
    New-Item -ItemType Directory -Force -Path $genDir | Out-Null
    Push-Location (Join-Path $Root "bridge\tools")
    & javac -encoding UTF-8 -nowarn -d $genDir "CompatGen.java" | Out-Host
    $e = $LASTEXITCODE
    Pop-Location
    if ($e -ne 0) { return $false }
    & java -cp $genDir CompatGen (Join-Path $Root "bridge\src\main\java\mtgunited\bridge") -cp $cp | Out-Host
    return ($LASTEXITCODE -eq 0)
}

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

    # Compile the bridge against this Forge (no Forge download or build needed). Any Forge version works: the
    # compatibility layer is generated from this Forge first.
    if (-not (Write-Compat "$($forgeJar.FullName);$gson")) { Fail "Couldn't read your Forge's program file. Is it a complete Forge (forge-gui-desktop ...jar-with-dependencies.jar)?" }
    Step "Building the MTG United bridge against your Forge"
    $classes = Join-Path $Root "bridge\target\ext-classes"
    if (Test-Path $classes) { Remove-Item -Recurse -Force $classes }
    New-Item -ItemType Directory -Force -Path $classes | Out-Null
    # Source paths relative to the source folder: they stay plain ASCII even when MTG United sits in a folder
    # with accented letters (an ASCII list of absolute paths mangled accented folder names and javac failed).
    $srcRoot = Join-Path $Root "bridge\src\main\java"
    $sources = Get-ChildItem -Recurse -Path $srcRoot -Filter *.java | ForEach-Object { '"' + $_.FullName.Substring($srcRoot.Length + 1).Replace('\', '/') + '"' }
    $argFile = Join-Path $Root "bridge\target\sources.txt"
    Set-Content -Path $argFile -Value $sources -Encoding ASCII
    Push-Location $srcRoot
    & javac -encoding UTF-8 -proc:none -nowarn -d $classes -cp "$($forgeJar.FullName);$gson" "@$argFile"
    $javacExit = $LASTEXITCODE
    Pop-Location
    if ($javacExit -ne 0) { Fail "The bridge doesn't compile against this Forge version (see the errors above; please report them). 'Install Forge' in the Forge tab builds a Forge that works." }
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
# Which Forge: the newest code on Forge's GitHub (default), or the version this release was tested with.
function Get-Target {
    if ($ForgeVersion -eq "tested") { return $ForgeCommit }
    if ($ForgeVersion -ne "latest") { return $ForgeVersion }        # a specific commit or tag
    $line = $null
    try { $line = (git ls-remote https://github.com/Card-Forge/forge.git HEAD | Select-Object -First 1) } catch { $line = $null }
    if (-not $line) { Write-Host "Couldn't ask GitHub for the newest Forge; using the tested version."; return $ForgeCommit }
    return ($line -split "\s+")[0]
}

function Checkout-Forge($commit) {
    if (-not (Test-Path (Join-Path $ForgeDir ".git"))) {
        Step "Downloading Forge source at $($commit.Substring(0,10)) (about 500 MB, mostly card scripts)"
        New-Item -ItemType Directory -Force -Path $ForgeDir | Out-Null
        Push-Location $ForgeDir
        git init -q | Out-Host
        git config core.longpaths true | Out-Host
        git remote add origin https://github.com/Card-Forge/forge.git | Out-Host
        Pop-Location
    }
    Push-Location $ForgeDir
    $head = (git rev-parse HEAD 2>$null)
    if ($head -ne $commit) {
        Step "Getting Forge $($commit.Substring(0,10))"
        git fetch --depth 1 origin $commit | Out-Host
        if ($LASTEXITCODE -ne 0) { Pop-Location; return $false }
        git checkout -q -f FETCH_HEAD | Out-Host
        if ($LASTEXITCODE -ne 0) { Pop-Location; return $false }
    } else {
        Write-Host "Forge already at $($commit.Substring(0,10))."
    }
    Pop-Location
    return $true
}

# Forge's version number (2.0.16-SNAPSHOT...), read from its pom, so the bridge links to what was just built.
function Get-ForgeRevision {
    [xml]$pom = Get-Content (Join-Path $ForgeDir "pom.xml") -Encoding UTF8
    $p = $pom.project.properties
    return "$($p.versionCode)$($p.snapshotName)"
}

# Forge's poms use a ${revision} placeholder that is only resolved by the flatten plugin, which Forge
# runs at release time. We run it explicitly so the installed poms are usable by the bridge build.
$Flatten = "org.codehaus.mojo:flatten-maven-plugin:1.6.0:flatten"

function Build-Forge($commit) {
    $marker = Join-Path $ToolsDir "forge-installed-$commit"
    if (-not $Clean -and (Test-Path $marker)) { Write-Host "Forge engine already built."; return $true }
    Step "Building the Forge engine (forge-core, forge-game, forge-ai, forge-gui). First build takes a few minutes."
    Push-Location $ForgeDir
    & $Mvn -B -q -N $Flatten install | Out-Host
    $code = $LASTEXITCODE
    if ($code -eq 0) {
        & $Mvn -B -q -pl forge-gui -am $Flatten install -DskipTests "-Dcheckstyle.skip=true" | Out-Host
        $code = $LASTEXITCODE
    }
    Pop-Location
    if ($code -ne 0) { return $false }
    New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
    Get-ChildItem $ToolsDir -Filter "forge-installed-*" -ErrorAction SilentlyContinue | Remove-Item -Force
    Set-Content -Path $marker -Value "ok"
    return $true
}

function Build-Bridge($revision) {
    Push-Location (Join-Path $Root "bridge")
    $cpFile = Join-Path $ToolsDir "bridge-classpath.txt"
    & $Mvn -B -U -q "-Dforge.version=$revision" dependency:build-classpath "-Dmdep.outputFile=$cpFile" | Out-Host
    $code = $LASTEXITCODE
    Pop-Location
    if ($code -ne 0) { return $false }
    if (-not (Write-Compat ((Get-Content $cpFile -Raw -Encoding UTF8).Trim()))) { return $false }
    Step "Building the MTG United bridge (Forge $revision)"
    Push-Location (Join-Path $Root "bridge")
    & $Mvn -B -U -q "-Dforge.version=$revision" package | Out-Host
    $code = $LASTEXITCODE
    Pop-Location
    return ($code -eq 0)
}

$target = Get-Target
$ok = (Checkout-Forge $target) -and (Build-Forge $target)
if ($ok) { $revision = Get-ForgeRevision; $ok = Build-Bridge $revision }
if (-not $ok -and $target -ne $ForgeCommit) {
    Step "The newest Forge didn't work with this MTG United; using the tested version instead"
    $target = $ForgeCommit
    $ok = (Checkout-Forge $target) -and (Build-Forge $target)
    if ($ok) { $revision = Get-ForgeRevision; $ok = Build-Bridge $revision }
}
if (-not $ok) { Fail "Building Forge or the bridge failed (see output above). Please send the error to whoever is helping you." }

$jar = Join-Path $Root "bridge\target\forge-bridge-all.jar"
Save-Config @{ mode = "bundled"; forgeDir = $ForgeDir; bridgeJar = $jar; assets = (Join-Path $ForgeDir "forge-gui"); forgeCommit = $target; forgeVersion = $revision }
Step "Done!"
Write-Host "Bridge: $jar"
Write-Host "Open MTG United and press Play; the client starts the bridge for you."
