# Corner for macOS: the model in Swift

`CornerCore` is the platform-neutral part of Corner (tasks, the sleep log, the energy curve, Plan, check-ins,
the sleep-need estimate and the completion log), ported line by line from the Windows app's C# model.
It uses Foundation only. The data files are the same as on Windows (`tasks.json`, `settings.json`, `sleep.json`,
`routines.json`, `done.csv`, `days.csv`, `checkins.csv`, `profile.txt`), so they can be copied between a PC and a Mac.
They live in `~/Library/Application Support/Corner`, or in the folder named by the `CORNER_DATA` environment variable.

`CornerChecks` is the port of the Windows checks (78 of them, same names and order).

## Running the checks

Needs Swift 5.9 or later (Xcode 15+ on macOS).

```sh
cd swift
bash run-checks.sh          # prints PASS/FAIL per check, then ALL PASS; exit code 1 on any failure
bash run-checks.sh --diff   # the same, then a diff against Tests/reference-output.txt
bash run-checks.sh sim      # the sleep-need simulation
```

The script copies `Tests/data/sleep.json` and `Tests/data/checkins.csv` (invented data, not anyone's real log)
into a temporary data folder, because some checks read them. `Tests/reference-output.txt` is what the C# checks
print on that same data. The last line before `ALL PASS` ("your own data: ...") depends on today's date, since the
sleep-need estimate only looks at the last 60 days, so it may differ from the reference.

# Corner.app: the native Mac app

`CornerApp` (the product `Corner`) is the native macOS app built on `CornerCore`: AppKit for the shell and SwiftUI
for the card. It runs on macOS 12 or later, as one universal binary for Apple Silicon and Intel.

```sh
cd swift
bash package.sh            # out/Corner.app, out/Corner-Mac-Swift.zip, then test renders in out/demo-*.png
DEMOS=0 bash package.sh    # without the renders
```

The app is not notarised: the first time, right-click Corner.app and choose Open.

## How it is put together

| File | What is in it |
| --- | --- |
| `main.swift` | Start-up: one running copy per data folder (a lock file), no Dock icon, `--demo`, `--icon`, `--peek` |
| `Panel.swift` | The shell: menu-bar icon and menu, the borderless non-activating panel, the hot corner (polled 60 times a second), showing, hiding and peeking, Full Mode, moving the panel, Cmd + scroll, the timers, the test renders |
| `Genie.swift` | The animations, ported from `Launch.cs`: the panel's picture flying out of its corner and back in as a funnel of thin slices (out of the app icon when Corner starts), the same between the corner panel and Full Mode, drawn in a see-through window that never takes a click; the parts that grow in under the pointer, the energy view sliding down under the chips, the energy % counting up; and the test renders that show them as frames |
| `Model.swift` | `AppModel`: everything the card shows and does (tasks, timers, prompts, the energy curve, naps, Plan, routines, check-ins, the record), ported from the Windows `Shell` |
| `WorkModel.swift` | More of `AppModel`, ported from the `Shell` half of `Work.cs` and `EnergyFeel.cs`: Plan by Schedule / Priority and the stopwatch, the work log (`work.csv`), the pause when the Mac sleeps, locks or sits idle, the order by priority, the habit chips, the energy % you can type over |
| `CardView.swift` | The card: header, the corner panel and Full Mode bodies, the next task and its timer (a countdown, or by priority a stopwatch), the row of chips, the add box with the habit chips under it, the 1-10 check-in, the time's-up box |
| `TasksView.swift` | The task list: rows (the circle carries a tick on the task being worked on), the quick-date strip, the Done bar, drag to reorder |
| `EnergyView.swift` | The Energy side: the curve with the energy % (click it to type how you feel), the three sleep figures, the sleep-need hint, the debt calculator, naps, the plan ticket (a schedule with the split-task offer, or an order by priority), routines |
| `SettingsView.swift` | The morning sleep check-in and the Settings page (planning, panel, energy, your record, sleep history) |
| `Toast.swift` | The energy notices: a card at the panel's corner with `ping.wav` |
| `Onboard.swift` | The welcome window on the very first start |
| `Controls.swift` | Shared pieces: glyphs drawn from the Windows path strings, buttons, chips, switches, text boxes, time entry, flow layout |
| `Theme.swift` | The two skins (Sage and Glass), Gilroy, and the grid of sizes and insets |
| `Platform.swift` | Idle time, the sound, notifications, Finder, the login item (a LaunchAgent) |
| `Icon.swift` | The menu-bar icon and the app icon |

Data lives where `CornerCore` puts it (`~/Library/Application Support/Corner`, or `CORNER_DATA`). The panel's
position on a Mac is kept under its own keys (`MacMoved`, `MacPosX`, `MacPosY`, `MacFullMoved`, ...) so a
`settings.json` copied from Windows does not put it off screen.

Test renders: `CORNER_DATA=/tmp/x Corner.app/Contents/MacOS/Corner --demo out.png <mode>` fills a throwaway list and
writes the panel to a PNG. Modes include `tasks`, `all`, `adding`, `energy`, `settings`, `checkin`, `feedback`,
`timeup`, `debt`, `nap`, `glass`, `rest`, `full`, `fullenergy`, `fullplan`, `fullroutines`, `fullsettings`,
`priority` (Plan by Priority, a task worked on for 12:34), `paused` (the same, then paused), `order` (Full Mode, a
proposed order), `habits` (the chips under the add box), `planning` (Full Mode settings, the Planning card),
`onboard0` to `onboard3` and `rise` / `dip` (the notice). A test render never pauses a task for being idle.

Eight more modes show an animation as frames at fixed moments, side by side in one picture: `launch` and
`launchclose` (out of the app icon as Corner starts, and back into it), `hoveropen` and `hoverclose` (out of the
corner and back), `fullswitch` and `fullexit` (the corner panel flowing into Full Mode, and Full Mode going back
down), `cycleopen` and `cycleclose` (the energy view sliding down under the chips, and folding up). Their clock is
moved by hand, so the frames are the same on every run. Every other mode is a still picture: nothing moves in it.
With "Reduce motion" on in System Settings the panel fades in and out as it did before and nothing flies or slides.
