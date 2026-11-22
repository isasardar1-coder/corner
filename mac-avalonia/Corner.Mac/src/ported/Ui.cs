// Ported from corner/src/Ui.cs (WPF) to Avalonia: tools/port_ui.py, then fixed by hand.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Corner
{
    /// A box with fully round ends: buttons, chips, pills, the round icon buttons, boxes to type in.
    /// The radius follows its height, because a fixed radius larger than half the height is squashed into an oval.
    class PillBorder : RimBorder
    {
        public PillBorder() { CornerRadius = new CornerRadius(12); } // until it has a size
        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            if (e.NewSize.Height > 0) CornerRadius = new CornerRadius(e.NewSize.Height / 2);
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
        static void Lean(TextBlock t) { if (UiItalic != null) { t.FontFamily = UiItalic; t.FontStyle = FontStyle.Italic; t.FontWeight = FontWeight.SemiBold; t.Padding = new Thickness(0, 0, 2, 0); } } // 2: room for the last letter's lean
        static void Lean(TextElement t) { if (UiItalic != null) { t.FontFamily = UiItalic; t.FontStyle = FontStyle.Italic; t.FontWeight = FontWeight.SemiBold; } }
        static FontFamily UiFont = FontFamily.Default; // the chosen font, for measuring text before it is on screen

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
        static FontWeight SemiSmall { get { return HasSemi ? FontWeight.SemiBold : FontWeight.Normal; } }

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
        const double RowOver = 6; // how far a task row's hover highlight reaches past its content, on both sides

        Border listCard, panelBg;   // the task list's white card; the panel itself
        StackPanel headIcons, headText; // the round buttons at the right of the header; the date at its left
        ColumnDefinition addCol; Path addPlus; // the add box: the + and the room it takes

        /// Spacing and text sizes of the parts that are built once, for the mode being shown. Called from Place.
        void Restyle()
        {
            Slim = !full;
            panelBg.CornerRadius = new CornerRadius(Sp(32, T.Glass ? 28 : 26));
            shadow.BlurRadius = Sp(42, 32); shadow.ShadowDepth = Sp(16, 12);
            SkinPanel(); // the panel's ground, tint and rim for the skin in use (Glass.cs)

            headerRow.Margin = new Thickness(Side, Sp(16, 11), Side, Sp(12, 9));
            headText.Margin = new Thickness(Slim ? PadX : 0, 0, 8, 0); // in Full Mode the round back button comes first
            dayText.FontSize = Hd(22); subText.FontSize = Z(Slim ? 13 : 12);
            double cell = Sp(34, 26);
            foreach (Control c in headIcons.Children) { c.Width = cell; c.Height = cell; c.Margin = new Thickness(Sp(6, 4), 0, 0, 0); }
            minBtn.Width = cell; minBtn.Height = cell; minBtn.Margin = new Thickness(0, 0, 12, 0);
            double rk = Sp(0.75, 0.58); ringBox.Margin = new Thickness(0); ringBox.LayoutScale(rk, 24, 24);

            inputHint.FontSize = Z(13); input.FontSize = Z(13.5); inputPreview.FontSize = Z(11);
            tasksAdd.Height = Sp(40, 32); tasksAdd.Margin = new Thickness(Side, 0, SideMid, GapY);
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
            listCard.CornerRadius = new CornerRadius(Radius); listCard.Margin = new Thickness(Side, 0, SideMid, 0);
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
            double w = tasksScroll.Bounds.Width, h = tasksScroll.Bounds.Height;
            tasksScroll.Clip = w > 0 && h > 0 ? new RectangleGeometry(new Rect(Side, 0, Math.Max(0, w - Side - SideMid), h), Radius, Radius) : null;
        }

        // ------------------------------------------------------------ controls

        /// A button, always a pill. kind: 0 = the main action (lime), 1 = secondary (a plain pill), 2 = quiet (text only).
        /// on: what it sits on, which decides the plain pill's colour: 0 = the panel or a tray, 1 = white, 2 = the dark prompt.
        static Control Btn(string text, int kind, Action click, int on = 0)
        {
            IBrush ink = kind == 0 ? T.OnAccent : on == 2 ? (kind == 1 ? T.OnStrong : T.OnStrongSoft) : kind == 1 ? T.Text : T.Muted;
            IBrush rest = kind == 0 ? T.Accent : kind == 2 || on == 2 ? (IBrush)Brushes.Transparent : on == 1 ? T.Raised : T.Field;
            IBrush hot = on == 2 ? T.StrongLift : on == 1 ? T.Ctl : T.Lift;
            var label = new TextBlock
            {
                Text = text, FontSize = Z(11.5), FontWeight = kind == 0 ? SemiSmall : FontWeight.Normal, Padding = new Thickness(0, 0, 1, 0),
                Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center
            };
            var b = new PillBorder
            {
                Padding = Slim ? new Thickness(kind == 2 ? 5 : 12, 5, kind == 2 ? 5 : 12, 6) : new Thickness(kind == 2 ? 6 : 15, 7, kind == 2 ? 6 : 15, 8), Margin = new Thickness(0, 0, Sp(6, 5), Sp(6, 5)),
                Background = rest, BorderThickness = new Thickness(1), BorderBrush = kind == 1 && on == 2 ? (IBrush)T.StrongLine : Brushes.Transparent,
                Child = label, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center
            };
            b.PointerEntered += (s, e) => { if (kind == 0) b.Opacity = 0.88; else if (kind == 1) b.Background = hot; else label.Foreground = on == 2 ? (IBrush)T.OnStrong : T.Text; };
            b.PointerExited += (s, e) => { if (kind == 0) b.Opacity = 1; else if (kind == 1) b.Background = rest; else label.Foreground = ink; };
            b.OnLeftDown((s, e) => { e.Handled = true; click(); });
            return b;
        }

        /// A chip: a white pill, forest green while what it stands for is open or on.
        static PillBorder Chip(Control content, bool on, string tip, Action click)
        {
            var b = new PillBorder { Height = 30, Background = on ? T.Strong : T.Field, Child = content, Cursor = Cursors.Hand, [ToolTip.TipProperty] = tip };
            if (!on) { b.PointerEntered += (s, e) => b.Background = T.Lift; b.PointerExited += (s, e) => b.Background = T.Field; }
            b.OnLeftDown((s, e) => { e.Handled = true; click(); });
            return b;
        }

        /// An on/off switch: a forest track with a lime knob when on.
        static Control Switch(bool on, Action click)
        {
            var knob = new Ellipse
            {
                Width = 14, Height = 14, Fill = on ? (IBrush)T.Accent : T.Field, Margin = new Thickness(3, 0, 3, 0),
                HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            };
            var b = new Border { Width = 36, Height = 20, CornerRadius = new CornerRadius(10), Background = on ? (IBrush)T.Strong : T.Ctl, Child = knob, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            b.OnLeftDown((s, e) => { e.Handled = true; click(); });
            return b;
        }

        /// One choice out of a few, shown as joined segments; the chosen one is forest green. Drawn for a white surface.
        static Control Segmented(string[] names, int chosen, Action<int> pick)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < names.Length; i++)
            {
                int v = i; bool on = i == chosen;
                var label = new TextBlock { Text = names[i], FontSize = Z(11), Foreground = on ? (IBrush)T.OnStrong : T.Muted };
                var seg = new PillBorder { Padding = new Thickness(11, 3, 11, 4), Background = on ? (IBrush)T.Strong : Brushes.Transparent, Child = label, Cursor = Cursors.Hand };
                seg.OnLeftDown((s, e) => { e.Handled = true; if (!on) pick(v); });
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
        static Control Bars(int level)
        {
            var c = new Canvas { Width = 13, Height = 11, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            double[] h = { 4, 7, 11 };
            for (int i = 0; i < 3; i++)
            {
                var r = new Rectangle { Width = 3, Height = h[i], RadiusX = 1, RadiusY = 1, Fill = i < level ? (IBrush)T.Strong : T.Ctl };
                Canvas.SetLeft(r, i * 5); Canvas.SetTop(r, 11 - h[i]);
                c.Children.Add(r);
            }
            return c;
        }

        /// A setting on one line: what it is on the left, its control on the right.
        static Control SettingLine(string title, string sub, Control control)
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
        static Control Step(string sign, Action click)
        {
            var label = new TextBlock { Text = sign, FontSize = Z(12), Foreground = T.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) };
            var b = new PillBorder { Width = 26, Height = 20, Background = T.Raised, Child = label, Cursor = Cursors.Hand };
            b.PointerEntered += (s, e) => b.Background = T.Ctl;
            b.PointerExited += (s, e) => b.Background = T.Raised;
            b.OnLeftDown((s, e) => { e.Handled = true; click(); });
            return b;
        }

        /// The colour that says where a moment falls on today's curve: lime on a peak, forest in the dip, grey between.
        IBrush ZoneBrush(DateTime t)
        {
            if (energy == null) return T.Ctl;
            foreach (var z in new[] { energy.Peak1, energy.Dip, energy.Peak2 })
                if (z != null && t >= z.From && t < z.To) return z.Peak ? (IBrush)T.Accent : T.Strong;
            return T.Ctl;
        }

        // ------------------------------------------------------------ the 1-10 prompt

        /// Shows the check-in when one is due (or was asked for). Never together with the morning sleep check-in.
        void ShowFeedback(DateTime now)
        {
            if (Bare && !rateNow) { feedbackBox.IsVisible = false; return; } // at rest the corner panel is the next task alone (Focus.cs)
            if (checkIn.IsVisible == true || energy == null || (!rateNow && InTask) || !(rateNow || Feedback.Due(now, energy)))
            {
                feedbackBox.IsVisible = false;
                return;
            }
            Action<string> answer = a =>
            {
                var at = Now();
                Feedback.Log(at, a, energy, activeToday, ActiveSession(at));
                rateNow = false;
                feedbackBox.IsVisible = false;
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
            var scale = new TextBlock { Text = "1 drained · 10 fresh", FontSize = Z(10), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = Slim ? true : false };
            Grid.SetColumn(scale, 1); top.Children.Add(scale);
            var skip = new Grid { Width = 16, Height = 16, Background = Brushes.Transparent, Cursor = Cursors.Hand, [ToolTip.TipProperty] = "Skip" };
            var cross = Glyph("M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5", T.Muted, 1.4, 10, 10);
            skip.Children.Add(cross);
            skip.PointerEntered += (s, e) => cross.Stroke = T.Text;
            skip.PointerExited += (s, e) => cross.Stroke = T.Muted;
            skip.OnLeftDown((s, e) => { e.Handled = true; answer("skipped"); });
            Grid.SetColumn(skip, 2); top.Children.Add(skip);
            sp.Children.Add(top);

            // ten separate cells; the one under the mouse turns lime
            var bar = new UniformGrid { Columns = 10, Margin = new Thickness(-1.5, 0, 0.5, 0) };
            for (int i = 1; i <= 10; i++)
            {
                string a = i.ToString();
                var label = new TextBlock { Text = a, FontSize = Z(11.5), Foreground = T.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var cell = new Border { Height = Sp(34, 28), CornerRadius = new CornerRadius(Sp(11, 9)), Margin = new Thickness(1.5, 0, 1.5, 0), Background = T.Lift, Child = label, Cursor = Cursors.Hand };
                cell.PointerEntered += (s, e) => { cell.Background = T.Accent; label.FontWeight = SemiSmall; };
                cell.PointerExited += (s, e) => { cell.Background = T.Lift; label.FontWeight = FontWeight.Normal; };
                cell.OnLeftDown((s, e) => { e.Handled = true; answer(a); });
                bar.Children.Add(cell);
            }
            sp.Children.Add(bar);
            var ends = new Grid { Margin = new Thickness(2, 6, 4, 0), IsVisible = Slim ? false : true };
            ends.Children.Add(new TextBlock { Text = "drained", FontSize = Z(10.5), Foreground = T.Muted });
            ends.Children.Add(new TextBlock { Text = "fresh", FontSize = Z(10.5), Foreground = T.Muted, HorizontalAlignment = HorizontalAlignment.Right });
            sp.Children.Add(ends);
            if (promptOnly && !AlertHolds())
            {   // popped up by itself: a thin line runs out over the 15 seconds it stays
                var st = new ScaleTransform(1, 1);
                sp.Children.Add(new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = T.Strong, Margin = new Thickness(0, Sp(10, 8), 2, 0), RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative), RenderTransform = st });
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(15)));
            }
            feedbackBox.Child = sp;
            feedbackBox.IsVisible = true;
        }

        // ------------------------------------------------------------ Settings page

        /// A small picture of the screen; click a corner to make it the one that opens the list.
        Control CornerPicker()
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
                    Width = on ? 10 : 6, Height = on ? 10 : 6, CornerRadius = new CornerRadius(on ? 3 : 2), Background = on ? (IBrush)T.Strong : T.Ctl, Margin = new Thickness(on ? 5 : 7),
                    HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom
                };
                var cell = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, [ToolTip.TipProperty] = names[v] };
                cell.Children.Add(dot);
                cell.OnLeftDown((s, e) =>
                {
                    e.Handled = true;
                    corner = v; Settings.Set("Corner", v); Settings.Set("Moved", 0); // a new corner puts the panel back beside it
                    if (shown && !full) Place();
                    DrawSettings();
                });
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

        static void SetLine(Panel card, Control line)
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
            top.Children.Add(new TextBlock { Text = "Settings", FontSize = Hd(16), FontWeight = FontWeight.SemiBold, Foreground = T.Text, VerticalAlignment = VerticalAlignment.Center });
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
            setCol.Children.Add(SetCaps("Panel"));
            var card = SetCard(setCol);
            SetLine(card, SettingLine("Look", T.Glass ? "Frosted glass over what is behind" : "Sage, forest and lime", Segmented(T.Names, T.Skin, SetSkin))); // (Glass.cs)
            var size = new StackPanel { Orientation = Orientation.Horizontal };
            size.Children.Add(Step("–", () => { SetScale(CurrentScale - 0.05, true); DrawSettings(); }));
            size.Children.Add(new TextBlock { Text = (int)Math.Round(CurrentScale * 100) + "%", Width = 42, TextAlignment = TextAlignment.Center, FontSize = Z(11.5), FontWeight = SemiSmall, Foreground = T.Text, VerticalAlignment = VerticalAlignment.Center });
            size.Children.Add(Step("+", () => { SetScale(CurrentScale + 0.05, true); DrawSettings(); }));
            SetLine(card, SettingLine(full ? "Text size in Full Mode" : "Text size", Os.ZoomKeyName + " + scroll also works", size));
            string[] corners = { "Top right", "Top left", "Bottom right", "Bottom left" };
            SetLine(card, SettingLine("Hot corner", corners[corner] + " opens the list", CornerPicker()));
            bool auto = Autostart.Enabled;
            SetLine(card, SettingLine(Os.Mac ? "Open at login" : "Start with Windows", null, Switch(auto, () => { Autostart.Set(!auto); DrawSettings(); })));
            SetLine(card, SettingLine("Panel position", "Drag the header to move it", Btn("Reset", 1, () => { ResetPosition(); DrawSettings(); }, 1)));

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
            card.Children.Add(Rule(new Thickness(0)));
            sect = card; DrawPrayer(); sect = energyView; // "I usually wake for Fajr" (UiEnergy.cs)

            DrawRecord(); // the last seven days: done, high effort, routines (Stats.cs)

            DrawSleepHistory(); // weekly and monthly averages and the 30-night pattern (UiSleep.cs)
        }
    }
}
