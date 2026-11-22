// Ported from corner/src/Glass.cs (WPF) to Avalonia: tools/port_ui.py, then fixed by hand.
using System;
using Avalonia.VisualTree;

namespace Corner
{
    /// A box that can carry a glass rim: a hairline, brighter at the top, drawn over its fill. In the Sage skin the
    /// rim's colours are fully transparent, so nothing shows, and nothing has to be redrawn when the skin changes.
    class RimBorder : Border
    {
        // Avalonia's Border cannot be drawn over, so the rim is two inset shadows: a bright line along the top and a
        // fainter one all round, which reads as the Windows rim's top-to-bottom fade. None in Sage.
        static readonly BoxShadows Rim = BoxShadows.Parse("inset 0 1 0 0 #B8FFFFFF, inset 0 0 0 1 #5FDFEAF2");
        static event Action SkinChanged;
        public static void Reskin() { if (SkinChanged != null) SkinChanged(); }

        void Paint() { BoxShadow = T.Glass && Background != null && Background != Brushes.Transparent ? Rim : default(BoxShadows); }
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); SkinChanged += Paint; Paint(); }
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); SkinChanged -= Paint; }
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BackgroundProperty && IsAttachedToVisualTree()) Paint();
        }
        bool IsAttachedToVisualTree() { return this.GetVisualRoot() != null; }
    }

    partial class Shell
    {
        Border glassTint, glassSheen; // the Glass skin's tint and rim, and the light on its top, between the picture of what is behind and the content

        Rect backView; bool backHooked;

        /// "light", "dark" or "busy" in a test run (CORNER_BACKDROP): a made-up wallpaper, so a test never reads the real
        /// screen unless it asks to with "screen". Null outside test runs.
        static string FakeBackdrop
        {
            get
            {
                if (!Program.Test) return null;
                string b = Environment.GetEnvironmentVariable("CORNER_BACKDROP");
                return b == "screen" ? null : string.IsNullOrEmpty(b) ? "light" : b;
            }
        }

        /// Takes the picture of the desktop for the Glass skin. Only while the panel is off the screen: it would be in it.
        void GrabBackdrop() { }

        /// Keeps the part of the picture that is behind the panel in the panel, as it opens, grows and is dragged.
        void TrackBackdrop() { }

        /// The panel's own layers for the skin in use. Called from Restyle (Ui.cs).
        void SkinPanel()
        {
            if (T.Glass)
            {
                panelBg.Background = GlassGround(); // see-through over the system blur on a Mac, plain frost elsewhere
                panelBg.BorderThickness = new Thickness(0);
                double pr = panelBg.CornerRadius.TopLeft;
                glassTint.CornerRadius = panelBg.CornerRadius; glassSheen.CornerRadius = new CornerRadius(pr - 1, pr - 1, 0, 0); glassTint.IsVisible = true;
                shadow.Color = T.GlassShadow; shadow.Opacity = T.GlassShadowOpacity;
                TrackBackdrop();
            }
            else
            {
                panelBg.Background = T.Bg; panelBg.BorderThickness = new Thickness(1);
                glassTint.IsVisible = false;
                shadow.Color = Color.FromRgb(60, 54, 34); shadow.Opacity = 0.3;
            }
            StartGlass(); // the system blur under the card, on a Mac (GlassMac.cs)
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
