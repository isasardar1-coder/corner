#!/usr/bin/env bash
# Runs Corner's checks (../windows/tests/Tests.cs) against Corner.Core on .NET 8 (macOS or Linux).
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
data="$(mktemp -d)"
for f in sleep.json checkins.csv; do [ -f "$root/../windows/tests/data/$f" ] && cp "$root/../windows/tests/data/$f" "$data/"; done
dotnet build "$root/Corner.Tests/Corner.Tests.csproj" -c Release -v q --nologo
CORNER_DATA="$data" dotnet "$root/Corner.Tests/bin/Release/net8.0/CornerTests.dll" "$@"
