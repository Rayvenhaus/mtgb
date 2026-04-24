# MTGB — The Monitor That Goes Bing

> *It monitors your prints. It goes Bing. Never leave a print behind.*

A Windows 10/11 system tray notification app for [SimplyPrint](https://simplyprint.io) —
because nobody expects a notification app. Or a Spanish Inquisition.

[![Release](https://img.shields.io/github/v/release/Rayvenhaus/mtgb?include_prereleases&label=release)](https://github.com/Rayvenhaus/mtgb/releases)
[![License](https://img.shields.io/github/license/Rayvenhaus/mtgb)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](https://github.com/Rayvenhaus/mtgb/releases)

---

## What is MTGB?

MTGB sits quietly in your system tray, watching your SimplyPrint printers in
real time. When something happens — print finished, print failed, printer went
offline, filament running low, spaghetti detected — it goes Bing.

That's what it does. The Ministry handles the rest.

---

## Features

- **Native Windows toast notifications** — rich, actionable, unobtrusive
- **Monitors unlimited printers** — the whole farm, every bay
- **Real-time updates** via SimplyPrint webhooks or configurable polling
- **Per-printer, per-event controls** — choose exactly which Bings you want
- **Action buttons on toasts** — Pause, Cancel directly from the notification
- **Quiet hours** — the Ministry respects your sleep
- **Notification grouping** — no floods, just useful summaries
- **Print history log** — everything that went Bing, with stats
- **Estimated completion countdowns** — live in the system tray
- **Flyout dashboard** — left-click the tray icon for a live overview
- **Auto-update** — MTGB checks for new versions and updates itself
- **Anonymous telemetry** — opt-in, no names, no IDs, just numbers
- **Community map** — optional anonymous dot on a map of MTGB installations
- **Start with Windows** — always watching, from the moment you log in
- **API key or OAuth2** authentication

---

## Requirements

- Windows 10 (1809 or later) or Windows 11
- SimplyPrint account with API access
- No .NET runtime required — self-contained installer

---

## Installation

### MSIX Installer (recommended)

1. Download `MTGB-v*-x64.msix` from the [latest release](https://github.com/Rayvenhaus/mtgb/releases)
2. Double-click the file to install

> **⚠️ Beta notice — unsigned installer**
>
> MTGB is currently in beta and the installer is not yet code-signed.
> Windows will show a SmartScreen warning when you run it.
>
> To proceed:
> - Click **More info**
> - Click **Run anyway**
>
> Code signing is in progress. This warning will disappear in a future release.
>
> **Windows 10 users** — you may need to enable sideloading first:
> Settings → Update & Security → For developers → Sideload apps

### Portable ZIP (no installation required)

1. Download `MTGB-v*-x64-portable.zip` from the [latest release](https://github.com/Rayvenhaus/mtgb/releases)
2. Extract to any folder
3. Run `MTGB.exe`

The portable version requires no installation and has no SmartScreen warning.
Settings are stored in `%APPDATA%\MTGB\`.

---

## First run — The Induction

On first launch MTGB will run the Induction — a short setup wizard that
collects your SimplyPrint Organisation ID and API key, and lets you configure
your standing orders.

Your API key is stored securely in the Windows Credential Manager.
It is never written to disk in plain text.

---

## Known issues — beta

| Issue | Status |
|---|---|
| Installer is unsigned — SmartScreen warning on install | Pending Certum code signing |
| OAuth2 login not yet available | Pending SimplyPrint approval |
| Uninstaller does not clean up `%APPDATA%\MTGB\` | Fix planned for v1.0.0 |

---

## Building from source

```powershell
git clone https://github.com/Rayvenhaus/mtgb.git
cd mtgb\src\MTGB
dotnet build -c Release
```

To produce a distributable MSIX and portable ZIP:

```powershell
.\package.ps1 -Version "0.5.3.0"
```

To regenerate icons:

```powershell
.\icon.ps1
```

To regenerate MSIX tile assets:

```powershell
.\assets.ps1
```

---

## Contributing

Contributions are welcome. Translations especially so.
Please read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting a PR.

---

## Privacy and telemetry

MTGB includes optional anonymous telemetry — **off by default**.
No names, no API keys, no printer names, no job files.
Just version numbers, printer counts, and event type statistics.

Full details in [TELEMETRY.md](TELEMETRY.md).

---

## License

MIT — see [LICENSE](LICENSE) for details.
OAuth2 client credentials are not part of this repository and are not
covered by this licence.

---

*Inspired by [Monty Python's](https://en.wikipedia.org/wiki/Monty_Python) "The Machine That Goes Ping".*
*Philosophically indebted to Sir Terry Pratchett.*
*No llamas were harmed in the making of this software.*