param(
  [string]$SdkRoot = $env:PLASMA_SDK_ROOT,
  [string]$GameUiRoot = $env:PLASMA_GAMEUI_ROOT,
  [ValidateSet('Debug', 'Development', 'Shipping')][string]$Configuration = 'Development',
  [switch]$Run,
  [switch]$Test,
  [switch]$SkipAssetTransform
)
$ErrorActionPreference = 'Stop'

# The engine is, in order: -SdkRoot or PLASMA_SDK_ROOT, an engine checkout this project sits inside,
# or the engine the Plasma Launcher assigned to this project (recorded in plLauncher).
function Resolve-PlasmaSdk([string]$Root) {
  if ($Root) { return (Resolve-Path -LiteralPath $Root).Path }
  for ($dir = Get-Item -LiteralPath $PSScriptRoot; $dir; $dir = $dir.Parent) {
    if (Test-Path -LiteralPath (Join-Path $dir.FullName 'Code/Engine/Foundation/Basics.h')) { return $dir.FullName }
  }
  $launcherFile = Join-Path $PSScriptRoot 'plLauncher'
  $engines = Join-Path $env:APPDATA 'com.plasmatech.launcher/engines.json'
  if ((Test-Path -LiteralPath $launcherFile) -and (Test-Path -LiteralPath $engines)) {
    $version = (Get-Content -LiteralPath $launcherFile -Raw | ConvertFrom-Json).engine_version
    # Windows PowerShell returns a JSON array as one object; assigning it first lets the pipeline enumerate it.
    $installed = Get-Content -LiteralPath $engines -Raw | ConvertFrom-Json
    $engine = $installed | Where-Object { $_.version -eq $version } | Select-Object -First 1
    if ($engine) { return $engine.install_path }
  }
  throw 'Could not find the engine. Add this project in the Plasma Launcher, or pass -SdkRoot with the engine folder.'
}

# Editor/Packages.ddl is the list the editor installs from, so the build uses the same versions.
function Get-ProjectPackages {
  $ddl = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Editor/Packages.ddl') -Raw
  [regex]::Matches($ddl, '%Id\s*\{\s*"([^"]+)"\s*\}\s*(?:string\s*)?(?:%Version\s*\{\s*"([^"]+)"\s*\})?') |
    ForEach-Object { @{ Id = $_.Groups[1].Value; Version = $_.Groups[2].Value } }
}

$SdkRoot = Resolve-PlasmaSdk $SdkRoot
Write-Host "Engine: $SdkRoot"
if (-not (Test-Path -LiteralPath (Join-Path $SdkRoot 'Code/Engine/Foundation/Basics.h'))) { throw 'SdkRoot does not contain engine headers.' }
$gameUiVersion = (Get-ProjectPackages | Where-Object { $_.Id -eq 'plasma.gameui' } | Select-Object -First 1).Version
if (-not $gameUiVersion) { throw 'Editor/Packages.ddl must list plasma.gameui with a version.' }
$packageDirectory = Join-Path $PSScriptRoot "Plugins/plasma.gameui/$gameUiVersion"
if (-not $GameUiRoot -and -not (Test-Path (Join-Path $packageDirectory 'Package.plPackage'))) {
  # Engine developers keep package checkouts beside the engine.
  $localPackage = Join-Path (Split-Path $SdkRoot -Parent) 'LocalPackages/plasma.gameui'
  if (Test-Path (Join-Path $localPackage 'Package.plPackage')) { $GameUiRoot = $localPackage }
}
if ($GameUiRoot) {
  $GameUiRoot = (Resolve-Path -LiteralPath $GameUiRoot).Path
  if ((Get-Content (Join-Path $GameUiRoot 'Package.plPackage') -Raw) -notmatch ('%Version\s*\{\s*"' + [regex]::Escape($gameUiVersion) + '"')) { throw "This project requires GameUI $gameUiVersion." }
  $packageBinaries = Join-Path $GameUiRoot "Bin/Windows_x64_$Configuration"
  if (-not (Test-Path (Join-Path $packageBinaries 'plGameUiPlugin.lib'))) { throw "Build GameUI for $Configuration before building the template." }
  New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
  Copy-Item -LiteralPath (Join-Path $GameUiRoot 'Package.plPackage') -Destination $packageDirectory -Force
  foreach ($folder in @('Assets', 'Include')) {
    if (Test-Path (Join-Path $GameUiRoot $folder)) { Copy-Item -LiteralPath (Join-Path $GameUiRoot $folder) -Destination $packageDirectory -Recurse -Force }
  }
  # Source checkouts keep public headers under Source; installed packages use Include.
  $sourceHeaders = Join-Path $GameUiRoot 'Source/GameUiPlugin'
  if (Test-Path $sourceHeaders) {
    Get-ChildItem -LiteralPath $sourceHeaders -Recurse -File | Where-Object { $_.Extension -in '.h', '.inl' } | ForEach-Object {
      $relativeHeader = $_.FullName.Substring($sourceHeaders.Length).TrimStart('\', '/')
      $destination = Join-Path $packageDirectory "Include/GameUiPlugin/$relativeHeader"
      New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
      Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }
  }
  $stagedBinaries = Join-Path $packageDirectory "Bin/Windows_x64_$Configuration"
  New-Item -ItemType Directory -Path $stagedBinaries -Force | Out-Null
  Get-ChildItem -LiteralPath $packageBinaries -File | Where-Object { $_.Extension -in '.dll', '.lib', '.pdb' } | Copy-Item -Destination $stagedBinaries -Force
}
$stagedBinaries = Join-Path $packageDirectory "Bin/Windows_x64_$Configuration"
if (-not (Test-Path (Join-Path $stagedBinaries 'plGameUiPlugin.lib'))) { throw "GameUI $gameUiVersion is not installed. Open the project in the editor once and accept the package install, or pass -GameUiRoot with a built GameUI package." }
# The standalone executable resolves the imported GameUI DLL beside the engine binaries.
Copy-Item -LiteralPath (Join-Path $stagedBinaries 'plGameUiPlugin.dll') -Destination (Join-Path $SdkRoot "Binaries/Windows_$Configuration") -Force
$previousSdk = $env:PLASMA_SDK_ROOT
$env:PLASMA_SDK_ROOT = $SdkRoot
Push-Location (Join-Path $PSScriptRoot 'CppSource')
try {
  & plasmabuild generate workspace --workspace BrickBreaker --ide vs2026
  if ($LASTEXITCODE -ne 0) { throw 'Workspace generation failed.' }
  # This small review project favors a fresh link over stale incremental outputs.
  & plasmabuild clean BrickBreakerGame -c $Configuration -p Windows
  if ($LASTEXITCODE -ne 0) { throw 'Cleaning template build intermediates failed.' }
  & plasmabuild build BrickBreakerGame -c $Configuration -p Windows -a x64
  if ($LASTEXITCODE -ne 0) { throw 'Game build failed.' }
  $binaryDirectory = Join-Path $SdkRoot "Binaries/Windows_$Configuration"
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CppSource/BrickBreakerPlugin/BrickBreakerPlugin.plPluginBundle') -Destination $binaryDirectory -Force
  if ($Test) {
    & plasmabuild build BrickBreakerTests -c $Configuration -p Windows -a x64
    if ($LASTEXITCODE -ne 0) { throw 'Mechanics test build failed.' }
    & (Join-Path $binaryDirectory 'BrickBreakerTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Mechanics tests failed.' }
  }
  if (-not $SkipAssetTransform) {
    $processor = Join-Path $binaryDirectory 'EditorProcessor.exe'
    if (-not (Test-Path $processor)) { throw 'EditorProcessor is required to transform the pipeline and GameUI assets.' }
    $assets = 'RenderPipelines/BrickBreaker.plRenderPipelineAsset;UI/HudFont.plUiFontAsset;UI/HudData.plBlackboardTemplateAsset;UI/BrickBreakerHUD.plUiDocumentAsset;Scenes/Main.plScene'
    $transform = Start-Process -FilePath $processor -ArgumentList '-project', ('"' + $PSScriptRoot + '"'), '-transform', 'Default', '-assets', $assets, '-outputDir', ('"' + (Join-Path $PSScriptRoot 'AssetCache/TransformLog') + '"') -WindowStyle Hidden -Wait -PassThru
    if ($transform.ExitCode -ne 0) { throw "Asset transformation failed with code $($transform.ExitCode). See AssetCache/TransformLog." }
  }
  if ($Run) {
    & (Join-Path $binaryDirectory 'BrickBreakerGame.exe') -project $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { throw "Game exited with code $LASTEXITCODE." }
  }
} finally {
  Pop-Location
  $env:PLASMA_SDK_ROOT = $previousSdk
}
