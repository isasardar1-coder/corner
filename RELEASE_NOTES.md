## Corner 1.1.1

Downloads: **Corner-Windows.zip** (Windows 10 and 11) and **Corner-Mac-Swift.zip** (Mac, Apple Silicon and Intel,
macOS 12 or newer). The [README](https://github.com/isasardar1-coder/corner#readme) says how to open them the first time.

### How to update

- **Windows, from 1.1.0:** nothing to do. Corner fetches this version by itself and restarts once you are not using
  it (Settings > Updates has "Check now").
- **Windows, from 1.0.0:** download `Corner-Windows.zip`, quit Corner (right-click its tray icon), and put the new
  `Corner.exe` in place of the old one. Keep your other files. From then on it updates itself.
- **Mac:** download `Corner-Mac-Swift.zip` and replace Corner.app in Applications. Your tasks are kept.
  The first open needs the same step as before (see the README).

### Better at learning your day (Windows and Mac)

- **Ticking a task off now counts.** "Usually this morning" and the order by priority used to learn only from tasks
  you pressed play on. A task you simply tick off now counts for that part of the day too.
- **Your peaks and dip can move.** Until now your 1-10 check-ins could lift or lower a peak or the dip. They can now
  also move each one earlier or later, by up to 90 minutes, to where your check-ins put it. The curve keeps one even
  hump on each side of the dip. Expect the times to shift by a few minutes as you add check-ins.
- **An easy start.** By priority, while you are still groggy after waking, light tasks come first ("easy start while
  you wake up") and the high-effort one waits for your peak, as it already did by schedule.
- **Honest reasons.** A long task started near the end of a peak now says "your peak lasts until 11:35" where it used
  to say "fits your peak".

### Fixed (Windows and Mac)

- If the computer went to sleep or hibernated before Corner was told, the sleep could be logged as work, and a
  countdown could run out meanwhile. The stretch now ends at the last second Corner saw you.
- After an automatic pause (10 idle minutes), the countdown stands where it was when you left, not ten minutes on.
- `done.csv` lost a task's timer minutes when Corner had been restarted in between.

### New on the Mac

- **Metal, Ceramic and Paper skins**, beside Sage and Glass: Settings > Panel > Look. The chosen tab, an on switch and
  the waiting timer now take the colour of each skin's main button, as on Windows.
- **Animations.** The panel flows out of its corner and back in, comes out of the app icon when Corner starts, and
  flows into Full Mode and back. The parts that appear when the pointer comes onto the panel grow in, and the energy
  view drops down under the chips. With "Reduce motion" on in System Settings, Corner keeps its short fade.

These two were built and checked on GitHub's Mac, as pictures and frame by frame. Nobody has yet watched them on a
Mac's own screen: if something flickers or sits in the wrong place, please say so.
