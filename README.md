# Corner

A small to-do list that lives in a corner of your screen. Move the pointer into the corner and the list slides out.
It also predicts your energy through the day from your sleep, and can plan your tasks around your peaks and dips.

## Download

Get the latest version from the **[Releases](../../releases/latest)** page:

| File | For |
|---|---|
| `Corner-Windows.zip` | Windows 10 and 11 |
| `Corner-Mac-Swift.zip` | Mac (native app, small download). Works on Apple Silicon and Intel. |
| `Corner-Mac-AppleSilicon.zip` / `Corner-Mac-Intel.zip` | Mac, the Avalonia version (same look as the Windows app, bigger download) |

### Windows

Unzip the folder anywhere you like (for example Documents) and open `Corner.exe`. If Windows SmartScreen says it
protected your PC, click **More info**, then **Run anyway**. Your tasks are saved in the same folder as `Corner.exe`.

### Mac

1. Unzip, then drag **Corner.app** into your Applications folder.
2. Open it. The first time, macOS says it can't check the app for malware, because it isn't from the App Store or a
   registered developer. This happens only once:
   - **macOS 15 (Sequoia) or newer:** click **Done**, then open **System Settings → Privacy & Security**, scroll down
     and click **Open Anyway** next to the line about Corner.
   - **macOS 14 or older:** right-click (or Control-click) Corner.app, choose **Open**, then **Open** again.
3. Corner lives in the menu bar, not the Dock. Move the pointer into the top-right corner of the screen to open the
   list. The menu-bar icon has the settings and Quit.

Your tasks are saved in `~/Library/Application Support/Corner`. The files are the same as on Windows, so you can
copy them between a PC and a Mac.

## The three versions

| Folder | What | Built with |
|---|---|---|
| [`windows`](windows) | The original app | C# and WPF, compiled by the C# compiler that ships with Windows (`build.ps1`) |
| [`swift`](swift) | Native Mac app | Swift, AppKit and SwiftUI (Swift Package Manager) |
| [`mac-avalonia`](mac-avalonia) | Mac port of the Windows app | C#, .NET 8 and Avalonia, sharing the Windows model code |

Every push is built and checked on GitHub's Windows and Mac machines (see the Actions tab). Pushing a tag such as
`v1.0.1` publishes a new release with all the downloads.

The apps are not signed with an Apple or Microsoft developer certificate, which is why the first open needs the
steps above.

## Fonts

Corner uses the Gilroy typeface (Medium, SemiBold, SemiBold Italic) by Radomir Tinkov, included here under the
repository owner's licence. Gilroy is a commercial font: if you reuse this code, get your own licence or swap in
another font.
