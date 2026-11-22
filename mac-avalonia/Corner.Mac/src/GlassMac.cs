// The Glass skin on a Mac. Windows' Corner takes a picture of the screen and blurs it; on a Mac that would need the
// Screen Recording permission. Instead a second, empty window sits exactly under the card and asks macOS for its
// own live blur (Avalonia's AcrylicBlur, drawn by NSVisualEffectView), with its corners rounded to the card's.
// The panel window above it stays see-through, so only the card's area is frosted.
using System;

namespace Corner
{
    partial class Shell
    {
        FloatWindow blur;
        DispatcherTimer blurTimer;

        /// The ground under the Glass tint: see-through over the system blur on a Mac, the plain frost elsewhere.
        IBrush GlassGround() { return Os.Mac ? (IBrush)Brushes.Transparent : T.GlassBase; }

        /// The notice card has no blur window of its own: it keeps the plain frost.
        static void GlassWindow(Window w, double radius, double inset) { }

        /// Puts the blur window under the card, or takes it away. Runs every frame while the panel shows in Glass.
        void TrackGlass()
        {
            bool want = Os.Mac && T.Glass && IsVisible && card.Bounds.Width > 0 && card.Opacity > 0.01;
            if (!want)
            {
                if (blur != null && blur.IsVisible) blur.Hide();
                if (blurTimer != null && !(Os.Mac && T.Glass && IsVisible)) blurTimer.Stop();
                return;
            }
            if (blur == null)
                blur = new FloatWindow
                {
                    Title = "Corner glass", Focusable = false,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent }
                };
            var a = card.Pts(new Point(0, 0)); var b = card.Pts(new Point(card.Bounds.Width, card.Bounds.Height));
            double w = Math.Max(1, b.X - a.X), h = Math.Max(1, b.Y - a.Y);
            if (Math.Abs(blur.Width - w) > 0.5) blur.Width = w;
            if (Math.Abs(blur.Height - h) > 0.5) blur.Height = h;
            if (Math.Abs(blur.Left - a.X) > 0.5 || Math.Abs(blur.Top - a.Y) > 0.5) { blur.Left = a.X; blur.Top = a.Y; }
            blur.Opacity = card.Opacity;
            if (!blur.IsVisible)
            {
                blur.Show();
                Os.RoundWindow(blur, panelBg.CornerRadius.TopLeft * CurrentScale);
                Os.OrderAbove(this, blur); // the panel stays on top of its own frost
            }
        }

        /// Starts following the card with the blur window; called when the panel is laid out (Restyle).
        void StartGlass()
        {
            if (!Os.Mac) return;
            if (blurTimer == null) blurTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (s, e) => TrackGlass());
            if (T.Glass) blurTimer.Start(); else blurTimer.Stop();
            if (blur != null && blur.IsVisible) Os.RoundWindow(blur, panelBg.CornerRadius.TopLeft * CurrentScale);
            TrackGlass();
        }
    }
}
