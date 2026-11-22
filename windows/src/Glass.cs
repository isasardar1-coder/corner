// Corner - the Glass skin: the frosted picture of whatever is behind the panel, the tint and the rim laid over it,
// and the change from one skin to the other. The colours of both skins are in T (Corner.cs).
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WF = System.Windows.Forms;

namespace Corner
{
    /// A box that can carry a glass rim: a hairline, brighter at the top, drawn over its fill. In the Sage skin the
    /// rim's colours are fully transparent, so nothing shows, and nothing has to be redrawn when the skin changes.
    class RimBorder : Border
    {
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth, h = ActualHeight;
            if (w < 6 || h < 6 || Background == null || Background == Brushes.Transparent) return;
            double r = Math.Max(0, Math.Min(CornerRadius.TopLeft, Math.Min(w, h) / 2) - 0.5);
            dc.DrawRoundedRectangle(null, T.RimPen, new Rect(0.5, 0.5, w - 1, h - 1), r, r);
        }
    }

    /// The picture behind the glass: the desktop as it was when the panel opened, at an eighth of its size, blurred,
    /// made a little more colourful, and lifted towards white so that dark text stays readable whatever is behind.
    /// It is kept in memory only, and only in this small blurred form.
    static class Backdrop
    {
        public const int K = 8;            // the picture is 1/K of the screen
        public static BitmapSource Image;  // null until the first grab, or when the screen could not be read
        public static BitmapSource Sharp;  // test renders only: the made-up wallpaper before it is blurred, at half size
        public static double Left, Top;    // where the picture's corner is on the screen, in pixels

        // the material (set with the Glass colours in T)
        public static int Blur = 3;        // box radius in the small picture's pixels, three passes: about 28 px on screen
        public static double Saturation = 1.15, Brightness = 1.0, Lift = 0.30;

        /// `fake` (test renders): "light", "dark" or "busy" draws a wallpaper instead of reading the screen.
        public static void Grab(string fake)
        {
            try
            {
                var vs = WF.SystemInformation.VirtualScreen;
                int w = Math.Max(8, vs.Width / K), h = Math.Max(8, vs.Height / K);
                byte[] px = fake != null ? Wallpaper(fake, vs.Width, vs.Height, w, h) : Screen(vs, w, h);
                BoxBlur(px, w, h, Blur);
                Tone(px);
                var img = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, w * 4);
                img.Freeze();
                Image = img; Left = vs.Left; Top = vs.Top;
            }
            catch (Exception ex) { Store.Log(ex); Image = null; }
        }

        static byte[] Screen(System.Drawing.Rectangle vs, int w, int h)
        {
            var px = new byte[w * h * 4];
            var fmt = System.Drawing.Imaging.PixelFormat.Format32bppRgb;
            using (var big = new System.Drawing.Bitmap(vs.Width, vs.Height, fmt))
            using (var small = new System.Drawing.Bitmap(w, h, fmt))
            {
                using (var g = System.Drawing.Graphics.FromImage(big)) g.CopyFromScreen(vs.Left, vs.Top, 0, 0, big.Size);
                using (var g = System.Drawing.Graphics.FromImage(small))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(big, new System.Drawing.Rectangle(0, 0, w, h), new System.Drawing.Rectangle(0, 0, vs.Width, vs.Height), System.Drawing.GraphicsUnit.Pixel);
                }
                var d = small.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, fmt);
                try { for (int y = 0; y < h; y++) Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), px, y * w * 4, w * 4); }
                finally { small.UnlockBits(d); }
            }
            return px;
        }

        static void BoxBlur(byte[] p, int w, int h, int r)
        {
            if (r < 1) return;
            var t = new byte[p.Length];
            for (int pass = 0; pass < 3; pass++) { Pass(p, t, w, h, r, true); Pass(t, p, w, h, r, false); }
        }

        static void Pass(byte[] src, byte[] dst, int w, int h, int r, bool across)
        {
            int n = across ? w : h, lines = across ? h : w, step = across ? 4 : w * 4, d = 2 * r + 1;
            for (int j = 0; j < lines; j++)
            {
                int line = across ? j * w * 4 : j * 4;
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0;
                    for (int k = -r; k <= r; k++) sum += src[line + Math.Min(n - 1, Math.Max(0, k)) * step + c];
                    for (int i = 0; i < n; i++)
                    {
                        dst[line + i * step + c] = (byte)(sum / d);
                        sum += src[line + Math.Min(n - 1, i + r + 1) * step + c] - src[line + Math.Max(0, i - r) * step + c];
                    }
                }
            }
        }

        static void Tone(byte[] p)
        {
            for (int i = 0; i < p.Length; i += 4)
            {
                double b = p[i], g = p[i + 1], r = p[i + 2], grey = 0.114 * b + 0.587 * g + 0.299 * r;
                p[i] = Fix(grey + (b - grey) * Saturation); p[i + 1] = Fix(grey + (g - grey) * Saturation); p[i + 2] = Fix(grey + (r - grey) * Saturation);
                p[i + 3] = 255;
            }
        }

        static byte Fix(double v)
        {
            v = Math.Max(0, Math.Min(255, v * Brightness));
            return (byte)(v + (255 - v) * Lift);
        }

        // ---- test renders: a wallpaper to stand behind the glass (the real screen is never read in a test run)

        static byte[] Wallpaper(string kind, int sw, int sh, int w, int h)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) Paint(dc, kind, sw, sh);
            Sharp = Shot(dv, sw, sh, sw / 2, sh / 2);
            var small = Shot(dv, sw, sh, w, h);
            var px = new byte[w * h * 4];
            small.CopyPixels(px, w * 4, 0);
            return px;
        }

        static BitmapSource Shot(DrawingVisual dv, int sw, int sh, int w, int h)
        {
            var host = new DrawingVisual();
            using (var dc = host.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform((double)w / sw, (double)h / sh));
                dc.DrawDrawing(dv.Drawing);
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host); rtb.Freeze();
            return rtb;
        }

        static Brush Blob(string hex) { var c = T.C(hex); return new RadialGradientBrush(c, Color.FromArgb(0, c.R, c.G, c.B)); }

        static void Paint(DrawingContext dc, string kind, int sw, int sh)
        {
            var all = new Rect(0, 0, sw, sh);
            if (kind == "dark")
            {
                dc.DrawRectangle(new LinearGradientBrush(T.C("#0A0F1F"), T.C("#1C1036"), 40), null, all);
                dc.DrawEllipse(Blob("#5B2A9D"), null, new Point(sw * 0.82, sh * 0.18), sw * 0.26, sw * 0.22);
                dc.DrawEllipse(Blob("#0E5A8A"), null, new Point(sw * 0.62, sh * 0.55), sw * 0.3, sw * 0.24);
                dc.DrawEllipse(Blob("#C0267E"), null, new Point(sw * 0.93, sh * 0.5), sw * 0.12, sw * 0.12);
                dc.DrawEllipse(Blob("#12332B"), null, new Point(sw * 0.3, sh * 0.8), sw * 0.3, sw * 0.2);
            }
            else
            {
                dc.DrawRectangle(new LinearGradientBrush(T.C("#58B7F5"), T.C("#F7A08A"), 35), null, all);
                dc.DrawEllipse(Blob("#FFD76B"), null, new Point(sw * 0.88, sh * 0.16), sw * 0.2, sw * 0.18);
                dc.DrawEllipse(Blob("#7A5CF0"), null, new Point(sw * 0.7, sh * 0.42), sw * 0.22, sw * 0.2);
                dc.DrawEllipse(Blob("#22C7A0"), null, new Point(sw * 0.95, sh * 0.6), sw * 0.18, sw * 0.2);
                dc.DrawEllipse(Blob("#FF5E7E"), null, new Point(sw * 0.45, sh * 0.75), sw * 0.26, sw * 0.2);
                dc.DrawEllipse(Blob("#FFFFFF"), null, new Point(sw * 0.6, sh * 0.1), sw * 0.14, sw * 0.08);
            }
            if (kind != "busy") return;
            // windows full of text, a dark one among them: the hardest thing to read over
            var ink = new SolidColorBrush(T.C("#1B1F24")); var paper = new SolidColorBrush(T.C("#FAFAFA")); var night = new SolidColorBrush(T.C("#15171C")); var green = new SolidColorBrush(T.C("#59D67A"));
            double ww = sw * 0.21, wh = sh * 0.34;
            for (int i = 0; i < 12; i++)
            {
                double x = sw * 0.04 + (i % 4) * sw * 0.245, y = sh * 0.03 + (i / 4) * sh * 0.325;
                bool dark = i % 3 == 1;
                dc.DrawRoundedRectangle(dark ? night : paper, null, new Rect(x, y, ww, wh), 10, 10);
                for (int l = 0; l < 14; l++)
                    dc.DrawRectangle(dark ? green : ink, null, new Rect(x + 18, y + 22 + l * wh / 16, ww * (0.35 + 0.55 * ((l * 37 + i * 11) % 10) / 10.0), 5));
                dc.DrawRectangle(new SolidColorBrush(T.C(i % 2 == 0 ? "#E5484D" : "#3E63DD")), null, new Rect(x + ww * 0.62, y + wh * 0.55, ww * 0.3, wh * 0.3));
            }
        }
    }

    partial class Shell
    {
        Border glassTint, glassSheen; // the Glass skin's tint and rim, and the light on its top, between the picture of what is behind and the content
        readonly ImageBrush backBrush = new ImageBrush { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute };
        Rect backView; bool backHooked;

        /// "light", "dark" or "busy" in a test run (CORNER_BACKDROP): a made-up wallpaper, so a test never reads the real
        /// screen unless it asks to with "screen". Null outside test runs.
        static string FakeBackdrop
        {
            get
            {
                if (Environment.GetEnvironmentVariable("CORNER_DATA") == null) return null;
                string b = Environment.GetEnvironmentVariable("CORNER_BACKDROP");
                return b == "screen" ? null : string.IsNullOrEmpty(b) ? "light" : b;
            }
        }

        /// Takes the picture of the desktop for the Glass skin. Only while the panel is off the screen: it would be in it.
        void GrabBackdrop()
        {
            if (!T.Glass || (IsVisible && Backdrop.Image != null)) return;
            var took = System.Diagnostics.Stopwatch.StartNew();
            Backdrop.Grab(FakeBackdrop);
            if (demoRun) try { System.IO.File.AppendAllText(System.IO.Path.Combine(Store.Dir, "backdrop.txt"), (Backdrop.Image == null ? "failed" : Backdrop.Image.PixelWidth + "x" + Backdrop.Image.PixelHeight) + " in " + took.ElapsedMilliseconds + " ms" + Environment.NewLine); } catch { }
            backBrush.ImageSource = Backdrop.Image; backView = Rect.Empty;
            if (!backHooked) { backHooked = true; CompositionTarget.Rendering += (s, e) => TrackBackdrop(); }
        }

        /// Keeps the part of the picture that is behind the panel in the panel, as it opens, grows and is dragged.
        void TrackBackdrop()
        {
            if (!T.Glass || !IsVisible || Backdrop.Image == null || card.ActualWidth <= 0 || card.ActualHeight <= 0) return;
            Point a, b;
            try { a = card.PointToScreen(new Point(0, 0)); b = card.PointToScreen(new Point(card.ActualWidth, card.ActualHeight)); }
            catch { return; }
            var v = new Rect((a.X - Backdrop.Left) / Backdrop.K, (a.Y - Backdrop.Top) / Backdrop.K, Math.Max(0.5, (b.X - a.X) / Backdrop.K), Math.Max(0.5, (b.Y - a.Y) / Backdrop.K));
            if (v == backView) return;
            backView = v; backBrush.Viewbox = v;
        }

        /// The panel's own layers for the skin in use. Called from Restyle (Ui.cs).
        void SkinPanel()
        {
            if (T.Glass)
            {
                panelBg.Background = Backdrop.Image != null ? (Brush)backBrush : T.GlassBase; // the picture, or plain frosted white if the screen could not be read
                panelBg.BorderThickness = new Thickness(0);
                double pr = panelBg.CornerRadius.TopLeft;
                glassTint.CornerRadius = panelBg.CornerRadius; glassSheen.CornerRadius = new CornerRadius(pr - 1, pr - 1, 0, 0); glassTint.Visibility = Visibility.Visible;
                shadow.Color = T.GlassShadow; shadow.Opacity = T.GlassShadowOpacity;
                TrackBackdrop();
            }
            else
            {
                panelBg.Background = T.Bg; panelBg.BorderThickness = new Thickness(1);
                glassTint.Visibility = Visibility.Collapsed;
                shadow.Color = Color.FromRgb(60, 54, 34); shadow.Opacity = 0.3;
            }
        }

        /// Settings: the other skin. The colours change where they are; the panel is laid out and drawn again around them.
        void SetSkin(int i)
        {
            if (i == T.Skin) return;
            Settings.Set("Skin", i);
            T.Use(i);
            Action redraw = () => { if (shown) Place(); Rebuild(); RefreshEnergy(false); if (settingsOpen) DrawSettings(); };
            if (T.Glass && IsVisible)
            {   // the picture of what is behind can only be taken with the panel off the screen: it blinks once
                Backdrop.Image = null;
                Hide();
                After(120, () => { GrabBackdrop(); Show(); redraw(); });
            }
            else redraw();
        }
    }
}
