// The launch: when Corner starts (with Windows, or by hand) the panel comes out of the app icon with the Genie effect:
// drawn as thin slices that leave the icon's square one after another, so its sides bend like a funnel whose neck is
// the icon. It shows for a few seconds, then goes back into the icon the same way, unless the pointer has been on it.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IO = System.IO;

namespace Corner
{
    partial class Shell
    {
        Grid rootGrid;
        bool launchZoom;      // the next ShowPanel comes out of the icon (set by Start)
        bool genieBack;       // the panel goes back into its corner when it hides (it came out with the Genie)
        bool genieIcon;       // ...into the app icon: only the startup panel, until the pointer has been on it
        bool demoGenie;       // test hook: demo panels come out with the Genie too
        bool genieOpening;    // the panel is coming out of its corner as one picture
        bool revealWaiting;   // ...and the pointer is on it already: the rest comes in when the picture has landed
        GenieHost genie;      // the warp under way, drawn over the window
        const double LaunchIcon = 64;

        /// The icon's square, where the panel comes out of and goes back into: just inside the panel's corner.
        Rect IconRect(Rect panel)
        {
            double s = LaunchIcon, inset = 6;
            double x = OnRight ? panel.Right - inset - s : panel.Left + inset;
            double y = OnTop ? panel.Top + inset : panel.Bottom - inset - s;
            return new Rect(x, y, s, s);
        }

        Rect PanelRect() { return card.TransformToAncestor(rootGrid).TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight)); }

        /// The panel as it is now, as a picture with room for its shadow, and where that picture sits in the window.
        bool CapturePanel(out BitmapSource pic, out Rect where)
        {
            pic = null; where = Rect.Empty;
            foreach (var dp in new[] { TranslateTransform.XProperty, TranslateTransform.YProperty }) { cardShift.BeginAnimation(dp, null); cardShift.SetValue(dp, 0.0); }
            foreach (var dp in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }) { cardScale.BeginAnimation(dp, null); cardScale.SetValue(dp, 1.0); }
            card.BeginAnimation(OpacityProperty, null); card.Opacity = 1;
            rootGrid.UpdateLayout();
            if (chartScroll != null && chartScroll.IsVisible && chartOffset >= 0)
            {   // a chart drawn just now scrolls to its place only once it has loaded: do it here, or the picture shows it unscrolled
                chartScroll.ScrollToHorizontalOffset(chartOffset);
                rootGrid.UpdateLayout(); rootGrid.UpdateLayout();
            }
            var rect = PanelRect();
            double W = rootGrid.ActualWidth, H = rootGrid.ActualHeight;
            if (rect.Width < 1 || rect.Height < 1 || W < 1 || H < 1) return false;
            var info = PresentationSource.FromVisual(rootGrid);
            double k = info != null && info.CompositionTarget != null ? info.CompositionTarget.TransformToDevice.M11 : 1;
            var all = new RenderTargetBitmap((int)Math.Ceiling(W * k), (int)Math.Ceiling(H * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
            var bg = rootGrid.Background; rootGrid.Background = null;
            all.Render(rootGrid);
            rootGrid.Background = bg;
            double pad = 36; // the shadow
            where = Rect.Intersect(new Rect(rect.X - pad, rect.Y - pad, rect.Width + 2 * pad, rect.Height + 2 * pad), new Rect(0, 0, W, H));
            int x0 = (int)(where.X * k), y0 = (int)(where.Y * k);
            var px = new Int32Rect(x0, y0, Math.Min(all.PixelWidth - x0, (int)(where.Width * k)), Math.Min(all.PixelHeight - y0, (int)(where.Height * k)));
            if (px.Width < 2 || px.Height < 2) return false;
            var crop = new CroppedBitmap(all, px); crop.Freeze();
            pic = crop;
            return true;
        }

        /// Called by ShowPanel right after the panel is shown: out of the icon.
        void LaunchZoom(bool icon)
        {
            launchZoom = false;
            if (rootGrid == null) return;
            try
            {
                StopGenie();
                BitmapSource pic; Rect where;
                if (!CapturePanel(out pic, out where)) return;
                var iconAt = IconRect(PanelRect());
                card.Opacity = 0;
                genieBack = true; genieIcon = icon; genieOpening = true;
                RunGenie(pic, where, iconAt, true, icon ? 560 : 420, () =>
                {
                    card.Opacity = 1; genieOpening = false;
                    if (revealWaiting) { revealWaiting = false; Reveal(); } // the pointer came onto it on its way out (Focus.cs)
                }, icon);
            }
            catch (Exception ex) { Store.Log(ex); card.Opacity = 1; }
        }

        /// From HidePanel, for the startup panel: back into the icon. False when it cannot; HidePanel then fades as usual.
        bool GenieHide(Action done)
        {
            genieBack = false;
            if (rootGrid == null || full) return false;
            try
            {
                StopGenie();
                BitmapSource pic; Rect where;
                if (!CapturePanel(out pic, out where)) return false;
                var icon = IconRect(PanelRect());
                card.Opacity = 0;
                bool withIcon = genieIcon; genieIcon = false;
                RunGenie(pic, where, icon, false, withIcon ? 460 : 380, done, withIcon);
                return true;
            }
            catch (Exception ex) { Store.Log(ex); return false; }
        }

        void StopGenie()
        {
            genieOpening = false; revealWaiting = false;
            if (genie == null) return;
            genie.Stop();
            rootGrid.Children.Remove(genie);
            genie = null;
            card.Opacity = shown ? 1 : 0;
        }

        void RunGenie(BitmapSource pic, Rect where, Rect icon, bool opening, int ms, Action done, bool showIcon)
        {
            var g = new GenieHost(pic, where, icon, OnTop, null, showIcon) { IsHitTestVisible = false };
            genie = g;
            rootGrid.Children.Add(g);
            g.Run(opening, ms, () =>
            {
                rootGrid.Children.Remove(g);
                if (genie != g) return; // a newer one took over
                genie = null;
                if (done != null) done();
            });
        }

        // ------------------------------------------------------------ switching between the corner and Full Mode
        // The two are different window sizes, so the warp is drawn in a see-through window of its own over the screen:
        // the corner panel flows out into the Full Mode window, and back down into it. No icon unless the corner
        // panel wasn't open (Full Mode from the tray): then it comes out of, and goes back into, the corner's icon.

        Window warpWin; Grid warpRoot; GenieHost warp;
        BitmapSource miniPic; Rect miniRect = Rect.Empty; // the corner panel as it was when Full Mode opened, on the screen
        bool switchWarp;  // ShowPanel: nothing fades in inside the panel, the warp draws it as one picture

        Rect OnScreen(Rect r) { return new Rect(Left + r.X, Top + r.Y, r.Width, r.Height); }

        Rect CornerIcon()
        {
            var wa = SystemParameters.WorkArea; double s = LaunchIcon, m = Gap + 6;
            return new Rect(OnRight ? wa.Right - m - s : wa.Left + m, OnTop ? wa.Top + m : wa.Bottom - m - s, s, s);
        }

        /// From EnterFull, before the corner panel goes: its picture, the neck the Full Mode window flows out of.
        void GrabMini()
        {
            miniPic = null; miniRect = Rect.Empty;
            if (!shown || !IsVisible || rootGrid == null) return;
            try { StopGenie(); BitmapSource p; Rect w; if (CapturePanel(out p, out w)) { miniPic = p; miniRect = OnScreen(w); } }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// From EnterFull, once the Full Mode window is up: out of the corner panel (or the icon).
        void WarpIntoFull()
        {
            try
            {
                BitmapSource pic; Rect where;
                if (!CapturePanel(out pic, out where)) { DropHold(); return; }
                card.Opacity = 0;
                var neck = miniPic != null ? miniRect : CornerIcon();
                RunWarp(pic, OnScreen(where), neck, miniPic, true, 600, () => { card.Opacity = 1; });
            }
            catch (Exception ex) { Store.Log(ex); DropHold(); card.Opacity = 1; }
        }

        /// From ExitFull, before the window goes: back down into the corner panel (or the icon).
        Action WarpOutOfFull()
        {
            try
            {
                StopWarp();
                BitmapSource pic; Rect where;
                if (!IsVisible || !CapturePanel(out pic, out where)) return null;
                var to = OnScreen(where);
                var neck = CornerIcon(); // shrinks into its corner and is gone
                return () => { try { RunWarp(pic, to, neck, null, false, 480, null); } catch (Exception ex) { Store.Log(ex); } };
            }
            catch (Exception ex) { Store.Log(ex); return null; }
        }

        void EnsureWarpWin()
        {
            if (warpWin == null)
            {
                warpRoot = new Grid { IsHitTestVisible = false };
                warpWin = new Window
                {
                    Title = "Corner", WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                    ShowInTaskbar = false, Topmost = true, ShowActivated = false, ResizeMode = ResizeMode.NoResize, Focusable = false,
                    IsHitTestVisible = false, Content = warpRoot
                };
                warpWin.SourceInitialized += (s, e) =>
                {   // clicks pass through, never takes the focus, not in Alt+Tab
                    var h = new WindowInteropHelper(warpWin).Handle;
                    SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80 | 0x08000000);
                };
            }
        }

        /// A while after Corner starts: the warp window shown once, empty, so its first real use isn't slow to appear.
        void WarmWarp()
        {
            try { if (warp != null || warpHold != null) return; EnsureWarpWin(); warpWin.Left = 0; warpWin.Top = 0; warpWin.Width = 1; warpWin.Height = 1; warpWin.Show(); warpWin.Hide(); }
            catch (Exception ex) { Store.Log(ex); }
        }

        Rect PlaceWarpWin(Rect a, Rect b)
        {
            var area = Rect.Union(SystemParameters.WorkArea, Rect.Union(a, b));
            warpWin.Left = area.Left; warpWin.Top = area.Top; warpWin.Width = area.Width; warpWin.Height = area.Height;
            return area;
        }

        Image warpHold; // the corner panel's picture, held on top while the window changes to Full Mode under it

        /// From EnterFull: the corner panel's picture on top, so the window can change size underneath unseen.
        bool HoldMini()
        {
            if (miniPic == null) return false;
            try
            {
                StopWarp(); DropHold(); EnsureWarpWin();
                var area = PlaceWarpWin(miniRect, miniRect);
                warpHold = new Image
                {
                    Source = miniPic, Stretch = Stretch.Fill, IsHitTestVisible = false, Width = miniRect.Width, Height = miniRect.Height,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(miniRect.X - area.Left, miniRect.Y - area.Top, 0, 0)
                };
                warpRoot.Children.Add(warpHold);
                if (!warpWin.IsVisible) warpWin.Show();
                return true;
            }
            catch (Exception ex) { Store.Log(ex); DropHold(); return false; }
        }

        void DropHold()
        {
            if (warpHold == null) return;
            warpRoot.Children.Remove(warpHold); warpHold = null;
            if (warp == null && warpWin != null) warpWin.Hide();
        }

        void RunWarp(BitmapSource pic, Rect to, Rect neck, BitmapSource neckPic, bool opening, int ms, Action done)
        {
            StopWarp();
            EnsureWarpWin();
            var area = PlaceWarpWin(to, neck);
            to.Offset(-area.Left, -area.Top); neck.Offset(-area.Left, -area.Top);
            var g = new GenieHost(pic, to, neck, OnTop, neckPic, false) { IsHitTestVisible = false };
            warp = g;
            warpRoot.Children.Add(g);
            if (warpHold != null) { warpRoot.Children.Remove(warpHold); warpHold = null; } // the warp takes over from the held picture
            if (!warpWin.IsVisible) warpWin.Show();
            g.Run(opening, ms, () =>
            {
                warpRoot.Children.Remove(g);
                if (warp != g) return;
                warp = null;
                warpWin.Hide();
                if (done != null) done();
            });
        }

        void StopWarp()
        {
            if (warp == null) return;
            warp.Stop();
            warpRoot.Children.Remove(warp);
            warp = null;
            card.Opacity = shown ? 1 : 0;
        }

        /// Test hook: the corner panel flowing into Full Mode ("fullswitch"), or Full Mode going back down ("fullexit").
        void DemoSwitch(string path, bool exit)
        {
            if (!exit) { After(900, () => { EnterFull(); DemoLaunchFrames(path, new[] { 40, 110, 190, 280, 390, 560 }, warpRoot); }); return; }
            After(900, () => { EnterFull(); After(1500, () => { ExitFull(); DemoLaunchFrames(path, new[] { 60, 150, 240, 330, 430, 600 }, warpRoot); }); });
        }

        // ------------------------------------------------------------ the cycle, in the corner panel

        FrameworkElement cycleBtn; // the cycle's round button in the corner panel's bottom row (Focus.cs)

        int cycleSeq; // (a fold that is overtaken must not close the cycle when it ends)

        /// From the cycle button: the chart drops down under the next task and its chips, as an extension of the panel,
        /// so the task and its timer stay in sight; and folds back up the same way.
        void CycleGenie(int to)
        {
            int seq = ++cycleSeq;
            var el = energyScroll;
            if (el != null) { el.BeginAnimation(MaxHeightProperty, null); el.BeginAnimation(OpacityProperty, null); el.Opacity = 1; }
            if (full || rootGrid == null || !shown || !IsVisible || to == tab || el == null) { SetTab(to); return; }
            try
            {
                StopGenie();
                if (to == 1)
                {
                    SetTab(1);
                    card.UpdateLayout();
                    double h = el.ActualHeight;
                    if (h <= 0) return;
                    var grow = new System.Windows.Media.Animation.DoubleAnimation(0, h, TimeSpan.FromMilliseconds(260)) { EasingFunction = Out };
                    grow.Completed += (s, e) => { if (seq == cycleSeq) el.BeginAnimation(MaxHeightProperty, null); };
                    el.BeginAnimation(MaxHeightProperty, grow);
                    Anim(el, OpacityProperty, 1, 220, from: 0);
                    return;
                }
                var fold = new System.Windows.Media.Animation.DoubleAnimation(el.ActualHeight, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = Out };
                fold.Completed += (s, e) =>
                {
                    if (seq != cycleSeq) return;
                    if (tab == 1 && !full) SetTab(0);
                    el.BeginAnimation(MaxHeightProperty, null); el.BeginAnimation(OpacityProperty, null); el.Opacity = 1;
                };
                el.BeginAnimation(MaxHeightProperty, fold);
                Anim(el, OpacityProperty, 0, 180);
            }
            catch (Exception ex) { Store.Log(ex); SetTab(to); }
        }

        Rect ButtonAt()
        {
            var b = cycleBtn;
            if (b == null || !b.IsVisible || !rootGrid.IsAncestorOf(b)) return Rect.Empty;
            return b.TransformToAncestor(rootGrid).TransformBounds(new Rect(0, 0, b.ActualWidth, b.ActualHeight));
        }

        /// Test hook: the cycle coming out of its button ("cycleopen"), or going back in ("cycleclose").
        void DemoCycle(string path, bool close)
        {
            if (!close) { After(900, () => { CycleGenie(1); DemoLaunchFrames(path, new[] { 30, 90, 160, 240, 330, 600 }); }); return; }
            After(900, () => { SetTab(1); After(700, () => { CycleGenie(0); DemoLaunchFrames(path, new[] { 30, 100, 170, 240, 320, 600 }); }); });
        }

        /// Test hook: the panel coming out of its corner ("launch", "hoveropen"), or going back in ("launchclose", "hoverclose").
        void DemoLaunch(string path, bool close)
        {
            if (!close) { DemoLaunchFrames(path, new[] { 30, 80, 130, 190, 280, 700 }); return; }
            After(900, () => { genieBack = true; HidePanel(); DemoLaunchFrames(path, new[] { 120, 240, 330, 420, 520, 640 }); });
        }

        /// Frames side by side in one picture.
        void DemoLaunchFrames(string path, int[] times, Panel src = null)
        {
            var frames = new List<RenderTargetBitmap>();
            int k = 0;
            double w = 0, h = 0;
            Action next = null;
            next = () =>
            {
                var root = src ?? rootGrid;
                root.Background = new SolidColorBrush(Color.FromRgb(185, 177, 151));
                root.UpdateLayout();
                if (w == 0) { w = root.ActualWidth; h = Math.Min(root.ActualHeight, 900); }
                var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root); frames.Add(rtb);
                if (++k < times.Length) { After(times[k] - times[k - 1], next); return; }
                double z = Math.Min(1, 560 / w), fw = w * z, fh = h * z; // wide frames (the whole screen) drawn smaller
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                    for (int i = 0; i < frames.Count; i++)
                        dc.DrawImage(frames[i], new Rect(i * (fw + 8), 0, fw, fh));
                w = fw; h = fh;
                var all = new RenderTargetBitmap((int)((w + 8) * frames.Count), (int)h, 96, 96, PixelFormats.Pbgra32);
                all.Render(dv);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(all));
                using (var fs = IO.File.Create(path)) enc.Save(fs);
                tray.Visible = false;
                Application.Current.Shutdown();
            };
            After(times[0], next);
        }
    }

    /// Draws the panel's picture as thin horizontal slices between the icon's square and the panel's place. The far
    /// slices leave the icon first and the near ones last, so the sides bend like a funnel whose neck is the icon.
    /// Time runs through a critically damped spring: quick and tight, with no wobble.
    class GenieHost : FrameworkElement
    {
        readonly Rect to, icon;
        readonly bool iconAtTop;
        readonly CroppedBitmap[] slices;
        readonly double[] at;  // the slices' edges, 0..1 down the picture
        double t;              // 0 = in the icon, 1 = the panel in place
        System.Diagnostics.Stopwatch clock;
        int ms; bool opening; Action done;
        double tail = 1;          // closing: the icon stays a moment after the panel has gone in, then fades
        const int TailMs = 280;
        const int Slices = 200;
        const double Lag = 0.75;  // how far the icon's end lags the far end: the curve of the funnel

        readonly BitmapSource neckPic; // the corner panel's picture in the neck, instead of the icon (the Full Mode switch)
        readonly bool showIcon;        // the app icon in the neck: only when Corner starts
        public BitmapSource BasePic;   // the panel as it is without the warped part, fading as the warp comes in (the cycle)
        public Rect BaseAt;
        bool Lingers { get { return showIcon || neckPic != null; } } // closing: something stays a moment in the neck

        public GenieHost(BitmapSource pic, Rect to, Rect icon, bool iconAtTop, BitmapSource neckPic = null, bool showIcon = true)
        {
            this.to = to; this.icon = icon; this.iconAtTop = iconAtTop; this.neckPic = neckPic; this.showIcon = showIcon;
            // A picture the size of the Full Mode window: smooth scaling takes about 40 ms a frame without the graphics
            // card, the plain kind half that. (It only shows while the picture is bent; in place it is pixel for pixel.)
            if ((long)pic.PixelWidth * pic.PixelHeight > 700000) RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            int n = Math.Max(2, Math.Min(Slices, pic.PixelHeight / 2)); // about 2 px each: smooth curved sides
            slices = new CroppedBitmap[n]; at = new double[n + 1];
            int[] py = new int[n + 1];
            for (int i = 0; i <= n; i++) { py[i] = (int)Math.Round(pic.PixelHeight * (double)i / n); at[i] = (double)py[i] / pic.PixelHeight; }
            for (int i = 0; i < n; i++)
            {
                var c = new CroppedBitmap(pic, new Int32Rect(0, py[i], pic.PixelWidth, Math.Max(1, py[i + 1] - py[i]))); c.Freeze();
                slices[i] = c;
            }
        }

        public void Run(bool opening, int ms, Action done)
        {
            this.opening = opening; this.ms = ms; this.done = done;
            t = opening ? 0 : 1;
            clock = System.Diagnostics.Stopwatch.StartNew();
            CompositionTarget.Rendering += Frame;
            InvalidateVisual();
        }

        public void Stop() { CompositionTarget.Rendering -= Frame; }

        void Frame(object sender, EventArgs e)
        {
            double e2 = clock.ElapsedMilliseconds, x = Math.Min(1, e2 / ms);
            t = opening ? x : 1 - x;
            if (!opening && Lingers) tail = Math.Max(0, 1 - Math.Max(0, e2 - ms) / TailMs);
            InvalidateVisual();
            if (x >= 1 && (opening || !Lingers || tail <= 0))
            {
                Stop();
                var d = done; done = null;
                if (d != null) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, d); // after this last frame is drawn
            }
        }

        /// A critically damped spring from 0 to 1: fast away, settling without overshoot.
        static double Spring(double x) { const double w = 8; double f = 1 - (1 + w * x) * Math.Exp(-w * x); return f / (1 - (1 + w) * Math.Exp(-w)); }
        static double Smooth(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
        static double Lerp(double a, double b, double k) { return a + (b - a) * k; }

        protected override void OnRender(DrawingContext dc)
        {
            double tc = Math.Max(0, Math.Min(1, t));
            // closing is not the spring backwards (that sits still for half its time, then snaps): it leaves at once and gathers speed
            double f = opening ? Spring(tc) : 1 - Math.Pow(1 - tc, 1.6);
            double down = Smooth(Math.Min(1, f * 1.35));  // how far the panel has slid out of the icon, end to end
            double alpha = Math.Min(1, f * 4.5);           // it takes over from the icon quickly
            if (BasePic != null)
            {
                double ba = 1 - Smooth(Math.Min(1, f * 1.4));
                if (ba > 0.01) { dc.PushOpacity(ba); dc.DrawImage(BasePic, BaseAt); dc.Pop(); }
            }
            if (neckPic != null)
            {   // the corner panel, handing over to the window flowing out of it
                double na = (1 - Math.Min(1, f * 2.4)) * tail;
                if (na > 0.01) { dc.PushOpacity(na); dc.DrawImage(neckPic, icon); dc.Pop(); }
                alpha = Math.Min(1, f * 3);
            }
            bool faded = alpha < 0.995; // (an opacity layer is slow without the graphics card: only while it is needed)
            if (faded) dc.PushOpacity(alpha);
            for (int i = 0; i < slices.Length; i++)
            {
                double v0 = at[i], v1 = at[i + 1], vm = (v0 + v1) / 2;
                double far = iconAtTop ? vm : 1 - vm;                    // 0 at the icon's end of the panel, 1 at the other
                double wide = Smooth(f * (1 + Lag) - Lag * (1 - far));   // the far end widens first, the icon's end last
                double y0 = Lerp(icon.Top + v0 * icon.Height, to.Top + v0 * to.Height, down);
                double y1 = Lerp(icon.Top + v1 * icon.Height, to.Top + v1 * to.Height, down);
                double l = Lerp(icon.Left, to.Left, wide), r = Lerp(icon.Right, to.Right, wide);
                if (y1 - y0 < 0.05 || r - l < 0.5) continue;
                dc.DrawImage(slices[i], new Rect(l, y0, r - l, y1 - y0 + 0.6)); // (a hair of overlap hides the seams)
            }
            if (faded) dc.Pop();
            // the icon, swelling a little as it fades into the panel
            if (neckPic != null || !showIcon) return;
            double ia = (opening ? 1 - Math.Min(1, f * 2.6) : 1 - Math.Min(1, f * 1.7)) * tail;
            if (ia <= 0.01) return;
            double s = icon.Width * (1 + 0.35 * f), cx = icon.X + icon.Width / 2, cy = icon.Y + icon.Height / 2;
            double x = cx - s / 2, y = cy - s / 2;
            dc.PushOpacity(ia);
            dc.DrawImage(Logo.Image, new Rect(x + s * 0.04, y + s * 0.04, s * 0.92, s * 0.92)); // the logo, the same art as the app icon (Logo.cs)
            dc.Pop();
        }
    }
}
