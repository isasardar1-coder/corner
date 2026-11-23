// Corner - the three made skins: Metal (brushed silver, white enamel, steel), Ceramic (sage stoneware and glazed
// porcelain) and Paper (blue card and white paper). Their colours are in T (Corner.cs); this is what gives them depth.
// Every box says what it is by the colour it is filled with (a tray, a card or pill, the strong colour, the accent),
// and is drawn accordingly: a shadow under it, then its fill, then its grain, bevel and gloss over that.
// Corner draws without the graphics card (see NOTES.md), so nothing here is blurred: a soft shadow is a few faint
// shapes one over the other, an edge is a stroke with a gradient along it, and the grains are small tiled pictures.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Corner
{
    static class Mat
    {
        public const int Metal = 2, Ceramic = 3, Paper = 4;
        public static bool On { get { return T.Skin >= 2; } }

        // ------------------------------------------------------------ brushes, made once and kept

        static readonly Brush[] blacks = new Brush[256], whites = new Brush[256];
        static Brush K(double a) { return Flat(blacks, a, 0); }
        static Brush W(double a) { return Flat(whites, a, 255); }
        static Brush Flat(Brush[] set, double a, byte v)
        {
            int i = Math.Max(0, Math.Min(255, (int)(a * 255 + 0.5)));
            if (set[i] == null) { var b = new SolidColorBrush(Color.FromArgb((byte)i, v, v, v)); b.Freeze(); set[i] = b; }
            return set[i];
        }

        static readonly Dictionary<string, Brush> grads = new Dictionary<string, Brush>();

        /// A gradient across a box, from a short description: "W.9@0 K.1@1" is white at 90% at the start and black
        /// at 10% at the end. `dir`: 'v' top to bottom, 'h' left to right, 'd' top left to bottom right.
        /// `k` scales every strength (for rings that fade as they go in).
        static Brush G(char dir, string spec, double k = 1)
        {
            string key = dir + spec + (k == 1 ? "" : k.ToString("0.###", CultureInfo.InvariantCulture));
            Brush found;
            if (grads.TryGetValue(key, out found)) return found;
            var stops = new GradientStopCollection();
            foreach (var part in spec.Split(' '))
            {
                int at = part.IndexOf('@');
                double a = double.Parse(part.Substring(1, at - 1), CultureInfo.InvariantCulture) * k, off = double.Parse(part.Substring(at + 1), CultureInfo.InvariantCulture);
                byte v = part[0] == 'W' ? (byte)255 : (byte)0;
                stops.Add(new GradientStop(Color.FromArgb((byte)Math.Max(0, Math.Min(255, a * 255 + 0.5)), v, v, v), off));
            }
            var g = new LinearGradientBrush(stops, new Point(0, 0), dir == 'v' ? new Point(0, 1) : dir == 'h' ? new Point(1, 0) : new Point(1, 1));
            g.Freeze();
            grads[key] = g;
            return g;
        }

        // ------------------------------------------------------------ the grains

        static ImageBrush brushed, brushedSoft, grain, grainSoft, grainStrong, speckle;

        static ImageBrush Tile(int w, int h, Func<int, int, double> value)
        {
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {   // above nought a touch of white, below it a touch of black
                    double v = value(x, y);
                    byte a = (byte)Math.Min(255, Math.Abs(v) * 255), c = v > 0 ? a : (byte)0; // (the colour is stored already weighed by its strength)
                    int i = (y * w + x) * 4;
                    px[i] = c; px[i + 1] = c; px[i + 2] = c; px[i + 3] = a;
                }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4);
            bmp.Freeze();
            var b = new ImageBrush(bmp) { TileMode = TileMode.Tile, Stretch = Stretch.Fill, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, w, h) };
            b.Freeze();
            return b;
        }

        static void MakeGrains()
        {
            if (brushed != null) return;
            var rnd = new Random(7);
            // brushed metal: long fine streaks, and each row a little lighter or darker than the next
            int bw = 320, bh = 160, reach = 34;
            var rows = new double[bh]; var streak = new double[bw * bh];
            for (int y = 0; y < bh; y++) rows[y] = rnd.NextDouble() * 2 - 1;
            for (int y = 0; y < bh; y++)
            {
                var line = new double[bw];
                for (int x = 0; x < bw; x++) line[x] = rnd.NextDouble() * 2 - 1;
                double sum = 0;
                for (int k = -reach; k <= reach; k++) sum += line[(k + bw) % bw];
                for (int x = 0; x < bw; x++)
                {
                    streak[y * bw + x] = sum / Math.Sqrt(2 * reach + 1);
                    sum += line[(x + reach + 1) % bw] - line[(x - reach + bw) % bw];
                }
            }
            Func<int, int, double> metal = (x, y) => 0.5 * (rows[y] + rows[(y + 1) % bh]) * 0.45 + streak[y * bw + x] * 0.55;
            brushed = Tile(bw, bh, (x, y) => metal(x, y) * 0.085);
            brushedSoft = Tile(bw, bh, (x, y) => metal(x, y) * 0.068);
            // paper: a fine even grain
            int gw = 192, gh = 192; var gv = new double[gw * gh];
            for (int i = 0; i < gv.Length; i++) gv[i] = rnd.NextDouble() * 2 - 1;
            Func<int, int, double> paper = (x, y) => (gv[y * gw + x] + gv[y * gw + (x + 1) % gw] + gv[((y + 1) % gh) * gw + x]) / 3;
            grain = Tile(gw, gh, (x, y) => paper(x, y) * 0.06);
            grainSoft = Tile(gw, gh, (x, y) => paper(x, y) * 0.038);
            grainStrong = Tile(gw, gh, (x, y) => paper(x, y) * 0.075);
            // stoneware: almost smooth, with a dark or light speck here and there
            var sv = new double[gw * gh];
            for (int i = 0; i < sv.Length; i++) { double q = rnd.NextDouble(); sv[i] = q < 0.0022 ? -0.085 : q < 0.004 ? 0.12 : (rnd.NextDouble() * 2 - 1) * 0.016; }
            speckle = Tile(gw, gh, (x, y) => sv[y * gw + x]);
        }

        // ------------------------------------------------------------ what a box is

        const int None = 0, Pressed = 1, Raised = 2, Strong = 3, Accent = 4, Soft = 5;

        static int Role(Brush b)
        {
            if (b == T.Tray) return Pressed;
            if (b == T.Field || b == T.Lift || b == T.Raised || b == T.Pop || b == T.Knob || b == T.DangerSoft) return Raised;
            if (b == T.Strong || b == T.Dip || b == T.StrongLift) return Strong;
            if (b == T.Accent || b == T.AccentOn || b == T.Main) return Accent;
            if (b == T.Soft) return Soft;
            return None;
        }

        // the shadow under a box that stands up from what it is on: (down, out, strength), nearest first
        static readonly double[] MetalShadow = { 1, 0, 0.26, 2, 0.6, 0.12, 3.6, 1.6, 0.05 };
        static readonly double[] CeramicShadow = { 1, 0, 0.10, 2.2, 0.8, 0.07, 4, 2, 0.045, 6.5, 3.5, 0.025 };
        static readonly double[] PaperShadow = { 0.8, 0, 0.12, 2, 0.8, 0.07, 4.5, 2.2, 0.035 };
        static readonly double[] PaperLaid = { 0.8, 0, 0.06, 2, 1, 0.03 }; // a tray in Paper: a sheet laid on the card

        static bool Size(Border b, out double w, out double h, out double r)
        {
            w = b.ActualWidth; h = b.ActualHeight; r = Math.Min(b.CornerRadius.TopLeft, Math.Min(w, h) / 2);
            return w >= 6 && h >= 6;
        }

        static Rect In(double w, double h, double by) { return new Rect(by, by, Math.Max(0, w - 2 * by), Math.Max(0, h - 2 * by)); }
        static void Fill(DrawingContext dc, Brush b, double w, double h, double r) { dc.DrawRoundedRectangle(b, null, new Rect(0, 0, w, h), r, r); }
        static void Ring(DrawingContext dc, Brush b, double thick, double inset, double w, double h, double r)
        {
            if (2 * inset + thick >= Math.Min(w, h)) return;
            double rr = Math.Max(0, r - inset);
            dc.DrawRoundedRectangle(null, new Pen(b, thick), In(w, h, inset), rr, rr);
        }

        /// Before the box's own fill: the shadow it casts.
        public static void Under(DrawingContext dc, Border box)
        {
            double w, h, r;
            int role = Role(box.Background);
            if (role == None || !Size(box, out w, out h, out r)) return;
            double[] sh = T.Skin == Metal ? (role == Pressed ? null : MetalShadow) : T.Skin == Ceramic ? (role == Pressed ? null : CeramicShadow) : role == Pressed ? PaperLaid : PaperShadow;
            if (sh == null) return;
            double s = Math.Max(0.55, Math.Min(1.5, h / 34)); // a small pill stands less high than a card
            for (int i = 0; i < sh.Length; i += 3)
            {
                double down = sh[i] * s, out_ = sh[i + 1] * s;
                dc.DrawRoundedRectangle(K(sh[i + 2]), null, new Rect(-out_, down - out_, w + 2 * out_, h + 2 * out_), r + out_, r + out_);
            }
        }

        /// After the fill: grain, bevel and gloss.
        public static void Over(DrawingContext dc, Border box, bool pill)
        {
            double w, h, r;
            int role = Role(box.Background);
            if (role == None || !Size(box, out w, out h, out r)) return;
            MakeGrains();
            if (T.Skin == Metal) MetalBox(dc, role, w, h, r);
            else if (T.Skin == Ceramic) CeramicBox(dc, role, pill, w, h, r);
            else PaperBox(dc, role, w, h, r);
        }

        static void MetalBox(DrawingContext dc, int role, double w, double h, double r)
        {
            if (role == Pressed)
            {   // sunk into the plate: brushed like it, dark under its top edge, bright along its bottom one
                Fill(dc, brushed, w, h, r);
                for (int i = 0; i < 4; i++) Ring(dc, G('v', "K.36@0 K0@.42 W0@.6 W.7@1", Math.Pow(1 - i / 4.0, 1.5)), 1.2, i + 0.6, w, h, r);
            }
            else if (role == Raised)
            {   // white enamel: a crisp bevel, lit from above
                Fill(dc, G('v', "W.3@0 W0@.4 K0@.6 K.07@1"), w, h, r);
                Ring(dc, G('v', "W1@0 W.3@.45 K.1@.7 K.24@1"), 1.2, 0.6, w, h, r);
            }
            else if (role == Strong)
            {   // dark lacquer: a hard dark edge and a thin line of light inside it
                Fill(dc, G('v', "W.11@0 W0@.45"), w, h, r);
                Ring(dc, K(0.5), 1, 0.5, w, h, r);
                Ring(dc, G('v', "W.34@0 W0@.5 W.08@1"), 1, 1.5, w, h, r);
            }
            else
            {   // steel: brushed, with a band of light across it
                Fill(dc, role == Accent ? brushed : brushedSoft, w, h, r);
                Fill(dc, G('h', "K.16@0 W.5@.28 W.12@.55 K.14@1"), w, h, r);
                Ring(dc, G('v', "W.85@0 W.1@.5 K.34@1"), 1.1, 0.55, w, h, r);
            }
        }

        static void CeramicBox(DrawingContext dc, int role, bool pill, double w, double h, double r)
        {
            if (role == Pressed)
            {   // pressed into the slab: a soft shade under its top edge and a soft lip of light at the bottom
                for (int i = 0; i < 6; i++) Ring(dc, G('v', "K.15@0 K0@.4 W0@.62 W.55@1", Math.Pow(1 - i / 6.0, 1.5)), 1.25, i + 0.6, w, h, r);
                return;
            }
            bool white = role == Raised;
            // glaze: rounded like a pillow, so light above and shade below, and bright where it turns towards the light
            Fill(dc, G('v', white ? (pill ? "W.5@0 W0@.5 K0@.62 K.06@1" : "W.3@0 W0@.3 K0@.75 K.04@1") : "W.24@0 W0@.5 K0@.7 K.1@1"), w, h, r);
            if (!white) Ring(dc, K(role == Strong ? 0.32 : 0.12), 1, 0.5, w, h, r);
            Ring(dc, G('v', white ? "W.95@0 W.3@.4 K.02@.7 K.07@1" : "W.6@0 W0@.38 W0@.8 W.1@1"), white ? 1.4 : 1.2, white ? 0.7 : 1.7, w, h, r);
        }

        static void PaperBox(DrawingContext dc, int role, double w, double h, double r)
        {
            // paper: a grain, stronger on the dark card, and the faintest light along a top edge
            if (role != Raised || h > 60) Fill(dc, role == Strong ? grainStrong : role == Raised ? grainSoft : grain, w, h, r); // (a small white pill goes without)
            if (role != Pressed) Ring(dc, G('v', role == Strong ? "W.14@0 W0@.3" : "W.7@0 W0@.3"), 1, 0.5, w, h, r);
        }

        /// The panel itself (GlassSheet draws this in place of glass): its grain and its edge.
        public static void Sheet(DrawingContext dc, double w, double h, double r)
        {
            MakeGrains();
            if (T.Skin == Metal)
            {   // a brushed plate with a polished chamfer round it
                Fill(dc, brushed, w, h, r);
                Fill(dc, G('d', "W.14@0 W0@.3 W.07@.6 W0@.85 K.05@1"), w, h, r);
                Ring(dc, G('d', "W.95@0 W.4@.5 W.8@1"), 1.6, 0.8, w, h, r);
                Ring(dc, K(0.2), 1, 2.2, w, h, r);
                Ring(dc, W(0.22), 1, 3.2, w, h, r);
            }
            else if (T.Skin == Ceramic)
            {   // a slab with a soft rounded edge: light on the side towards the light, shade on the other
                Fill(dc, speckle, w, h, r);
                for (int i = 0; i < 8; i++) Ring(dc, G('d', "W.6@0 W0@.42 K0@.58 K.12@1", Math.Pow(1 - i / 8.0, 1.4)), 1.25, i + 0.6, w, h, r);
            }
            else
            {   // a sheet of card
                Fill(dc, grain, w, h, r);
                Ring(dc, G('v', "W.55@0 W0@.2"), 1, 0.5, w, h, r);
            }
        }
    }

    partial class Shell
    {
        /// Test hook ("drawtime"): how long the open panel takes to draw without the graphics card, written to
        /// drawtime.txt in the test folder. A rough guide to how smooth a skin will be, for comparing skins.
        void DemoDrawTime()
        {
            After(4500, () =>
            {   // (late enough for Full Mode to have opened)
                var root = (Grid)Content; root.UpdateLayout();
                int n = 12, pw = (int)(root.ActualWidth * 1.25), ph = (int)(Math.Min(root.ActualHeight, 900) * 1.25);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < n; i++) { var rtb = new RenderTargetBitmap(pw, ph, 120, 120, PixelFormats.Pbgra32); rtb.Render(root); }
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(Store.Dir, "drawtime.txt"), T.Names[T.Skin] + (T.Glass && T.Dark ? " dark" : "") + (full ? " full " : " ") + (sw.ElapsedMilliseconds / (double)n).ToString("0.0", CultureInfo.InvariantCulture) + " ms" + Environment.NewLine); } catch { }
            });
        }
    }

    /// A raised card that keeps its plain look in the Glass skin (no rim there), and gets its material in the made skins.
    class CardBorder : RimBorder
    {
        protected override bool GlassRim { get { return false; } }
    }
}
