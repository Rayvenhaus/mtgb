# ============================================================
# MTGB — MSIX Packaging Script
# Produces a signed or unsigned MSIX and a single-file
# portable ZIP from the publish output.
# Ministry of Printer Observation & Void Containment
# ============================================================

param(
    [string]$Version = "0.5.1.0",
    [switch]$Sign = $false,
    [string]$CertPath = "",
    [string]$CertPassword = ""
)

$ErrorActionPreference = "Stop"

# ── Resolve paths ─────────────────────────────────────────────
$repoRoot      = $PSScriptRoot
$projectDir    = Join-Path $repoRoot "src\MTGB"
$publishMsix   = Join-Path $projectDir "bin\Publish\MSIX"
$publishPortable = Join-Path $projectDir "bin\Publish\Portable"
$outputDir     = Join-Path $repoRoot "dist"
$msixPath      = Join-Path $outputDir "MTGB-$Version-x64.msix"
$zipPath       = Join-Path $outputDir "MTGB-$Version-x64-portable.zip"

# ── Locate MakeAppx ───────────────────────────────────────────
$makeAppx = $null
$signTool = $null

$sdkBin = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
if (Test-Path $sdkBin) {
    $makeAppx = Get-ChildItem $sdkBin -Recurse `
        -Filter "makeappx.exe" |
        Where-Object { $_.FullName -match "x64" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName

    $signTool = Get-ChildItem $sdkBin -Recurse `
        -Filter "signtool.exe" |
        Where-Object { $_.FullName -match "x64" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $makeAppx) {
    Write-Error "MakeAppx.exe not found. " +
        "Install Windows SDK 10.0.22621 or later."
    exit 1
}

Write-Host "============================================"
Write-Host " MTGB Packaging — v$Version"
Write-Host " The Ministry is preparing the distribution"
Write-Host "============================================"
Write-Host " Repo:     $repoRoot"
Write-Host " Project:  $projectDir"
Write-Host " MakeAppx: $makeAppx"
Write-Host ""

# ── Step 1 — Clean output directory ──────────────────────────
Write-Host "[1/7] Cleaning output directory..."
if (Test-Path $outputDir) {
    Remove-Item "$outputDir\*" -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}
Write-Host "      Done."

# ── Step 2 — Publish MSIX ────────────────────────────────────
Write-Host "[2/7] Publishing MSIX build..."
Push-Location $projectDir
dotnet publish -p:PublishProfile=MSIX --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Error "dotnet publish (MSIX) failed."
    exit 1
}
Pop-Location
Write-Host "      Done."

# ── Step 3 — Publish portable single-file ────────────────────
Write-Host "[3/7] Publishing portable single-file build..."
Push-Location $projectDir

if (Test-Path $publishPortable) {
    Remove-Item "$publishPortable\*" -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $publishPortable | Out-Null
}

dotnet publish `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $publishPortable `
    --nologo -v quiet

if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Error "dotnet publish (portable) failed."
    exit 1
}
Pop-Location
Write-Host "      Done."

# ── Step 4 — Prepare MSIX manifest ───────────────────────────
Write-Host "[4/7] Preparing package layout..."

$manifestSrc  = Join-Path $projectDir "Package.appxmanifest"
$manifestDest = Join-Path $publishMsix "AppxManifest.xml"

$manifest = Get-Content $manifestSrc -Raw
$manifest = $manifest -replace 'Version="[\d.]+"', `
    "Version=`"$Version`""

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText(
    $manifestDest, $manifest, $utf8NoBom)

Write-Host "      Manifest written — v$Version"

# ── Step 5 — Build MSIX ───────────────────────────────────────
Write-Host "[5/7] Building MSIX package..."
& $makeAppx pack /d $publishMsix /p $msixPath /overwrite /nv
if ($LASTEXITCODE -ne 0) {
    Write-Error "MakeAppx failed with exit code $LASTEXITCODE"
    exit 1
}
Write-Host "      MSIX created — $msixPath"

# ── Step 6 — Sign (optional) ──────────────────────────────────
if ($Sign) {
    Write-Host "[6/7] Signing MSIX..."
    if ([string]::IsNullOrEmpty($CertPath)) {
        Write-Error "CertPath is required when -Sign is specified."
        exit 1
    }
    if (-not $signTool) {
        Write-Error "SignTool.exe not found."
        exit 1
    }
    & $signTool sign /fd SHA256 /a /f $CertPath `
        /p $CertPassword $msixPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "SignTool failed with exit code $LASTEXITCODE"
        exit 1
    }
    Write-Host "      Signed."
} else {
    Write-Host "[6/7] Signing skipped — unsigned build."
    Write-Host "      Note: Users will need to enable sideloading"
    Write-Host "      or trust the certificate to install."
}

# ── Step 7 — Build portable ZIP ──────────────────────────────
Write-Host "[7/7] Building portable ZIP..."

$tempZipDir = Join-Path $outputDir "portable_temp"

if (Test-Path $tempZipDir) {
    Remove-Item $tempZipDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempZipDir | Out-Null
New-Item -ItemType Directory -Path "$tempZipDir\Assets" | Out-Null

# MTGB.exe
Copy-Item "$publishPortable\MTGB.exe" $tempZipDir

# appsettings.json
Copy-Item "$publishPortable\appsettings.json" $tempZipDir

# Assets — from publish output
Copy-Item "$publishPortable\Assets\countries.json" "$tempZipDir\Assets\"
Copy-Item "$publishPortable\Assets\mtgbNotification.wav" "$tempZipDir\Assets\"

# mtgb.ico — copy directly from source since publish doesn't include it
$icoSource = Join-Path $projectDir "Assets\mtgb.ico"
Copy-Item $icoSource "$tempZipDir\Assets\"

Compress-Archive -Path "$tempZipDir\*" `
    -DestinationPath $zipPath `
    -Force

Remove-Item $tempZipDir -Recurse -Force

Write-Host "      ZIP created — $zipPath"

# ── Done ──────────────────────────────────────────────────────
Write-Host ""
Write-Host "============================================"
Write-Host " Done. The Ministry has packaged MTGB."
$msixSize = [math]::Round(
    (Get-Item $msixPath).Length / 1MB, 1)
$zipSize  = [math]::Round(
    (Get-Item $zipPath).Length / 1MB, 1)
Write-Host " MSIX:     $msixSize MB — $msixPath"
Write-Host " Portable: $zipSize MB — $zipPath"
Write-Host " No llamas were harmed."
Write-Host "============================================"