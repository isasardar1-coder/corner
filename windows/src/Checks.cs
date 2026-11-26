// Corner - energy check-ins (1 to 10), tuning the curve from them, and Full Mode.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IO = System.IO;

namespace Corner
{
    // ---------------------------------------------------------------- check-in log (checkins.csv, beside tasks.json)

    /// "How fresh do you feel?" answers, 1 (drained) to 10 (fresh), and when to ask.
    public static class Feedback
    {
        public const int IntensiveDays = 14;     // days of frequent asking, counted from the first check-in
        public const double IntensiveEvery = 1.5;  // hours between prompts during those days
        public const int IntensiveMax = 8;         // prompts per day during those days
        public const double NormalEvery = 3.0;     // afterwards: hours between prompts
        public const int NormalMax = 4;            // afterwards: prompts per day
        public const double FirstAfterWake = 0.25; // hours after waking before the first prompt
        public const double IdleMax = 120;         // seconds without keyboard or mouse before you count as away
        public const double SettledMin = 5;        // minutes back at the PC before a prompt may appear

        public class Entry
        {
            public DateTime At;
            public int Score;          // 1-10; 0 = dismissed
            public double Predicted;   // what the untuned model said for that moment (0-1)
            public double SinceWake;   // hours since the final wake-up
        }

        static string File { get { return IO.Path.Combine(Store.Dir, "checkins.csv"); } }
        const string Fmt = "yyyy-MM-ddTHH:mm";
        const string Head = "time,score,predicted,hours_since_wake,active_min_today,active_min_session";

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
                    DateTime t; int score; double pred = 0, since = 0;
                    if (p.Length < 2 || !DateTime.TryParseExact(p[0], Fmt, inv, DateTimeStyles.None, out t)) continue;
                    if (!int.TryParse(p[1], out score) || score < 1 || score > 10) score = 0;
                    if (p.Length > 2) double.TryParse(p[2], NumberStyles.Float, inv, out pred);
                    if (p.Length > 3) double.TryParse(p[3], NumberStyles.Float, inv, out since);
                    list.Add(new Entry { At = t, Score = score, Predicted = pred, SinceWake = since });
                }
            }
            catch (Exception ex) { Store.Log(ex); }
            return list;
        }

        /// True when it is time to ask again: often in the first two weeks, then a few times a day.
        public static bool Due(DateTime now, EnergyDay e)
        {
            if (e == null || now < e.Wake.AddHours(FirstAfterWake) || now > e.Bed) return false;
            var all = Read();
            var today = all.Where(x => SleepLog.Today(x.At) == SleepLog.Today(now)).ToList();
            bool intensive = all.Count == 0 || (now - all.Min(x => x.At)).TotalDays < IntensiveDays;
            var latest = today.Count > 0 ? today.Max(x => x.At) : DateTime.MinValue;
            return today.Count < (intensive ? IntensiveMax : NormalMax) && (now - latest).TotalHours >= (intensive ? IntensiveEvery : NormalEvery);
        }

        public static void Log(DateTime now, string answer, EnergyDay e, double activeToday = 0, double activeSession = 0)
        {
            try
            {
                var inv = CultureInfo.InvariantCulture;
                string line = now.ToString(Fmt, inv) + "," + answer + ","
                    + (e != null ? e.RawAt(now).ToString("0.000", inv) : "") + ","
                    + (e != null ? (now - e.Wake).TotalHours.ToString("0.00", inv) : "") + ","
                    + Math.Round(activeToday).ToString(inv) + "," + Math.Round(activeSession).ToString(inv);
                IO.File.AppendAllText(File, (IO.File.Exists(File) ? "" : Head + "\r\n") + line + "\r\n");
            }
            catch (Exception ex) { Store.Log(ex); }
        }
    }

    // ---------------------------------------------------------------- tuning the curve from the check-ins

    /// Learns where the model is regularly too high or too low for you, by time since waking,
    /// and returns a small correction. The curve takes it at each peak and at the dip only, and eases between them,
    /// so it stays smooth (EnergyModel.Reshape in Energy.cs). Nothing is changed until there are enough check-ins.
    public static class Tuning
    {
        public const int MinRatings = 12;      // check-ins needed before any correction is applied
        public const int WindowDays = 60;      // only check-ins from the last this-many days are used
        public const double Sigma = 1.25;      // hours; how far one check-in's influence spreads along the day (a peak or the dip is a few hours wide)
        public const double Shrink = 1.5;      // caution: a spot needs about this many nearby check-ins before it moves half way
        public const double MinCorrelation = 0.3; // below this your scores do not track the model enough to fit a slope
        public const double MaxShift = 0.15;   // the most the curve may be moved at any point (the curve spans roughly 0.3)
        public const double MinSpread = 0.05;  // if all your scores are nearly the same there is nothing to learn

        public static int Count;               // check-ins available for tuning

        public static Func<double, double> Build(IList<Feedback.Entry> all, DateTime now)
        {
            var r = all.Where(x => x.Score > 0 && x.Predicted > 0 && (now - x.At).TotalDays <= WindowDays).ToList();
            Count = r.Count;
            if (r.Count < MinRatings) return null;

            // Scores (1-10) and the model (0-1) are on different scales: line them up by their averages and spreads.
            var s = r.Select(x => (x.Score - 1) / 9.0).ToArray();
            var p = r.Select(x => x.Predicted).ToArray();
            double ms = s.Average(), mp = p.Average();
            double sds = Math.Sqrt(s.Sum(v => (v - ms) * (v - ms)) / s.Length), sdp = Math.Sqrt(p.Sum(v => (v - mp) * (v - mp)) / p.Length);
            if (sds < MinSpread || sdp < 1e-6) return null;
            // score units per model unit: the fitted slope when your scores track the model at all, else the ratio of spreads
            double cov = 0; for (int i = 0; i < s.Length; i++) cov += (s[i] - ms) * (p[i] - mp);
            cov /= s.Length;
            double corr = cov / (sds * sdp);
            double scale = Math.Max(0.5, Math.Min(4, corr > MinCorrelation ? cov / (sdp * sdp) : sds / sdp));

            var hours = r.Select(x => x.SinceWake).ToArray();
            var miss = new double[r.Count]; // how far the model was off, in the model's own units
            for (int i = 0; i < miss.Length; i++) miss[i] = (s[i] - ms) / scale - (p[i] - mp);

            return t =>
            {
                double w = 0, sum = 0;
                for (int i = 0; i < miss.Length; i++)
                {
                    double k = Math.Exp(-(t - hours[i]) * (t - hours[i]) / (2 * Sigma * Sigma));
                    w += k; sum += k * miss[i];
                }
                return Math.Max(-MaxShift, Math.Min(MaxShift, sum / (w + Shrink)));
            };
        }

        /// The correction in use right now, or null ("Tune" = 0 in settings.json switches it off).
        public static Func<double, double> Active(DateTime now)
        {
            var fit = Build(Feedback.Read(), now);
            return Settings.Get("Tune", 1) > 0.5 ? fit : null;
        }
    }

    partial class Shell
    {
        // ------------------------------------------------------------ time at the PC

        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        /// Seconds since the last key press or mouse movement. This is all Corner looks at: no apps, no window titles.
        static double IdleSeconds()
        {
            var li = new LASTINPUTINFO { cbSize = 8 };
            if (!GetLastInputInfo(ref li)) return 0;
            return unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0;
        }

        DateTime activeSince = DateTime.MinValue, activeDay = DateTime.MinValue;
        double activeToday; // minutes at the PC today
        int checkTicks;

        double ActiveSession(DateTime now) { return activeSince == DateTime.MinValue ? 0 : (now - activeSince).TotalMinutes; }

        /// Runs every 10 seconds: counts time at the PC and brings up the check-in when one is due and you are there.
        void CheckInTick()
        {
            var now = Now(); var day = SleepLog.Today(now);
            double dayKey = day.Year * 10000 + day.Month * 100 + day.Day;
            if (activeDay != day) { activeDay = day; activeToday = Settings.Get("ActiveDay", 0) == dayKey ? Settings.Get("ActiveMin", 0) : 0; }
            bool here = IdleSeconds() < Feedback.IdleMax;
            if (here) { if (activeSince == DateTime.MinValue) activeSince = now; activeToday += 10 / 60.0; }
            else activeSince = DateTime.MinValue;
            checkTicks++;
            if (checkTicks % 30 == 0 && here) { Settings.Set("ActiveDay", dayKey); Settings.Set("ActiveMin", Math.Round(activeToday)); } // kept across restarts

            if (checkTicks % 6 == 0 && here && !paused && !Busy()) EnergyNudges(now); // dip and peak reminders (UiSleep.cs)
            if (checkTicks % 6 != 0 || !here || paused || ActiveSession(now) < Feedback.SettledMin) return;
            if (InTask) return; // a task timer is running: wait until it ends or the task is ticked off (Focus.cs)
            if (shown) { if (full && feedbackBox.Visibility != Visibility.Visible) ShowFeedback(now); return; }
            if (Busy()) return;
            SleepLog.Load();
            if (SleepLog.NeedsCheckIn(now)) return; // the morning sleep check-in comes first
            var e = EnergyModel.Build(SleepLog.Data.Nights, Settings.Get("SleepNeed", EnergyModel.DefaultNeed), day);
            if (!Feedback.Due(now, e)) return;
            promptOnly = true;
            ShowPanel(15000);
        }

        // ------------------------------------------------------------ the 1-10 prompt

        Border feedbackBox, tabsRow;
        Grid headerRow, bodyGrid;
        bool promptOnly;  // the panel is showing nothing but the check-in
        bool rateNow;     // "Rate now" was pressed


        /// When the check-in pops up by itself it is the only thing shown; the hot corner brings back the rest.
        void ApplyPromptOnly()
        {
            if (settingsOpen) { settingsOpen = false; settingsScroll.Visibility = Visibility.Collapsed; leftDock.Visibility = Visibility.Visible; } // every opening starts on the lists
            promptOnly = promptOnly && !full && (feedbackBox.Visibility == Visibility.Visible || alertBox.Visibility == Visibility.Visible);
            var rest = promptOnly ? Visibility.Collapsed : Visibility.Visible;
            headerRow.Visibility = rest; bodyGrid.Visibility = rest;
            tabsRow.Visibility = promptOnly || full ? Visibility.Collapsed : Visibility.Visible;
            if (promptOnly) { tasksAdd.Visibility = Visibility.Collapsed; checkIn.Visibility = Visibility.Collapsed; }
            if (Bare) checkIn.Visibility = Visibility.Collapsed; // at rest the corner panel is the next task alone; Reveal asks again (Focus.cs)
            FocusLayout(); // which of the next-task box, action row, add box and list are showing (Focus.cs)
            LayoutTop();
        }

        void LayoutTop()
        {
            // The trays and prompts above the list, top to bottom. With no header above them (a prompt alone, or the corner
            // panel at rest) the first and the last keep the same distance from the panel's edge as the sides do.
            bool alone = promptOnly || Bare;
            var boxes = new[] { checkIn, feedbackBox, alertBox, focusBox };
            Border first = null, last = null;
            foreach (var b in boxes)
            {
                if (b == null || b.Visibility != Visibility.Visible || (b == focusBox && FullArranged)) continue;
                if (first == null) first = b;
                last = b;
            }
            foreach (var b in boxes)
                if (b != null && !(b == focusBox && FullArranged)) b.Margin = new Thickness(TaskL, alone && b == first ? Side : 0, TaskR, alone && b == last ? Side : GapY);
        }

        // Layout under the header: everything sits in the left column; in Full Mode the energy view moves to a second column.
        DockPanel leftDock;
        Grid mainGrid;

        FrameworkElement BuildMain()
        {
            mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition());
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mainGrid.Children.Add(leftDock);
            settingsView = new StackPanel { Margin = new Thickness(20, 2, 20, 16), MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left };
            settingsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false, Content = settingsView, Visibility = Visibility.Collapsed
            };
            Grid.SetColumnSpan(settingsScroll, 2);
            mainGrid.Children.Add(settingsScroll);
            return mainGrid;
        }

        void MoveEnergy(Grid to, int column)
        {
            var from = energyScroll.Parent as Grid;
            if (from != to)
            {
                if (from != null) from.Children.Remove(energyScroll);
                to.Children.Add(energyScroll);
            }
            Grid.SetColumn(energyScroll, column);
        }


        // ------------------------------------------------------------ Full Mode

        const double FullW = 800; // two equal columns of FullW / 2: tasks and energy
        bool full, quitting;

        void PlaceFull(Rect wa, double z)
        {
            Width = FullW * z + 2 * Pad; Height = wa.Height;
            Left = wa.Left + (wa.Width - Width) / 2; Top = wa.Top;
            if (Settings.Get("FullMoved", 0) > 0.5)
            {   // where it was last dragged to, kept on screen
                Left = Math.Max(wa.Left - Pad, Math.Min(wa.Right - Width + Pad, Settings.Get("FullX", Left)));
                Top = Math.Max(wa.Top - Pad + 8, Math.Min(wa.Bottom - 260, Settings.Get("FullY", Top)));
            }
            card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Top;
            card.Margin = new Thickness(0, Pad, 0, 0);
            card.RenderTransformOrigin = new Point(0.5, 0);
            card.MaxHeight = Math.Max(300, (wa.Bottom - Top - 2 * Pad) / z);
        }

        // ------------------------------------------------------------ moving the panel (both modes)

        bool moving, floating; // floating: the corner panel has been dragged away from its corner
        POINT moveFrom; double moveLeft, moveTop;

        /// Corner mode: uses the dragged position if there is one, and sizes the card to the room below (or above) it.
        void PlaceMoved(Rect wa)
        {
            floating = Settings.Get("Moved", 0) > 0.5;
            if (floating)
            {
                Left = Math.Max(wa.Left - 20, Math.Min(wa.Right - Width + 20, Settings.Get("PosX", Left)));
                double lo = OnTop ? wa.Top : wa.Top - (wa.Height - 240), hi = OnTop ? wa.Top + wa.Height - 240 : wa.Top;
                Top = Math.Max(lo, Math.Min(hi, Settings.Get("PosY", Top)));
            }
            double room = OnTop ? wa.Bottom - Top - Gap : Top + Height - Gap - wa.Top;
            card.MaxHeight = Math.Max(200, (room - 56) / scale);
        }

        // Dragging by the header. Done by hand (not Window.DragMove) so it behaves the same in both modes.
        void BeginMove(MouseButtonEventArgs e)
        {
            POINT p;
            if (!GetCursorPos(out p)) return;
            moving = true; moveFrom = p; moveLeft = Left; moveTop = Top;
            headerRow.CaptureMouse();
            e.Handled = true;
        }

        void ContinueMove()
        {
            POINT p;
            if (!moving || !GetCursorPos(out p)) return;
            var src = PresentationSource.FromVisual(this);
            double k = src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.M11 : 1; // pixels -> WPF units
            Left = moveLeft + (p.X - moveFrom.X) * k;
            Top = moveTop + (p.Y - moveFrom.Y) * k;
        }

        void EndMove()
        {
            if (!moving) return;
            moving = false;
            headerRow.ReleaseMouseCapture();
            if (Math.Abs(Left - moveLeft) + Math.Abs(Top - moveTop) < 3) { Left = moveLeft; Top = moveTop; return; } // a click, not a drag
            Settings.Set(full ? "FullX" : "PosX", Math.Round(Left));
            Settings.Set(full ? "FullY" : "PosY", Math.Round(Top));
            Settings.Set(full ? "FullMoved" : "Moved", 1);
            Place(); // keeps it on screen and refits the height
        }

        void ResetPosition()
        {
            Settings.Set("Moved", 0); Settings.Set("FullMoved", 0);
            if (shown) Place();
        }

        // ------------------------------------------------------------ look: font and per-mode text size

        // One font: Gilroy, loaded from the .ttf files beside Corner.exe (or in a "fonts" folder there). Medium is the
        // ordinary weight and SemiBold the emphasis. If the files are missing Corner falls back to Segoe UI.
        double fullScale = 1; // Full Mode has its own text size
        double CurrentScale { get { return full ? fullScale : scale; } }

        static FontFamily FindGilroy(bool italic)
        {
            string exe = IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            foreach (var dir in new[] { exe, IO.Path.Combine(exe, "fonts") })
            {
                try
                {
                    if (!IO.Directory.Exists(dir)) continue;
                    foreach (var f in System.Windows.Media.Fonts.GetFontFamilies(dir + "\\"))
                        if (f.Source.IndexOf("#Gilroy", StringComparison.OrdinalIgnoreCase) >= 0 && (italic
                            ? f.GetTypefaces().Any(t => t.Style == FontStyles.Italic && !t.IsObliqueSimulated)
                            : f.GetTypefaces().Any(t => t.Weight == FontWeights.SemiBold && t.Style == FontStyles.Normal))) return f;
                }
                catch (Exception ex) { Store.Log(ex); }
            }
            return null;
        }

        void LoadLook()
        {
            fullScale = Math.Max(0.7, Math.Min(1.6, Settings.Get("FullScale", 1)));
            T.Use((int)Settings.Get("Skin", 1)); // Glass unless another skin was chosen (Glass.cs)
            var gilroy = FindGilroy(false); UiItalic = FindGilroy(true);
            FontFamily = UiFont = gilroy ?? new FontFamily("Segoe UI");
            FontK = gilroy != null ? GilroyFit : 1; HasSemi = true;
        }

        // ------------------------------------------------------------ Settings, inside the panel

        bool settingsOpen;

        FrameworkElement SectionHead(string text, string count, bool open, Action toggle)
        {
            var head = new Grid { Height = Sp(24, 22), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var hs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var chev = Glyph("M2.5,1 L5.5,4 L2.5,7", T.Muted, 1.4, 8, 8);
            chev.RenderTransformOrigin = new Point(0.5, 0.5); chev.RenderTransform = new RotateTransform(open ? 90 : 0);
            hs.Children.Add(chev);
            hs.Children.Add(new TextBlock { Text = text.ToUpperInvariant(), FontSize = Z(10), FontWeight = SemiSmall, Foreground = T.Muted, Margin = new Thickness(9, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            if (count != null) hs.Children.Add(new TextBlock { Text = count, FontSize = Z(Sp(10.5, 10)), Foreground = T.Faint, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(hs);
            head.MouseLeftButtonDown += (s, e) => { e.Handled = true; toggle(); };
            return head;
        }


        // The settings page: opened by the gear in the header, it takes the place of the lists until closed.
        ScrollViewer settingsScroll;
        StackPanel settingsView;

        void ToggleSettings()
        {
            settingsOpen = !settingsOpen;
            if (settingsOpen)
            {
                promptOnly = false; ApplyPromptOnly(); // also clears settingsOpen, so set it again
                settingsOpen = true;
                leftDock.Visibility = Visibility.Collapsed; energyScroll.Visibility = Visibility.Collapsed; tabsRow.Visibility = Visibility.Collapsed;
                settingsScroll.Visibility = Visibility.Visible;
                DrawSettings();
            }
            else CloseSettings();
        }

        void CloseSettings()
        {
            settingsOpen = false;
            settingsScroll.Visibility = Visibility.Collapsed;
            leftDock.Visibility = Visibility.Visible;
            SetTab(tab);
        }



        /// A normal window: wider, tasks and energy side by side, on the taskbar, and it stays until closed.
        void EnterFull()
        {
            if (full) { Activate(); return; }
            if (enteringFull) return;
            GrabMini(); genieBack = false; // the corner panel's picture, for the warp (Launch.cs)
            if (!HoldMini()) { SwitchToFull(); return; }
            // The picture is on top now. Empty the panel under it and let that be drawn before the window is hidden:
            // a window shows its last frame for an instant when it comes back, and that frame must be empty.
            enteringFull = true;
            After(40, () =>
            {
                card.BeginAnimation(OpacityProperty, null); card.Opacity = 0;
                After(30, () => { enteringFull = false; if (!full) SwitchToFull(); else DropHold(); });
            });
        }

        bool enteringFull;

        void SwitchToFull()
        {
            shown = false; gen++; Hide();
            full = true; paused = false; promptOnly = false; tray.Text = "Corner";
            var h = new WindowInteropHelper(this).EnsureHandle();
            SetWindowLong(h, -20, (GetWindowLong(h, -20) & ~0x80) | 0x40000); // an app window while in Full Mode
            ShowInTaskbar = true; Topmost = false;
            ApplyFullIcon(); // the hot-corner picture, on the taskbar (HotIcon.cs)
            switchWarp = true; ShowPanel(0); switchWarp = false;
            Activate();
            WarpIntoFull(); // out of the corner panel (Launch.cs)
        }

        void ExitFull()
        {
            if (!full) return;
            var warpBack = WarpOutOfFull(); // back down into its corner (Launch.cs)
            full = false; shown = false; int g = ++gen;
            card.BeginAnimation(OpacityProperty, null);
            if (warpBack != null)
            {   // the warp on top first; then the window empties under it and goes (see EnterFull)
                warpBack();
                After(40, () => { if (gen != g) return; card.Opacity = 0; After(30, () => { if (gen == g) LeaveFull(); }); });
                return;
            }
            card.Opacity = 0;
            LeaveFull();
        }

        void LeaveFull()
        {
            Hide();
            var h = new WindowInteropHelper(this).EnsureHandle();
            SetWindowLong(h, -20, (GetWindowLong(h, -20) & ~0x40000) | 0x80); // back to a tool window: off the taskbar and Alt+Tab
            ShowInTaskbar = false; Topmost = true;
            card.BeginAnimation(OpacityProperty, null); card.Opacity = 0;
        }
    }
}
