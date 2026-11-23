// Corner - the tray icon: the hot-corner picture (HotIcon.cs), the same one the Full Mode window shows on the taskbar.
// The light one while Windows is in light mode, the dark one in dark mode. Written by hand (HotIcon.cs and Logo.cs are generated).
using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace Corner
{
    partial class Shell
    {
        bool? trayDark; // which hot-corner picture the tray has now

        /// The taskbar is dark: Windows' own mode (Settings > Personalisation > Colours), which is not always the apps' mode.
        /// The Full Mode window's icon is the picture that stands out on it: the white one on a dark taskbar (HotIcon.cs).
        /// A test run can force it with CORNER_TASKBAR_DARK.
        static bool TaskbarDark()
        {
            string forced = Environment.GetEnvironmentVariable("CORNER_TASKBAR_DARK");
            if (Environment.GetEnvironmentVariable("CORNER_DATA") != null && forced != null) return forced == "1";
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", null);
                if (v is int) return (int)v == 0;
            }
            catch { }
            return Logo.WindowsIsDark();
        }

        /// Puts the hot-corner picture in the tray. Called after every ApplyLogo (Logo.cs), which puts the logo there.
        void ApplyTrayIcon()
        {
            if (tray == null || trayDark == Logo.Dark) return;
            try
            {
                var ms = new MemoryStream();
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(HotIcon.Image(Logo.Dark)));
                enc.Save(ms);
                ms.Position = 0;
                using (var big = new System.Drawing.Bitmap(ms))
                using (var small = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = System.Drawing.Graphics.FromImage(small))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.DrawImage(big, new System.Drawing.Rectangle(0, 0, 32, 32));
                    }
                    tray.Icon = System.Drawing.Icon.FromHandle(small.GetHicon());
                }
                trayDark = Logo.Dark;
            }
            catch (Exception ex) { Store.Log(ex); }
        }
    }
}
