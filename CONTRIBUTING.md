# Contributing to MTGB

*The Ministry of Printer Observation & Void Containment welcomes your contribution.*
*We have a form for this. You are, in a sense, filling it in right now.*

---

## Before You Begin

MTGB is a community project. It exists because one person got tired of 
watching a browser tab and built something better. It will get better 
still because people like you are here.

A few things worth knowing before you dive in:

- MTGB is a Windows 10/11 WPF application built on .NET 8
- The codebase is C# throughout — no JavaScript, no web frontend
- The voice is Monty Python meets Terry Pratchett — helpful, 
  absurd, sincere. See [Voice & Tone](#voice--tone) below.
- The Ministry is welcoming but the code has standards.
  The Redundant Department of Redundancy will notice.

---

## Ways to Contribute

- **Report a bug** — something is broken and shouldn't be
- **Suggest a feature** — something is missing and should exist
- **Submit a fix** — you found a bug and fixed it, bless you
- **Add an event type** — MTGB doesn't yet notify on something it should
- **Add a translation** — bring MTGB to your language
- **Improve the docs** — something is unclear, wrong, or missing
- **Write flavour text** — the message pools always have room for more

---

## Reporting Bugs

Open a GitHub issue. Include:

- What you expected to happen
- What actually happened
- Your MTGB version (visible in Settings → About)
- Your Windows version
- Your SimplyPrint printer integration type if relevant
  (Bambu, Prusa, Creality etc)
- Log output from `%APPDATA%\MTGB\logs\` if available —
  the logs are timestamped and usually tell the whole story

Please check existing issues before opening a new one.
The Ministry keeps records. Duplicates are filed but not acted upon.

---

## Suggesting Features

Open a GitHub issue with the `enhancement` label.

Describe what you want and why — not just what the feature does
but what problem it solves. The best feature requests come with
a story: *"I was watching a print at 2am and I wished MTGB could..."*

The Ministry reads everything. Response times vary.
The scribes are thorough, not fast.

---

## Submitting a Pull Request

1. Fork the repository
2. Create a branch — `feature/your-feature-name` 
   or `fix/what-youre-fixing`
3. Make your changes
4. Build and test locally — the project must compile clean
   with no new warnings
5. Update `CHANGELOG.md` under `[Unreleased]` — one line
   per meaningful change
6. Submit the pull request with a clear description of what
   changed and why

**Branch naming:**
feature/add-queue-notifications
fix/toast-line-limit-crash
i18n/german-translation

**Commit messages** — clear and specific:
✓ Fix toast line limit crash when 3+ events arrive simultaneously
✓ Add printer.queue_added event type
✓ Add German translation
✗ Fixed stuff
✗ WIP
✗ asdfgh

The Ministry will review your PR. Feedback will be constructive.
If your code is good, it ships. If it needs work, we'll say so clearly
and tell you what needs changing. We do not close PRs without explanation.

---

## Adding a New Event Type

Event types are data-driven — adding a new one does not require
changes to the rules engine or notification manager.

**Step 1 — Register the event in `EventDefinition.cs`:**

```csharp
new EventDefinition
{
    Id = "your.event.id",
    DisplayName = "Human Readable Name",
    Description = "What this event means in plain English.",
    IsCritical = false,    // true = bypasses quiet hours
    Category = EventCategory.YourCategory
}
```

**Step 2 — Add detection logic in `StateDiffEngine.cs`:**

Find the `CompareSnapshots` method and add your detection
condition. Follow the existing pattern — compare previous
snapshot to fresh snapshot, fire the event when the condition
is met.

**Step 3 — Add a toast title in `NotificationManager.cs`:**

Find `BuildTitle` and add a case for your event ID:

```csharp
"your.event.id" => $"{evt.PrinterName} — Your Event Title",
```

**Step 4 — Add flavour text (optional but encouraged):**

Find `BuildBody` and add a flavour line for your event.
See [Voice & Tone](#voice--tone) for guidance on getting
the tone right.

**Step 5 — Add to the Settings event pick list:**

Find the event toggles section in `SettingsWindow.xaml` and
add a toggle for your new event type. Follow the existing
pattern exactly — tag the toggle with your event ID.

---

## Adding a Translation

MTGB uses `.resx` resource files for localisation.
The base locale is `en-AU` — Australian English.

**Step 1 — Copy the base resource file:**
src/MTGB/Resources/Strings.resx → Strings.de-DE.resx

Replace `de-DE` with your locale code.

**Step 2 — Translate the strings:**

Open the new `.resx` file and translate each value.
Leave the keys (names) exactly as they are — only translate
the values.

**Step 3 — Add your locale to the language selector:**

In `SettingsWindow.xaml` find the `LanguageSelector` 
ComboBox and add your locale:

```xml
<ComboBoxItem Tag="de-DE">Deutsch</ComboBoxItem>
```

**Step 4 — Test it:**

Set your language to the new locale in Settings → Advanced
and verify the UI renders correctly. Check for text truncation
in buttons and labels — some languages are significantly longer
than English and the UI needs to accommodate them.

**A note on flavour text:**

The flavour message pools in `NotificationManager.cs` and
`TrayIcon.cs` are not in the resource files — they are
intentionally in code because the humour is highly
language-specific and direct translation rarely works.

If you are adding a translation, you are warmly encouraged
to write new flavour text pools in your language that
capture the same spirit — absurd, helpful, sincere —
rather than translating the English ones literally.

The Ministry trusts your judgement. The scribes are watching.

---

## Voice & Tone

MTGB has a voice. It is important.

The voice is **Monty Python meets Terry Pratchett** — which is to say:
absurd premises treated with complete sincerity, bureaucratic
formality applied to entirely informal situations, genuine warmth
underneath the jokes, and an absolute refusal to be boring.

**What this means in practice:**

- Helpful first, funny second. The joke must never obscure
  the information.
- The Ministry is real. It has forms. It files things. In triplicate.
  Play it straight.
- Failure messages should acknowledge the failure honestly
  while making the user feel slightly better about it.
- Never punch down. The humour is about the situation,
  never the user.
- Short is better than long. Terry Pratchett could say more
  in six words than most writers manage in six paragraphs.

**Good examples from the codebase:**
"It is not dead, it's resting. (It's dead.)"
"We apologise for the inconvenience."
"The Redundant Department of Redundancy has been notified."
"Polling will carry the burden alone, stoically, without complaint."
"Step away from the printer."

**What to avoid:**
✗ Trying too hard — if you have to explain the joke, it's not the joke
✗ References so obscure only three people will get them
✗ Anything mean-spirited
✗ Corporate language dressed up as humour
✗ Exclamation marks used for enthusiasm rather than irony

If you write a flavour message and it makes you smirk,
it probably belongs. If it makes you groan, it definitely belongs.
If it makes you genuinely laugh, submit it immediately.

---

## Code Style

MTGB follows standard C# conventions with a few specifics:

- **Nullable reference types** enabled throughout — no `!` shortcuts
  without a comment explaining why
- **`var`** for local variables where the type is obvious from context
- **XML doc comments** on all public members
- **No magic strings** — event IDs live in `EventRegistry`,
  credential keys in `CredentialKey`, settings in `AppSettings`
- **Logging** — `LogInformation` for genuinely useful runtime events,
  `LogDebug` for diagnostic detail, `LogWarning` for recoverable
  problems, `LogError` for failures
- **Dispose** — anything that implements `IDisposable` gets disposed.
  The Ministry does not leak resources. It leaks forms. Never resources.

The project uses `.editorconfig` for formatting rules.
Visual Studio will apply them automatically.

---

## UI and Theming

MTGB uses a centralised theme resource dictionary at:
`src/MTGB/UI/Themes/mtgbTheme.xaml`

This is the **single source of truth** for all colours, brushes,
and control styles. If you are contributing UI changes:

- **Never hardcode colours** in XAML or C# — use theme token references
- **Never add styles to individual window resources** — add them to
  `mtgbTheme.xaml` if they are reusable, or to the window's local
  resources only if they are genuinely window-specific
- **Reference brushes by key** in XAML:
  `Foreground="{StaticResource GoldPrimaryBrush}"`
- **Reference colours in C#** using the palette constants defined
  at the top of each codebehind file — do not write `Color.FromRgb`
  inline with raw hex values

**The colour tokens:**

| Key | Hex | Usage |
|---|---|---|
| `BgDeepestBrush` | `#0f1f4a` | Titlebars, navbars, window chrome |
| `BgPrimaryBrush` | `#1e3b8a` | Main content areas |
| `BgRaisedBrush` | `#2c4dba` | Cards, raised surfaces |
| `BgInputBrush` | `#0f1f4a` | Text inputs |
| `AccentBlueBrush` | `#3c83f6` | Borders, interactive elements |
| `GoldPrimaryBrush` | `#fbbd23` | Primary CTA, highlights, the Bing |
| `GoldBrightBrush` | `#fddf49` | Button gradient tops |
| `TextPrimaryBrush` | `#f5f5f5` | Headings, main text |
| `TextMutedBrush` | `#d1d5db` | Body copy, descriptions |
| `TextDimBrush` | `#a0aec0` | Labels, placeholders, inactive |
| `SemanticSuccessBrush` | `#3BB273` | Success states |
| `SemanticWarningBrush` | `#F18F01` | Warning states |
| `SemanticErrorBrush` | `#E84855` | Error states, danger actions |

**Button styles** — use these, do not create new ones:
- `PrimaryButton` — gold gradient, main call to action
- `SecondaryButton` — navy gradient, navigation
- `GhostButton` — translucent blue, subtle actions
- `DangerButton` — red tint, destructive actions only
- `ExitButton` — text only, turns red on hover

**Icon generation** — if you change the icon, regenerate all sizes:
```powershell
.\icon.ps1
.\assets.ps1
```

---

## What We Won't Accept

- Code that introduces security vulnerabilities
- Changes that break the existing test suite
- Features that phone home without explicit user consent —
  MTGB's telemetry policy is opt-in, always, no exceptions
- Anything that harms llamas

---

## Questions

Open a GitHub issue with the `question` label.
Or find us on the SimplyPrint Discord.

The Ministry is here to help.
We have always been here to help.
We will continue to be here to help.

*This is not a threat.*

---

*MTGB — The Monitor That Goes Bing*
*Never leave a print behind.*
*No llamas were harmed in the writing of this document.*