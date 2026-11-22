#!/usr/bin/env bash
# Runs Corner's checks (Sources/CornerChecks) against CornerCore.
# The checks read sleep.json and checkins.csv from the data folder, so a temporary CORNER_DATA folder gets the
# synthetic copies in Tests/data; nothing in your real Corner folder is read or written.
# Usage: ./run-checks.sh            run the checks
#        ./run-checks.sh sim        the sleep-need simulation (300 made-up sleepers per case)
#        ./run-checks.sh --diff     run the checks and compare the output with Tests/reference-output.txt
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
data="$(mktemp -d "${TMPDIR:-/tmp}/corner-checks.XXXXXX")"
trap 'rm -rf "$data"' EXIT
cp "$root/Tests/data/sleep.json" "$root/Tests/data/checkins.csv" "$data/"
export CORNER_DATA="$data"
cd "$root"
swift build -c release --product CornerChecks
if [ "${1:-}" = "--diff" ]; then
    out="$data/output.txt"
    status=0
    swift run -c release --skip-build CornerChecks > "$out" || status=$?
    cat "$out"
    echo "---- diff against Tests/reference-output.txt (C# output on the same data; the 'your own data' line depends on today's date)"
    diff <(tr -d '\r' < "$root/Tests/reference-output.txt") <(tr -d '\r' < "$out") && echo "identical"
    exit $status
fi
swift run -c release --skip-build CornerChecks "$@"
