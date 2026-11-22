// Corner - how the Energy side is drawn: the morning sleep check-in, the curve in its tray, the three sleep figures,
// naps, the plan (a white "ticket") and the routines list. The model itself is in Energy.cs and Plan.cs.
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

namespace Corner
{
    partial class Shell
    {
        Border tabLineTasks, tabLineEnergy;
        DockPanel energyChip; // the one-line summary beside the old tabs (the row is hidden; the chips replaced it)
        Ellipse energyDot;
        Panel sect;           // where the Energy view is being drawn: the view itself, or the box that is open

        /// What follows is drawn inside a box of the Energy view: a tray (T.Tray) or a white card (T.Field).
        void BeginCard(Brush surface)
        {
            var sp = new StackPanel();
            energyView.Children.Add(new RimBorder { CornerRadius = new CornerRadius(Radius), Background = surface, Padding = new Thickness(PadX, PadY, PadX, PadY), Margin = new Thickness(0, 0, 0, GapY), Child = sp });
            sect = sp;
        }

        void EndCard() { sect = energyView; }

        void BuildEnergyTop(DockPanel dock)
        {
            var row = new Border { Margin = new Thickness(20, 0, 20, 12), BorderBrush = T.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
            var rowGrid = new Grid(); row.Child = rowGrid;
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var tabs = new StackPanel { Orientation = Orientation.Horizontal };
            tabs.Children.Add(Tab("Tasks", 0, out tabTasks, out tabLineTasks));
            tabs.Children.Add(Tab("Energy", 1, out tabEnergy, out tabLineEnergy));
            rowGrid.Children.Add(tabs);

            energyDot = new Ellipse { Width = 6, Height = 6, Fill = T.Ctl, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) };
            energyLine = new TextBlock { FontSize = Z(11), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            energyChip = new DockPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 1, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            DockPanel.SetDock(energyDot, Dock.Left);
            energyChip.Children.Add(energyDot); energyChip.Children.Add(energyLine);
            energyChip.MouseLeftButtonDown += (s, e) => { e.Handled = true; SetTab(1); };
            Grid.SetColumn(energyChip, 1); rowGrid.Children.Add(energyChip);
            tabsRow = row;
            DockPanel.SetDock(row, Dock.Top); dock.Children.Add(row);

            // the two trays above the next task; their colour, corners and padding are set in Restyle (Ui.cs)
            checkIn = new RimBorder { Visibility = Visibility.Collapsed };
            leftDock = new DockPanel { LastChildFill = true }; // check-in, add box and the lists; see BuildMain (Checks.cs)
            DockPanel.SetDock(checkIn, Dock.Top); leftDock.Children.Add(checkIn);

            feedbackBox = new RimBorder { Visibility = Visibility.Collapsed };
            DockPanel.SetDock(feedbackBox, Dock.Top); leftDock.Children.Add(feedbackBox);
            BuildFocus(); // prompts, the next-task tray and the row of chips (Focus.cs)
        }

        FrameworkElement Tab(string text, int index, out TextBlock label, out Border line)
        {
            label = new TextBlock { Text = text, FontSize = Z(12), Foreground = T.Muted, Margin = new Thickness(0, 0, 0, 7) };
            line = new Border { Height = 2, Background = T.Accent, Visibility = Visibility.Hidden };
            var sp = new StackPanel { Margin = new Thickness(0, 0, 16, -1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            sp.Children.Add(label); sp.Children.Add(line);
            sp.MouseLeftButtonDown += (s, e) => { e.Handled = true; SetTab(index); };
            return sp;
        }

        // ------------------------------------------------------------ morning check-in

        void OpenCheckIn(bool edit)
        {
            var day = SleepLog.Today(Now());
            var tonight = SleepLog.ForDay(day);
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = edit ? "Last night's sleep" : "Good morning" + (Profile.Name.Length > 0 ? ", " + Profile.Name : "") + ". How did you sleep?", FontSize = Hd(14), FontWeight = FontWeights.SemiBold, Foreground = T.Text, Margin = new Thickness(0, 0, 0, 10) });
            var caps = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 4) };
            var c1 = Caps("Fell asleep"); c1.Width = TimeEntry.HostWidth + 6;
            caps.Children.Add(c1); caps.Children.Add(Caps("Woke"));
            sp.Children.Add(caps);

            // One row per sleep period: the first is the usual two boxes, "add another" adds up to two more.
            var rows = new StackPanel();
            var entries = new List<TimeEntry[]>();
            Action save = null, addBlank = null;
            var more = Btn("+ Add another sleep period", 2, () => addBlank());
            more.HorizontalAlignment = HorizontalAlignment.Left; more.Margin = new Thickness(-3, 0, 0, 2);

            Action<DateTime?, DateTime?> addRow = (from, to) =>
            {
                var asleep = new TimeEntry("asleep", entries.Count > 0); // a later period starts in the small hours, so "5" means am
                var woke = new TimeEntry("woke", true);
                if (from != null) asleep.Fill(from.Value);
                if (to != null) woke.Fill(to.Value);
                KeyEventHandler keys = (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; save(); } };
                asleep.Box.KeyDown += keys; woke.Box.KeyDown += keys;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(asleep.Host); row.Children.Add(woke.Host);
                var pair = new[] { asleep, woke };
                if (entries.Count > 0)
                {
                    var drop = new Grid { Width = 20, Height = 20, Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6), ToolTip = "Remove this period" };
                    var cross = Glyph("M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5", T.Muted, 1.4, 10, 10);
                    drop.Children.Add(cross);
                    drop.MouseEnter += (s, e) => cross.Stroke = T.Danger;
                    drop.MouseLeave += (s, e) => cross.Stroke = T.Muted;
                    drop.MouseLeftButtonDown += (s, e) => { e.Handled = true; entries.Remove(pair); rows.Children.Remove(row); more.Visibility = Visibility.Visible; };
                    row.Children.Add(drop);
                }
                entries.Add(pair);
                rows.Children.Add(row);
                more.Visibility = entries.Count < 3 ? Visibility.Visible : Visibility.Collapsed;
            };
            addBlank = () => addRow(null, null);

            if (edit && tonight != null)
                foreach (var p in tonight.Sleep) addRow(p.From, p.To);
            else if (Settings.Get("PrayerOn", 0) > 0.5)
            {   // "I usually wake for prayer": the split is filled in; only bedtime and the final wake-up are left to type
                var up = day.AddMinutes(Settings.Get("PrayerAt", PrayerAtDefault));
                addRow(null, up);
                addRow(up.AddMinutes(Settings.Get("PrayerMin", PrayerMinDefault)), null);
            }
            else addRow(null, null);

            save = () =>
            {
                var a = new List<TimeSpan>(); var w = new List<TimeSpan>(); bool ok = true;
                foreach (var pair in entries)
                {
                    TimeSpan ta, tw;
                    bool okA = pair[0].TryGet(out ta), okW = pair[1].TryGet(out tw);
                    if (!okA) pair[0].Box.Foreground = T.Danger;
                    if (!okW) pair[1].Box.Foreground = T.Danger;
                    ok &= okA && okW; a.Add(ta); w.Add(tw);
                }
                if (!ok) return;
                var n = SleepLog.BuildSplit(a, w, day);
                if (n == null) { foreach (var pair in entries) { pair[0].Box.Foreground = T.Danger; pair[1].Box.Foreground = T.Danger; } return; }
                SleepLog.Record(n);
                checkIn.Visibility = Visibility.Collapsed;
                RefreshEnergy(false);
                SetTab(1);
            };
            sp.Children.Add(rows);
            sp.Children.Add(more);
            sp.Children.Add(new Border { Height = 1, Background = T.Ctl, Margin = new Thickness(0, 4, 0, 9) }); // a rule on a tray is a shade darker than one on white
            var foot = new Grid();
            var skip = Btn(edit ? "Cancel" : "Same as last night", 2, () =>
            {
                if (!edit) SleepLog.Skip(day);
                checkIn.Visibility = Visibility.Collapsed;
                RefreshEnergy(false);
            });
            skip.HorizontalAlignment = HorizontalAlignment.Left; skip.Margin = new Thickness(-3, 0, 0, 0);
            var ok2 = Btn("Save", 0, () => save());
            ok2.HorizontalAlignment = HorizontalAlignment.Right; ok2.Margin = new Thickness(0);
            foot.Children.Add(skip); foot.Children.Add(ok2);
            sp.Children.Add(foot);
            checkIn.Child = sp;
            checkIn.Visibility = Visibility.Visible;
        }

        // ------------------------------------------------------------ curve

        static string DurShort(double hours)
        {
            int m = (int)Math.Round(hours * 60);
            if (m < 60) return m + "m";
            return m / 60 + "h" + (m % 60 > 0 ? " " + m % 60 + "m" : "");
        }

        /// One of the three figures under the chart: a white card. All three are built the same - a small icon, the
        /// figure, its name, one more line of a fixed height - so they come out equal. The last line is handed back
        /// for whatever does not fit as text (Need puts its - and + there).
        static Border Stat(string icon, string caption, string value, string sub, Action click, out Grid third)
        {
            var sp = new StackPanel();
            var mark = Glyph(icon, T.Muted, 1.4, 14, 14); mark.HorizontalAlignment = HorizontalAlignment.Left; mark.Margin = new Thickness(0, 0, 0, Sp(4, 3));
            sp.Children.Add(mark);
            sp.Children.Add(new TextBlock { Text = value, FontSize = Hd(Sp(17, 15)), FontWeight = FontWeights.SemiBold, Foreground = T.Text, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = caption, FontSize = Z(10.5), Foreground = T.Muted, Margin = new Thickness(0, 1, 0, 0) });
            third = new Grid { MinHeight = Sp(20, 18), Margin = new Thickness(0, 1, 0, 2) }; // the 2 underneath is taken off the box's padding: display scaling may round a box a fraction short, and that must not clip the - and +
            if (sub != null) third.Children.Add(new TextBlock { Text = sub, FontSize = Z(10), Foreground = T.Muted, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(third);
            // the outline is see-through until the figure's own panel is open (the debt calculator)
            var b = new Border { CornerRadius = new CornerRadius(Sp(18, 16)), Background = T.Field, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1.5), Padding = new Thickness(PadX - 3.5, PadY - 3.5, PadX - 5.5, PadY - 5.5), Margin = new Thickness(Sp(4, 3), 0, Sp(4, 3), 0), Child = sp };
            if (click != null)
            {
                b.Cursor = Cursors.Hand; b.ToolTip = "Click to correct";
                b.MouseLeftButtonDown += (s, e) => { e.Handled = true; click(); };
            }
            return b;
        }

        void DrawEnergy()
        {
            energyView.Children.Clear();
            sect = energyView;
            // boxes start Side in from the panel's edge (SideMid on the side that faces the tasks column in Full Mode);
            // every block ends with a gap of GapY, which the last one gives back
            energyView.Margin = new Thickness(SideMid, 0, Side, -GapY);
            chartPins.Clear();
            if (energy == null)
            {
                BeginCard(T.Tray);
                sect.Children.Add(new TextBlock { Text = "Enter last night's sleep to see today's curve.", FontSize = Z(12.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
                var w = new WrapPanel { Margin = new Thickness(0, 0, 0, -Sp(6, 5)) }; w.Children.Add(Btn("Enter sleep", 0, () => OpenCheckIn(false)));
                sect.Children.Add(w);
                EndCard();
                if (full) DrawRoutines(); // routines stay editable before any sleep is entered (Full Mode only)
                return;
            }

            var now = Now();
            // the tray: what the curve says right now, the rest of the line underneath, then the chart
            BeginCard(T.Tray);
            string line = energy.ZoneLine(now); int cut = line.IndexOf(" · ", StringComparison.Ordinal);
            string head = cut < 0 ? line : line.Substring(0, cut), rest = cut < 0 ? "" : line.Substring(cut + 3);
            if (rest.Length > 0) rest = char.ToUpper(rest[0]) + rest.Substring(1);
            if (energy.BedPush >= 5) rest += (rest.Length > 0 ? " · " : "") + "late nap: wind-down about " + (int)Math.Round(energy.BedPush) + " min later";
            sect.Children.Add(new TextBlock { Text = head, FontSize = Hd(Sp(18, 16)), FontWeight = FontWeights.SemiBold, Foreground = T.Text, TextWrapping = TextWrapping.Wrap });
            sect.Children.Add(new TextBlock { Text = rest, FontSize = Z(11.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, Sp(8, 5)), Visibility = rest.Length > 0 ? Visibility.Visible : Visibility.Collapsed });

            // as wide as the room inside the tray
            double H = Sp(104, 86), W = (full ? FullW / 2 : CardW) - energyView.Margin.Left - energyView.Margin.Right - 2 * PadX, yTop = 26;
            // a zone's name stays in sight while the corner panel's chart is scrolled: it follows the left edge inside its zone
            Action<TextBlock, double, double> pin = (lbl, x0, x1) =>
            {
                lbl.FontFamily = UiFont; lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double lw = lbl.DesiredSize.Width;
                Action<double> place = off => Canvas.SetLeft(lbl, Math.Max(x0 + 6, Math.Min(x1 - lw - 5, off + 6)));
                place(0); chartPins.Add(place);
            };
            var s = energy.Samples; int n = s.Length;
            // The chart covers 24 hours from waking. Full Mode fits all of it; the corner panel shows six hours at a time,
            // labelled every hour, and scrolls with the bar underneath.
            var all = energy.All; double view = W; W = full ? view : view * 4;
            double lo = s.Min() - 0.03, hi = s.Max() + 0.03, total = (energy.NextWake - energy.Wake).TotalMinutes;
            Func<DateTime, double> X = t => W * Math.Max(0, Math.Min(1, (t - energy.Wake).TotalMinutes / total));
            Func<double, double> Y = v => yTop + (1 - (v - lo) / (hi - lo)) * (H - 6 - yTop);

            var c = new Canvas { Background = Brushes.Transparent, Width = W, Height = H + 28, HorizontalAlignment = HorizontalAlignment.Left };
            var pts = new PointCollection();
            for (int i = 0; i < n; i++) pts.Add(new Point(X(energy.TimeAt(i)), Y(s[i])));
            double xBed = X(energy.TimeAt(n - 1));
            var area = new PointCollection(pts); area.Add(new Point(xBed, H)); area.Add(new Point(0, H));
            if (all.Length > n)
            {   // expected sleep: a sand block, with the curve carried on faintly to the next wake-up
                var asleep = new Rectangle { Width = Math.Max(1, W - xBed), Height = H, RadiusX = 8, RadiusY = 8, Fill = T.Sand };
                Canvas.SetLeft(asleep, xBed); c.Children.Add(asleep);
                var nl = new TextBlock { Text = "sleep", FontSize = Z(9), Foreground = T.Text };
                pin(nl, xBed, W); Canvas.SetTop(nl, 4); c.Children.Add(nl);
                var tail = new PointCollection();
                for (int i = n - 1; i < all.Length; i++) tail.Add(new Point(X(energy.TimeAt(i)), Y(Math.Max(lo + 0.01, all[i])))); // never below the waking day's range
                c.Children.Add(new Polyline { Points = tail, Stroke = T.Text, StrokeThickness = 1.25, Opacity = 0.4, StrokeDashArray = new DoubleCollection(new double[] { 3, 3 }) });
            }
            // the area under the curve is filled solid: lime under a peak, forest under the dip, grey-green between
            c.Children.Add(new Polygon { Points = area, Fill = T.Ctl });
            var names = new List<TextBlock>();
            foreach (var z in new[] { energy.Peak1, energy.Dip, energy.Peak2 })
            {
                if (z == null) continue;
                double zx = X(z.From), zw = Math.Max(1, X(z.To) - zx);
                c.Children.Add(new Polygon { Points = area, Fill = z.Peak ? (Brush)T.Accent : T.Strong, Clip = new RectangleGeometry(new Rect(zx, 0, zw, H)) });
                if (zw >= 28)
                {
                    var zl = new TextBlock { Text = z.Peak ? "peak" : "dip", FontSize = Z(9), Foreground = z.Peak ? (Brush)T.OnAccent : T.OnStrong };
                    pin(zl, zx, zx + zw); Canvas.SetTop(zl, H - 15); names.Add(zl);
                }
            }
            foreach (var nap in energy.Naps)
            {   // a logged nap: a narrow band
                var band = new Rectangle { Width = Math.Max(3, X(nap.To) - X(nap.From)), Height = H, RadiusX = 2, RadiusY = 2, Fill = T.Strong, Opacity = 0.35 };
                Canvas.SetLeft(band, X(nap.From)); c.Children.Add(band);
            }
            c.Children.Add(new Polyline { Points = pts, Stroke = T.Text, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            foreach (var zl in names) c.Children.Add(zl); // the names sit over the curve

            if (now >= energy.Wake && now <= energy.Bed)
            {
                double x = X(now);
                c.Children.Add(new Line { X1 = x, X2 = x, Y1 = Y(energy.At(now)), Y2 = H, Stroke = T.Text, StrokeThickness = 1, Opacity = 0.5, StrokeDashArray = new DoubleCollection(new double[] { 2, 3 }) });
                var dot = new Ellipse { Width = 10, Height = 10, Fill = T.Field, Stroke = T.Text, StrokeThickness = 2.25 };
                Canvas.SetLeft(dot, x - 5); Canvas.SetTop(dot, Y(energy.At(now)) - 5); c.Children.Add(dot);
                // a small dark pill with the time, above the dot
                var tip = new Border { CornerRadius = new CornerRadius(8), Background = T.Strong, Padding = new Thickness(7, 1, 7, 2), Child = new TextBlock { Text = EnergyDay.Clock(now), FontFamily = UiFont, FontSize = Z(9.5), FontWeight = SemiSmall, Foreground = T.OnStrong } };
                tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double tw = tip.DesiredSize.Width, th = tip.DesiredSize.Height;
                Canvas.SetLeft(tip, Math.Max(0, Math.Min(W - tw, x - tw / 2))); Canvas.SetTop(tip, Math.Max(0, Y(energy.At(now)) - 9 - th)); c.Children.Add(tip);
            }
            // time axis: a label at every hour in the corner panel, every second or third in Full Mode
            double TickHours = !full ? 1 : EnergyDay.Use12 ? 3 : 2; // am/pm labels are wider; the fitted chart has less room
            var tick = energy.Wake.Date.AddHours(Math.Ceiling(energy.Wake.TimeOfDay.TotalHours / TickHours) * TickHours);
            for (; tick <= energy.NextWake; tick = tick.AddHours(TickHours))
            {
                double x = X(tick);
                var lb = new TextBlock { Text = EnergyDay.HourLabel(tick), FontFamily = UiFont, FontSize = Z(10), Foreground = T.Faint };
                lb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double lw = lb.DesiredSize.Width;
                Canvas.SetLeft(lb, Math.Max(0, Math.Min(W - lw, x - lw / 2))); Canvas.SetTop(lb, H + 13);
                c.Children.Add(lb);
            }
            AddChart(c, view, W, H, Y, now); // the cursor, and scrolling in the corner panel (UiSleep.cs)
            EndCard();

            // slept / debt / need
            var night = energy.Last;
            string sleptSub = night.Sleep.Count > 1 ? night.Sleep.Count + " periods" : EnergyDay.Clock(night.Sleep[0].From) + "–" + EnergyDay.Clock(night.Sleep[0].To);
            if (energy.Naps.Count > 0) sleptSub += " + " + DurShort(energy.Naps.Sum(x => x.Hours)) + " nap";
            if (night.Assumed || night.WokeAt.Date != SleepLog.Today(now)) sleptSub += " (assumed)";
            // the corner panel has room for a few words on the last line; the whole of it is on the tooltip
            string sleptShort = night.Sleep.Count > 1 ? night.Sleep.Count + " periods" : "woke " + EnergyDay.Clock(night.Sleep[0].To);
            bool debt = energy.DebtHours >= 0.25;
            Grid last;
            var stats = new UniformGrid { Columns = 3, Margin = new Thickness(-Sp(4, 3), 0, -Sp(4, 3), GapY) };
            var slept = Stat("M11.5,8.5 A5,5 0 0 1 5.5,2.5 A5,5 0 1 0 11.5,8.5 Z", "Slept", DurShort(night.Hours), Slim ? sleptShort : sleptSub, () => OpenCheckIn(true), out last);
            slept.ToolTip = sleptSub + "  ·  click to correct";
            stats.Children.Add(slept);
            var debtCard = Stat("M7,2 A5,5 0 1 1 6.99,2 Z M4.5,7 H9.5", "Debt", debt ? DurShort(energy.DebtHours) : "none", (Slim ? "" : "over ") + EnergyModel.DebtNights + " nights", null, out last);
            debtCard.ToolTip = "Sleep debt calculator"; debtCard.Cursor = Cursors.Hand;
            debtCard.MouseLeftButtonDown += (s2, e2) => { e2.Handled = true; debtOpen = !debtOpen; DrawEnergy(); };
            if (debtOpen) debtCard.BorderBrush = T.Strong; // outlined while its calculator is open underneath
            stats.Children.Add(debtCard);
            var need = Stat("M1.5,10.5 V4.5 M1.5,8 H12.5 V10.5 M12.5,8 V6.5 A2,2 0 0 0 10.5,4.5 H6 V8", "Need", DurShort(energy.Need), null, null, out last);
            Action<double> step = dv => { Settings.Set("SleepNeed", Math.Max(5, Math.Min(11, energy.Need + dv))); RefreshEnergy(false); };
            var steps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var minus = Step("–", () => step(-0.25)); var plus = Step("+", () => step(0.25));
            ((Border)minus).Height = Sp(18, 16); ((Border)plus).Height = Sp(18, 16); plus.Margin = new Thickness(4, 0, 0, 0);
            steps.Children.Add(minus); steps.Children.Add(plus);
            last.Children.Add(steps); // the last line, where the other two have text
            if (needHint != null) need.ToolTip = needHint.Note; // where the estimate from your check-ins stands
            stats.Children.Add(need);
            sect.Children.Add(stats);
            DrawNeedHint(now); // a sleep need suggested from your check-ins, once there is one
            if (debtOpen) DrawDebt(now); // the sleep debt calculator (UiSleep.cs)

            DrawNaps(now);
            if (!full) return; // the corner panel stops here; settings, the plan and routines are in Full Mode only

            // "I usually wake for Fajr", Tuning and Clock are on the Settings page (Ui.cs)
            DrawPlan(now);
        }

        /// The sleep need your check-ins point to (Need.cs), offered once it differs from the setting by 15 minutes or more.
        /// Nothing changes until "Use" is pressed. "Keep" is remembered, so the same figure is not offered again.
        void DrawNeedHint(DateTime now)
        {
            var h = needHint; double cur = energy.Need;
            if (h == null || !h.Ready || Math.Abs(h.Need - cur) < 0.249 || Math.Abs(Settings.Get("NeedDeclined", 0) - h.Need) < 0.249) return;
            var with = EnergyModel.Build(SleepLog.Data.Nights, h.Need, SleepLog.Today(now));
            string text = "Your check-ins point to a sleep need of about " + DurShort(h.Need)
                + (h.High - h.Low > 0.01 ? " (likely " + DurShort(h.Low) + " to " + DurShort(h.High) + ")" : "") + ", from " + h.Days + " days. It is set to " + DurShort(cur) + "."
                + (with != null ? " With " + DurShort(h.Need) + " your sleep debt would read " + (with.DebtHours >= 0.25 ? DurShort(with.DebtHours) : "none") + " instead of " + (energy.DebtHours >= 0.25 ? DurShort(energy.DebtHours) : "none") + "." : "");
            BeginCard(T.Tray);
            sect.Children.Add(Caps("Sleep need"));
            sect.Children.Add(new TextBlock { Text = text, FontSize = Z(12), Foreground = T.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 9) });
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, -Sp(6, 5)) };
            row.Children.Add(Btn("Use " + DurShort(h.Need), 0, () => { Settings.Set("SleepNeed", h.Need); RefreshEnergy(false); }));
            row.Children.Add(Btn("Keep " + DurShort(cur), 2, () => { Settings.Set("NeedDeclined", h.Need); DrawEnergy(); }));
            sect.Children.Add(row);
            EndCard();
        }


        // ------------------------------------------------------------ naps

        /// "Log a nap" and "Rate now", the day's logged naps, the nap form and the re-plan offer.
        void DrawNaps(DateTime now)
        {
            var day = SleepLog.Today(now);
            var acts = new UniformGrid { Columns = 2, Margin = new Thickness(-Sp(4, 3), 0, -Sp(4, 3), 0) }; // two halves, split where the three figures above would be
            var napBtn = Btn(napOpen ? "Close" : "Log a nap", 1, () => { napOpen = !napOpen; DrawEnergy(); });
            var rateBtn = Btn("Rate now", 1, () => { rateNow = true; ShowFeedback(Now()); });
            napBtn.Margin = new Thickness(Sp(4, 3), 0, Sp(4, 3), GapY); rateBtn.Margin = new Thickness(Sp(4, 3), 0, Sp(4, 3), GapY);
            acts.Children.Add(napBtn); acts.Children.Add(rateBtn);
            sect.Children.Add(acts);

            Action<DateTime, DateTime, Action> log = (from, to, bad) =>
            {
                double mins = (to - from).TotalMinutes;
                if (mins < 5 || mins > 240 || to > now.AddMinutes(5) || from < energy.Wake || !SleepLog.AddNap(day, from, to)) { bad(); return; }
                napOpen = false;
                napOffer = data.Tasks.Any(t => Planner.Plannable(t, day)); // offer only; nothing moves without a yes
                RefreshEnergy(false);
            };
            if (napOpen)
            {
                var r1 = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
                var from = new TimeEntry("from", true, true);
                var to = new TimeEntry("to", true, true);
                r1.Children.Add(from.Host); r1.Children.Add(to.Host);
                Action add = () =>
                {
                    TimeSpan a, b;
                    bool okA = from.TryGet(out a), okB = to.TryGet(out b);
                    if (!okA) from.Box.Foreground = T.Danger;
                    if (!okB) to.Box.Foreground = T.Danger;
                    if (okA && okB) log(day + a, day + b, () => { from.Box.Foreground = T.Danger; to.Box.Foreground = T.Danger; });
                };
                KeyEventHandler k1 = (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; add(); } };
                from.Box.KeyDown += k1; to.Box.KeyDown += k1;
                r1.Children.Add(Btn("Add nap", 0, add));
                sect.Children.Add(r1);

                var r2 = new WrapPanel();
                r2.Children.Add(Label("or just woke, slept"));
                TextBox minutes;
                r2.Children.Add(MiniBox("25", 24, out minutes));
                r2.Children.Add(Label("min"));
                Action just = () =>
                {
                    int m;
                    if (!int.TryParse(minutes.Text, out m)) { minutes.Foreground = T.Danger; return; }
                    var end = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
                    log(end.AddMinutes(-m), end, () => minutes.Foreground = T.Danger);
                };
                minutes.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; just(); } };
                r2.Children.Add(Btn("Log", 1, just));
                sect.Children.Add(r2);
            }
            foreach (var n in energy.Naps)
            {
                var nap = n;
                var g = new Grid();
                var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                left.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = T.Strong, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
                left.Children.Add(new TextBlock { Text = "Nap " + EnergyDay.Clock(nap.From) + "–" + EnergyDay.Clock(nap.To), FontSize = Z(12), Foreground = T.Text });
                g.Children.Add(left);
                var remove = Btn("Remove", 2, () => { SleepLog.RemoveNap(day, nap); napOffer = false; RefreshEnergy(false); });
                remove.HorizontalAlignment = HorizontalAlignment.Right; remove.Margin = new Thickness(0);
                g.Children.Add(remove);
                sect.Children.Add(new PillBorder { Background = T.Field, Padding = new Thickness(PadX, 3, PadX - Sp(6, 5), 3), Margin = new Thickness(0, 0, 0, GapY), Child = g });
            }
            if (napOffer && full) // the plan it leads to is only shown in Full Mode
            {
                var g = new Grid { Margin = new Thickness(PadX, 0, 0, GapY) };
                g.Children.Add(new TextBlock { Text = "Re-plan what hasn't started?", FontSize = Z(11.5), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center });
                var yesNo = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var no = Btn("No", 2, () => { napOffer = false; DrawEnergy(); }); no.Margin = new Thickness(0, 0, 4, 0);
                var yes = Btn("Re-plan", 0, () => { napOffer = false; MakePlan(true); }); yes.Margin = new Thickness(0);
                yesNo.Children.Add(no); yesNo.Children.Add(yes);
                g.Children.Add(yesNo);
                sect.Children.Add(g);
            }
        }

        // ------------------------------------------------------------ "I usually wake for Fajr" (on the Settings page)

        /// Drawn into a white card of the Settings page, so its boxes are the colour of a control on white.
        void DrawPrayer()
        {
            bool on = Settings.Get("PrayerOn", 0) > 0.5;
            sect.Children.Add(SettingLine("I usually wake for Fajr", on ? null : "Fills in the split when you enter last night's sleep", Switch(on, () => { Settings.Set("PrayerOn", on ? 0 : 1); if (settingsOpen) DrawSettings(); else DrawEnergy(); })));
            if (!on) return;
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(Label("At"));
            var at = new TimeEntry("time", true);
            at.Host.Background = T.Raised;
            at.Fill(DateTime.Today.AddMinutes(Settings.Get("PrayerAt", PrayerAtDefault)));
            row.Children.Add(at.Host);
            row.Children.Add(Label("for"));
            TextBox minutes;
            row.Children.Add(MiniBox(((int)Settings.Get("PrayerMin", PrayerMinDefault)).ToString(), 24, out minutes, T.Raised));
            row.Children.Add(Label("min"));
            // No Save button: a valid time and length are stored on Enter or when the box is left.
            Action save = () =>
            {
                TimeSpan t; int m;
                bool okT = at.TryGet(out t), okM = int.TryParse(minutes.Text, out m) && m >= 5 && m <= 180;
                if (!okT) at.Box.Foreground = T.Danger;
                if (!okM) minutes.Foreground = T.Danger;
                if (!okT || !okM) return;
                if (t.TotalMinutes != Settings.Get("PrayerAt", PrayerAtDefault)) Settings.Set("PrayerAt", t.TotalMinutes);
                if (m != (int)Settings.Get("PrayerMin", PrayerMinDefault)) Settings.Set("PrayerMin", m);
            };
            KeyEventHandler keys = (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; save(); } };
            at.Box.KeyDown += keys; minutes.KeyDown += keys;
            at.Box.LostKeyboardFocus += (s, e) => save();
            minutes.LostKeyboardFocus += (s, e) => save();
            sect.Children.Add(row);
        }

        // ------------------------------------------------------------ Plan

        /// The plan is a white card cut like a ticket: a lime strip down its left edge names it (the text runs upwards),
        /// and once there is a proposal a pale-lime foot holds the key to the ticks.
        void DrawPlan(DateTime now)
        {
            string name = (proposal == null ? "Today's plan" : "Proposed plan").ToUpperInvariant();
            double r = Radius;
            var body = new StackPanel { Margin = new Thickness(PadX, PadY, PadX, PadY) };
            var foot = new Border { Background = T.Soft, CornerRadius = new CornerRadius(0, 0, r, 0), Padding = new Thickness(PadX, 6, PadX, 7), Visibility = Visibility.Collapsed };
            var right = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(foot, Dock.Bottom); right.Children.Add(foot); right.Children.Add(body);
            var strip = new Border
            {
                Width = 30, Background = T.Accent, CornerRadius = new CornerRadius(r, 0, 0, r),
                Child = new TextBlock { Text = name, FontSize = Z(10), FontWeight = SemiSmall, Foreground = T.OnAccent, LayoutTransform = new RotateTransform(-90), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 0, 12) }
            };
            var ticket = new Grid { MinHeight = TextW(name, Z(10), SemiSmall) + 26 }; // tall enough for its name
            ticket.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ticket.ColumnDefinitions.Add(new ColumnDefinition());
            ticket.Children.Add(strip);
            Grid.SetColumn(right, 1); ticket.Children.Add(right);
            energyView.Children.Add(new RimBorder { CornerRadius = new CornerRadius(r), Background = T.Field, Margin = new Thickness(0, 0, 0, GapY), Child = ticket });
            sect = body;

            // the time Plan fills on the left, what can be done on the right
            var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var planNow = now; var planEnd = Planner.Until(energy, planNow);
            string window = planNow >= planEnd ? "Too close to bedtime to plan" : "Plans from " + (planNow < energy.Wake ? EnergyDay.Clock(energy.Wake) : "now") + " to " + EnergyDay.Clock(planEnd);
            head.Children.Add(new TextBlock { Text = window, FontSize = Z(11.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            Action<FrameworkElement> put = b => { b.Margin = new Thickness(4, 0, 0, 0); acts.Children.Add(b); };
            if (proposal == null)
            {
                if (planUndo != null) put(Btn("Undo plan", 2, UndoPlan, 1));
                put(Btn("Plan my day", 0, MakePlan, 1));
            }
            else
            {
                put(Btn("Discard", 2, () => { proposal = null; DrawEnergy(); }, 1));
                if (proposal.Any(x => x.Placed)) put(Btn("Apply", 0, ApplyPlan, 1));
            }
            Grid.SetColumn(acts, 1); head.Children.Add(acts);
            sect.Children.Add(head);

            if (proposal != null)
            {
                string why = null;
                if (proposal.Count == 0)
                    why = data.Tasks.Any(x => !x.Done) ? "Nothing to plan: your open tasks either have a time you set or belong to a later day. Clear a task's time to let Plan place it."
                        : "Nothing to plan yet: add a few tasks first, with an effort level (!high, !med, !low) and a length (45m).";
                else if (!proposal.Any(x => x.Placed))
                    why = "None of these fit before " + EnergyDay.Clock(planEnd) + ". Shorten them, or plan again tomorrow.";
                if (why != null)
                    sect.Children.Add(new TextBlock { Text = why, FontSize = Z(12), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
                double timeW = TextW(EnergyDay.Use12 ? "12:00pm" : "00:00", Z(11.5), SemiSmall) + 10;
                foreach (var it in proposal.OrderBy(x => x.Placed ? 0 : 1).ThenBy(x => x.Start))
                {
                    var g = new Grid { Margin = new Thickness(0, 3, 0, 4) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(timeW) });
                    g.ColumnDefinitions.Add(new ColumnDefinition());
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    // the tick says where the slot falls: on a peak, in the dip, or between
                    g.Children.Add(new Border { Width = 4, Height = 14, CornerRadius = new CornerRadius(2), Background = it.Placed ? ZoneBrush(it.Start) : T.Ctl, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
                    var time = new TextBlock { Text = it.Placed ? EnergyDay.Clock(it.Start) : "—", FontSize = Z(11.5), FontWeight = SemiSmall, Foreground = it.Placed ? (Brush)T.Text : T.Faint, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(time, 1); g.Children.Add(time);
                    var title = new TextBlock { Text = it.Title, FontSize = Z(12), Foreground = it.Placed ? (Brush)T.Text : T.Muted, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(title, 2); g.Children.Add(title);
                    var bits = new List<string>();
                    if (it.Level != null) bits.Add(TaskTags.Short(it.Level));
                    if (it.Physical) bits.Add("phys");
                    bits.Add(TaskTags.Len(it.Minutes));
                    var tag = new TextBlock { Text = it.Placed ? string.Join(" · ", bits) : "no room today", FontSize = Z(10.5), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    Grid.SetColumn(tag, 3); g.Children.Add(tag);
                    sect.Children.Add(g);
                }
                if (proposal.Any(x => x.Placed))
                {   // the key, in the ticket's foot; the ticks are outlined so the lime one shows on the pale lime
                    var key = new StackPanel { Orientation = Orientation.Horizontal };
                    string[] names = { "on a peak", "in the dip", "between" };
                    Brush[] brushes = { T.Accent, T.Strong, T.Ctl };
                    for (int i = 0; i < 3; i++)
                    {
                        key.Children.Add(new Border { Width = 5, Height = 11, CornerRadius = new CornerRadius(2.5), Background = brushes[i], BorderBrush = T.Muted, BorderThickness = new Thickness(i == 1 ? 0 : 0.75), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
                        key.Children.Add(new TextBlock { Text = names[i], FontSize = Z(10), Foreground = T.Muted, Margin = new Thickness(0, 0, 12, 0) });
                    }
                    foot.Child = key; foot.Visibility = Visibility.Visible;
                }
            }
            EndCard();
            DrawRoutines();
        }

        // ------------------------------------------------------------ the Routines list

        void DrawRoutines()
        {
            if (Routines.Data == null) Routines.Load();
            var all = Routines.Data.Routines;
            BeginCard(T.Field);
            var rhead = (Grid)SectionHead("Routines", all.Count.ToString(), routinesOpen, () => { routinesOpen = !routinesOpen; editRoutine = null; DrawEnergy(); });
            var today = SleepLog.Today(Now());
            var waiting = all.Where(x => !x.Off && Routines.AppliesOn(x, today) && !RoutineInToday(x)).ToList();
            if (waiting.Count > 0)
            {   // every routine that is on for today and not yet in the task list
                var addAll = Btn(waiting.Count == 1 ? "Add 1 to today" : "Add " + waiting.Count + " to today", 1, () => AddRoutinesToToday(waiting));
                addAll.HorizontalAlignment = HorizontalAlignment.Right; addAll.Margin = new Thickness(0);
                ((FrameworkElement)addAll).ToolTip = "Send today's routines to your tasks, so Plan my day can place them";
                rhead.Children.Add(addAll); rhead.Height = double.NaN;
            }
            sect.Children.Add(rhead);
            if (!routinesOpen) { EndCard(); return; }

            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                var row = new Grid { Margin = new Thickness(0, 3, 0, 5), Background = Brushes.Transparent };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = Z(11), Foreground = T.Faint, Margin = new Thickness(0, 1, 0, 0) });

                if (editRoutine == r.Id)
                {
                    var tb = Box(12); tb.TextWrapping = TextWrapping.Wrap; tb.Text = Routines.Format(r, all);
                    var host = new Border { CornerRadius = new CornerRadius(12), Background = T.Raised, BorderBrush = T.Strong, BorderThickness = new Thickness(1.5), Padding = new Thickness(9, 4, 9, 5), Child = tb };
                    Grid.SetColumn(host, 1); Grid.SetColumnSpan(host, 2); row.Children.Add(host);
                    tb.KeyDown += (s, e) =>
                    {
                        if (e.Key == Key.Escape) { e.Handled = true; editRoutine = null; DrawEnergy(); }
                        else if (e.Key == Key.Enter)
                        {
                            e.Handled = true;
                            if (tb.Text.Trim().Length > 0) { Routines.Apply(r, tb.Text, all); Routines.Save(); }
                            editRoutine = null; DrawEnergy();
                        }
                    };
                    Dispatcher.BeginInvoke(new Action(() => { Activate(); tb.Focus(); tb.CaretIndex = tb.Text.Length; }));
                }
                else
                {
                    var sp = new StackPanel { Cursor = Cursors.Hand, Background = Brushes.Transparent };
                    sp.Children.Add(new TextBlock { Text = r.Title, FontSize = Z(12), Foreground = r.Off ? (Brush)T.Muted : T.Text, TextWrapping = TextWrapping.Wrap });
                    sp.Children.Add(new TextBlock { Text = Routines.Summary(r, all), FontSize = Z(10.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
                    sp.MouseLeftButtonDown += (s, e) => { e.Handled = true; editRoutine = r.Id; DrawEnergy(); };
                    Grid.SetColumn(sp, 1); row.Children.Add(sp);
                    var del = new Grid { Width = 18, Height = 18, Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
                    var cross = Glyph("M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5", T.Faint, 1.3, 10, 10);
                    cross.HorizontalAlignment = HorizontalAlignment.Right; cross.Margin = new Thickness(0, 0, -1, 0);
                    del.Children.Add(cross);
                    del.MouseEnter += (s, e) => cross.Stroke = T.Danger;
                    del.MouseLeave += (s, e) => cross.Stroke = T.Faint;
                    del.MouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        all.Remove(r);
                        foreach (var o in all) if (o.After == r.Id) o.After = null;
                        Routines.Save(); DrawEnergy();
                    };
                    // send this one routine to today's tasks; a tick once it is there
                    bool inToday = RoutineInToday(r);
                    var send = new Grid { Width = 22, Height = 18, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top, Cursor = inToday ? null : Cursors.Hand,
                        ToolTip = inToday ? "In today's tasks" : "Add to today's tasks" };
                    var sendGlyph = inToday ? Glyph("M1.5,5.5 L4,8 L8.5,2", T.Muted, 1.4, 10, 10) : Glyph("M5,1 V9 M1,5 H9", T.Faint, 1.4, 10, 10);
                    sendGlyph.HorizontalAlignment = HorizontalAlignment.Center; send.Children.Add(sendGlyph);
                    if (!inToday)
                    {
                        send.MouseEnter += (s, e) => sendGlyph.Stroke = T.Text;
                        send.MouseLeave += (s, e) => sendGlyph.Stroke = T.Faint;
                        send.MouseLeftButtonDown += (s, e) => { e.Handled = true; AddRoutinesToToday(new[] { r }); };
                    }
                    var ends = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
                    ends.Children.Add(send); ends.Children.Add(del);
                    Grid.SetColumn(ends, 2); row.Children.Add(ends);
                }
                sect.Children.Add(row);
            }

            var add = Box(12);
            var hint = new TextBlock { Text = "Add a routine", FontSize = Z(12), Foreground = T.Faint, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 1) };
            var ag = new Grid(); ag.Children.Add(hint); ag.Children.Add(add);
            sect.Children.Add(new PillBorder { Background = T.Raised, Padding = new Thickness(12, 4, 5, 4), Margin = new Thickness(0, 4, 0, 2), Child = ag, Cursor = Cursors.IBeam });
            // Effort (the three bars, click to change) and length sit in the box itself, beside the title. No instructions underneath.
            string[] efforts = { null, "low", "medium", "high" };
            string[] effortNames = { "not set", "low", "medium", "high" };
            ag.ColumnDefinitions.Add(new ColumnDefinition()); ag.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var effort = new Grid { Width = 24, Height = 20, Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
            Action paintEffort = () =>
            {
                int lvl = Math.Max(0, Array.IndexOf(efforts, newRoutineEnergy));
                effort.Children.Clear();
                var bars = Bars(lvl); bars.HorizontalAlignment = HorizontalAlignment.Center;
                effort.Children.Add(bars);
                effort.ToolTip = "Effort: " + effortNames[lvl] + " (click to change)";
            };
            paintEffort();
            effort.MouseLeftButtonDown += (s, e) => { e.Handled = true; newRoutineEnergy = efforts[(Math.Max(0, Array.IndexOf(efforts, newRoutineEnergy)) + 1) % efforts.Length]; paintEffort(); };
            TextBox lengthBox;
            var lengthHost = MiniBox(newRoutineMin.ToString(), 26, out lengthBox); // white: the box it sits in is tinted
            lengthHost.Margin = new Thickness(0); lengthHost.VerticalAlignment = VerticalAlignment.Center; lengthHost.ToolTip = "Length in minutes";
            var unit = new TextBlock { Text = "min", FontSize = Z(11), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 8, 1) };
            var extra = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            extra.Children.Add(effort); extra.Children.Add(lengthHost); extra.Children.Add(unit);
            Grid.SetColumn(extra, 1); ag.Children.Add(extra);

            add.Text = newRoutineText ?? "";
            Action sync = () => { hint.Visibility = add.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            sync();
            add.TextChanged += (s, e) => { newRoutineText = add.Text; sync(); };
            Action addRoutine = () =>
            {
                string typed = add.Text.Trim();
                if (typed.Length == 0) { add.Focus(); return; }
                int mins;
                if (!int.TryParse(lengthBox.Text.Trim(), out mins) || mins < 1 || mins > 600) { lengthBox.Foreground = T.Danger; return; }
                newRoutineMin = mins;
                var r = new Routine { Id = "r" + Guid.NewGuid().ToString("N").Substring(0, 8) };
                Routines.Apply(r, typed, all);
                // the pickers below fill in what was not typed as a tag
                if (!System.Text.RegularExpressions.Regex.IsMatch(typed, @"(^|\s)!(low|med|medium|high)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) r.Energy = newRoutineEnergy;
                if (!System.Text.RegularExpressions.Regex.IsMatch(typed, @"(^|\s)\d+(\.\d+)?\s?(m|min|h)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) r.Minutes = newRoutineMin;
                newRoutineText = null; newRoutineEnergy = null; newRoutineMin = 30;
                all.Add(r); Routines.Save(); DrawEnergy();
            };
            KeyEventHandler enter = (s, e) => { if (e.Key == Key.Enter && add.Text.Trim().Length > 0) { e.Handled = true; addRoutine(); } };
            add.KeyDown += enter; lengthBox.KeyDown += enter;
            EndCard();
        }

        // ------------------------------------------------------------ energy on a task row

        /// Energy on a task row: one to three bars, then "phys" and the length. One click cycles unset -> low -> med -> high -> unset.
        FrameworkElement EnergyTag(TaskItem t)
        {
            string level = TaskTags.Level(t.Energy);
            int bars = level == "high" ? 3 : level == "medium" ? 2 : level == "low" ? 1 : 0;
            var parts = new List<string>();
            if (t.Kind == "physical") parts.Add("phys");
            if (t.Minutes > 0) parts.Add(TaskTags.Len(t.Minutes));
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var b = Bars(bars); b.Margin = new Thickness(2, 0, parts.Count > 0 ? 5 : 0, 1);
            sp.Children.Add(b);
            if (parts.Count > 0) sp.Children.Add(new TextBlock { Text = string.Join(" ", parts), FontSize = Z(10.5), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) });
            // nothing set: the empty bars only show while the row is hovered (MakeRow fades a Grid in and out)
            FrameworkElement el;
            if (bars > 0 || parts.Count > 0) el = new Border { Child = sp };
            else { var g = new Grid { Opacity = 0 }; g.Children.Add(sp); el = g; }
            el.Margin = new Thickness(0, 0, 6, 0); el.Cursor = Cursors.Hand; el.ToolTip = level == null ? "Energy: not set" : "Energy: " + level;
            if (el is Border) ((Border)el).Background = Brushes.Transparent; else ((Grid)el).Background = Brushes.Transparent;
            el.MouseLeftButtonDown += (s, ev) =>
            {
                ev.Handled = true;
                t.Energy = level == null ? "low" : level == "low" ? "medium" : level == "medium" ? "high" : null;
                Save(); Rebuild();
            };
            return el;
        }
    }
}
