## Corner 1.1.3

Downloads: **Corner-Windows.zip** (Windows 10 and 11) and **Corner-Mac-Swift.zip** (Mac, Apple Silicon and Intel,
macOS 12 or newer). The [README](https://github.com/isasardar1-coder/corner#readme) says how to open them the first time.

This release is for the Mac. On Windows nothing changes but the version number.

### How to update

- **Mac:** download `Corner-Mac-Swift.zip` once more and replace Corner.app in Applications. Your tasks are kept.
  The first open needs the same step as before (see the README). From then on Corner is meant to update itself.
- **Windows, from 1.1.0 or newer:** nothing to do; Corner fetches it by itself.
- **Windows, from 1.0.0:** download `Corner-Windows.zip`, quit Corner (right-click its tray icon), and put the new
  `Corner.exe` in place of the old one. Keep your other files. From then on it updates itself.

### New on the Mac

- **Corner updates itself**, as on Windows. It looks for a new release a minute after it starts and every 6 hours, and
  swaps itself once the panel is closed and no timer is running. Settings > Panel has the switch, the version and
  "Check now". If macOS does not let Corner replace itself, it says so and offers the download page instead. Corner.app
  has to be in Applications (or another folder you can write to) for this.
- **Full Mode:** a long task list now scrolls as a whole card, and the Done card waits until last night's sleep is
  entered, both as on Windows.

### What to know

- The self-update was tested on GitHub's Mac: a real swap and restart, and damaged, wrong and blocked downloads. It
  has not been tried on a Mac in daily use: macOS may ask for permission the first time. If an update does not go
  through, please say what macOS showed.
