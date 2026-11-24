# Corner for macOS: the model in Swift

`CornerCore` is the platform-neutral part of Corner (tasks, the sleep log, the energy curve, Plan, check-ins,
the sleep-need estimate and the completion log), ported line by line from the Windows app's C# model.
It uses Foundation only. The data files are the same as on Windows (`tasks.json`, `settings.json`, `sleep.json`,
`routines.json`, `done.csv`, `days.csv`, `checkins.csv`, `profile.txt`), so they can be copied between a PC and a Mac.
They live in `~/Library/Application Support/Corner`, or in the folder named by the `CORNER_DATA` environment variable.

`CornerChecks` is the port of the Windows checks (131 of them, same names and order; the Windows checks of its self-update have no counterpart here).

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
| `CardView.swift` | The card: the header (the corner panel's is the ring, Full Mode and Close; Full Mode's has the day, the gear and the way back), the corner panel (at rest the next task alone; the pointer brings in the header and the chips) and Full Mode's two equal columns, the next task on one line with its timer (a countdown, or by priority a stopwatch), the row of chips, the add box with the habit chips under it, the 1-10 check-in, the time's-up box |
| `TasksView.swift` | The task list: rows (the circle carries a tick on the task being worked on), the quick-date strip, the Done bar (corner panel), drag to reorder; in Full Mode it scrolls inside its card, and an applied plan stands beside its tasks as the upright "Today's plan" strip |
| `EnergyView.swift` | The Energy side: the curve with the energy % (click it to type how you feel), which is all the corner panel shows of it; and Full Mode's energy column: the next task, the curve, three round icons that open Slept, Debt and Need, the sleep-need hint, the debt calculator, naps and "Rate now", a slim Routines bar and a slim Done bar. Also the plan card of the tasks column (a schedule with the split-task offer, or an order by priority) |
| `SettingsView.swift` | The morning sleep check-in and the Settings page, opened from Full Mode (planning, panel with "Full Mode sides", energy, your record, sleep history) |
| `Toast.swift` | The energy notices: a card at the panel's corner with `ping.wav` |
| `Onboard.swift` | The welcome window on the very first start |
| `Glass.swift` | The Glass skin as Liquid Glass, ported from the Windows `Glass.cs`: the sheet a panel is made of (the system's live blur of what is behind the window, a thin veil, a glow from the edge, two hairlines lit from the top left), the glow, rim and gloss of the boxes on it, light or dark glass, and the drawn wallpapers of the test renders |
| `Controls.swift` | Shared pieces: glyphs drawn from the Windows path strings, buttons, chips, switches, text boxes, time entry, flow layout |
| `Theme.swift` | The colours of the five skins (Sage, Glass, Metal, Ceramic, Paper; `Skin` in `settings.json` is 0 to 4, as on Windows; Glass has a second set for dark glass), Gilroy, and the grid of sizes and insets |
| `Material.swift` | The depth of Metal, Ceramic and Paper, ported from the Windows `Material.cs`: the panel's sheet, trays pressed in, cards and pills raised, with their grain, bevel, gloss and layered shadows (no blur). Also `Plate`, the one place every box of every skin is drawn |
| `Platform.swift` | Idle time, the sound, notifications, Finder, the login item (a LaunchAgent) |
| `Icon.swift` | The menu-bar icon and the app icon |

Data lives where `CornerCore` puts it (`~/Library/Application Support/Corner`, or `CORNER_DATA`). The panel's
position on a Mac is kept under its own keys (`MacMoved`, `MacPosX`, `MacPosY`, `MacFullMoved`, ...) so a
`settings.json` copied from Windows does not put it off screen.

Test renders: `CORNER_DATA=/tmp/x Corner.app/Contents/MacOS/Corner --demo out.png <mode>` fills a throwaway list and
writes the panel to a PNG. The corner panel: `rest` (as it opens: the next task alone), `resttimeup` (the same with a
time's-up box over it), `tasks` (after the pointer has come onto it: the header and the chips), `all` (the list
open), `adding` (the add box), `habits` (the add box with the chips of what you usually do), `energy` (the chart
dropped down under the chips), `timer` (a task being timed: "Now" and "Reset" over it), `timeup`, `checkin`,
`feedback`, `priority` (Plan by Priority, a task worked on for 12:34) and `paused` (the same, then paused). Full
Mode: `full`, `fullplan` (a proposed plan), `fullapplied` (an applied plan: the strip beside its tasks), `fullstats`
(the Slept and Need boxes open), `debt` (the Debt box and its calculator), `nap`, `fullroutines`, `fullswap` (the
energy column on the left), `order` (by priority, a proposed order), `settings` or `fullsettings`, and `planning`
(the same by priority). A mode that starts with `full` is that mode in Full Mode, and one that starts with `rest`
is that mode before the pointer has come. Then `onboard0` to `onboard3` and `rise` / `dip` (the notice). `glass`,
`metal`, `ceramic` and `paper` are `tasks` in
that skin, and `fullglass`, `fullmetal`, `fullceramic`, `fullpaper` the same in Full Mode. Any other mode is rendered
in a skin with `CORNER_SKIN=0` to `4` (Sage, Glass, Metal, Ceramic, Paper), which is read in a test render only; in
`package.sh` that is written `settings@2` in `DEMO_MODES`, and the picture is `demo-settings-skin2.png`.
`CORNER_SKIN_TO` then changes the skin while the panel is on screen, as its pill in Settings does (`settings@0:2`,
`demo-settings-skin0to2.png`): the picture should be the same as the one that started in that skin.

Glass is Liquid Glass, as on Windows: clear glass with a bright double rim, its boxes thin layers of white, light
glass with dark text or dark glass with white text. What is behind it is the system's own blur of whatever is behind
the window (`NSVisualEffectView`), which stays live; the screen is never read, so Corner needs no screen-recording
permission. For the same reason it cannot measure how light the desktop is, as Windows does: on a Mac the glass is
dark when macOS is in Dark Mode and light otherwise, settled each time the panel, the notice or the welcome window
opens. The picture bent inwards at the rim is not ported.

A test render cannot show the live blur, so Glass is rendered over a drawn wallpaper, which the glass shows as
Windows shows the screen (blurred, more colourful, evened out for reading). `CORNER_BACKDROP=light`, `dark` or
`busy` (windows full of text) chooses it, `light` when it is not set; in a test render the glass is light or dark by
how light the wallpaper is behind the panel (dark below 30%, as on Windows), or as `CORNER_GLASS=light` or `dark`
says. Both are read in a test render only. In `package.sh`, `all@1/dark` is a mode in Glass over the dark wallpaper
(`demo-all-skin1-dark.png`) and `all@1/busy/dark` is dark glass over the busy one
(`demo-all-skin1-busy-glassdark.png`). The other skins are rendered on sand, as before, and so are the frames of an
animation, where a flat fill stands in for the blur as it does in the animations themselves.
A test render never pauses a task for being idle.

Eight more modes show an animation as frames at fixed moments, side by side in one picture: `launch` and
`launchclose` (out of the app icon as Corner starts, and back into it), `hoveropen` and `hoverclose` (out of the
corner and back), `fullswitch` and `fullexit` (the corner panel flowing into Full Mode, and Full Mode going back
down), `cycleopen` and `cycleclose` (the energy view sliding down under the chips, and folding up). Their clock is
moved by hand, so the frames are the same on every run. Three more show an animation being cut short: `hoverback`
(the pointer leaves while the panel is still flying out), `hoveragain` (it is opened again before it has gone back
in) and `fullearly` (Full Mode entered while the panel is flying out); and `hoverbottom` is `hoveropen` from the
bottom-left corner. Every other mode is a still picture: nothing moves in it.

When Corner starts, the panel comes out of its icon, rests in its corner for a few seconds and goes back in, as on
Windows. With "Reduce motion" on in System Settings the panel fades in and out as it did before and nothing flies
or slides.
