# Installing MTGB

MTGB is distributed as a Windows setup executable:

```text
MTGB-v0.6.2-beta-x64-Setup.exe
```

The setup program is a WiX Burn bootstrapper. It installs the .NET 8 Windows Desktop Runtime if needed, then installs MTGB through the bundled MSI.

## Requirements

- Windows 10 version 1809 or later, or Windows 11.
- x64 processor.
- A SimplyPrint account with API access.

The installer handles the required .NET Desktop Runtime.

## Install

1. Download the latest `MTGB-v*-x64-Setup.exe` from the GitHub release.
2. Run the setup executable.
3. Follow the installer prompts.
4. Launch MTGB from the Start Menu.
5. Complete the first-run Induction wizard.

MTGB installs by default to:

```text
C:\Program Files\MTGB
```

The installer allows a different install folder.

## Beta SmartScreen Notice

MTGB beta builds are not yet code-signed.

Windows may show:

```text
Windows protected your PC
```

To continue:

1. Click `More info`.
2. Click `Run anyway`.

This warning is expected until code signing is added.

## Installed Layout

The intended install layout is:

```text
[INSTALLFOLDER]\
    MTGB.exe
    data\
        assets\
            countries.json
            mtgbNotification.wav
    logs\
```

MTGB intentionally keeps its controlled runtime files under the selected install folder.

## First Run

On first launch, MTGB opens the Induction wizard.

Induction collects:

- SimplyPrint Organisation ID.
- SimplyPrint API key.
- Start with Windows preference.
- Optional anonymous telemetry preference.
- Optional community map registration.

The API key is stored in Windows Credential Manager.

Settings are written to:

```text
[INSTALLFOLDER]\data\appsettings.json
```

Logs are written to:

```text
[INSTALLFOLDER]\logs
```

Runtime assets are loaded from:

```text
[INSTALLFOLDER]\data\assets
```

## Uninstall

Uninstall MTGB from Windows Apps or Control Panel.

Target uninstall behavior is complete removal of MTGB-controlled traces:

- `MTGB.exe`
- `data`
- `logs`
- `data\assets`
- Start Menu shortcut
- MTGB installer registry keys
- MTGB startup registry entry
- MTGB Credential Manager entries
- MTGB community map or telemetry records through the community installation removal endpoint

Windows-owned traces such as Event Viewer records, Prefetch, Windows Installer cache metadata, Defender history, and WER reports are not MTGB-controlled and are not removed.

## Troubleshooting

### MTGB will not start

Check the newest log file in:

```text
[INSTALLFOLDER]\logs
```

If no log file exists, confirm that the installer created `data` and `logs` and that the current user can write to them.

### Country picker is empty

Confirm this file exists:

```text
[INSTALLFOLDER]\data\assets\countries.json
```

### Notification sound does not play

Confirm this file exists:

```text
[INSTALLFOLDER]\data\assets\mtgbNotification.wav
```

### Connection test fails during Induction

- Confirm the Organisation ID from the SimplyPrint URL:

```text
simplyprint.io/panel/[organisation-id]/dashboard
```

- Regenerate or re-copy the API key from SimplyPrint settings.
- Confirm outbound HTTPS is not blocked by firewall or proxy.

## Getting Help

Open an issue:

```text
https://github.com/Rayvenhaus/mtgb/issues
```

Include:

- MTGB version.
- Windows version.
- Whether this was a fresh install, upgrade, or uninstall.
- The newest log file from `[INSTALLFOLDER]\logs`, if available.
