# Corner for macOS

The Mac version of Corner, on Avalonia (C#). The Windows app in `..\windows` stays the source of truth for the
model; nothing here writes inside it.

| Folder | What |
|---|---|
| `Corner.Core` | The shared model as a .NET 8 library. `src/` is **generated** from `..\windows\src` by `tools\sync_core.py`. |
| `Corner.Tests` | Runs the unchanged `..\windows\tests\Tests.cs` against Corner.Core. |
| `Corner.Mac` | The app. `src/ported` is the Windows UI converted to Avalonia (`tools\port_ui.py`, then fixed by hand). `src/*.cs` are the Mac-only parts: menu-bar icon, pointer and idle time, sound, notices, login item, Glass blur. |
| `tools` | `sync_core.py`, `port_ui.py`, `fonts.py` (Gilroy with one family name), `package.py` (Corner.app zips). |

## Everyday commands (from this folder)

Checks (78, all pass):

```powershell
powershell -ExecutionPolicy Bypass -File run-tests.ps1
```

After changing model code in `..\windows\src`:

```powershell
python tools\sync_core.py
```

A test render of the Mac UI on Windows (any demo mode of the Windows build: tasks, all, energy, settings, onboard, rise, …):

```powershell
$env:CORNER_DATA = "$env:TEMP\corner-mac-demo"; New-Item -ItemType Directory -Force $env:CORNER_DATA | Out-Null; .\Corner.Mac\bin\Debug\net8.0\Corner.exe --demo "$env:TEMP\corner-mac.png" all
```

Build the Mac zips (out\Corner-Mac-AppleSilicon.zip, out\Corner-Mac-Intel.zip):

```powershell
foreach ($r in 'osx-arm64', 'osx-x64') { dotnet publish Corner.Mac\Corner.Mac.csproj -c Release -r $r --self-contained true -o "out\publish-$r" }; python tools\package.py
```

## Bringing over a later change to the Windows UI

`src/ported` carries hand fixes, so do not regenerate it in place. Convert into a scratch folder and diff:
`python tools\port_ui.py --out <scratch>`, then compare with `Corner.Mac\src\ported` and carry the change across.

## Not yet checked on a real Mac

See PLAN.md, section 5.
