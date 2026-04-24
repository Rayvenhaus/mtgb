# Installing MTGB

> *The Ministry welcomes you.*
> *We have prepared the forms.*
> *There are only a few of them.*

---

## Requirements

- Windows 10 (version 1809 or later) or Windows 11
- x64 processor
- No .NET runtime required — MTGB is self-contained
- A SimplyPrint account with API access

---

## Two ways to install

MTGB ships as two packages with every release:

| Package        | File                       | Best for                                                        |
|----------------|----------------------------|-----------------------------------------------------------------|
| MSIX installer | `MTGB-v*-x64.msix`         | Most users — installs cleanly, appears in Add/Remove Programs   |
| Portable ZIP   | `MTGB-v*-x64-portable.zip` | Users who prefer no installation, or who hit SmartScreen issues |

Both are unsigned during the beta period. This is documented
and expected. Code signing is in progress.

---

## MSIX installer

### Step 1 — Download

Download `MTGB-v*-x64.msix` from the
[latest release](https://github.com/Rayvenhaus/mtgb/releases).

### Step 2 — Windows 10 only: enable sideloading

Windows 11 supports unsigned MSIX packages by default.
Windows 10 requires sideloading to be enabled first.

**Windows 10:**
Settings → Update & Security → For developers →
select **Sideload apps** → confirm when prompted.

You only need to do this once.

### Step 3 — Install

Double-click `MTGB-v*-x64.msix`.

### Step 4 — SmartScreen warning

Because MTGB is not yet code-signed, Windows will show
a SmartScreen warning:

> *"Windows protected your PC"*

This is expected during the beta period. To proceed:

1. Click **More info**
2. Click **Run anyway**

MTGB will then install normally.

> **Why does this happen?**
> Windows SmartScreen warns on software that has not been
> signed by a trusted certificate authority. MTGB is in the
> process of obtaining a code signing certificate.
> Once signed, this warning will not appear.
> The source code is fully open and auditable at
> https://github.com/Rayvenhaus/mtgb

### Step 5 — Launch

MTGB will appear in your Start menu.
On first launch it will run the Induction — a short setup
wizard that connects MTGB to your SimplyPrint account.

---

## Portable ZIP

No installation required. No SmartScreen warning.

### Step 1 — Download

Download `MTGB-v*-x64-portable.zip` from the
[latest release](https://github.com/Rayvenhaus/mtgb/releases).

### Step 2 — Extract

Extract the ZIP to any folder. A permanent location is
recommended — MTGB will run from wherever you put it.

Suggested locations:
- `C:\Program Files\MTGB\`
- `C:\Users\YourName\Apps\MTGB\`

### Step 3 — Launch

Run `MTGB.exe`.

On first launch it will run the Induction wizard.

### Step 4 — Optional: start with Windows

The portable version can still start with Windows.
During the Induction, enable **Start with Windows** on
the Standing Orders screen. MTGB will add itself to the
Windows startup registry automatically.

---

## Settings and data

MTGB stores all settings and history in:

```
%APPDATA%\MTGB\
```

Which resolves to something like:

```
C:\Users\YourName\AppData\Roaming\MTGB\
```

This directory contains:

| File               | Contents                                    |
|--------------------|---------------------------------------------|
| `appsettings.json` | All settings including your Organisation ID |
| `history.json`     | Notification history log                    |
| `logs\`            | Application logs, rotated daily             |

Your API key is **not** stored here — it is stored securely
in the Windows Credential Manager and never written to disk
in plain text.

---

## Uninstalling

### MSIX

Settings → Apps → MTGB → Uninstall.

Note: the `%APPDATA%\MTGB\` directory is not removed
automatically. Delete it manually if you want a clean removal.
This will be fixed in a future release.

### Portable

Delete the folder you extracted MTGB into.
Delete `%APPDATA%\MTGB\` if you want to remove settings and history.

If you enabled Start with Windows, remove the registry entry:

```
HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\MTGB
```

Or launch MTGB, go to Settings → Advanced, and disable
**Start with Windows** before deleting the folder.

---

## Updating

MTGB checks for updates automatically on startup and every
72 hours during non-quiet hours. When a new version is
available a toast notification will appear. Click it to
open the update window, download the new version, and install.

You can also check manually via the tray icon right-click menu.

---

## Known issues — beta

| Issue | Workaround |
|---|---|
| SmartScreen warning on MSIX install | Click More info → Run anyway, or use the portable ZIP |
| Windows 10 requires sideloading enabled | Settings → Update & Security → For developers → Sideload apps |
| Uninstaller does not remove `%APPDATA%\MTGB\` | Delete manually after uninstalling |
| OAuth2 login not yet available | Use API key authentication |

---

## Troubleshooting

**MTGB won't start after install**
Check `%APPDATA%\MTGB\logs\` for error details.
The log file is named by date — open the most recent one.

**Toast notifications not appearing**
Check Windows notification settings:
Settings → System → Notifications → ensure MTGB is allowed.

**Connection test fails during Induction**
- Verify your Organisation ID — it appears in your SimplyPrint
  URL: `simplyprint.io/panel/[your-id-here]/dashboard`
- Verify your API key — regenerate it in SimplyPrint →
  Settings → API if needed
- Check your firewall is not blocking outbound HTTPS

**SmartScreen won't let me proceed**
Use the portable ZIP instead — it has no SmartScreen warning.

---

## Getting help

Open a GitHub issue:
https://github.com/Rayvenhaus/mtgb/issues

Or find the community on the SimplyPrint Discord.

---

*MTGB — The Monitor That Goes Bing*
*Never leave a print behind.*
*No llamas were harmed in the installation of this software.*
