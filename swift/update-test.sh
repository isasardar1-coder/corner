#!/bin/bash
# The self-update, for real: a copy of Corner.app made to say it is an old version is started, finds a "release" in a
# folder here (CORNER_UPDATE_URL), and has to put the new app in its own place and open it. Then the same with a
# damaged download, with one of the wrong version, and from a folder that cannot be written to: the old app must
# still be there, whole. Run on a Mac after package.sh.
set -euo pipefail
cd "$(dirname "$0")"
T="$(cd "$(mktemp -d)" && pwd -P)"
trap 'chmod -R u+w "$T"; rm -rf "$T"' EXIT

version() { /usr/libexec/PlistBuddy -c "Print CFBundleShortVersionString" "$1/Corner.app/Contents/Info.plist"; }
copy() { # copy <folder> <version>
  mkdir -p "$1"
  ditto out/Corner.app "$1/Corner.app"
  /usr/libexec/PlistBuddy -c "Set CFBundleShortVersionString $2" "$1/Corner.app/Contents/Info.plist"
  codesign --force --deep --sign - "$1/Corner.app" 2>/dev/null
}
release() { # release <folder> <tag>
  echo "{\"tag_name\": \"$2\", \"assets\": [{\"browser_download_url\": \"file://$1/Corner-Mac-Swift.zip\"}]}" > "$1/latest.json"
}
start() { # start <app folder> <release folder> <seconds>
  DATA="$(mktemp -d "$T/data.XXXXXX")"
  echo '{"LastVersion": 10000}' > "$DATA/settings.json"
  echo '{"tasks": []}' > "$DATA/tasks.json"
  CORNER_DATA="$DATA" CORNER_UPDATE_URL="file://$2/latest.json" "$1/Corner.app/Contents/MacOS/Corner" & PID=$!
  for i in $(seq 1 "$3"); do kill -0 $PID 2>/dev/null || break; sleep 1; done
  if kill -0 $PID 2>/dev/null; then ENDED=no; kill $PID; else ENDED=yes; fi
  wait $PID 2>/dev/null || true
  cat "$DATA/error.log" 2>/dev/null || true
}
expect() { # expect <what> <app folder> <ended> <version>
  echo "$1: ended=$ENDED version $(version "$2")"
  [ "$ENDED" = "$3" ] && [ "$(version "$2")" = "$4" ] && codesign --verify --deep --strict "$2/Corner.app"
}

copy "$T/new" 9.9.0
ditto -c -k --keepParent "$T/new/Corner.app" "$T/new/Corner-Mac-Swift.zip"
release "$T/new" v9.9.0

copy "$T/a" 1.1.2
start "$T/a" "$T/new" 60
expect "replaced" "$T/a" yes 9.9.0
for i in $(seq 1 20); do pgrep -f "$T/a/Corner.app/Contents/MacOS/Corner" >/dev/null && break; sleep 1; done
echo "opened again: $(pgrep -fl "$T/a/Corner.app/Contents/MacOS/Corner")"
pkill -f "$T/a/Corner.app/Contents/MacOS/Corner"

copy "$T/b" 1.1.2
mkdir "$T/broken"
head -c 200000 "$T/new/Corner-Mac-Swift.zip" > "$T/broken/Corner-Mac-Swift.zip"
release "$T/broken" v9.9.0
start "$T/b" "$T/broken" 25
expect "damaged download" "$T/b" no 1.1.2

copy "$T/c" 1.1.2
release "$T/new" v9.9.9
start "$T/c" "$T/new" 25
expect "wrong version inside" "$T/c" no 1.1.2
release "$T/new" v9.9.0

copy "$T/d" 1.1.2
chmod a-w "$T/d"
start "$T/d" "$T/new" 25
expect "folder that cannot be written to" "$T/d" no 1.1.2

copy "$T/e" 9.9.0
start "$T/e" "$T/new" 25
expect "already the latest" "$T/e" no 9.9.0
echo "UPDATE TESTS PASS"
