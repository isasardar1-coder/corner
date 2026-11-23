// Corner - added 2026-11-19: the chart's cursor and scrolling, the sleep debt calculator,
// the sleep history shown in Settings, and the two energy reminders.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WF = System.Windows.Forms;

namespace Corner
{
    partial class Shell
    {
        // ------------------------------------------------------------ the chart: cursor and scrolling

        FrameworkElement chartThumb; // the scroll bar's handle (the test hook drags it)
        ScrollViewer chartScroll;    // the corner panel's chart, so a picture of the panel shows it where it is scrolled (Launch.cs)
        double chartOffset = -1; // how far the corner panel's chart is scrolled; -1 = centre on now
        readonly List<Action<double>> chartPins = new List<Action<double>>(); // zone names that follow the scrolling (DrawEnergy)

        /// Adds the chart to the view. Moving the mouse over it shows a cursor with the time and energy there.
        /// When the chart is wider than its space (the corner panel) it scrolls: drag it, or use the mouse wheel.
        void AddChart(Canvas c, double view, double W, double H, Func<double, double> Y, DateTime now)
        {
            var e = energy;
            var line = new Line { Y1 = 12, Y2 = H, Stroke = T.Text, StrokeThickness = 1, Opacity = 0.6, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            var dot = new Ellipse { Width = 7, Height = 7, Fill = T.Text, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            var text = new TextBlock { FontSize = Z(9.5), FontWeight = SemiSmall, Foreground = T.Text };
            var tip = new Border { CornerRadius = new CornerRadius(7), Background = T.Field, Padding = new Thickness(6, 1, 6, 2), Child = text, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            c.Children.Add(line); c.Children.Add(dot); c.Children.Add(tip);

            ScrollViewer sv = null;
            bool drag = false; double dragX = 0, dragOff = 0;
            Action<double> scrollTo = null; // set below when the chart scrolls

            Action<double> place = x =>
            {
                x = Math.Max(0, Math.Min(W, x));
                var t = e.Wake.AddMinutes(x / W * (e.NextWake - e.Wake).TotalMinutes);
                double v = e.AllAt(t);
                string zone = t >= e.Bed ? "sleep" : "";
                foreach (var z in new[] { e.Peak1, e.Dip, e.Peak2 })
                    if (z != null && t >= z.From && t < z.To) zone = z.Peak ? "peak" : "dip";
                bool asleep = t >= e.Bed; // no energy figure for the hours you are expected to be asleep
                text.Text = EnergyDay.Clock(t) + (zone.Length > 0 ? "  ·  " + zone : "");
                dot.Opacity = asleep ? 0 : 1;
                tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double tw = tip.DesiredSize.Width;
                double y = Math.Max(4, Math.Min(H - 4, Y(v)));
                line.X1 = line.X2 = x;
                Canvas.SetLeft(dot, x - 3.5); Canvas.SetTop(dot, y - 3.5);
                Canvas.SetLeft(tip, Math.Max(0, Math.Min(W - tw, x - tw / 2))); Canvas.SetTop(tip, -3);
                line.Visibility = dot.Visibility = tip.Visibility = Visibility.Visible;
            };
            c.MouseMove += (s, a) =>
            {
                if (drag && scrollTo != null) scrollTo(dragOff - (a.GetPosition(sv).X - dragX));
                place(a.GetPosition(c).X);
            };
            c.MouseLeave += (s, a) => { if (!drag) line.Visibility = dot.Visibility = tip.Visibility = Visibility.Collapsed; };

            if (W <= view + 0.5) { sect.Children.Add(c); return; } // Full Mode: the whole day fits

            c.Margin = new Thickness(0);
            sv = new ScrollViewer
            {
                Width = view, HorizontalAlignment = HorizontalAlignment.Left, Focusable = false, Content = c,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            c.Cursor = Cursors.ScrollWE;
            // The scroll bar: a rail with a handle to drag. Clicking the rail beside the handle jumps there.
            double max = W - view, thumbW = Math.Max(44, view * view / W), run = view - thumbW;
            // It is drawn thin; the part that takes the mouse is taller, so it is easy to grab.
            var bar = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = T.Muted, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var thumb = new Grid { Height = 12, Width = thumbW, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeWE, ToolTip = "Drag to move through the day" };
            thumb.Children.Add(bar);
            var rail = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = T.Ctl, VerticalAlignment = VerticalAlignment.Center };
            var track = new Grid { Height = 12, Width = view, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, -2) }; // the tray's own padding follows
            track.Children.Add(rail); track.Children.Add(thumb);

            if (chartOffset < 0) chartOffset = (now - e.Wake).TotalMinutes / (e.NextWake - e.Wake).TotalMinutes * W - view / 2;
            chartOffset = Math.Max(0, Math.Min(max, chartOffset));
            double start = chartOffset;
            Action<double> go = off =>
            {
                chartOffset = Math.Max(0, Math.Min(max, off));
                sv.ScrollToHorizontalOffset(chartOffset);
                thumb.Margin = new Thickness(chartOffset / max * run, 0, 0, 0);
                foreach (var p in chartPins) p(chartOffset);
            };
            scrollTo = go; chartThumb = thumb; chartScroll = sv;
            thumb.Margin = new Thickness(start / max * run, 0, 0, 0);
            foreach (var p in chartPins) p(start);
            sv.Loaded += (s, a) => sv.ScrollToHorizontalOffset(start);
            bool thumbDrag = false; double thumbX = 0, thumbOff = 0;
            thumb.MouseEnter += (s, a) => bar.Background = T.Text;
            thumb.MouseLeave += (s, a) => { if (!thumbDrag) bar.Background = T.Muted; };
            thumb.MouseLeftButtonDown += (s, a) => { a.Handled = true; thumbDrag = true; thumbX = a.GetPosition(track).X; thumbOff = chartOffset; thumb.CaptureMouse(); };
            thumb.MouseMove += (s, a) => { if (thumbDrag) go(thumbOff + (a.GetPosition(track).X - thumbX) / run * max); };
            thumb.MouseLeftButtonUp += (s, a) => { if (thumbDrag) { thumbDrag = false; thumb.ReleaseMouseCapture(); bar.Background = T.Muted; } };
            thumb.LostMouseCapture += (s, a) => thumbDrag = false;
            track.MouseLeftButtonDown += (s, a) => { a.Handled = true; go((a.GetPosition(track).X - thumbW / 2) / run * max); };
            sv.PreviewMouseWheel += (s, a) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return; // Ctrl + scroll is the text size
                go(chartOffset - a.Delta * 0.5);
                a.Handled = true;
            };
            track.MouseWheel += (s, a) => { go(chartOffset - a.Delta * 0.5); a.Handled = true; };
            c.MouseLeftButtonDown += (s, a) => { a.Handled = true; drag = true; dragX = a.GetPosition(sv).X; dragOff = chartOffset; c.CaptureMouse(); };
            c.MouseLeftButtonUp += (s, a) => { if (drag) { drag = false; c.ReleaseMouseCapture(); } };
            c.LostMouseCapture += (s, a) => drag = false;

            sect.Children.Add(sv);
            sect.Children.Add(track);
        }

        // ------------------------------------------------------------ sleep debt calculator

        // the "Add a routine" form: what is typed and picked survives a redraw
        string newRoutineText, newRoutineEnergy;
        int newRoutineMin = 30;

        bool debtOpen;        // opened by clicking the Debt figure
        readonly bool[] statOpen = new bool[3]; // Full Mode: which of Slept, Debt and Need are showing their box (UiEnergy.cs)
        int debtExtra = 30;   // minutes of extra sleep a night, for the pay-back sum

        static string Mins(double minutes)
        {
            int m = (int)Math.Round(minutes);
            return m < 60 ? m + " min" : m / 60 + "h" + (m % 60 > 0 ? " " + m % 60 + "m" : "");
        }

        void DrawDebt(DateTime now)
        {
            var day = SleepLog.Today(now); double need = energy.Need; int span = EnergyModel.DebtNights;
            var nights = SleepLog.Data.Nights.Where(x => x != null && x.Valid && x.WokeAt.Date <= day && x.WokeAt.Date > day.AddDays(-span)).ToList();

            var sp = new StackPanel();
            var head = Caps("Sleep debt calculator"); head.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(head);

            // one bar per day for the last 14 days; the line across is your sleep need
            const double BH = 46; double top = need + 2;
            var bars = new UniformGrid { Columns = span, Height = BH };
            int shortNights = 0;
            for (int i = span - 1; i >= 0; i--)
            {
                var d = day.AddDays(-i);
                var n = nights.LastOrDefault(x => x.WokeAt.Date == d);
                if (n == null) { bars.Children.Add(new Border { Height = 2, Width = 2, CornerRadius = new CornerRadius(1), Background = T.Ctl, VerticalAlignment = VerticalAlignment.Bottom }); continue; }
                bool isShort = n.TotalHours < need - 0.01; if (isShort) shortNights++;
                bars.Children.Add(new Border
                {
                    Height = Math.Max(3, Math.Min(1, n.TotalHours / top) * BH), Margin = new Thickness(2, 0, 2, 0), CornerRadius = new CornerRadius(2),
                    Background = isShort ? (Brush)T.Strong : T.Ctl, Opacity = n.Assumed ? 0.5 : 1, VerticalAlignment = VerticalAlignment.Bottom, // short nights are dark
                    ToolTip = d.ToString("ddd d MMM", CultureInfo.CurrentCulture) + ": slept " + DurShort(n.TotalHours) + (isShort ? ", " + Mins((need - n.TotalHours) * 60) + " short" : "") + (n.Assumed ? " (assumed)" : "")
                });
            }
            var chart = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            chart.Children.Add(bars);
            chart.Children.Add(new Border { Height = 1, Background = T.Text, Opacity = 0.35, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, need / top * BH), IsHitTestVisible = false });
            sp.Children.Add(chart);
            sp.Children.Add(new TextBlock { Text = "Last " + span + " days · the line is your " + DurShort(need) + " need · " + nights.Count + " logged, " + shortNights + " short", FontSize = Z(10.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

            double debt = energy.DebtHours;
            if (debt < 0.25)
            {
                sp.Children.Add(new TextBlock { Text = "No sleep debt. Nothing to pay back.", FontSize = Z(12), Foreground = T.Text, TextWrapping = TextWrapping.Wrap });
            }
            else
            {
                // when some of the 14 nights were never logged, say how much of the figure is measured and how much is estimated
                int unlogged = span - energy.DebtLogged; double est = energy.DebtEstimate;
                string total = est >= 0.01 && debt - est >= 0
                    ? "You are about " + Mins(debt * 60) + " short in total: " + Mins((debt - est) * 60) + " over the " + energy.DebtLogged + " nights logged, and about " + Mins(est * 60) + " for the " + unlogged + (unlogged == 1 ? " night" : " nights") + " not logged, counted as an average night."
                    : "You are " + Mins(debt * 60) + " short in total.";
                sp.Children.Add(new TextBlock { Text = total + " Longer nights pay it back; shorter ones add to it.", FontSize = Z(12), Foreground = T.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });

                int nightsToClear = (int)Math.Ceiling(debt * 60 / debtExtra);
                var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
                Func<string, TextBlock> word = text => { var l = Label(text); l.Margin = new Thickness(0, 0, 0, 1); return l; }; // Label's own margin is for rows of boxes
                row.Children.Add(word("Sleep"));
                var minus = Step("–", () => { debtExtra = Math.Max(15, debtExtra - 15); DrawEnergy(); });
                var plus = Step("+", () => { debtExtra = Math.Min(120, debtExtra + 15); DrawEnergy(); });
                minus.Margin = new Thickness(8, 0, 0, 0); plus.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(minus);
                row.Children.Add(new TextBlock { Text = debtExtra + " min", Width = 52, TextAlignment = TextAlignment.Center, FontSize = Z(11.5), FontWeight = SemiSmall, Foreground = T.Text, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(plus);
                row.Children.Add(word("more a night"));
                row.Margin = new Thickness(0, 0, 0, 6);
                sp.Children.Add(row);
                // Paying back means sleeping your need plus the extra, so the bedtime is counted back from the wake-up
                // (with the time you are usually awake in the night added), not from last night's bedtime.
                double awake = Math.Max(0, (energy.Last.WokeAt - energy.Last.AsleepAt).TotalHours - energy.Last.Hours);
                var asleepBy = energy.Wake.AddHours(-(need + awake)).AddMinutes(-debtExtra);
                sp.Children.Add(new TextBlock { Text = "Cleared in " + nightsToClear + (nightsToClear == 1 ? " night" : " nights") + " of " + DurShort(need + debtExtra / 60.0) + " sleep: asleep by about " + EnergyDay.Clock(asleepBy) + " to wake at " + EnergyDay.Clock(energy.Wake) + ".", FontSize = Z(11.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

                // using the dip to recharge
                string tipText;
                if (energy.Dip == null) tipText = "Recharge: a nap of about 20 minutes in the early afternoon pays back 20 minutes and lifts the hours after it.";
                else if (now < energy.Dip.To) tipText = "Recharge in the dip: a nap of about 20 minutes between " + EnergyDay.Clock(energy.Dip.From) + " and " + EnergyDay.Clock(energy.Dip.To) + " pays back 20 minutes and lifts the hours after it. Keep it under 30 minutes and finished by " + EnergyDay.Clock(now.Date.AddHours(EnergyModel.LateNapHour)) + ", or tonight's sleep moves later. Use \"Log a nap\" afterwards so it counts.";
                else tipText = "Today's dip has passed. A late nap would push tonight's sleep later, so an earlier bedtime is the better way to pay back now.";
                var tipBox = new Border { CornerRadius = new CornerRadius(12), Background = T.Raised, Padding = new Thickness(11, 8, 11, 9), Child = new TextBlock { Text = tipText, FontSize = Z(11.5), Foreground = T.Text, TextWrapping = TextWrapping.Wrap } };
                sp.Children.Add(tipBox);
            }
            sect.Children.Add(new RimBorder { CornerRadius = new CornerRadius(Radius), Background = T.Field, Padding = new Thickness(PadX, PadY, PadX, PadY), Margin = new Thickness(0, 0, 0, GapY), Child = sp });
        }

        // ------------------------------------------------------------ sleep history (Settings page)

        /// Average clock time of falling asleep: times after midnight count as "later than" the evening before.
        static double MeanBedHour(IEnumerable<Night> nights)
        {
            double m = nights.Average(n => { double h = n.AsleepAt.TimeOfDay.TotalHours; return h < 12 ? h + 24 : h; });
            return m >= 24 ? m - 24 : m;
        }

        void DrawSleepHistory()
        {
            // a heading in the column of the Settings page that holds energy and sleep, then a white card (Ui.cs)
            setCol.Children.Add(SetCaps("Sleep history"));
            var view = SetCard(setCol);
            view.Margin = new Thickness(0, PadY - 4, 0, PadY - 2);

            SleepLog.Load();
            var day = SleepLog.Today(Now());
            double need = Settings.Get("SleepNeed", EnergyModel.DefaultNeed);
            var logged = SleepLog.Data.Nights.Where(n => n != null && n.Valid && !n.Assumed && n.WokeAt.Date <= day).OrderBy(n => n.WokeAt).ToList();
            if (logged.Count == 0)
            {
                view.Children.Add(new TextBlock { Text = "Enter your sleep in the morning check-in and the weekly and monthly averages appear here. Mornings you skip are left out.", FontSize = Z(11.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap });
                return;
            }

            var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Sp(1.25, 0.95), GridUnitType.Star) });
            for (int i = 0; i < 4; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 3; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Action<int, int, string, bool, Brush> cell = (r, c, text, bold, brush) =>
            {
                var tb = new TextBlock { Text = text, FontSize = Z(r == 0 ? 10 : c == 0 ? 11 : Sp(12, 11)), FontWeight = bold ? SemiSmall : FontWeights.Normal, Foreground = brush, Margin = new Thickness(0, 3, 4, 3), TextWrapping = c == 0 ? TextWrapping.Wrap : TextWrapping.NoWrap };
                Grid.SetRow(tb, r); Grid.SetColumn(tb, c); g.Children.Add(tb);
            };
            string[] heads = { "", "Asleep", "Woke", "Slept", "Debt" };
            for (int i = 1; i < heads.Length; i++) cell(0, i, heads[i].ToUpperInvariant(), true, T.Muted);
            int row = 1;
            foreach (int days in new[] { 7, 30 })
            {
                var w = logged.Where(n => n.WokeAt.Date > day.AddDays(-days)).ToList();
                cell(row, 0, (days == 7 ? "Week" : "Month") + "\n" + w.Count + (w.Count == 1 ? " night" : " nights"), false, T.Muted);
                if (w.Count == 0) { cell(row, 1, "–", false, T.Faint); row++; continue; }
                double debt = Math.Max(0, w.Sum(n => need - n.TotalHours));
                cell(row, 1, EnergyDay.Clock(DateTime.Today.AddHours(MeanBedHour(w))), true, T.Text);
                cell(row, 2, EnergyDay.Clock(DateTime.Today.AddHours(w.Average(n => n.WokeAt.TimeOfDay.TotalHours))), true, T.Text);
                cell(row, 3, DurShort(w.Average(n => n.TotalHours)), true, T.Text);
                cell(row, 4, debt < 0.25 ? "none" : DurShort(debt), true, T.Text);
                row++;
            }
            view.Children.Add(g);

            // the pattern over time: one bar per night, from falling asleep (top) to waking (bottom)
            const int Days = 30; const double PH = 78, From = 18, Hours = 20, LabelW = 26; // the strip runs from 18:00 to 14:00
            double PW = setW - LabelW, col = PW / Days;
            var p = new Canvas { Width = PW + LabelW, Height = PH + 16, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (int hour in new[] { 0, 6, 12 })
            {
                double y = (hour + 24 - From) % 24 / Hours * PH;
                p.Children.Add(new Line { X1 = LabelW, X2 = LabelW + PW, Y1 = y, Y2 = y, Stroke = T.Line, StrokeThickness = 1 });
                var lb = new TextBlock { Text = EnergyDay.HourLabel(DateTime.Today.AddHours(hour)), FontSize = Z(9), Foreground = T.Faint };
                Canvas.SetLeft(lb, 0); Canvas.SetTop(lb, y - 6); p.Children.Add(lb);
            }
            foreach (var n in SleepLog.Data.Nights.Where(x => x != null && x.Valid && x.WokeAt.Date <= day && x.WokeAt.Date > day.AddDays(-Days)))
            {
                int back = (day - n.WokeAt.Date).Days;
                var origin = n.WokeAt.Date.AddDays(-1).AddHours(From);
                foreach (var part in n.Sleep)
                {
                    double y0 = Math.Max(0, Math.Min(PH, (part.From - origin).TotalHours / Hours * PH)), y1 = Math.Max(0, Math.Min(PH, (part.To - origin).TotalHours / Hours * PH));
                    if (y1 - y0 < 1) continue;
                    var bar = new Rectangle { Width = Math.Max(2, col - 3), Height = y1 - y0, RadiusX = 2, RadiusY = 2, Fill = n.TotalHours < need - 0.01 ? (Brush)T.Strong : T.Ctl, Opacity = n.Assumed ? 0.35 : 1 };
                    bar.ToolTip = n.WokeAt.ToString("ddd d MMM", CultureInfo.CurrentCulture) + ": " + EnergyDay.Clock(n.AsleepAt) + "–" + EnergyDay.Clock(n.WokeAt) + ", " + DurShort(n.TotalHours) + (n.Assumed ? " (assumed)" : "");
                    Canvas.SetLeft(bar, LabelW + (Days - 1 - back) * col + 1.5); Canvas.SetTop(bar, y0); p.Children.Add(bar);
                }
            }
            var l0 = new TextBlock { Text = Days + " days ago", FontSize = Z(9), Foreground = T.Faint }; Canvas.SetLeft(l0, LabelW); Canvas.SetTop(l0, PH + 3); p.Children.Add(l0);
            var l1 = new TextBlock { Text = "today", FontSize = Z(9), Foreground = T.Faint }; Canvas.SetRight(l1, 0); Canvas.SetTop(l1, PH + 3); p.Children.Add(l1);
            view.Children.Add(p);
            view.Children.Add(new TextBlock { Text = "Each bar is one night, from falling asleep (top) to waking (bottom). Dark nights were shorter than your " + DurShort(need) + " need; faint ones were assumed, not entered. Averages use entered nights only.", FontSize = Z(10.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        }

        // ------------------------------------------------------------ energy reminders

        public const double RiseLeadMin = 10; // minutes before each peak that the "do your hardest task" card shows
        public const double DipLeadMin = 20;  // minutes before the dip that the "step away" card shows

        static readonly Random nudgeRandom = new Random();
        static string RiseLine() { return RiseLines[nudgeRandom.Next(RiseLines.Length)]; }
        static string DipLine() { return DipLines[nudgeRandom.Next(DipLines.Length)]; }

        /// One random line on a card in Corner's own look, at its corner (Toast.cs); a tray balloon if that fails.
        void ShowNudge(string line)
        {
            try { ShowToast(line); }
            catch (Exception ex) { Store.Log(ex); try { tray.ShowBalloonTip(10000, "Corner", line, WF.ToolTipIcon.None); } catch { } }
        }

        static readonly string[] DipLines =
        {
            "Your biological battery is about to hit that sad little red bar—step away before you start making decisions you’ll have to apologize for.",
            "The afternoon lobotomy is coming soon; do yourself a favor and stop pretending to be useful before it arrives.",
            "Incoming: a scheduled collapse of your higher brain functions. Park the ambition and go stare at a wall.",
            "Your neurons are about to file a union grievance—give them a break before they go on strike permanently.",
            "The great daily cognitive recession is almost here; liquidate your focus and go do nothing with dignity.",
            "Brace yourself—the human equivalent of a \"404 Not Found\" error is rolling in. Step away from the keyboard.",
            "Your energy is about to fall off a cliff; don't still be holding a heavy thought when it happens.",
            "A mandatory software crash is approaching—save your work and go become a piece of furniture for twenty minutes.",
            "The neurochemical tide is about to go out, leaving nothing behind but wet sand and bad ideas. Take a break.",
            "Coming soon: the part of the day where your brain turns into lukewarm oatmeal. Stop trying to sculpt it.",
            "Your willpower is about to pack its bags and leave without a forwarding address—go grab a coffee and let it.",
            "The daily devolution from \"functioning adult\" to \"houseplant\" is inbound. Embrace the photosynthesis.",
            "A wave of pure, uncut mediocrity is about to hit your bloodstream—step off the stage before you embarrass yourself.",
            "Your prefrontal cortex is about to clock out for the afternoon; don't leave the lizard brain in charge of your emails.",
            "The cognitive fog is rolling in fast—pull over before you drive your productivity straight into a ditch.",
            "Incoming: your scheduled window of zero-calorie existence. Stop hustling and just breathe for a minute.",
            "Your brain is about to switch from \"high-speed fiber\" to \"dial-up modem\"—stop trying to download anything important.",
            "A brief, mercy-killing dip in your energy is coming soon; accept it like a grown-up and go take a walk.",
            "The biological curfew on competence is almost here—close the tabs before you start clicking things you shouldn't.",
            "Your mental engine is about to start knocking—kill the ignition before you blow a gasket over a spreadsheet.",
            "Coming soon to a nervous system near you: complete and total apathy. Might as well beat it to the punch and relax.",
            "The afternoon slump is creeping up behind you with a baseball bat—sit down voluntarily before it makes you.",
            "Your daily lease on alertness is about to expire; vacate the premises before you get evicted.",
            "A glorious, guilt-free void of ambition is approaching—lean into it and stop trying to optimize your pulse.",
            "The energy dip is on the horizon; drop the rope, walk away, and let the world spin without your supervision for a while.",
        };

        static readonly string[] RiseLines =
        {
            "A brief, miraculous surge of competence is coming soon—try not to blow it reorganizing your sock drawer.",
            "Your daily ration of functioning gray matter is about to drop; aim it at the ugly stuff before it spoils.",
            "The dopamine tide is coming in soon—use it to move a mountain, not to argue with a stranger on the internet.",
            "Incoming: a rare, temporary truce between your brain and your willpower. Don't waste it on fonts.",
            "Your cognitive high tide is rolling in; get the heavy lifting done before you wash back out to sea.",
            "Brace yourself—a suspiciously functional version of you is about to show up. Hand them the hardest job immediately.",
            "The biological lightning strike is coming soon; point it at the monster, not the mosquitos.",
            "You’re about to experience a brief outbreak of actual intelligence—quarantine it on your worst task.",
            "A window of non-idiocy is opening shortly; shove your biggest problem through it before it slams shut.",
            "Your nervous system is winding up for its one daily miracle—don't insult it with busywork.",
            "The mental caffeine is about to kick in, whether you drank any or not—go pick a fight with something difficult.",
            "Coming soon to a skull near you: actual focus. Use it on the stuff that makes you sweat, not the stuff that just makes you feel busy.",
            "Your brain’s daily lease on genius is about to start; don't sublet it to your inbox.",
            "A rare alignment of neurons is approaching—sacrifice your hardest task to it before the eclipse ends.",
            "The energy surge is on the horizon; prepare to do something that actually justifies your opposable thumbs.",
            "Your daily allotment of give-a-damn is arriving shortly—spend it all in one place, on the heaviest thing you own.",
            "Brace for impact: a wave of sheer, unadulterated competence is inbound. Put the hardest work in its path.",
            "The great biological clock is about to strike \"useful\"—be ready to wrestle something that fights back.",
            "A brief, terrifying burst of clarity is coming soon; use it to murder the task you’ve been politely ignoring.",
            "Your brain is about to stop buffering—don't waste the bandwidth on low-resolution chores.",
            "The daily surge of human potential is almost here; try to do something a golden retriever couldn't accidentally accomplish.",
            "Incoming: your scheduled window of peak cognitive violence. Point it at the problem that’s been mocking you all week.",
            "The neurochemical cavalry is riding over the hill—point them at the fortress, not the weeds.",
            "You’re about to hit your brief daily phase of evolutionary superiority; do the heavy thinking before you devolve back into a sofa cushion.",
            "A suspicious amount of energy is about to hit your bloodstream—channel it into something hard before it turns into anxiety.",
        };

        /// Called once a minute while you are at the PC. Each shows one random line on a card, at most once a day or per peak:
        /// 1. 20 minutes before the dip: a line from DipLines telling you to step away;
        /// 2. 10 minutes before each peak: a line from RiseLines telling you to do your hardest task.
        void EnergyNudges(DateTime now)
        {
            try
            {
                var day = SleepLog.Today(now);
                double key = day.Year * 10000 + day.Month * 100 + day.Day;
                SleepLog.Load();
                if (SleepLog.NeedsCheckIn(now)) return;
                var e = EnergyModel.Build(SleepLog.Data.Nights, Settings.Get("SleepNeed", EnergyModel.DefaultNeed), day, Tuning.Active(now));
                if (e == null) return;

                if (e.Dip != null && now >= e.Dip.From.AddMinutes(-DipLeadMin) && now < e.Dip.From && Settings.Get("DipNudge", 0) != key)
                {
                    Settings.Set("DipNudge", key);
                    ShowNudge(DipLine());
                }

                var rises = new[] { e.Peak1, e.Peak2 };
                for (int i = 0; i < rises.Length; i++)
                {
                    var z = rises[i]; double k = key * 10 + i + 1;
                    if (z == null || now < z.From.AddMinutes(-RiseLeadMin) || now >= z.From || Settings.Get("RiseNudge", 0) == k) continue;
                    Settings.Set("RiseNudge", k);
                    ShowNudge(RiseLine());
                }
            }
            catch (Exception ex) { Store.Log(ex); }
        }
    }
}
