// Corner - the soft shadow under the panel, the notice and the welcome window.
// It was a DropShadowEffect on the box itself. Corner now draws without the graphics card (Program.Main says why),
// and drawn that way a blurred effect is worked out again on every frame in which its box moves or grows, which
// made the panel open in jerks. So the shadow is blurred once, by the same effect, into a small picture, and that
// picture is fitted round the box: its four corners as they are, its four sides stretched between them.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Corner
{
    /// Put it under an opaque box of the same size (before it in the same Grid). The numbers mean what they mean on a
    /// DropShadowEffect that points straight down: Blur is its BlurRadius, Depth its ShadowDepth, Strength its Opacity.
    class Shade : FrameworkElement
    {
        double radius = 18, blur = 30, depth = 8, strength = 0.3; Color tint = Color.FromRgb(60, 54, 34);
        public double Radius { get { return radius; } set { if (radius != value) { radius = value; InvalidateVisual(); } } }   // the box's corners
        public double Blur { get { return blur; } set { if (blur != value) { blur = value; InvalidateVisual(); } } }
        public double Depth { get { return depth; } set { if (depth != value) { depth = value; InvalidateVisual(); } } }
        public double Strength { get { return strength; } set { if (strength != value) { strength = value; InvalidateVisual(); } } }
        public Color Tint { get { return tint; } set { if (tint != value) { tint = value; InvalidateVisual(); } } }

        public Shade()
        {
            IsHitTestVisible = false;
            // Each pixel is taken from the picture as it is, not mixed from its neighbours. Without the graphics card
            // the mixing costs five times as much, and in something this smooth the difference cannot be seen.
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        }

        const double Q = 2; // the picture's pixels per unit
        static readonly Dictionary<string, BitmapSource> tiles = new Dictionary<string, BitmapSource>();

        /// `m`: how far the shadow reaches past the box. `c`: the side of a corner piece, which has to hold all of
        /// the shadow that the corner's roundness has a say in.
        static void Sizes(double radius, double blur, out double m, out double c)
        {
            m = Math.Ceiling(blur) + 2;
            c = m + Math.Ceiling(radius + blur) + 2;
        }

        /// The shadow of a box just big enough to have four whole corners, with nothing on top of it. The effect is
        /// asked to throw it far to the right, clear of the box, and that part is kept.
        static BitmapSource Tile(double radius, double blur, Color tint, double strength)
        {
            string key = radius + "|" + blur + "|" + tint + "|" + strength;
            BitmapSource tile;
            if (tiles.TryGetValue(key, out tile)) return tile;
            double m, c; Sizes(radius, blur, out m, out c);
            double box = 2 * (c - m) + 4, all = box + 2 * m, far = all + 8;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRoundedRectangle(Brushes.Black, null, new Rect(m, m, box, box), radius, radius);
            dv.Effect = new DropShadowEffect { BlurRadius = blur, ShadowDepth = far, Direction = 0, Color = tint, Opacity = strength };
            int n = (int)(all * Q);
            var wide = new RenderTargetBitmap((int)((far + all) * Q), n, 96 * Q, 96 * Q, PixelFormats.Pbgra32);
            wide.Render(dv);
            var px = new byte[n * n * 4];
            wide.CopyPixels(new Int32Rect((int)(far * Q), 0, n, n), px, n * 4, 0);
            tile = BitmapSource.Create(n, n, 96 * Q, 96 * Q, PixelFormats.Pbgra32, null, px, n * 4);
            tile.Freeze();
            if (tiles.Count > 24) tiles.Clear(); // (a handful in practice: one per skin, mode and window)
            tiles[key] = tile;
            return tile;
        }

        /// Where a place along the shadow lies in its picture: the corner pieces as they are, the middle stretched.
        static double Along(double x, double from, double to, double c, double side)
        {
            if (x <= from + c) return x - from;
            if (x >= to - c) return side - (to - x);
            return c + (x - from - c) * (side - 2 * c) / (to - from - 2 * c);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 2 || h < 2 || strength <= 0) return;
            double m, c; Sizes(radius, blur, out m, out c);
            var tile = Tile(radius, blur, tint, strength);
            double side = tile.Width; // in units
            double x0 = -m, x1 = w + m, y0 = depth - m, y1 = depth + h + m; // where the shadow lies
            // a box too small for whole corner pieces gets the outer part of each
            double cx = Math.Min(c, (x1 - x0) / 2), cy = Math.Min(c, (y1 - y0) / 2);
            // Cut where the picture's pieces meet, and where the box begins to hide the shadow: a piece that lies
            // wholly under the box is left out. (All but the corners of the box, a unit in from its edge.)
            double rr = radius + 1;
            var xs = new List<double> { x0, x0 + cx, x1 - cx, x1, 1, rr, w - rr, w - 1 };
            var ys = new List<double> { y0, y0 + cy, y1 - cy, y1, 1, rr, h - rr, h - 1 };
            xs.RemoveAll(v => v < x0 || v > x1); ys.RemoveAll(v => v < y0 || v > y1);
            xs.Sort(); ys.Sort();
            dc.PushGuidelineSet(new GuidelineSet(xs.ToArray(), ys.ToArray())); // the pieces meet on whole pixels: no seam, no overlap
            for (int j = 0; j + 1 < ys.Count; j++)
                for (int i = 0; i + 1 < xs.Count; i++)
                {
                    double a = xs[i], b = xs[i + 1], t = ys[j], u = ys[j + 1];
                    if (b - a < 0.01 || u - t < 0.01) continue;
                    bool under = (a >= rr && b <= w - rr && t >= 1 && u <= h - 1) || (a >= 1 && b <= w - 1 && t >= rr && u <= h - rr);
                    if (under) continue;
                    double sa = Along(a, x0, x1, cx, side), sb = Along(b, x0, x1, cx, side), st = Along(t, y0, y1, cy, side), su = Along(u, y0, y1, cy, side);
                    var piece = new ImageBrush(tile) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(sa, st, Math.Max(0.01, sb - sa), Math.Max(0.01, su - st)) };
                    dc.DrawRectangle(piece, null, new Rect(a, t, b - a, u - t));
                }
            dc.Pop();
        }
    }
}
