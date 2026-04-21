# ============================================================
# MTGB — MSIX Packaging Script
# Produces a signed or unsigned MSIX from the publish output
# Ministry of Printer Observation & Void Containment
# ============================================================

param(
    [string]$Version = "0.4.1.0",
    [switch]$Sign = $false,
    [string]$CertPath = "",
    [string]$CertPassword = ""
)

$ErrorActionPreference = "Stop"

$makeAppx  = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\makeappx.exe"
$signTool  = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
$projectDir = "E:\GitHub\mtgb\src\MTGB"
$publishDir = "$projectDir\bin\Publish\MSIX"
$outputDir  = "E:\GitHub\mtgb\dist"
$msixPath   = "$outputDir\MTGB-$Version-x64.msix"
$zipPath    = "$outputDir\MTGB-$Version-x64-portable.zip"

Write-Host "============================================"
Write-Host " MTGB Packaging — v$Version"
Write-Host " The Ministry is preparing the distribution"
Write-Host "============================================"
Write-Host ""

# ── Step 1 — Clean output directory ──────────────────────────
Write-Host "[1/5] Cleaning output directory..."
if (Test-Path $outputDir) {
    Remove-Item "$outputDir\*" -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}
Write-Host "      Done."

# ── Step 2 — Publish ─────────────────────────────────────────
Write-Host "[2/5] Publishing MTGB..."
Push-Location $projectDir
dotnet publish -p:PublishProfile=MSIX --nologo -v quiet
Pop-Location
Write-Host "      Done."

# ── Step 3 — Copy manifest into publish output ────────────────
Write-Host "[3/5] Preparing package layout..."

# Update version in manifest
$manifestSrc  = "$projectDir\Package.appxmanifest"
$manifestDest = "$publishDir\AppxManifest.xml"
$manifest = Get-Content $manifestSrc -Raw
$manifest = $manifest -replace 'Version="[\d.]+"', "Version=`"$Version`""
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($manifestDest, $manifest, $utf8NoBom)

Write-Host "      Manifest written — v$Version"

# ── Step 4 — Build MSIX ───────────────────────────────────────
Write-Host "[4/5] Building MSIX package..."
& $makeAppx pack /d $publishDir /p $msixPath /overwrite /nv
if ($LASTEXITCODE -ne 0) {
    Write-Error "MakeAppx failed with exit code $LASTEXITCODE"
    exit 1
}
Write-Host "      MSIX created — $msixPath"

# ── Step 5 — Sign (optional) ──────────────────────────────────
if ($Sign) {
    Write-Host "[5/5] Signing MSIX..."
    if ([string]::IsNullOrEmpty($CertPath)) {
        Write-Error "CertPath is required when -Sign is specified"
        exit 1
    }
    & $signTool sign /fd SHA256 /a /f $CertPath /p $CertPassword $msixPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "SignTool failed with exit code $LASTEXITCODE"
        exit 1
    }
    Write-Host "      Signed."
} else {
    Write-Host "[5/5] Signing skipped — unsigned build."
    Write-Host "      Note: Users will need to enable sideloading"
    Write-Host "      or trust the certificate to install."
}

# ── Step 6 — Build portable ZIP ──────────────────────────────
Write-Host ""
Write-Host "[+]   Building portable ZIP..."
Compress-Archive -Path "$publishDir\*" `
    -DestinationPath $zipPath `
    -Force
Write-Host "      ZIP created — $zipPath"

# ── Done ──────────────────────────────────────────────────────
Write-Host ""
Write-Host "============================================"
Write-Host " Done. The Ministry has packaged MTGB."
$msixSize = [math]::Round((Get-Item $msixPath).Length / 1MB, 1)
$zipSize  = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host " MSIX:     $msixSize MB — $msixPath"
Write-Host " Portable: $zipSize MB — $zipPath"
Write-Host " No llamas were harmed."
Write-Host "============================================"