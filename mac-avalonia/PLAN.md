# Corner for macOS — port plan

Status, 2026-11-21: you chose Avalonia. The core library, the full UI port and the Mac zips are built; what is
left needs a real Mac (section 5).

## 1. Shared core (done)

`Corner.Core` is a .NET 8 class library holding everything in Corner that is not UI:

| From the Windows source | What it is |
|---|---|
| `Corner.cs` | `TaskItem`, `Data`, `Store` (tasks.json), `Settings` (settings.json), `When` ("tomorrow 9am") |
| `Energy.cs` | `EnergyModel`, `EnergyDay`, `Zone`, `Night`, `SleepSpan`, `SleepLog` (sleep.json) |
| `Plan.cs` | `TaskTags`, `Routine`, `Routines` (routines.json), `PlanItem`, `Planner` |
| `Need.cs` | `NeedEstimate` (sleep-need suggestion) |
| `Stats.cs` | `TaskLog` (done.csv, days.csv) |
| `Checks.cs` | `Feedback` (checkins.csv), `Tuning` |
| `Onboard.cs` | `Profile` (profile.txt) |

`Corner.Core/src` is **generated** by `tools/sync_core.py` from `..\windows\src`. The script drops every WPF/WinForms
type (`Shell`, `T`, `Row`, `RimBorder`, `Backdrop`, `PillBorder`, `Program`, `Autostart`) and the WPF usings, and
keeps the rest byte for byte. The Windows app stays the one source of truth: change the model there, re-run the
script, and the Mac build picks it up. Nothing in `..\windows` is written.

`run-tests.ps1` builds `Corner.Tests` (the unchanged `..\windows\tests\Tests.cs`) against the library and runs it on
copies of `sleep.json` and `checkins.csv`. Result: **ALL PASS, 78 checks**, and the full output is identical, line for
line, to the same checks built with the Windows `csc` on .NET Framework (171 lines, no differences).

The data files keep the same format, so a friend could copy their files between a PC and a Mac.

## 2. macOS UI: two ways

### A. Avalonia (recommended)

Avalonia is a cross-platform UI toolkit for C# whose controls mirror WPF's (`Grid`, `StackPanel`, `Border`,
`TextBlock`, `Canvas`, shapes, brushes, transforms). Corner builds its UI in code, not XAML, so most of the ~3,800
lines of `Shell` code port by renaming types and events rather than by rewriting.

- **Reuses** the core library as is, and the 78 checks keep guarding it.
- **Builds from this PC**: `dotnet publish -r osx-arm64` (and `osx-x64` for Intel Macs) cross-compiles here, and the
  `.app` folder (Info.plist, binary, fonts, ping.wav) can be assembled here too.
- **Costs**: a bigger download (about 35 MB zipped, since .NET travels inside the app), and a few macOS calls made
  through small native bindings (listed below). Startup is a little slower than a native app.
- Could later replace the WPF version on Windows too, so both platforms share one UI. Not part of this plan.

### B. Native Swift (SwiftUI/AppKit)

- **Feels most native**, smallest app (~5 MB), first-class access to every macOS API.
- **Rewrites everything**, the core included: ~2,000 lines of model, planner and sleep-need code re-typed in Swift,
  plus the checks. Two copies of the energy model would then drift apart with every tweak.
- **Cannot be built or even compiled from this PC.** Every step needs Xcode on a Mac, so all of it is blind until then.

### How each Corner feature maps

| Feature (Windows source) | Avalonia | Swift |
|---|---|---|
| Hot corner (`GetCursorPos` polling, `Corner.cs`) | Poll `NSEvent.mouseLocation` through a 10-line Objective-C binding. No permission needed. | `NSEvent.mouseLocation`, same idea. |
| Tray icon (`NotifyIcon`) | Avalonia `TrayIcon` → a macOS menu-bar icon. `LSUIElement` in Info.plist hides the Dock icon. | `NSStatusItem`. |
| Floating panel | Borderless, topmost `Window`. | `NSPanel` (can float over full-screen apps). |
| Start at login (`Run` registry key) | A LaunchAgent plist in `~/Library/LaunchAgents`. Works unsigned. | `SMAppService` (cleaner, but wants a signed app). |
| Onboarding window (`Onboard.cs`) | Ported as a normal window; the slide animations map to Avalonia transitions. | Rebuilt in SwiftUI. |
| Energy cards with sound (`Toast.cs`) | A small borderless window that slides in; `ping.wav` played with `afplay` (built into macOS). | `NSSound`. |
| Glass skin (`Glass.cs` grabs and blurs the screen) | `TransparencyLevelHint = AcrylicBlur`, which macOS draws with its own vibrancy (`NSVisualEffectView`). Real live blur, no screen grab, no Screen Recording permission. The Sage skin ports unchanged. | `NSVisualEffectView` directly. |
| Idle time for check-ins (`GetLastInputInfo`) | `CGEventSourceSecondsSinceLastEventType`, no permission. | Same call. |
| "Busy / presenting" check (`SHQueryUserNotificationState`) | No direct equivalent; first version skips it (Focus modes could come later). | Same. |
| Ctrl + wheel resize | Cmd + wheel. | Same. |
| Gilroy fonts | Embedded as app resources. | Bundled in the app. |
| Single instance (named mutex) | A lock file in the data folder. | One app per bundle by default. |
| Data folder (beside Corner.exe) | `~/Library/Application Support/Corner`, because a downloaded `.app` is read-only. | Same. |

Things that will behave differently on a Mac: the top-right corner also holds macOS's own clock and Control Centre,
and a user may have a system hot corner there. Corner's corner choice (top right / left, bottom right / left) carries
over, so a friend can pick another.

## 3. What needs a real Mac

From this PC I can write the app, cross-compile it, assemble `Corner.app` and zip it with the right file
permissions. I cannot run it. A Mac (or a Mac build host) is needed for:

1. **Testing.** Any first run, and every look and feel check: panel position, menu-bar icon, blur, sound, login item.
2. **Signing.** Apple Silicon Macs refuse to run a binary that carries no signature at all. The .NET tools cannot
   sign a Mac binary from Windows, so the app needs at least an "ad-hoc" signature: one `codesign` command on a Mac, or
   the open-source `rcodesign` tool, which also runs on Windows (a download I would ask about first).
3. **Notarizing.** Needs an Apple Developer account ($99/year) and a Developer ID certificate. Without it, macOS
   blocks the first open of a downloaded app:
   - macOS 14 and older: right-click (Control-click) the app, choose **Open**, then **Open** again.
   - **macOS 15 Sequoia and newer: right-click → Open no longer works.** The friend opens the app once, closes the
     warning, then goes to **System Settings → Privacy & Security**, scrolls down and clicks **Open Anyway**.
   Either way it is needed once; after that the app opens normally.

Ways to get a Mac:
- your own Mac or a friend's, for an hour of testing;
- GitHub Actions `macos` runners (free for public repositories; private ones use paid minutes at a higher rate);
- a rented cloud Mac (MacinCloud, AWS EC2 Mac), billed by the hour or day.

## 4. Order of work once you choose (Avalonia)

1. `Corner.Mac` project: menu-bar icon, empty panel at the hot corner, data folder, single instance. Build the
   `.app` zip and get it **starting on a Mac** — the first real milestone.
2. Port the panel: task list, add box with "tomorrow 9am" parsing, tags, done list (`Corner.cs`, `Ui.cs`).
3. Energy tab, chart, sleep check-in, Plan my day, routines (`UiEnergy.cs`, `UiSleep.cs`, `Plan.cs`).
4. Focus view and timers (`Focus.cs`), check-ins (`Checks.cs`), stats (`Stats.cs`).
5. Energy cards with sound (`Toast.cs`), onboarding (`Onboard.cs`), settings, start at login.
6. Sage skin, then Glass on macOS vibrancy.
7. Package: `Corner-Mac-AppleSilicon.zip` and `Corner-Mac-Intel.zip` in `..\windows\share` (written only when you say),
   with a one-page "how to open it the first time" note.

## 5. Where it stands (2026-11-21)

Done and checked on this PC:
- The whole Windows UI is ported: corner panel, task list, add box, drag to reorder, the next task and timers,
  Full Mode, Energy tab and chart, sleep check-in and history, naps, sleep-debt calculator, Plan my day, routines,
  the 1-10 check-ins, settings, the welcome window and the energy notices with sound.
- 30 test renders, each beside the same render from the Windows build: they match, apart from slightly taller
  text lines and a word or two wrapping differently.
- A live run: starts, shows the panel, runs its timers, no errors.
- `out/Corner-Mac-AppleSilicon.zip` (44 MB) and `out/Corner-Mac-Intel.zip` (46 MB). The .NET SDK gave the app's
  launcher an ad-hoc signature, so Apple Silicon will run it; it is not notarized, so friends need the one-time
  "Open Anyway" step (written in the zip's `How to open Corner.txt`).

Written for the Mac but never run on one (each could need a fix after the first try):
- the hot corner (pointer position from `NSEvent`), the menu-bar icon and its menu, no Dock icon;
- floating over every Space and full-screen apps;
- Open at login (a LaunchAgent), notices (Notification Centre via `osascript`), the ping (`afplay`);
- the Glass skin's live blur: a second window under the card with macOS's own blur and rounded corners.
  Sage is the default skin and does not depend on it;
- the card's drop shadow: Avalonia leaves effects out of test renders, so it has only been seen in the code.

To finish, on any Mac with macOS 12 or newer: unzip the right zip, open it as the text file says, and try the
list above. If something fails, the details are in `~/Library/Application Support/Corner/error.log`.
