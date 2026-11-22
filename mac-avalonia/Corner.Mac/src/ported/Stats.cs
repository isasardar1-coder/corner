// Ported from corner/src/Stats.cs (WPF) to Avalonia: tools/port_ui.py, then fixed by hand.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using IO = System.IO;

namespace Corner
{
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
            SetLine(card, SettingLine("The full log", Os.Mac ? "done.csv and days.csv, in Corner's data folder. They open in Numbers." : "done.csv and days.csv, beside Corner. They open in Excel.", Btn("Show", 1, () =>
            {
                try
                {
                    string f = IO.Path.Combine(TaskLog.Folder, "done.csv");
                    Os.Reveal(IO.File.Exists(f) ? f : TaskLog.Folder);
                }
                catch (Exception ex) { Store.Log(ex); }
            }, 1)));
        }

        static Control Big(string text)
        {
            return new TextBlock { Text = text, FontSize = Hd(15), FontWeight = FontWeight.SemiBold, Foreground = T.Text, VerticalAlignment = VerticalAlignment.Center };
        }
    }
}
