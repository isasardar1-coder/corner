# Builds Corner.exe with the C# compiler that ships with Windows. No SDK needed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ico  = Join-Path $root 'src\corner.ico'
$out  = Join-Path $root 'Corner.exe'
$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'

# --- icon -------------------------------------------------------------------
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    function New-Art([int]$s) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
        $m = [single]($s * 0.06); $w = [single]($s - 2 * $m); $r = [single]($s * 0.26)
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p.AddArc($m, $m, $r * 2, $r * 2, 180, 90)
        $p.AddArc($m + $w - $r * 2, $m, $r * 2, $r * 2, 270, 90)
        $p.AddArc($m + $w - $r * 2, $m + $w - $r * 2, $r * 2, $r * 2, 0, 90)
        $p.AddArc($m, $m + $w - $r * 2, $r * 2, $r * 2, 90, 90)
        $p.CloseFigure()
        $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
        $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 158, 156, 255)), ([System.Drawing.Color]::FromArgb(255, 84, 80, 214)), 60
        $g.FillPath($br, $p)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]($s * 0.115))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ([single]($s * 0.29)), ([single]($s * 0.53))),
            (New-Object System.Drawing.PointF ([single]($s * 0.44)), ([single]($s * 0.67))),
            (New-Object System.Drawing.PointF ([single]($s * 0.72)), ([single]($s * 0.36))))
        $g.DrawLines($pen, $pts)
        $g.Dispose()
        return $bmp
    }
    $sizes = 16, 20, 24, 32, 48, 64, 256
    $images = @()
    foreach ($s in $sizes) {
        $bmp = New-Art $s
        $ms = New-Object System.IO.MemoryStream
        if ($s -eq 256) {
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        } else {
            $bw = New-Object System.IO.BinaryWriter $ms
            $maskRow = [int]([math]::Floor(($s + 31) / 32)) * 4
            $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
            $bw.Write([int]0); $bw.Write([int]($s * $s * 4 + $maskRow * $s)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
            $lock = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $s, $s), 'ReadOnly', [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $buf = New-Object byte[] ($s * 4)
            for ($y = $s - 1; $y -ge 0; $y--) {
                [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($lock.Scan0.ToInt64() + $y * $lock.Stride), $buf, 0, $s * 4)
                $bw.Write($buf)
            }
            $bmp.UnlockBits($lock)
            $bw.Write((New-Object byte[] ($maskRow * $s)))
            $bw.Flush()
        }
        $images += , $ms.ToArray()
        $bmp.Dispose()
    }
    $fs = [System.IO.File]::Create($ico)
    $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $b = if ($s -eq 256) { 0 } else { $s }
        $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$images[$i].Length); $w.Write([int]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write($img) }
    $w.Flush(); $fs.Close()
}

# --- compile ----------------------------------------------------------------
Get-Process Corner -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
& "$fw\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu "/out:$out" "/win32icon:$ico" `
    "/r:$fw\WPF\PresentationCore.dll" "/r:$fw\WPF\PresentationFramework.dll" "/r:$fw\WPF\WindowsBase.dll" `
    "/r:$fw\System.Xaml.dll" "/r:$fw\System.Windows.Forms.dll" "/r:$fw\System.Drawing.dll" `
    "/r:$fw\System.Runtime.Serialization.dll" "/r:$fw\System.Xml.dll" "/r:$fw\System.Core.dll" `
    (Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName)
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
"Built $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
