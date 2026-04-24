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

- Native Windows toast notifications — rich, actionable, unobtrusive
- Monitors unlimited printers — the whole farm, every bay
- Real-time updates via SimplyPrint webhooks or configurable polling
- Per-printer, per-event notification controls
- Action buttons on toasts — Pause, Cancel directly from the notification
- Quiet hours — the Ministry respects your sleep
- Notification grouping — no floods, just useful summaries
- Print history log with stats
- Estimated completion countdowns live in the system tray
- Flyout dashboard — left-click the tray icon for a live overview
- Auto-update — MTGB checks for new versions and updates itself
- Anonymous telemetry — opt-in, no names, no IDs, just numbers
- Community map — optional anonymous dot on a map of MTGB installations
- Start with Windows — always watching, from the moment you log in
- API key or OAuth2 authentication

---

## Requirements

- Windows 10 (1809 or later) or Windows 11
- SimplyPrint account with API access
- No .NET runtime required — self-contained installer

---

## Installation

Download the latest release from the
[releases page](https://github.com/Rayvenhaus/mtgb/releases).

> **⚠️ Beta notice — unsigned installer**
> MTGB is currently in beta and the installer is not yet code-signed.
> Windows will show a SmartScreen warning. See [INSTALL.md](INSTALL.md)
> for full instructions including how to proceed and how to use the
> portable ZIP as an alternative.

Full installation instructions, troubleshooting, and uninstall
guidance: **[INSTALL.md](INSTALL.md)**

---

## Documentation

| Document | Contents |
|---|---|
| [INSTALL.md](INSTALL.md) | Installation, SmartScreen, portable ZIP, troubleshooting |
| [CONTRIBUTING.md](CONTRIBUTING.md) | How to contribute, code style, voice and tone |
| [TELEMETRY.md](TELEMETRY.md) | Full privacy and telemetry disclosure |
| [SECURITY.md](SECURITY.md) | Vulnerability reporting policy |
| [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) | Community standards |
| [CHANGELOG.md](CHANGELOG.md) | Release history |
| [THE_TRUTH.md](THE_TRUTH.md) | The origin story |

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

To regenerate icons and tile assets:

```powershell
.\icon.ps1
.\assets.ps1
```

---

## Contributing

Contributions are welcome. Translations especially so.
Please read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting a PR.

---

## License

MIT — see [LICENSE](LICENSE) for details.
OAuth2 client credentials are not part of this repository and are not
covered by this licence.

---

*Inspired by [Monty Python's](https://en.wikipedia.org/wiki/Monty_Python) "The Machine That Goes Ping".*
*Philosophically indebted to Sir Terry Pratchett.*
*No llamas were harmed in the making of this software.*
