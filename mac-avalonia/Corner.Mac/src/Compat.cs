// The few WPF ideas Corner's UI code leans on that Avalonia spells differently or does not have: WPF-style
// BeginAnimation tweens with easing, left-button pointer events, mouse capture, cursors, the screen's work area.
// They let the ported code in src/ported read almost as it does in the Windows source.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;

namespace Corner
{
    // ------------------------------------------------------------ easing (WPF names)

    public enum EasingMode { EaseIn, EaseOut, EaseInOut }

    public interface IEasingFunction { double Ease(double t); }

    public abstract class EasingFunctionBase : IEasingFunction
    {
        public EasingMode EasingMode { get; set; }
        protected abstract double EaseIn(double t);
        public double Ease(double t)
        {
            switch (EasingMode)
            {
                case EasingMode.EaseIn: return EaseIn(t);
                case EasingMode.EaseOut: return 1 - EaseIn(1 - t);
                default: return t < 0.5 ? EaseIn(t * 2) / 2 : 1 - EaseIn((1 - t) * 2) / 2;
            }
        }
        public void Freeze() { }
    }

    public class QuinticEase : EasingFunctionBase { protected override double EaseIn(double t) { return t * t * t * t * t; } }
    public class QuarticEase : EasingFunctionBase { protected override double EaseIn(double t) { return t * t * t * t; } }
    public class CubicEase : EasingFunctionBase { protected override double EaseIn(double t) { return t * t * t; } }
    public class QuadraticEase : EasingFunctionBase { protected override double EaseIn(double t) { return t * t; } }
    public class SineEase : EasingFunctionBase { protected override double EaseIn(double t) { return 1 - Math.Cos(t * Math.PI / 2); } }
    public class BackEase : EasingFunctionBase
    {
        public double Amplitude { get; set; } = 1;
        protected override double EaseIn(double t) { return t * t * t - t * Math.Max(0, Amplitude) * Math.Sin(t * Math.PI); }
    }

    // ------------------------------------------------------------ WPF-style animations, run by one shared clock

    public struct RepeatBehavior
    {
        public double Count; public bool IsForever;
        public RepeatBehavior(double count) { Count = count; IsForever = false; }
        public static RepeatBehavior Forever { get { return new RepeatBehavior { IsForever = true, Count = 1 }; } }
    }

    public abstract class AnimationBase
    {
        public TimeSpan Duration;
        public TimeSpan? BeginTime;
        public IEasingFunction EasingFunction;
        public bool AutoReverse;
        public RepeatBehavior RepeatBehavior = new RepeatBehavior(1);
        public event EventHandler Completed;
        internal void Done() { if (Completed != null) Completed(this, EventArgs.Empty); }
        internal abstract object From0(object current);
        internal abstract object Lerp(object from, double k);
    }

    public class DoubleAnimation : AnimationBase
    {
        public double? From, To;
        public DoubleAnimation() { }
        public DoubleAnimation(double to, TimeSpan d) { To = to; Duration = d; }
        public DoubleAnimation(double from, double to, TimeSpan d) { From = from; To = to; Duration = d; }
        internal override object From0(object current)
        {
            if (From != null) return From.Value;
            double c = current is double ? (double)current : 0;
            return double.IsNaN(c) ? 0.0 : c;
        }
        internal override object Lerp(object from, double k) { double a = (double)from; return a + ((To ?? a) - a) * k; }
    }

    public class ThicknessAnimation : AnimationBase
    {
        public Thickness? From, To;
        public ThicknessAnimation(Thickness to, TimeSpan d) { To = to; Duration = d; }
        public ThicknessAnimation(Thickness from, Thickness to, TimeSpan d) { From = from; To = to; Duration = d; }
        internal override object From0(object current) { return From ?? (current is Thickness ? (Thickness)current : default(Thickness)); }
        internal override object Lerp(object from, double k)
        {
            var a = (Thickness)from; var b = To ?? a;
            return new Thickness(a.Left + (b.Left - a.Left) * k, a.Top + (b.Top - a.Top) * k, a.Right + (b.Right - a.Right) * k, a.Bottom + (b.Bottom - a.Bottom) * k);
        }
    }

    public static class Anims
    {
        class Run { public AvaloniaObject O; public AvaloniaProperty P; public AnimationBase A; public object From; public long Start; }
        static readonly Dictionary<(AvaloniaObject, AvaloniaProperty), Run> runs = new Dictionary<(AvaloniaObject, AvaloniaProperty), Run>();
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static DispatcherTimer timer;

        /// Starts a, replacing whatever was animating that property; null just stops it, leaving the value where it is.
        public static void BeginAnimation(this AvaloniaObject o, AvaloniaProperty p, AnimationBase a)
        {
            runs.Remove((o, p));
            if (a == null) return;
            var r = new Run { O = o, P = p, A = a, Start = clock.ElapsedMilliseconds + (long)(a.BeginTime ?? TimeSpan.Zero).TotalMilliseconds };
            if (a.BeginTime == null || a.BeginTime.Value <= TimeSpan.Zero) r.From = a.From0(o.GetValue(p));
            runs[(o, p)] = r;
            if (timer == null)
            {
                timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (s, e) => Tick());
            }
            if (!timer.IsEnabled) timer.Start();
            Tick();
        }

        static void Tick()
        {
            long now = clock.ElapsedMilliseconds;
            var finished = new List<Run>();
            foreach (var r in new List<Run>(runs.Values))
            {
                if (now < r.Start) continue;
                if (r.From == null) r.From = r.A.From0(r.O.GetValue(r.P));
                double len = Math.Max(1, r.A.Duration.TotalMilliseconds);
                double cycle = r.A.AutoReverse ? 2 * len : len;
                double times = r.A.RepeatBehavior.IsForever ? double.PositiveInfinity : Math.Max(1, r.A.RepeatBehavior.Count);
                double el = now - r.Start;
                bool end = el >= cycle * times;
                double x = end ? (r.A.AutoReverse ? 0 : 1) : (el % cycle) / len;
                if (x > 1) x = 2 - x; // coming back
                double k = r.A.EasingFunction != null ? r.A.EasingFunction.Ease(Math.Max(0, Math.Min(1, x))) : x;
                try { r.O.SetValue(r.P, r.A.Lerp(r.From, k)); } catch { end = true; }
                if (end) finished.Add(r);
            }
            foreach (var r in finished)
            {
                Run cur;
                if (runs.TryGetValue((r.O, r.P), out cur) && cur == r) runs.Remove((r.O, r.P));
                r.A.Done();
            }
            if (runs.Count == 0 && timer != null) timer.Stop();
        }
    }

    // ------------------------------------------------------------ pointer: left button, capture, wheel

    public static class Ptr
    {
        static IPointer last;

        public static void OnLeftDown(this Control c, EventHandler<PointerPressedEventArgs> h)
        {
            c.PointerPressed += (s, e) =>
            {
                if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
                last = e.Pointer;
                h(s, e);
            };
        }

        public static void OnLeftUp(this Control c, EventHandler<PointerReleasedEventArgs> h)
        {
            c.PointerReleased += (s, e) => { if (e.InitialPressMouseButton == MouseButton.Left) h(s, e); };
        }

        /// A wheel handler that runs before the controls inside get the wheel (WPF's PreviewMouseWheel).
        public static void OnPreviewWheel(this Control c, EventHandler<PointerWheelEventArgs> h)
        {
            c.AddHandler(InputElement.PointerWheelChangedEvent, h, RoutingStrategies.Tunnel);
        }

        public static void CaptureMouse(this Control c) { if (last != null) last.Capture(c); }
        public static void ReleaseMouseCapture(this Control c) { if (last != null && last.Captured == c) last.Capture(null); }
        public static bool IsCaptured(this Control c) { return last != null && last.Captured == c; }

        /// Avalonia's TranslatePoint can fail (not in the same tree yet); WPF's could not. Zero then.
        public static Point Tp(this Visual v, Point p, Visual to) { var r = v.TranslatePoint(p, to); return r ?? new Point(0, 0); }

        /// A point of v on the screen, in the same units as Wpf.ScreenBounds (device-independent).
        public static Point Pts(this Visual v, Point p)
        {
            var top = TopLevel.GetTopLevel(v);
            if (top == null) return p;
            var px = v.PointToScreen(p);
            double k = top.RenderScaling;
            return new Point(px.X / k, px.Y / k);
        }
    }

    public static class Cursors
    {
        public static readonly Cursor Arrow = new Cursor(StandardCursorType.Arrow);
        public static readonly Cursor Hand = new Cursor(StandardCursorType.Hand);
        public static readonly Cursor IBeam = new Cursor(StandardCursorType.Ibeam);
        public static readonly Cursor SizeAll = new Cursor(StandardCursorType.SizeAll);
        public static readonly Cursor SizeNS = new Cursor(StandardCursorType.SizeNorthSouth);
        public static readonly Cursor SizeWE = new Cursor(StandardCursorType.SizeWestEast);
        public static readonly Cursor No = new Cursor(StandardCursorType.No);
    }

    // ------------------------------------------------------------ shapes and controls with WPF's shape

    /// WPF's Line, with X1/Y1/X2/Y2 (Avalonia's has StartPoint and EndPoint).
    public class Line : Avalonia.Controls.Shapes.Line
    {
        public double X1 { get { return StartPoint.X; } set { StartPoint = new Point(value, StartPoint.Y); } }
        public double Y1 { get { return StartPoint.Y; } set { StartPoint = new Point(StartPoint.X, value); } }
        public double X2 { get { return EndPoint.X; } set { EndPoint = new Point(value, EndPoint.Y); } }
        public double Y2 { get { return EndPoint.Y; } set { EndPoint = new Point(EndPoint.X, value); } }
    }

    public class PointCollection : List<Point>
    {
        public PointCollection() { }
        public PointCollection(IEnumerable<Point> p) : base(p) { }
    }

    /// A borderless, see-through, always-on-top window that does not take the focus, placed in device-independent
    /// units with Left and Top (the Windows build's notices and welcome window).
    public class FloatWindow : Window
    {
        protected override Type StyleKeyOverride { get { return typeof(Window); } }
        double left, top;
        public double Left { get { return left; } set { left = value; Wpf.MoveTo(this, left, top); } }
        public double Top { get { return top; } set { top = value; Wpf.MoveTo(this, left, top); } }
        /// After the first frame is drawn (WPF's ContentRendered).
        public event EventHandler ContentRendered;
        public FloatWindow()
        {
            SystemDecorations = SystemDecorations.None; Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            ShowInTaskbar = false; Topmost = true; ShowActivated = false; CanResize = false;
            Opened += (s, e) =>
            {
                Os.FloatEverywhere(this);
                Dispatcher.UIThread.Post(() => { if (ContentRendered != null) ContentRendered(this, EventArgs.Empty); }, DispatcherPriority.Background);
            };
        }
    }

    public static class Controls
    {
        public static void ScrollToHorizontalOffset(this ScrollViewer sv, double x) { sv.Offset = new Vector(Math.Max(0, x), sv.Offset.Y); }
        public static void ScrollToVerticalOffset(this ScrollViewer sv, double y) { sv.Offset = new Vector(sv.Offset.X, Math.Max(0, y)); }

        /// WPF's LayoutTransform = ScaleTransform(k) for a control of a known size w x h: drawn scaled about its centre,
        /// with negative margins so it takes the scaled room in layout. Call after its Margin is set.
        public static T LayoutScale<T>(this T c, double k, double w, double h) where T : Control
        {
            c.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            c.RenderTransform = new ScaleTransform(k, k);
            double dx = w * (1 - k) / 2, dy = h * (1 - k) / 2;
            var m = c.Margin;
            c.Margin = new Thickness(m.Left - dx, m.Top - dy, m.Right - dx, m.Bottom - dy);
            return c;
        }

        /// The font's line spacing as a multiple of its size (WPF's FontFamily.LineSpacing).
        public static double LineSpacing(this FontFamily f)
        {
            try
            {
                IGlyphTypeface g;
                if (FontManager.Current.TryGetGlyphTypeface(new Typeface(f), out g))
                {
                    var m = g.Metrics;
                    return (-m.Ascent + m.Descent + m.LineGap) / (double)m.DesignEmHeight;
                }
            }
            catch { }
            return 1.2;
        }
    }

    // ------------------------------------------------------------ screen and app

    public static class Wpf
    {
        public static Window Main;

        static Avalonia.Platform.Screen Primary
        {
            get { return Main == null || Main.Screens == null ? null : Main.Screens.Primary ?? (Main.Screens.All.Count > 0 ? Main.Screens.All[0] : null); }
        }

        static Rect ToDip(PixelRect r, double k) { return new Rect(r.X / k, r.Y / k, r.Width / k, r.Height / k); }

        /// The primary screen without the menu bar and Dock (SystemParameters.WorkArea), in device-independent units.
        public static Rect WorkArea { get { var s = Primary; return s == null ? new Rect(0, 0, 1440, 900) : ToDip(s.WorkingArea, s.Scaling); } }

        /// The whole primary screen, in device-independent units.
        public static Rect ScreenBounds { get { var s = Primary; return s == null ? new Rect(0, 0, 1440, 900) : ToDip(s.Bounds, s.Scaling); } }

        public static double Scaling { get { var s = Primary; return s == null ? 1 : s.Scaling; } }

        /// Moves a window to a position given in device-independent units, the way WPF's Left and Top did.
        public static void MoveTo(Window w, double left, double top)
        {
            double k = Scaling;
            w.Position = new PixelPoint((int)Math.Round(left * k), (int)Math.Round(top * k));
        }

        public static void Quit()
        {
            var l = Application.Current == null ? null : Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (l != null) l.Shutdown(); else Environment.Exit(0);
        }

        /// Geometry WPF builds with constructors.
        public static EllipseGeometry Ellipse(Point c, double rx, double ry) { return new EllipseGeometry { Center = c, RadiusX = rx, RadiusY = ry }; }

        public static ArcSegment Arc(Point to, Size size, double angle, bool large, SweepDirection dir)
        {
            return new ArcSegment { Point = to, Size = size, RotationAngle = angle, IsLargeArc = large, SweepDirection = dir };
        }

        /// WPF's LinearGradientBrush(from, to, angle).
        public static LinearGradientBrush Linear(Color a, Color b, double angle)
        {
            double r = angle * Math.PI / 180, x = Math.Cos(r), y = Math.Sin(r);
            // WPF maps the angle onto the unit square from (0,0); the end point lies along the angle
            double k = 1 / Math.Max(Math.Abs(x), Math.Abs(y));
            var g = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(x * k, y * k, RelativeUnit.Relative)
            };
            if (x < 0 || y < 0)
            {   // keep the line inside the box when the angle points up or left
                g.StartPoint = new RelativePoint(x < 0 ? 1 : 0, y < 0 ? 1 : 0, RelativeUnit.Relative);
                g.EndPoint = new RelativePoint((x < 0 ? 1 : 0) + x * k, (y < 0 ? 1 : 0) + y * k, RelativeUnit.Relative);
            }
            g.GradientStops.Add(new GradientStop(a, 0)); g.GradientStops.Add(new GradientStop(b, 1));
            return g;
        }

        public static LinearGradientBrush Linear(GradientStops stops, Point from, Point to)
        {
            var g = new LinearGradientBrush { StartPoint = new RelativePoint(from, RelativeUnit.Relative), EndPoint = new RelativePoint(to, RelativeUnit.Relative) };
            foreach (var s in stops) g.GradientStops.Add(s);
            return g;
        }
    }
}
