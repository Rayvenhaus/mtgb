param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$Version = "0.6.2-beta",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$appProject = Join-Path $repoRoot "src\MTGB\MTGB.csproj"
$installerProject = Join-Path $repoRoot "src\mtgbInstaller\mtgbInstaller.wixproj"
$bootstrapperProject = Join-Path $repoRoot "src\mtgbBootstrapper\mtgbBootstrapper.wixproj"
$publishProfile = "win-x64-singlefile"
$installerVersion = ($Version.TrimStart("v") -replace "-.*$", "")
$publishDir = Join-Path $repoRoot "src\MTGB\bin\x64\Release\publish\win-x64-singlefile"
$msiPath = Join-Path $repoRoot "src\mtgbInstaller\bin\Release\MTGB-$installerVersion-setup.msi"
$setupPath = Join-Path $repoRoot "src\mtgbBootstrapper\bin\x64\Release\MTGB-Setup.exe"
$distDir = Join-Path $repoRoot "dist"

function Invoke-Checked {
    param(
        [scriptblock]$Command,
        [string]$FailureMessage
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE"
    }
}

Write-Host "============================================"
Write-Host " MTGB installer build"
Write-Host " Version:       $Version"
Write-Host " MSI version:   $installerVersion"
Write-Host " Configuration: $Configuration"
Write-Host " Platform:      $Platform"
Write-Host "============================================"

if ($Clean) {
    Write-Host "[1/5] Cleaning build outputs..."
    Invoke-Checked { dotnet clean $appProject -c $Configuration -p:Platform=$Platform --nologo } "MTGB clean failed."
    Invoke-Checked { dotnet clean $installerProject -c $Configuration -p:Platform=$Platform --nologo } "MSI clean failed."
    Invoke-Checked { dotnet clean $bootstrapperProject -c $Configuration -p:Platform=$Platform --nologo } "Bootstrapper clean failed."
} else {
    Write-Host "[1/5] Clean skipped."
}

Write-Host "[2/5] Publishing MTGB single-file payload..."
Invoke-Checked {
    dotnet publish $appProject `
        -c $Configuration `
        -p:Platform=$Platform `
        -p:PublishProfile=$publishProfile `
        --nologo
} "MTGB publish failed."

if (-not (Test-Path (Join-Path $publishDir "MTGB.exe"))) {
    throw "Publish did not produce MTGB.exe at $publishDir"
}

Write-Host "[3/5] Building WiX MSI..."
Invoke-Checked {
    dotnet build $installerProject `
        -c $Configuration `
        -p:Platform=$Platform `
        -p:ProductVersion=$installerVersion `
        --nologo
} "MSI build failed."

if (-not (Test-Path $msiPath)) {
    throw "MSI was not produced at $msiPath"
}

Write-Host "[4/5] Building Burn bootstrapper..."
Invoke-Checked {
    dotnet build $bootstrapperProject `
        -c $Configuration `
        -p:Platform=$Platform `
        -p:ProductVersion=$installerVersion `
        --nologo
} "Bootstrapper build failed."

if (-not (Test-Path $setupPath)) {
    throw "Bootstrapper was not produced at $setupPath"
}

Write-Host "[5/5] Staging release artifacts..."
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

$setupOut = Join-Path $distDir "MTGB-v$Version-x64-Setup.exe"
$msiOut = Join-Path $distDir "MTGB-v$Version-x64.msi"

Copy-Item $setupPath $setupOut -Force
Copy-Item $msiPath $msiOut -Force

Write-Host ""
Write-Host "============================================"
Write-Host " Done. Installer artifacts staged:"
Write-Host " $setupOut"
Write-Host " $msiOut"
Write-Host "============================================"
