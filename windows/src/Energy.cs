// Corner - energy curve: sleep log, the alertness model, and the Energy tab.
// Task tags, routines, the roster and Plan are in Plan.cs.
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
    // ---------------------------------------------------------------- the model

    /// Two-process model of alertness (Borbely): sleep pressure S that builds while awake and
    /// clears during sleep, plus a circadian rhythm C with a 24 h and a 12 h component.
    /// The 12 h component is what produces the post-lunch dip. Everything tunable is here.
    public static class EnergyModel
    {
        // --- sleep need and debt
        public const double DefaultNeed = 8.0;      // hours of sleep needed per night (overridden by "SleepNeed" in settings.json)
        public const int DebtNights = 14;           // how many nights count towards sleep debt
        public const int DebtEstimateMin = 3;       // nights logged in that window before the unlogged ones are counted as an average night (15 = never)
        public const double DebtCap = 12.0;         // debt (hours) at which its effects stop growing
        public const double DebtDrop = 0.10;        // how far full debt lowers the whole curve
        public const double DebtDipGain = 0.40;     // how much full debt strengthens the 12 h rhythm (deepens the dip)
        public const double DebtPressure = 0.10;    // extra sleep pressure at wake under full debt

        // --- process S: homeostatic sleep pressure, 0 (fully rested) to 1
        public const double RiseTau = 18.2;         // hours; how fast pressure builds while awake
        public const double DecayTau = 4.2;         // hours; how fast pressure clears while asleep
        public const double BedPressure = 0.75;     // assumed pressure when falling asleep
        public const double RestedPressure = 0.11;  // pressure after a full night; the curve is not penalised below this
        public const double MaxWakePressure = 0.50; // cap on pressure at wake after a very short night
        public const double PressureWeight = 0.55;  // how strongly pressure pulls energy down

        // --- process C: circadian rhythm (clock hours, on a morning when you wake at your usual time)
        public const double Base = 0.62;            // resting level of the curve
        public const double Amp24 = 0.12;           // size of the 24 h rhythm
        public const double Peak24 = 17.0;          // clock hour where the 24 h rhythm peaks
        public const double Amp12 = 0.14;           // size of the 12 h rhythm
        public const double Peak12 = 8.75;          // clock hour of its morning peak; with the other terms the dip bottoms out near 14:30
        public const double WakeShift = 0.5;        // waking an hour later than your usual time moves the rhythm (and the dip) this many hours later
        public const double ShiftMax = 2.0;         // largest shift allowed, hours

        // --- sleep inertia (morning grogginess)
        public const double InertiaDepth = 0.25;    // how far energy is held down right at wake
        public const double InertiaTau = 0.6;       // hours; how fast grogginess clears
        public const double GroggyDone = 0.04;      // grogginess counts as over below this

        // --- zones
        public const double PeakBand = 0.40;        // a peak zone covers the top 40% of the peak-to-dip range
        public const double DipBand = 0.10;         // the dip zone covers the bottom 10% of it (about 1.5-2 h)
        public const double MinProminence = 0.03;   // a dip shallower than this is ignored
        public const double MinDay = 10, MaxDay = 20; // plausible hours awake; outside this, bedtime = wake + 24 - need

        // --- naps
        public const double NapShortMax = 30;          // minutes; a nap longer than this leaves a heavier groggy stretch
        public const double NapInertiaShort = 0.06;    // how far energy is held down right after a short nap
        public const double NapInertiaTauShort = 0.15; // hours; how fast that clears (gone in about 15-20 min)
        public const double NapInertiaLong = 0.16;     // the same after a long nap
        public const double NapInertiaTauLong = 0.40;  // hours (gone in about 45 min)
        public const double NapLift = 0.06;            // lift after a nap, on top of the sleep pressure it cleared
        public const double NapLiftFull = 20;          // minutes of nap that give the full lift
        public const double NapLiftTau = 2.5;          // hours; how long the lift lasts
        public const double LateNapHour = 16.0;        // nap time after this clock hour pushes tonight's wind-down later
        public const double LateNapFactor = 1.5;       // minutes later per minute napped after that hour
        public const double LateNapMax = 90;           // the most it can push, minutes

        /// Sleep pressure at the final wake-up: it clears during every sleep period and builds again in every waking gap.
        static double WakePressure(Night night)
        {
            double s = BedPressure;
            var ps = night.Sleep;
            for (int i = 0; i < ps.Count; i++)
            {
                s *= Math.Exp(-ps[i].Hours / DecayTau);
                if (i + 1 < ps.Count) s = 1 - (1 - s) * Math.Exp(-(ps[i + 1].From - ps[i].To).TotalHours / RiseTau);
            }
            return s;
        }

        public static EnergyDay Build(IList<Night> nights, double need, DateTime day, Func<double, double> correction = null)
        {
            day = day.Date;
            var valid = (nights ?? new List<Night>()).Where(x => x != null && x.Valid && x.WokeAt.Date <= day).OrderBy(x => x.WokeAt).ToList();
            if (valid.Count == 0) return null;
            var last = valid[valid.Count - 1];
            var recent = valid.Where(x => x.WokeAt.Date > day.AddDays(-DebtNights)).ToList();

            double shortfall = recent.Sum(x => need - x.TotalHours); // every sleep period and nap of the day counts
            int logged = recent.Select(x => x.WokeAt.Date).Distinct().Count();
            Func<double, double> share = hours => Math.Min(Math.Max(0, hours), DebtCap) / DebtCap; // debt as a part of DebtCap, 0 to 1

            // The rhythm is anchored to your usual wake time (the median of the earlier nights), so on a normal
            // night the dip sits near 14:30. Waking later or earlier than usual moves it part of the way.
            var earlier = recent.Where(x => x != last).Select(x => x.WokeAt.TimeOfDay.TotalHours).OrderBy(x => x).ToList();
            double usualWake = earlier.Count > 0 ? earlier[earlier.Count / 2] : last.WokeAt.TimeOfDay.TotalHours;
            double shift = Math.Max(-ShiftMax, Math.Min(ShiftMax, (last.WokeAt.TimeOfDay.TotalHours - usualWake) * WakeShift));

            var e = new EnergyDay { Last = last, Need = need, DebtLogged = logged };
            e.Wake = day + last.WokeAt.TimeOfDay;
            e.Bed = day + last.AsleepAt.TimeOfDay;
            if (e.Bed <= e.Wake) e.Bed = e.Bed.AddDays(1);
            double span = (e.Bed - e.Wake).TotalHours;
            if (span < MinDay || span > MaxDay) { e.Bed = e.Wake.AddHours(24 - need); span = 24 - need; }

            // today's naps (only when last night's record is today's)
            if (last.WokeAt.Date == day) e.Naps = last.NapSpans.Where(x => x.To > e.Wake).ToList();
            // only the part of a nap that falls after LateNapHour counts, so a nap that just crosses it barely moves bedtime
            var lateFrom = day.AddHours(LateNapHour);
            double lateMinutes = e.Naps.Where(x => x.To.Date == day && x.To > lateFrom).Sum(x => (x.To - (x.From > lateFrom ? x.From : lateFrom)).TotalMinutes);
            e.BedPush = Math.Min(LateNapMax, lateMinutes * LateNapFactor);
            if (e.BedPush > 0) { e.Bed = e.Bed.AddMinutes(e.BedPush); span += e.BedPush / 60; }

            // Debt as it stood when you woke, before today's naps. A nap pays its minutes back from the moment it ends,
            // so logging one changes the rest of the day and never the hours before it.
            double napHours = e.Naps.Sum(x => x.Hours);
            double beforeNaps = shortfall + napHours;
            // Nights in the window that were never logged are counted as an average logged night (today's naps left out of
            // the average, so a nap pays back its own minutes and no more). With too few nights logged there is no average to trust.
            if (logged >= DebtEstimateMin && logged < DebtNights) e.DebtEstimate = (DebtNights - logged) * beforeNaps / logged;
            beforeNaps += e.DebtEstimate;
            e.DebtHours = Math.Max(0, beforeNaps - napHours);
            double d = share(beforeNaps);

            double s0 = Math.Min(MaxWakePressure, WakePressure(last) + DebtPressure * d);
            double wakeHour = e.Wake.TimeOfDay.TotalHours, dt = EnergyDay.StepMin / 60.0;
            int n = (int)(span * 60 / EnergyDay.StepMin) + 1;
            e.Samples = new double[n]; e.Raw = new double[n];
            int nAll = Math.Max(n, 24 * 60 / EnergyDay.StepMin + 1);
            e.All = new double[nAll];    // the same curve carried on past bedtime to the next wake-up, for the chart only
            var plain = new double[n];   // the same day without any nap: the peak and dip zones are read from this
            double s = s0, sPlain = s0;
            for (int i = 0; i < nAll; i++)
            {
                double t = i * dt, h = wakeHour + t;
                var at = e.TimeAt(i);
                if (i > 0)
                {   // pressure builds while awake and clears during a nap
                    sPlain = 1 - (1 - sPlain) * Math.Exp(-dt / RiseTau);
                    s = e.Naps.Any(x => at > x.From && at <= x.To) ? s * Math.Exp(-dt / DecayTau) : 1 - (1 - s) * Math.Exp(-dt / RiseTau);
                }
                Func<double, double> rhythmAt = dd => Base
                    + Amp24 * Math.Cos(2 * Math.PI * (h - Peak24 - shift) / 24)
                    + Amp12 * (1 + DebtDipGain * dd) * Math.Cos(2 * Math.PI * (h - Peak12 - shift) / 12)
                    - InertiaDepth * Math.Exp(-t / InertiaTau)
                    - DebtDrop * dd;
                double rhythmPlain = rhythmAt(d);
                double rhythm = e.Naps.Count == 0 ? rhythmPlain : rhythmAt(share(beforeNaps - e.Naps.Where(x => x.To <= at).Sum(x => x.Hours)));
                double nap = 0;
                foreach (var x in e.Naps)
                {   // after a nap: a short groggy stretch, then a lift that fades over a few hours
                    if (at < x.To) continue;
                    double since = (at - x.To).TotalHours; bool longNap = x.Minutes > NapShortMax;
                    nap += NapLift * Math.Min(1, x.Minutes / NapLiftFull) * Math.Exp(-since / NapLiftTau)
                         - (longNap ? NapInertiaLong : NapInertiaShort) * Math.Exp(-since / (longNap ? NapInertiaTauLong : NapInertiaTauShort));
                }
                double fit = correction != null ? correction(t) : 0; // learned from your check-ins, by hours since waking (Checks.cs)
                double raw = Math.Max(0, Math.Min(1, rhythm - PressureWeight * (s - RestedPressure) + nap));
                e.All[i] = Math.Max(0, Math.Min(1, raw + fit));
                if (i >= n) continue;
                plain[i] = Math.Max(0, Math.Min(1, rhythmPlain - PressureWeight * (sPlain - RestedPressure) + fit));
                e.Raw[i] = raw;
                e.Samples[i] = e.All[i];
            }
            e.GroggyUntil = e.Wake.AddHours(-InertiaTau * Math.Log(GroggyDone / InertiaDepth));
            FindZones(e, plain);
            return e;
        }

        static void FindZones(EnergyDay e, double[] s)
        {
            int n = s.Length;
            if (n < 3) return;

            // The dip is the low point that stands out most: the one lying deepest below the highest point on each side of it.
            // The peaks are those two highest points. (Taking "the first bump" as the morning peak is not safe: once the
            // check-ins tune the curve it can have small bumps of its own, and the real dip was then lost.)
            var before = new double[n]; var after = new double[n];
            before[0] = s[0]; for (int i = 1; i < n; i++) before[i] = Math.Max(before[i - 1], s[i]);
            after[n - 1] = s[n - 1]; for (int i = n - 2; i >= 0; i--) after[i] = Math.Max(after[i + 1], s[i]);
            int dip = -1, m1 = 0, m2 = -1; double depth = 0;
            for (int i = 1; i < n - 1; i++)
            {
                double p = Math.Min(before[i], after[i]) - s[i];
                if (p > depth) { depth = p; dip = i; }
            }
            if (depth < MinProminence) dip = -1;
            if (dip >= 0)
            {
                for (int i = 0; i <= dip; i++) if (s[i] > s[m1]) m1 = i;
                m2 = dip; for (int i = dip; i < n; i++) if (s[i] > s[m2]) m2 = i;
            }
            if (dip < 0)
            {
                // one hump only: a single peak zone measured against the end of the day
                int top = 0; for (int i = 0; i < n; i++) if (s[i] > s[top]) top = i;
                e.Peak1 = Run(e, s,top, s[top] - PeakBand * (s[top] - s[n - 1]), 0, n - 1, true, true);
                return;
            }
            e.Peak1 = Run(e, s,m1, s[m1] - PeakBand * (s[m1] - s[dip]), 0, dip, true, true);
            if (e.Peak1.From < e.GroggyUntil && e.GroggyUntil < e.Peak1.To) e.Peak1.From = e.GroggyUntil; // not a peak while still groggy
            e.Peak2 = Run(e, s,m2, s[m2] - PeakBand * (s[m2] - s[dip]), dip, n - 1, true, true);
            e.Dip = Run(e, s,dip, s[dip] + DipBand * (Math.Min(s[m1], s[m2]) - s[dip]), m1, m2, false, false);
            // the moment the curve has fallen half way from the top of the first peak to the bottom of the dip
            double half = (s[m1] + s[dip]) / 2;
            for (int i = m1; i <= dip; i++) if (s[i] <= half) { e.HalfDown = e.TimeAt(i); break; }
        }

        // Grows a zone outwards from index c while the curve stays above (or below) the threshold.
        static Zone Run(EnergyDay e, double[] s, int c, double thr, int lo, int hi, bool above, bool peak)
        {
            int a = c, b = c;
            while (a > lo && (above ? s[a - 1] >= thr : s[a - 1] <= thr)) a--;
            while (b < hi && (above ? s[b + 1] >= thr : s[b + 1] <= thr)) b++;
            return new Zone { From = e.TimeAt(a), To = e.TimeAt(b), Peak = peak };
        }

        /// Test helper: the same night repeated, to see what the model does with it.
        public static EnergyDay Sample(TimeSpan asleep, TimeSpan woke, int nights, double need, DateTime day)
        {
            var list = new List<Night>();
            for (int i = 0; i < nights; i++) list.Add(SleepLog.Build(asleep, woke, day.Date.AddDays(-i)));
            return Build(list, need, day);
        }
    }

    public class Zone
    {
        public DateTime From, To;
        public bool Peak;
    }

    public class EnergyDay
    {
        public const int StepMin = 5;
        public DateTime Wake, Bed, GroggyUntil;
        public double DebtHours, Need;
        public int DebtLogged;       // nights logged in the debt window
        public double DebtEstimate;  // hours of DebtHours that stand for the nights not logged (0 when none are estimated)
        public Night Last;
        public double[] Samples;
        public double[] Raw; // the same curve before any tuning from check-ins
        public double[] All; // Samples carried on to 24 hours after waking (what the curve would do if you stayed up); drawn on the chart
        public DateTime? HalfDown; // half way down from the first peak to the dip: time to leave high-effort work
        public DateTime NextWake { get { return Wake.AddMinutes((All.Length - 1) * StepMin); } }

        public double AllAt(DateTime t)
        {
            double x = (t - Wake).TotalMinutes / StepMin;
            if (x <= 0) return All[0];
            if (x >= All.Length - 1) return All[All.Length - 1];
            int i = (int)x;
            return All[i] + (All[i + 1] - All[i]) * (x - i);
        }
        public Zone Peak1, Dip, Peak2;
        public List<SleepSpan> Naps = new List<SleepSpan>(); // today's logged naps
        public double BedPush;                               // minutes a late nap pushes tonight's wind-down

        public DateTime TimeAt(int i) { return Wake.AddMinutes(i * StepMin); }

        public double At(DateTime t)
        {
            double x = (t - Wake).TotalMinutes / StepMin;
            if (x <= 0) return Samples[0];
            if (x >= Samples.Length - 1) return Samples[Samples.Length - 1];
            int i = (int)x;
            return Samples[i] + (Samples[i + 1] - Samples[i]) * (x - i);
        }

        /// The untuned model's value at a moment, for the check-in log.
        public double RawAt(DateTime t)
        {
            return Raw[(int)Math.Max(0, Math.Min(Raw.Length - 1, Math.Round((t - Wake).TotalMinutes / StepMin)))];
        }

        /// am/pm or 24-hour display. Starts from the Windows setting; "Clock12" in settings.json overrides it.
        public static bool Use12 = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.IndexOf('h') >= 0;

        public static string Clock(DateTime t)
        {
            var inv = CultureInfo.InvariantCulture;
            return Use12 ? t.ToString("h:mm", inv) + (t.Hour < 12 ? "am" : "pm") : t.ToString("HH:mm", inv);
        }

        /// Short label for a whole hour on the chart axis: "14" or "2pm".
        public static string HourLabel(DateTime t)
        {
            return Use12 ? t.ToString("%h", CultureInfo.InvariantCulture) + (t.Hour < 12 ? "am" : "pm") : t.Hour.ToString();
        }
        static string Span(Zone z) { return Clock(z.From) + "–" + Clock(z.To); }

        /// One line for right now, e.g. "Peak until 11:40 · dip 14:10–15:30".
        public string Summary(DateTime now)
        {
            return ZoneLine(now) + (BedPush >= 5 ? " · late nap: wind-down about " + (int)Math.Round(BedPush) + " min later" : "");
        }

        /// The short form, without the late-nap note (used beside the tabs, where there is little room).
        public string ZoneLine(DateTime now)
        {
            if (Peak1 == null) return "Bed around " + Clock(Bed);
            if (now < Peak1.To)
                return (now < Peak1.From ? "Peak from " + Clock(Peak1.From) : "Peak until " + Clock(Peak1.To)) + (Dip != null ? " · dip " + Span(Dip) : "");
            if (Dip != null && now < Dip.From) return "Dip " + Span(Dip) + (Peak2 != null ? " · peak " + Span(Peak2) : "");
            if (Dip != null && now < Dip.To) return "Dip until " + Clock(Dip.To) + (Peak2 != null ? " · next peak " + Clock(Peak2.From) : "");
            if (Peak2 != null && now < Peak2.From) return "Second peak " + Span(Peak2);
            if (Peak2 != null && now < Peak2.To) return "Second peak until " + Clock(Peak2.To);
            return now < Bed ? "Winding down · bed around " + Clock(Bed) : "Past bedtime";
        }
    }

    // ---------------------------------------------------------------- sleep log (sleep.json, beside tasks.json)

    /// One stretch of sleep: a part of the night, or a nap.
    [DataContract]
    public class SleepSpan
    {
        [DataMember] public string Asleep;
        [DataMember] public string Woke;

        public DateTime From { get { return Night.P(Asleep); } }
        public DateTime To { get { return Night.P(Woke); } }
        public double Hours { get { return (To - From).TotalHours; } }
        public double Minutes { get { return (To - From).TotalMinutes; } }
        public bool Valid { get { return From > DateTime.MinValue && To > From; } }

        public static SleepSpan Make(DateTime from, DateTime to)
        {
            return new SleepSpan { Asleep = from.ToString(Night.Fmt, CultureInfo.InvariantCulture), Woke = to.ToString(Night.Fmt, CultureInfo.InvariantCulture) };
        }
    }

    [DataContract]
    public class Night
    {
        [DataMember] public string Asleep; // start of the first sleep period
        [DataMember] public string Woke;   // the final wake-up of the main sleep
        [DataMember] public bool Assumed;  // true when the check-in was skipped and the previous night was reused
        // Both left out of the file when not used, so a night with one period is written exactly as before.
        [DataMember(EmitDefaultValue = false)] public List<SleepSpan> Periods; // every period, only when the night was split
        [DataMember(EmitDefaultValue = false)] public List<SleepSpan> Naps;    // naps taken during the day this night leads into

        internal const string Fmt = "yyyy-MM-ddTHH:mm";
        internal static DateTime P(string s)
        {
            DateTime d;
            return DateTime.TryParseExact(s ?? "", Fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue;
        }
        public DateTime AsleepAt { get { return P(Asleep); } }
        public DateTime WokeAt { get { return P(Woke); } }

        /// The periods of the main sleep, in order. One period unless the night was split.
        public List<SleepSpan> Sleep
        {
            get
            {
                if (Periods != null && Periods.Count > 1 && Periods.All(p => p != null && p.Valid))
                {
                    var ps = Periods.OrderBy(p => p.From).ToList();
                    bool ordered = true;
                    for (int i = 1; i < ps.Count; i++) if (ps[i].From < ps[i - 1].To) ordered = false;
                    if (ordered && ps[0].From >= AsleepAt && ps[ps.Count - 1].To <= WokeAt) return ps;
                }
                return new List<SleepSpan> { SleepSpan.Make(AsleepAt, WokeAt) };
            }
        }

        /// The day's naps in order. Naps that overlap (the same one entered twice) are joined, so no minute is counted twice.
        public List<SleepSpan> NapSpans
        {
            get
            {
                var list = new List<SleepSpan>();
                foreach (var x in (Naps ?? new List<SleepSpan>()).Where(x => x != null && x.Valid).OrderBy(x => x.From))
                {
                    var prev = list.Count > 0 ? list[list.Count - 1] : null;
                    if (prev == null || x.From > prev.To) list.Add(x);
                    else if (x.To > prev.To) list[list.Count - 1] = SleepSpan.Make(prev.From, x.To);
                }
                return list;
            }
        }

        public double Hours { get { return Sleep.Sum(p => p.Hours); } }                 // main sleep, waking gaps left out
        public double NapHours { get { return NapSpans.Sum(p => p.Hours); } }
        public double TotalHours { get { return Hours + NapHours; } }
        public bool Valid { get { return AsleepAt > DateTime.MinValue && WokeAt > DateTime.MinValue && Hours >= 1 && (WokeAt - AsleepAt).TotalHours <= 16; } }

        public static Night Make(DateTime asleep, DateTime woke, bool assumed)
        {
            return new Night { Asleep = asleep.ToString(Fmt, CultureInfo.InvariantCulture), Woke = woke.ToString(Fmt, CultureInfo.InvariantCulture), Assumed = assumed };
        }
    }

    [DataContract]
    public class SleepData
    {
        [DataMember] public List<Night> Nights;
        [DataMember] public string AskedOn; // the day the check-in was last answered or skipped
    }

    public static class SleepLog
    {
        public const int Keep = 400;         // nights kept (sleep debt looks at the last 14; the history in Settings at the last 30)
        public const int DayStartsAt = 4;    // 04:00: before this, "today" is still yesterday

        public static SleepData Data = new SleepData { Nights = new List<Night>() };
        static string File { get { return IO.Path.Combine(Store.Dir, "sleep.json"); } }

        public static DateTime Today(DateTime now) { return now.AddHours(-DayStartsAt).Date; }

        public static void Load()
        {
            try
            {
                if (!IO.File.Exists(File)) return;
                using (var fs = IO.File.OpenRead(File))
                {
                    var d = (SleepData)new DataContractJsonSerializer(typeof(SleepData)).ReadObject(fs);
                    if (d == null) return;
                    if (d.Nights == null) d.Nights = new List<Night>();
                    d.Nights.RemoveAll(n => n == null || !n.Valid);
                    Data = d;
                }
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        static void Save()
        {
            try
            {
                string bak = IO.Path.Combine(Store.Dir, "sleep.before-split.json"); // one-off copy before the first write by the split-sleep version
                if (IO.File.Exists(File) && !IO.File.Exists(bak)) IO.File.Copy(File, bak);
                string tmp = File + ".tmp";
                using (var fs = IO.File.Create(tmp)) new DataContractJsonSerializer(typeof(SleepData)).WriteObject(fs, Data);
                if (IO.File.Exists(File)) Store.ReplaceSafely(tmp, File); else IO.File.Move(tmp, File);
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        public static Night ForDay(DateTime day) { return Data.Nights.LastOrDefault(n => n.WokeAt.Date == day.Date); }
        public static Night Latest { get { return Data.Nights.OrderBy(n => n.WokeAt).LastOrDefault(); } }

        public static bool NeedsCheckIn(DateTime now)
        {
            var day = Today(now);
            return ForDay(day) == null && Data.AskedOn != day.ToString("yyyy-MM-dd");
        }

        /// A night that ends on `day` at clock time `woke`, having started at clock time `asleep`. Null if implausible.
        public static Night Build(TimeSpan asleep, TimeSpan woke, DateTime day)
        {
            DateTime w = day.Date + woke, a = day.Date + asleep;
            if (a >= w) a = a.AddDays(-1);
            var n = Night.Make(a, w, false);
            return n.Valid ? n : null;
        }

        /// A night in one to three periods, given as clock times in order; the last one ends on `day`. Null if implausible.
        public static Night BuildSplit(IList<TimeSpan> asleep, IList<TimeSpan> woke, DateTime day)
        {
            int n = asleep.Count;
            if (n == 0 || woke.Count != n) return null;
            var spans = new SleepSpan[n];
            DateTime next = DateTime.MaxValue; // start of the period that follows
            for (int i = n - 1; i >= 0; i--)
            {
                DateTime w = day.Date + woke[i];
                while (w > next) w = w.AddDays(-1);
                DateTime a = w.Date + asleep[i];
                if (a >= w) a = a.AddDays(-1);
                if ((w - a).TotalHours > 16) return null;
                spans[i] = SleepSpan.Make(a, w); next = a;
            }
            var night = Night.Make(spans[0].From, spans[n - 1].To, false);
            if (n > 1) night.Periods = spans.ToList();
            return night.Valid && night.Sleep.Count == n ? night : null;
        }

        /// Adds a nap to the given day (creating that day's night from the most recent one if it was never entered).
        public static bool AddNap(DateTime day, DateTime from, DateTime to)
        {
            if (ForDay(day) == null) Skip(day);
            var night = ForDay(day);
            if (night == null || to <= from) return false;
            if (from < night.WokeAt) return false;                                // inside the night's sleep: already counted there
            if (night.NapSpans.Any(x => from < x.To && x.From < to)) return false; // overlaps a nap already logged: it would count twice
            if (night.Naps == null) night.Naps = new List<SleepSpan>();
            night.Naps.Add(SleepSpan.Make(from, to));
            night.Naps = night.NapSpans;
            Save();
            return true;
        }

        public static void RemoveNap(DateTime day, SleepSpan nap)
        {
            var night = ForDay(day);
            if (night == null || night.Naps == null) return;
            night.Naps.RemoveAll(x => x == null || !x.Valid || (x.From < nap.To && nap.From < x.To)); // everything inside the nap shown
            if (night.Naps.Count == 0) night.Naps = null;
            Save();
        }

        // IMPORT HOOK: a future import (phone, watch, a CSV) only needs to turn each night into
        // Night.Make(asleep, woke, false) (or BuildSplit for a night in parts) and pass it to Record.
        public static void Record(Night n)
        {
            var old = ForDay(n.WokeAt.Date);
            if (old != null && n.Naps == null) n.Naps = old.Naps; // correcting the night keeps the day's naps
            Data.Nights.RemoveAll(x => x.WokeAt.Date == n.WokeAt.Date);
            Data.Nights.Add(n);
            Data.Nights = Data.Nights.OrderBy(x => x.WokeAt).ToList();
            if (Data.Nights.Count > Keep) Data.Nights.RemoveRange(0, Data.Nights.Count - Keep);
            Data.AskedOn = n.WokeAt.Date.ToString("yyyy-MM-dd");
            Save();
        }

        /// Check-in skipped: reuse the most recent night's times for today.
        public static void Skip(DateTime day)
        {
            var last = Latest;
            Night n = null;
            if (last != null)
            {   // the same periods, moved to today (a split night keeps its split; naps are not copied)
                double days = (day.Date - last.WokeAt.Date).TotalDays;
                var ps = last.Sleep.Select(p => SleepSpan.Make(p.From.AddDays(days), p.To.AddDays(days))).ToList();
                n = Night.Make(ps[0].From, ps[ps.Count - 1].To, true);
                if (ps.Count > 1) n.Periods = ps;
                if (!n.Valid) n = null;
            }
            if (n != null) Record(n);
            Data.AskedOn = day.Date.ToString("yyyy-MM-dd");
            Save();
        }

        static readonly Regex ClockRx = new Regex(@"^\s*(?:at\s+)?(\d{1,2})(?:\s*[:.]?\s*(\d{2}))?\s*(am|pm|a|p)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// Loose clock times: "11:30pm", "23:30", "6:15", "615", "6".
        /// Without am/pm, a bedtime of 6-11 means the evening and a wake time means the morning.
        public static bool TryParseClock(string text, bool waking, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            string s = (text ?? "").Trim().ToLowerInvariant();
            if (s == "midnight") return true;
            if (s == "noon") { time = TimeSpan.FromHours(12); return true; }
            var m = ClockRx.Match(s);
            if (!m.Success) return false;
            int h = int.Parse(m.Groups[1].Value), min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            if (min > 59) return false;
            if (m.Groups[3].Success)
            {
                if (h < 1 || h > 12) return false;
                if (m.Groups[3].Value[0] == 'p') { if (h != 12) h += 12; } else if (h == 12) h = 0;
            }
            else
            {
                if (h > 23) return false;
                if (!waking) { if (h == 12) h = 0; else if (h >= 6 && h <= 11) h += 12; }
            }
            time = new TimeSpan(h, min, 0);
            return true;
        }
    }

    // ---------------------------------------------------------------- the Energy tab

    partial class Shell
    {
        EnergyDay energy;
        NeedEstimate.Result needHint; // null until the first refresh
        int tab; // 0 = tasks, 1 = energy
        TextBlock tabTasks, tabEnergy, energyLine;
        Border checkIn, tasksAdd;
        ScrollViewer tasksScroll, energyScroll;
        StackPanel energyView;


        FrameworkElement BuildBody(ScrollViewer tasks)
        {
            tasksScroll = tasks;
            energyView = new StackPanel { Margin = new Thickness(20, 2, 20, 14) };
            energyScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false, Content = energyView, Visibility = Visibility.Collapsed
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(tasks); g.Children.Add(energyScroll);
            bodyGrid = g;
            return g;
        }

        void SetTab(int i)
        {
            tab = i;
            tabTasks.Foreground = i == 0 ? T.Text : T.Muted; tabTasks.FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal;
            tabEnergy.Foreground = i == 1 ? T.Text : T.Muted; tabEnergy.FontWeight = i == 1 ? FontWeights.SemiBold : FontWeights.Normal;
            tabLineTasks.Visibility = i == 0 ? Visibility.Visible : Visibility.Hidden; tabLineEnergy.Visibility = i == 1 ? Visibility.Visible : Visibility.Hidden;
            if (full)
            {   // Full Mode: no tabs; tasks on the left, energy on the right
                tabsRow.Visibility = Visibility.Collapsed;
                tasksAdd.Visibility = Visibility.Visible; tasksScroll.Visibility = Visibility.Visible; energyScroll.Visibility = Visibility.Visible;
                MoveEnergy(mainGrid, 1); energyScroll.Width = FullW / 2; // the second of two equal columns
                LayoutTop(); DrawEnergy(); FocusLayout();
                return;
            }
            tabsRow.Visibility = Visibility.Visible; MoveEnergy(bodyGrid, 0); energyScroll.Width = double.NaN; LayoutTop();
            var tasksOn = i == 0 ? Visibility.Visible : Visibility.Collapsed;
            tasksAdd.Visibility = tasksOn; tasksScroll.Visibility = tasksOn; energyChip.Visibility = tasksOn;
            energyScroll.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
            FocusLayout(); // next task, action row, and whether the add box and list are open (Focus.cs)
            if (i == 1) DrawEnergy();
        }

        /// Recomputes today's curve. `opening` = the panel is just being shown.
        void RefreshEnergy(bool opening)
        {
            SleepLog.Load();
            EnergyDay.Use12 = Settings.Get("Clock12", EnergyDay.Use12 ? 1 : 0) > 0.5;
            var now = Now();
            energy =EnergyModel.Build(SleepLog.Data.Nights, Settings.Get("SleepNeed", EnergyModel.DefaultNeed), SleepLog.Today(now), Tuning.Active(now));
            needHint = NeedEstimate.Build(SleepLog.Data.Nights, Feedback.Read(), now); // what the check-ins say about your sleep need (Need.cs)
            energyLine.Text = energy == null ? "" : energy.ZoneLine(now);
            energyDot.Fill = ZoneBrush(now); energyDot.Visibility = energy == null ? Visibility.Collapsed : Visibility.Visible;
            if (opening)
            {
                chartOffset = -1; // the chart starts centred on now each time the panel opens (UiSleep.cs)
                EnsureRoutines(SleepLog.Today(now)); // today's routines join the task list (Plan.cs)
                SetTab(0);
                if (SleepLog.NeedsCheckIn(now)) OpenCheckIn(false); else checkIn.Visibility = Visibility.Collapsed;
                ShowFeedback(now); // sharp / ok / foggy, at most three times a day (Plan.cs)
            }
            else if (tab == 1 || full) DrawEnergy();
        }

        // ------------------------------------------------------------ morning check-in


        /// A clock-time box that takes digits and ":" only, with an am/pm switch beside it.
        /// The switch follows what is typed (a bedtime of 11 is pm, of 1 is am) until it is clicked.
        class TimeEntry
        {
            public const double HostWidth = 92;
            public readonly Border Host;
            public readonly TextBox Box;
            readonly TextBlock tag;
            readonly bool waking, daytime;
            bool pm, manual, fixing;
            static readonly Regex Rx = new Regex(@"^(\d{1,2})(?::?(\d{2}))?$");

            public TimeEntry(string hintText, bool waking, bool daytime = false)
            {
                this.waking = waking; this.daytime = daytime;
                Box = Shell.Box(12.5); Box.MaxLength = 5;
                var hint = new TextBlock { Text = hintText, FontSize = Z(12), Foreground = T.Faint, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 1) };
                var g = new Grid { Width = 44 }; g.Children.Add(hint); g.Children.Add(Box);
                tag = new TextBlock { FontSize = Z(11), FontWeight = SemiSmall, Foreground = T.Strong, Cursor = Cursors.Hand, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 2, 1) };
                tag.MouseLeftButtonDown += (s, e) => { e.Handled = true; manual = true; SetPm(!pm); };
                var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(g); sp.Children.Add(tag);
                Host = new PillBorder { Width = HostWidth, Background = T.Field, BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Padding = new Thickness(10, 4, 7, 5), Margin = new Thickness(0, 0, 6, 6), Child = sp, Cursor = Cursors.IBeam };
                Box.GotKeyboardFocus += (s, e) => Host.BorderBrush = T.Strong;   // the box being typed in is outlined
                Box.LostKeyboardFocus += (s, e) => Host.BorderBrush = Brushes.Transparent;
                SetPm(!waking);
                Box.TextChanged += (s, e) =>
                {
                    if (fixing) return;
                    string clean = new string(Box.Text.Where(ch => char.IsDigit(ch) || ch == ':').ToArray());
                    if (clean != Box.Text) { fixing = true; Box.Text = clean; Box.CaretIndex = clean.Length; fixing = false; }
                    hint.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                    Box.Foreground = T.Text;
                    int h, min;
                    if (!manual && Read(out h, out min)) SetPm(daytime ? h >= 12 || h <= 6 : waking ? h >= 12 : (h >= 6 && h <= 11) || h >= 13); // daytime: 1-6 means the afternoon
                };
            }

            void SetPm(bool on) { pm = on; tag.Text = on ? "pm" : "am"; }

            bool Read(out int h, out int min)
            {
                h = 0; min = 0;
                var m = Rx.Match(Box.Text);
                if (!m.Success) return false;
                h = int.Parse(m.Groups[1].Value); min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                return h <= 23 && min <= 59;
            }

            public bool TryGet(out TimeSpan t)
            {
                t = TimeSpan.Zero; int h, min;
                if (!Read(out h, out min)) return false;
                if (h >= 1 && h <= 12) { if (pm) { if (h != 12) h += 12; } else if (h == 12) h = 0; } // 13-23 and 0 are already 24-hour
                t = new TimeSpan(h, min, 0);
                return true;
            }

            public void Fill(DateTime d)
            {
                manual = true; SetPm(d.Hour >= 12);
                Box.Text = d.ToString("h:mm", CultureInfo.InvariantCulture);
            }
        }

        // ------------------------------------------------------------ curve

        static string Dur(double hours)
        {
            int m = (int)Math.Round(hours * 60);
            if (m < 60) return m + " min";
            return m / 60 + " h" + (m % 60 > 0 ? " " + m % 60 + " min" : "");
        }


        // ------------------------------------------------------------ naps

        bool napOpen, napOffer;

        /// A small box for a number of minutes. It is white; pass Raised when it sits on something white.
        static Border MiniBox(string text, double width, out TextBox box, Brush surface = null)
        {
            var tb = Shell.Box(11.5); tb.Text = text; tb.MaxLength = 3; tb.Width = width;
            bool fixing = false;
            tb.TextChanged += (s, e) =>
            {
                if (fixing) return;
                string clean = new string(tb.Text.Where(char.IsDigit).ToArray());
                if (clean != tb.Text) { fixing = true; tb.Text = clean; tb.CaretIndex = clean.Length; fixing = false; }
                tb.Foreground = T.Text;
            };
            box = tb;
            return new PillBorder { Background = surface ?? T.Field, Padding = new Thickness(10, 5, 9, 6), Margin = new Thickness(0, 0, 6, 6), Child = tb, Cursor = Cursors.IBeam };
        }

        static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, FontSize = Z(11.5), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 7) };
        }


        // ------------------------------------------------------------ "I usually wake for prayer"

        const double PrayerAtDefault = 300, PrayerMinDefault = 45; // 05:00 for 45 minutes, until set


        // ------------------------------------------------------------ test hook (only reached with CORNER_DATA set)

        void DemoSeed(string mode)
        {
            if (mode == "prompt") promptOnly = true;
            if (mode == "plan" || mode == "applied")
            {
                foreach (var s in new[] { "Tidy desk", "Anki insertion !low 20m", "Review animation !med", "Emails !low", "Write concept 14 !high 45m" }) { input.Text = s; AddTask(); }
                input.Text = "";

            }
            if (mode == "checkin" || mode == "real") return; // "real" = whatever sleep.json is already in the test folder
            var day = SleepLog.Today(Now());
            var short_ = new[] { 2, 3, 6, 9 };
            for (int i = 12; i >= 0; i--)
                SleepLog.Record(SleepLog.Build(new TimeSpan(23, short_.Contains(i) ? 55 : 30, 0), new TimeSpan(short_.Contains(i) ? 5 : 6, short_.Contains(i) ? 40 : 15, 0), day.AddDays(-i)));
            if (mode == "split" || mode == "nap" || mode == "latenap")
                SleepLog.Record(SleepLog.BuildSplit(new[] { new TimeSpan(23, 30, 0), new TimeSpan(5, 15, 0) }, new[] { new TimeSpan(4, 30, 0), new TimeSpan(7, 0, 0) }, day));
            if (mode == "nap") { SleepLog.AddNap(day, day.AddHours(14.5), day.AddHours(14.5).AddMinutes(25)); napOffer = true; }
            if (mode == "latenap") SleepLog.AddNap(day, day.AddHours(17), day.AddHours(17).AddMinutes(40));
        }

        void DemoAfterShow(string mode) { if (mode == "all") { allOpen = true; FocusLayout(); } if (mode == "adding") { addOpen = true; FocusLayout(); } if (mode == "timer") { StartTimer(Upcoming()[0]); timerEnd = timerEnd.AddSeconds(-754); RepaintTimers(); DrawFocus(); } if (mode == "timeup") { StartTimer(Upcoming()[0]); TimeUp(); } if (mode == "timeuppop") { shown = false; gen++; Hide(); StartTimer(Upcoming()[0]); TimeUp(); } if (mode == "nextup") { CompleteTask(Upcoming()[0]); } if (mode == "moved") { if (Environment.GetEnvironmentVariable("CORNER_FULL") == "1") EnterFull(); moving = true; moveLeft = Left; moveTop = Top; Left -= 300; Top += 120; EndMove(); } if (Environment.GetEnvironmentVariable("CORNER_FULL") == "1") EnterFull(); if (mode == "editsplit") { SleepLog.Record(SleepLog.BuildSplit(new[] { new TimeSpan(23, 0, 0), new TimeSpan(4, 45, 0), new TimeSpan(6, 30, 0) }, new[] { new TimeSpan(4, 0, 0), new TimeSpan(6, 0, 0), new TimeSpan(7, 30, 0) }, SleepLog.Today(Now()))); RefreshEnergy(false); SetTab(1); OpenCheckIn(true); } if (mode == "energy" || mode == "real" || mode == "plan" || mode == "split" || mode == "nap" || mode == "latenap") SetTab(1); if (mode == "feedback") { Feedback.Log(Now(), "7", energy); ShowFeedback(Now()); } if (mode == "routines") { routinesOpen = true; SetTab(1); } if (mode == "drag") { SetTab(1); After(1200, () => { var p = chartThumb.PointToScreen(new Point(chartThumb.ActualWidth / 2, chartThumb.ActualHeight / 2)); IO.File.WriteAllText(IO.Path.Combine(Store.Dir, "thumb.txt"), (int)p.X + "," + (int)p.Y); }); } if (mode == "scroll") { chartOffset = 520; SetTab(1); } if (mode == "addroutine") { routinesOpen = true; newRoutineText = "Read a paper"; newRoutineEnergy = "high"; newRoutineMin = 45; SetTab(1); After(400, () => energyScroll.ScrollToEnd()); } if (mode == "debt") { debtOpen = true; SetTab(1); } if (mode == "history") { ToggleSettings(); After(400, () => settingsScroll.ScrollToEnd()); } if (mode == "settings") ToggleSettings(); if (mode == "plan") MakePlan(); if (mode == "applied") { MakePlan(); ApplyPlan(); SetTab(0); } if (mode == "routinesone") { routinesOpen = true; SetTab(1); AddRoutinesToToday(Routines.Data.Routines.Take(1)); } if (mode == "toglass" || mode == "tosage") { ToggleSettings(); After(300, () => SetSkin(mode == "toglass" ? 1 : 0)); } }
    }
}
