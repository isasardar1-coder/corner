// Checks for Corner's sleep and energy model and for Plan. Run tests\run.ps1: it compiles this file together with
// src\*.cs into a console program and prints PASS or FAIL for each check. Not part of Corner.exe.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Corner;

namespace CornerTests
{
    static class Program
    {
        static int fails;
        static void Check(string what, bool ok, string detail = "")
        {
            if (!ok) fails++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what + (detail != "" ? "   [" + detail + "]" : ""));
        }
        static string C(DateTime t) { return t.ToString("HH:mm"); }
        static string Z(Zone z) { return z == null ? "none" : C(z.From) + "-" + C(z.To); }
        static TimeSpan T(int h, int m) { return new TimeSpan(h, m, 0); }

        static void Describe(string name, EnergyDay e)
        {
            Console.WriteLine("  " + name + ": wake " + C(e.Wake) + " bed " + C(e.Bed) + " groggy<" + C(e.GroggyUntil) + " peak1 " + Z(e.Peak1) + " dip " + Z(e.Dip) + " peak2 " + Z(e.Peak2)
                + " debt " + e.DebtHours.ToString("0.00") + "h  E@10=" + e.At(e.Wake.Date.AddHours(10)).ToString("0.000") + " E@14.5=" + e.At(e.Wake.Date.AddHours(14.5)).ToString("0.000") + " E@19=" + e.At(e.Wake.Date.AddHours(19)).ToString("0.000"));
        }

        static List<Night> Repeat(TimeSpan asleep, TimeSpan woke, int nights, DateTime day)
        {
            var list = new List<Night>();
            for (int i = nights - 1; i >= 0; i--) list.Add(SleepLog.Build(asleep, woke, day.AddDays(-i)));
            return list;
        }

        static TaskItem Task(string title, string energy, int minutes, string kind = null)
        {
            return new TaskItem { Id = Guid.NewGuid().ToString("N"), Title = title, Energy = energy, Minutes = minutes, Kind = kind };
        }

        static double PeakShare(EnergyDay e, DateTime a, int minutes)
        {
            Func<Zone, double> overlap = z => z == null ? 0 : Math.Max(0, (Min(z.To, a.AddMinutes(minutes)) - Max(z.From, a)).TotalMinutes) / minutes;
            return overlap(e.Peak1) + overlap(e.Peak2);
        }
        static string ZoneOf(EnergyDay e, DateTime a, int minutes)
        {
            Func<Zone, double> overlap = z => z == null ? 0 : Math.Max(0, (Min(z.To, a.AddMinutes(minutes)) - Max(z.From, a)).TotalMinutes) / minutes;
            double p = PeakShare(e, a, minutes), d = overlap(e.Dip);
            string s = p >= 0.5 ? "peak" : d >= 0.5 ? "dip" : a.AddMinutes(minutes) <= e.GroggyUntil.AddMinutes(4) ? "groggy" : "between";
            return s + " (" + (int)Math.Round(p * 100) + "% peak, " + (int)Math.Round(d * 100) + "% dip)";
        }
        static DateTime Min(DateTime a, DateTime b) { return a < b ? a : b; }
        static DateTime Max(DateTime a, DateTime b) { return a > b ? a : b; }

        static void PrintPlan(EnergyDay e, List<PlanItem> plan)
        {
            foreach (var it in plan.OrderBy(x => x.Placed ? 0 : 1).ThenBy(x => x.Start))
                Console.WriteLine("    " + (it.Placed ? C(it.Start) + "-" + C(it.Start.AddMinutes(it.Minutes)) : "  -- no room") + "  " + (it.Level ?? "unset").PadRight(7) + (it.Physical ? "phys " : "     ") + it.Title.PadRight(28) + (it.Placed ? ZoneOf(e, it.Start, it.Minutes) + "  avgE=" + AvgE(e, it).ToString("0.000") : ""));
        }
        static double AvgE(EnergyDay e, PlanItem it)
        {
            double s = 0; int n = 0;
            for (var t = it.Start; t < it.Start.AddMinutes(it.Minutes); t = t.AddMinutes(5)) { s += e.At(t); n++; }
            return s / n;
        }

        // ------------------------------------------------------------------ simulated sleepers, for the sleep-need estimate

        static double Gauss(Random r) { return Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble()); }

        /// A made-up person whose true need is known: each day's scores drop by `perHour` points for every hour their recent
        /// sleep falls short of it. `lastNightOnly`: the drop follows last night alone instead of the last three nights.
        static void Sleeper(Random rng, DateTime day, double trueNeed, int days, double sleepMean, double sleepSd, double perHour, double noise, bool lastNightOnly, out List<Night> nights, out List<Feedback.Entry> checks, int perDay = 0)
        {
            nights = new List<Night>(); checks = new List<Feedback.Entry>();
            var lens = new List<double>();
            for (int i = days - 1; i >= 0; i--)
            {
                double len = Math.Max(4.5, Math.Min(10.5, sleepMean + sleepSd * Gauss(rng)));
                var wake = day.AddDays(-i).AddHours(7).AddMinutes(Math.Round(20 * Gauss(rng)));
                nights.Add(Night.Make(wake.AddMinutes(-Math.Round(len * 60)), wake, false));
                lens.Add(nights[nights.Count - 1].Hours);
                int k = lens.Count - 1;
                double recent = lastNightOnly || k < 2 ? lens[k] : 0.5 * lens[k] + 0.3 * lens[k - 1] + 0.2 * lens[k - 2];
                double drop = perHour * Math.Max(0, trueNeed - recent);
                int count = perDay > 0 ? perDay : 3 + rng.Next(3);
                for (int c = 0; c < count; c++)
                {
                    double t = 1 + rng.NextDouble() * 13;
                    double usual = 6.8 + 1.3 * Math.Cos(2 * Math.PI * (t - 3) / 12) - 0.08 * t; // a morning peak, an afternoon dip, an evening rise
                    int score = (int)Math.Max(1, Math.Min(10, Math.Round(usual - drop + noise * Gauss(rng))));
                    checks.Add(new Feedback.Entry { At = wake.AddHours(t), Score = score, Predicted = 0.5, SinceWake = t });
                }
            }
        }

        static void Sim()
        {
            var day = new DateTime(2026, 11, 21); var now = day.AddHours(23);
            Console.WriteLine("Sleep-need estimate on simulated sleepers (300 each). ready = a figure was suggested; of those: average error, share within 30 min, share off by more than 45 min.");
            Action<string, double, int, double, double, double, double, bool> run = (name, trueNeed, days, mean, sd, perHour, noise, lastOnly) =>
            {
                int ready = 0, close = 0, far = 0; double err = 0; var notes = new Dictionary<string, int>();
                for (int seed = 1; seed <= 300; seed++)
                {
                    List<Night> n; List<Feedback.Entry> c;
                    Sleeper(new Random(seed), day, trueNeed, days, mean, sd, perHour, noise, lastOnly, out n, out c);
                    var r = NeedEstimate.Build(n, c, now);
                    if (!r.Ready) { string key = r.Note.Substring(0, Math.Min(34, r.Note.Length)); int v; notes.TryGetValue(key, out v); notes[key] = v + 1; continue; }
                    ready++; double e = Math.Abs(r.Need - Math.Max(NeedEstimate.Floor, trueNeed)); err += e;
                    if (e <= 0.5 + 1e-9) close++; if (e > 0.75 + 1e-9) far++;
                }
                Console.WriteLine("  " + name.PadRight(46) + " ready " + (ready * 100 / 300).ToString().PadLeft(3) + "%" + (ready > 0 ? "  avg error " + (err / ready * 60).ToString("0") + " min, within 30 min " + (close * 100 / ready) + "%, off >45 min " + (far * 100 / ready) + "%" : ""));
                foreach (var p in notes.OrderByDescending(p => p.Value)) Console.WriteLine("        not ready " + (p.Value * 100 / 300).ToString().PadLeft(3) + "%: " + p.Key + "...");
            };
            run("need 7h30, 14 days, varied nights", 7.5, 14, 7.3, 0.9, 1.2, 1.0, false);
            run("need 7h30, 28 days, varied nights", 7.5, 28, 7.3, 0.9, 1.2, 1.0, false);
            run("need 7h30, 56 days, varied nights", 7.5, 56, 7.3, 0.9, 1.2, 1.0, false);
            run("need 7h30, 28 days, follows last night only", 7.5, 28, 7.3, 0.9, 1.2, 1.0, true);
            run("need 7h30, 28 days, noisier scores", 7.5, 28, 7.3, 0.9, 1.2, 1.6, false);
            run("need 7h30, 28 days, weak effect", 7.5, 28, 7.3, 0.9, 0.6, 1.0, false);
            run("need 8h30, 28 days, sleeps 7h20 on average", 8.5, 28, 7.3, 0.9, 1.2, 1.0, false);
            run("need 7h30, 28 days, nights all alike", 7.5, 28, 7.3, 0.3, 1.2, 1.0, false);
            run("need 6h15 (below the floor), 28 days", 6.25, 28, 7.0, 0.9, 1.2, 1.0, false);
            run("sleep makes no difference, 28 days", 7.5, 28, 7.3, 0.9, 0.0, 1.0, false);
            run("sleep makes no difference, 56 days", 7.5, 56, 7.3, 0.9, 0.0, 1.0, false);
        }

        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "sim") { Sim(); return; }
            if (args.Length > 0 && args[0] == "seed")
            {   // fills the data folder (CORNER_DATA) with 40 days of a made-up sleeper who needs 7h30, to render the suggestion
                List<Night> n; List<Feedback.Entry> c;
                Sleeper(new Random(7), DateTime.Today, 7.5, 40, 7.3, 0.9, 1.5, 0.4, false, out n, out c, 5);
                foreach (var x in n) SleepLog.Record(x);
                var lines = new List<string> { "time,score,predicted,hours_since_wake,active_min_today,active_min_session" };
                lines.AddRange(c.Where(x => x.At <= DateTime.Today.AddHours(15)).Select(x => x.At.ToString("yyyy-MM-ddTHH:mm") + "," + x.Score + ",0.500," + x.SinceWake.ToString("0.00", CultureInfo.InvariantCulture) + ",0,0"));
                System.IO.File.WriteAllLines(System.IO.Path.Combine(Environment.GetEnvironmentVariable("CORNER_DATA"), "checkins.csv"), lines);
                Console.WriteLine(NeedEstimate.Build(n, c, DateTime.Today.AddHours(15)).Note);
                return;
            }
            var day = new DateTime(2026, 11, 21);
            double need = 8;

            // ------------------------------------------------------------------ 1. curve from last night
            Console.WriteLine("== 1. Energy curve from last night's sleep");
            var normal = EnergyModel.Build(Repeat(T(23, 0), T(7, 0), 14, day), need, day);
            Describe("8h x14        ", normal);
            double dipMid = normal.Dip == null ? 0 : (normal.Dip.From.TimeOfDay.TotalHours + normal.Dip.To.TimeOfDay.TotalHours) / 2;
            Check("normal night: dip centred near 14:30", normal.Dip != null && Math.Abs(dipMid - 14.5) <= 0.5, "centre " + dipMid.ToString("0.00"));
            Check("normal night: no debt", normal.DebtHours < 0.01);
            Check("normal night: groggy -> peak1 -> dip -> peak2 in order", normal.Peak1 != null && normal.Peak2 != null && normal.Dip != null && normal.GroggyUntil <= normal.Peak1.From && normal.Peak1.To < normal.Dip.From && normal.Dip.To < normal.Peak2.From);

            var ns = Repeat(T(23, 0), T(7, 0), 13, day.AddDays(-1)); ns.Add(SleepLog.Build(T(2, 0), T(7, 0), day));
            var shortNight = EnergyModel.Build(ns, need, day);
            Describe("5h last night ", shortNight);
            Check("short night lowers the whole curve", Enumerable.Range(0, Math.Min(normal.Samples.Length, shortNight.Samples.Length)).All(i => shortNight.Samples[i] <= normal.Samples[i] + 1e-9));
            Check("short night: debt = 3h", Math.Abs(shortNight.DebtHours - 3) < 0.01, shortNight.DebtHours.ToString("0.00"));

            var nl = Repeat(T(23, 0), T(7, 0), 13, day.AddDays(-1)); nl.Add(SleepLog.Build(T(22, 0), T(8, 0), day));
            Describe("10h last night", EnergyModel.Build(nl, need, day));

            var nv = Repeat(T(23, 0), T(7, 0), 13, day.AddDays(-1)); nv.Add(SleepLog.Build(T(4, 0), T(7, 0), day));
            var veryShort = EnergyModel.Build(nv, need, day);
            Describe("3h last night ", veryShort);
            Check("3h night is lower than 5h night at 10:00", veryShort.At(day.AddHours(10)) < shortNight.At(day.AddHours(10)) - 0.005, veryShort.At(day.AddHours(10)).ToString("0.000") + " vs " + shortNight.At(day.AddHours(10)).ToString("0.000"));

            var sp = Repeat(T(23, 0), T(7, 0), 13, day.AddDays(-1));
            var split = SleepLog.BuildSplit(new[] { T(23, 30), T(5, 15) }, new[] { T(4, 30), T(7, 0) }, day);
            sp.Add(split);
            var es = EnergyModel.Build(sp, need, day);
            Describe("split 6h45    ", es);
            Check("split night totals 6h45", Math.Abs(split.Hours - 6.75) < 1e-9, split.Hours.ToString("0.000"));
            Check("split night: curve starts at 07:00", es.Wake == day.AddHours(7));
            var one = Repeat(T(23, 0), T(7, 0), 13, day.AddDays(-1)); one.Add(SleepLog.Build(T(0, 15), T(7, 0), day));
            var eo = EnergyModel.Build(one, need, day);
            Check("split costs a little vs. same sleep in one piece", es.At(day.AddHours(10)) < eo.At(day.AddHours(10)) && eo.At(day.AddHours(10)) - es.At(day.AddHours(10)) < 0.03, (eo.At(day.AddHours(10)) - es.At(day.AddHours(10))).ToString("0.0000"));
            var three = SleepLog.BuildSplit(new[] { T(22, 0), T(0, 15), T(5, 10) }, new[] { T(23, 30), T(4, 30), T(7, 30) }, day);
            Check("three periods, first ends before midnight: 1h30 + 4h15 + 2h20", three != null && Math.Abs(three.Hours - (1.5 + 4.25 + 2 + 20 / 60.0)) < 1e-9 && three.AsleepAt == day.AddDays(-1).AddHours(22), three == null ? "null" : three.Hours.ToString("0.000"));

            var lw = Repeat(T(23, 0), T(7, 0), 13, day.AddDays(-1)); lw.Add(SleepLog.Build(T(1, 0), T(9, 0), day));
            var late = EnergyModel.Build(lw, need, day);
            Describe("woke 09:00    ", late);
            Check("waking 2h late moves the dip about 1h later", late.Dip != null && Math.Abs((late.Dip.From - normal.Dip.From).TotalHours - 1) < 0.5, late.Dip == null ? "no dip" : (late.Dip.From - normal.Dip.From).TotalHours.ToString("0.00"));

            Console.WriteLine("  -- sweep of single-night patterns (14 nights each)");
            foreach (var p in new[] { new[] { 21, 0, 5, 0 }, new[] { 22, 0, 6, 0 }, new[] { 0, 0, 8, 0 }, new[] { 1, 0, 9, 0 }, new[] { 2, 0, 10, 0 }, new[] { 3, 0, 11, 0 }, new[] { 0, 30, 6, 30 }, new[] { 1, 0, 6, 0 }, new[] { 23, 0, 9, 0 } })
            {
                var e = EnergyModel.Build(Repeat(T(p[0], p[1]), T(p[2], p[3]), 14, day), need, day);
                Describe(p[0].ToString("00") + ":" + p[1].ToString("00") + "-" + p[2].ToString("00") + ":" + p[3].ToString("00") + "   ", e);
                Check("   zones present and ordered", e.Peak1 != null && e.Dip != null && e.Peak2 != null && e.Peak1.To <= e.Dip.From && e.Dip.To <= e.Peak2.From && e.Peak1.From >= e.Wake);
            }

            // zones once the check-in tuning is active: corrections with bumps must not make the zones vanish
            Console.WriteLine("  -- with a tuning correction added to the curve");
            var bumps = new Dictionary<string, Func<double, double>>
            {
                { "early lift, then low (0.5h +, 1.6h -)", t => 0.08 * Math.Exp(-(t - 0.5) * (t - 0.5) / 1.1) - 0.10 * Math.Exp(-(t - 1.6) * (t - 1.6) / 1.1) },
                { "sharper morning, flatter evening    ", t => 0.12 * Math.Exp(-(t - 2.2) * (t - 2.2) / 1.1) - 0.08 * Math.Exp(-(t - 12) * (t - 12) / 1.1) },
                { "late-evening bump (15h +)           ", t => 0.10 * Math.Exp(-(t - 15) * (t - 15) / 1.1) - 0.06 * Math.Exp(-(t - 13.5) * (t - 13.5) / 1.1) },
                { "ripple                              ", t => 0.05 * Math.Sin(t * 2.4) },
            };
            foreach (var b in bumps)
            {
                var e = EnergyModel.Build(Repeat(T(23, 0), T(7, 0), 14, day), need, day, b.Value);
                Describe(b.Key, e);
                Check("   tuned curve keeps peak, dip (12:30-16:30) and second peak", e.Peak1 != null && e.Dip != null && e.Peak2 != null && e.Dip.From.TimeOfDay.TotalHours >= 12.5 && e.Dip.To.TimeOfDay.TotalHours <= 16.5 && e.Peak1.To <= e.Dip.From && e.Dip.To <= e.Peak2.From);
            }

            // ------------------------------------------------------------------ 2. debt
            Console.WriteLine("== 2. Sleep debt");
            SleepLog.Load();
            // your own log (a copy of sleep.json), on the day of its latest night; the debt is added up again here, straight from the file's times
            var realNights = SleepLog.Data.Nights.ToList(); // kept: the nap checks further down write their own nights over the copy
            var realDay = SleepLog.Latest != null ? SleepLog.Latest.WokeAt.Date : day;
            var real = EnergyModel.Build(SleepLog.Data.Nights, need, realDay);
            if (real != null)
            {
                Describe("real sleep.json", real);
                var window = SleepLog.Data.Nights.Where(x => x.WokeAt.Date > realDay.AddDays(-14) && x.WokeAt.Date <= realDay).ToList();
                double measured = window.Sum(x => need
                    - (x.Periods != null && x.Periods.Count > 1 ? x.Periods.Sum(p => (Night.P(p.Woke) - Night.P(p.Asleep)).TotalHours) : (x.WokeAt - x.AsleepAt).TotalHours)
                    - (x.Naps ?? new List<SleepSpan>()).Sum(p => (Night.P(p.Woke) - Night.P(p.Asleep)).TotalHours));
                // unlogged nights count as an average logged night (today's naps left out of the average), once 3 nights are logged
                double todayNaps = real.Naps.Sum(x => x.Hours);
                double guessed = window.Count >= 3 && window.Count < 14 ? (14 - window.Count) * (measured + todayNaps) / window.Count : 0;
                double hand = Math.Max(0, measured + guessed);
                Check("real data: debt matches an independent sum of the file", Math.Abs(real.DebtHours - hand) < 0.01, real.DebtHours.ToString("0.000") + " vs " + hand.ToString("0.000"));
                Console.WriteLine("  nights logged in window: " + window.Count + " of 14; measured " + measured.ToString("0.00") + "h + estimated " + guessed.ToString("0.00") + "h");
            }
            var d14 = EnergyModel.Build(Repeat(T(0, 0), T(7, 0), 20, day), need, day);
            Check("14 x 7h = 14h debt (older nights ignored)", Math.Abs(d14.DebtHours - 14) < 0.01, d14.DebtHours.ToString("0.00"));
            var mix = Repeat(T(0, 0), T(7, 0), 7, day.AddDays(-7)); mix.AddRange(Repeat(T(22, 0), T(7, 0), 7, day));
            var dm = EnergyModel.Build(mix, need, day);
            Check("7 x 7h then 7 x 9h = 0 debt", Math.Abs(dm.DebtHours) < 0.01, dm.DebtHours.ToString("0.00"));
            var d7 = EnergyModel.Build(Repeat(T(23, 30), T(7, 0), 14, day), need, day); // 14 x 7h30 = 7h debt
            Check("more debt -> lower energy at 10:00 and deeper dip", Math.Abs(d7.DebtHours - 7) < 0.01 && d14.At(day.AddHours(10)) < d7.At(day.AddHours(10)) && d14.At(day.AddHours(14.5)) < d7.At(day.AddHours(14.5)), d7.At(day.AddHours(10)).ToString("0.000") + " -> " + d14.At(day.AddHours(10)).ToString("0.000"));
            var d9 = EnergyModel.Build(Repeat(T(23, 0), T(7, 0), 14, day), 9, day);
            Check("need 9h with 8h nights x14 = 14h debt", Math.Abs(d9.DebtHours - 14) < 0.01, d9.DebtHours.ToString("0.00"));

            // nights that were never logged count as an average logged night
            var half = EnergyModel.Build(Repeat(T(0, 0), T(7, 0), 7, day), need, day);
            Check("7 nights of 7h logged, 7 not logged = 14h (7h measured + 7h estimated)", Math.Abs(half.DebtHours - 14) < 0.01 && Math.Abs(half.DebtEstimate - 7) < 0.01 && half.DebtLogged == 7, half.DebtHours.ToString("0.00") + ", estimate " + half.DebtEstimate.ToString("0.00"));
            Check("...and the curve is the same as with all 14 logged", Math.Abs(half.At(day.AddHours(10)) - d14.At(day.AddHours(10))) < 1e-9);
            var uneven = new List<Night> { SleepLog.Build(T(2, 0), T(7, 0), day.AddDays(-6)), SleepLog.Build(T(23, 0), T(7, 0), day.AddDays(-3)), SleepLog.Build(T(0, 0), T(7, 0), day.AddDays(-1)), SleepLog.Build(T(23, 0), T(7, 0), day) };
            var un = EnergyModel.Build(uneven, need, day); // shortfalls 3, 0, 1, 0: average 1h a night
            Check("4 logged (3h, 0, 1h, 0 short), 10 not logged = 4h + 10 x 1h = 14h", Math.Abs(un.DebtHours - 14) < 0.01, un.DebtHours.ToString("0.00"));
            var gaps = new List<Night> { SleepLog.Build(T(1, 0), T(7, 0), day.AddDays(-5)), SleepLog.Build(T(1, 0), T(7, 0), day) };
            var two = EnergyModel.Build(gaps, need, day);
            Check("only 2 nights logged: too few to estimate, debt = 4h measured", Math.Abs(two.DebtHours - 4) < 0.01 && two.DebtEstimate == 0, two.DebtHours.ToString("0.00"));
            var over = EnergyModel.Build(Repeat(T(22, 0), T(7, 0), 5, day), need, day);
            Check("5 nights of 9h logged, 9 not logged: no debt, never below zero", over.DebtHours == 0, over.DebtHours.ToString("0.00"));
            var napGap = Repeat(T(0, 0), T(7, 0), 7, day); var napGap2 = Repeat(T(0, 0), T(7, 0), 7, day);
            napGap2[6].Naps = new List<SleepSpan> { SleepSpan.Make(day.AddHours(14.5), day.AddHours(14.5).AddMinutes(30)) };
            double dropGap = EnergyModel.Build(napGap, need, day).DebtHours - EnergyModel.Build(napGap2, need, day).DebtHours;
            Check("with unlogged nights, a 30-minute nap still pays back exactly 30 minutes", Math.Abs(dropGap - 0.5) < 1e-6, (dropGap * 60).ToString("0.0") + " min");
            Console.WriteLine("  today not logged, 3 x 6h before: debt = " + EnergyModel.Build(Repeat(T(1, 0), T(7, 0), 3, day.AddDays(-1)), need, day).DebtHours.ToString("0.00") + "h (6h measured + 11 nights estimated at 2h)");

            // ------------------------------------------------------------------ 3. naps
            Console.WriteLine("== 3. Naps");
            Func<List<Night>> baseNights = () => { var l = Repeat(T(23, 0), T(7, 0), 13, day.AddDays(-1)); l.Add(SleepLog.BuildSplit(new[] { T(23, 30), T(5, 15) }, new[] { T(4, 30), T(7, 0) }, day)); return l; };
            var b0 = baseNights(); var e0 = EnergyModel.Build(b0, need, day);
            var b1 = baseNights(); b1[b1.Count - 1].Naps = new List<SleepSpan> { SleepSpan.Make(day.AddHours(14.5), day.AddHours(14.5).AddMinutes(25)) };
            var e1 = EnergyModel.Build(b1, need, day);
            Describe("no nap        ", e0); Describe("nap 14:30 25m ", e1);
            Check("nap: debt drops by exactly the nap length", Math.Abs((e0.DebtHours - e1.DebtHours) - 25 / 60.0) < 1e-6, (e0.DebtHours - e1.DebtHours).ToString("0.000"));
            Check("nap: curve before the nap is unchanged", Enumerable.Range(0, (int)(7.5 * 12)).All(i => Math.Abs(e0.Samples[i] - e1.Samples[i]) < 1e-12), "max change " + Enumerable.Range(0, (int)(7.5 * 12)).Max(i => Math.Abs(e0.Samples[i] - e1.Samples[i])).ToString("0.0000"));
            Check("nap: afternoon lifts afterwards (15:30, 16:30, 18:00)", new[] { 15.5, 16.5, 18 }.All(h => e1.At(day.AddHours(h)) > e0.At(day.AddHours(h)) + 0.01), string.Join(" ", new[] { 15.0, 15.5, 16.5, 18, 21 }.Select(h => h + ":" + (e1.At(day.AddHours(h)) - e0.At(day.AddHours(h))).ToString("+0.000;-0.000"))));
            Check("nap: short groggy stretch right at waking, gone in 20 min", e1.At(day.AddHours(14.5).AddMinutes(25)) - e0.At(day.AddHours(14.5).AddMinutes(25)) < e1.At(day.AddHours(14.5).AddMinutes(45)) - e0.At(day.AddHours(14.5).AddMinutes(45)));
            Check("nap before 16:00: bedtime not pushed", e1.BedPush == 0 && e1.Bed == e0.Bed);
            Check("nap: peak and dip zones stay where they were", Z(e0.Peak1) == Z(e1.Peak1) && Z(e0.Dip) == Z(e1.Dip) && Z(e0.Peak2) == Z(e1.Peak2), Z(e1.Peak1) + " / " + Z(e1.Dip) + " / " + Z(e1.Peak2));

            var b2 = baseNights(); b2[b2.Count - 1].Naps = new List<SleepSpan> { SleepSpan.Make(day.AddHours(17), day.AddHours(17).AddMinutes(40)) };
            var e2 = EnergyModel.Build(b2, need, day);
            Describe("nap 17:00 40m ", e2);
            Check("late nap pushes bedtime 60 min and says so", Math.Abs(e2.BedPush - 60) < 1e-6 && (e2.Bed - e0.Bed).TotalMinutes == 60 && e2.Summary(day.AddHours(18)).Contains("late nap"), e2.Summary(day.AddHours(18)));
            Check("long nap: heavier grogginess than a short one", (e2.At(day.AddHours(17).AddMinutes(40)) - e0.At(day.AddHours(17).AddMinutes(40))) < (e1.At(day.AddHours(14.5).AddMinutes(25)) - e0.At(day.AddHours(14.5).AddMinutes(25))));

            Func<double, double, double> push = (fromH, mins) => { var b = baseNights(); b[b.Count - 1].Naps = new List<SleepSpan> { SleepSpan.Make(day.AddHours(fromH), day.AddHours(fromH).AddMinutes(mins)) }; return EnergyModel.Build(b, need, day).BedPush; };
            Console.WriteLine("  bed push: nap 15:30-15:55 " + push(15.5, 25) + " min, 15:30-16:05 " + push(15.5, 35) + " min, 15:45-16:15 " + push(15.75, 30) + " min, 16:00-16:30 " + push(16, 30) + " min");
            Check("a nap that only just crosses 16:00 pushes bedtime only a little", push(15.5, 35) <= 10, push(15.5, 35) + " min");

            var b4 = baseNights(); b4[b4.Count - 1].Naps = new List<SleepSpan> { SleepSpan.Make(day.AddHours(14.5), day.AddHours(14.5).AddMinutes(25)), SleepSpan.Make(day.AddHours(14.5), day.AddHours(14.5).AddMinutes(25)), SleepSpan.Make(day.AddHours(14.6), day.AddHours(14.5).AddMinutes(25)) };
            var e4 = EnergyModel.Build(b4, need, day);
            Check("a nap already in the file twice is counted once in debt", Math.Abs((e0.DebtHours - e4.DebtHours) - 25 / 60.0) < 1e-6, "debt drop " + ((e0.DebtHours - e4.DebtHours) * 60).ToString("0") + " min");
            Check("a nap already in the file twice lifts once", Math.Abs(e4.At(day.AddHours(16)) - e1.At(day.AddHours(16))) < 1e-9, (e4.At(day.AddHours(16)) - e1.At(day.AddHours(16))).ToString("+0.000"));

            SleepLog.Data = new SleepData { Nights = baseNights() };
            bool a1 = SleepLog.AddNap(day, day.AddHours(14.5), day.AddHours(14.5).AddMinutes(25));
            bool a2 = SleepLog.AddNap(day, day.AddHours(14.6), day.AddHours(14.6).AddMinutes(25));
            Check("AddNap refuses a nap that overlaps one already logged", a1 && !a2, "first " + a1 + ", overlapping " + a2);
            bool a3 = SleepLog.AddNap(day, day.AddHours(6.5), day.AddHours(6.9));
            Check("AddNap refuses a nap inside the main sleep", !a3, "" + a3);
            bool a6 = SleepLog.AddNap(day, day.AddHours(16.5), day.AddHours(16.5).AddMinutes(20));
            Check("AddNap accepts a second, separate nap", a6 && SleepLog.ForDay(day).NapSpans.Count == 2);

            SleepLog.Data = new SleepData { Nights = Repeat(T(23, 0), T(7, 0), 3, day.AddDays(-1)) };
            bool a5 = SleepLog.AddNap(day, day.AddHours(13), day.AddHours(13).AddMinutes(20));
            var e5 = EnergyModel.Build(SleepLog.Data.Nights, need, day);
            Check("nap on a skipped morning still counts today", a5 && e5.Naps.Count == 1);

            // ------------------------------------------------------------------ 4. planner
            Console.WriteLine("== 4. Plan by effort");
            Func<List<TaskItem>> mk = () => new List<TaskItem>
            {
                Task("Tidy desk", null, 0), Task("Anki insertion", "low", 20), Task("Review animation", "medium", 30), Task("Emails", "low", 30),
                Task("Write concept 14", "high", 45), Task("Deep reading", "high", 60), Task("Workout", "high", 20, "physical"), Task("Admin forms", "low", 30), Task("Slides", "medium", 45)
            };
            var en = normal;
            var plan = Planner.Plan(mk(), en, null, en.Wake);
            Console.WriteLine("  free day, planned at wake (" + C(en.Wake) + "); zones " + Z(en.Peak1) + " / " + Z(en.Dip) + " / " + Z(en.Peak2) + ", groggy until " + C(en.GroggyUntil));
            PrintPlan(en, plan);
            Check("everything placed", plan.All(x => x.Placed));
            Check("no two tasks overlap", !plan.Where(x => x.Placed).Any(a => plan.Where(x => x.Placed && x != a).Any(b => a.Start < b.Start.AddMinutes(b.Minutes) && b.Start < a.Start.AddMinutes(a.Minutes))));
            // the planner works in 5-minute steps, so a zone edge can be up to a minute or two off the step
            Check("high mental tasks are fully on a peak", plan.Where(x => x.Level == "high" && !x.Physical).All(x => PeakShare(en, x.Start, x.Minutes) > 0.97));
            Check("no high task inside the groggy stretch", plan.Where(x => x.Level == "high" && !x.Physical).All(x => x.Start >= en.GroggyUntil.AddMinutes(-2.5)));
            Check("low tasks are in the groggy stretch or the dip, never on a peak", plan.Where(x => x.Level == "low").All(x => !ZoneOf(en, x.Start, x.Minutes).StartsWith("peak")));
            Check("physical task 16:00-20:00", plan.Where(x => x.Physical).All(x => x.Start.Hour >= 16 && x.Start.AddMinutes(x.Minutes) <= day.AddHours(20)));
            double hi = plan.Where(x => x.Level == "high" && !x.Physical).Average(x => AvgE(en, x)), me = plan.Where(x => x.Level == "medium").Average(x => AvgE(en, x)), lo = plan.Where(x => x.Level == "low").Average(x => AvgE(en, x));
            Check("average energy under tasks: high > medium > low", hi > me && me > lo, hi.ToString("0.000") + " / " + me.ToString("0.000") + " / " + lo.ToString("0.000"));

            // planning later in the day: from now on, never before now or past bedtime
            foreach (var at in new[] { day.AddHours(14), day.AddHours(18.5), en.Bed.AddMinutes(-80) })
            {
                var p2 = Planner.Plan(mk(), en, null, at);
                Console.WriteLine("  planned at " + at.ToString("HH:mm") + " (bed " + en.Bed.ToString("HH:mm") + ")");
                PrintPlan(en, p2);
                Check("planned at " + at.ToString("HH:mm") + ": something placed, nothing before now or past bed",
                    p2.Any(x => x.Placed) && p2.Where(x => x.Placed).All(x => x.Start >= at && x.Start.AddMinutes(x.Minutes) <= en.Bed));
            }

            if (real != null)
            {
                var mine = new List<TaskItem> { Task("Writing", "high", 90), Task("VO in 11 Labs", "medium", 30), Task("Emails", "low", 20), Task("Tidy desk", null, 0) };
                var p3 = Planner.Plan(mine, real, null, real.Wake.AddMinutes(20));
                Console.WriteLine("  real night, planned 20 min after waking; zones " + Z(real.Peak1) + " / " + Z(real.Dip) + " / " + Z(real.Peak2) + ", groggy until " + C(real.GroggyUntil));
                PrintPlan(real, p3);
                Check("Writing (90m high) sits fully on the morning peak", PeakShare(real, p3[0].Start, 90) > 0.97, ZoneOf(real, p3[0].Start, 90) + ", peak is " + (int)(real.Peak1.To - real.Peak1.From).TotalMinutes + " min");
            }

            // the seeded routine chain from the brief: voice-over -> transcript -> studio first, then Writing on the peak
            var rs = new List<Routine>
            {
                new Routine { Id = "r1", Title = "Voice-over", Minutes = 20, Energy = "low", Prefer = "early" }, new Routine { Id = "r2", Title = "Transcript", Minutes = 2, Energy = "low", After = "r1" },
                new Routine { Id = "r3", Title = "Studio start", Minutes = 15, Energy = "medium", After = "r2" }, new Routine { Id = "r5", Title = "Writing", Minutes = 90, Energy = "high" },
                new Routine { Id = "r6", Title = "Workout", Minutes = 20, Energy = "high", Kind = "physical" }, new Routine { Id = "r7", Title = "YouTube Watch Later", Minutes = 20, Energy = "medium", Prefer = "dip" },
            };
            var rt = rs.Select(r => new TaskItem { Id = Guid.NewGuid().ToString("N"), Title = r.Title, Energy = r.Energy, Minutes = r.Minutes, Kind = r.Kind, Routine = r.Id, RoutineDay = "2026-11-21" }).ToList();
            var p4 = Planner.Plan(rt, en, rs, en.Wake);
            Console.WriteLine("  seeded routines, free day, planned at wake");
            PrintPlan(en, p4);
            Func<string, PlanItem> by = id => p4.First(x => x.Routine == id);
            Check("routine chain first, in order, then Writing fully on the peak, YouTube in the dip", by("r1").Start < by("r2").Start && by("r2").Start < by("r3").Start && by("r3").Start < by("r5").Start && PeakShare(en, by("r5").Start, 90) > 0.97 && ZoneOf(en, by("r7").Start, 20).StartsWith("dip"));

            var tasks = mk();
            var first = Planner.Plan(tasks, e0, null, e0.Wake);
            foreach (var it in first.Where(x => x.Placed)) { var t = tasks.First(x => x.Id == it.Id); t.SetDue(it.Start, true); t.Planned = true; }
            var nowNap = day.AddHours(13);
            var b6 = baseNights(); b6[b6.Count - 1].Naps = new List<SleepSpan> { SleepSpan.Make(day.AddHours(12.5), day.AddHours(12.5).AddMinutes(25)) };
            var e6 = EnergyModel.Build(b6, need, day);
            var re = Planner.Plan(tasks, e6, null, nowNap, true);
            Console.WriteLine("  re-plan at 13:00 after a 12:30 nap (tasks already started are kept)");
            PrintPlan(e6, re);
            Check("re-plan: nothing before now", re.Where(x => x.Placed).All(x => x.Start >= nowNap));
            Check("re-plan: only tasks that had not started are moved", re.All(x => first.First(f => f.Id == x.Id).Start > nowNap || !first.First(f => f.Id == x.Id).Placed), re.Count + " re-planned of " + first.Count);

            // a nap logged in the future part of an old plan: plan must avoid it
            var napLater = baseNights(); napLater[napLater.Count - 1].Naps = new List<SleepSpan> { SleepSpan.Make(day.AddHours(8.5), day.AddHours(9)) };
            var e7 = EnergyModel.Build(napLater, need, day);
            var p7 = Planner.Plan(mk(), e7, null, e7.Wake);
            Check("plan never puts a task inside a logged nap", p7.Where(x => x.Placed).All(x => !(x.Start < day.AddHours(9) && day.AddHours(8.5) < x.Start.AddMinutes(x.Minutes))));

            var p5 = Planner.Plan(new List<TaskItem> { Task("Thesis", "high", 240) }, en, null, en.Wake);
            Console.WriteLine("  a high task longer than the peak");
            PrintPlan(en, p5);
            // every 4-hour slot that is at least 80% on the peak; the one chosen must be the one with the most energy under it
            double bestE = Enumerable.Range(0, 40).Select(k => en.Wake.AddMinutes(5 * k)).Where(t => PeakShare(en, t, 240) >= 0.8).Max(t => AvgE(en, new PlanItem { Start = t, Minutes = 240 }));
            Check("a high task longer than the peak takes the highest-energy slot that is mostly on it", p5[0].Placed && PeakShare(en, p5[0].Start, 240) >= 0.8 && AvgE(en, p5[0]) >= bestE - 1e-9, AvgE(en, p5[0]).ToString("0.0000") + " vs best " + bestE.ToString("0.0000"));

            // ------------------------------------------------------------------ 4b. completion log (done.csv, days.csv)
            Console.WriteLine("== 4b. Completion log");
            {
                foreach (var f in new[] { "done.csv", "days.csv" }) { var fp = System.IO.Path.Combine(Store.Dir, f); if (System.IO.File.Exists(fp)) System.IO.File.Delete(fp); }
                var d0 = day; var at = d0.AddHours(10);
                var lgHi = Task("Write, the \"big\" one", "high", 60); lgHi.Planned = true; lgHi.SetDue(d0.AddHours(9.5), true); lgHi.Planned = true;
                var med = Task("Review", "medium", 30); var low = Task("Emails", "low", 20); var low2 = Task("Tidy", "low", 15);
                var lgRt = Task("Reading", "medium", 30); lgRt.Routine = "r1"; lgRt.RoutineDay = d0.ToString("yyyy-MM-dd");
                var later = Task("Next week", "high", 30); later.SetDue(d0.AddDays(7), false);
                TaskLog.Done(lgHi, at, d0, 52.5, "peak"); lgHi.Done = true;
                TaskLog.Done(med, at.AddHours(1), d0, 0, "between"); med.Done = true;
                TaskLog.Done(low, at.AddHours(2), d0, 0, "dip"); low.Done = true;
                TaskLog.Undone(low.Id, d0); low.Done = false; // unticked again: no longer counts as done
                var rows = TaskLog.ReadDone();
                Check("done.csv: one row per task that stayed done, title with comma and quotes intact", rows.Count == 2 && rows[0].Title == "Write, the \"big\" one" && rows[0].TimerMinutes == 52.5 && rows[0].Planned && rows[0].Zone == "peak", rows.Count + " rows");
                var all = new List<TaskItem> { lgHi, med, low, low2, lgRt, later };
                var sum = TaskLog.Summarise(d0, all, rows, 7.25, 1.5);
                Console.WriteLine("  " + sum.Done + " done of " + sum.Tasks + ", high " + sum.DoneBy[0] + "/" + sum.TotalBy[0] + ", routines " + sum.RoutinesDone + "/" + sum.RoutinesSent + ", on peak " + sum.HighDoneOnPeak);
                Check("day summary: 2 done of 5 (a task due next week is not counted)", sum.Done == 2 && sum.Open == 3 && sum.Tasks == 5);
                Check("day summary: by effort, routines, placed by Plan, high on a peak", sum.DoneBy[0] == 1 && sum.TotalBy[0] == 1 && sum.TotalBy[2] == 2 && sum.RoutinesSent == 1 && sum.RoutinesDone == 0 && sum.PlacedDone == 1 && sum.HighDoneOnPeak == 1);
                TaskLog.WriteDay(sum);
                var back = TaskLog.ReadDays();
                Check("days.csv reads back what was written", back.Count == 1 && back[0].Done == 2 && back[0].Tasks == 5 && back[0].TotalBy[2] == 2 && back[0].HighDoneOnPeak == 1);
            }

            // ------------------------------------------------------------------ 5. sleep-need suggestion
            Console.WriteLine("== 5. Sleep-need suggestion (simulated sleepers with a known need; \"run.ps1 sim\" runs 300 of each)");
            var late23 = day.AddHours(23);
            Func<double, int, double, double, double, double, int, NeedEstimate.Result> sleeper = (trueNeed, days, mean, sd, perHour, noise, seed) =>
            {
                List<Night> n; List<Feedback.Entry> c;
                Sleeper(new Random(seed), day, trueNeed, days, mean, sd, perHour, noise, false, out n, out c, 5);
                return NeedEstimate.Build(n, c, late23);
            };
            var clear = sleeper(7.5, 56, 7.3, 0.9, 1.5, 0.4, 7);
            Check("clear case (need 7h30, 56 days): suggests within 15 min, range contains 7h30", clear.Ready && Math.Abs(clear.Need - 7.5) <= 0.25 + 1e-9 && clear.Low <= 7.5 && clear.High >= 7.5, clear.Note);
            int hit = 0, ready5 = 0;
            for (int seed = 1; seed <= 40; seed++) { var r = sleeper(7.5, 56, 7.3, 0.9, 1.5, 0.4, seed); if (r.Ready) { ready5++; if (Math.Abs(r.Need - 7.5) <= 0.5 + 1e-9) hit++; } }
            Check("40 clear cases: most get a figure, and every figure is within 30 min", ready5 >= 30 && hit == ready5, ready5 + " of 40 suggested, " + hit + " within 30 min");
            var few = sleeper(7.5, 10, 7.3, 0.9, 1.5, 0.4, 7);
            Check("10 days: nothing suggested, and it says how many days it has", !few.Ready && few.Days == 10 && few.Note.Contains("10 so far"), few.Note);
            var flat = sleeper(7.5, 56, 7.3, 0.9, 0.0, 0.8, 7);
            Check("scores that do not follow sleep: nothing suggested", !flat.Ready, flat.Note);
            var under = sleeper(9.25, 56, 7.2, 0.6, 1.5, 0.4, 7);
            Check("someone who never gets enough: no figure, told they may need more", !under.Ready && under.Note.Contains("may need more"), under.Note);
            int lowBall = 0;
            for (int seed = 1; seed <= 40; seed++) { var r = sleeper(6.25, 56, 7.0, 0.9, 1.5, 0.4, seed); if (r.Ready && r.Need < NeedEstimate.Floor - 1e-9) lowBall++; }
            Check("a true need of 6h15: never suggests under the 7h floor", lowBall == 0);
            {
                List<Night> n; List<Feedback.Entry> c;
                Sleeper(new Random(7), day, 7.5, 56, 7.3, 0.9, 1.5, 0.4, false, out n, out c, 5);
                foreach (var x in n) x.Assumed = true;
                Check("assumed nights (skipped mornings) are not used", NeedEstimate.Build(n, c, late23).Days == 0);
            }
            var mine2 = NeedEstimate.Build(realNights, Feedback.Read(), DateTime.Now);
            Console.WriteLine("  your own data: " + mine2.Note);

            Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
        }
    }
}
