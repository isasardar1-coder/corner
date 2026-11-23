// Corner - "I feel 63%, not 30%": click the energy counter on the cycle card, type how much energy you feel you have,
// and the counter is matched to you from then on. If you say so it also counts as a check-in, which teaches the
// curve's shape (Tuning in Checks.cs). Corrections are kept in feel.csv, beside tasks.json.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IO = System.IO;

namespace Corner
{
    /// Your corrections of the energy counter, and the counter's number worked out from them.
    public static class Calibration
    {
        public const int Keep = 20;          // the latest this-many corrections are used
        public const int WindowDays = 60;    // and none older than this
        public const int StretchMin = 3;     // corrections needed before the scale may stretch, not only shift
        public const double SpreadMin = 0.04; // ...and they must be this far apart on the curve

        public class Entry { public DateTime At; public double Model; public double Felt; } // Model and Felt are 0..1

        static string File { get { return IO.Path.Combine(Store.Dir, "feel.csv"); } }
        const string Fmt = "yyyy-MM-ddTHH:mm";

        public static List<Entry> Read()
        {
            var list = new List<Entry>();
            try
            {
                if (!IO.File.Exists(File)) return list;
                var inv = CultureInfo.InvariantCulture;
                foreach (var line in IO.File.ReadAllLines(File))
                {
                    var p = line.Split(',');
                    DateTime t; double m, f;
                    if (p.Length < 3 || !DateTime.TryParseExact(p[0], Fmt, inv, DateTimeStyles.None, out t)) continue;
                    if (!double.TryParse(p[1], NumberStyles.Float, inv, out m) || !double.TryParse(p[2], NumberStyles.Float, inv, out f)) continue;
                    list.Add(new Entry { At = t, Model = m, Felt = f });
                }
            }
            catch (Exception ex) { Store.Log(ex); }
            return list;
        }

        public static void Add(DateTime at, double model, double felt)
        {
            try
            {
                var inv = CultureInfo.InvariantCulture;
                string line = at.ToString(Fmt, inv) + "," + model.ToString("0.000", inv) + "," + felt.ToString("0.00", inv);
                IO.File.AppendAllText(File, (IO.File.Exists(File) ? "" : "time,curve,felt\r\n") + line + "\r\n");
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        public static void Clear() { try { if (IO.File.Exists(File)) IO.File.Delete(File); } catch (Exception ex) { Store.Log(ex); } }

        public static int Count(DateTime now) { return Use(Read(), now).Count; }

        static List<Entry> Use(IEnumerable<Entry> all, DateTime now)
        {
            var r = all.Where(x => (now - x.At).TotalDays <= WindowDays).OrderBy(x => x.At).ToList();
            return r.Count > Keep ? r.Skip(r.Count - Keep).ToList() : r;
        }

        /// The counter's level (0..1) for the curve's level v. One correction shifts the scale; several far enough
        /// apart on the curve also stretch it, so the counter moves as much as you say your energy does.
        public static double Map(double v, IList<Entry> all, DateTime now)
        {
            var r = Use(all, now);
            if (r.Count == 0) return v;
            double mm = r.Average(x => x.Model), mf = r.Average(x => x.Felt), b = 1;
            if (r.Count >= StretchMin)
            {
                double vm = r.Sum(x => (x.Model - mm) * (x.Model - mm)) / r.Count;
                if (Math.Sqrt(vm) >= SpreadMin)
                {
                    double cov = r.Sum(x => (x.Model - mm) * (x.Felt - mf)) / r.Count;
                    b = Math.Max(0.5, Math.Min(4, cov / vm));
                }
            }
            return Math.Max(0, Math.Min(1, mf + b * (v - mm)));
        }

        public static double Map(double v, DateTime now) { return Map(v, Read(), now); }
    }

    /// What a correction may lead to in today's tasks: a re-plan when the peaks and dip moved, and a high-effort task
    /// brought forward when you say you have more energy than the counter showed.
    public static class FeelPlan
    {
        public const int MoveMin = 10;        // a zone edge that moves less than this many minutes is not a move
        public const double HighFelt = 0.55;  // you need to feel at least this...
        public const int HighGap = 15;        // ...and this many points above the counter, for the high-effort offer
        public const int SoonMin = 60;        // no offer when a high-effort task is already timed within this many minutes

        public static bool ZonesMoved(EnergyDay a, EnergyDay b)
        {
            if (a == null || b == null) return false;
            return Moved(a.Peak1, b.Peak1) || Moved(a.Dip, b.Dip) || Moved(a.Peak2, b.Peak2);
        }

        static bool Moved(Zone x, Zone y)
        {
            if (x == null || y == null) return (x == null) != (y == null);
            return Math.Abs((x.From - y.From).TotalMinutes) >= MoveMin || Math.Abs((x.To - y.To).TotalMinutes) >= MoveMin;
        }

        /// The high-effort task to bring forward now, or null. Overdue ones first, then later today, then those without a time.
        public static TaskItem HighNow(IEnumerable<TaskItem> tasks, double felt, int shown, DateTime now, bool inTask)
        {
            if (inTask || felt < HighFelt || felt * 100 - shown < HighGap) return null;
            var high = tasks.Where(t => !t.Done && TaskTags.Level(t.Energy) == "high" && (t.DueAt == null || t.DueAt.Value.Date <= now.Date)).ToList();
            if (high.Any(t => t.HasTime && t.DueAt.Value >= now && t.DueAt.Value <= now.AddMinutes(SoonMin))) return null; // one is coming up anyway
            return high.OrderBy(t => t.HasTime && t.DueAt.Value < now ? 0 : t.HasTime ? 1 : 2).ThenBy(t => t.DueAt ?? DateTime.MaxValue).FirstOrDefault();
        }
    }

    partial class Shell
    {
        class FeelAfter { public bool Replan; public string HighId; public int Felt; }
        FeelAfter feelAfter; // after a correction: re-plan the day? bring a high-effort task forward?

        void AfterFeel(EnergyDay before, FeelAsk a, bool tuned)
        {
            if (workId != null) workFelt = Math.Round(a.Felt * 100); // typed while working on a task: into its row of work.csv (Work.cs)
            bool moved = tuned && !PriorityMode && FeelPlan.ZonesMoved(before, energy) && data.Tasks.Any(t => !t.Done); // (by priority there are no times to re-plan)
            var hi = FeelPlan.HighNow(data.Tasks, a.Felt, a.Shown, Now(), InTask);
            feelAfter = moved || hi != null ? new FeelAfter { Replan = moved, HighId = hi != null ? hi.Id : null, Felt = (int)Math.Round(a.Felt * 100) } : null;
        }

        /// Where the correction box was: the questions that follow from it, each with its own answer.
        FrameworkElement FeelAfterBox()
        {
            var f = feelAfter;
            var p = new StackPanel();
            Action tidy = () => { if (!f.Replan && f.HighId == null) feelAfter = null; };
            Action<string, string, FrameworkElement, FrameworkElement> ask = (title, body, yes, no) =>
            {
                if (p.Children.Count > 0) p.Children.Add(new Border { Height = 1, Background = T.Line, Margin = new Thickness(0, 2, 0, Sp(10, 8)) });
                p.Children.Add(new TextBlock { Text = title, FontSize = Z(12.5), FontWeight = SemiSmall, Foreground = T.Text, TextWrapping = TextWrapping.Wrap });
                p.Children.Add(new TextBlock { Text = body, FontSize = Z(11.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, Sp(9, 7)) });
                var row = new WrapPanel();
                foreach (var b in new[] { yes, no }) { b.Margin = new Thickness(0, 0, 6, 6); row.Children.Add(b); }
                p.Children.Add(row);
            };
            if (f.Replan && energy != null)
                ask("Your peak and dip moved.", "Re-plan the rest of today to the new times above? You'll see the new plan before anything changes.",
                    Btn("Re-plan today", 0, () => { f.Replan = false; tidy(); planSplit.Clear(); planKeep.Clear(); MakePlan(true); }), // (Plan.cs)
                    Btn("Not now", 2, () => { f.Replan = false; tidy(); DrawEnergy(); }));
            var t = f.HighId == null ? null : data.Tasks.FirstOrDefault(x => x.Id == f.HighId && !x.Done);
            if (t == null) f.HighId = null;
            else
                ask("You have energy now.", "You feel " + f.Felt + "%. Bring “" + t.Title + "” (high effort) forward to now?",
                    Btn("Move it to now", 0, () =>
                    {
                        f.HighId = null; tidy();
                        var now = Now(); var at = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
                        at = at.AddMinutes((5 - at.Minute % 5) % 5); // the next 5 minutes
                        t.SetDue(at, true); t.Planned = true; // Plan may move it again
                        SortByTime(); Save(); Rebuild(); DrawEnergy();
                    }),
                    Btn("Not now", 2, () => { f.HighId = null; tidy(); DrawEnergy(); }));
            tidy();
            if (p.Children.Count == 0) return null;
            return new RimBorder { CornerRadius = new CornerRadius(Radius * 0.75), Background = T.Field, Padding = new Thickness(PadX, Sp(10, 8), PadX, Sp(4, 2)), Margin = new Thickness(0, 0, 0, Sp(10, 8)), Child = p };
        }

        class FeelAsk { public double Felt, Model; public int Shown; public DateTime At; }
        FeelAsk feelAsk; // a correction typed into the counter, waiting for "Fine-tune the chart" or "Only the number"

        /// The counter's number turns into a box to type in.
        void EditFeel(Panel box, FrameworkElement num, int shown)
        {
            var tb = new TextBox
            {
                Text = shown.ToString(), FontFamily = UiFont, FontSize = Hd(Sp(20, 17)), FontWeight = FontWeights.SemiBold, Foreground = T.Text,
                Background = T.Field, BorderThickness = new Thickness(0), Width = Z(62), MaxLength = 3, Padding = new Thickness(6, 0, 6, 0),
                HorizontalContentAlignment = HorizontalAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right, CaretBrush = T.Text,
                ToolTip = "How much energy do you feel you have? 1 to 100, then Enter"
            };
            int i = box.Children.IndexOf(num);
            if (i < 0) return;
            box.Children.RemoveAt(i); box.Children.Insert(i, tb);
            bool closed = false;
            Action cancel = () => { if (closed) return; closed = true; commitEdit = null; DrawEnergy(); };
            Action commit = () =>
            {
                if (closed) return;
                int v;
                if (!int.TryParse(tb.Text.Trim().TrimEnd('%').Trim(), out v) || v < 1 || v > 100 || v == shown || energy == null) { cancel(); return; }
                closed = true; commitEdit = null;
                var now = Now();
                feelAsk = new FeelAsk { Felt = v / 100.0, Model = energy.AllAt(now), Shown = shown, At = now };
                DrawEnergy();
            };
            tb.PreviewTextInput += (s, e) => { if (e.Text.Any(ch => !char.IsDigit(ch) && ch != '%')) e.Handled = true; };
            tb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; commit(); }
                else if (e.Key == Key.Escape) { e.Handled = true; cancel(); }
            };
            tb.LostKeyboardFocus += (s, e) => commit();
            commitEdit = cancel; // the panel hiding drops it
            Activate(); tb.Focus(); tb.SelectAll();
        }

        /// Under the cycle card's heading, after a number was typed into the counter.
        FrameworkElement FeelAskBox()
        {
            var a = feelAsk;
            var p = new StackPanel();
            p.Children.Add(new TextBlock
            {
                Text = "You feel " + (int)Math.Round(a.Felt * 100) + "%. The counter said " + a.Shown + "%.",
                FontSize = Z(12.5), FontWeight = SemiSmall, Foreground = T.Text, TextWrapping = TextWrapping.Wrap
            });
            int have = Tuning.Count + 1;
            string how = have >= Tuning.MinRatings
                ? "Fine-tuning also counts as a check-in, so the chart moves toward how you feel at this time of day."
                : "Fine-tuning also counts as a check-in. The chart starts to reshape after " + Tuning.MinRatings + " of them (this makes " + have + ").";
            p.Children.Add(new TextBlock { Text = "The counter will match you from now on. " + how, FontSize = Z(11.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, Sp(9, 7)) });
            var row = new WrapPanel();
            Action<FrameworkElement> put = b => { b.Margin = new Thickness(0, 0, 6, 6); row.Children.Add(b); };
            put(Btn("Fine-tune the chart", 0, () =>
            {
                feelAsk = null;
                var before = energy;
                Calibration.Add(a.At, a.Model, a.Felt);
                int score = (int)Math.Max(1, Math.Min(10, Math.Round(1 + 9 * a.Felt)));
                Feedback.Log(a.At, score.ToString(CultureInfo.InvariantCulture), energy, activeToday, ActiveSession(a.At)); // as a 1-10 check-in (Checks.cs)
                RefreshEnergy(false);
                AfterFeel(before, a, true); // re-plan? a high-effort task now?
                DrawEnergy();
            }));
            put(Btn("Only the number", 1, () => { feelAsk = null; Calibration.Add(a.At, a.Model, a.Felt); AfterFeel(energy, a, false); DrawEnergy(); }));
            put(Btn("Cancel", 2, () => { feelAsk = null; DrawEnergy(); }));
            p.Children.Add(row);
            return new RimBorder { CornerRadius = new CornerRadius(Radius * 0.75), Background = T.Field, Padding = new Thickness(PadX, Sp(10, 8), PadX, Sp(4, 2)), Margin = new Thickness(0, 0, 0, Sp(10, 8)), Child = p };
        }
    }
}
