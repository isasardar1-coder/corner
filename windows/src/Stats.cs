// Corner - the record of what gets done, kept on this PC only, as two spreadsheets beside tasks.json:
//   done.csv  one row each time a task is ticked off (and removed again if it is unticked the same day)
//   days.csv  one row per day Corner was running, written when the next day starts
// Both open in Excel or Google Sheets. Settings shows a short summary of the last seven days.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using IO = System.IO;

namespace Corner
{
    public static class TaskLog
    {
        public class Row
        {
            public DateTime Day, At;
            public string Id, Title, Effort, Kind, Zone;
            public bool Routine, Planned;
            public int Minutes;
            public double TimerMinutes;
        }

        static string DoneFile { get { return IO.Path.Combine(Store.Dir, "done.csv"); } }
        static string DaysFile { get { return IO.Path.Combine(Store.Dir, "days.csv"); } }
        public static string Folder { get { return Store.Dir; } }

        public const string DoneHead = "date,time,task_id,title,effort,kind,from_routine,minutes_planned,placed_by_plan,planned_start,minutes_from_planned_start,timer_minutes,energy_zone";
        public const string DaysHead = "date,tasks,done,open,done_pct,high_done,high_total,medium_done,medium_total,low_done,low_total,untagged_done,untagged_total,routines_sent,routines_done,placed_by_plan,placed_done,high_done_on_peak,sleep_hours,sleep_debt_hours";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// Goes up each time done.csv is written, so what is worked out from it (Habits.Log in Work.cs) knows to read it again.
        public static int Changes;

        static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        /// Splits one CSV line, with quoted fields.
        public static List<string> Split(string line)
        {
            var f = new List<string>(); var sb = new StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
                else if (c == '"') q = true;
                else if (c == ',') { f.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            f.Add(sb.ToString());
            return f;
        }

        static void Append(string file, string head, string line)
        {
            try
            {
                bool fresh = !IO.File.Exists(file);
                IO.File.AppendAllText(file, (fresh ? head + Environment.NewLine : "") + line + Environment.NewLine, new UTF8Encoding(fresh));
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// A task was ticked off at `at`, on the day `day` (the day as Corner counts it, which starts in the early morning).
        public static void Done(TaskItem t, DateTime at, DateTime day, double timerMinutes, string zone)
        {
            var due = t.DueAt;
            string level = TaskTags.Level(t.Energy);
            var p = new[]
            {
                day.ToString("yyyy-MM-dd", Inv), at.ToString("HH:mm", Inv), t.Id, Csv(t.Title), level ?? "", t.Kind ?? "mental",
                t.Routine != null ? "yes" : "no", (t.Minutes > 0 ? t.Minutes : Planner.DefaultMinutes).ToString(Inv),
                t.Planned ? "yes" : "no", t.Planned && due != null ? due.Value.ToString("HH:mm", Inv) : "",
                t.Planned && due != null ? Math.Round((at - due.Value).TotalMinutes).ToString(Inv) : "",
                timerMinutes > 0 ? Math.Round(timerMinutes, 1).ToString(Inv) : "", zone ?? ""
            };
            Append(DoneFile, DoneHead, string.Join(",", p));
            Changes++;
        }

        /// A task was unticked: its row for `day` comes out again, so the record only holds what stayed done.
        public static void Undone(string id, DateTime day)
        {
            try
            {
                if (!IO.File.Exists(DoneFile)) return;
                string d = day.ToString("yyyy-MM-dd", Inv);
                var lines = IO.File.ReadAllLines(DoneFile).ToList();
                int at = lines.FindLastIndex(l => { var f = Split(l); return f.Count > 2 && f[0] == d && f[2] == id; });
                if (at <= 0) return;
                lines.RemoveAt(at);
                IO.File.WriteAllLines(DoneFile, lines, new UTF8Encoding(true));
                Changes++;
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        public static List<Row> ReadDone()
        {
            var list = new List<Row>();
            try
            {
                if (!IO.File.Exists(DoneFile)) return list;
                foreach (var line in IO.File.ReadAllLines(DoneFile).Skip(1))
                {
                    var f = Split(line); DateTime day, at; TimeSpan tm; int mins; double tmr;
                    if (f.Count < 13 || !DateTime.TryParseExact(f[0], "yyyy-MM-dd", Inv, DateTimeStyles.None, out day)) continue;
                    at = TimeSpan.TryParseExact(f[1], @"hh\:mm", Inv, out tm) ? day + tm : day;
                    int.TryParse(f[7], NumberStyles.Integer, Inv, out mins);
                    double.TryParse(f[11], NumberStyles.Float, Inv, out tmr);
                    list.Add(new Row { Day = day, At = at, Id = f[2], Title = f[3], Effort = f[4], Kind = f[5], Routine = f[6] == "yes", Minutes = mins, Planned = f[8] == "yes", TimerMinutes = tmr, Zone = f[12] });
                }
            }
            catch (Exception ex) { Store.Log(ex); }
            return list;
        }

        public class Day
        {
            public DateTime Date;
            public int Tasks, Done, Open, RoutinesSent, RoutinesDone, Placed, PlacedDone, HighDoneOnPeak;
            public int[] DoneBy = new int[4], TotalBy = new int[4]; // high, medium, low, untagged
            public double? Sleep, Debt;
        }

        static readonly string[] Levels = { "high", "medium", "low", null };

        /// The day's figures: what was done (from done.csv) and what was still open at its end (from the task list).
        public static Day Summarise(DateTime day, IList<TaskItem> tasks, IList<Row> doneRows, double? sleep, double? debt)
        {
            string key = day.ToString("yyyy-MM-dd", Inv);
            var done = doneRows.Where(r => r.Day == day.Date).GroupBy(r => r.Id).Select(g => g.Last()).ToList();
            var doneIds = new HashSet<string>(done.Select(r => r.Id));
            var open = tasks.Where(t => !t.Done && !doneIds.Contains(t.Id)
                && (t.Routine == null ? (t.DueAt == null || t.DueAt.Value.Date <= day.Date) : t.RoutineDay == key)).ToList();
            var d = new Day { Date = day.Date, Done = done.Count, Open = open.Count, Sleep = sleep, Debt = debt };
            d.Tasks = d.Done + d.Open;
            for (int i = 0; i < 4; i++)
            {
                d.DoneBy[i] = done.Count(r => (r.Effort.Length == 0 ? null : r.Effort) == Levels[i]);
                d.TotalBy[i] = d.DoneBy[i] + open.Count(t => TaskTags.Level(t.Energy) == Levels[i]);
            }
            d.RoutinesDone = done.Count(r => r.Routine);
            d.RoutinesSent = d.RoutinesDone + open.Count(t => t.Routine != null);
            d.PlacedDone = done.Count(r => r.Planned);
            d.Placed = d.PlacedDone + open.Count(t => t.Planned && t.DueAt != null && t.DueAt.Value.Date == day.Date);
            d.HighDoneOnPeak = done.Count(r => r.Effort == "high" && r.Zone == "peak");
            return d;
        }

        public static void WriteDay(Day d)
        {
            Func<double?, string> num = v => v == null ? "" : Math.Round(v.Value, 2).ToString(Inv);
            var p = new List<string> { d.Date.ToString("yyyy-MM-dd", Inv), d.Tasks.ToString(Inv), d.Done.ToString(Inv), d.Open.ToString(Inv),
                d.Tasks > 0 ? Math.Round(100.0 * d.Done / d.Tasks).ToString(Inv) : "" };
            for (int i = 0; i < 4; i++) { p.Add(d.DoneBy[i].ToString(Inv)); p.Add(d.TotalBy[i].ToString(Inv)); }
            p.AddRange(new[] { d.RoutinesSent.ToString(Inv), d.RoutinesDone.ToString(Inv), d.Placed.ToString(Inv), d.PlacedDone.ToString(Inv), d.HighDoneOnPeak.ToString(Inv), num(d.Sleep), num(d.Debt) });
            Append(DaysFile, DaysHead, string.Join(",", p));
        }

        public static List<Day> ReadDays()
        {
            var list = new List<Day>();
            try
            {
                if (!IO.File.Exists(DaysFile)) return list;
                foreach (var line in IO.File.ReadAllLines(DaysFile).Skip(1))
                {
                    var f = Split(line); DateTime day;
                    if (f.Count < 20 || !DateTime.TryParseExact(f[0], "yyyy-MM-dd", Inv, DateTimeStyles.None, out day)) continue;
                    Func<int, int> I = i => { int v; int.TryParse(f[i], NumberStyles.Integer, Inv, out v); return v; };
                    var d = new Day { Date = day, Tasks = I(1), Done = I(2), Open = I(3), RoutinesSent = I(13), RoutinesDone = I(14), Placed = I(15), PlacedDone = I(16), HighDoneOnPeak = I(17) };
                    for (int i = 0; i < 4; i++) { d.DoneBy[i] = I(5 + 2 * i); d.TotalBy[i] = I(6 + 2 * i); }
                    list.Add(d);
                }
            }
            catch (Exception ex) { Store.Log(ex); }
            return list;
        }
    }

    partial class Shell
    {
        readonly Dictionary<string, double> timerSecs = new Dictionary<string, double>(); // seconds each task's timer has run, this session
        DateTime lastTimerTick = DateTime.Now;

        /// Called every second: adds the time a running timer ran to its task.
        void CountTimer()
        {
            var now = DateTime.Now; double dt = Math.Min(5, (now - lastTimerTick).TotalSeconds); lastTimerTick = now;
            if (timerId == null || !timerRunning || dt <= 0) return;
            double s; timerSecs.TryGetValue(timerId, out s); timerSecs[timerId] = s + dt;
        }

        string ZoneName(DateTime t)
        {
            if (energy == null) return "";
            foreach (var z in new[] { energy.Peak1, energy.Dip, energy.Peak2 })
                if (z != null && t >= z.From && t < z.To) return z.Peak ? "peak" : "dip";
            return "between";
        }

        void LogDone(TaskItem t)
        {
            var now = Now();
            double s; timerSecs.TryGetValue(t.Id, out s);
            double worked = Elapsed(t); // the time worked on it, kept in tasks.json: it outlasts a restart of Corner, which the count above does not (Work.cs)
            if (worked > 0) s = worked;
            TaskLog.Done(t, now, SleepLog.Today(now), s / 60, ZoneName(now));
        }

        void LogUndone(TaskItem t) { TaskLog.Undone(t.Id, SleepLog.Today(Now())); }

        /// When a new day starts, writes the day before (the last day Corner was running) to days.csv. Called before
        /// the morning clean-up of unfinished routine tasks, so they still count as that day's open tasks.
        void LogDays()
        {
            try
            {
                var today = SleepLog.Today(Now());
                double key = today.Year * 10000 + today.Month * 100 + today.Day, seen = Settings.Get("StatsDay", 0);
                if (seen == key) return;
                Settings.Set("StatsDay", key);
                if (seen <= 0) return; // the first day: nothing to look back on
                int s = (int)seen; var day = new DateTime(s / 10000, s / 100 % 100, s % 100);
                if (day >= today) return;
                SleepLog.Load();
                var night = SleepLog.ForDay(day);
                var e = EnergyModel.Build(SleepLog.Data.Nights, Settings.Get("SleepNeed", EnergyModel.DefaultNeed), day);
                TaskLog.WriteDay(TaskLog.Summarise(day, data.Tasks, TaskLog.ReadDone(), night != null ? night.TotalHours : (double?)null, e != null ? e.DebtHours : (double?)null));
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// The last seven days, for Settings: earlier days from days.csv, today as it stands.
        void DrawRecord()
        {
            var now = Now(); var today = SleepLog.Today(now);
            var rows = TaskLog.ReadDone();
            var days = TaskLog.ReadDays().Where(d => d.Date > today.AddDays(-7) && d.Date < today).GroupBy(d => d.Date).Select(g => g.Last()).ToList();
            days.Add(TaskLog.Summarise(today, data.Tasks, rows, null, null));
            int done = days.Sum(d => d.Done), total = days.Sum(d => d.Tasks);
            int hiDone = days.Sum(d => d.DoneBy[0]), hiTotal = days.Sum(d => d.TotalBy[0]), hiPeak = days.Sum(d => d.HighDoneOnPeak);
            int rSent = days.Sum(d => d.RoutinesSent), rDone = days.Sum(d => d.RoutinesDone);
            var week = rows.Where(r => r.Day > today.AddDays(-7)).ToList();
            double timed = week.Sum(r => r.TimerMinutes);

            setCol.Children.Add(SetCaps("Your record"));
            var card = SetCard(setCol);
            Func<int, int, string> pct = (a, b) => b > 0 ? (int)Math.Round(100.0 * a / b) + "%" : "–";
            SetLine(card, SettingLine("Done in 7 days", total > 0 ? done + " of " + total + " tasks · " + (days.Count > 1 ? Math.Round(done / (double)days.Count, 1).ToString(CultureInfo.CurrentCulture) + " a day" : "today so far") : "Nothing yet",
                Big(pct(done, total))));
            SetLine(card, SettingLine("High effort", hiTotal > 0 ? hiDone + " of " + hiTotal + " done" + (hiDone > 0 ? " · " + pct(hiPeak, hiDone) + " of them on a peak" : "") : "No high-effort tasks yet", Big(pct(hiDone, hiTotal))));
            if (rSent > 0) SetLine(card, SettingLine("Routines", rDone + " of " + rSent + " sent to the day were done", Big(pct(rDone, rSent))));
            if (timed > 0) SetLine(card, SettingLine("Timed", "Minutes on task timers this week", Big(Math.Round(timed).ToString(CultureInfo.CurrentCulture))));
            SetLine(card, SettingLine("The full log", "done.csv and days.csv, beside Corner. They open in Excel.", Btn("Show", 1, () =>
            {
                try
                {
                    string f = IO.Path.Combine(TaskLog.Folder, "done.csv");
                    System.Diagnostics.Process.Start("explorer.exe", IO.File.Exists(f) ? "/select,\"" + f + "\"" : "\"" + TaskLog.Folder + "\"");
                }
                catch (Exception ex) { Store.Log(ex); }
            }, 1)));
        }

        static FrameworkElement Big(string text)
        {
            return new TextBlock { Text = text, FontSize = Hd(15), FontWeight = FontWeights.SemiBold, Foreground = T.Text, VerticalAlignment = VerticalAlignment.Center };
        }
    }
}
