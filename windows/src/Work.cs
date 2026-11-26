// Corner - working on a task, and what Corner learns from it (2026-11-23).
//   work.csv   one row for each stretch of work on a task, from play to pause or done: when, how long, and the energy
//              % at its start and end. Kept beside tasks.json; it opens in Excel.
//   Habits     what the log says you usually do in each part of the day (morning, noon, afternoon, evening).
//   Priority   "Plan by: Priority" (Settings): the open tasks in the order to do them for your energy now. No clock times.
// The list is put in that order when a plan is applied and each time a task is paused or completed, and at no other time.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IO = System.IO;

namespace Corner
{
    // ---------------------------------------------------------------- work.csv

    public class WorkRow
    {
        public DateTime Start, End;
        public double Minutes;
        public string Id, Title, Effort, Kind, Ended; // Ended: paused, completed, switched, away, timer ended, closed
        public bool Routine;
        public int Planned;                           // the task's planned length, minutes
        public double E0 = -1, E1 = -1, Felt = -1;    // energy % at the start and the end, and a % typed in meanwhile; -1 = not known
        public bool Ticked;                           // not from work.csv: a task ticked off with no timer (done.csv). It marks the day, and has no minutes
    }

    public static class WorkLog
    {
        public const string Head = "date,start,end,minutes,task_id,title,effort,kind,from_routine,minutes_planned,energy_start,energy_end,energy_typed,ended,part_of_day";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string File { get { return IO.Path.Combine(Store.Dir, "work.csv"); } }

        static List<WorkRow> cache;
        /// The whole log, read once and then kept up as rows are added.
        public static List<WorkRow> Rows { get { if (cache == null) cache = Read(); return cache; } }
        public static void Forget() { cache = null; }

        static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        static string Pct(double v) { return v < 0 ? "" : Math.Round(v).ToString(Inv); }
        static double Num(string s) { double v; return s.Length > 0 && double.TryParse(s, NumberStyles.Float, Inv, out v) ? v : -1; }

        public static string Line(WorkRow r)
        {
            return string.Join(",", new[]
            {
                r.Start.ToString("yyyy-MM-dd", Inv), r.Start.ToString("HH:mm:ss", Inv), r.End.ToString("HH:mm:ss", Inv),
                Math.Round(r.Minutes, 1).ToString(Inv), r.Id ?? "", Csv(r.Title), r.Effort ?? "", r.Kind ?? "mental", r.Routine ? "yes" : "no",
                r.Planned.ToString(Inv), Pct(r.E0), Pct(r.E1), Pct(r.Felt), r.Ended ?? "", Habits.PartNames[Habits.Part(r.Start)]
            });
        }

        public static bool Parse(string line, out WorkRow r)
        {
            r = null;
            var f = TaskLog.Split(line); DateTime day; TimeSpan a, b; double mins; int planned;
            if (f.Count < 15 || !DateTime.TryParseExact(f[0], "yyyy-MM-dd", Inv, DateTimeStyles.None, out day)) return false;
            if (!TimeSpan.TryParseExact(f[1], @"hh\:mm\:ss", Inv, out a) || !TimeSpan.TryParseExact(f[2], @"hh\:mm\:ss", Inv, out b)) return false;
            double.TryParse(f[3], NumberStyles.Float, Inv, out mins);
            int.TryParse(f[9], NumberStyles.Integer, Inv, out planned);
            r = new WorkRow
            {
                Start = day + a, End = day + b + (b < a ? TimeSpan.FromDays(1) : TimeSpan.Zero), Minutes = mins, Id = f[4], Title = f[5],
                Effort = f[6].Length == 0 ? null : f[6], Kind = f[7], Routine = f[8] == "yes", Planned = planned,
                E0 = Num(f[10]), E1 = Num(f[11]), Felt = Num(f[12]), Ended = f[13]
            };
            return true;
        }

        public static List<WorkRow> Read()
        {
            var list = new List<WorkRow>();
            try
            {
                if (!IO.File.Exists(File)) return list;
                foreach (var line in IO.File.ReadAllLines(File).Skip(1)) { WorkRow r; if (Parse(line, out r)) list.Add(r); }
            }
            catch (Exception ex) { Store.Log(ex); }
            return list;
        }

        public static void Add(WorkRow r)
        {
            Rows.Add(r);
            try
            {
                bool fresh = !IO.File.Exists(File);
                IO.File.AppendAllText(File, (fresh ? Head + Environment.NewLine : "") + Line(r) + Environment.NewLine, new UTF8Encoding(fresh));
            }
            catch (Exception ex) { Store.Log(ex); }
        }
    }

    // ---------------------------------------------------------------- what you usually do, and when

    public static class Habits
    {
        public static readonly string[] PartNames = { "morning", "noon", "afternoon", "evening" };
        public const int MinDays = 3;        // done in the same part of the day on at least this many days...
        public const int WindowDays = 28;    // ...of the last this-many, to count as usual
        public const double MinMinutes = 1;  // a stretch shorter than this is not counted

        /// 0 morning (to 12:00), 1 noon (to 14:00), 2 afternoon (to 18:00), 3 evening (and the small hours).
        public static int Part(DateTime t)
        {
            double h = t.TimeOfDay.TotalHours;
            return h < 4 ? 3 : h < 12 ? 0 : h < 14 ? 1 : h < 18 ? 2 : 3;
        }

        /// What makes two tasks "the same thing": the title, without case, spare spaces or a "(1/2)" at its end.
        public static string Key(string title)
        {
            string s = Regex.Replace(title ?? "", @"\s*\(\d+/\d+\)\s*$", "");
            return Regex.Replace(s, @"\s+", " ").Trim().ToLowerInvariant();
        }

        public class Habit { public string Title, Effort, Kind; public int Minutes, Days; public bool Routine; }

        /// The work log and, from done.csv, the tasks ticked off without a timer: ticking a task off is doing it too.
        /// A tick is left out when the same task has timed work on the same day (that work already marks the day).
        /// A tick from before work.csv existed that has timer minutes of its own keeps them.
        public static List<WorkRow> WithDone(IEnumerable<WorkRow> work, IEnumerable<TaskLog.Row> done)
        {
            var all = new List<WorkRow>(work ?? new WorkRow[0]);
            var timed = new HashSet<string>(all.Where(r => r.Minutes >= MinMinutes).Select(r => r.Id + "|" + SleepLog.Today(r.Start).ToString("yyyyMMdd")));
            var inWork = new HashSet<string>(all.Select(r => r.Id));
            foreach (var d in done ?? new TaskLog.Row[0])
            {
                if (timed.Contains(d.Id + "|" + d.Day.ToString("yyyyMMdd"))) continue;
                var at = d.At.TimeOfDay.TotalHours < SleepLog.DayStartsAt ? d.At.AddDays(1) : d.At; // the small hours belong to the day before (done.csv)
                bool had = d.TimerMinutes >= MinMinutes && !inWork.Contains(d.Id); // (a task in work.csv has its minutes there, on the days they were worked)
                all.Add(new WorkRow
                {
                    Start = had ? at.AddMinutes(-d.TimerMinutes) : at, End = at, Minutes = had ? d.TimerMinutes : 0, Id = d.Id, Title = d.Title,
                    Effort = string.IsNullOrEmpty(d.Effort) ? null : d.Effort, Kind = d.Kind, Routine = d.Routine, Planned = d.Minutes, Ended = "completed", Ticked = !had
                });
            }
            return all;
        }

        static List<WorkRow> log; static int logWork = -1, logDone = -1;
        /// What was worked on and what was ticked off, together. Made again only when one of the two logs has changed.
        public static List<WorkRow> Log
        {
            get
            {
                if (log == null || logWork != WorkLog.Rows.Count || logDone != TaskLog.Changes)
                {
                    logWork = WorkLog.Rows.Count; logDone = TaskLog.Changes;
                    log = WithDone(WorkLog.Rows, TaskLog.ReadDone());
                }
                return log;
            }
        }

        /// The minutes a row stands for: its own, or for a tick with no timer the length the task was given.
        static double Weight(WorkRow r) { return r.Ticked ? (r.Planned > 0 ? r.Planned : Planner.DefaultMinutes) : r.Minutes; }

        static IEnumerable<WorkRow> Window(IEnumerable<WorkRow> rows, int part, DateTime today)
        {
            return rows.Where(r => (r.Minutes >= MinMinutes || r.Ticked) && Part(r.Start) == part && r.Start.Date < today.Date && r.Start.Date >= today.Date.AddDays(-WindowDays));
        }

        /// The things usually done in this part of the day, most regular first. A habit's length is the middle one of
        /// the minutes it really took on the days it was timed, to the nearest five; never timed, it is the length it was given.
        public static List<Habit> Usual(IEnumerable<WorkRow> rows, int part, DateTime today)
        {
            var list = new List<Habit>();
            foreach (var g in Window(rows, part, today).GroupBy(r => Key(r.Title)))
            {
                var days = g.GroupBy(r => r.Start.Date).Select(d => d.Sum(r => r.Minutes)).ToList();
                if (g.Key.Length == 0 || days.Count < MinDays) continue;
                var last = g.OrderBy(r => r.Start).Last();
                var timed = days.Where(m => m >= MinMinutes).OrderBy(m => m).ToList();
                double mid = timed.Count > 0 ? timed[timed.Count / 2] : Weight(last);
                int mins = (int)Math.Max(5, Math.Round(mid / 5.0) * 5);
                list.Add(new Habit { Title = Regex.Replace(last.Title, @"\s*\(\d+/\d+\)\s*$", ""), Effort = last.Effort, Kind = last.Kind, Minutes = mins, Days = days.Count, Routine = last.Routine });
            }
            return list.OrderByDescending(h => h.Days).ThenBy(h => h.Title).ToList();
        }

        /// How much of this part of the day's logged minutes went to work of this effort and kind, 0..1.
        public static double TypeShare(IEnumerable<WorkRow> rows, int part, DateTime today, string effort, string kind)
        {
            var w = Window(rows, part, today).ToList();
            double all = w.Sum(r => Weight(r));
            if (all < 30) return 0; // too little to say
            bool phys = kind == "physical";
            return w.Where(r => r.Effort == effort && (r.Kind == "physical") == phys).Sum(r => Weight(r)) / all;
        }
    }

    // ---------------------------------------------------------------- the order to do things in, for your energy now

    public class Ranked
    {
        public TaskItem Task;
        public double Score;
        public string Why;   // a few words on why it stands where it does, or null
        public bool Later;   // dated for a later day: after everything else, as it was
    }

    public static class Priority
    {
        public const double LowLevel = 0.35, HighLevel = 0.70; // the energy counter, 0..1: under and over these it has a say too
        public const int SoonMin = 60;                         // a time you set yourself counts as "due" this long before it

        public static Zone ZoneAt(EnergyDay e, DateTime now)
        {
            if (e == null) return null;
            foreach (var z in new[] { e.Peak1, e.Dip, e.Peak2 }) if (z != null && now >= z.From && now < z.To) return z;
            return null;
        }

        /// The open tasks, best first for this moment. `level` is the energy counter (0..1, or below 0 when not known);
        /// `rows` is the work log; `working` is the task being worked on, which stays first.
        public static List<Ranked> Rank(IList<TaskItem> tasks, EnergyDay e, DateTime now, double level, IList<WorkRow> rows, string working)
        {
            int part = Habits.Part(now);
            rows = rows ?? new List<WorkRow>();
            var usual = new HashSet<string>(Habits.Usual(rows, part, now).Select(h => Habits.Key(h.Title)));
            var zone = ZoneAt(e, now);
            bool waking = e != null && now >= e.Wake && now < e.GroggyUntil; // the groggy stretch after waking: Plan keeps it for light work, and so does this
            var list = new List<Ranked>(); var at = new Dictionary<Ranked, int>(); int i = 0;
            foreach (var t in tasks)
            {
                i++;
                if (t.Done) continue;
                var r = One(t, zone, waking, now, level, rows, part, usual);
                if (t.Id == working) { r.Score = 1000; r.Why = null; }
                at[r] = i; list.Add(r);
            }
            return list.OrderBy(r => r.Later ? 1 : 0).ThenByDescending(r => r.Later ? 0 : r.Score).ThenBy(r => at[r]).ToList();
        }

        static Ranked One(TaskItem t, Zone zone, bool waking, DateTime now, double level, IList<WorkRow> rows, int part, HashSet<string> usual)
        {
            var r = new Ranked { Task = t };
            var due = t.DueAt;
            if (due != null && due.Value.Date > now.Date) { r.Later = true; return r; }
            double score = 0, best = 0; string why = null;
            Action<double, string> add = (v, text) => { score += v; if (text != null && v > best) { best = v; why = text; } };

            string effort = TaskTags.Level(t.Energy);
            int mins = t.Minutes > 0 ? t.Minutes : Planner.DefaultMinutes;
            bool peak = zone != null && zone.Peak, dip = zone != null && !zone.Peak;
            if (t.Kind == "physical")
            {   // physical work has its own time of day, whatever the curve is doing
                double h = now.TimeOfDay.TotalHours;
                if (h >= Planner.PhysicalFrom && h < Planner.PhysicalTo) add(2.5, "good time for physical work");
            }
            else if (waking)
            {   // still waking up: something light first, and the hard thing waits for the peak (as Plan places them)
                if (effort == "low") add(2, "easy start while you wake up");
                else if (effort == "medium") add(0.5, null);
                else if (effort == "high") add(-1, null);
            }
            else
            {
                double fit = 0;
                if (effort == "high") { fit = peak ? 3 : dip ? -2 : 0.5; add(fit, peak ? "fits your peak" : null); }
                else if (effort == "medium") { fit = dip ? 0 : 1.5; add(fit, peak || dip ? null : "fits your energy now"); }
                else if (effort == "low") { fit = dip ? 3 : peak ? -1 : 1; add(fit, dip ? "light work for the dip" : null); }
                if (zone != null && fit >= 1.5)
                {   // and does it fit in what is left of the peak or the dip?
                    double left = (zone.To - now).TotalMinutes;
                    if (mins <= left + 5) add(1, null);
                    else if (mins > left + 15)
                    {   // it runs well past the end: still the best time for it, but the reason says how long the peak lasts
                        add(-1, null);
                        if (peak && why == "fits your peak") why = "your peak lasts until " + EnergyDay.Clock(zone.To);
                    }
                }
                if (level >= 0 && level < LowLevel) { if (effort == "low") add(1.5, "low energy now"); else if (effort == "high") add(-1.5, null); }
                if (level > HighLevel && effort == "high") add(1, "high energy now");
            }

            if (due != null && t.HasTime && !t.Planned)
            {   // a time you set yourself (a time an old schedule gave it does not count)
                if (due.Value <= now) add(6, due.Value.Date == now.Date ? "due at " + EnergyDay.Clock(due.Value) : "due");
                else if (due.Value <= now.AddMinutes(SoonMin)) add(5, "due at " + EnergyDay.Clock(due.Value));
            }
            else if (due != null && !t.HasTime)
            {
                if (due.Value.Date < now.Date) add(3, "due"); else add(1, "due today");
            }

            if (usual.Contains(Habits.Key(t.Title))) add(1.5, "usual for your " + Habits.PartNames[part]);
            else add(Habits.TypeShare(rows, part, now, effort, t.Kind), null);

            r.Score = score; r.Why = why;
            return r;
        }
    }

    // ---------------------------------------------------------------- Shell: the stopwatch, the log, the order, the chips

    partial class Shell
    {
        const double AwayIdleSec = 600; // no key or mouse for this long pauses the task being worked on
        const double TickGapSec = 90;   // the once-a-second tick missing for this long: the PC was asleep, and the work stopped when it was last seen

        static bool PriorityMode { get { return Settings.Get("PlanBy", 0) > 0.5; } }
        bool ShowWatch { get { return Settings.Get("Stopwatch", 1) > 0.5; } }

        string workId;          // the task whose stretch of work is open
        DateTime workStart;
        DateTime workSeen;      // the last second the open stretch was seen going on (WorkAwake)
        double workE0 = -1, workFelt = -1;
        string workWhy;         // why the open stretch is about to end, when the code that ends it knows
        Dictionary<string, string> rankWhy = new Dictionary<string, string>(); // from the last ordering: task id -> why

        void InitWork()
        {
            Application.Current.Exit += (s, e) => { if (workId != null) { WorkEnd("closed", DateTime.Now); Save(); } };
            try
            {
                // (the time is read as the event arrives: by the time the window's thread gets to it, the PC may have slept and woken)
                Microsoft.Win32.SystemEvents.SessionSwitch += (s, e) => { if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) { var at = DateTime.Now; Dispatcher.BeginInvoke(new Action(() => AutoPause(at))); } };
                Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == Microsoft.Win32.PowerModes.Suspend) { var at = DateTime.Now; Dispatcher.BeginInvoke(new Action(() => AutoPause(at))); } };
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// The energy counter's number now, as the panel shows it; -1 when there is no curve.
        double EnergyPct(DateTime now)
        {
            if (energy == null) return -1;
            return Math.Max(1, Math.Min(100, Math.Round(Calibration.Map(energy.AllAt(now), now) * 100)));
        }

        /// Seconds worked on a task so far, the open stretch included.
        double Elapsed(TaskItem t) { return t.Worked + (workId == t.Id ? Math.Max(0, (DateTime.Now - workStart).TotalSeconds) : 0); }

        /// Keeps the log in step with the timer: called after anything starts, pauses or stops it, and once a second.
        void WorkSync()
        {
            string want = timerRunning ? timerId : null;
            if (want == workId) { workWhy = null; return; }
            if (workId != null) WorkEnd(workWhy ?? (want != null ? "switched" : "paused"), DateTime.Now);
            workWhy = null;
            if (want != null) { workId = want; workStart = workSeen = DateTime.Now; workE0 = EnergyPct(Now()); workFelt = -1; }
        }

        WorkRow RowFor(TaskItem t, DateTime start, DateTime end, string ended)
        {
            return new WorkRow
            {
                Start = start, End = end, Minutes = (end - start).TotalMinutes, Id = t.Id, Title = t.Title, Effort = TaskTags.Level(t.Energy),
                Kind = t.Kind ?? "mental", Routine = t.Routine != null, Planned = DurationMin(t), Ended = ended
            };
        }

        /// Closes the open stretch and writes its row. A stretch of a few seconds (a slip of the hand) is dropped.
        void WorkEnd(string ended, DateTime end)
        {
            string id = workId; workId = null;
            var t = data == null ? null : data.Tasks.FirstOrDefault(x => x.Id == id);
            if (t == null) return;
            if (end < workStart) end = workStart;
            double secs = (end - workStart).TotalSeconds;
            if (secs < 5 && ended != "completed") return;
            t.Worked += secs;
            var r = RowFor(t, workStart, end, ended);
            r.E0 = workE0; r.E1 = EnergyPct(Now().AddSeconds(-(DateTime.Now - end).TotalSeconds)); r.Felt = workFelt;
            WorkLog.Add(r);
        }

        /// From TaskCompleted, before the timer is stopped: the row that ends here says "completed". A task finished
        /// while paused gets a row of no length, so the log still has the moment it was completed.
        void WorkCompleted(TaskItem t)
        {
            if (workId == t.Id) { workWhy = "completed"; return; }
            if (t.Worked <= 0) return;
            var now = DateTime.Now; var r = RowFor(t, now, now, "completed");
            r.E0 = r.E1 = EnergyPct(Now());
            WorkLog.Add(r);
        }

        /// The PC locked or went to sleep, or sat untouched for ten minutes: the task being worked on is paused, and the
        /// stretch ends when you left, not when Corner noticed.
        void AutoPause(DateTime end)
        {
            if (workId == null) return;
            if (end < workStart) end = workStart;
            WorkEnd("away", end);
            if (PriorityMode) { timerRunning = false; timerLeft = 0; ResortData(); } // (the task stays in hand, paused: Focus.cs TimerClick)
            else { timerLeft = Math.Max(0, (timerEnd - end).TotalSeconds); timerRunning = false; } // the countdown stops where it stood when you left
            Save();
            if (shown) { Rebuild(); RepaintTimers(); DrawFocus(); }
        }

        /// Once a second, before the timer is looked at (Focus.cs). The PC can go to sleep or hibernate without Corner
        /// being told in time; the ticks then simply stop. A long gap between two ticks means the work ended at the
        /// last one, so a night's sleep is never logged as work and never runs a countdown out.
        void WorkAwake()
        {
            if (workId == null) return;
            var now = DateTime.Now;
            if ((now - workSeen).TotalSeconds > TickGapSec && workSeen >= workStart) { AutoPause(workSeen); return; }
            workSeen = now;
        }

        /// Once a second (Focus.cs).
        void WorkTick()
        {
            WorkSync();
            if (workId == null || demoRun) return;
            double idle = IdleSeconds();
            if (idle >= AwayIdleSec) AutoPause(DateTime.Now.AddSeconds(-idle));
        }

        // ------------------------------------------------------------ the order

        static double DayKey(DateTime d) { return d.Year * 10000 + d.Month * 100 + d.Day; }
        /// A priority plan was applied today: the strip stands beside its tasks (FullLayout.cs).
        bool OrderToday { get { return PriorityMode && Settings.Get("OrderDay", 0) == DayKey(SleepLog.Today(Now())); } }

        List<Ranked> RankNow()
        {
            var now = Now(); double pct = EnergyPct(now);
            return Priority.Rank(data.Tasks, energy, now, pct < 0 ? -1 : pct / 100, Habits.Log, timerId); // the task in hand stays first, running or paused
        }

        /// Priority mode: puts the open tasks in the order to do them now. Only the data; the caller redraws.
        /// Called when a plan is applied and when a task is paused or completed, and at no other time.
        void ResortData()
        {
            if (!PriorityMode || data == null) return;
            var ranked = RankNow();
            rankWhy = ranked.Where(r => r.Why != null).ToDictionary(r => r.Task.Id, r => r.Why);
            var done = data.Tasks.Where(t => t.Done).ToList();
            data.Tasks.Clear(); data.Tasks.AddRange(ranked.Select(r => r.Task)); data.Tasks.AddRange(done);
        }

        List<Ranked> orderProposal;  // shown in the plan card, not yet applied
        List<string> orderUndo;      // the list order the applied one replaced

        void MakeOrder() { orderProposal = RankNow().Where(r => !r.Later).ToList(); DrawEnergy(); }

        void ApplyOrder()
        {
            if (orderProposal == null) return;
            if (commitEdit != null) commitEdit();
            orderUndo = data.Tasks.Select(t => t.Id).ToList();
            orderProposal = null;
            ResortData();
            Settings.Set("OrderDay", DayKey(SleepLog.Today(Now())));
            Save(); Rebuild(); DrawFocus(); DrawEnergy();
        }

        void UndoOrder()
        {
            Settings.Set("OrderDay", 0);
            if (orderUndo != null)
            {
                var was = orderUndo; orderUndo = null;
                var sorted = data.Tasks.Select((t, i) => new { t, i, k = was.IndexOf(t.Id) }).OrderBy(x => x.k < 0 ? int.MaxValue : x.k).ThenBy(x => x.i).Select(x => x.t).ToList();
                data.Tasks.Clear(); data.Tasks.AddRange(sorted);
            }
            rankWhy.Clear();
            Save(); Rebuild(); DrawFocus(); DrawEnergy();
        }

        /// The plan card in Priority mode (from DrawPlan, UiEnergy.cs): what comes after what, each with its reason.
        void DrawOrderPlan()
        {
            if (orderProposal != null) { var cap = Caps("Proposed order"); cap.Margin = new Thickness(0, 0, 0, 6); sect.Children.Add(cap); }
            var head = new Grid { Margin = new Thickness(0, 0, 0, orderProposal != null ? 6 : 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new TextBlock { Text = "Puts today's tasks in order for your energy now", FontSize = Z(11.5), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Action<FrameworkElement> put = b => { b.Margin = new Thickness(4, 0, 0, 0); acts.Children.Add(b); };
            if (orderProposal == null)
            {
                if (OrderToday) put(Btn("Undo plan", 2, UndoOrder, 1));
                put(Btn("Plan my day", 0, MakeOrder, 1));
            }
            else
            {
                put(Btn("Discard", 2, () => { orderProposal = null; DrawEnergy(); }, 1));
                if (orderProposal.Count > 0) put(Btn("Apply", 0, ApplyOrder, 1));
            }
            Grid.SetColumn(acts, 1); head.Children.Add(acts);
            sect.Children.Add(head);
            if (orderProposal == null) return;

            if (orderProposal.Count == 0)
                sect.Children.Add(new TextBlock { Text = "Nothing to order yet: add a few tasks first, with an effort level (!high, !med, !low) and a length (45m).", FontSize = Z(12), Foreground = T.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            double numW = TextW("00", Z(11.5), SemiSmall) + 8;
            for (int i = 0; i < orderProposal.Count; i++)
            {
                var it = orderProposal[i];
                var g = new Grid { Margin = new Thickness(0, 3, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(numW) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = Z(11.5), FontWeight = SemiSmall, Foreground = T.Text, VerticalAlignment = VerticalAlignment.Center });
                var title = new TextBlock { Text = it.Task.Title, FontSize = Z(12), Foreground = T.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(title, 1); g.Children.Add(title);
                var bits = new List<string>();
                if (it.Why != null) bits.Add(it.Why);
                else { string lv = TaskTags.Level(it.Task.Energy); if (lv != null) bits.Add(TaskTags.Short(lv)); bits.Add(TaskTags.Len(DurationMin(it.Task))); }
                var tag = new TextBlock { Text = string.Join(" · ", bits), FontSize = Z(10.5), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumn(tag, 2); g.Children.Add(tag);
                sect.Children.Add(g);
            }
        }

        /// Settings: plan by schedule (0) or by priority (1). A running timer is stopped first, and logged.
        void SetPlanBy(int v)
        {
            StopTimer();
            if (alertKind != 0) ClearAlert();
            Settings.Set("PlanBy", v);
            proposal = null; orderProposal = null; rankWhy.Clear();
            Rebuild(); DrawFocus();
            if (energy != null && (tab == 1 || full)) DrawEnergy();
        }

        // ------------------------------------------------------------ "Usually this morning": one-click chips under the add box

        Border addPill;      // the box you type a task into
        WrapPanel habitRow;  // and, under it, what you usually do at this time of day
        List<Habits.Habit> habitCache; List<WorkRow> habitLog; int habitPart = -1; DateTime habitDay;

        /// From Rebuild. The things the log says you usually do in this part of the day and that are not in the list yet.
        void DrawHabits()
        {
            if (habitRow == null || data == null) return;
            var now = Now(); int part = Habits.Part(now);
            var logNow = Habits.Log; // timed work and ticked-off tasks; a new list whenever either log has changed
            if (habitCache == null || habitLog != logNow || habitPart != part || habitDay != now.Date)
            {
                habitCache = Habits.Usual(logNow, part, now);
                habitLog = logNow; habitPart = part; habitDay = now.Date;
            }
            habitRow.Children.Clear();
            var have = new HashSet<string>(data.Tasks.Select(t => Habits.Key(t.Title)));
            var offer = habitCache.Where(h => !have.Contains(Habits.Key(h.Title))).Take(Slim ? 3 : 4).ToList();
            habitRow.Visibility = offer.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (offer.Count == 0) return;
            habitRow.Children.Add(new TextBlock { Text = "Usually this " + Habits.PartNames[part] + ":", FontSize = Z(11), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4) });
            foreach (var h in offer)
            {
                var hb = h;
                var label = new TextBlock { Text = h.Title, FontSize = Z(11), Foreground = T.Text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 };
                var chip = new PillBorder { Background = T.Field, Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 4, 4), Child = label, Cursor = Cursors.Hand, ToolTip = "Add to today · " + TaskTags.Len(h.Minutes) + (h.Effort != null ? " · " + TaskTags.Short(h.Effort) : "") + " · on " + h.Days + " days lately" };
                chip.MouseEnter += (s, e) => chip.Background = T.Lift;
                chip.MouseLeave += (s, e) => chip.Background = T.Field;
                chip.MouseLeftButtonDown += (s, e) => { e.Handled = true; AddHabit(hb); };
                habitRow.Children.Add(chip);
            }
        }

        void AddHabit(Habits.Habit h)
        {
            var rt = Routines.Data == null ? null : Routines.Data.Routines.FirstOrDefault(r => Habits.Key(r.Title) == Habits.Key(h.Title));
            if (rt != null && !RoutineInToday(rt)) { AddRoutinesToToday(new[] { rt }); return; }
            flashId = Guid.NewGuid().ToString("N");
            data.Tasks.Insert(0, new TaskItem { Id = flashId, Title = h.Title, Energy = TaskTags.Level(h.Effort), Kind = h.Kind == "physical" ? "physical" : null, Minutes = h.Minutes });
            Save(); Rebuild(); DrawFocus();
        }

        // ------------------------------------------------------------ Settings

        void DrawPlanningSettings()
        {
            setCol.Children.Add(SetCaps("Planning"));
            var card = SetCard(setCol);
            bool prio = PriorityMode, watch = ShowWatch;
            SetLine(card, SettingLine("Plan by", prio ? "What to do next, in order, for your energy now. No clock times." : "A clock time for each task, and a countdown.",
                Segmented(new[] { "Schedule", "Priority" }, prio ? 1 : 0, i => { SetPlanBy(i); DrawSettings(); })));
            if (prio) SetLine(card, SettingLine("Show stopwatch", "The time on a task is logged either way", Switch(watch, () => { Settings.Set("Stopwatch", watch ? 0 : 1); Rebuild(); DrawFocus(); DrawSettings(); })));
            int n = WorkLog.Rows.Count;
            SetLine(card, SettingLine("Work log", (n == 0 ? "Nothing yet. " : n + (n == 1 ? " stretch" : " stretches") + " of work. ") + "work.csv, beside Corner. It opens in Excel.", Btn("Show", 1, () =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", IO.File.Exists(WorkLog.File) ? "/select,\"" + WorkLog.File + "\"" : "\"" + Store.Dir + "\""); }
                catch (Exception ex) { Store.Log(ex); }
            }, 1)));
        }

        // ------------------------------------------------------------ test hook: modes "w:all+work+plan" and "realw:…"

        void DemoWork(string mode)
        {
            int at = mode.IndexOf("w:", StringComparison.Ordinal);
            if (at < 0 || (at > 0 && mode.Substring(0, at) != "real")) return;
            foreach (var step in mode.Substring(at + 2).Split('+'))
            {
                if (step == "all") { allOpen = true; FocusLayout(); }
                if (step == "add") { addOpen = true; FocusLayout(); }
                if (step == "sort") { ResortData(); Rebuild(); DrawFocus(); }
                if (step == "work" || step == "over") { StartTimer(Upcoming()[0]); workStart = workStart.AddSeconds(step == "over" ? -4000 : -754); Rebuild(); RepaintTimers(); DrawFocus(); }
                if (step == "pause") { TimerClick(Upcoming()[0]); }
                if (step == "slept")
                {   // started 1000 s ago, last seen going on 400 s in, then no tick until now: the PC was asleep (WorkAwake)
                    StartTimer(Upcoming()[0]); workStart = workStart.AddSeconds(-1000); if (!PriorityMode) timerEnd = timerEnd.AddSeconds(-1000);
                    workSeen = workStart.AddSeconds(400); WorkAwake(); Rebuild(); RepaintTimers(); DrawFocus();
                }
                if (step == "plan") { SetTab(1); MakePlan(); }
                if (step == "apply") { MakePlan(); if (PriorityMode) ApplyOrder(); else ApplyPlan(); SetTab(0); }
                if (step == "settings") After(1200, ToggleSettings); // (after Full Mode has opened)
                if (step.StartsWith("skin") && step.Length == 5) { int to = step[4] - '0'; After(2000, () => SetSkin(to)); } // a change of skin, once everything is still
            }
        }
    }
}
