# Runs Corner's existing checks (../windows/tests/Tests.cs) against Corner.Core on .NET 8.
# Uses copies of sleep.json and checkins.csv in a temp folder; the shared test data in ../windows/tests/data.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$data = Join-Path ([IO.Path]::GetTempPath()) 'corner-mac-tests'
New-Item -ItemType Directory -Force $data | Out-Null
Get-ChildItem $data | Remove-Item -Force
foreach ($f in 'sleep.json', 'checkins.csv') { $p = Join-Path $root "..\windows\tests\data\$f"; if (Test-Path $p) { Copy-Item $p $data } }
dotnet build (Join-Path $root 'Corner.Tests\Corner.Tests.csproj') -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
$env:CORNER_DATA = $data
dotnet (Join-Path $root 'Corner.Tests\bin\Release\net8.0\CornerTests.dll') @args
exit $LASTEXITCODE
