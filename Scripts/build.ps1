@"
 Build script - compile a mod DLL and copy to game mods directory
 Usage: .\Scripts\build.ps1 [ModName]
   Default: QuickPlay
"@

$modName = if ($args[0]) { $args[0] } else { "QuickPlay" }
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$modDir = Join-Path $rootDir "Mods" $modName
$gameModsDir = "D:\Program Files\Steam\steamapps\common\Slay the Spire 2\mods\$modName"

Write-Host "=== Building $modName ==="

# 1. Build
Set-Location $modDir
dotnet build -c Release --nologo 2>&1 | Out-String

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# 2. Ensure target directory exists
if (-not (Test-Path $gameModsDir)) {
    New-Item -ItemType Directory -Force -Path $gameModsDir | Out-Null
}

# 3. Copy DLL and manifest
$buildDll = Join-Path $modDir "bin\Release\net9.0\$modName.dll"
$manifest = Join-Path $modDir "mod_manifest.json"

if (Test-Path $buildDll) {
    Copy-Item $buildDll (Join-Path $gameModsDir "$modName.dll") -Force
    Copy-Item $manifest (Join-Path $gameModsDir "mod_manifest.json") -Force
    Write-Host "Deployed to: $gameModsDir" -ForegroundColor Green
} else {
    Write-Host "DLL not found: $buildDll" -ForegroundColor Red
    exit 1
}
