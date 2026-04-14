# Changelog

All notable changes to MTGB — The Monitor That Goes Bing will be 
documented here.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- Everything. It did not previously exist.

---

## [0.1.0] — 14/04/2026

### The one where it goes Bing for the first time.

### Added

#### Foundation
- Full project scaffold — solution, csproj, folder structure
- MIT licence — the community may do as they wish, within reason
- README.md — including the full origin story as filed with the Ministry
- `.gitignore` and `.gitattributes` configured for .NET WPF projects
- `appsettings.json` — default configuration with sensible defaults
- `AppSettings.cs` — strongly typed configuration model covering auth,
  polling, webhooks, notifications, quiet hours, per-printer overrides,
  and UI preferences
- `EventDefinition.cs` and `EventRegistry` — dynamic event pick list
  driven by data not code, covering print jobs, printer state, progress
  milestones, temperature alerts, filament and queue events
- Multi-resolution application icon (16, 32, 48, 256px) generated from
  the Art Deco 1930s brass and mahogany wood panel design created in
  Leonardo.ai — the most over-engineered tray icon in 3D printing history

#### Security
- `CredentialManager.cs` — Windows Credential Manager wrapper using
  Win32 P/Invoke for secure storage of API keys, OAuth2 tokens, and
  webhook secrets. Credentials are encrypted by Windows, scoped to the
  current user, and survive app reinstalls
- `WebhookSecretManager` — generates a cryptographically secure
  per-installation webhook secret on first run via Double-Secret
  Validation™. Never stored in plain text. Never committed to source.
  If this goes wrong someone is updating their CV at 0300.

#### API layer
- `SimplyPrintApiClient.cs` — typed HTTP client wrapping all SimplyPrint
  endpoints MTGB requires: printer info, pause, resume, cancel, and
  webhook registration/deletion. Handles both API key and OAuth2 auth
  headers. Strongly typed response models via System.Text.Json.
- `AuthService.cs` — manages both authentication paths:
  - API key path: validates against `/account/Test`, stores in
    Credential Manager
  - OAuth2 PKCE path: full browser-based login flow via
    `IdentityModel.OidcClient`, local `HttpListener` callback, stores
    and refreshes tokens automatically. OAuth2 client registration
    submitted to SimplyPrint pending approval.
  - `LocalCallbackBrowser` — opens system browser for login, catches
    the OAuth2 callback, responds with a branded confirmation page
    that says "It goes Bing."

#### Data pipeline
- `StateDiffEngine.cs` — in-memory snapshot engine that detects printer
  state changes by comparing successive API responses. Fires events for:
  online/offline transitions, print started/finished/failed/paused/
  resumed/cancelled, progress milestones (25/50/75%), filament sensor
  triggers, and temperature drops. Milestone deduplication per job ID
  via HashSet. Temperature alert cooldown of 10 minutes per printer.
- `PollingWorker.cs` — .NET 8 BackgroundService polling `/printers/Get`
  on a configurable interval (minimum 10s floor — good API citizenship).
  Progressive backoff on failure: 30s → 60s → 120s → 300s → 600s.
  Skips cycles gracefully when not authenticated. The Ministry is
  cautiously optimistic when polling recovers.
- `WebhookWorker.cs` — local `HttpListener` receiving real-time event
  POSTs from SimplyPrint. Auto-registers and auto-deregisters the webhook
  with SimplyPrint on startup/shutdown. Double-Secret Validation™ on
  every incoming payload via `X-SP-Token` header using constant-time
  comparison to prevent timing attacks. Responds "Bing." on successful
  receipt. Falls back to polling-only gracefully if registration fails.

#### Notifications
- `NotificationManager.cs` — full rules engine processing detected events
  through: global mute → quiet hours → per-printer enabled → per-event
  type → deduplication (30s window) → grouping (configurable batch
  window) → Windows 11 toast delivery via
  `Microsoft.Toolkit.Uwp.Notifications`
- Notification history log — all events recorded to
  `%APPDATA%\MTGB\history.json` regardless of suppression, capped at
  1000 entries, survives app restarts
- Toast action buttons — Pause, Resume, Cancel wired to API client via
  `ToastActionRequested` event
- Rotating flavour message pools for every notification type — because
  MTGB goes Bing with dignity, a smirk, and occasional Monty Python
  references. Highlights include:
  - "It is not dead, it's resting. (It's dead.)"
  - "Step away from the printer."
  - "The Redundant Department of Redundancy has been notified."
  - "We apologise for the inconvenience."
  - "Someone may have forgotten the bol for the Spag bol."
  - "Blessed silence." — on mute

#### User interface
- `TrayIcon.cs` — system tray presence with:
  - Left click → flyout panel slides up from taskbar
  - Right click → minimal context menu
  - Icon state loop synced to farm status every 5 seconds
  - Contextual hover flavour text from message pools
  - Toast action routing to API client
- `FlyoutWindow.xaml` — Art Deco dark brass flyout panel featuring:
  - Dark background (#0D0D0F) with brass/gold (#C9930E) accents
  - Animated slide-up from taskbar with cubic ease and fade
  - Live printer status cards with progress bars and file names
  - Hover tooltips with contextual flavour text in Courier New italic
  - Mute toggle with animated thumb
  - "MUTED · BLESSED SILENCE" header state
  - History, Settings, Dashboard, Exit action buttons
  - Footer showing last poll time and webhook status
  - Georgia serif for Art Deco headings, Courier New for status text,
    Segoe UI Variable for UI chrome
- Placeholder stubs for `SettingsWindow` and `HistoryWindow`
  pending Phase 5 implementation

### Fixed
- `NETSDK1022` duplicate Page items — .NET SDK auto-includes XAML files,
  manual ItemGroup removed
- `SupportedOSPlatformVersion` mismatch — resolved by setting TFM to
  `net8.0-windows10.0.19041.0`
- WPF transparency chrome issue — resolved via `WS_EX_TOOLWINDOW`
  native style applied on `SourceInitialized`
- Flyout positioning — `ContentRendered` event used to guarantee
  `ActualHeight` is available before positioning above taskbar
- STA thread violation — WPF app instantiated before host services start,
  background services launched via `Task.Run`

### Technical notes
- Target framework: `net8.0-windows10.0.19041.0`
- Minimum supported OS: Windows 10 1809 (10.0.17763.0)
- Architecture: x64
- OAuth2 client registration submitted to SimplyPrint — pending approval
- Webhook auto-registration implemented — requires authentication to activate
- Double-Secret Validation™ is what we call cryptography around here,
  because if we get it wrong someone is updating their CV at 0300

### Known issues
- Right-click context menu rendering incorrectly — fix in progress
- Settings window is a placeholder — credentials cannot yet be configured
  via UI (Phase 5)
- History window is a placeholder (Phase 5)
- OAuth2 flow requires SimplyPrint approval before it can be tested

---

## Origin story

> *"Look guys, I'm building a Windows 10/11 notification app for 
> SimplyPrint. It monitors your prints. It goes Bing. Never leave a 
> print behind again. But No ONE expects a Notification App, quietly 
> waiting, to suddenly pop up and notify you, in the middle of your 
> latest doom scrolling, or whatever it is you're doing alone, in front 
> of the computer late at night, and I'm not judging, of the fact that 
> the latest 8 hour print went from perfection to Spag bol (Minus the 
> bol) in 2.3 nano seconds flat. It's called MTGB. That's all you need 
> to know for now. Drop a reaction if you'd use it. Or tell me to take 
> it to the Ministry of Silly Apps. Either way."*

*Inspired by Monty Python's "The Machine That Goes Ping". No NHS budget 
was harmed in the making of this app, although the Redundant Department 
of Redundancy was notified, twice, in triplicate.*

---

[Unreleased]: https://github.com/YOUR_USERNAME/mtgb/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/YOUR_USERNAME/mtgb/releases/tag/v0.1.0
