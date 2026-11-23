// Corner - task energy tags, daily routines and the Plan action.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IO = System.IO;

namespace Corner
{
    // ---------------------------------------------------------------- tags typed into a task or routine

    public static class TaskTags
    {
        static readonly RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex EnergyRx = new Regex(@"(?:^|\s)!(high|medium|med|low|deep|light|h|m|l)(?=\s|$)", Opt);
        static readonly Regex KindRx = new Regex(@"(?:^|\s)!(physical|phys|mental)(?=\s|$)", Opt);
        // "45m", "20min", "2h" as a word of its own; not after "in" ("in 20m" is still a reminder time)
        static readonly Regex LenRx = new Regex(@"(?<!\bin)(?:^|\s)(\d{1,3})(m|min|mins|h|hr|hrs)(?=\s|$)", Opt);

        /// "high" / "medium" / "low" / null. Also understands the short forms and the older deep/light.
        public static string Level(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "high": case "h": case "deep": return "high";
                case "medium": case "med": case "m": return "medium";
                case "low": case "l": case "light": return "low";
                default: return null;
            }
        }

        public static string Short(string level) { return level == "medium" ? "med" : level; }
        public static string Len(int minutes) { return minutes % 60 == 0 ? minutes / 60 + "h" : minutes + "m"; }

        public static string Extract(string text, out string energy, out int minutes)
        {
            string kind;
            return Extract(text, out energy, out minutes, out kind);
        }

        /// Pulls "!high" / "!med" / "!low", "!phys" / "!mental" and a length like "45m" out of the text; returns the rest.
        public static string Extract(string text, out string energy, out int minutes, out string kind)
        {
            energy = null; minutes = 0; kind = null;
            string s = text ?? "", en = null, kd = null; int mins = 0;
            var m = EnergyRx.Match(s);
            if (m.Success) { en = Level(m.Groups[1].Value); s = EnergyRx.Replace(s, " "); }
            m = KindRx.Match(s);
            if (m.Success) { kd = char.ToLowerInvariant(m.Groups[1].Value[0]) == 'p' ? "physical" : "mental"; s = KindRx.Replace(s, " "); }
            m = LenRx.Match(s);
            if (m.Success)
            {
                int n = int.Parse(m.Groups[1].Value);
                mins = char.ToLowerInvariant(m.Groups[2].Value[0]) == 'h' ? n * 60 : n;
                if (mins < 1 || mins > 600) mins = 0; else s = LenRx.Replace(s, " ", 1);
            }
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length == 0) return (text ?? "").Trim(); // nothing but tags: treat it as plain text
            energy = en; minutes = mins; kind = kd;
            return s;
        }
    }

    // ---------------------------------------------------------------- daily routines (routines.json, beside tasks.json)

    [DataContract]
    public class Routine
    {
        [DataMember] public string Id;
        [DataMember] public string Title;
        [DataMember] public string Energy;   // "low", "medium", "high" or null
        [DataMember] public string Kind;     // "physical", or null for mental
        [DataMember] public int Minutes;
        [DataMember] public string Days;     // "daily", "weekdays", "weekends" or "mon,wed,fri"
        [DataMember] public string After;    // Id of a routine that must come earlier the same day
        [DataMember] public int CheckIns;    // more than 1 = that many short check-ins spread over the day
        [DataMember] public string Prefer;   // "early" (first thing) or "dip" (the afternoon dip); null = by energy level
        [DataMember(EmitDefaultValue = false)] public bool Off; // switched off: kept in the list but not added to the day
    }

    [DataContract]
    public class RoutineData
    {
        [DataMember] public string AddedOn;  // the day today's routines were last added to the task list
        [DataMember] public List<Routine> Routines;
    }

    public static class Routines
    {
        public static RoutineData Data;
        static string File { get { return IO.Path.Combine(Store.Dir, "routines.json"); } }

        /// Reads routines.json; on the very first run writes the starting set.
        public static void Load()
        {
            try
            {
                if (IO.File.Exists(File))
                {
                    using (var fs = IO.File.OpenRead(File))
                    {
                        var d = (RoutineData)new DataContractJsonSerializer(typeof(RoutineData)).ReadObject(fs);
                        if (d != null)
                        {
                            if (d.Routines == null) d.Routines = new List<Routine>();
                            d.Routines.RemoveAll(r => r == null || string.IsNullOrEmpty(r.Id) || string.IsNullOrEmpty(r.Title));
                            Data = d;
                            return;
                        }
                    }
                }
            }
            catch (Exception ex) { Store.Log(ex); if (Data != null) return; }
            if (Data == null) { Data = new RoutineData { Routines = Seed() }; if (!IO.File.Exists(File)) Save(); }
        }

        public static void Save()
        {
            try
            {
                string tmp = File + ".tmp";
                using (var fs = IO.File.Create(tmp)) new DataContractJsonSerializer(typeof(RoutineData)).WriteObject(fs, Data);
                if (IO.File.Exists(File)) Store.ReplaceSafely(tmp, File); else IO.File.Move(tmp, File);
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        static List<Routine> Seed()
        {
            return new List<Routine>
            {
            };
        }

        /// Adds the optional "Nap" routine once, switched off. Deleting it afterwards is respected.
        public static void OfferNap()
        {
            if (Data == null || Settings.Get("NapRoutine", 0) > 0.5) return;
            if (!Data.Routines.Any(r => r.Id == "nap"))
            {
                Data.Routines.Add(new Routine { Id = "nap", Title = "Nap", Minutes = 20, Energy = "low", Days = "daily", Prefer = "dip", Off = true });
                Save();
            }
            Settings.Set("NapRoutine", 1);
        }

        static readonly string[] DayNames = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

        public static bool AppliesOn(Routine r, DateTime day)
        {
            string d = (r.Days ?? "daily").ToLowerInvariant();
            bool weekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday;
            if (d == "daily" || d == "") return true;
            if (d == "weekdays") return !weekend;
            if (d == "weekends") return weekend;
            return d.Split(',').Contains(DayNames[(int)day.DayOfWeek]);
        }

        static bool Take(ref string s, string pattern, out Match m)
        {
            m = Regex.Match(s, pattern);
            if (m.Success) s = s.Remove(m.Index, m.Length).Insert(m.Index, " ");
            return m.Success;
        }

        /// Fills a routine from one typed line, e.g. "Workout 20m !high !phys daily".
        /// Anything not mentioned goes back to its default.
        public static void Apply(Routine r, string text, IList<Routine> all)
        {
            string s = " " + (text ?? "") + " "; Match m;
            Take(ref s, @"\s!noclinic(?=\s)", out m); // an old tag: dropped
            r.Off = Take(ref s, @"\s!off(?=\s)", out m);
            r.Prefer = Take(ref s, @"\s!early(?=\s)", out m) ? "early" : Take(ref s, @"\s!dip(?=\s)", out m) ? "dip" : null;
            r.After = null;
            if (Take(ref s, @"\safter[\s:]*#?(\d{1,2})(?=\s)", out m))
            {
                int i = int.Parse(m.Groups[1].Value) - 1;
                if (i >= 0 && i < all.Count && all[i] != r) r.After = all[i].Id;
            }
            r.CheckIns = Take(ref s, @"\sx(\d)(?=\s)", out m) ? int.Parse(m.Groups[1].Value) : 0;
            r.Days = Take(ref s, @"\s(daily|weekdays|weekends|(?:mon|tue|wed|thu|fri|sat|sun)(?:,(?:mon|tue|wed|thu|fri|sat|sun))*)(?=\s)", out m) ? m.Groups[1].Value : "daily";
            string en, kind; int mins;
            r.Title = TaskTags.Extract(s, out en, out mins, out kind);
            r.Energy = en; r.Minutes = mins > 0 ? mins : Planner.DefaultMinutes; r.Kind = kind == "physical" ? kind : null;
        }

        /// The typed line that Apply would turn back into this routine.
        public static string Format(Routine r, IList<Routine> all)
        {
            var p = new List<string> { r.Title, TaskTags.Len(r.Minutes > 0 ? r.Minutes : Planner.DefaultMinutes) };
            if (TaskTags.Level(r.Energy) != null) p.Add("!" + TaskTags.Short(TaskTags.Level(r.Energy)));
            if (r.Kind == "physical") p.Add("!phys");
            p.Add(string.IsNullOrEmpty(r.Days) ? "daily" : r.Days);
            int a = IndexOf(all, r.After);
            if (a >= 0) p.Add("after " + (a + 1));
            if (r.CheckIns > 1) p.Add("x" + r.CheckIns);
            if (r.Prefer != null) p.Add("!" + r.Prefer);
            if (r.Off) p.Add("!off");
            return string.Join(" ", p);
        }

        public static string Summary(Routine r, IList<Routine> all)
        {
            var p = new List<string> { TaskTags.Len(r.Minutes > 0 ? r.Minutes : Planner.DefaultMinutes) };
            if (TaskTags.Level(r.Energy) != null) p.Add(TaskTags.Short(TaskTags.Level(r.Energy)));
            if (r.Kind == "physical") p.Add("physical");
            p.Add(string.IsNullOrEmpty(r.Days) ? "daily" : r.Days);
            int a = IndexOf(all, r.After);
            if (a >= 0) p.Add("after " + (a + 1));
            if (r.CheckIns > 1) p.Add(r.CheckIns + " check-ins");
            if (r.Prefer == "early") p.Add("first thing");
            if (r.Prefer == "dip") p.Add("in the dip");
            if (r.Off) p.Add("off");
            return string.Join(" · ", p);
        }

        static int IndexOf(IList<Routine> all, string id)
        {
            if (id != null) for (int i = 0; i < all.Count; i++) if (all[i].Id == id) return i;
            return -1;
        }
    }

    // ---------------------------------------------------------------- Plan my day

    public class PlanItem
    {
        public string Id, Title, Level, Routine, After, Prefer;
        public int Minutes, Part;
        public bool Physical, CheckIn, Placed;
        public DateTime Start;
        internal int End;
        internal bool Tried;
        public int SplitOf;                 // 1 or 2: this is that session of a task split across both peaks (0 = not split)
        public bool CanSplit;               // a high task that misses the first peak as one session, but would fit as two
        public int SplitA, SplitB;          // the two sessions' minutes, when CanSplit
        public DateTime SplitAt1, SplitAt2; // and their starts
    }

    /// Proposes a start time for each open task that has no fixed time.
    public static class Planner
    {
        public const int DefaultMinutes = 30;   // length of a task with no length given
        public const int GapMinutes = 5;        // breathing room after a task (none inside an "after" chain)
        public const int StopBeforeBed = 60;    // nothing is planned in the last hour before bed
        public const double PhysicalFrom = 16;  // physical tasks: preferred window, clock hours
        public const double PhysicalTo = 20;
        public const int CheckInGap = 60;       // check-ins are at least this many minutes apart
        public const int CheckInSpread = 120;   // and at most about this far apart when the day has room
        public const int PeakFit = 80;          // when no slot lies wholly on a peak: percent of a task that must, to count as "mostly on the peak"
        public const int MinSession = 30;       // a split task's shorter session is at least this long

        /// Plan, plus an offer on each high task that would not sit wholly on the first peak as one session but
        /// fits as two (one on each peak): CanSplit, with the two sessions' lengths and starts. `split` holds the ids
        /// of the tasks the user chose to split; those come back as two items each (SplitOf 1 and 2).
        public static List<PlanItem> Propose(IList<TaskItem> tasks, EnergyDay e, IList<Routine> routines, DateTime now, bool keepStarted, ICollection<string> split)
        {
            var plan = Plan(tasks, e, routines, now, keepStarted, split);
            if (e.Peak1 == null || e.Peak2 == null) return plan;
            foreach (var it in plan.Where(x => x.Level == "high" && !x.Physical && !x.CheckIn && x.SplitOf == 0 && x.Minutes >= 2 * MinSession).ToList())
            {
                double slack = EnergyDay.StepMin / 2.0 + 0.5; // starts are on the 5-minute grid, the peak's edges are not
                if (it.Placed && it.Start >= e.Peak1.From.AddMinutes(-slack) && it.Start.AddMinutes(it.Minutes) <= e.Peak1.To.AddMinutes(slack)) continue; // already wholly on the first peak
                var s = new HashSet<string>(split ?? new string[0]); s.Add(it.Id);
                var alt = Plan(tasks, e, routines, now, keepStarted, s);
                var a = alt.FirstOrDefault(x => x.Id == it.Id && x.SplitOf == 1);
                var b = alt.FirstOrDefault(x => x.Id == it.Id && x.SplitOf == 2);
                if (a == null || b == null || !a.Placed || !b.Placed) continue;
                it.CanSplit = true; it.SplitA = a.Minutes; it.SplitB = b.Minutes; it.SplitAt1 = a.Start; it.SplitAt2 = b.Start;
            }
            return plan;
        }

        /// The end of the time Plan fills when planning at `now`: an hour before bed, or bedtime itself once
        /// less than half an hour is left before that hour, so a plan made late in the evening still has room.
        public static DateTime Until(EnergyDay e, DateTime now)
        {
            var wind = e.Bed.AddMinutes(-StopBeforeBed);
            return now > wind.AddMinutes(-30) ? e.Bed : wind;
        }

        public static bool Plannable(TaskItem t, DateTime day)
        {
            if (t.Done) return false;
            var d = t.DueAt;
            if (d == null) return true;
            if (d.Value.Date > day) return false;   // belongs to a later day
            return !t.HasTime || t.Planned;         // a time you set yourself stays where it is
        }

        /// keepStarted: planned tasks whose start time has already passed stay where they are (used when re-planning after a nap).
        public static List<PlanItem> Plan(IList<TaskItem> tasks, EnergyDay e, IList<Routine> routines, DateTime now, bool keepStarted = false, ICollection<string> split = null)
        {
            Func<TaskItem, bool> started = t => keepStarted && t.Planned && t.HasTime && t.DueAt != null && t.DueAt.Value <= now;
            int step = EnergyDay.StepMin, n = e.Samples.Length, gap = GapMinutes / step;
            var day = e.Wake.Date; string key = day.ToString("yyyy-MM-dd");
            var taken = new bool[n];
            var zone = new int[n];   // 1 = peak, 2 = dip
            var peakNo = new int[n]; // 1 = the first peak, 2 = the second
            Func<DateTime, int> idx = t => Math.Max(0, Math.Min(n, (int)Math.Round((t - e.Wake).TotalMinutes / step)));
            Action<int[], DateTime, DateTime, int> fill = (arr, a, b, v) => { for (int i = idx(a); i < idx(b); i++) arr[i] = v; };
            if (e.Peak1 != null) { fill(zone, e.Peak1.From, e.Peak1.To, 1); fill(peakNo, e.Peak1.From, e.Peak1.To, 1); }
            if (e.Peak2 != null) { fill(zone, e.Peak2.From, e.Peak2.To, 1); fill(peakNo, e.Peak2.From, e.Peak2.To, 2); }
            if (e.Dip != null) fill(zone, e.Dip.From, e.Dip.To, 2);
            foreach (var nap in e.Naps) for (int i = idx(nap.From); i < idx(nap.To); i++) taken[i] = true; // never inside a logged sleep period
            foreach (var t in tasks)
            {   // tasks with a time you set yourself keep their slot
                var d = t.DueAt;
                if (t.Done || d == null || !t.HasTime || (t.Planned && !started(t)) || d.Value < e.Wake || d.Value > e.Bed) continue;
                for (int i = idx(d.Value); i < idx(d.Value.AddMinutes((t.Minutes > 0 ? t.Minutes : DefaultMinutes) + GapMinutes)); i++) taken[i] = true;
            }

            int first = Math.Max(0, Math.Min(n, (int)Math.Ceiling((now - e.Wake).TotalMinutes / step)));
            int last = idx(Until(e, now));
            int groggy = idx(e.GroggyUntil);

            var items = tasks.Where(t => Plannable(t, day) && !started(t)).Select(t =>
            {
                var r = t.Routine == null || routines == null ? null : routines.FirstOrDefault(x => x.Id == t.Routine);
                return new PlanItem
                {
                    Id = t.Id, Title = t.Title, Minutes = t.Minutes > 0 ? t.Minutes : DefaultMinutes, Level = TaskTags.Level(t.Energy),
                    Physical = t.Kind == "physical", Routine = t.Routine, Part = t.Part, CheckIn = t.Part > 0,
                    After = r != null ? r.After : null, Prefer = r != null ? r.Prefer : null
                };
            }).ToList();

            Func<PlanItem, int> lenOf = it => Math.Max(1, (int)Math.Ceiling(it.Minutes / (double)step));
            Func<PlanItem, int, bool> fits = (it, i) =>
            {
                int len = lenOf(it);
                if (i < first || i + len > last) return false;
                for (int k = i; k < i + len; k++) if (taken[k]) return false;
                return true;
            };
            Func<PlanItem, int, Func<int, bool>, int> find = (it, from, ok) =>
            {
                for (int i = Math.Max(first, from); i + lenOf(it) <= last; i++) if (fits(it, i) && ok(i)) return i;
                return -1;
            };
            Func<PlanItem, int, int, int> count = (it, i, z) => { int c = 0; for (int k = i; k < i + lenOf(it); k++) if (zone[k] == z) c++; return c; };
            Func<PlanItem, int, Func<int, bool>, int> mostEnergy = (it, from, ok) =>
            {
                int at = -1; double best = -1;
                for (int i = Math.Max(first, from); i + lenOf(it) <= last; i++)
                {
                    if (!fits(it, i) || !ok(i)) continue;
                    double sum = 0; for (int k = i; k < i + lenOf(it); k++) sum += e.Samples[k];
                    if (sum > best) { best = sum; at = i; }
                }
                return at;
            };

            // an "after" link whose earlier routine is itself being planned: the follower is placed straight after it
            Func<PlanItem, PlanItem> plannedPred = it => it.After == null ? null : items.FirstOrDefault(p => p != it && p.Routine == it.After && p.Part <= 1);
            Func<PlanItem, int> notBefore = it =>
            {
                if (it.After == null) return first;
                var pt = tasks.FirstOrDefault(t => t.Routine == it.After && t.RoutineDay == key);
                var d = pt == null ? null : pt.DueAt;
                if (pt == null || pt.Done || d == null || !pt.HasTime) return first;
                return Math.Max(first, idx(d.Value.AddMinutes(pt.Minutes > 0 ? pt.Minutes : DefaultMinutes)));
            };

            Action<PlanItem, int> place = null;
            place = (it, at) =>
            {
                int len = lenOf(it);
                var followers = it.Routine == null || it.Part > 1 ? new List<PlanItem>()
                    : items.Where(f => f != it && f.After == it.Routine && !f.CheckIn && !f.Placed).ToList();
                for (int k = at; k < Math.Min(n, at + len + (followers.Count > 0 ? 0 : gap)); k++) taken[k] = true;
                it.Placed = true; it.Tried = true; it.Start = e.TimeAt(at); it.End = at + len;
                foreach (var f in followers)
                {
                    if (f.Placed) continue;
                    int j = find(f, it.End, i => true);
                    if (j >= 0) place(f, j);
                }
            };

            Func<PlanItem, int, int> choose = (it, from) =>
            {
                int len = lenOf(it), at = -1;
                if (it.Prefer == "early") return find(it, from, i => true);
                if (it.Physical)
                {   // late afternoon or early evening if there is room, else anywhere
                    int a = idx(day.AddHours(PhysicalFrom)), b = idx(day.AddHours(PhysicalTo));
                    at = find(it, Math.Max(from, a), i => i + len <= b);
                    if (at < 0) at = find(it, Math.Max(from, a), i => true);
                    if (at < 0) at = find(it, from, i => true);
                    return at;
                }
                if (it.Prefer == "dip" || it.Level == "low")
                {   // the groggy stretch after waking (low only), then the dip, then anywhere off the peaks
                    if (it.Prefer != "dip") at = find(it, from, i => i + len <= groggy);
                    if (at < 0) at = find(it, from, i => count(it, i, 2) == len);
                    if (at < 0) at = find(it, from, i => count(it, i, 2) * 2 >= len);
                    if (at < 0) at = find(it, from, i => count(it, i, 1) == 0);
                    if (at < 0) at = find(it, from, i => true);
                    return at;
                }
                if (it.Level == "high")
                {   // the earliest slot entirely on a peak. If none is left (or the task is longer than the peak): the
                    // highest-energy slot that is mostly on a peak, then half on one, then the highest-energy slot of the day.
                    at = find(it, from, i => count(it, i, 1) == len);
                    if (at < 0) at = mostEnergy(it, from, i => count(it, i, 1) * 100 >= len * PeakFit);
                    if (at < 0) at = mostEnergy(it, from, i => count(it, i, 1) * 2 >= len);
                    return at >= 0 ? at : mostEnergy(it, from, i => true);
                }
                if (it.Level == "medium")
                {
                    at = find(it, from, i => count(it, i, 1) == len);
                    if (at < 0) at = mostEnergy(it, from, i => count(it, i, 1) * 100 >= len * PeakFit);
                    return at >= 0 ? at : mostEnergy(it, from, i => true);
                }
                at = find(it, from, i => count(it, i, 1) == 0);
                return at >= 0 ? at : find(it, from, i => true);
            };

            // A task the user chose to split: one session wholly on the first peak, the other wholly on the second.
            // The two lengths are as even as the peaks allow, each at least MinSession.
            Func<PlanItem, int, int, bool> onPeak = (p, i, no) => { for (int k = i; k < i + lenOf(p); k++) if (k >= n || peakNo[k] != no) return false; return true; };
            Func<PlanItem, bool> trySplit = it =>
            {
                int total = it.Minutes, from = notBefore(it);
                if (total < 2 * MinSession) return false;
                var sizes = new List<int>();
                for (int m = MinSession; m <= total - MinSession; m += step) sizes.Add(m);
                foreach (int a in sizes.OrderBy(m => Math.Abs(2 * m - total)).ThenByDescending(m => m))
                {
                    var p1 = new PlanItem { Minutes = a }; var p2 = new PlanItem { Minutes = total - a };
                    int i1 = find(p1, from, i => onPeak(p1, i, 1));
                    if (i1 < 0) continue;
                    int i2 = find(p2, i1 + lenOf(p1) + gap, i => onPeak(p2, i, 2));
                    if (i2 < 0) continue;
                    it.Minutes = a; it.SplitOf = 1; place(it, i1);
                    var second = new PlanItem { Id = it.Id, Title = it.Title, Level = it.Level, Routine = it.Routine, Minutes = total - a, SplitOf = 2, Tried = true };
                    items.Add(second); place(second, i2);
                    return true;
                }
                return false;
            };

            // Order of choosing; within each group, the order in the task list.
            var groups = new Func<PlanItem, bool>[]
            {
                x => x.Prefer == "early",                       // the early block, with everything chained after it
                x => !x.Physical && x.Level == "high",          // high mental work takes the peaks
                x => x.Physical,
                x => x.Level == "medium" && x.Prefer != "dip",  // medium gets what is left of the peaks
                x => x.Prefer == "dip" || x.Level == "low",
                x => true
            };
            Action<int, int> run = (fromGroup, toGroup) =>
            {
                for (int gi = fromGroup; gi <= toGroup; gi++)
                    foreach (var it in items.Where(groups[gi]).ToList())
                    {
                        if (it.Placed || it.Tried || it.CheckIn || plannedPred(it) != null) continue;
                        it.Tried = true;
                        if (split != null && split.Contains(it.Id) && it.Level == "high" && !it.Physical && trySplit(it)) continue;
                        int at = choose(it, notBefore(it));
                        if (at >= 0) place(it, at);
                    }
            };
            run(0, 3);

            // Check-ins: spread through the day after the routine they follow, in the gaps between the blocks above.
            // They go in before the low and untagged tasks, which then fill in around them.
            foreach (var grp in items.Where(x => x.CheckIn).GroupBy(x => x.Routine))
            {
                var list = grp.OrderBy(x => x.Part).ToList();
                var pred = plannedPred(list[0]);
                if (pred != null && pred.CheckIn) pred = null;
                if (pred != null && !pred.Placed) continue;
                int start = pred != null ? pred.End : notBefore(list[0]);
                int every = Math.Max(CheckInGap / step, Math.Min(CheckInSpread / step, (last - start) / (list.Count + 1)));
                int prev = -10000;
                for (int k = 0; k < list.Count; k++)
                {
                    int at = find(list[k], Math.Max(start + (k + 1) * every, prev + CheckInGap / step), i => true);
                    if (at < 0) break;
                    place(list[k], at); prev = at;
                }
            }

            run(4, 5);
            return items;
        }
    }

    // ---------------------------------------------------------------- Shell: tags on rows, routines, roster, Plan

    partial class Shell
    {
        /// "Now", overridable for tests with CORNER_NOW=HH:mm.
        static DateTime Now()
        {
            TimeSpan t; string s = Environment.GetEnvironmentVariable("CORNER_NOW");
            return s != null && TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out t) ? DateTime.Today + t : DateTime.Now;
        }

        void UpdateInputPreview()
        {
            string en, kind, title; int mins; DateTime due; bool ht;
            string text = TaskTags.Extract(input.Text, out en, out mins, out kind);
            var parts = new List<string>();
            if (en != null) parts.Add(TaskTags.Short(en));
            if (kind == "physical") parts.Add("phys");
            if (mins > 0) parts.Add(TaskTags.Len(mins));
            if (When.Parse(text, out title, out due, out ht)) parts.Add(When.Label(due, ht));
            inputPreview.Text = string.Join(" · ", parts);
            inputChip.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }


        /// Marks a task that came from a routine.
        static FrameworkElement RoutineMark()
        {
            return new TextBlock { Text = "↻", FontSize = Z(11.5), Foreground = T.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 2), IsHitTestVisible = false };
        }

        // ------------------------------------------------------------ routines -> today's tasks

        /// First open of the day: drop yesterday's unfinished routine tasks. Routines join the day only when you send
        /// them, with "Add to today" in the Routines list (AddRoutinesToToday).
        void EnsureRoutines(DateTime day)
        {
            LogDays(); // yesterday into days.csv first, while its unfinished routine tasks are still here (Stats.cs)
            Routines.Load();
            Routines.OfferNap();
            string key = day.ToString("yyyy-MM-dd");
            if (data.Tasks.RemoveAll(t => t.Routine != null && !t.Done && t.RoutineDay != key) > 0) Save();
        }

        /// True when the routine is already among today's tasks (done or not).
        bool RoutineInToday(Routine r)
        {
            string key = SleepLog.Today(Now()).ToString("yyyy-MM-dd");
            return data.Tasks.Any(t => t.Routine == r.Id && t.RoutineDay == key);
        }

        /// Sends routines to today's task list, each with its effort and length, so Plan my day can place them.
        /// A routine already in today's list is not added again. Returns how many tasks were added.
        int AddRoutinesToToday(IEnumerable<Routine> routines)
        {
            string key = SleepLog.Today(Now()).ToString("yyyy-MM-dd");
            int added = 0;
            foreach (var r in routines.ToList())
            {
                if (RoutineInToday(r)) continue;
                int n = Math.Max(1, r.CheckIns);
                for (int k = 1; k <= n; k++, added++)
                    data.Tasks.Add(new TaskItem
                    {
                        Id = Guid.NewGuid().ToString("N"), Title = r.Title + (n > 1 ? " (" + k + "/" + n + ")" : ""),
                        Energy = TaskTags.Level(r.Energy), Kind = r.Kind == "physical" ? "physical" : null, Minutes = r.Minutes,
                        Routine = r.Id, RoutineDay = key, Part = n > 1 ? k : 0
                    });
            }
            if (added > 0) { proposal = null; Save(); Rebuild(); DrawEnergy(); }
            return added;
        }


        // ------------------------------------------------------------ Plan

        class PlanUndo { public string Id, Due, Title; public bool HasTime, Notified, Planned, Created; public int Minutes; }
        List<PlanItem> proposal;   // shown, not yet applied
        List<PlanUndo> planUndo;   // what the last applied plan replaced
        HashSet<string> planSplit = new HashSet<string>(); // tasks the user chose to split across both peaks in the shown plan
        HashSet<string> planKeep = new HashSet<string>();  // and those they chose to keep as one session (no more asking)
        bool routinesOpen;
        string editRoutine;


        void MakePlan() { if (PriorityMode) { MakeOrder(); return; } planSplit.Clear(); planKeep.Clear(); MakePlan(false); } // by priority: an order, not times (Work.cs)

        bool planKeepStarted;
        void MakePlan(bool keepStarted)
        {
            if (energy == null) return;
            planKeepStarted = keepStarted;
            proposal = Planner.Propose(data.Tasks, energy, Routines.Data != null ? Routines.Data.Routines : null, Now(), keepStarted, planSplit);
            DrawEnergy();
        }

        /// The answer to "split it, or keep it as one session?" on a row of the shown plan.
        void SplitInPlan(string id, bool split)
        {
            if (split) { planSplit.Add(id); planKeep.Remove(id); } else { planSplit.Remove(id); planKeep.Add(id); }
            MakePlan(planKeepStarted);
        }

        void ApplyPlan()
        {
            if (proposal == null) return;
            planUndo = new List<PlanUndo>();
            planOrder = data.Tasks.Select(x => x.Id).ToList();
            foreach (var it in proposal.Where(x => x.Placed).OrderBy(x => x.SplitOf))
            {
                var t = data.Tasks.FirstOrDefault(x => x.Id == it.Id);
                if (t == null || t.Done) continue;
                if (it.SplitOf == 2)
                {   // the second session becomes a task of its own, right after the first
                    var t2 = new TaskItem
                    {
                        Id = Guid.NewGuid().ToString("N"), Title = SplitTitle(it.Title, 2), Energy = t.Energy, Kind = t.Kind,
                        Minutes = it.Minutes, Routine = t.Routine, RoutineDay = t.RoutineDay
                    };
                    t2.SetDue(it.Start, true); t2.Planned = true;
                    data.Tasks.Insert(data.Tasks.IndexOf(t) + 1, t2);
                    planUndo.Add(new PlanUndo { Id = t2.Id, Created = true });
                    continue;
                }
                planUndo.Add(new PlanUndo { Id = t.Id, Due = t.Due, HasTime = t.HasTime, Notified = t.Notified, Planned = t.Planned, Title = t.Title, Minutes = t.Minutes });
                t.SetDue(it.Start, true);
                t.Planned = true;
                if (it.SplitOf == 1) { t.Title = SplitTitle(it.Title, 1); t.Minutes = it.Minutes; }
            }
            proposal = null; planSplit.Clear(); planKeep.Clear();
            SortByTime();
            Save(); Rebuild(); DrawEnergy();
        }

        List<string> planOrder;    // the list order the last applied plan replaced

        /// After a plan is applied: today's timed tasks first, in time order, then the rest as they were.
        /// Dragging a row still reorders the list by hand afterwards.
        void SortByTime()
        {
            var tomorrow = DateTime.Today.AddDays(1);
            Func<TaskItem, int> group = t =>
            {
                var d = t.DueAt;
                if (t.Done) return 4;
                if (t.HasTime && d != null && d.Value < tomorrow) return 1;
                if (d != null && d.Value.Date > DateTime.Today) return 3;
                return 2;
            };
            var sorted = data.Tasks.Select((t, i) => new { t, i, g = group(t) })
                .OrderBy(x => x.g).ThenBy(x => x.g == 1 ? x.t.DueAt.Value.Ticks : 0).ThenBy(x => x.i).Select(x => x.t).ToList();
            data.Tasks.Clear(); data.Tasks.AddRange(sorted);
        }

        static string SplitTitle(string title, int part) { return title + " (" + part + "/2)"; }

        void UndoPlan()
        {
            if (planUndo == null) return;
            foreach (var u in planUndo)
            {
                var t = data.Tasks.FirstOrDefault(x => x.Id == u.Id);
                if (t == null) continue;
                if (u.Created) { if (!t.Done) data.Tasks.Remove(t); continue; }
                t.Due = u.Due; t.HasTime = u.HasTime; t.Notified = u.Notified; t.Planned = u.Planned;
                if (u.Title != null) { t.Title = u.Title; t.Minutes = u.Minutes; }
            }
            planUndo = null;
            if (planOrder != null)
            {   // the list order from before the plan; tasks added since keep their place at the end
                var was = planOrder; planOrder = null;
                var sorted = data.Tasks.Select((t, i) => new { t, i, k = was.IndexOf(t.Id) }).OrderBy(x => x.k < 0 ? int.MaxValue : x.k).ThenBy(x => x.i).Select(x => x.t).ToList();
                data.Tasks.Clear(); data.Tasks.AddRange(sorted);
            }
            Save(); Rebuild(); DrawEnergy();
        }

        // ------------------------------------------------------------ the Routines list

        const string RoutineHelp = "After the title, type any of: 20m · !low !med !high · !phys · daily, weekdays or mon,wed,fri · after 2 · x3 for check-ins · !early · !dip · !off";

    }
}
