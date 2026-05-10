# MTGB Release Checklist

This checklist is the shipping lane for MTGB `0.6.2 beta` and later patch builds.

The goal is not perfection. The goal is a Windows app that installs, runs, goes Bing, and uninstalls cleanly enough that the Ministry can deny everything.

## Version Policy

MTGB uses `major.minor.patch` for product versions.

Patch builds increment the last number:

```text
0.6.0 -> 0.6.1 -> 0.6.2
```

Pre-release tags may add a suffix:

```text
v0.6.2-beta
```

The Windows Installer product version must stay numeric. Do not put `beta` in the MSI `Version` field.

## Scope Freeze For 0.6.2 Beta

Ship only this:

- WPF app starts on Windows 10/11 x64.
- Burn installs .NET 8 Windows Desktop Runtime if missing.
- MSI installs MTGB under the selected install folder.
- First-run Induction opens on clean install.
- Induction connection test works.
- Induction saves settings to `data\appsettings.json`.
- Runtime assets load from `data\assets`.
- Logs write to `logs`.
- Tray icon appears.
- Start Menu shortcut launches MTGB.
- Uninstall removes MTGB-controlled files, folders, shortcuts, registry entries, credentials, and server records where implemented.

Everything else is after release unless it blocks one of those bullets.

## Local Build

Run from the repository root:

```powershell
.\build-installer.ps1 -Version "0.6.2-beta" -Clean
```

Expected artifacts:

```text
dist\MTGB-v0.6.2-beta-x64-Setup.exe
dist\MTGB-v0.6.2-beta-x64.msi
```

The setup EXE is the end-user artifact. The MSI is kept for diagnostics and advanced/manual installs.

## Fresh Install VM Test

Use a clean Windows 10 or Windows 11 x64 VM.

Verify:

- `MTGB-v0.6.2-beta-x64-Setup.exe` starts.
- .NET Desktop Runtime installs when missing.
- MTGB installs successfully.
- Install folder defaults to `C:\Program Files\MTGB`.
- Install folder contains only expected MTGB payload:
  - `MTGB.exe`
  - `data\assets\countries.json`
  - `data\assets\mtgbNotification.wav`
  - `logs`
- Start Menu shortcut is directly `MTGB`.
- Launch from Start Menu opens Induction.
- Country picker loads.
- Test Connection changes to `Test Passed` or `Test Failed`.
- Continue button text is readable.
- Summary shows API key as entered, verified, and secured.
- Settings are written to `data\appsettings.json`.
- Tray icon appears after Induction.
- Notification sound path works.
- No root `Assets` folder is required.
- No root `appsettings.json` is required.

## Uninstall VM Test

Uninstall MTGB from Windows Apps.

Verify MTGB-controlled traces are removed:

- Install folder.
- `data`.
- `logs`.
- Start Menu shortcut.
- MTGB installer registry keys.
- MTGB startup registry entry.
- MTGB Credential Manager entries.
- Community map registration, when server-side removal exists.
- Telemetry/server installation record, when server-side removal exists.

The MSI calls `MTGB.exe --cleanup-uninstall` on real uninstall only.
That cleanup mode removes current-user startup and credential traces,
then calls `DELETE /mtgb/v1/installations` when an install ID exists.

Do not try to scrub Windows-owned traces such as Prefetch, Event Viewer, Windows Installer cache metadata, Defender history, or WER reports.

## Upgrade Test

Install an older beta, complete Induction, then install the new beta over it.

Verify:

- Install succeeds.
- App launches after upgrade.
- `data\appsettings.json` survives the upgrade.
- Credential Manager API key survives the upgrade.
- History/logs survive unless the release explicitly says otherwise.
- Recursive cleanup does not run during upgrade.

## Release

Only tag after the VM install, uninstall, and upgrade checks pass.

### Tag-triggered release

```powershell
git tag v0.6.2-beta
git push origin v0.6.2-beta
```

### Manual release dispatch

GitHub also supports a manual release run from:

```text
Actions -> Release -> Run workflow
```

Use this version input:

```text
0.6.2-beta
```

GitHub Actions builds a draft prerelease containing:

```text
MTGB-v0.6.2-beta-x64-Setup.exe
MTGB-v0.6.2-beta-x64.msi
```

Publish the draft only after the release assets have been downloaded and smoke-tested.

After publishing the GitHub release, update the community release endpoint with the public setup URL.

Before using the setup-based release endpoint on an existing server, run:

```text
server\mtgb\v1\migrations\2026-05-10-release-info-setup-url.sql
```
