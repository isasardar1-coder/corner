"""Builds Corner.app from a `dotnet publish -r osx-*` folder and zips it for sharing, on Windows.

    python tools/package.py            # both: out/Corner-Mac-AppleSilicon.zip and out/Corner-Mac-Intel.zip

The bundle: Contents/Info.plist (menu-bar app, no Dock icon), Contents/MacOS/<published files>,
Contents/Resources/Corner.icns. The zip keeps Unix permissions, so the app's launcher stays executable when a Mac
unpacks it. Run publish first (see README.md). Nothing here signs or notarizes: see PLAN.md.
"""
import io, os, struct, sys, zipfile
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..'))
OUT = os.path.join(ROOT, 'out')
ICON = os.path.join(ROOT, 'Corner.Mac', 'Assets', 'icon.png')
VERSION = '1.0.0'

PLIST = '''<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Corner</string>
  <key>CFBundleDisplayName</key><string>Corner</string>
  <key>CFBundleIdentifier</key><string>app.corner.todo</string>
  <key>CFBundleExecutable</key><string>Corner</string>
  <key>CFBundleIconFile</key><string>Corner</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>%(v)s</string>
  <key>CFBundleVersion</key><string>%(v)s</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>Corner</string>
</dict>
</plist>
'''

README = '''Corner for Mac
==============

1. Unzip, then drag Corner.app into your Applications folder.
2. Open it. The first time, macOS says it cannot check the app for malware, because it is
   not from the App Store or a registered developer. This happens once:

   macOS 15 (Sequoia) or newer
     - Click Done on the warning.
     - Open System Settings > Privacy & Security, scroll down, and click "Open Anyway"
       next to the line about Corner. Confirm with your password or Touch ID.

   macOS 14 (Sonoma) or older
     - Right-click (or Control-click) Corner.app, choose Open, then click Open again.

3. Corner lives in the menu bar (a small square with a tick), not in the Dock.
   Move the pointer into the top-right corner of the screen to open the list.
   The menu-bar icon has Show list, Full Mode, which corner it opens from, text size,
   Open at login, and Quit.

Your tasks and sleep log are kept in ~/Library/Application Support/Corner
(tasks.json, sleep.json, routines.json, settings.json, done.csv, days.csv).
They are the same files as on Windows, so they can be copied between the two.
'''


def icns(png):
    im = Image.open(png).convert('RGBA')
    parts = []
    for tag, size in ((b'ic10', 1024), (b'ic09', 512), (b'ic08', 256), (b'ic07', 128), (b'ic14', 512), (b'ic13', 256), (b'ic12', 64), (b'ic11', 32)):
        buf = io.BytesIO(); im.resize((size, size), Image.LANCZOS).save(buf, 'PNG')
        data = buf.getvalue(); parts.append(tag + struct.pack('>I', len(data) + 8) + data)
    body = b''.join(parts)
    return b'icns' + struct.pack('>I', len(body) + 8) + body


def entry(z, name, data, mode):
    info = zipfile.ZipInfo(name, date_time=(2026, 11, 21, 12, 0, 0))
    info.create_system = 3  # Unix, so the permissions below are honoured
    info.external_attr = (mode & 0xFFFF) << 16
    info.compress_type = zipfile.ZIP_DEFLATED
    z.writestr(info, data)


def build(rid, label):
    pub = os.path.join(OUT, 'publish-' + rid)
    if not os.path.isdir(pub): sys.exit('missing ' + pub + ' - run dotnet publish first')
    dest = os.path.join(OUT, 'Corner-Mac-%s.zip' % label)
    icon = icns(ICON)
    with zipfile.ZipFile(dest, 'w') as z:
        for d in ('Corner.app/', 'Corner.app/Contents/', 'Corner.app/Contents/MacOS/', 'Corner.app/Contents/Resources/'):
            entry(z, d, b'', 0o40755)
        entry(z, 'Corner.app/Contents/Info.plist', (PLIST % {'v': VERSION}).encode(), 0o100644)
        entry(z, 'Corner.app/Contents/PkgInfo', b'APPL????', 0o100644)
        entry(z, 'Corner.app/Contents/Resources/Corner.icns', icon, 0o100644)
        for f in sorted(os.listdir(pub)):
            if f.endswith('.pdb'): continue
            p = os.path.join(pub, f)
            if os.path.isdir(p): continue
            exe = f in ('Corner', 'createdump') or f.endswith('.dylib')
            with open(p, 'rb') as fh: entry(z, 'Corner.app/Contents/MacOS/' + f, fh.read(), 0o100755 if exe else 0o100644)
        entry(z, 'How to open Corner.txt', README.encode(), 0o100644)
    print(dest, '%.1f MB' % (os.path.getsize(dest) / 1e6))


if __name__ == '__main__':
    build('osx-arm64', 'AppleSilicon')
    build('osx-x64', 'Intel')
