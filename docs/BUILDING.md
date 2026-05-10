# Building MTGB

MTGB is a Windows 10/11 WPF tray app packaged with WiX v7.

The current delivery model is:

```text
framework-dependent single-file publish
-> WiX MSI
-> WiX Burn bootstrapper
```

The bootstrapper installs the .NET 8 Windows Desktop Runtime when it is missing.

## Prerequisites

- Windows 10/11.
- .NET SDK capable of building `net8.0-windows10.0.19041.0`.
- WiX v7 SDK packages restored by NuGet.
- The .NET 8 Windows Desktop Runtime redistributable in:

```text
src\mtgbBootstrapper\redist\windowsdesktop-runtime-8.0.26-win-x64.exe
```

## One Command Build

Run from the repository root:

```powershell
.\build-installer.ps1 -Version "0.6.2-beta" -Clean
```

The script performs the required build order:

1. Optionally cleans project outputs.
2. Publishes MTGB using `win-x64-singlefile`.
3. Builds the WiX MSI.
4. Builds the Burn bootstrapper.
5. Copies release artifacts into `dist`.

## Manual Build Order

Use this only when debugging individual stages:

```powershell
dotnet publish src\MTGB\MTGB.csproj -c Release -p:Platform=x64 -p:PublishProfile=win-x64-singlefile
dotnet build src\mtgbInstaller\mtgbInstaller.wixproj -c Release -p:Platform=x64
dotnet build src\mtgbBootstrapper\mtgbBootstrapper.wixproj -c Release -p:Platform=x64
```

Do not rely on Visual Studio Build Solution for release artifacts until the solution/platform mapping and pipeline are finalized.

`package.ps1` is retained only as a compatibility wrapper. New release work should call `build-installer.ps1`.

## Outputs

Published app payload:

```text
src\MTGB\bin\x64\Release\publish\win-x64-singlefile\MTGB.exe
```

MSI:

```text
src\mtgbInstaller\bin\Release\MTGB-0.6.2-setup.msi
```

Burn setup:

```text
src\mtgbBootstrapper\bin\x64\Release\MTGB-Setup.exe
```

Staged release artifacts:

```text
dist\MTGB-v0.6.2-beta-x64-Setup.exe
dist\MTGB-v0.6.2-beta-x64.msi
```
