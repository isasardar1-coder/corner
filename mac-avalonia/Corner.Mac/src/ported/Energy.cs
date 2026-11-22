// Ported from corner/src/Energy.cs (WPF) to Avalonia: tools/port_ui.py, then fixed by hand.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using IO = System.IO;

namespace Corner
{
    partial class Shell
    {
        EnergyDay energy;
        NeedEstimate.Result needHint; // null until the first refresh
        int tab; // 0 = tasks, 1 = energy
        TextBlock tabTasks, tabEnergy, energyLine;
        Border checkIn, tasksAdd;
        ScrollViewer tasksScroll, energyScroll;
        StackPanel energyView;


        Control BuildBody(ScrollViewer tasks)
        {
            tasksScroll = tasks;
            energyView = new StackPanel { Margin = new Thickness(20, 2, 20, 14) };
            energyScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false, Content = energyView, IsVisible = false
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
            tabTasks.Foreground = i == 0 ? T.Text : T.Muted; tabTasks.FontWeight = i == 0 ? FontWeight.SemiBold : FontWeight.Normal;
            tabEnergy.Foreground = i == 1 ? T.Text : T.Muted; tabEnergy.FontWeight = i == 1 ? FontWeight.SemiBold : FontWeight.Normal;
            tabLineTasks.Opacity = i == 0 ? 1 : 0; tabLineEnergy.Opacity = i == 1 ? 1 : 0;
            if (full)
            {   // Full Mode: no tabs; tasks on the left, energy on the right
                tabsRow.IsVisible = false;
                tasksAdd.IsVisible = true; tasksScroll.IsVisible = true; energyScroll.IsVisible = true;
                MoveEnergy(mainGrid, 1); energyScroll.Width = FullW / 2; // the second of two equal columns
                LayoutTop(); DrawEnergy(); FocusLayout();
                return;
            }
            tabsRow.IsVisible = true; MoveEnergy(bodyGrid, 0); energyScroll.Width = double.NaN; LayoutTop();
            var tasksOn = i == 0 ? true : false;
            tasksAdd.IsVisible = tasksOn; tasksScroll.IsVisible = tasksOn; energyChip.IsVisible = tasksOn;
            energyScroll.IsVisible = i == 1 ? true : false;
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
            energyDot.Fill = ZoneBrush(now); energyDot.IsVisible = energy == null ? false : true;
            if (opening)
            {
                chartOffset = -1; // the chart starts centred on now each time the panel opens (UiSleep.cs)
                EnsureRoutines(SleepLog.Today(now)); // today's routines join the task list (Plan.cs)
                SetTab(0);
                if (SleepLog.NeedsCheckIn(now)) OpenCheckIn(false); else checkIn.IsVisible = false;
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
                tag.OnLeftDown((s, e) => { e.Handled = true; manual = true; SetPm(!pm); });
                var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(g); sp.Children.Add(tag);
                Host = new PillBorder { Width = HostWidth, Background = T.Field, BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Padding = new Thickness(10, 4, 7, 5), Margin = new Thickness(0, 0, 6, 6), Child = sp, Cursor = Cursors.IBeam };
                Box.GotFocus += (s, e) => Host.BorderBrush = T.Strong;   // the box being typed in is outlined
                Box.LostFocus += (s, e) => Host.BorderBrush = Brushes.Transparent;
                SetPm(!waking);
                Box.TextChanged += (s, e) =>
                {
                    if (fixing) return;
                    string clean = new string(Box.Text.Where(ch => char.IsDigit(ch) || ch == ':').ToArray());
                    if (clean != Box.Text) { fixing = true; Box.Text = clean; Box.CaretIndex = clean.Length; fixing = false; }
                    hint.IsVisible = Box.Text.Length == 0 ? true : false;
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
        static Border MiniBox(string text, double width, out TextBox box, IBrush surface = null)
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

        void DemoAfterShow(string mode) { if (mode == "all") { allOpen = true; FocusLayout(); } if (mode == "adding") { addOpen = true; FocusLayout(); } if (mode == "timer") { StartTimer(Upcoming()[0]); timerEnd = timerEnd.AddSeconds(-754); RepaintTimers(); DrawFocus(); } if (mode == "timeup") { StartTimer(Upcoming()[0]); TimeUp(); } if (mode == "timeuppop") { shown = false; gen++; Hide(); StartTimer(Upcoming()[0]); TimeUp(); } if (mode == "nextup") { CompleteTask(Upcoming()[0]); } if (mode == "moved") { if (Environment.GetEnvironmentVariable("CORNER_FULL") == "1") EnterFull(); moving = true; moveLeft = Left; moveTop = Top; Left -= 300; Top += 120; EndMove(); } if (Environment.GetEnvironmentVariable("CORNER_FULL") == "1") EnterFull(); if (mode == "editsplit") { SleepLog.Record(SleepLog.BuildSplit(new[] { new TimeSpan(23, 0, 0), new TimeSpan(4, 45, 0), new TimeSpan(6, 30, 0) }, new[] { new TimeSpan(4, 0, 0), new TimeSpan(6, 0, 0), new TimeSpan(7, 30, 0) }, SleepLog.Today(Now()))); RefreshEnergy(false); SetTab(1); OpenCheckIn(true); } if (mode == "energy" || mode == "real" || mode == "plan" || mode == "split" || mode == "nap" || mode == "latenap") SetTab(1); if (mode == "feedback") { Feedback.Log(Now(), "7", energy); ShowFeedback(Now()); } if (mode == "routines") { routinesOpen = true; SetTab(1); } if (mode == "drag") { SetTab(1); After(1200, () => { var p = chartThumb.Pts(new Point(chartThumb.Bounds.Width / 2, chartThumb.Bounds.Height / 2)); IO.File.WriteAllText(IO.Path.Combine(Store.Dir, "thumb.txt"), (int)p.X + "," + (int)p.Y); }); } if (mode == "scroll") { chartOffset = 520; SetTab(1); } if (mode == "addroutine") { routinesOpen = true; newRoutineText = "Read a paper"; newRoutineEnergy = "high"; newRoutineMin = 45; SetTab(1); After(400, () => energyScroll.ScrollToEnd()); } if (mode == "debt") { debtOpen = true; SetTab(1); } if (mode == "history") { ToggleSettings(); After(400, () => settingsScroll.ScrollToEnd()); } if (mode == "settings") ToggleSettings(); if (mode == "plan") MakePlan(); if (mode == "applied") { MakePlan(); ApplyPlan(); SetTab(0); } if (mode == "routinesone") { routinesOpen = true; SetTab(1); AddRoutinesToToday(Routines.Data.Routines.Take(1)); } if (mode == "toglass" || mode == "tosage") { ToggleSettings(); After(300, () => SetSkin(mode == "toglass" ? 1 : 0)); } }
    }
}
