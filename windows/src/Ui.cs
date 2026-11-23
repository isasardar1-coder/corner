// Corner - the grid both modes are drawn on, the shared controls (buttons, switches, chips, energy bars),
// and the two pages built only from them: the 1-10 freshness prompt and Settings. The colours are T, in Corner.cs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Corner
{
    /// A box with fully round ends: buttons, chips, pills, the round icon buttons, boxes to type in.
    /// The radius follows its height, because a fixed radius larger than half the height is squashed into an oval.
    class PillBorder : RimBorder
    {
        public PillBorder() { CornerRadius = new CornerRadius(12); } // until it has a size
        protected override bool Glossy { get { return true; } }      // the Glass skin: light across its upper half (Glass.cs)
        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            if (info.NewSize.Height > 0) CornerRadius = new CornerRadius(info.NewSize.Height / 2);
        }
    }

    partial class Shell
    {
        // ------------------------------------------------------------ the grid: density, text sizes, insets

        /// True while the corner panel is showing. It is the quick-glance view: tighter, and with four text sizes only.
        /// Full Mode keeps the sizes written in the code. Both modes share the insets below.
        static bool Slim = true;

        // FontK evens out how large the font looks (1 for the Segoe UI fallback); HasSemi: it has a real semibold.
        const double GilroyFit = 0.97; // Gilroy looks a touch larger than Segoe UI at the same size
        static double FontK = 1; static bool HasSemi = true;
        static FontFamily UiItalic; // Gilroy SemiBold Italic, a family of its own; null when the file is missing
        /// "Overdue" is the one thing set in the italic. Its file holds a single face, which Windows lists as bold italic.
        static void Lean(TextBlock t) { if (UiItalic != null) { t.FontFamily = UiItalic; t.FontStyle = FontStyles.Italic; t.FontWeight = FontWeights.Bold; t.Padding = new Thickness(0, 0, 2, 0); } } // 2: room for the last letter's lean
        static void Lean(System.Windows.Documents.TextElement t) { if (UiItalic != null) { t.FontFamily = UiItalic; t.FontStyle = FontStyles.Italic; t.FontWeight = FontWeights.Bold; } }
        static FontFamily UiFont = new FontFamily("Segoe UI"); // the chosen font, for measuring text before it is on screen

        /// The text size to use, evened out for the chosen font. In the corner panel every size up to 16 snaps to one
        /// of four steps a little over a tenth apart - 10 labels, 11 small, 12 body, 13.5 heading.
        static double Z(double size)
        {
            double step = !Slim ? size : size <= 10 ? 10 : size <= 11.5 ? 11 : size <= 13.5 ? 12 : size <= 16 ? 13.5 : size;
            return Math.Round(step * FontK * 2) / 2;
        }

        /// The size of a heading (always drawn semibold): a touch smaller in a font whose only heavy weight is bold.
        static double Hd(double size) { double z = Z(size); return HasSemi ? z : Math.Round(z * 0.95 * 2) / 2; }

        /// Emphasis for small text: semibold, or plain weight when the font has no semibold.
        static FontWeight SemiSmall { get { return HasSemi ? FontWeights.SemiBold : FontWeights.Normal; } }

        /// How wide a piece of text will be in the chosen font.
        static double TextW(string text, double size, FontWeight weight)
        {
            var tb = new TextBlock { Text = text, FontFamily = UiFont, FontSize = size, FontWeight = weight };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Math.Ceiling(tb.DesiredSize.Width);
        }

        /// The text, cut with "…" if it would wrap to more lines than asked for at this width. (A TextBlock's own
        /// trimming cuts a wrapped text on its first line, so the cut is found by measuring.)
        static string ClampLines(string text, double width, double size, int lines)
        {
            var tb = new TextBlock { FontFamily = UiFont, FontSize = size, TextWrapping = TextWrapping.Wrap };
            Func<string, double> height = s => { tb.Text = s; tb.Measure(new Size(width, double.PositiveInfinity)); return tb.DesiredSize.Height; };
            double max = height("X") * lines + 0.5;
            if (width <= 0 || height(text) <= max) return text;
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (height(text.Substring(0, mid).TrimEnd() + "…") <= max) lo = mid; else hi = mid - 1;
            }
            return text.Substring(0, lo).TrimEnd() + "…";
        }

        /// A measurement that is smaller in the corner panel: Sp(full, corner).
        static double Sp(double roomy, double slim) { return Slim ? slim : roomy; }

        // The grid both modes are drawn on. Every box (a tray or a white card) starts Side in from the panel's edge, and
        // its content PadX further (Inset). The header's round buttons start and end where the boxes do; its text starts
        // on the content line. Boxes are GapY apart and share one corner radius. Full Mode is two equal columns of
        // FullW / 2, with SideMid on the side of each that faces the other.
        static double Side { get { return Slim ? 12 : 18; } }
        static double PadX { get { return Slim ? 12 : 14; } }
        static double PadY { get { return Slim ? 11 : 13; } }
        static double Inset { get { return Side + PadX; } }
        static double GapY { get { return Slim ? 8 : 10; } }
        static double Radius { get { return Slim ? 20 : 22; } }
        static double SideMid { get { return Slim ? Side : 8; } }
        // the tasks column's outer and inner sides; Full Mode with "energy on the left" mirrors them
        static bool Swapped;
        static double TaskL { get { return Swapped && !Slim ? SideMid : Side; } }
        static double TaskR { get { return Swapped && !Slim ? Side : SideMid; } }
        const double RowOver = 6; // how far a task row's hover highlight reaches past its content, on both sides

        Border listCard, panelBg;   // the task list's white card; the panel itself
        StackPanel headIcons, headText; // the round buttons at the right of the header; the date at its left
        ColumnDefinition addCol; Path addPlus; // the add box: the + and the room it takes

        /// Spacing and text sizes of the parts that are built once, for the mode being shown. Called from Place.
        void Restyle()
        {
            Slim = !full; Swapped = Settings.Get("SwapSides", 0) > 0.5;
            panelBg.CornerRadius = new CornerRadius(Sp(32, T.Glass ? 28 : 26));
            shadow.Radius = panelBg.CornerRadius.TopLeft; shadow.Blur = Sp(42, 32); shadow.Depth = Sp(16, 12);
            SkinPanel(); // the panel's ground, tint and rim for the skin in use (Glass.cs)

            headerRow.Margin = new Thickness(Side, Sp(16, 11), Side, Sp(12, 9));
            headText.Margin = new Thickness(Slim ? PadX : 0, 0, 8, 0); // in Full Mode the round back button comes first
            dayText.FontSize = Hd(22); subText.FontSize = Z(Slim ? 13 : 12);
            double cell = Sp(34, 26);
            foreach (FrameworkElement c in headIcons.Children) { c.Width = cell; c.Height = cell; c.Margin = new Thickness(Sp(6, 4), 0, 0, 0); }
            minBtn.Width = cell; minBtn.Height = cell; minBtn.Margin = new Thickness(0, 0, 12, 0);
            double rk = Sp(0.75, 0.58); ringBox.LayoutTransform = new ScaleTransform(rk, rk);

            inputHint.FontSize = Z(13); input.FontSize = Z(13.5); inputPreview.FontSize = Z(11);
            addPill.Height = Sp(40, 32); tasksAdd.Margin = FullArranged ? new Thickness(TaskL, GapY, TaskR, 0) : new Thickness(TaskL, 0, TaskR, GapY); // Full Mode: under the list (FullLayout.cs)
            addPlus.Margin = new Thickness(PadX - 2, 0, 0, 0); addCol.Width = new GridLength(PadX - 2 + 14 + Sp(9, 7));
            inputChip.Margin = new Thickness(8, 0, Sp(7, 5), 0);
            actionRow.Height = 30;

            // the trays: the morning check-in, the 1-10 prompt, the next task
            foreach (var box in new[] { checkIn, feedbackBox, focusBox })
            {
                box.Background = T.Tray; box.BorderThickness = new Thickness(0); box.CornerRadius = new CornerRadius(Radius);
                box.Padding = new Thickness(PadX, PadY, PadX, PadY);
            }
            feedbackBox.Padding = new Thickness(PadX, PadY - 1, PadX - 2, PadY); // its row of numbers has 2 of its own at each end
            // (their margins, and those of the two prompts, are set in LayoutTop, Checks.cs)
            // a prompt that needs an answer is the one dark box; its buttons bring their own gap underneath
            alertBox.Background = T.Strong; alertBox.BorderThickness = new Thickness(0); alertBox.CornerRadius = new CornerRadius(Radius);
            alertBox.Padding = new Thickness(PadX + 2, PadY + 2, PadX, PadY + 2 - Sp(6, 5));
            // the white card of the task list
            listCard.CornerRadius = new CornerRadius(Radius); listCard.Margin = new Thickness(TaskL, 0, TaskR, 0);
            listCard.Padding = new Thickness(PadX - RowOver, Sp(8, 6), PadX - RowOver, Sp(8, 6));
            list.Margin = new Thickness(0);
            // whatever scrolls stops Side above the panel's bottom edge, inside its round corners
            tasksScroll.Margin = energyScroll.Margin = settingsScroll.Margin = new Thickness(0, 0, 0, Side);
            ClipList();
            LayoutTop();
        }

        /// A long task list scrolls inside its card's shape: the view is cut with the same round corners.
        void ClipList()
        {
            double w = tasksScroll.ActualWidth, h = tasksScroll.ActualHeight;
            tasksScroll.Clip = w > 0 && h > 0 ? new RectangleGeometry(new Rect(TaskL, 0, Math.Max(0, w - TaskL - TaskR), h), Radius, Radius) : null;
        }

        // ------------------------------------------------------------ controls

        /// A button, always a pill. kind: 0 = the main action (lime), 1 = secondary (a plain pill), 2 = quiet (text only).
        /// on: what it sits on, which decides the plain pill's colour: 0 = the panel or a tray, 1 = white, 2 = the dark prompt.
        static FrameworkElement Btn(string text, int kind, Action click, int on = 0)
        {
            Brush ink = kind == 0 ? T.OnMain : on == 2 ? (kind == 1 ? T.OnStrong : T.OnStrongSoft) : kind == 1 ? T.Text : T.Muted;
            Brush rest = kind == 0 ? T.Main : kind == 2 || on == 2 ? (Brush)Brushes.Transparent : on == 1 ? T.Raised : T.Field;
            Brush hot = on == 2 ? T.StrongLift : on == 1 ? T.Ctl : T.Lift;
            var label = new TextBlock
            {
                Text = text, FontSize = Z(11.5), FontWeight = kind == 0 ? SemiSmall : FontWeights.Normal, Padding = new Thickness(0, 0, 1, 0),
                Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center
            };
            var b = new PillBorder
            {
                Padding = Slim ? new Thickness(kind == 2 ? 5 : 12, 5, kind == 2 ? 5 : 12, 6) : new Thickness(kind == 2 ? 6 : 15, 7, kind == 2 ? 6 : 15, 8), Margin = new Thickness(0, 0, Sp(6, 5), Sp(6, 5)),
                Background = rest, BorderThickness = new Thickness(1), BorderBrush = kind == 1 && on == 2 ? (Brush)T.StrongLine : Brushes.Transparent,
                Child = label, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center
            };
            b.MouseEnter += (s, e) => { if (kind == 0) b.Opacity = 0.88; else if (kind == 1) b.Background = hot; else label.Foreground = on == 2 ? (Brush)T.OnStrong : T.Text; };
            b.MouseLeave += (s, e) => { if (kind == 0) b.Opacity = 1; else if (kind == 1) b.Background = rest; else label.Foreground = ink; };
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; click(); };
            return b;
        }

        /// A chip: a white pill, forest green while what it stands for is open or on.
        static PillBorder Chip(FrameworkElement content, bool on, string tip, Action click)
        {
            var b = new PillBorder { Height = 30, Background = on ? T.Strong : T.Field, Child = content, Cursor = Cursors.Hand, ToolTip = tip };
            if (!on) { b.MouseEnter += (s, e) => b.Background = T.Lift; b.MouseLeave += (s, e) => b.Background = T.Field; }
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; click(); };
            return b;
        }

        /// An on/off switch. On, its track is the colour of the main button and chosen tabs, with the knob in that colour's text colour.
        static FrameworkElement Switch(bool on, Action click)
        {
            var knob = new Ellipse
            {
                Width = 14, Height = 14, Fill = on ? (Brush)T.OnMain : T.Knob, Margin = new Thickness(3, 0, 3, 0),
                HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            };
            var b = new Border { Width = 36, Height = 20, CornerRadius = new CornerRadius(10), Background = on ? (Brush)T.Main : T.Ctl, Child = knob, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; Click.Play(); click(); }; // (Sound.cs)
            return b;
        }

        /// The five skins, as a row of pills that wraps: too many for joined segments in the corner panel.
        FrameworkElement SkinPicker()
        {
            string[] says = { "Sage, forest and lime", "Clear glass; dark over dark things", "Brushed silver, white enamel and steel", "Sage stoneware and glazed porcelain", "Blue card and white paper" };
            var sp = new StackPanel { Margin = new Thickness(0, Sp(10, 9), 0, Sp(6, 5)) };
            sp.Children.Add(new TextBlock { Text = "Look", FontSize = Z(12.5), Foreground = T.Text });
            sp.Children.Add(new TextBlock { Text = says[T.Skin], FontSize = Z(10.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, Sp(8, 7)) });
            var row = new WrapPanel();
            foreach (int i in new[] { 1, 2, 3, 4, 0 }) // Glass first (the default), then Metal, Ceramic, Paper, Sage; the numbers stay as saved
            {
                int v = i; bool on = i == T.Skin;
                var pill = new PillBorder { Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(0, 0, 5, 5), Background = on ? (Brush)T.Main : T.Raised, Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = T.Names[i], FontSize = Z(11), Foreground = on ? (Brush)T.OnMain : T.Text } };
                pill.MouseLeftButtonDown += (s, e) => { e.Handled = true; if (!on) SetSkin(v); };
                row.Children.Add(pill);
            }
            sp.Children.Add(row);
            return sp;
        }

        /// One choice out of a few, shown as joined segments; the chosen one is forest green. Drawn for a white surface.
        static FrameworkElement Segmented(string[] names, int chosen, Action<int> pick)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < names.Length; i++)
            {
                int v = i; bool on = i == chosen;
                var label = new TextBlock { Text = names[i], FontSize = Z(11), Foreground = on ? (Brush)T.OnMain : T.Muted };
                var seg = new PillBorder { Padding = new Thickness(11, 3, 11, 4), Background = on ? (Brush)T.Main : Brushes.Transparent, Child = label, Cursor = Cursors.Hand };
                seg.MouseLeftButtonDown += (s, e) => { e.Handled = true; if (!on) pick(v); };
                sp.Children.Add(seg);
            }
            return new PillBorder { Background = T.Raised, Padding = new Thickness(3), Child = sp, VerticalAlignment = VerticalAlignment.Center };
        }

        /// A small capital heading for a group.
        static TextBlock Caps(string text)
        {
            return new TextBlock { Text = text.ToUpperInvariant(), FontSize = Z(10), FontWeight = SemiSmall, Foreground = T.Muted };
        }

        /// A thin rule, for white surfaces.
        static Border Rule(Thickness margin) { return new Border { Height = 1, Background = T.Line, Margin = margin }; }

        /// Energy as one to three bars (0 = not set: three empty bars).
        static FrameworkElement Bars(int level)
        {
            var c = new Canvas { Width = 13, Height = 11, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            double[] h = { 4, 7, 11 };
            for (int i = 0; i < 3; i++)
            {
                var r = new Rectangle { Width = 3, Height = h[i], RadiusX = 1, RadiusY = 1, Fill = i < level ? (Brush)T.Strong : T.Ctl };
                Canvas.SetLeft(r, i * 5); Canvas.SetTop(r, 11 - h[i]);
                c.Children.Add(r);
            }
            return c;
        }

        /// A setting on one line: what it is on the left, its control on the right.
        static FrameworkElement SettingLine(string title, string sub, FrameworkElement control)
        {
            var g = new Grid { Margin = new Thickness(0, Sp(10, 9), 0, Sp(10, 9)) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = Z(12.5), Foreground = T.Text, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(sub)) sp.Children.Add(new TextBlock { Text = sub, FontSize = Z(10.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            g.Children.Add(sp);
            control.VerticalAlignment = VerticalAlignment.Center; control.Margin = new Thickness(0);
            Grid.SetColumn(control, 1); g.Children.Add(control);
            return g;
        }

        /// A small round-ended button, for the - and + steppers. Drawn for a white surface.
        static FrameworkElement Step(string sign, Action click)
        {
            // the sign is drawn, not typed: a typed "+" and "–" sit at different heights and off the middle
            var label = Glyph(sign == "+" ? "M5,1.5 V8.5 M1.5,5 H8.5" : "M1.5,5 H8.5", T.Text, 1.4, 10, 10);
            label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
            var b = new PillBorder { Width = 26, Height = 20, Background = T.Raised, Child = label, Cursor = Cursors.Hand };
            b.MouseEnter += (s, e) => b.Background = T.Ctl;
            b.MouseLeave += (s, e) => b.Background = T.Raised;
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; click(); };
            return b;
        }

        /// The colour that says where a moment falls on today's curve: lime on a peak, forest in the dip, grey between.
        Brush ZoneBrush(DateTime t)
        {
            if (energy == null) return T.Ctl;
            foreach (var z in new[] { energy.Peak1, energy.Dip, energy.Peak2 })
                if (z != null && t >= z.From && t < z.To) return z.Peak ? (Brush)T.Accent : T.Strong;
            return T.Ctl;
        }

        // ------------------------------------------------------------ the 1-10 prompt

        /// Shows the check-in when one is due (or was asked for). Never together with the morning sleep check-in.
        void ShowFeedback(DateTime now)
        {
            if (Bare && !rateNow) { feedbackBox.Visibility = Visibility.Collapsed; return; } // at rest the corner panel is the next task alone (Focus.cs)
            if (checkIn.Visibility == Visibility.Visible || energy == null || (!rateNow && InTask) || !(rateNow || Feedback.Due(now, energy)))
            {
                feedbackBox.Visibility = Visibility.Collapsed;
                return;
            }
            Action<string> answer = a =>
            {
                var at = Now();
                Feedback.Log(at, a, energy, activeToday, ActiveSession(at));
                rateNow = false;
                feedbackBox.Visibility = Visibility.Collapsed;
                if (promptOnly && AlertHolds()) FocusLayout(); // the time's-up prompt beside it still needs an answer
                else if (promptOnly) HidePanel(); else RefreshEnergy(false); // a new answer may move the tuned curve
            };
            var sp = new StackPanel();
            // one line: the question, the two ends of the scale (corner panel), and the cross at the far end
            var top = new Grid { Margin = new Thickness(0, 0, 2, Sp(9, 7)) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(new TextBlock { Text = "How fresh do you feel?", FontSize = Z(12.5), FontWeight = SemiSmall, Foreground = T.Text, VerticalAlignment = VerticalAlignment.Center });
            var scale = new TextBlock { Text = "1 drained · 10 fresh", FontSize = Z(10), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Slim ? Visibility.Visible : Visibility.Collapsed };
            Grid.SetColumn(scale, 1); top.Children.Add(scale);
            var skip = new Grid { Width = 16, Height = 16, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Skip" };
            var cross = Glyph("M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5", T.Muted, 1.4, 10, 10);
            skip.Children.Add(cross);
            skip.MouseEnter += (s, e) => cross.Stroke = T.Text;
            skip.MouseLeave += (s, e) => cross.Stroke = T.Muted;
            skip.MouseLeftButtonDown += (s, e) => { e.Handled = true; answer("skipped"); };
            Grid.SetColumn(skip, 2); top.Children.Add(skip);
            sp.Children.Add(top);

            // ten separate cells; the one under the mouse turns lime
            var bar = new UniformGrid { Columns = 10, Margin = new Thickness(-1.5, 0, 0.5, 0) };
            for (int i = 1; i <= 10; i++)
            {
                string a = i.ToString();
                var label = new TextBlock { Text = a, FontSize = Z(11.5), Foreground = T.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var cell = new RimBorder { Height = Sp(34, 28), CornerRadius = new CornerRadius(Sp(11, 9)), Margin = new Thickness(1.5, 0, 1.5, 0), Background = T.Lift, Child = label, Cursor = Cursors.Hand };
                cell.MouseEnter += (s, e) => { cell.Background = T.Accent; label.FontWeight = SemiSmall; };
                cell.MouseLeave += (s, e) => { cell.Background = T.Lift; label.FontWeight = FontWeights.Normal; };
                cell.MouseLeftButtonDown += (s, e) => { e.Handled = true; answer(a); };
                bar.Children.Add(cell);
            }
            sp.Children.Add(bar);
            var ends = new Grid { Margin = new Thickness(2, 6, 4, 0), Visibility = Slim ? Visibility.Collapsed : Visibility.Visible };
            ends.Children.Add(new TextBlock { Text = "drained", FontSize = Z(10.5), Foreground = T.Muted });
            ends.Children.Add(new TextBlock { Text = "fresh", FontSize = Z(10.5), Foreground = T.Muted, HorizontalAlignment = HorizontalAlignment.Right });
            sp.Children.Add(ends);
            if (promptOnly && !AlertHolds())
            {   // popped up by itself: a thin line runs out over the 15 seconds it stays
                var st = new ScaleTransform(1, 1);
                sp.Children.Add(new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = T.Strong, Margin = new Thickness(0, Sp(10, 8), 2, 0), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = st });
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(15)));
            }
            feedbackBox.Child = sp;
            feedbackBox.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------ Settings page

        /// A small picture of the screen; click a corner to make it the one that opens the list.
        FrameworkElement CornerPicker()
        {
            string[] names = { "Top right", "Top left", "Bottom right", "Bottom left" };
            var g = new Grid { Width = 60, Height = 44 };
            g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 4; i++)
            {
                int v = i; bool on = corner == v, right = v == 0 || v == 2, top = v < 2;
                var dot = new Border
                {
                    Width = on ? 10 : 6, Height = on ? 10 : 6, CornerRadius = new CornerRadius(on ? 3 : 2), Background = on ? (Brush)T.Main : T.Ctl, Margin = new Thickness(on ? 5 : 7),
                    HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom
                };
                var cell = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = names[v] };
                cell.Children.Add(dot);
                cell.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    corner = v; Settings.Set("Corner", v); Settings.Set("Moved", 0); // a new corner puts the panel back beside it
                    if (shown && !full) Place();
                    DrawSettings();
                };
                Grid.SetRow(cell, top ? 0 : 1); Grid.SetColumn(cell, right ? 1 : 0);
                g.Children.Add(cell);
            }
            return new Border { CornerRadius = new CornerRadius(10), Background = T.Raised, Child = g };
        }

        Panel setCol;  // the column of the Settings page being filled
        double setW;   // how wide the inside of a card in it is

        /// The heading over a card on the Settings page; it starts on the content line.
        static TextBlock SetCaps(string text)
        {
            var cap = Caps(text); cap.Margin = new Thickness(PadX, 2, 0, 6);
            return cap;
        }

        /// A white card on the Settings page. Add its lines with SetLine, which rules them off from each other.
        StackPanel SetCard(Panel into)
        {
            var sp = new StackPanel();
            into.Children.Add(new RimBorder { CornerRadius = new CornerRadius(Radius), Background = T.Field, Padding = new Thickness(PadX, 1, PadX, 1), Margin = new Thickness(0, 0, 0, GapY), Child = sp });
            return sp;
        }

        static void SetLine(Panel card, FrameworkElement line)
        {
            if (card.Children.Count > 0) card.Children.Add(Rule(new Thickness(0)));
            card.Children.Add(line);
        }

        /// Text size, corner, start-up and position, then energy and sleep: white cards on the panel.
        /// The corner panel shows one column. Full Mode shows two equal ones: the panel, and energy and sleep.
        void DrawSettings()
        {
            settingsView.Children.Clear();
            settingsView.HorizontalAlignment = HorizontalAlignment.Stretch; settingsView.Width = double.NaN; settingsView.MaxWidth = double.PositiveInfinity;
            settingsView.Margin = new Thickness(Side, 0, Side, -GapY); // the last card gives its gap back
            double gutter = 2 * SideMid; // the two columns are where the two columns of the main view are
            setW = (full ? (FullW - 2 * Side - gutter) / 2 : CardW - 2 * Side) - 2 * PadX;
            var top = new Grid { Margin = new Thickness(PadX, 0, 0, GapY) };
            top.Children.Add(new TextBlock { Text = "Settings", FontSize = Hd(16), FontWeight = FontWeights.SemiBold, Foreground = T.Text, VerticalAlignment = VerticalAlignment.Center });
            var done = Btn("Done", 0, CloseSettings);
            done.HorizontalAlignment = HorizontalAlignment.Right; done.Margin = new Thickness(0);
            top.Children.Add(done);
            settingsView.Children.Add(top);

            Panel look = settingsView, feel = settingsView;
            if (full)
            {
                var cols = new Grid();
                cols.ColumnDefinitions.Add(new ColumnDefinition());
                cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gutter) });
                cols.ColumnDefinitions.Add(new ColumnDefinition());
                look = new StackPanel(); feel = new StackPanel();
                Grid.SetColumn(feel, 2); cols.Children.Add(look); cols.Children.Add(feel);
                settingsView.Children.Add(cols);
            }

            // ---- the panel itself
            setCol = look;
            DrawPlanningSettings(); // first: plan by schedule or priority, the stopwatch, the work log (Work.cs)
            setCol.Children.Add(SetCaps("Panel"));
            var card = SetCard(setCol);
            SetLine(card, SkinPicker()); // Sage, Glass, Metal, Ceramic or Paper (Glass.cs, Material.cs)
            var size = new StackPanel { Orientation = Orientation.Horizontal };
            size.Children.Add(Step("–", () => { SetScale(CurrentScale - 0.05, true); DrawSettings(); }));
            size.Children.Add(new TextBlock { Text = (int)Math.Round(CurrentScale * 100) + "%", Width = 42, TextAlignment = TextAlignment.Center, FontSize = Z(11.5), FontWeight = SemiSmall, Foreground = T.Text, VerticalAlignment = VerticalAlignment.Center });
            size.Children.Add(Step("+", () => { SetScale(CurrentScale + 0.05, true); DrawSettings(); }));
            SetLine(card, SettingLine(full ? "Text size in Full Mode" : "Text size", "Ctrl + scroll also works", size));
            string[] corners = { "Top right", "Top left", "Bottom right", "Bottom left" };
            SetLine(card, SettingLine("Hot corner", corners[corner] + " opens the list", CornerPicker()));
            bool auto = Autostart.Enabled;
            SetLine(card, SettingLine("Start with Windows", null, Switch(auto, () => { Autostart.Set(!auto); DrawSettings(); })));
            bool clicks = Settings.Get("Click", 1) > 0.5; // the tick when a task is ticked off or a switch is turned (Sound.cs)
            SetLine(card, SettingLine("Click sounds", null, Switch(clicks, () => { Settings.Set("Click", clicks ? 0 : 1); DrawSettings(); })));
            SetLine(card, SettingLine("Full Mode sides", Swapped ? "Energy on the left, tasks on the right" : "Tasks on the left, energy on the right",
                Segmented(new[] { "Tasks left", "Energy left" }, Swapped ? 1 : 0, i => { Settings.Set("SwapSides", i); Swapped = i == 1; Restyle(); DrawSettings(); })));
            SetLine(card, SettingLine("Panel position", "Drag the header to move it", Btn("Reset", 1, () => { ResetPosition(); DrawSettings(); }, 1)));
            DrawUpdates(card); // (Update.cs)

            // ---- energy and sleep
            setCol = feel;
            setCol.Children.Add(SetCaps("Energy"));
            card = SetCard(setCol);
            SetLine(card, SettingLine("Clock", null, Segmented(new[] { "24h", "am/pm" }, EnergyDay.Use12 ? 1 : 0, i => { Settings.Set("Clock12", i); RefreshEnergy(false); DrawSettings(); })));
            bool tune = Settings.Get("Tune", 1) > 0.5;
            string tuneStatus = Tuning.Count < Tuning.MinRatings
                ? Tuning.Count + " of " + Tuning.MinRatings + " check-ins so far"
                : (tune ? "Curve adjusted from " : "Off · ") + Tuning.Count + " check-ins";
            SetLine(card, SettingLine("Tuning", tuneStatus, Switch(tune, () => { Settings.Set("Tune", tune ? 0 : 1); RefreshEnergy(false); DrawSettings(); })));
            int feels = Calibration.Count(Now());
            if (feels > 0)
                SetLine(card, SettingLine("Energy %", "Matched to " + feels + (feels == 1 ? " correction" : " corrections") + " you typed in",
                    Btn("Reset", 1, () => { Calibration.Clear(); DrawSettings(); if (tab == 1 || full) DrawEnergy(); }, 1)));
            card.Children.Add(Rule(new Thickness(0)));
            sect = card; DrawPrayer(); sect = energyView; // "I usually wake for Fajr" (UiEnergy.cs)

            DrawRecord(); // the last seven days: done, high effort, routines (Stats.cs)

            DrawSleepHistory(); // weekly and monthly averages and the 30-night pattern (UiSleep.cs)
        }
    }
}
