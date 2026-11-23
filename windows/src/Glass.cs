// Corner - the Glass skin, made to look like the Liquid Glass of iPadOS 26: clear glass that shows what is behind
// it softly blurred and bent at the rim, with a bright double edge; light over light things and dark over dark ones.
// Here: the picture of what is behind (Backdrop), the sheet a panel is made of (GlassSheet), the rim and gloss of the
// boxes on it (RimBorder), and the change from one skin to the other. The colours of both skins are in T (Corner.cs).
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WF = System.Windows.Forms;

namespace Corner
{
    /// A box lying on the glass. Over its fill it gets a soft glow inside the edge and a bright hairline round it,
    /// brightest along the top; a pill is also glossy across its upper half. In the Sage skin these are drawn with
    /// fully transparent colours, so nothing shows, and nothing has to be redrawn when the skin changes.
    class RimBorder : Border
    {
        protected virtual bool Glossy { get { return false; } }
        protected virtual bool GlassRim { get { return true; } }

        public RimBorder() { RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor); } // the grains (Material.cs) are drawn as they are, not smoothed

        protected override void OnRender(DrawingContext dc)
        {
            if (Mat.On) { Mat.Under(dc, this); base.OnRender(dc); Mat.Over(dc, this, Glossy); return; } // Metal, Ceramic, Paper (Material.cs)
            base.OnRender(dc);
            double w = ActualWidth, h = ActualHeight;
            if (!GlassRim || w < 6 || h < 6 || Background == null || Background == Brushes.Transparent) return;
            double r = Math.Min(CornerRadius.TopLeft, Math.Min(w, h) / 2);
            if (Glossy) dc.DrawRoundedRectangle(T.Gloss, null, new Rect(0, 0, w, h), r, r);
            dc.DrawRoundedRectangle(null, T.BoxGlowPen, new Rect(1.5, 1.5, w - 3, h - 3), Math.Max(0, r - 1.5), Math.Max(0, r - 1.5));
            dc.DrawRoundedRectangle(null, T.RimPen, new Rect(0.5, 0.5, w - 1, h - 1), Math.Max(0, r - 0.5), Math.Max(0, r - 0.5));
        }
    }

    /// The picture behind the glass: the desktop as it was when the panel opened, at an eighth of its size and blurred.
    /// It is kept in memory only, and only in this small blurred form. From it comes the picture that is shown, made
    /// more colourful and evened out for reading: under light glass the dark parts are lifted, under dark glass the
    /// bright parts are dimmed, and what is already the right way round is left as vivid as it is.
    static class Backdrop
    {
        public const int K = 8;            // the picture is 1/K of the screen
        public static BitmapSource Image;  // null until the first grab, or when the screen could not be read
        public static BitmapSource Sharp;  // test renders only: the made-up wallpaper before it is blurred, at half size
        public static double Left, Top;    // where the picture's corner is on the screen, in pixels
        public static int Stamp;           // goes up with every new picture, so a sheet knows to draw again

        static byte[] raw; static int rw, rh; // the blurred picture, before it is toned for light or dark glass
        static byte[] toned;                  // and after: what Image is made of

        public static int Blur = 2;        // box radius in the small picture's pixels, three passes: about 20 px on screen
        public static double Saturation = 1.35;
        public static double LiftA = 0.42; // light glass: how far black is lifted towards white (mid-tones much less)
        public static double DimA = 0.62;  // dark glass: how far white is dimmed (mid-tones less)

        /// `fake` (test renders): "light", "dark" or "busy" draws a wallpaper instead of reading the screen.
        public static void Grab(string fake)
        {
            try
            {
                var vs = WF.SystemInformation.VirtualScreen;
                int w = Math.Max(8, vs.Width / K), h = Math.Max(8, vs.Height / K);
                byte[] px = fake != null ? Wallpaper(fake, vs.Width, vs.Height, w, h) : Screen(vs, w, h);
                BoxBlur(px, w, h, Blur);
                raw = px; rw = w; rh = h; Left = vs.Left; Top = vs.Top;
                Tone();
            }
            catch (Exception ex) { Store.Log(ex); raw = null; Image = null; Stamp++; }
        }

        // A picture taken for the other skins too, each time the panel opens, on a thread of its own: it costs the
        // opening nothing, and a change to Glass while the panel is up then has its picture ready. (Taking one at that
        // moment means leaving the panel out of a screen capture first, which shows as a pause and a flicker.)
        class Spare { public byte[] Px; public int W, H; public double Left, Top; }
        static volatile Spare spare;
        static int spareRun;

        public static bool HasSpare { get { return spare != null; } }

        /// Called while the panel is still off the screen.
        public static void GrabSpare(string fake)
        {
            int run = System.Threading.Interlocked.Increment(ref spareRun);
            spare = null;
            var vs = WF.SystemInformation.VirtualScreen;
            int w = Math.Max(8, vs.Width / K), h = Math.Max(8, vs.Height / K);
            Action<byte[]> keep = px => { BoxBlur(px, w, h, Blur); if (run == spareRun) spare = new Spare { Px = px, W = w, H = h, Left = vs.Left, Top = vs.Top }; };
            try
            {
                if (fake != null) { keep(Wallpaper(fake, vs.Width, vs.Height, w, h)); return; } // (a test's wallpaper is drawn, which needs this thread)
                System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { keep(Screen(vs, w, h)); } catch { } });
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// The picture in use, kept as the spare: for going from Glass to another skin and back while the panel is up.
        public static void KeepAsSpare()
        {
            if (raw != null) spare = new Spare { Px = raw, W = rw, H = rh, Left = Left, Top = Top };
        }

        /// The spare becomes the picture in use. False when there is none.
        public static bool UseSpare()
        {
            var s = spare;
            if (s == null) return false;
            raw = s.Px; rw = s.W; rh = s.H; Left = s.Left; Top = s.Top;
            Tone();
            return Image != null;
        }

        /// Makes the picture that is shown from the blurred one, for the glass in use (T.Dark).
        public static void Tone()
        {
            if (raw == null) return;
            var p = (byte[])raw.Clone(); bool dark = T.Dark;
            for (int i = 0; i < p.Length; i += 4)
            {
                double b = p[i], g = p[i + 1], r = p[i + 2], grey = 0.114 * b + 0.587 * g + 0.299 * r;
                b = Clamp(grey + (b - grey) * Saturation); g = Clamp(grey + (g - grey) * Saturation); r = Clamp(grey + (r - grey) * Saturation);
                double l = grey / 255;
                if (dark) { double f = 1 - DimA * l; b *= f; g *= f; r *= f; }
                else { double a = LiftA * (1 - l) * (1 - l); b += (255 - b) * a; g += (255 - g) * a; r += (255 - r) * a; }
                p[i] = (byte)b; p[i + 1] = (byte)g; p[i + 2] = (byte)r; p[i + 3] = 255;
            }
            var img = BitmapSource.Create(rw, rh, 96, 96, PixelFormats.Bgr32, null, p, rw * 4);
            img.Freeze();
            toned = p; Image = img; Stamp++;
        }

        /// The part of the picture behind a rectangle of the screen (in pixels), enlarged to the screen's own size.
        /// A sheet draws this pixel for pixel. Enlarging the small picture afresh on every frame, which is what an
        /// ImageBrush does, costs about eight times as much now that Corner draws without the graphics card.
        /// Null when there is no picture, or the rectangle is too big to be worth holding.
        public static BitmapSource Enlarge(Int32Rect at)
        {
            var src = toned; int sw = rw, sh = rh, w = at.Width, h = at.Height;
            if (src == null || sw < 2 || sh < 2 || w < 1 || h < 1 || (long)w * h > 6000000) return null;
            var px = new byte[w * h * 4];
            // each column: the two pixels of the small picture it lies between, and how far along (0 to 256)
            var xi = new int[w]; var xf = new int[w];
            for (int x = 0; x < w; x++)
            {
                double u = (at.X - Left + x + 0.5) / K - 0.5; int i = (int)Math.Floor(u), f = (int)((u - i) * 256);
                if (i < 0) { i = 0; f = 0; } else if (i > sw - 2) { i = sw - 2; f = 256; }
                xi[x] = i * 4; xf[x] = f;
            }
            var row = new int[sw * 4];
            for (int y = 0; y < h; y++)
            {
                double v = (at.Y - Top + y + 0.5) / K - 0.5; int j = (int)Math.Floor(v), fy = (int)((v - j) * 256);
                if (j < 0) { j = 0; fy = 0; } else if (j > sh - 2) { j = sh - 2; fy = 256; }
                int r0 = j * sw * 4, r1 = r0 + sw * 4, gy = 256 - fy;
                for (int k = 0; k < row.Length; k++) row[k] = src[r0 + k] * gy + src[r1 + k] * fy;
                int o = y * w * 4;
                for (int x = 0; x < w; x++, o += 4)
                {
                    int i = xi[x], f = xf[x], g = 256 - f;
                    px[o] = (byte)((row[i] * g + row[i + 4] * f + 32768) >> 16);
                    px[o + 1] = (byte)((row[i + 1] * g + row[i + 5] * f + 32768) >> 16);
                    px[o + 2] = (byte)((row[i + 2] * g + row[i + 6] * f + 32768) >> 16);
                    px[o + 3] = 255;
                }
            }
            var img = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, w * 4);
            img.Freeze();
            return img;
        }

        static double Clamp(double v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        /// How light the part of the screen inside a rectangle (in screen pixels) is, 0 to 1. -1 when there is no picture.
        public static double Light(Rect screen)
        {
            if (raw == null) return -1;
            int x0 = Math.Max(0, Math.Min(rw - 1, (int)((screen.Left - Left) / K))), x1 = Math.Max(x0 + 1, Math.Min(rw, (int)Math.Ceiling((screen.Right - Left) / K)));
            int y0 = Math.Max(0, Math.Min(rh - 1, (int)((screen.Top - Top) / K))), y1 = Math.Max(y0 + 1, Math.Min(rh, (int)Math.Ceiling((screen.Bottom - Top) / K)));
            double sum = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) { int i = (y * rw + x) * 4; sum += 0.114 * raw[i] + 0.587 * raw[i + 1] + 0.299 * raw[i + 2]; }
            return sum / ((x1 - x0) * (y1 - y0)) / 255;
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
                dc.DrawRectangle(new LinearGradientBrush(T.C("#05081C"), T.C("#150A33"), 40), null, all);
                dc.DrawEllipse(Blob("#3A1FA8"), null, new Point(sw * 0.86, sh * 0.2), sw * 0.2, sw * 0.2);
                dc.DrawEllipse(Blob("#0B4FB8"), null, new Point(sw * 0.64, sh * 0.5), sw * 0.26, sw * 0.2);
                dc.DrawEllipse(Blob("#7A2BD6"), null, new Point(sw * 0.97, sh * 0.62), sw * 0.14, sw * 0.16);
                dc.DrawEllipse(Blob("#0A2F5C"), null, new Point(sw * 0.3, sh * 0.8), sw * 0.3, sw * 0.2);
            }
            else
            {   // big waves of strong colour, like a tablet's wallpaper
                dc.DrawRectangle(new LinearGradientBrush(T.C("#1767E0"), T.C("#6C3FE6"), 35), null, all);
                dc.DrawEllipse(Blob("#19C3F2"), null, new Point(sw * 0.74, sh * 0.2), sw * 0.2, sw * 0.17);
                dc.DrawEllipse(Blob("#2FE0C2"), null, new Point(sw * 1.0, sh * 0.05), sw * 0.14, sw * 0.14);
                dc.DrawEllipse(Blob("#E052D8"), null, new Point(sw * 0.66, sh * 0.62), sw * 0.18, sw * 0.16);
                dc.DrawEllipse(Blob("#FF8A3C"), null, new Point(sw * 0.9, sh * 0.78), sw * 0.24, sw * 0.2);
                dc.DrawEllipse(Blob("#FFC94D"), null, new Point(sw * 1.0, sh * 0.5), sw * 0.1, sw * 0.12);
                dc.DrawEllipse(Blob("#2A8BF5"), null, new Point(sw * 0.4, sh * 0.35), sw * 0.26, sw * 0.2);
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

    /// The sheet of glass a panel is made of. From the back: the picture of what is behind it; the same picture again in
    /// rings round the edge, each drawn from further in, which is the bend a thick rim gives; a thin veil; a glow that
    /// fades in from the edge; and two bright hairlines, lit from the top left. It keeps itself in step with the screen
    /// as its panel opens, grows and is dragged. Put it over an opaque box of the same shape (which casts the shadow).
    class GlassSheet : FrameworkElement
    {
        public double Radius = 28;
        public bool Plain; // no picture: frosted glass with the same veil and rim (the welcome window)

        static readonly double[] Bend = { 13, 8.5, 5, 2.4, 0.9 }; // how far in each ring looks, outermost first
        const double RingW = 2.2;

        Rect view = Rect.Empty; int stamp = -1; bool hooked; Window win;
        BitmapSource big; Int32Rect bigAt; int bigStamp = -1; // the picture at the screen's own size, for the part of the screen the window covers
        readonly System.Diagnostics.Stopwatch since = new System.Diagnostics.Stopwatch();

        public GlassSheet()
        {
            IsHitTestVisible = false;
            if (Mat.On) RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor); // a grain is drawn as it is
            // In step with the screen: on every frame for a moment after it appears (the opening moves it without a
            // new layout), and after that only when its layout or its window's place changes. Nothing runs while idle.
            EventHandler laidOut = (s, e) => Sync();
            Loaded += (s, e) =>
            {
                var w = Window.GetWindow(this);
                if (w != null && w != win) { win = w; win.LocationChanged += (s2, e2) => Sync(); }
                LayoutUpdated -= laidOut; LayoutUpdated += laidOut;
                Follow();
            };
            IsVisibleChanged += (s, e) => { if (IsVisible) Follow(); };
            Unloaded += (s, e) => { Unhook(); LayoutUpdated -= laidOut; }; // (a closed notice must not be kept alive by it)
        }

        /// Keeps step on every frame for the next second.
        public void Follow()
        {
            since.Restart();
            if (!hooked) { hooked = true; CompositionTarget.Rendering += Tick; }
            Sync();
        }

        void Unhook() { if (hooked) { hooked = false; CompositionTarget.Rendering -= Tick; } }
        void Tick(object s, EventArgs e) { Sync(); if (since.ElapsedMilliseconds > 1000) Unhook(); }

        /// Finds where the picture of the screen lies in this sheet's own units, and draws again if that has changed.
        public void Sync()
        {
            if (Plain || !T.Glass || Backdrop.Image == null || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0) return;
            Point a, b;
            try { a = PointToScreen(new Point(0, 0)); b = PointToScreen(new Point(ActualWidth, ActualHeight)); }
            catch { return; }
            double sx = (b.X - a.X) / ActualWidth, sy = (b.Y - a.Y) / ActualHeight;
            if (sx <= 0 || sy <= 0) return;
            bool fresh = Enlarge();
            var v = big != null ? new Rect((bigAt.X - a.X) / sx, (bigAt.Y - a.Y) / sy, bigAt.Width / sx, bigAt.Height / sy)
                : new Rect((Backdrop.Left - a.X) / sx, (Backdrop.Top - a.Y) / sy, Backdrop.Image.PixelWidth * Backdrop.K / sx, Backdrop.Image.PixelHeight * Backdrop.K / sy);
            if (v == view && stamp == Backdrop.Stamp && !fresh) return;
            view = v; stamp = Backdrop.Stamp;
            InvalidateVisual();
        }

        /// Keeps `big` (Backdrop.Enlarge) for the picture in use and for where the window is now. True when it was
        /// made again: about 5 ms for the corner panel's window, once an opening, and once a step while it is dragged.
        bool Enlarge()
        {
            if (win == null) return false;
            Point p, q;
            try { p = win.PointToScreen(new Point(0, 0)); q = win.PointToScreen(new Point(win.ActualWidth, win.ActualHeight)); }
            catch { return false; }
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            var at = new Int32Rect(x, y, Math.Max(0, (int)Math.Ceiling(q.X) - x), Math.Max(0, (int)Math.Ceiling(q.Y) - y));
            if (bigStamp == Backdrop.Stamp && at == bigAt) return false;
            big = Backdrop.Enlarge(at); bigAt = at; bigStamp = Backdrop.Stamp;
            // pixel for pixel when it is at the screen's size; the small picture has to be smoothed as it is enlarged
            RenderOptions.SetBitmapScalingMode(this, big != null ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Unspecified);
            return true;
        }

        /// The picture, placed so that what is `dx`, `dy` further in shows here (0, 0: where it really is).
        Brush Picture(double w, double h, double dx, double dy)
        {
            double kx = Math.Max(0.3, 1 - dx / (w / 2)), ky = Math.Max(0.3, 1 - dy / (h / 2)), cx = w / 2, cy = h / 2;
            return new ImageBrush(big ?? Backdrop.Image)
            {
                Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(cx + (view.X - cx) / kx, cy + (view.Y - cy) / ky, view.Width / kx, view.Height / ky)
            };
        }

        static Rect In(double w, double h, double by) { return new Rect(by, by, Math.Max(0, w - 2 * by), Math.Max(0, h - 2 * by)); }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 8 || h < 8) return;
            double r = Math.Min(Radius, Math.Min(w, h) / 2);
            var all = new Rect(0, 0, w, h);
            if (Mat.On) { Mat.Sheet(dc, w, h, r); return; } // Metal, Ceramic, Paper: the panel's grain and edge (Material.cs)
            bool picture = !Plain && Backdrop.Image != null && !view.IsEmpty;
            if (picture)
            {
                dc.DrawRoundedRectangle(Picture(w, h, 0, 0), null, all, r, r);
                for (int i = Bend.Length - 1; i >= 0; i--)
                {   // inner rings first; each overlaps the next one out by a hair, so no seam shows
                    double at = (i + 0.5) * RingW, rr = Math.Max(0, r - at);
                    if (2 * at + RingW >= Math.Min(w, h)) continue;
                    dc.DrawRoundedRectangle(null, new Pen(Picture(w, h, Bend[i], Bend[i]), RingW + 0.8), In(w, h, at), rr, rr);
                }
            }
            else dc.DrawRoundedRectangle(T.GlassBase, null, all, r, r);

            dc.DrawRoundedRectangle(T.GlassVeil, null, all, r, r);
            for (int i = 0; i < T.GlowPens.Length; i++)
            {
                double at = i + 0.5, rr = Math.Max(0, r - at);
                dc.DrawRoundedRectangle(null, T.GlowPens[i], In(w, h, at), rr, rr);
            }
            dc.DrawRoundedRectangle(null, T.SheetRim2Pen, In(w, h, 2.6), Math.Max(0, r - 2.6), Math.Max(0, r - 2.6));
            dc.DrawRoundedRectangle(null, T.SheetRimPen, In(w, h, 0.5), Math.Max(0, r - 0.5), Math.Max(0, r - 0.5));
        }
    }

    partial class Shell
    {
        Border glassTint;       // holds the panel's sheet of glass; shown only in the Glass skin
        GlassSheet glassSheet;  // the sheet itself, between the panel's shadow-casting box and its content

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
        /// The other skins take a spare one in the background, in case Glass is chosen while the panel is up (SetSkin).
        void GrabBackdrop()
        {
            if (!T.Glass) { if (!IsVisible) Backdrop.GrabSpare(FakeBackdrop); return; }
            if (IsVisible && Backdrop.Image != null) return;
            var took = System.Diagnostics.Stopwatch.StartNew();
            Backdrop.Grab(FakeBackdrop);
            if (demoRun) try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(Store.Dir, "backdrop.txt"), (Backdrop.Image == null ? "failed" : Backdrop.Image.PixelWidth + "x" + Backdrop.Image.PixelHeight) + " in " + took.ElapsedMilliseconds + " ms" + Environment.NewLine);
                if (Backdrop.Image != null && Environment.GetEnvironmentVariable("CORNER_DUMP") == "1")
                {   // test hook: the picture itself, to see that the panel is not in it
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(Backdrop.Image));
                    using (var fs = System.IO.File.Create(System.IO.Path.Combine(Store.Dir, "backdrop.png"))) enc.Save(fs);
                }
            }
            catch { }
        }

        /// Liquid Glass is light over light things and dark over dark ones. This looks at the part of the screen behind
        /// a rectangle (in screen pixels) and changes the glass if it has to. Between light and dark it stays as it
        /// was, so it does not flip from one opening to the next over a mid-grey.
        void AdaptGlass(Rect screen)
        {
            if (!T.Glass) return;
            double light = Backdrop.Light(screen);
            if (light < 0) return;
            bool dark = T.Dark ? light < 0.38 : light < 0.30;
            if (dark == T.Dark) return;
            T.Use(1, dark);
            Backdrop.Tone();
            SkinPanel();
        }

        /// The same for the panel as it opens. The corner panel opens small and then grows, so it is judged on the
        /// part of the screen it will grow over as well.
        void AdaptGlassPanel()
        {
            if (!T.Glass) return;
            try
            {
                card.UpdateLayout();
                var a = card.PointToScreen(new Point(0, 0)); var b = card.PointToScreen(new Point(card.ActualWidth, card.ActualHeight));
                var rect = new Rect(a, b);
                double tall = 380 * rect.Width / Math.Max(1, card.ActualWidth);
                if (!full && rect.Height < tall) { if (!OnTop) rect.Y -= tall - rect.Height; rect.Height = tall; }
                AdaptGlass(rect);
            }
            catch { }
        }

        /// The panel's own layers for the skin in use. Called from Restyle (Ui.cs).
        void SkinPanel()
        {
            if (T.Glass)
            {
                panelBg.Background = T.GlassBase; // under the sheet: it casts the shadow, and shows if the screen could not be read
                panelBg.BorderThickness = new Thickness(0);
                glassSheet.Radius = panelBg.CornerRadius.TopLeft; glassTint.Visibility = Visibility.Visible;
                shadow.Tint = T.GlassShadow; shadow.Strength = T.GlassShadowOpacity;
                glassSheet.Sync(); glassSheet.InvalidateVisual();
            }
            else if (Mat.On)
            {   // Metal, Ceramic, Paper: the box in the skin's colour, and over it the sheet with its grain and edge (Material.cs)
                panelBg.Background = T.Bg; panelBg.BorderThickness = new Thickness(0);
                glassSheet.Radius = panelBg.CornerRadius.TopLeft; glassTint.Visibility = Visibility.Visible;
                RenderOptions.SetBitmapScalingMode(glassSheet, BitmapScalingMode.NearestNeighbor);
                shadow.Tint = T.Skin == Mat.Metal ? Color.FromRgb(20, 22, 26) : T.Skin == Mat.Ceramic ? Color.FromRgb(70, 60, 36) : Color.FromRgb(40, 56, 74);
                shadow.Strength = T.Skin == Mat.Metal ? 0.36 : T.Skin == Mat.Ceramic ? 0.3 : 0.24;
                glassSheet.InvalidateVisual();
            }
            else
            {
                panelBg.Background = T.Bg; panelBg.BorderThickness = new Thickness(1);
                glassTint.Visibility = Visibility.Collapsed;
                shadow.Tint = Color.FromRgb(60, 54, 34); shadow.Strength = 0.3;
            }
        }

        /// A box keeps what it drew. After a change of skin every box and sheet is asked to draw itself again.
        static void Repaint(DependencyObject o)
        {
            if (o == null) return;
            var u = o as UIElement;
            if (u is RimBorder || u is GlassSheet) u.InvalidateVisual();
            for (int i = 0, n = VisualTreeHelper.GetChildrenCount(o); i < n; i++) Repaint(VisualTreeHelper.GetChild(o, i));
        }

        /// Settings: the other skin. The colours change where they are; the panel is laid out and drawn again around them.
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
        const uint ExcludeFromCapture = 0x11; // Windows 10 2004 and later: the window is left out of screen captures

        void SetSkin(int i)
        {
            if (i == T.Skin) return;
            Settings.Set("Skin", i);
            Action redraw = () =>
            {
                var took = System.Diagnostics.Stopwatch.StartNew();
                if (shown) Place(); Rebuild(); RefreshEnergy(false); if (settingsOpen) DrawSettings(); Repaint(card);
                if (demoRun) try { System.IO.File.AppendAllText(System.IO.Path.Combine(Store.Dir, "backdrop.txt"), "skin " + T.Names[T.Skin] + " redrawn in " + took.ElapsedMilliseconds + " ms" + Environment.NewLine); } catch { }
                if (demoRun) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { try { System.IO.File.AppendAllText(System.IO.Path.Combine(Store.Dir, "backdrop.txt"), "  and on the screen after " + took.ElapsedMilliseconds + " ms" + Environment.NewLine); } catch { } })); // (laid out and drawn)
            };
            if (i != 1 && T.Glass && IsVisible) Backdrop.KeepAsSpare(); // leaving Glass: its picture stays good until the panel closes
            if (i == 1 && IsVisible && Backdrop.HasSpare)
            {   // the picture taken as the panel opened is waiting: Glass comes at once, like the other skins
                var sw = System.Diagnostics.Stopwatch.StartNew(); long t1, t2, t3;
                T.Use(i);
                if (Backdrop.UseSpare())
                {
                    AdaptGlassPanel(); // light or dark glass is settled first, from where the panel already is: settling it after the redraw laid the whole panel out twice, which showed as a jerk
                    t1 = sw.ElapsedMilliseconds; redraw(); t2 = sw.ElapsedMilliseconds; AdaptGlassPanel(); t3 = sw.ElapsedMilliseconds;
                    if (demoRun) { card.UpdateLayout(); long t4 = sw.ElapsedMilliseconds; try { System.IO.File.AppendAllText(System.IO.Path.Combine(Store.Dir, "backdrop.txt"), "  spare " + t1 + ", redraw " + (t2 - t1) + ", adapt " + (t3 - t2) + ", layout " + (t4 - t3) + " ms, dark " + T.Dark + Environment.NewLine); } catch { } }
                    return;
                }
            }
            if (i == 1 && IsVisible)
            {   // No picture (the panel was only just opened): one has to be taken now. The panel is left out of the screen capture for a
                // moment instead of being hidden, so it stays where it is, in the old skin, until the glass is ready.
                var h = new WindowInteropHelper(this).Handle;
                bool left = false;
                try { left = SetWindowDisplayAffinity(h, ExcludeFromCapture); } catch { }
                if (!left)
                {   // older Windows: the panel has to go off the screen for the picture, so it blinks once
                    T.Use(i); Backdrop.Image = null; Hide();
                    After(120, () => { GrabBackdrop(); Show(); redraw(); AdaptGlassPanel(); });
                    return;
                }
                After(50, () => // a frame for the screen to be drawn without the panel
                {
                    T.Use(i); Backdrop.Image = null;
                    GrabBackdrop();
                    try { SetWindowDisplayAffinity(h, 0); } catch { }
                    AdaptGlassPanel(); redraw(); AdaptGlassPanel(); // (light or dark first: see above)
                });
                return;
            }
            T.Use(i);
            redraw();
        }
    }
}
