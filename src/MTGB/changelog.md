# Changelog

All notable changes to MTGB — The Monitor That Goes Bing will be 
documented here.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.2.16] — 17/04/2026
### The one where it actually goes Bing.

### Added
- Diagnostic logging added to `NotificationManager.ProcessEventsAsync`
  — event processing and suppression state now logged for debugging

### Fixed
- Notification grouping flush — grouping disabled for initial testing,
  confirmed toasts fire correctly without grouping. Grouping flush
  timer fix deferred to next patch.
- Printing status indicator colour changed from amber to blue `#378ADD`
  — amber reserved for warnings, blue for active healthy printing state
  updated in `GetStatusInfo` and `UpdateBingDot` in `FlyoutWindow.xaml.cs`

### Confirmed working — end to end
- Toast notifications appearing as banners with audio
- Flavour text delivering correctly on every event type
- MTGB icon and "It goes Bing" attribution on every toast
- Grouped toasts firing correctly across multiple printers
- Notification centre history populated and persistent
- Polling worker detecting real printer state changes
- Diff engine comparing snapshots and firing correct events
- Rules engine passing events through correctly
- The machine goes Bing. This is what it does.
- Never leave a print behind.

---

## [0.2.14] — Unreleased
### The one where the settings window stops bleeding everywhere.

### Fixed
- Settings window scroll bleed-through — `ClipToBounds="True"` 
  added to TabControl content Border and SectionCard style.
  Rounded corners no longer bleed over content when scrolling.
- Account tab dead space removed — ConnectionStatusPanel 
  inline styles with zero bottom margin, ConnectButtonPanel 
  named and margin corrected. Disconnect button and connected
  status now render cleanly without gaps.
- Flyout hover flavour text no longer cycles on mouse move —
  text cached per printer per state, only re-picked when 
  printer state actually changes.

---

## [0.2.13] — 17/04/2026
### The one where it actually connects.

### Added
- Full Settings window implementation — six tabs, fully wired:
  Account, Printers, Notifications, Quiet hours, Advanced, About
- API key authentication with connection test and secure storage
- Per-printer enable/disable toggles populated from live API data
- Full event pick list with Pythonesque flavour descriptions
- Quiet hours configuration with allow-critical override
- Polling interval, webhook toggle, startup with Windows, language
- Danger zone — clear notification history
- About tab with origin quote, GitHub link and NHS line
- Save/Cancel footer with unsaved changes detection
- Dark title bar via `DwmSetWindowAttribute` on window load
- First successful authenticated connection to SimplyPrint API
- Real printer data flowing into flyout panel

### Fixed
- URL construction — `OrgUrl` builds absolute URLs directly,
  removing `HttpClient` base address ambiguity
- `IOptions<AppSettings>` snapshot issue — org ID passed directly
  to `TestConnectionAsync` bypassing stale snapshot which was 
  still 0 at test time
- Settings window content clipping — Disconnect button and footer
  now visible
- WPF media bar on SettingsWindow — `DwmSetWindowAttribute`
  called per-window in `OnLoaded`, not globally in `App.OnStartup`
  where no windows exist yet
- `CS0414` warning on `_isLoading` suppressed via `#pragma`
- Null reference exceptions during settings load — null guards
  added to `UpdateAuthPanels`, `UpdateQuietHoursPanels`,
  `UpdateWebhookPortPanel`, `MarkDirty` and `SetFooterStatus`

---

## [0.2.12] — 17/04/2026
### The one where the right-click menu stops being funky.

### Added
- `TrayContextMenu.xaml` — fully custom dark brass context menu
  with `ControlTemplate` overrides bypassing WPF visual tree
  isolation. Dark background, brass gold header, red Exit item,
  correct separator styling.
- `App.xaml` complete dark theme foundation — MTGB Art Deco 
  palette as global resources, `SystemColors` overrides for 
  menus, highlights and controls, global styles for all WPF 
  control types. Dark throughout regardless of Windows theme.
- `app.manifest` — Windows 10/11 compatibility declaration and
  Per Monitor V2 DPI awareness for crisp high-DPI rendering.

### Fixed
- Right-click context menu rendering — custom `ControlTemplate`
  with hardcoded MTGB colours bypasses WPF popup visual tree
  isolation
- `prefersDarkMode` removed from manifest — not a valid Windows
  app manifest element
- `XDG0046` — `SystemColors` keys moved to root 
  `ResourceDictionary`
- `XDG0012` — `ToolTipBrushKey` removed, tooltip theming via
  `Style` instead

---

## [0.2.11] — 17/04/2026
### The one where the flyout stops falling off the screen.

### Fixed
- Flyout positioning — `ContentRendered` event guarantees 
  `ActualHeight` is available before positioning above taskbar
- Flyout only opening once — `IsLoaded` check with animation
  replay on subsequent `Show()` calls
- Duplicate `PositionAboveTaskbar` method removed
- WPF transparency chrome / media bar — `WS_EX_TOOLWINDOW`
  applied on `SourceInitialized`

---

## [0.2.10] — 17/04/2026
### The one where it boots without crashing.

### Fixed
- `CS7036` — `App` constructor removed, `SetServiceProvider` 
  pattern adopted, WPF instantiated before host services
- STA thread violation — background services launched via 
  `Task.Run`, WPF owns the STA thread from first line of `Main`
- `NETSDK1135` — TFM updated to `net8.0-windows10.0.19041.0`
- `StartupObject` set to `MTGB.Program` to override WPF-generated
  `App.Main` entry point
- `System.IO` using directive added to `Program.cs` and 
  `TrayIcon.cs`
- `NETSDK1022` duplicate Page items — SDK auto-includes XAML,
  manual `ItemGroup` removed
- Empty `.xaml` placeholder files created for `FlyoutWindow`,
  `SettingsWindow` and `HistoryWindow`

---

## [0.2.9] — 16/04/2026
### The one where it gets a face.

### Added
- `TrayIcon.cs` — system tray presence with left-click flyout,
  right-click minimal menu, icon state loop synced to printer
  farm status every 5 seconds, contextual hover flavour text
  pools, toast action routing to API client
- `FlyoutWindow.xaml` — Art Deco dark brass flyout panel:
  - Dark background `#0D0D0F` with brass/gold `#C9930E` accents
  - Animated slide-up from taskbar with cubic ease and fade
  - Live printer status cards with progress bars and filenames
  - Hover tooltips with contextual flavour text in Courier New
  - Mute toggle with animated thumb
  - `MUTED · BLESSED SILENCE` header state
  - History, Settings, Dashboard, Exit action buttons
  - Footer showing last poll time and webhook status
  - Georgia serif for Art Deco headings, Courier New for status,
    Segoe UI Variable for UI chrome
- Placeholder stubs for `SettingsWindow` and `HistoryWindow`

---

## [0.2.8] — 16/04/2026
### The one where it goes Bing with feeling.

### Added
- `NotificationManager.cs` — full rules engine:
  global mute → quiet hours → per-printer → per-event type →
  deduplication (30s window) → grouping → Windows 11 toast
- Notification history log — all events recorded to
  `%APPDATA%\MTGB\history.json`, capped at 1000 entries,
  survives app restarts
- Toast action buttons — Pause, Resume, Cancel wired to API
  client via `ToastActionRequested` event
- Rotating flavour message pools for every notification type.
  Highlights include:
  - *"It is not dead, it's resting. (It's dead.)"*
  - *"Step away from the printer."*
  - *"Someone may have forgotten the bol for the Spag bol."*
  - *"We apologise for the inconvenience."*
  - *"Blessed silence."* — on mute

---

## [0.2.7] — 16/04/2026
### The one where it watches.

### Added
- `PollingWorker.cs` — .NET 8 `BackgroundService` polling
  `/printers/Get` on configurable interval, minimum 10s floor.
  Progressive backoff on failure: 30s → 60s → 120s → 300s → 600s.
  Skips gracefully when not authenticated. The Ministry is
  cautiously optimistic when polling recovers.
- `WebhookWorker.cs` — local `HttpListener` receiving real-time
  event POSTs from SimplyPrint. Auto-registers and deregisters
  webhook on startup/shutdown. Double-Secret Validation™ on every
  payload via `X-SP-Token` constant-time comparison. Responds
  `"Bing."` on successful receipt.

---

## [0.2.6] — 16/04/2026
### The one where it understands what it's looking at.

### Added
- `StateDiffEngine.cs` — in-memory snapshot engine detecting
  printer state changes: online/offline, print started/finished/
  failed/paused/resumed/cancelled, progress milestones 25/50/75%,
  filament sensor triggers, temperature drops. Milestone
  deduplication per job ID via `HashSet`. Temperature alert
  cooldown 10 minutes per printer.

---

## [0.2.5] — 16/04/2026
### The one where it knows who you are.

### Added
- `AuthService.cs` — API key path validates against
  `/account/Test`, stores in Credential Manager. OAuth2 PKCE
  path via `IdentityModel.OidcClient`, local `HttpListener`
  callback, branded confirmation page that says *"It goes Bing."*
  OAuth2 client registration submitted to SimplyPrint.

---

## [0.2.4] — 16/04/2026
### The one where it talks to SimplyPrint.

### Added
- `SimplyPrintApiClient.cs` — typed HTTP client wrapping all
  required SimplyPrint endpoints: printer info, pause, resume,
  cancel, webhook registration/deletion. Handles API key and
  OAuth2 auth headers. Strongly typed response models via
  `System.Text.Json`.

---

## [0.2.3] — 16/04/2026
### The one where it knows what events mean.

### Added
- `AppSettings.cs` — strongly typed configuration model covering
  auth, polling, webhooks, notifications, quiet hours, per-printer
  overrides and UI preferences
- `EventDefinition.cs` and `EventRegistry` — dynamic event pick
  list driven by data not code, covering print jobs, printer state,
  progress milestones, temperature alerts, filament and queue events
- `appsettings.json` — default configuration with sensible defaults

---

## [0.2.2] — 16/04/2026
### The one where secrets are kept properly.

### Added
- `CredentialManager.cs` — Windows Credential Manager wrapper
  via Win32 P/Invoke. Credentials encrypted by Windows, scoped
  to current user, survive reinstalls. Double-Secret Validation™.
- `WebhookSecretManager` — cryptographically secure per-install
  webhook secret generated on first run via
  `RandomNumberGenerator`. Never plain text. Never committed.
  If wrong, someone updates their CV at 0300.

---

## [0.2.1] — 15/04/2026
### The one where it has a face, an icon, and a story.

### Added
- Art Deco 1930s brass and mahogany wood panel icon — generated
  via Leonardo.ai, assembled into multi-resolution `mtgb.ico`
  via ImageMagick (16, 32, 48, 256px)
- `THE_TRUTH.md` — canonical origin story written in the
  tradition of Monty Python and Sir Terry Pratchett. Filed once.
  Filed again. Filed a third time.
- `CHANGELOG.md` — because apparently we needed one of these
- Versioning policy established — PATCH/MINOR/MAJOR/pre-release

---

## [0.2.0] — 15/04/2026
### The one where the scaffold goes up.

### Added
- Full project scaffold — solution, `.csproj`, folder structure
- MIT licence — the community may do as they wish, within reason
- `README.md` — including the full origin story
- `.gitignore` and `.gitattributes` for .NET WPF projects
- `Program.cs` — entry point and DI wiring via .NET 8 Generic Host
- `App.xaml` and `App.xaml.cs` — WPF application with exception
  handling and startup flow

### Technical notes
- Target framework: `net8.0-windows10.0.19041.0`
- Minimum supported OS: Windows 10 1809 (10.0.17763.0)
- Architecture: x64

---

## [0.1.0] — 14/04/2026
### The one where it exists.

### Added
- Repository created
- Private GitHub repo established
- `THE_TRUTH.md` concept established
- MTGB named — The Monitor That Goes Bing
- Monty Python and Terry Pratchett writing style adopted as canon
- Double-Secret Validation™ coined
- The Redundant Department of Redundancy notified. Twice.
  In triplicate.

---

[Unreleased]: https://github.com/rayvenhaus/mtgb/compare/v0.2.14...HEAD
[0.2.14]: https://github.com/rayvenhaus/mtgb/compare/v0.2.13...v0.2.14
[0.2.13]: https://github.com/rayvenhaus/mtgb/compare/v0.2.12...v0.2.13
[0.2.12]: https://github.com/rayvenhaus/mtgb/compare/v0.2.11...v0.2.12
[0.2.11]: https://github.com/rayvenhaus/mtgb/compare/v0.2.10...v0.2.11
[0.2.10]: https://github.com/rayvenhaus/mtgb/compare/v0.2.9...v0.2.10
[0.2.9]: https://github.com/rayvenhaus/mtgb/compare/v0.2.8...v0.2.9
[0.2.8]: https://github.com/rayvenhaus/mtgb/compare/v0.2.7...v0.2.8
[0.2.7]: https://github.com/rayvenhaus/mtgb/compare/v0.2.6...v0.2.7
[0.2.6]: https://github.com/rayvenhaus/mtgb/compare/v0.2.5...v0.2.6
[0.2.5]: https://github.com/rayvenhaus/mtgb/compare/v0.2.4...v0.2.5
[0.2.4]: https://github.com/rayvenhaus/mtgb/compare/v0.2.3...v0.2.4
[0.2.3]: https://github.com/rayvenhaus/mtgb/compare/v0.2.2...v0.2.3
[0.2.2]: https://github.com/rayvenhaus/mtgb/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/rayvenhaus/mtgb/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/rayvenhaus/mtgb/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/rayvenhaus/mtgb/releases/tag/v0.1.0