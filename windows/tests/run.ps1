# Checks Corner's sleep and planning sums. Compiles src\*.cs together with Tests.cs into a console program in the
# temp folder and runs it against a copy of sleep.json. Corner.exe, its data files and the running app are not touched.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$work = Join-Path $env:TEMP 'corner-tests'
$data = Join-Path $work 'data'
New-Item -ItemType Directory -Force $data | Out-Null
Get-ChildItem $data | Remove-Item -Force
foreach ($f in 'sleep.json', 'checkins.csv') { if (Test-Path (Join-Path $root $f)) { Copy-Item (Join-Path $root $f) $data } }
$out = Join-Path $work 'CornerTests.exe'
$files = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName) + (Join-Path $root 'tests\Tests.cs')
& "$fw\csc.exe" /nologo /target:exe /main:CornerTests.Program /nowarn:0169,0649,0414 "/out:$out" `
    "/r:$fw\WPF\PresentationCore.dll" "/r:$fw\WPF\PresentationFramework.dll" "/r:$fw\WPF\WindowsBase.dll" `
    "/r:$fw\System.Xaml.dll" "/r:$fw\System.Windows.Forms.dll" "/r:$fw\System.Drawing.dll" `
    "/r:$fw\System.Runtime.Serialization.dll" "/r:$fw\System.Xml.dll" "/r:$fw\System.Core.dll" `
    "/r:$fw\System.IO.Compression.dll" "/r:$fw\System.IO.Compression.FileSystem.dll" $files
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }
$env:CORNER_DATA = $data
& $out @args   # "run.ps1 sim" runs the sleep-need estimate on simulated sleepers instead of the checks
