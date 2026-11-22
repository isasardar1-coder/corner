// Ported from corner/src/Onboard.cs (WPF) to Avalonia: tools/port_ui.py, then fixed by hand.
using System;
using System.Collections.Generic;
using System.Linq;
using IO = System.IO;

namespace Corner
{
    partial class Shell
    {
        const int ObSteps = 4;
        Window onboard;
        Grid obStage;
        Border[] obDots;
        Control obBack;
        TextBlock obNextText;
        int obStep;
        string obName = "";
        double obNeed = EnergyModel.DefaultNeed;
        readonly List<Routine> obRoutines = new List<Routine>();
        string obEffort = "medium";
        TextBox obFocus; // the box to type in on the step showing

        static double Fk(double size) { return Math.Round(size * FontK * 2) / 2; }

        /// Opens the welcome window at `step` (0 = the start).
        void ShowOnboarding(int step = 0)
        {
            obStep = step;
            var card = new Grid();
            var bg = new Border { CornerRadius = new CornerRadius(26), Background = T.Glass ? T.GlassBase : (IBrush)T.Bg, BorderBrush = T.Edge, BorderThickness = new Thickness(T.Glass ? 0 : 1),
                Effect = new DropShadowDirectionEffect { BlurRadius = 40, ShadowDepth = 12, Direction = 270, Color = T.Glass ? T.GlassShadow : Color.FromRgb(60, 54, 34), Opacity = 0.28 } };
            card.Children.Add(bg);
            if (T.Glass) card.Children.Add(new Border { CornerRadius = new CornerRadius(26), Background = T.GlassTint, BorderBrush = T.GlassRim, BorderThickness = new Thickness(1), IsHitTestVisible = false });

            var layout = new Grid { Margin = new Thickness(34, 26, 34, 28) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            card.Children.Add(layout);

            // top: where you are (dots), and a way out
            var top = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            obDots = new Border[ObSteps];
            for (int i = 0; i < ObSteps; i++)
            {
                obDots[i] = new Border { Height = 8, Width = i == step ? 24 : 8, CornerRadius = new CornerRadius(4), Background = i <= step ? (IBrush)T.Strong : T.Ctl, Margin = new Thickness(0, 0, 6, 0) };
                dots.Children.Add(obDots[i]);
            }
            top.Children.Add(dots);
            var close = new Grid { Width = 28, Height = 28, Background = Brushes.Transparent, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, [ToolTip.TipProperty] = "Skip setup" };
            var cross = Glyph("M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5", T.Muted, 1.5, 10, 10);
            close.Children.Add(cross);
            close.PointerEntered += (s, e) => cross.Stroke = T.Text; close.PointerExited += (s, e) => cross.Stroke = T.Muted;
            close.OnLeftDown((s, e) => { e.Handled = true; ObFinish(); });
            top.Children.Add(close);
            layout.Children.Add(top);

            obStage = new Grid { ClipToBounds = false };
            Grid.SetRow(obStage, 1); layout.Children.Add(obStage);

            // bottom: back and continue
            var foot = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            obBack = ObButton("Back", false, () => ObGo(obStep - 1));
            obBack.HorizontalAlignment = HorizontalAlignment.Left;
            foot.Children.Add(obBack);
            var next = ObButton("Continue", true, ObNext);
            next.HorizontalAlignment = HorizontalAlignment.Right;
            obNextText = (TextBlock)((Border)next).Child;
            foot.Children.Add(next);
            Grid.SetRow(foot, 2); layout.Children.Add(foot);

            var scale = new ScaleTransform(0.94, 0.94);
            var root = new Grid { Margin = new Thickness(30), RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), RenderTransform = scale, Opacity = 0 };
            TextElementFont(root);
            root.Children.Add(card);
            card.OnLeftDown((s, e) => { try { onboard.BeginMoveDrag(e); } catch { } });

            onboard = new FloatWindow
            {
                Title = "Welcome to Corner", ShowInTaskbar = true, Topmost = false, ShowActivated = true, Width = 520, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = root, FontFamily = UiFont
            };
            onboard.AddHandler(KeyDownEvent, (s, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; ObFinish(); }
                else if (e.Key == Key.Enter && !(obStep == 2 && obFocus != null && obFocus.IsFocused && obFocus.Text.Trim().Length > 0)) { e.Handled = true; ObNext(); }
            }, RoutingStrategies.Tunnel);
            ((FloatWindow)onboard).ContentRendered += (s, e) =>
            {
                Anim(root, OpacityProperty, 1, 260);
                Anim(scale, ScaleTransform.ScaleXProperty, 1, 520, ease: Pop); Anim(scale, ScaleTransform.ScaleYProperty, 1, 520, ease: Pop);
            };
            ObShow(step, 0);
            onboard.Show();
            onboard.Activate();
        }

        static void TextElementFont(Control e) { TextElement.SetFontFamily(e, UiFont); }

        void ObNext()
        {
            if (obStep == 0 && obFocus != null) obName = obFocus.Text.Trim();
            if (obStep >= ObSteps - 1) { ObFinish(); return; }
            ObGo(obStep + 1);
        }

        void ObGo(int to)
        {
            if (to < 0 || to >= ObSteps || to == obStep) return;
            if (obStep == 0 && obFocus != null) obName = obFocus.Text.Trim();
            int dir = to > obStep ? 1 : -1;
            obStep = to;
            ObShow(to, dir);
        }

        /// Puts step `i` on the stage: the old one slides out, the new one slides in with its lines one after another.
        void ObShow(int i, int dir)
        {
            foreach (Control old in obStage.Children.Cast<Control>().ToList())
            {
                var shift = new TranslateTransform(); old.RenderTransform = shift; old.IsHitTestVisible = false;
                Anim(shift, TranslateTransform.XProperty, -dir * 40, 220, ease: Out);
                var o = old; Anim(old, OpacityProperty, 0, 180, done: () => obStage.Children.Remove(o));
            }
            for (int k = 0; k < ObSteps; k++)
            {
                Anim(obDots[k], WidthProperty, k == i ? 24 : 8, 320, ease: Out);
                obDots[k].Background = k <= i ? (IBrush)T.Strong : T.Ctl;
            }
            obBack.Opacity = i == 0 ? 0 : 1; obBack.IsHitTestVisible = i != 0;
            obNextText.Text = i == ObSteps - 1 ? "Start" : i == 2 && obRoutines.Count == 0 ? "Skip for now" : "Continue";

            obFocus = null;
            var page = i == 0 ? ObWelcome() : i == 1 ? ObSleep() : i == 2 ? ObRoutines() : ObDone();
            var slide = new TranslateTransform(dir * 46, 0);
            page.RenderTransform = slide;
            obStage.Children.Add(page);
            Anim(slide, TranslateTransform.XProperty, 0, 420, delay: dir == 0 ? 120 : 60, ease: Out);
            int n = 0;
            foreach (Control c in page.Children)
            {   // each line rises into place a moment after the one above it
                var rise = new TranslateTransform(0, 16); c.RenderTransform = rise; c.Opacity = 0;
                int delay = (dir == 0 ? 220 : 120) + 70 * n++;
                Anim(c, OpacityProperty, 1, 340, delay: delay);
                Anim(rise, TranslateTransform.YProperty, 0, 480, delay: delay, ease: Out);
            }
            if (obFocus != null) { var f = obFocus; After(dir == 0 ? 450 : 300, () => { f.Focus(); f.CaretIndex = f.Text.Length; }); }
        }

        // ------------------------------------------------------------ the four steps

        StackPanel ObWelcome()
        {
            var p = new StackPanel();
            // a small energy curve that draws itself
            var curve = new Path
            {
                Data = Geometry.Parse("M0,46 C18,46 26,6 52,6 C78,6 84,40 104,40 C124,40 132,14 154,14 C176,14 182,48 200,52"),
                Stroke = T.Strong, StrokeThickness = 3.5, StrokeLineCap = PenLineCap.Round,
                StrokeDashArray = new AvaloniaList<double> { 80, 80 }, StrokeDashOffset = 80, Width = 200, Height = 56, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 6, 0, 22)
            };
            Anim(curve, Shape.StrokeDashOffsetProperty, 0, 1400, delay: 350, ease: Soft);
            var dot = new Ellipse { Width = 12, Height = 12, Fill = T.Accent, Stroke = T.Strong, StrokeThickness = 2.5, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(46, 0, 0, 0), Opacity = 0 };
            Anim(dot, OpacityProperty, 1, 300, delay: 900);
            var art = new Grid(); art.Children.Add(curve); art.Children.Add(dot);
            p.Children.Add(art);
            p.Children.Add(ObTitle("Hi, I'm Corner."));
            p.Children.Add(ObBody("I plan your day around your energy: the hard tasks on your peaks, the easy ones in your afternoon dip, and a reminder before each."));
            p.Children.Add(ObLabel("What should I call you?", 26));
            p.Children.Add(ObInput("Your name", obName, out obFocus));
            return p;
        }

        StackPanel ObSleep()
        {
            var p = new StackPanel();
            p.Children.Add(ObTitle(obName.Length > 0 ? "How much sleep do you need, " + obName + "?" : "How much sleep do you need?"));
            p.Children.Add(ObBody("Most adults need 7 to 9 hours a night. Corner uses this to work out your sleep debt and the shape of each day's energy."));
            var big = new TextBlock { FontSize = Fk(56), FontWeight = FontWeight.SemiBold, Foreground = T.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MinWidth = 210, TextAlignment = TextAlignment.Center, RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative) };
            var pop = new ScaleTransform(1, 1); big.RenderTransform = pop;
            Action show = () => big.Text = DurShort(obNeed);
            show();
            Action<double> step = dv =>
            {
                double v = Math.Max(5, Math.Min(11, obNeed + dv));
                if (v == obNeed) return;
                obNeed = v; show();
                Anim(pop, ScaleTransform.ScaleXProperty, 1, 380, from: 1.1, ease: Pop); Anim(pop, ScaleTransform.ScaleYProperty, 1, 380, from: 1.1, ease: Pop);
            };
            var row = new Grid { Margin = new Thickness(0, 34, 0, 10), HorizontalAlignment = HorizontalAlignment.Center };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var minus = ObRound("M1,5 H9", () => step(-0.25)); row.Children.Add(minus);
            Grid.SetColumn(big, 1); row.Children.Add(big);
            var plus = ObRound("M1,5 H9 M5,1 V9", () => step(0.25)); Grid.SetColumn(plus, 2); row.Children.Add(plus);
            p.Children.Add(row);
            p.Children.Add(new TextBlock { Text = "Not sure? Keep 8 hours. Once Corner knows you, it suggests a figure from how fresh you feel.", FontSize = Fk(12.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(20, 12, 20, 0) });
            return p;
        }

        StackPanel obList; TextBlock obEmpty; TextBox obMinutes; StackPanel obEffortRow;

        StackPanel ObRoutines()
        {
            var p = new StackPanel();
            p.Children.Add(ObTitle("What do you do most days?"));
            p.Children.Add(ObBody("Add your daily routines, how much effort each takes and how long. Each morning, send them to your tasks and Plan my day fits them to your energy."));

            // the entry: a name, then effort and length, then Add
            var entry = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
            TextBox name;
            entry.Children.Add(ObInput("e.g. Exercise, Reading, Emails", "", out name));
            obFocus = name;
            var opts = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            opts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); opts.ColumnDefinitions.Add(new ColumnDefinition()); opts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            obEffortRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ObPaintEffort();
            opts.Children.Add(obEffortRow);
            var len = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var mbox = MiniBox("30", 26, out obMinutes); mbox.Margin = new Thickness(0); mbox.Background = T.Field; obMinutes.FontSize = Fk(13);
            len.Children.Add(mbox);
            len.Children.Add(new TextBlock { Text = "min", FontSize = Fk(12.5), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 1) });
            Grid.SetColumn(len, 1); opts.Children.Add(len);
            Action add = () =>
            {
                string t = name.Text.Trim(); if (t.Length == 0) { name.Focus(); return; }
                int m; if (!int.TryParse(obMinutes.Text, out m) || m <= 0) m = Planner.DefaultMinutes;
                ObAddRoutine(t, obEffort, Math.Min(m, 600));
                name.Text = ""; name.Focus();
            };
            var addBtn = ObButton("Add", false, add); Grid.SetColumn(addBtn, 2); opts.Children.Add(addBtn);
            name.KeyDown += (s, e) => { if (e.Key == Key.Enter && name.Text.Trim().Length > 0) { e.Handled = true; add(); } };
            entry.Children.Add(opts);
            p.Children.Add(entry);

            // quick picks
            var picks = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            picks.Children.Add(new TextBlock { Text = "Try", FontSize = Fk(12), Foreground = T.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 6) });
            foreach (var s in new[] { new { T = "Exercise", E = "high", M = 30 }, new { T = "Reading", E = "medium", M = 30 }, new { T = "Emails", E = "low", M = 20 }, new { T = "Deep work", E = "high", M = 90 } })
            {
                var x = s;
                var chip = new PillBorder { Background = T.Lift, Padding = new Thickness(12, 5, 12, 6), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                    Child = new TextBlock { Text = "+ " + x.T, FontSize = Fk(12), Foreground = T.Text } };
                chip.PointerEntered += (o, e) => chip.Background = T.Field; chip.PointerExited += (o, e) => chip.Background = T.Lift;
                chip.OnLeftDown((o, e) => { e.Handled = true; if (!obRoutines.Any(r => r.Title == x.T)) ObAddRoutine(x.T, x.E, x.M); });
                picks.Children.Add(chip);
            }
            p.Children.Add(picks);

            // what has been added
            obList = new StackPanel();
            obEmpty = new TextBlock { Text = "Nothing yet. You can also add routines later.", FontSize = Fk(12.5), Foreground = T.Faint, Margin = new Thickness(2, 8, 0, 0) };
            var listBox = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            listBox.Children.Add(obEmpty);
            listBox.Children.Add(new ScrollViewer { Content = obList, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            foreach (var r in obRoutines) obList.Children.Add(ObRoutineRow(r, false));
            obEmpty.IsVisible = obRoutines.Count == 0 ? true : false;
            p.Children.Add(listBox);
            return p;
        }

        StackPanel ObDone()
        {
            var p = new StackPanel();
            // a tick that pops in and draws itself
            var badge = new Grid { Width = 78, Height = 78, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 24), RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative) };
            var pop = new ScaleTransform(0.4, 0.4); badge.RenderTransform = pop;
            badge.Children.Add(new Ellipse { Fill = T.Accent });
            var tick = new Path { Data = Geometry.Parse("M2,13 L11,22 L28,3"), Stroke = T.OnAccent, StrokeThickness = 4.5, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
                Width = 30, Height = 25, StrokeDashArray = new AvaloniaList<double> { 12, 12 }, StrokeDashOffset = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badge.Children.Add(tick);
            Anim(pop, ScaleTransform.ScaleXProperty, 1, 620, delay: 250, ease: Pop); Anim(pop, ScaleTransform.ScaleYProperty, 1, 620, delay: 250, ease: Pop);
            Anim(tick, Shape.StrokeDashOffsetProperty, 0, 520, delay: 560, ease: Out);
            p.Children.Add(badge);
            p.Children.Add(ObTitle(obName.Length > 0 ? "You're all set, " + obName + "." : "You're all set."));
            string[] where = { "top-right", "top-left", "bottom-right", "bottom-left" };
            p.Children.Add(ObBody("Corner lives in the " + where[Math.Max(0, Math.Min(3, corner))] + " corner of your screen. Move your pointer there any time to see your tasks."));
            p.Children.Add(ObBody("Next, tell it how you slept last night, and it will draw today's energy curve."));
            var summary = new List<string> { DurShort(obNeed) + " of sleep" };
            summary.Add(obRoutines.Count == 0 ? "no routines yet" : obRoutines.Count == 1 ? "1 routine" : obRoutines.Count + " routines");
            p.Children.Add(new PillBorder { Background = T.Soft, Padding = new Thickness(14, 6, 14, 7), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 0),
                Child = new TextBlock { Text = string.Join("  ·  ", summary), FontSize = Fk(12.5), FontWeight = FontWeight.SemiBold, Foreground = T.Text } });
            return p;
        }

        // ------------------------------------------------------------ routines on the third step

        void ObAddRoutine(string title, string effort, int minutes)
        {
            var r = new Routine { Id = "r" + Guid.NewGuid().ToString("N").Substring(0, 8), Title = title, Energy = effort, Minutes = minutes, Days = "daily" };
            obRoutines.Add(r);
            obEmpty.IsVisible = false;
            obList.Children.Add(ObRoutineRow(r, true));
            obNextText.Text = "Continue";
        }

        Control ObRoutineRow(Routine r, bool animate)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            var box = new RimBorder { CornerRadius = new CornerRadius(14), Background = T.Field, Padding = new Thickness(14, 9, 10, 10), Child = g };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bars = Bars(Math.Max(1, Array.IndexOf(new[] { null, "low", "medium", "high" }, r.Energy))); bars.Margin = new Thickness(0, 0, 12, 0);
            g.Children.Add(bars);
            var t = new TextBlock { Text = r.Title, FontSize = Fk(13.5), Foreground = T.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, 1); g.Children.Add(t);
            var meta = new TextBlock { Text = TaskTags.Short(r.Energy) + " · " + TaskTags.Len(r.Minutes), FontSize = Fk(12), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            Grid.SetColumn(meta, 2); g.Children.Add(meta);
            var del = new Grid { Width = 22, Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand, [ToolTip.TipProperty] = "Remove" };
            var x = Glyph("M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5", T.Faint, 1.3, 10, 10); del.Children.Add(x);
            del.PointerEntered += (s, e) => x.Stroke = T.Danger; del.PointerExited += (s, e) => x.Stroke = T.Faint;
            del.OnLeftDown((s, e) =>
            {
                e.Handled = true; obRoutines.Remove(r);
                Anim(box, OpacityProperty, 0, 160, done: () =>
                {
                    obList.Children.Remove(box);
                    if (obRoutines.Count == 0) { obEmpty.IsVisible = true; if (obStep == 2) obNextText.Text = "Skip for now"; }
                });
            });
            Grid.SetColumn(del, 3); g.Children.Add(del);
            if (animate)
            {
                var slide = new TranslateTransform(0, -8); box.RenderTransform = slide; box.Opacity = 0;
                Anim(box, OpacityProperty, 1, 260); Anim(slide, TranslateTransform.YProperty, 0, 380, ease: Out);
            }
            return box;
        }

        void ObPaintEffort()
        {
            obEffortRow.Children.Clear();
            string[] levels = { "low", "medium", "high" }; string[] names = { "Low", "Medium", "High" };
            for (int i = 0; i < 3; i++)
            {
                string lv = levels[i]; bool on = obEffort == lv;
                var c = new StackPanel { Orientation = Orientation.Horizontal };
                var b = Bars(i + 1); b.Margin = new Thickness(0, 0, 6, 0); c.Children.Add(b);
                c.Children.Add(new TextBlock { Text = names[i], FontSize = Fk(12), Foreground = on ? T.OnStrong : T.Text, VerticalAlignment = VerticalAlignment.Center });
                if (on) foreach (var r in ((Canvas)b).Children.OfType<Rectangle>()) r.Fill = T.OnStrong;
                var chip = new PillBorder { Background = on ? (IBrush)T.Strong : T.Field, Padding = new Thickness(11, 6, 12, 7), Margin = new Thickness(0, 0, 5, 0), Cursor = Cursors.Hand, Child = c, [ToolTip.TipProperty] = names[i] + " effort" };
                chip.OnLeftDown((s, e) => { e.Handled = true; obEffort = lv; ObPaintEffort(); });
                obEffortRow.Children.Add(chip);
            }
        }

        // ------------------------------------------------------------ finishing

        /// Keeps what was entered (also when the window is closed early) and opens the corner panel.
        void ObFinish()
        {
            if (onboard == null) return;
            if (obStep == 0 && obFocus != null) obName = obFocus.Text.Trim();
            var win = onboard; onboard = null;
            try
            {
                if (obName.Length > 0) Profile.Name = obName;
                Settings.Set("SleepNeed", obNeed);
                Routines.Load();
                foreach (var r in obRoutines) Routines.Data.Routines.Add(r);
                Routines.Save();
                Settings.Set("Onboarded", 1);
            }
            catch (Exception ex) { Store.Log(ex); }
            var root = (Control)win.Content;
            Anim(root, OpacityProperty, 0, 220, done: () =>
            {
                win.Close();
                if (!demoRun) ShowPanel(15000);
            });
        }

        // ------------------------------------------------------------ pieces

        TextBlock ObTitle(string text)
        {
            return new TextBlock { Text = text, FontSize = Fk(29), FontWeight = FontWeight.SemiBold, Foreground = T.Text, TextWrapping = TextWrapping.Wrap, LineHeight = Fk(29) * 1.15, Margin = new Thickness(0, 0, 0, 12) };
        }

        TextBlock ObBody(string text)
        {
            return new TextBlock { Text = text, FontSize = Fk(14.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, LineHeight = Fk(14.5) * 1.45, Margin = new Thickness(0, 0, 0, 6) };
        }

        TextBlock ObLabel(string text, double top)
        {
            return new TextBlock { Text = text, FontSize = Fk(13.5), FontWeight = FontWeight.SemiBold, Foreground = T.Text, Margin = new Thickness(2, top, 0, 10) };
        }

        Control ObInput(string hint, string text, out TextBox box)
        {
            var tb = Box(16); tb.FontSize = Fk(16); tb.Text = text; tb.MaxLength = 60;
            var h = new TextBlock { Text = hint, FontSize = Fk(16), Foreground = T.Faint, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 1) };
            h.IsVisible = text.Length > 0 ? false : true;
            tb.TextChanged += (s, e) => h.IsVisible = tb.Text.Length > 0 ? false : true;
            var g = new Grid(); g.Children.Add(h); g.Children.Add(tb);
            var pill = new PillBorder { Background = T.Field, Padding = new Thickness(18, 11, 18, 12), Child = g, Cursor = Cursors.IBeam, BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent };
            tb.GotFocus += (s, e) => pill.BorderBrush = T.Strong;
            tb.LostFocus += (s, e) => pill.BorderBrush = Brushes.Transparent;
            pill.OnLeftDown((s, e) => { e.Handled = true; tb.Focus(); });
            box = tb;
            return pill;
        }

        Control ObButton(string text, bool main, Action click)
        {
            var label = new TextBlock { Text = text, FontSize = Fk(14), FontWeight = main ? FontWeight.SemiBold : FontWeight.Normal, Foreground = main ? T.OnAccent : T.Text };
            IBrush rest = main ? (IBrush)T.Accent : T.Lift;
            var b = new PillBorder { Background = rest, Padding = new Thickness(main ? 26 : 18, 11, main ? 26 : 18, 12), Child = label, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative) };
            var press = new ScaleTransform(1, 1); b.RenderTransform = press;
            b.PointerEntered += (s, e) => { if (main) b.Opacity = 0.9; else b.Background = T.Field; };
            b.PointerExited += (s, e) => { b.Opacity = 1; b.Background = rest; Anim(press, ScaleTransform.ScaleXProperty, 1, 120); Anim(press, ScaleTransform.ScaleYProperty, 1, 120); };
            b.OnLeftDown((s, e) => { e.Handled = true; Anim(press, ScaleTransform.ScaleXProperty, 0.95, 90); Anim(press, ScaleTransform.ScaleYProperty, 0.95, 90); });
            b.OnLeftUp((s, e) =>
            {
                e.Handled = true;
                Anim(press, ScaleTransform.ScaleXProperty, 1, 260, ease: Pop); Anim(press, ScaleTransform.ScaleYProperty, 1, 260, ease: Pop);
                click();
            });
            return b;
        }

        Control ObRound(string glyph, Action click)
        {
            var g = new Grid { Width = 48, Height = 48, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            var disc = new Ellipse { Fill = T.Field };
            g.Children.Add(disc);
            g.Children.Add(Glyph(glyph, T.Text, 1.8, 14, 14));
            g.PointerEntered += (s, e) => disc.Fill = T.Lift; g.PointerExited += (s, e) => disc.Fill = T.Field;
            g.OnLeftDown((s, e) => { e.Handled = true; click(); });
            return g;
        }

        // ------------------------------------------------------------ test renders (mode "onboard0" to "onboard3")

        void DemoOnboard(string path, int step)
        {
            demoRun = true;
            obName = "Sam";
            if (step >= 2) { obRoutines.Add(new Routine { Id = "d1", Title = "Exercise", Energy = "high", Minutes = 30, Days = "daily" }); obRoutines.Add(new Routine { Id = "d2", Title = "Reading", Energy = "medium", Minutes = 30, Days = "daily" }); obRoutines.Add(new Routine { Id = "d3", Title = "Emails", Energy = "low", Minutes = 20, Days = "daily" }); }
            if (step >= 1) obNeed = 7.5;
            ShowOnboarding(step);
            After(2600, () =>
            {
                var root = (Control)onboard.Content;
                var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(185, 177, 151)) };
                onboard.Content = null; host.Children.Add(root);
                host.Measure(new Size(onboard.Width, onboard.Height)); host.Arrange(new Rect(0, 0, onboard.Width, onboard.Height)); host.UpdateLayout();
                SavePng(host, path, onboard.Height);
                onboard.Close(); HideTray();
                Wpf.Quit();
            });
        }
    }
}
