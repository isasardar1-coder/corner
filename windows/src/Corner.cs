// Corner - a to-do list that lives in the top-right corner of the screen.
// Built with the C# compiler that ships with Windows (.NET Framework 4.x, C# 5), see build.ps1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using IO = System.IO;
using WF = System.Windows.Forms;

namespace Corner
{
    // ---------------------------------------------------------------- model

    [DataContract]
    public class TaskItem
    {
        [DataMember] public string Id;
        [DataMember] public string Title;
        [DataMember] public bool Done;
        [DataMember] public string DoneAt;
        [DataMember] public string Due;
        [DataMember] public bool HasTime;
        [DataMember] public bool Notified;
        // Added for the energy features. Left out of the file while unset, so older tasks are written exactly as before.
        [DataMember(EmitDefaultValue = false)] public string Energy;  // "deep", "light", or null
        [DataMember(EmitDefaultValue = false)] public int Minutes;    // length in minutes; 0 = default
        [DataMember(EmitDefaultValue = false)] public bool Planned;   // Due was set by Plan, so Plan may move it again
        [DataMember(EmitDefaultValue = false)] public string Kind;       // "physical", or null for mental
        [DataMember(EmitDefaultValue = false)] public string Routine;    // Id of the routine this task came from
        [DataMember(EmitDefaultValue = false)] public string RoutineDay; // the day it was added for
        [DataMember(EmitDefaultValue = false)] public int Part;          // 1..n for a routine's check-ins
        [DataMember(EmitDefaultValue = false)] public double Worked;     // seconds worked on it so far, from its timer (Work.cs)

        const string Fmt = "yyyy-MM-ddTHH:mm";

        public DateTime? DueAt
        {
            get
            {
                DateTime d;
                if (!string.IsNullOrEmpty(Due) && DateTime.TryParseExact(Due, Fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
                return null;
            }
        }

        public void SetDue(DateTime? d, bool hasTime)
        {
            Due = d == null ? null : d.Value.ToString(Fmt, CultureInfo.InvariantCulture);
            HasTime = d != null && hasTime;
            Planned = false;
            Notified = false;
        }

        public bool Overdue
        {
            get
            {
                var d = DueAt;
                if (d == null) return false;
                return HasTime ? d.Value < DateTime.Now : d.Value.Date < DateTime.Today;
            }
        }
    }

    [DataContract]
    public class Data
    {
        [DataMember] public List<TaskItem> Tasks;
        [DataMember] public bool DoneOpen;
    }

    static class Store
    {
        public static readonly string Dir = Environment.GetEnvironmentVariable("CORNER_DATA") ?? IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location); // tasks.json lives next to Corner.exe
        public static readonly string File = IO.Path.Combine(Dir, "tasks.json");

        public static bool Exists { get { return IO.File.Exists(File); } }

        static DateTime stamp;
        static DateTime DiskStamp() { try { return IO.File.Exists(File) ? IO.File.GetLastWriteTimeUtc(File) : DateTime.MinValue; } catch { return stamp; } }
        public static bool ChangedOnDisk { get { return DiskStamp() != stamp; } }

        /// Re-reads the file after someone else edited it. Null when it is missing or unreadable (e.g. half written).
        public static Data Reload()
        {
            try
            {
                if (!Exists) return null;
                var st = DiskStamp(); Data d;
                using (var fs = IO.File.OpenRead(File)) d = (Data)new DataContractJsonSerializer(typeof(Data)).ReadObject(fs);
                if (d == null) return null;
                if (d.Tasks == null) d.Tasks = new List<TaskItem>();
                d.Tasks.RemoveAll(t => t == null || string.IsNullOrEmpty(t.Id));
                stamp = st;
                return d;
            }
            catch { return null; }
        }

        public static Data Load()
        {
            try
            {
                if (Exists)
                {
                    using (var fs = IO.File.OpenRead(File))
                    {
                        stamp = DiskStamp(); var d = (Data)new DataContractJsonSerializer(typeof(Data)).ReadObject(fs);
                        if (d != null)
                        {
                            if (d.Tasks == null) d.Tasks = new List<TaskItem>();
                            d.Tasks.RemoveAll(t => t == null || string.IsNullOrEmpty(t.Id));
                            return d;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex);
                try { IO.File.Copy(File, File + ".bad", true); } catch { }
            }
            return new Data { Tasks = new List<TaskItem>() };
        }

        public static void Save(Data d)
        {
            try
            {
                IO.Directory.CreateDirectory(Dir);
                string bak = IO.Path.Combine(Dir, "tasks.before-energy.json"); // one-off copy before the first write by the energy version
                if (IO.File.Exists(File) && !IO.File.Exists(bak)) IO.File.Copy(File, bak);
                string tmp = File + ".tmp";
                using (var fs = IO.File.Create(tmp))
                    new DataContractJsonSerializer(typeof(Data)).WriteObject(fs, d);
                if (IO.File.Exists(File)) ReplaceSafely(tmp, File);
                else IO.File.Move(tmp, File);
                stamp = DiskStamp();
            }
            catch (Exception ex) { Log(ex); }
        }

        /// Another program can briefly hold tasks.json open. Try a few times, then overwrite it in place.
        public static void ReplaceSafely(string tmp, string file)
        {
            for (int i = 0; i < 4; i++)
            {
                try { IO.File.Replace(tmp, file, null); return; }
                catch (IO.IOException) { System.Threading.Thread.Sleep(60); }
            }
            IO.File.Copy(tmp, file, true);
            IO.File.Delete(tmp);
        }

        public static void Log(Exception ex)
        {
            try
            {
                IO.Directory.CreateDirectory(Dir);
                IO.File.AppendAllText(IO.Path.Combine(Dir, "error.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine);
            }
            catch { }
        }
    }

    /// settings.json beside tasks.json: a flat list of named numbers.
    static class Settings
    {
        static string F { get { return IO.Path.Combine(Store.Dir, "settings.json"); } }
        static Dictionary<string, double> values;

        static void Load()
        {
            values = new Dictionary<string, double>();
            try
            {
                if (!IO.File.Exists(F)) return;
                foreach (Match m in Regex.Matches(IO.File.ReadAllText(F), @"""(\w+)""\s*:\s*(-?[0-9.]+)"))
                {
                    double v;
                    if (double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) values[m.Groups[1].Value] = v;
                }
            }
            catch { }
        }

        public static double Get(string key, double fallback)
        {
            if (values == null) Load();
            double v;
            return values.TryGetValue(key, out v) ? v : fallback;
        }

        public static void Set(string key, double value)
        {
            if (values == null) Load();
            values[key] = value;
            try { IO.File.WriteAllText(F, "{" + string.Join(", ", values.Select(p => "\"" + p.Key + "\": " + p.Value.ToString("0.###", CultureInfo.InvariantCulture))) + "}"); }
            catch (Exception ex) { Store.Log(ex); }
        }

        public static double LoadScale() { return Get("Scale", 1); }
        public static void SaveScale(double v) { Set("Scale", v); }
    }

    static class Autostart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Corner";

        static string Command { get { return "\"" + System.Reflection.Assembly.GetEntryAssembly().Location + "\""; } }

        public static bool Enabled
        {
            get
            {
                try { using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)) return k != null && k.GetValue(Name) != null; }
                catch { return false; }
            }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(Name, Command);
                    else k.DeleteValue(Name, false);
                }
            }
            catch (Exception ex) { Store.Log(ex); }
        }
    }

    // ---------------------------------------------------------------- "tomorrow 9am" parsing

    public static class When
    {
        const string Mon = "jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec";
        const string D = @"today|tonight|tomorrow|tmrw|tmr|next\s+week|(?:next\s+)?(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday|mon|tues|tue|wed|thurs|thur|thu|fri|sat|sun)|(?:" + Mon + @")[a-z]*\.?\s+\d{1,2}|\d{1,2}\s+(?:" + Mon + @")[a-z]*|\d{4}-\d{1,2}-\d{1,2}|in\s+\d+\s*(?:days?|d|weeks?|w)";
        const string Tm = @"(?:at\s+|@\s*)?\d{1,2}(?::\d{2})?\s*(?:am|pm)|(?:at\s+|@\s*)?\d{1,2}:\d{2}|(?:at\s+|@\s*)\d{1,2}|noon|midnight";
        const string R = @"in\s+\d+\s*(?:minutes?|mins?|m|hours?|hrs?|h)";

        static readonly Regex Rx = new Regex(
            @"^(?<title>.*?)(?:^|\s+)(?:(?:by|on|due)\s+)?(?:(?<rel>" + R + @")|(?<d>" + D + @")(?:\s+(?<t>" + Tm + @"))?|(?<t>" + Tm + @")(?:\s+(?:on\s+)?(?<d>" + D + @"))?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// Finds a date/time phrase at the END of the text. Returns false when there is none.
        public static bool Parse(string text, out string title, out DateTime due, out bool hasTime)
        {
            title = (text ?? "").Trim(); due = DateTime.MinValue; hasTime = false;
            var m = Rx.Match(title);
            if (!m.Success) return false;
            string tt = m.Groups["title"].Value.Trim();
            if (tt.Length == 0) return false;
            var now = DateTime.Now;

            if (m.Groups["rel"].Success)
            {
                var rm = Regex.Match(m.Groups["rel"].Value, @"(\d+)\s*([a-z])", RegexOptions.IgnoreCase);
                int n;
                if (!int.TryParse(rm.Groups[1].Value, out n) || n > 100000) return false;
                var dt = now.AddMinutes(char.ToLowerInvariant(rm.Groups[2].Value[0]) == 'm' ? n : n * 60.0);
                due = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0);
                hasTime = true; title = tt; return true;
            }

            DateTime date = now.Date; bool haveDate = false; TimeSpan time = TimeSpan.Zero; bool haveTime = false;
            if (m.Groups["d"].Success)
            {
                string ds = Regex.Replace(m.Groups["d"].Value.ToLowerInvariant(), @"\s+", " ");
                if (!ResolveDate(ds, now.Date, out date)) return false;
                haveDate = true;
                if (ds == "tonight") { time = TimeSpan.FromHours(20); haveTime = true; }
            }
            if (m.Groups["t"].Success)
            {
                if (!ResolveTime(m.Groups["t"].Value.ToLowerInvariant(), out time)) return false;
                haveTime = true;
            }
            due = date + time;
            if (!haveDate && due <= now) due = due.AddDays(1);
            hasTime = haveTime; title = tt; return true;
        }

        static bool ResolveDate(string s, DateTime today, out DateTime date)
        {
            date = today;
            try
            {
                if (s == "today" || s == "tonight") return true;
                if (s == "tomorrow" || s == "tmrw" || s == "tmr") { date = today.AddDays(1); return true; }
                if (s == "next week") { int d = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7; date = today.AddDays(d == 0 ? 7 : d); return true; }
                Match m;
                if ((m = Regex.Match(s, @"^in (\d+) ?([dw])")).Success)
                {
                    int n; if (!int.TryParse(m.Groups[1].Value, out n) || n > 3650) return false;
                    date = today.AddDays(m.Groups[2].Value == "w" ? n * 7 : n); return true;
                }
                if ((m = Regex.Match(s, @"^(\d{4})-(\d{1,2})-(\d{1,2})$")).Success)
                {
                    date = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)); return true;
                }
                if ((m = Regex.Match(s, @"^(" + Mon + @")[a-z]*\.? (\d{1,2})$")).Success || (m = Regex.Match(s, @"^(?<dd>\d{1,2}) (?<mm>" + Mon + @")")).Success)
                {
                    string mon = m.Groups["mm"].Success ? m.Groups["mm"].Value : m.Groups[1].Value;
                    string day = m.Groups["dd"].Success ? m.Groups["dd"].Value : m.Groups[2].Value;
                    int mi = Array.IndexOf(Mon.Split('|'), mon) + 1;
                    date = new DateTime(today.Year, mi, int.Parse(day));
                    if (date < today) date = date.AddYears(1);
                    return true;
                }
                string w = s.StartsWith("next ") ? s.Substring(5) : s;
                int wi = Array.IndexOf(new[] { "sun", "mon", "tue", "wed", "thu", "fri", "sat" }, w.Substring(0, 3));
                if (wi < 0) return false;
                int delta = (wi - (int)today.DayOfWeek + 7) % 7;
                date = today.AddDays(delta == 0 ? 7 : delta);
                return true;
            }
            catch { return false; }
        }

        static bool ResolveTime(string s, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (s == "noon") { time = TimeSpan.FromHours(12); return true; }
            if (s == "midnight") return true;
            var m = Regex.Match(s, @"(\d{1,2})(?::(\d{2}))?\s*(am|pm)?");
            if (!m.Success) return false;
            int h = int.Parse(m.Groups[1].Value);
            int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            if (min > 59) return false;
            if (m.Groups[3].Success)
            {
                if (h < 1 || h > 12) return false;
                if (m.Groups[3].Value == "pm") { if (h != 12) h += 12; } else if (h == 12) h = 0;
            }
            else
            {
                if (h > 23) return false;
                bool clock12 = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.IndexOf('h') >= 0;
                // "at 5" almost always means the afternoon
                if (h >= 1 && h <= 7 && (!m.Groups[2].Success || clock12)) h += 12;
            }
            time = new TimeSpan(h, min, 0);
            return true;
        }

        public static string Label(DateTime due, bool hasTime)
        {
            var inv = CultureInfo.InvariantCulture;
            var today = DateTime.Today;
            int diff = (due.Date - today).Days;
            string s = diff == 0 ? "Today" : diff == 1 ? "Tomorrow" : diff == -1 ? "Yesterday"
                : (diff > 1 && diff < 7) ? due.ToString("dddd", inv)
                : due.Year == today.Year ? due.ToString("MMM d", inv) : due.ToString("MMM d, yyyy", inv);
            if (hasTime) s += " " + due.ToString("t", CultureInfo.CurrentCulture);
            return s;
        }
    }

    // ---------------------------------------------------------------- colours

    /// Corner has five skins, chosen in Settings. Each colour is named for what it is used for, so a change here changes
    /// every place that plays that part; each has a value for Sage, for Glass, for Glass when it is dark, and then for
    /// Metal, Ceramic and Paper (whose depth is drawn in Material.cs).
    ///   Sage:  a pale sage panel with darker trays, white cards and pills, forest green for whatever is chosen or
    ///          switched on, and lime for the main action, the running timer and the peaks.
    ///   Glass: after the Liquid Glass of iPadOS 26. The panel is clear glass over a blurred picture of whatever is
    ///          behind it (Glass.cs); trays, cards and pills are thin layers of white on it, each with a bright rim.
    ///          Over dark things the glass is dark and the text white (T.Dark, which Glass.cs sets as a panel opens).
    /// The brushes are never replaced: a change of skin changes their colours, and everything drawn with them follows.
    static class T
    {
        public static readonly string[] Names = { "Sage", "Glass", "Metal", "Ceramic", "Paper" };
        public static int Skin;
        public static bool Dark; // Glass only: the glass is dark, because what is behind it is
        public static bool Glass { get { return Skin == 1; } }

        static readonly List<SolidColorBrush> brushes = new List<SolidColorBrush>();
        static readonly List<string[]> values = new List<string[]>();
        public static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
        static SolidColorBrush B(string sage, string glass, string dark, string metal, string ceramic, string paper) { var b = new SolidColorBrush(C(sage)); brushes.Add(b); values.Add(new[] { sage, glass, dark, metal, ceramic, paper }); return b; }

        public static readonly SolidColorBrush
            // surfaces, from the back forwards (Glass: how much white each lays over what is under it)
            Bg = B("#E8ECDB", "#38FFFFFF", "#14FFFFFF", "#B2B4B6", "#DCDFCB", "#C9E0F1"),         // the panel (Glass: the sheet itself is drawn in Glass.cs)
            Edge = B("#D3D9C2", "#FFD4E2EE", "#FF39455E", "#8E9093", "#C6CAB3", "#B3CFE3"),       // the panel's own edge, and a tooltip's
            Tray = B("#D9DFC7", "#40FFFFFF", "#14FFFFFF", "#A3A5A8", "#CFD3BC", "#BBD4E7"),       // a box a step apart from the panel: the next task, the check-ins, the chart
            Field = B("#FFFFFF", "#57FFFFFF", "#1CFFFFFF", "#EDEDEE", "#F0EEE7", "#F5F4F2"),      // the brightest surface: cards, chips, pills, boxes to type in
            Lift = B("#EEF1E4", "#6BFFFFFF", "#26FFFFFF", "#F6F6F7", "#F6F4EE", "#FBFAF9"),       // a control sitting on a tray, and a Field chip under the mouse
            Raised = B("#E8ECDB", "#5CFFFFFF", "#21FFFFFF", "#E1E2E4", "#E6E6D8", "#DDE9F3"),     // a control sitting on a Field card
            Ctl = B("#C6CFB1", "#2E16304A", "#3DFFFFFF", "#C4C6C9", "#C3C9AE", "#A9C4D9"),        // tracks, rails, empty bars, the chart between its zones
            Line = B("#E3E7D6", "#2416304A", "#26FFFFFF", "#D9DADC", "#E0E0D2", "#DFE7EE"),       // a thin rule on a Field card
            Hover = B("#0F16281D", "#3DFFFFFF", "#1FFFFFFF", "#14000000", "#0F16281D", "#14204868"),    // a task row under the mouse
            Ring = B("#D9DFC7", "#3316304A", "#3DFFFFFF", "#D0D2D5", "#D5D9C3", "#B5CEE0"),       // the empty part of the progress ring in the header
            Pop = B("#FFFFFF", "#F7F4F8FC", "#F71B2433", "#FFFFFF", "#FBFAF6", "#FFFFFF"),        // a tooltip: on its own over anything, so never see-through
            // text; all three are readable on the panel, on a tray and on a card
            Text = B("#141A12", "#0F1822", "#FFFFFF", "#121416", "#141A12", "#14202B"), Muted = B("#535C4B", "#D6142438", "#D1FFFFFF", "#3F4347", "#4E5746", "#44566A"), Faint = B("#586150", "#BD142438", "#B3FFFFFF", "#4A4E53", "#566050", "#4F6074"),
            // the strong colour: the next task's timer, whatever is chosen or on, and the prompts that need an answer.
            // Dark in Sage and on light glass; on dark glass it is white, with dark text on it.
            Strong = B("#16281D", "#EB132A40", "#E0F4F8FF", "#0F2416", "#132717", "#1E3721"), OnStrong = B("#FFFFFF", "#FFFFFF", "#0E1A26", "#FFFFFF", "#FFFFFF", "#FFFFFF"), OnStrongSoft = B("#C9D3BE", "#CFE3F0", "#B8152433", "#C3CEC4", "#C9D3BE", "#C5D2C6"), StrongLine = B("#7C8C7D", "#73BFE6F2", "#59152433", "#6F8273", "#7C8C7D", "#7E9482"), StrongLift = B("#24392C", "#33FFFFFF", "#1F152433", "#1F3A28", "#24392C", "#2F4C34"),
            Dip = B("#16281D", "#EB132A40", "#A6050A12", "#0F2416", "#132717", "#1E3721"), OnDip = B("#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF"), // the dip on the chart: dark in every skin
            // the accent: the main action, a running timer, the peaks. Soft is its pale form: today, the row being timed, footers
            Accent = B("#B4C12A", "#DEFFFFFF", "#D9F4F8FF", "#5F656B", "#BECB2B", "#3E79AF"), OnAccent = B("#141A12", "#0F1822", "#0E1A26", "#FFFFFF", "#141A12", "#FFFFFF"), Soft = B("#EEF2B4", "#94FFFFFF", "#42FFFFFF", "#9EA2A6", "#CFD94F", "#A9C6DD"),
            AccentOn = B("#B4C12A", "#EDF3F7FB", "#1B2A3A", "#D9DCDF", "#C6D233", "#A9C6DD"),        // the accent seen on the strong colour: a prompt's caption, a switch's knob
            // the main button ("Done", "Plan my day") and the chosen one of a set of tabs: the accent, except in Glass, where it is bright clear glass
            Main = B("#B4C12A", "#CCFFFFFF", "#E0F4F8FF", "#5F656B", "#BECB2B", "#3E79AF"), OnMain = B("#141A12", "#0F1822", "#0E1A26", "#FFFFFF", "#141A12", "#FFFFFF"),
            Ink = B("#2A5238", "#EB132A40", "#F4F8FF", "#3C4248", "#46664B", "#2F6596"),              // the line logo in the header
            Knob = B("#FFFFFF", "#F5FFFFFF", "#F0FFFFFF", "#FFFFFF", "#FBFAF6", "#FFFFFF"),          // the knob of a switch that is off
            Sand = B("#CDC6AB", "#59B4C0CC", "#33C9D3DA", "#C8CACD", "#C2C8AD", "#A3BFD6"),       // the hours of expected sleep on the chart
            Danger = B("#A8401F", "#C4260F", "#FF8A75", "#A8321A", "#A8401F", "#B03A1E"), DangerSoft = B("#F4DDD3", "#73F2B5A8", "#4DFF6B55", "#F0D9D2", "#F3DDD2", "#F2DCD4"); // overdue, and nothing else

        // ---- Glass only: what the sheet of glass and the boxes on it are drawn with (Glass.cs). Like the colours
        // above these are changed in place by Use; in Sage they are all fully transparent.
        static SolidColorBrush Clear() { return new SolidColorBrush(Colors.Transparent); }
        static GradientStop Stop(double at) { return new GradientStop(Colors.Transparent, at); }
        static LinearGradientBrush Grad(Point from, Point to, GradientStop[] stops) { return new LinearGradientBrush(new GradientStopCollection(stops), from, to); }

        static readonly GradientStop[] boxRim = { Stop(0), Stop(0.5), Stop(1) }, sheetRim = { Stop(0), Stop(0.5), Stop(1) }, sheetRim2 = { Stop(0), Stop(0.5), Stop(1) }, gloss = { Stop(0), Stop(0.62) };
        static readonly SolidColorBrush boxGlow = Clear(), veil = Clear(), glassBase = new SolidColorBrush(C("#DCE6F0"));
        static readonly SolidColorBrush[] glow = { Clear(), Clear(), Clear(), Clear(), Clear(), Clear(), Clear() };

        /// A box on the glass: the hairline round it (bright along the top, faint down the sides, some again at the
        /// bottom), the soft glow just inside that, and the gloss across the upper half of a pill.
        public static readonly Pen RimPen = new Pen(Grad(new Point(0, 0), new Point(0, 1), boxRim), 1);
        public static readonly Pen BoxGlowPen = new Pen(boxGlow, 2);
        public static readonly Brush Gloss = Grad(new Point(0, 0), new Point(0, 1), gloss);
        /// The sheet: its thin veil, what is under it when the screen cannot be read, the glow that fades in from its
        /// edge (one pen a unit, outermost first), and its two hairlines, lit from the top left.
        public static readonly Brush GlassVeil = veil, GlassBase = glassBase;
        public static readonly Pen[] GlowPens = { new Pen(glow[0], 1.2), new Pen(glow[1], 1.2), new Pen(glow[2], 1.2), new Pen(glow[3], 1.2), new Pen(glow[4], 1.2), new Pen(glow[5], 1.2), new Pen(glow[6], 1.2) };
        public static readonly Pen SheetRimPen = new Pen(Grad(new Point(0, 0), new Point(1, 1), sheetRim), 1.1);
        public static readonly Pen SheetRim2Pen = new Pen(Grad(new Point(0, 0), new Point(1, 1), sheetRim2), 0.9);
        public static Color GlassShadow = C("#16244A"); public static double GlassShadowOpacity = 0.3;

        static string Pick(string glass, string dark) { return Skin != 1 ? "#00FFFFFF" : Dark ? dark : glass; }

        public static void Use(int skin) { Use(skin, Dark); }

        public static void Use(int skin, bool dark)
        {
            Skin = skin < 0 || skin >= Names.Length ? 0 : skin; Dark = dark;
            int col = Skin == 0 ? 0 : Skin == 1 ? (Dark ? 2 : 1) : Skin + 1; // (Glass has two columns)
            for (int i = 0; i < brushes.Count; i++) brushes[i].Color = C(values[i][col]);

            boxRim[0].Color = C(Pick("#E0FFFFFF", "#80FFFFFF")); boxRim[1].Color = C(Pick("#47FFFFFF", "#1FFFFFFF")); boxRim[2].Color = C(Pick("#94FFFFFF", "#3DFFFFFF"));
            boxGlow.Color = C(Pick("#1FFFFFFF", "#0FFFFFFF"));
            gloss[0].Color = C(Pick("#30FFFFFF", "#17FFFFFF")); gloss[1].Color = C("#00FFFFFF");
            sheetRim[0].Color = C(Pick("#F2FFFFFF", "#A6DCE6FF")); sheetRim[1].Color = C(Pick("#61FFFFFF", "#2BFFFFFF")); sheetRim[2].Color = C(Pick("#C2FFFFFF", "#66DCE6FF"));
            sheetRim2[0].Color = C(Pick("#A6FFFFFF", "#47FFFFFF")); sheetRim2[1].Color = C(Pick("#38FFFFFF", "#12FFFFFF")); sheetRim2[2].Color = C(Pick("#80FFFFFF", "#2BFFFFFF"));
            veil.Color = C(Pick("#38FFFFFF", "#14FFFFFF"));
            glassBase.Color = C(Dark ? "#141A28" : "#DCE6F0");
            for (int i = 0; i < glow.Length; i++)
            {
                double a = Skin != 1 ? 0 : (Dark ? 0.13 : 0.21) * Math.Pow(1 - i / (double)glow.Length, 1.6);
                glow[i].Color = Color.FromArgb((byte)(a * 255), 255, 255, 255);
            }
            GlassShadow = C(Dark ? "#000000" : "#16244A"); GlassShadowOpacity = Dark ? 0.5 : 0.3;
        }
    }

    // ---------------------------------------------------------------- the panel

    class Row
    {
        public TaskItem Task;
        public StackPanel Wrap;
        public Grid Main;
        public Border Hover;
        public TranslateTransform Shift;
    }

    partial class Shell : Window
    {
        const double CardW = 312, Gap = 12, Pad = 44; // CardW: the corner panel; Full Mode is FullW (Checks.cs)

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

        static readonly IEasingFunction Out = Frozen(new QuinticEase { EasingMode = EasingMode.EaseOut });
        static readonly IEasingFunction Soft = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
        static readonly IEasingFunction Pop = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 });
        static IEasingFunction Frozen(EasingFunctionBase e) { e.Freeze(); return e; }

        Data data;
        readonly Stopwatch clock = Stopwatch.StartNew();
        Grid card; Shade shadow; // the panel, and the soft shadow under it (Shade.cs)
        readonly TranslateTransform cardShift = new TranslateTransform();
        readonly ScaleTransform cardScale = new ScaleTransform(1, 1);
        TextBlock inputHint, inputPreview;
        Grid headLogo;
        Border inputChip;
        Path ringArc; Border ringDisc;
        TextBox input;
        StackPanel list;
        readonly List<Row> rows = new List<Row>();

        bool shown, cornerBlocked;
        int gen, pending;
        long cornerSince, awaySince, peekUntil;
        string whenOpenId, flashId;
        Action commitEdit;

        Row dragRow; Point dragStart; bool dragging, dragOnLabel;
        double[] dTops, dHeights, dTargets; int dIndex;

        WF.NotifyIcon tray;
        DispatcherTimer mouseTimer, remindTimer;

        // ------------------------------------------------------------ startup

        public Shell()
        {
            Title = "Corner";
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            Width = CardW + Gap + Pad; Height = 600; Left = -10000; Top = -10000;
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80); // tool window: stay out of Alt+Tab
            };
            Build();
            Closing += (s, e) => { if (full && !quitting) { e.Cancel = true; ExitFull(); } }; // closing the Full Mode window returns to the corner
        }

        public void Start(bool peek, bool test)
        {
            bool firstRun = !Store.Exists;
            data = Store.Load();
            SetScale(Settings.LoadScale(), false);
            corner = Math.Max(0, Math.Min(3, (int)Settings.Get("Corner", 0)));
            LoadLook(); // font and Full Mode text size (Checks.cs)
            if (test) { } else if (firstRun) { Autostart.Set(true); Store.Save(data); }
            else if (Autostart.Enabled) Autostart.Set(true); // keep the path current if the exe was moved

            InitTray();

            mouseTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };
            mouseTimer.Tick += (s, e) => MouseTick();
            mouseTimer.Start();
            remindTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            remindTimer.Tick += (s, e) => { LogDays(); CheckReminders(); CheckInTick(); LiveEnergy(); ApplyLogo(false); ApplyFullIcon(); ApplyTrayIcon(); }; // LogDays: yesterday into days.csv (Stats.cs)
            remindTimer.Start();

            InitUpdates(test);
            After(6000, WarmWarp); // (Launch.cs) // a newer Corner on GitHub: fetched and swapped in by itself (Update.cs)
            if (firstRun && (!test || Environment.GetEnvironmentVariable("CORNER_ONBOARD") == "1")) ShowOnboarding(); // a new install: name, sleep need, routines (Onboard.cs)
            else if (peek || !test) { launchZoom = true; ShowPanel(peek ? 6000 : 4500); } // Corner starting: it opens out of its icon, then rests in its corner (Launch.cs)
            CheckReminders();
        }

        void InitTray()
        {
            tray = new WF.NotifyIcon { Text = "Corner" };
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location); }
            catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            ApplyLogo(true); // the light or the dark logo, as Windows is in light or dark mode (Logo.cs)
            ApplyTrayIcon(); // the tray shows the hot-corner picture, as the taskbar does in Full Mode (TrayIcon.cs)
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("View Full Mode", null, (s, e) => EnterFull());
            menu.Items.Add("Show list", null, (s, e) => Wake());
            var auto = new WF.ToolStripMenuItem("Start with Windows") { Checked = Autostart.Enabled, CheckOnClick = true };
            auto.CheckedChanged += (s, e) => Autostart.Set(auto.Checked);
            sizeMenu = new WF.ToolStripMenuItem("Text size");
            foreach (var p in new[] { 0.8, 0.9, 1.0, 1.15, 1.3 })
            {
                double v = p;
                var it = new WF.ToolStripMenuItem((int)Math.Round(v * 100) + "%") { Tag = v };
                it.Click += (s, e) => { SetScale(v, true); Wake(); };
                sizeMenu.DropDownItems.Add(it);
            }
            var cornerMenu = new WF.ToolStripMenuItem("Opens from");
            string[] cornerNames = { "Top right", "Top left", "Bottom right", "Bottom left" };
            for (int i = 0; i < cornerNames.Length; i++)
            {
                int v = i;
                var it = new WF.ToolStripMenuItem(cornerNames[i]) { Checked = corner == v };
                it.Click += (s, e) =>
                {
                    corner = v; Settings.Set("Corner", v); Settings.Set("Moved", 0);
                    foreach (WF.ToolStripMenuItem o in cornerMenu.DropDownItems) o.Checked = o == it;
                    if (shown) Place();
                    Wake();
                };
                cornerMenu.DropDownItems.Add(it);
            }
            menu.Items.Add(cornerMenu);
            menu.Opening += (s, e) =>
            {   // the same settings can be changed inside the panel, so read the ticks fresh each time
                for (int i = 0; i < cornerMenu.DropDownItems.Count; i++) ((WF.ToolStripMenuItem)cornerMenu.DropDownItems[i]).Checked = corner == i;
                foreach (WF.ToolStripMenuItem it in sizeMenu.DropDownItems) it.Checked = Math.Abs((double)it.Tag - CurrentScale) < 0.001;
                if (auto.Checked != Autostart.Enabled) auto.Checked = Autostart.Enabled;
            };
            menu.Items.Add(sizeMenu);
            SetScale(scale, false);
            menu.Items.Add(auto);
            menu.Items.Add("Reset position", null, (s, e) => { ResetPosition(); Wake(); });
            menu.Items.Add("Restart", null, (s, e) => { quitting = true; tray.Visible = false; Program.Restart(); });
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Quit", null, (s, e) => { quitting = true; tray.Visible = false; Application.Current.Shutdown(); });
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) Wake(); };
            tray.Visible = true;
        }

        // ------------------------------------------------------------ helpers

        static void Anim(DependencyObject o, DependencyProperty dp, double to, int ms, int delay = 0, double? from = null, IEasingFunction ease = null, Action done = null)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Soft };
            if (from != null) a.From = from;
            if (delay > 0) a.BeginTime = TimeSpan.FromMilliseconds(delay);
            if (done != null) a.Completed += (s, e) => done();
            ((IAnimatable)o).BeginAnimation(dp, a);
        }

        static void Set(DependencyObject o, DependencyProperty dp, double v)
        {
            ((IAnimatable)o).BeginAnimation(dp, null);
            o.SetValue(dp, v);
        }

        void After(int ms, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); a(); };
            t.Start();
        }

        static Path Glyph(string d, Brush stroke, double th, double w, double h)
        {
            return new Path
            {
                Data = Geometry.Parse(d), Stroke = stroke, StrokeThickness = th, Width = w, Height = h,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
            };
        }

        static TextBox Box(double size)
        {
            return new TextBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = T.Text, CaretBrush = T.Text,
                SelectionBrush = T.Accent, FontSize = Z(size), Padding = new Thickness(0), FocusVisualStyle = null,
                VerticalContentAlignment = VerticalAlignment.Center, ContextMenu = null
            };
        }

        void Save() { Store.Save(data); }

        // Picks up changes made to tasks.json outside the app.
        void SyncFromDisk()
        {
            if (!Store.ChangedOnDisk || commitEdit != null || pending > 0 || dragging) return;
            var d = Store.Reload();
            if (d == null) return;
            data = d; whenOpenId = null;
            if (shown) Rebuild();
        }

        // "Closed": no hot corner until the tray icon is clicked.
        // Text size: one zoom factor for the whole card, so the panel shrinks with the text.
        readonly ScaleTransform zoom = new ScaleTransform(1, 1);
        double scale = 1;
        WF.ToolStripMenuItem sizeMenu;

        // Which corner of the screen opens the list: 0 top right, 1 top left, 2 bottom right, 3 bottom left ("Corner" in settings.json)
        int corner;
        bool OnRight { get { return corner == 0 || corner == 2; } }
        bool OnTop { get { return corner < 2; } }

        void Place()
        {
            Restyle(); // spacing and text sizes for the corner panel or Full Mode (Ui.cs)
            var wa = SystemParameters.WorkArea;
            double z = full ? fullScale : scale;
            zoom.ScaleX = z; zoom.ScaleY = z;
            card.Width = full ? FullW : CardW;
            card.MinHeight = 0; // Full Mode too: as high as its two columns, so an empty day or closed routines make it shorter
            if (full) { PlaceFull(wa, z); return; }
            Width = CardW * scale + Gap + Pad; Height = wa.Height; Left = OnRight ? wa.Right - Width : wa.Left; Top = wa.Top;
            card.HorizontalAlignment = OnRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            card.VerticalAlignment = OnTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            card.Margin = new Thickness(OnRight ? 0 : Gap, OnTop ? Gap : 0, OnRight ? Gap : 0, OnTop ? 0 : Gap);
            card.RenderTransformOrigin = new Point(OnRight ? 1 : 0, OnTop ? 0 : 1);
            PlaceMoved(wa); // a dragged position, and the height that fits there (Checks.cs)
        }

        void SetScale(double v, bool save)
        {
            if (full)
            {   // Full Mode keeps its own text size
                fullScale = Math.Max(0.7, Math.Min(1.6, Math.Round(v * 20) / 20));
                if (save) Settings.Set("FullScale", fullScale);
                if (shown) Place();
                return;
            }
            scale = Math.Max(0.6, Math.Min(1.5, Math.Round(v * 20) / 20));
            zoom.ScaleX = scale; zoom.ScaleY = scale;
            if (shown) Place();
            if (save) Settings.SaveScale(scale);
            if (sizeMenu != null)
                foreach (WF.ToolStripMenuItem it in sizeMenu.DropDownItems) it.Checked = Math.Abs((double)it.Tag - scale) < 0.001;
        }

        bool paused;
        void Sleep()
        {
            if (full) { ExitFull(); return; } // in Full Mode the cross just closes the window
            paused = true; HidePanel();
            tray.Text = "Corner is closed - click to reopen";
            try { tray.ShowBalloonTip(4000, "Corner is closed", "Click its tray icon to bring it back.", WF.ToolTipIcon.None); } catch { }
        }
        void Wake() { if (full) { Activate(); return; } paused = false; tray.Text = "Corner"; ShowPanel(5000); }

        // ------------------------------------------------------------ static layout

        void Build()
        {
            card = new Grid
            {
                Width = CardW, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, Gap, Gap, 0), RenderTransformOrigin = new Point(1, 0), Opacity = 0
            };
            var tg = new TransformGroup(); tg.Children.Add(cardScale); tg.Children.Add(cardShift);
            card.RenderTransform = tg;
            card.LayoutTransform = zoom;

            shadow = new Shade { Radius = 18, Blur = 34, Depth = 10, Tint = Color.FromRgb(60, 54, 34), Strength = 0.3 };
            card.Children.Add(shadow);
            panelBg = new Border { CornerRadius = new CornerRadius(18), Background = T.Bg, BorderBrush = T.Edge, BorderThickness = new Thickness(1) };
            card.Children.Add(panelBg);
            // the Glass skin's tint and rim, over the picture of what is behind the panel (Glass.cs)
            glassSheet = new GlassSheet();
            glassTint = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Child = glassSheet };
            card.Children.Add(glassTint);

            var dock = new DockPanel { LastChildFill = true };
            card.Children.Add(dock);

            // header: (Full Mode only) a round button back to the corner, then the date; at the right the progress ring and
            // the round white icon buttons. Sizes and margins for each mode are set in Restyle (Ui.cs).
            var header = new Grid { Background = Brushes.Transparent };
            headerRow = header;
            header.Cursor = Cursors.SizeAll; // drag the panel by its header, in both modes (Checks.cs)
            header.MouseLeftButtonDown += (s, e) => BeginMove(e);
            header.MouseMove += (s, e) => ContinueMove();
            header.MouseLeftButtonUp += (s, e) => EndMove();
            header.LostMouseCapture += (s, e) => EndMove();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headIcons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(headIcons, 2); header.Children.Add(headIcons);
            minBtn = HeadIcon("M8.5,5 H1.5 M4.5,2 L1.5,5 L4.5,8", 1.5, 10, "Back to the corner", ExitFull);
            headIcons.Children.Remove(minBtn); minBtn.Visibility = Visibility.Collapsed; minBtn.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(minBtn); // the first column, shown in Full Mode
            headText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            // the logo, in both modes: the ball on the hill as two lines, in the skin's own ink (drawn after design\logo-outline-sunburst; sized in Restyle)
            headLogo = new Grid { Width = 46, Height = 28, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
            var mark = new GeometryGroup { Transform = new MatrixTransform(0.127, 0, 0, 0.127, -4.2, -12.8) };
            mark.Children.Add(new EllipseGeometry(new Point(155, 168), 42, 42));
            mark.Children.Add(Geometry.Parse("M41,285 C75,243 110,212 155,212 C240,212 255,298 328,298 C352,298 372,286 388,266"));
            headLogo.Children.Add(new Path { Data = mark, Stroke = T.Ink, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            headText.Children.Add(headLogo);
            Grid.SetColumn(headText, 1); header.Children.Add(headText);
            // how much of the list is done: a ring on a white disc, drawn at 24 and scaled to fit the disc
            var ring = new Grid { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ring.Children.Add(new Ellipse { Width = 22, Height = 22, Stroke = T.Ring, StrokeThickness = 3 });
            ringArc = new Path { Stroke = T.Strong, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Width = 24, Height = 24 };
            ring.Children.Add(ringArc);
            ringBox = ring;
            ringDisc = new PillBorder { Background = T.Field, Child = ring }; // not a button: its tooltip says what it counts (UpdateHeader)
            headIcons.Children.Add(ringDisc);
            gearBox = HeadIcon("M6,4.2 A1.8,1.8 0 1 1 5.99,4.2 Z M6,0.8 V2.2 M6,9.8 V11.2 M0.8,6 H2.2 M9.8,6 H11.2 M2.3,2.3 L3.3,3.3 M8.7,8.7 L9.7,9.7 M2.3,9.7 L3.3,8.7 M8.7,3.3 L9.7,2.3", 1.2, 12, "Settings", ToggleSettings); // the settings page (Checks.cs)
            fullBox = HeadIcon("M1.5,4 V1.5 H4 M6,1.5 H8.5 V4 M8.5,6 V8.5 H6 M4,8.5 H1.5 V6", 1.3, 10, "Full Mode", EnterFull); // (Checks.cs)
            HeadIcon("M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5", 1.4, 10, "Close", Sleep);
            DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
            BuildEnergyTop(dock); // tabs, energy line, morning check-in (Energy.cs)

            // add box
            var addBox = new PillBorder { Height = 40, Background = T.Field, BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Cursor = Cursors.IBeam };
            var ag = new Grid();
            addCol = new ColumnDefinition { Width = new GridLength(36) }; // set with the + in Restyle (Ui.cs)
            ag.ColumnDefinitions.Add(addCol);
            ag.ColumnDefinitions.Add(new ColumnDefinition());
            ag.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addPlus = Glyph("M7,2.5 V11.5 M2.5,7 H11.5", T.Muted, 1.5, 14, 14);
            addPlus.HorizontalAlignment = HorizontalAlignment.Left;
            ag.Children.Add(addPlus);
            inputHint = new TextBlock { Text = "Add a task  ·  try “gym tomorrow 7am”", FontSize = Z(13), Foreground = T.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 1), IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(inputHint, 1); Grid.SetColumnSpan(inputHint, 2); ag.Children.Add(inputHint);
            input = Box(13.5);
            Grid.SetColumn(input, 1); ag.Children.Add(input);
            // what the typed line will become ("Nov 30 14:00", "low · 45m"), as a small pale-lime chip
            inputPreview = new TextBlock { FontSize = Z(11), Foreground = T.Text };
            inputChip = new PillBorder { Background = T.Soft, Padding = new Thickness(7, 2, 7, 3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false, Child = inputPreview };
            Grid.SetColumn(inputChip, 2); ag.Children.Add(inputChip);
            addBox.Child = ag;
            // under the box, what you usually do at this time of day, as chips (Work.cs)
            addPill = addBox;
            habitRow = new WrapPanel { Margin = new Thickness(2, 6, 0, 0), Visibility = Visibility.Collapsed };
            var addStack = new StackPanel(); addStack.Children.Add(addBox); addStack.Children.Add(habitRow);
            var addWrap = new Border { Margin = new Thickness(14, 0, 14, 8), Child = addStack };
            DockPanel.SetDock(addWrap, Dock.Top); leftDock.Children.Add(addWrap);
            tasksAdd = addWrap;

            input.TextChanged += (s, e) =>
            {
                inputHint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

                UpdateInputPreview();
                
            };
            input.GotKeyboardFocus += (s, e) => addBox.BorderBrush = T.Strong; // the box being typed in is outlined
            input.LostKeyboardFocus += (s, e) => addBox.BorderBrush = Brushes.Transparent;
            input.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { AddTask(); e.Handled = true; }
                else if (e.Key == Key.Escape) { if (input.Text.Length > 0) input.Clear(); else if (addOpen && !full) { addOpen = false; FocusLayout(); } else HidePanel(); e.Handled = true; }
            };

            // list
            list = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = list };
            // the task list sits in a white card, which scrolls with it
            listCard = new RimBorder { Background = T.Field };
            scroller.Content = null;
            listHost = new Grid(); listHost.Children.Add(list);
            BuildPlanStrip(); // "Today's plan", upright beside the planned tasks in Full Mode (FullLayout.cs)
            listCard.Child = listHost; scroller.Content = listCard;
            leftDock.Children.Add(BuildBody(scroller));
            scroller.SizeChanged += (s, e) => ClipList(); // (Ui.cs)
            dock.Children.Add(BuildMain());

            card.MouseLeftButtonDown += (s, e) => { Activate(); input.Focus(); };
            // the corner panel opens on the next task alone; the pointer on it brings in the header and the rest (Focus.cs)
            card.MouseEnter += (s, e) => { if (!demoRun) Reveal(); };
            card.MouseMove += (s, e) => { if (bare && !demoRun) Reveal(); };
            card.PreviewMouseWheel += (s, e) =>
            {
                if ((GetAsyncKeyState(0x11) & 0x8000) == 0) return; // Ctrl + wheel resizes
                e.Handled = true;
                SetScale(CurrentScale + (e.Delta > 0 ? 0.05 : -0.05), true);
            };

            var root = rootGrid = new Grid();
            root.Children.Add(card);
            Content = root;
        }

        /// A round white button in the header. They share a size and a spacing, which Restyle sets for the mode.
        Border HeadIcon(string path, double thickness, double size, string tip, Action click)
        {
            var cell = new PillBorder { Background = T.Field, Cursor = Cursors.Hand, ToolTip = tip, Child = Glyph(path, T.Text, thickness, size, size) };
            cell.MouseEnter += (s, e) => cell.Background = T.Lift;
            cell.MouseLeave += (s, e) => cell.Background = T.Field;
            cell.MouseLeftButtonDown += (s, e) => { e.Handled = true; click(); };
            headIcons.Children.Add(cell);
            return cell;
        }

        void UpdateHeader()
        {
            var inv = CultureInfo.InvariantCulture;
            int open = data.Tasks.Count(t => !t.Done), done = DoneToday().Count, all = open + done; // today's: what is open and what was done today
            headLogo.ToolTip = DateTime.Now.ToString("dddd, MMMM d", inv) + (open > 0 ? "  ·  " + open + " open" : "");

            DrawFocus(); // the next-task box follows every change (Focus.cs)
            double f = all == 0 ? 0 : (double)done / all;
            ringDisc.ToolTip = all == 0 ? "Progress: fills as tasks are ticked off" : "Progress: " + done + " of " + all + " tasks done today";
            const double c = 12, r = 9.5;
            if (f <= 0) ringArc.Data = null;
            else if (f >= 0.999) ringArc.Data = new EllipseGeometry(new Point(c, c), r, r);
            else
            {
                double a = f * 2 * Math.PI;
                var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
                fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, true));
                var g = new PathGeometry(); g.Figures.Add(fig);
                ringArc.Data = g;
            }
        }

        // ------------------------------------------------------------ list

        void Rebuild()
        {
            commitEdit = null;
            list.Children.Clear(); rows.Clear(); rowTimers.Clear();
            var active = data.Tasks.Where(t => !t.Done).ToList();
            var done = DoneToday(); // (Work.cs)

            foreach (var t in active) { var r = MakeRow(t); rows.Add(r); list.Children.Add(r.Wrap); }
            if (active.Count == 0)
                list.Children.Add(new TextBlock
                {
                    Text = done.Count > 0 ? "All done. Enjoy it." : "Nothing here yet.",
                    FontSize = Z(12.5), Foreground = T.Faint, Margin = new Thickness(RowOver, 6, RowOver, 6)
                });
            if (done.Count > 0 && !FullArranged) // (Full Mode shows them under the routines: FillDone)
            {
                list.Children.Add(DoneHeader(done.Count));
                if (data.DoneOpen) foreach (var t in done) list.Children.Add(MakeRow(t).Wrap);
            }
            flashId = null;
            DrawHabits(); // "Usually this morning: …" under the add box (Work.cs)
            PlacePlanStrip();
            FillDone();
            UpdateHeader();
        }

        FrameworkElement DoneHeader(int count)
        {
            // a pale-lime bar across the list: the arrow starts where the rows' circles do, "Clear" ends where the timers do
            var g = new Grid { Height = Sp(32, 28) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var chev = Glyph("M2.5,1 L5.5,4 L2.5,7", T.Text, 1.5, 8, 8);
            chev.RenderTransformOrigin = new Point(0.5, 0.5);
            chev.RenderTransform = new RotateTransform(data.DoneOpen ? 90 : 0);
            sp.Children.Add(chev);
            sp.Children.Add(new TextBlock { Text = "DONE", FontSize = Z(10), FontWeight = SemiSmall, Foreground = T.Text, Margin = new Thickness(Sp(9, 7), 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = count.ToString(), FontSize = Z(Sp(10.5, 10)), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(sp);
            if (data.DoneOpen)
            {
                var clear = new TextBlock { Text = "Clear", FontSize = Z(11.5), Foreground = T.Muted, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 4, 0, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                clear.MouseEnter += (s, e) => clear.Foreground = T.Danger;
                clear.MouseLeave += (s, e) => clear.Foreground = T.Muted;
                clear.MouseLeftButtonDown += (s, e) => { e.Handled = true; data.Tasks.RemoveAll(t => t.Done); Save(); Rebuild(); };
                g.Children.Add(clear);
            }
            var bar = new RimBorder { CornerRadius = new CornerRadius(Sp(12, 10)), Background = T.Soft, Padding = new Thickness(RowOver + 2, 0, RowOver + 2, 0), Margin = new Thickness(0, Sp(6, 4), 0, 0), Child = g, Cursor = Cursors.Hand };
            bar.MouseLeftButtonDown += (s, e) => { e.Handled = true; data.DoneOpen = !data.DoneOpen; Save(); Rebuild(); };
            return bar;
        }

        Row MakeRow(TaskItem t)
        {
            var row = new Row { Task = t, Shift = new TranslateTransform() };
            var wrap = row.Wrap = new StackPanel { ClipToBounds = true, RenderTransform = row.Shift };
            // Every row has the same columns, so circles, titles and timers each form a straight line:
            // circle | title | delete (shown on hover) | effort and date (Full Mode), then the timer | the highlight's overhang
            double rowH = Sp(34, 26), ck = Sp(18, 15);
            var main = row.Main = new Grid { Background = Brushes.Transparent, MinHeight = rowH };
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RowOver + ck + Sp(10, 8)) });
            main.ColumnDefinitions.Add(new ColumnDefinition());
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Sp(22, 18)) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RowOver) });
            wrap.Children.Add(main);

            var hover = row.Hover = new Border { CornerRadius = new CornerRadius(Sp(12, 10)), Background = T.Hover, Opacity = 0, IsHitTestVisible = false };
            Grid.SetColumnSpan(hover, 5); main.Children.Add(hover);
            if (flashId == t.Id)
            {
                var glow = new Border { CornerRadius = new CornerRadius(Sp(12, 10)), Background = T.Accent, Opacity = 0, IsHitTestVisible = false };
                Grid.SetColumnSpan(glow, 5); main.Children.Add(glow);
                glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.28, TimeSpan.FromMilliseconds(520)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
            }

            // checkbox: drawn at 18 and scaled to the mode's size
            var box = new Grid { Height = rowH, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var mark = new Grid { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(RowOver, 0, 0, 0), LayoutTransform = new ScaleTransform(ck / 18, ck / 18) };
            var ringHot = new Ellipse { Width = 18, Height = 18, Fill = T.Soft, Stroke = T.Text, StrokeThickness = 1.5, Opacity = 0, IsHitTestVisible = false }; // under the mouse the circle fills pale lime
            var fillScale = new ScaleTransform(t.Done ? 1 : 0, t.Done ? 1 : 0);
            var fill = new Ellipse { Width = 18, Height = 18, Fill = t.Done ? (Brush)T.Ctl : T.Accent, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = fillScale, IsHitTestVisible = false };
            // an open task's tick shows only under the mouse, faintly, to say the circle ticks it off; the task being
            // timed has an empty circle like the rest (a tick there looked like it was already done)
            var tick = Glyph("M5,9.4 L7.9,12.2 L13.2,6.2", T.Text, 1.8, 18, 18);
            tick.Opacity = t.Done ? 1 : 0;
            mark.Children.Add(new Ellipse { Width = 18, Height = 18, Stroke = t.Done ? (Brush)T.Ctl : T.Text, StrokeThickness = 1.5, IsHitTestVisible = false });
            mark.Children.Add(ringHot); mark.Children.Add(fill); mark.Children.Add(tick);
            box.Children.Add(mark);
            box.MouseEnter += (s, e) => { Anim(ringHot, OpacityProperty, 1, 100); if (!t.Done) Anim(tick, OpacityProperty, 0.5, 100); };
            box.MouseLeave += (s, e) => { Anim(ringHot, OpacityProperty, 0, 160); if (!t.Done) Anim(tick, OpacityProperty, 0, 160); };
            main.Children.Add(box);

            // title
            var label = new TextBlock
            {
                Text = t.Title, TextWrapping = TextWrapping.Wrap, FontSize = Z(13.5), Foreground = t.Done ? T.Muted : T.Text,
                Margin = Slim ? new Thickness(0, 5, 4, 6) : new Thickness(0, 8, 6, 9), VerticalAlignment = VerticalAlignment.Center, Cursor = t.Done ? Cursors.Arrow : Cursors.IBeam
            };
            if (Slim)
            {   // the quick-glance list keeps a title to two lines; the whole of it is on the row's tooltip
                double timerW = t.Done ? 0 : 18 + Math.Max(TextW("00:00", Z(11), SemiSmall), TextW(TimerText(t), Z(11), SemiSmall));
                double titleW = CardW - 2 * Side - 2 * (PadX - RowOver) - (RowOver + ck + Sp(10, 8)) - Sp(22, 18) - timerW - RowOver - label.Margin.Right;
                label.Text = ClampLines(t.Title, titleW, label.FontSize, 2);
            }
            if (t.Done) label.TextDecorations = TextDecorations.Strikethrough;
            Grid.SetColumn(label, 1); main.Children.Add(label);

            // due chip (or a faint clock on hover when there is no date)
            FrameworkElement clockIcon = null, etag = null;
            if (!t.Done)
            {
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Height = rowH, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Cursor = Cursors.Hand };
                if (t.Routine != null) chip.Children.Add(RoutineMark());
                etag = EnergyTag(t); chip.Children.Add(etag); // deep / light / unset, one click cycles
                // by priority (Work.cs) nothing is "overdue", and a time an old schedule gave a task is not shown
                bool prioRow = PriorityMode, overdue = t.Overdue && !prioRow;
                var due = prioRow && t.Planned ? null : t.DueAt;
                if (due != null)
                {
                    Brush b = overdue ? T.Danger : due.Value.Date <= DateTime.Today ? (Brush)T.Text : T.Muted;
                    if (t.HasTime)
                    {
                        var bell = Glyph("M5,1.2 C3.3,1.2 2.3,2.6 2.3,4.3 V6.2 L1.3,7.8 H8.7 L7.7,6.2 V4.3 C7.7,2.6 6.7,1.2 5,1.2 Z M4.2,9.3 H5.8", b, 1, 10, 10.5);
                        bell.Margin = new Thickness(0, 1, 4, 0);
                        chip.Children.Add(bell);
                    }
                    // overdue says so in words, not only in red
                    string when = When.Label(due.Value, t.HasTime);
                    if (overdue) when = "Overdue · " + (due.Value.Date == DateTime.Today ? due.Value.ToString("t", CultureInfo.CurrentCulture) : when.StartsWith("Yesterday") ? "y" + when.Substring(1) : when);
                    var whenText = new TextBlock { Text = when, FontSize = Z(11.5), Foreground = b, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 1) }; if (overdue) Lean(whenText);
                    if (overdue || due.Value.Date <= DateTime.Today)
                    {   // today and overdue are tags: pale lime, and a pale red
                        whenText.Margin = new Thickness(0, 0, 0, 1);
                        chip.Children.Add(new PillBorder { Background = overdue ? T.DangerSoft : T.Soft, Padding = new Thickness(8, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center, Child = whenText });
                    }
                    else chip.Children.Add(whenText);
                }
                else
                {
                    clockIcon = Glyph("M6,1 A5,5 0 1 1 5.99,1 Z M6,3.4 V6 L7.9,7.1", T.Muted, 1.1, 12, 12);
                    clockIcon.Margin = new Thickness(0, 0, 8, 0);
                    clockIcon.Opacity = whenOpenId == t.Id ? 1 : 0;
                    chip.Children.Insert(0, clockIcon);
                }
                chip.MouseLeftButtonDown += (s, e) => { e.Handled = true; whenOpenId = whenOpenId == t.Id ? null : t.Id; Rebuild(); };
                main.Children.Add(RowSide(t, chip, main)); // the timer; effort and date beside it in Full Mode, on the tooltip otherwise (Focus.cs)
            }
            else
            {   // done: how long it was worked on and from when to when (Work.cs). Beside the title in Full Mode, under it in the corner panel
                string note = DoneNote(t);
                if (note != null)
                {
                    var noteText = new TextBlock { Text = note, FontSize = Z(11), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center };
                    if (Slim) { noteText.Margin = new Thickness(RowOver + ck + Sp(10, 8), -4, RowOver, 5); wrap.Children.Add(noteText); }
                    else { noteText.Margin = new Thickness(8, 0, 0, 1); Grid.SetColumn(noteText, 3); main.Children.Add(noteText); }
                }
            }

            // delete
            var del = new Grid { Height = rowH, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Cursor = Cursors.Hand, Opacity = 0, ToolTip = "Delete" };
            var cross = Glyph("M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5", T.Muted, 1.4, 10, 10);
            del.Children.Add(cross);
            del.MouseEnter += (s, e) => cross.Stroke = T.Danger;
            del.MouseLeave += (s, e) => cross.Stroke = T.Muted;
            Grid.SetColumn(del, 2); main.Children.Add(del);

            main.MouseEnter += (s, e) =>
            {
                if (dragging) return;
                Anim(hover, OpacityProperty, 1, 90); Anim(del, OpacityProperty, 1, 90);
                if (clockIcon != null) Anim(clockIcon, OpacityProperty, 1, 90);
                if (etag is Grid) Anim(etag, OpacityProperty, 1, 90);
            };
            main.MouseLeave += (s, e) =>
            {
                if (dragging) return;
                Anim(hover, OpacityProperty, 0, 180); Anim(del, OpacityProperty, 0, 180);
                if (clockIcon != null && whenOpenId != t.Id) Anim(clockIcon, OpacityProperty, 0, 180);
                if (etag is Grid) Anim(etag, OpacityProperty, 0, 180);
            };

            box.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                Click.Play(); // (Sound.cs)
                if (t.Done)
                {
                    t.Done = false; t.DoneAt = null;
                    LogUndone(t); // out of done.csv again (Stats.cs)
                    data.Tasks.Remove(t); data.Tasks.Add(t);
                    Save(); Rebuild();
                    return;
                }
                bool wasNext = IsFocus(t);
                t.Done = true; t.DoneAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                LogDone(t); // a row in done.csv (Stats.cs)
                TaskCompleted(t, wasNext); // stops its timer and may offer the next task (Focus.cs)
                if (whenOpenId == t.Id) whenOpenId = null;
                Save(); UpdateHeader();
                main.IsHitTestVisible = false;
                Anim(fillScale, ScaleTransform.ScaleXProperty, 1, 260, ease: Pop);
                Anim(fillScale, ScaleTransform.ScaleYProperty, 1, 260, ease: Pop);
                tick.Stroke = T.OnAccent;
                tick.StrokeDashArray = new DoubleCollection(new double[] { 9, 9 });
                tick.StrokeDashOffset = 9; Anim(tick, OpacityProperty, 1, 1); // (an animation, as the hover's is one)
                Anim(tick, Shape.StrokeDashOffsetProperty, 0, 240, delay: 90);
                label.TextDecorations = TextDecorations.Strikethrough;
                Anim(label, OpacityProperty, 0.45, 260);
                pending++;
                After(520, () => Collapse(wrap, 220));
            };

            del.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (timerId == t.Id) StopTimer(); // (its stretch of work is logged first: Work.cs)
                data.Tasks.Remove(t);
                if (whenOpenId == t.Id) whenOpenId = null;
                Save(); UpdateHeader();
                main.IsHitTestVisible = false;
                pending++;
                Collapse(wrap, 180);
            };

            if (!t.Done)
            {
                main.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    dragRow = row; dragging = false; dragOnLabel = label.IsMouseOver;
                    dragStart = e.GetPosition(list);
                    main.CaptureMouse();
                };
                main.MouseMove += (s, e) =>
                {
                    if (dragRow != row || !main.IsMouseCaptured) return;
                    double dy = e.GetPosition(list).Y - dragStart.Y;
                    if (!dragging && Math.Abs(dy) > 5 && rows.Count > 1 && commitEdit == null) BeginDrag();
                    if (dragging) UpdateDrag(dy);
                };
                main.MouseLeftButtonUp += (s, e) =>
                {
                    if (dragRow != row) return;
                    bool wasDrag = dragging, onLabel = dragOnLabel;
                    if (wasDrag) EndDrag();
                    dragRow = null;
                    main.ReleaseMouseCapture();
                    if (!wasDrag && onLabel) BeginEdit(row, label);
                };
                main.LostMouseCapture += (s, e) => { if (dragRow == row && dragging) { dragging = false; dragRow = null; Rebuild(); } };

                if (whenOpenId == t.Id) wrap.Children.Add(WhenStrip(t));
            }
            return row;
        }

        void Collapse(FrameworkElement el, int ms)
        {
            Anim(el, OpacityProperty, 0, ms);
            Anim(el, HeightProperty, 0, ms, from: el.ActualHeight, done: () => { if (--pending <= 0) { pending = 0; Rebuild(); } });
        }

        // ------------------------------------------------------------ add / edit

        void AddTask()
        {
            string text = input.Text.Trim();
            if (text.Length == 0) return;
            string title; DateTime due; bool ht;
            var t = new TaskItem { Id = Guid.NewGuid().ToString("N") };
            string en; int mins;
            string kind;
            text = TaskTags.Extract(text, out en, out mins, out kind); // "!high", "!med", "!low", "!phys", "45m"
            t.Energy = en; t.Minutes = mins; t.Kind = kind == "physical" ? kind : null;
            if (When.Parse(text, out title, out due, out ht)) t.SetDue(due, ht);
            t.Title = title;
            data.Tasks.Insert(0, t);
            Save();
            input.Clear();
            Rebuild();
            if (rows.Count > 0)
            {
                var w = rows[0].Wrap;
                list.UpdateLayout();
                double h = w.ActualHeight;
                Anim(w, OpacityProperty, 1, 260, from: 0);
                Anim(w, HeightProperty, h, 260, from: 0, ease: Out, done: () => Set(w, HeightProperty, double.NaN));
            }
        }

        void BeginEdit(Row row, TextBlock label)
        {
            var t = row.Task;
            var tb = Box(13.5);
            tb.Text = t.Title; tb.TextWrapping = TextWrapping.Wrap;
            tb.Margin = Slim ? new Thickness(-2, 5, 4, 6) : new Thickness(-2, 8, 6, 9); tb.VerticalAlignment = VerticalAlignment.Center; // -2: the box's own inner margin
            label.Visibility = Visibility.Hidden;
            Grid.SetColumn(tb, 1); row.Main.Children.Add(tb);
            bool closed = false;
            Action<bool> finish = keep =>
            {
                if (closed) return;
                closed = true; commitEdit = null;
                string text = tb.Text.Trim();
                if (keep && text.Length > 0)
                {
                    string en; int mins;
                    string kind;
                    text = TaskTags.Extract(text, out en, out mins, out kind);
                    if (kind != null) t.Kind = kind == "physical" ? kind : null;
                    if (en != null) t.Energy = en;
                    if (mins > 0) t.Minutes = mins;
                }
                if (keep && text.Length > 0)
                {
                    string title; DateTime due; bool ht;
                    if (When.Parse(text, out title, out due, out ht)) t.SetDue(due, ht);
                    t.Title = title;
                    Save();
                }
                Dispatcher.BeginInvoke(new Action(Rebuild));
            };
            commitEdit = () => finish(true);
            tb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { finish(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { finish(false); e.Handled = true; }
            };
            tb.LostKeyboardFocus += (s, e) => finish(true);
            Activate(); tb.Focus(); tb.CaretIndex = tb.Text.Length;
        }

        FrameworkElement WhenStrip(TaskItem t)
        {
            var strip = new WrapPanel { Margin = new Thickness(RowOver + Sp(18, 15) + Sp(10, 8), 0, RowOver, Sp(8, 4)) }; // under the title
            Action<DateTime> setDate = d =>
            {
                var old = t.DueAt; bool planned = t.Planned && t.HasTime && old != null && old.Value.Date == d.Date;
                t.SetDue(t.HasTime && old != null ? d + old.Value.TimeOfDay : d, t.HasTime);
                t.Planned = planned; // picking the same day again keeps a planned time movable by Plan
                whenOpenId = null; Save(); Rebuild();
            };
            var today = DateTime.Today;
            int toMon = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
            strip.Children.Add(Pill("Today", () => setDate(today)));
            strip.Children.Add(Pill("Tomorrow", () => setDate(today.AddDays(1))));
            strip.Children.Add(Pill("Next week", () => setDate(today.AddDays(toMon == 0 ? 7 : toMon))));

            var tb = Box(11.5);
            var hint = new TextBlock { Text = "or “fri 3pm”", FontSize = Z(11.5), Foreground = T.Faint, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 1) };
            var fg = new Grid { Width = 76 }; fg.Children.Add(hint); fg.Children.Add(tb);
            strip.Children.Add(new PillBorder { Background = T.Field, BorderBrush = T.Ctl, BorderThickness = new Thickness(1), Padding = new Thickness(8, 3, 8, 4), Margin = new Thickness(0, 0, 6, 6), Child = fg, Cursor = Cursors.IBeam });
            tb.TextChanged += (s, e) => { hint.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; tb.Foreground = T.Text; };
            tb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { whenOpenId = null; Rebuild(); e.Handled = true; }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    string title; DateTime due; bool ht;
                    if (When.Parse("x " + tb.Text, out title, out due, out ht) && title == "x")
                    { t.SetDue(due, ht); whenOpenId = null; Save(); Rebuild(); }
                    else tb.Foreground = T.Danger;
                }
            };
            if (t.DueAt != null)
                strip.Children.Add(Pill("Clear", () => { t.SetDue(null, false); whenOpenId = null; Save(); Rebuild(); }));
            return strip;
        }

        FrameworkElement Pill(string text, Action click)
        {
            var label = new TextBlock { Text = text, FontSize = Z(11.5), Foreground = T.Text, Padding = new Thickness(0, 0, 1, 0) }; // 1 px of slack: layout rounding otherwise clips the last letter
            var b = new PillBorder { Background = T.Raised, Padding = Slim ? new Thickness(9, 3, 8, 4) : new Thickness(11, 4, 10, 5), Margin = new Thickness(0, 0, 6, 6), Child = label, Cursor = Cursors.Hand };
            b.MouseEnter += (s, e) => b.Background = T.Ctl;
            b.MouseLeave += (s, e) => b.Background = T.Raised;
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; click(); };
            return b;
        }

        // ------------------------------------------------------------ drag to reorder

        void BeginDrag()
        {
            dragging = true;
            if (whenOpenId != null) { /* keep layout stable: strips stay where they are */ }
            dIndex = rows.IndexOf(dragRow);
            dTops = rows.Select(r => r.Wrap.TranslatePoint(new Point(0, 0), list).Y).ToArray();
            dHeights = rows.Select(r => r.Wrap.ActualHeight).ToArray();
            dTargets = new double[rows.Count];
            Panel.SetZIndex(dragRow.Wrap, 5);
            Anim(dragRow.Hover, OpacityProperty, 1, 80);
        }

        void UpdateDrag(double dy)
        {
            int d = dIndex, n = rows.Count;
            dy = Math.Max(dTops[0] - dTops[d], Math.Min(dTops[n - 1] + dHeights[n - 1] - dTops[d] - dHeights[d], dy));
            Set(dragRow.Shift, TranslateTransform.YProperty, dy);
            double top = dTops[d] + dy, bottom = top + dHeights[d];
            for (int i = 0; i < n; i++)
            {
                if (i == d) continue;
                double mid = dTops[i] + dHeights[i] / 2, target = 0;
                if (i < d && top < mid) target = dHeights[d];
                if (i > d && bottom > mid) target = -dHeights[d];
                if (target != dTargets[i]) { dTargets[i] = target; Anim(rows[i].Shift, TranslateTransform.YProperty, target, 180); }
            }
        }

        void EndDrag()
        {
            dragging = false;
            int d = dIndex, above = 0, below = 0; double final = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (i < d && dTargets[i] > 0) { above++; final -= dHeights[i]; }
                if (i > d && dTargets[i] < 0) { below++; final += dHeights[i]; }
            }
            int to = d - above + below;
            var moved = dragRow.Task;
            var active = rows.Select(r => r.Task).ToList();
            active.RemoveAt(d); active.Insert(to, moved);
            var rest = data.Tasks.Where(t => t.Done).ToList();
            data.Tasks = active.Concat(rest).ToList();
            Save();
            Anim(dragRow.Shift, TranslateTransform.YProperty, final, 130, done: Rebuild);
        }

        // ------------------------------------------------------------ show / hide

        static bool Busy()
        {
            try { int st; return SHQueryUserNotificationState(out st) == 0 && (st == 2 || st == 3 || st == 4); }
            catch { return false; }
        }

        void MouseTick()
        {
            POINT p;
            if (demoRun) return; // a test render: the real pointer neither opens nor hides the panel
            if (full) return; // Full Mode stays until closed; the hot corner is idle meanwhile
            if (!GetCursorPos(out p)) return;
            var sb = WF.Screen.PrimaryScreen.Bounds;
            bool inCorner = (OnRight ? p.X >= sb.Right - 4 && p.X < sb.Right : p.X >= sb.Left && p.X <= sb.Left + 3)
                         && (OnTop ? p.Y >= sb.Top && p.Y <= sb.Top + 3 : p.Y >= sb.Bottom - 4 && p.Y < sb.Bottom);
            bool btn = (GetAsyncKeyState(1) & 0x8000) != 0;
            long now = clock.ElapsedMilliseconds;

            if (!shown)
            {
                if (!inCorner) { cornerSince = 0; cornerBlocked = false; return; }
                if (paused) return;
                if (btn) cornerBlocked = true; // clicking a window's close button is not a request for the list
                if (cornerBlocked) return;
                if (cornerSince == 0) cornerSince = now;
                else if (now - cornerSince >= 140 && !Busy()) ShowPanel(0);
                return;
            }

            if (promptOnly && inCorner) { promptOnly = false; bare = false; ApplyPromptOnly(); } // the corner brings back the whole list
            bool inside = inCorner || InKeepZone(p);
            if (inside) { awaySince = 0; peekUntil = floating && inCorner && !InKeepZone(p) ? now + 2500 : 0; } // moved panel: the wait restarts while you rest in the corner
            else if (btn || dragging || now < peekUntil || AlertHolds()) awaySince = 0; // a time-up prompt stays until answered
            else if (awaySince == 0) awaySince = now;
            else if (now - awaySince > 260) HidePanel();
        }

        bool InKeepZone(POINT p)
        {
            try
            {
                var a = card.PointToScreen(new Point(0, 0));
                var b = card.PointToScreen(new Point(card.ActualWidth, card.ActualHeight));
                double m = 18 * (b.X - a.X) / Math.Max(1, card.ActualWidth);
                // generous on the two sides that face the corner, so the trip from the corner to the card (across the taskbar, at the bottom) does not hide it
                return p.X >= a.X - (OnRight ? m : 4 * m) && p.X <= b.X + (OnRight ? 4 * m : m) && p.Y >= a.Y - (OnTop ? 4 * m : m) && p.Y <= b.Y + (OnTop ? m : 4 * m);
            }
            catch { return true; }
        }

        public void ShowPanel(int peekMs)
        {
            if (peekMs > 0) peekUntil = clock.ElapsedMilliseconds + peekMs;
            else if (!full && !shown && Settings.Get("Moved", 0) > 0.5) peekUntil = clock.ElapsedMilliseconds + 3000; // a moved panel: time to reach it from the corner
            if (shown) return;
            GrabBackdrop(); // the Glass skin's picture of what is behind, taken while the panel is still off the screen (Glass.cs)
            Place();
            
            SyncFromDisk();
            shown = true; gen++; awaySince = 0;
            bare = !full; // the corner panel opens on the next task alone (Focus.cs)
            RefreshEnergy(true);
            ApplyPromptOnly(); // a check-in that popped up by itself shows alone (Checks.cs)
            Rebuild();
            Show();
            AdaptGlassPanel(); // the Glass skin: light or dark glass, by what is behind the panel (Glass.cs)
            if (switchWarp) { card.BeginAnimation(OpacityProperty, null); card.Opacity = 0; } // the warp brings it in (Launch.cs)
            else Anim(card, OpacityProperty, 1, 160);
            Anim(cardShift, TranslateTransform.XProperty, 0, 320, from: OnRight ? 18 : -18, ease: Out);
            Anim(cardShift, TranslateTransform.YProperty, 0, 320, from: OnTop ? -12 : 12, ease: Out);
            Anim(cardScale, ScaleTransform.ScaleXProperty, 1, 320, from: 0.955, ease: Out);
            Anim(cardScale, ScaleTransform.ScaleYProperty, 1, 320, from: 0.955, ease: Out);
            bool launching = launchZoom, genieOpen = launching || (!full && !switchWarp && (!demoRun || demoGenie));
            if (genieOpen) LaunchZoom(launching); // out of its corner with the Genie; out of the app icon when Corner starts (Launch.cs)
            int i = 0;
            foreach (UIElement el in list.Children)
            {
                if (i >= 14 || genieOpen || switchWarp) break; // (the launch draws the panel as one picture; nothing moves inside it)
                el.Opacity = 0;
                Anim(el, OpacityProperty, 1, 240, delay: 50 + i * 24);
                i++;
            }
        }

        void HidePanel()
        {
            if (!shown) return;
            if (full) { ExitFull(); return; }
            shown = false; int g = ++gen;
            if (commitEdit != null) commitEdit();
            whenOpenId = null; peekUntil = 0;
            if (genieBack && GenieHide(() => { if (gen == g) { Hide(); card.Opacity = 0; } })) return; // back into its corner with the Genie (Launch.cs)
            Anim(cardShift, TranslateTransform.XProperty, OnRight ? 10 : -10, 140);
            Anim(cardShift, TranslateTransform.YProperty, OnTop ? -6 : 6, 140);
            Anim(card, OpacityProperty, 0, 140, done: () => { if (gen == g) Hide(); });
        }

        // Test hook (only with CORNER_DATA set): fills a throwaway list and renders the card to a PNG.
        public void Demo(string path, string mode)
        {
            if (mode.StartsWith("onboard")) { DemoOnboard(path, mode.Length > 7 ? mode[7] - '0' : 0); return; } // the welcome window (Onboard.cs)
            if (mode == "rise" || mode == "dip") { DemoToast(path, mode == "dip"); return; } // the energy notice where it would show (Toast.cs)
            if (mode == "riselive" || mode == "diplive") { ShowNudge(mode == "diplive" ? DipLine() : RiseLine()); After(ToastMs + 1500, () => { tray.Visible = false; Application.Current.Shutdown(); }); return; } // the same, on the real screen
            if (!mode.StartsWith("real") || mode == "real")
            {   // "realplan" / "realsplit": the test folder's own tasks, untouched
                foreach (var s in new[] { "Book flights", "Finish the quarterly report and send it to the whole team friday", "Buy milk", "Call Sam tomorrow 9am", "Water the plants today", "Stretch in 0 min" }) { input.Text = s; AddTask(); }
                data.Tasks[5].Done = true; data.Tasks[5].DoneAt = "x"; data.DoneOpen = true; whenOpenId = data.Tasks[2].Id;
                data.Tasks[3].SetDue(DateTime.Today.AddDays(-1), false);
                input.Text = "Dentist nov 30 at 2pm";
            }
            // "rest…" modes keep the corner panel as it opens (the next task alone); "resthover" then brings the rest in, as the pointer would
            bool rest = mode.StartsWith("rest"); if (rest && mode.Length > 4) mode = mode.Substring(4);
            demoRun = true;
            if (mode == "launch" || mode == "launchclose") launchZoom = true;
            if (mode == "hoveropen" || mode == "hoverclose") demoGenie = true;
            DemoSeed(mode);
            ShowPanel(600000);
            if (!rest) Reveal(); else if (mode == "hover") After(500, Reveal);
            DemoAfterShow(mode);
            if (mode == "feelafter" && energy != null) { SetTab(1); var ft = data.Tasks.FirstOrDefault(x => !x.Done); feelAfter = new FeelAfter { Replan = true, HighId = ft != null ? ft.Id : null, Felt = 63 }; DrawEnergy(); }
            if (mode == "feel" && energy != null) { SetTab(1); var fn = Now(); feelAsk = new FeelAsk { Felt = 0.63, Model = energy.AllAt(fn), Shown = 30, At = fn }; DrawEnergy(); } // the counter corrected (EnergyFeel.cs)
            if (mode == "launch" || mode == "launchclose" || mode == "hoveropen" || mode == "hoverclose") { DemoLaunch(path, mode.EndsWith("close")); return; } // frames of the Genie effect (Launch.cs)
            if (mode == "cycleopen" || mode == "cycleclose") { DemoCycle(path, mode == "cycleclose"); return; } // the cycle out of its button
            if (mode == "fullswitch" || mode == "fullexit") { DemoSwitch(path, mode == "fullexit"); return; } // and of the switch to Full Mode and back
            After(mode == "drag" || mode == "drawtime" || mode == "livetime" ? 55000 : Environment.GetEnvironmentVariable("CORNER_FULL") == "1" ? 3200 : 1500, () => // (Full Mode needs longer to open)
            {
                var root = (Grid)Content;
                root.Background = new SolidColorBrush(Color.FromRgb(185, 177, 151)); // a sand backdrop for the test renders
                if (T.Glass && Backdrop.Sharp != null)
                {   // the Glass skin: the made-up wallpaper it is frosting, placed as the window is on the screen
                    var o = root.PointToScreen(new Point(0, 0)); var q = root.PointToScreen(new Point(root.ActualWidth, root.ActualHeight));
                    root.Background = new ImageBrush(Backdrop.Sharp) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect((o.X - Backdrop.Left) / 2, (o.Y - Backdrop.Top) / 2, (q.X - o.X) / 2, (q.Y - o.Y) / 2) };
                }
                root.UpdateLayout();
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(root.ActualWidth * 2), (int)(Math.Min(root.ActualHeight, 900) * 2), 192, 192, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using (var fs = IO.File.Create(path)) enc.Save(fs);
                tray.Visible = false;
                Application.Current.Shutdown();
            });
        }

        // ------------------------------------------------------------ reminders

        void CheckReminders()
        {
            SyncFromDisk();
            var now = DateTime.Now; TaskItem hit = null; int n = 0;
            foreach (var t in data.Tasks)
            {
                if (t.Done || !t.HasTime || t.Notified) continue;
                var d = t.DueAt;
                if (d != null && d.Value <= now) { t.Notified = true; if (hit == null) hit = t; n++; }
            }
            if (hit == null) return;
            Save();
            try { tray.ShowBalloonTip(8000, n == 1 ? "Reminder" : n + " reminders", hit.Title, WF.ToolTipIcon.None); } catch { }
            if (Busy() || paused) return;
            flashId = hit.Id;
            if (shown) Rebuild(); else ShowPanel(9000);
        }
    }

    static class Program
    {
        static System.Threading.Mutex mutex;

        public static void Restart()
        {
            try { mutex.ReleaseMutex(); } catch { }
            try { Process.Start(System.Reflection.Assembly.GetEntryAssembly().Location); } catch (Exception ex) { Store.Log(ex); }
            Application.Current.Shutdown();
        }

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            bool test = Environment.GetEnvironmentVariable("CORNER_DATA") != null;
            // a test run is one instance per data folder, so renders never wait on (or get blocked by) another test copy
            mutex = new System.Threading.Mutex(true, "Corner.Todo.SingleInstance" + (test ? ".Test." + Store.Dir.ToLowerInvariant().GetHashCode().ToString("x") : ""), out created);
            if (!created) return;
            // Corner draws itself without the graphics card. Asked to use it, WPF sets up a 3D device, and on a laptop
            // whose graphics share the main memory (Intel Iris Xe here) that alone took about 300 MB, for as long as
            // Corner ran, whatever was on the screen: an empty window cost the same. A panel this small is drawn as
            // smoothly without it (the shadow and the Glass picture were changed to make that so: Shade.cs, Glass.cs).
            // "Gpu": 1 in settings.json brings the graphics card back.
            if (Settings.Get("Gpu", 0) < 0.5) RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) => { Store.Log(e.Exception); e.Handled = true; };
            var shell = new Shell();
            shell.Start(args.Contains("--peek"), test);
            if (test && args.Length >= 2 && args[0] == "--demo") shell.Demo(args[1], args.Length > 2 ? args[2] : "tasks");
            app.Run();
            GC.KeepAlive(mutex);
        }
    }
}

