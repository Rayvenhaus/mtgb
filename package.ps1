param(
    [string]$Version = "0.6.2-beta",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

Write-Warning "package.ps1 is deprecated. Use build-installer.ps1 directly for WiX/Burn releases."

$repoRoot = $PSScriptRoot
$buildScript = Join-Path $repoRoot "build-installer.ps1"

& $buildScript -Version $Version -Clean:$Clean
