# ============================================================
# MTGB — MSIX Packaging Script
# Produces a signed or unsigned MSIX from the publish output
# Ministry of Printer Observation & Void Containment
# ============================================================

param(
    [string]$Version = "0.5.1.0",
    [switch]$Sign = $false,
    [string]$CertPath = "",
    [string]$CertPassword = ""
)

$ErrorActionPreference = "Stop"

# ── Resolve paths relative to script location ─────────────────
$repoRoot   = $PSScriptRoot
$projectDir = Join-Path $repoRoot "src\MTGB"
$publishDir = Join-Path $projectDir "bin\Publish\MSIX"
$outputDir  = Join-Path $repoRoot "dist"
$msixPath   = Join-Path $outputDir "MTGB-$Version-x64.msix"
$zipPath    = Join-Path $outputDir "MTGB-$Version-x64-portable.zip"

# ── Locate MakeAppx — search common SDK locations ─────────────
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
Write-Host " Repo:    $repoRoot"
Write-Host " Project: $projectDir"
Write-Host " MakeAppx: $makeAppx"
Write-Host ""

# ── Step 1 — Clean output directory ──────────────────────────
Write-Host "[1/6] Cleaning output directory..."
if (Test-Path $outputDir) {
    Remove-Item "$outputDir\*" -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}
Write-Host "      Done."

# ── Step 2 — Publish ─────────────────────────────────────────
Write-Host "[2/6] Publishing MTGB..."
Push-Location $projectDir
dotnet publish -p:PublishProfile=MSIX --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Error "dotnet publish failed."
    exit 1
}
Pop-Location
Write-Host "      Done."

# ── Step 3 — Prepare package layout ──────────────────────────
Write-Host "[3/6] Preparing package layout..."

$manifestSrc  = Join-Path $projectDir "Package.appxmanifest"
$manifestDest = Join-Path $publishDir "AppxManifest.xml"

$manifest  = Get-Content $manifestSrc -Raw
$manifest  = $manifest -replace 'Version="[\d.]+"', `
    "Version=`"$Version`""

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText(
    $manifestDest, $manifest, $utf8NoBom)

Write-Host "      Manifest written — v$Version"

# ── Step 4 — Build MSIX ───────────────────────────────────────
Write-Host "[4/6] Building MSIX package..."
& $makeAppx pack /d $publishDir /p $msixPath /overwrite /nv
if ($LASTEXITCODE -ne 0) {
    Write-Error "MakeAppx failed with exit code $LASTEXITCODE"
    exit 1
}
Write-Host "      MSIX created — $msixPath"

# ── Step 5 — Sign (optional) ──────────────────────────────────
if ($Sign) {
    Write-Host "[5/6] Signing MSIX..."
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
    Write-Host "[5/6] Signing skipped — unsigned build."
    Write-Host "      Note: Users will need to enable sideloading"
    Write-Host "      or trust the certificate to install."
}

# ── Step 6 — Build portable ZIP ──────────────────────────────
Write-Host "[6/6] Building portable ZIP..."
Compress-Archive -Path "$publishDir\*" `
    -DestinationPath $zipPath `
    -Force
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