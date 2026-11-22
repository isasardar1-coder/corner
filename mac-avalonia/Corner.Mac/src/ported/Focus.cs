// Ported from corner/src/Focus.cs (WPF) to Avalonia: tools/port_ui.py, then fixed by hand.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Corner
{
    partial class Shell
    {
        Border focusBox, alertBox;
        Border minBtn, gearBox, fullBox; // header buttons: back to the corner (Full Mode), settings, Full Mode
        Grid actionRow, ringBox;
        bool allOpen;  // the full task list is expanded (corner panel)
        bool addOpen;  // the add-a-task box is showing (corner panel)

        // ------------------------------------------------------------ building

        /// Called once, after the check-in boxes: adds the prompt box, the next-task tray and the row of chips above the list.
        /// Their colours, corners and padding are set in Restyle (Ui.cs).
        void BuildFocus()
        {
            alertBox = new RimBorder { IsVisible = false };
            DockPanel.SetDock(alertBox, Dock.Top); leftDock.Children.Add(alertBox);

            focusBox = new RimBorder();
            DockPanel.SetDock(focusBox, Dock.Top); leftDock.Children.Add(focusBox);

            actionRow = new Grid { Height = 30 };
            DockPanel.SetDock(actionRow, Dock.Top); leftDock.Children.Add(actionRow);

            // the Tasks / Energy tabs are replaced by the chips; the old row stays in the tree but takes no room
            tabsRow.MaxHeight = 0; tabsRow.Margin = new Thickness(0); tabsRow.ClipToBounds = true; // WPF clips what is cut short; Avalonia only when asked
            allOpen = Settings.Get("AllOpen", 0) > 0.5;

            var ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            ticker.Tick += (s, e) => { CountTimer(); TimerTick(); }; // CountTimer: minutes on each task, for done.csv (Stats.cs)
            ticker.Start();
        }

        /// Decides what is visible below the header, for the corner panel and for Full Mode.
        void FocusLayout()
        {
            if (focusBox == null) return;
            minBtn.IsVisible = full ? true : false;
            if (fullBox != null) fullBox.IsVisible = full ? false : true;

            var on = true; var off = false;
            if (promptOnly) { focusBox.IsVisible = off; actionRow.IsVisible = off; tasksAdd.IsVisible = off; LayoutTop(); return; }
            if (full)
            {   // Full Mode: next task on top, then the add box and the whole list; energy is the other column
                focusBox.IsVisible = on; actionRow.IsVisible = off; tasksAdd.IsVisible = on; tasksScroll.IsVisible = on;
                tasksAdd.Margin = new Thickness(Side, 0, SideMid, GapY);
                LayoutTop();
                return;
            }
            // the corner panel. At rest it is the next task alone: no header, no chips, no list (Reveal brings them in)
            bool tasksView = tab == 0, rest = Bare;
            bool adding = tasksView && addOpen && !rest, listing = tasksView && allOpen && !rest;
            headerRow.IsVisible = rest ? off : on;
            focusBox.IsVisible = tasksView ? on : off;
            actionRow.IsVisible = rest ? off : on;
            tasksAdd.IsVisible = adding ? on : off;
            tasksScroll.IsVisible = listing ? on : off;
            // whatever is last in the panel keeps the edge's distance below it
            bool more = !tasksView || allOpen || addOpen;
            actionRow.Margin = new Thickness(Side, 0, Side, more ? GapY : Side);
            tasksAdd.Margin = new Thickness(Side, 0, SideMid, allOpen ? GapY : Side);
            tasksScroll.Margin = new Thickness(0, 0, 0, listing ? Side : 0); // (collapsed, it must not hold the edge's distance twice)
            LayoutTop(); // the margins of the trays and prompts (Checks.cs)
            DrawActions();
        }

        // ------------------------------------------------------------ the corner panel at rest

        bool bare;    // the corner panel opened on the next task alone, and the pointer has not been on it yet
        bool demoRun; // a test render: the real pointer is ignored
        bool Bare { get { return bare && !full && !promptOnly && !settingsOpen; } }

        /// The corner panel opens on the next task alone (with a time's-up prompt, if one is waiting). When the pointer
        /// comes onto the panel, the header, the check-ins, the chips and the open list come in, and stay until it closes.
        void Reveal()
        {
            if (!bare) return;
            bool was = Bare;
            bare = false;
            if (!was || !shown) return;
            var now = Now();
            if (SleepLog.NeedsCheckIn(now)) OpenCheckIn(false);
            ShowFeedback(now); // a 1-10 check-in that was due waits for this moment (Ui.cs)
            FocusLayout();
            card.UpdateLayout();
            foreach (var el in new Control[] { headerRow, checkIn, feedbackBox, actionRow, tasksAdd, tasksScroll }) SlideIn(el);
        }

        /// A part that has just been made visible grows in from nothing, so the panel opens out instead of jumping.
        void SlideIn(Control el)
        {
            if (el == null || el.IsVisible != true || el.Bounds.Height <= 0) return;
            var m = el.Margin; var t = TimeSpan.FromMilliseconds(200);
            var grow = new DoubleAnimation(0, el.Bounds.Height, t) { EasingFunction = Out };
            grow.Completed += (s, e) => { el.BeginAnimation(MaxHeightProperty, null); el.BeginAnimation(MarginProperty, null); }; // back to their own values
            el.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(m.Left, 0, m.Right, 0), m, t) { EasingFunction = Out });
            el.BeginAnimation(MaxHeightProperty, grow);
            Anim(el, OpacityProperty, 1, 220, from: 0);
        }

        // ------------------------------------------------------------ what comes next

        static int DurationMin(TaskItem t) { return t.Minutes > 0 ? t.Minutes : Planner.DefaultMinutes; }

        /// Open tasks in the order to do them: the one being timed, then today's timed tasks by time
        /// (so an applied plan leads), then the rest in list order, then anything dated for a later day.
        List<TaskItem> Upcoming()
        {
            var tomorrow = DateTime.Today.AddDays(1);
            Func<TaskItem, int> group = t =>
            {
                var d = t.DueAt;
                if (t.Id == timerId) return 0;
                if (t.HasTime && d != null && d.Value < tomorrow) return 1;
                if (d != null && d.Value.Date > DateTime.Today) return 3;
                return 2;
            };
            return data.Tasks.Where(t => !t.Done).Select((t, i) => new { t, i, g = group(t) })
                .OrderBy(x => x.g).ThenBy(x => x.g == 1 ? x.t.DueAt.Value.Ticks : 0).ThenBy(x => x.i).Select(x => x.t).ToList();
        }

        bool IsFocus(TaskItem t) { return Upcoming().FirstOrDefault() == t; }

        /// Effort, length and date in words: what used to sit on the row, now shown on hover.
        static string InfoText(TaskItem t)
        {
            var p = new List<string>();
            string level = TaskTags.Level(t.Energy);
            if (level != null) p.Add(char.ToUpperInvariant(level[0]) + level.Substring(1) + " effort");
            if (t.Kind == "physical") p.Add("physical");
            p.Add(DurationMin(t) + " min");
            var d = t.DueAt;
            if (d != null) p.Add((t.Overdue ? "overdue · " : "") + When.Label(d.Value, t.HasTime));
            if (t.Routine != null) p.Add("routine");
            return string.Join(" · ", p);
        }

        static ToolTip Tip(string text)
        {
            return new ToolTip
            {
                Content = new TextBlock { Text = text, FontSize = Z(11.5), Foreground = T.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 },
                Background = T.Pop, BorderBrush = T.Edge, BorderThickness = new Thickness(1), Padding = new Thickness(9, 5, 9, 6)
            };
        }

        readonly List<Action> focusTimers = new List<Action>(), rowTimers = new List<Action>();

        /// The tray at the top, three lines: what it is ("Next", "Now", "Paused") and when it is due; the task, large,
        /// beside its circle; then its timer as a dark pill and, on a white chip, the task after it.
        void DrawFocus()
        {
            if (focusBox == null || data == null) return;
            focusTimers.Clear();
            var up = Upcoming();
            var sp = new StackPanel();
            if (up.Count == 0)
            {
                sp.Children.Add(new TextBlock { Text = data.Tasks.Count > 0 ? "All done. Enjoy it." : "Nothing here yet.", FontSize = Z(13), Foreground = T.Faint });
                focusBox.Child = sp;
                if (!promptOnly && !full) DrawActions();
                return;
            }

            var t = up[0];
            bool timed = timerId == t.Id, late = t.Overdue && !timed;
            var due = t.DueAt;
            string state = timed ? (timerRunning ? "Now" : "Paused") : "Next";
            string when = t.HasTime && due != null ? (late ? "overdue " : "") + EnergyDay.Clock(due.Value) : null;

            // line 1: the state, then when it is due; while it is being timed, "Reset" at the far end
            var top = new Grid { Margin = new Thickness(0, 0, 0, Sp(9, 7)) };
            var says = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var cap = Caps(state); cap.VerticalAlignment = VerticalAlignment.Center;
            if (timed && timerRunning) cap.Foreground = T.Text;
            says.Children.Add(cap);
            if (when != null)
            {
                var w = new TextBlock { Text = when, FontSize = Z(11), Foreground = late ? (IBrush)T.Danger : T.Muted, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                if (late) Lean(w);
                says.Children.Add(w);
            }
            top.Children.Add(says);
            if (timed)
            {   // a timer that has been started can be put back to the task's full length
                var reset = new TextBlock { Text = "Reset", FontSize = Z(11), Foreground = T.Muted, Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(10, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, [ToolTip.TipProperty] = "Put the timer back to " + DurationMin(t) + " min" };
                reset.PointerEntered += (s, e) => reset.Foreground = T.Text;
                reset.PointerExited += (s, e) => reset.Foreground = T.Muted;
                reset.OnLeftDown((s, e) => { e.Handled = true; StopTimer(); RepaintTimers(); DrawFocus(); });
                top.Children.Add(reset);
            }
            sp.Children.Add(top);

            // line 2: the circle and the task; the circle is centred on the title's first line
            double ck = Sp(22, 20), lead = ck + Sp(11, 9);
            double titleSize = Hd(Sp(22, 18)), first = Math.Ceiling(Math.Max(titleSize * UiFont.LineSpacing(), ck));
            var main = new Grid { Background = Brushes.Transparent };
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(lead) });
            main.ColumnDefinitions.Add(new ColumnDefinition());
            var box = new Grid { Height = first, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Cursor = Cursors.Hand, [ToolTip.TipProperty] = "Mark as done" };
            var ring = new Ellipse { Width = ck, Height = ck, Stroke = T.Text, StrokeThickness = 1.5, Fill = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            box.Children.Add(ring);
            box.PointerEntered += (s, e) => ring.Fill = T.Soft;
            box.PointerExited += (s, e) => ring.Fill = Brushes.Transparent;
            box.OnLeftDown((s, e) => { e.Handled = true; CompleteTask(t); });
            main.Children.Add(box);
            var title = new TextBlock { Text = t.Title, FontSize = titleSize, FontWeight = FontWeight.SemiBold, Foreground = T.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top, [ToolTip.TipProperty] = Tip(InfoText(t)) };
            Grid.SetColumn(title, 1); main.Children.Add(title);
            sp.Children.Add(main);

            // line 3: the timer, and what comes after
            var row = new Grid { Margin = new Thickness(0, Sp(11, 9), 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(TimerPill(t, true, focusTimers));
            if (up.Count > 1)
            {
                var n = up[1]; var nd = n.DueAt;
                var then = new TextBlock
                {
                    Text = "then " + n.Title + (n.HasTime && nd != null && nd.Value.Date <= DateTime.Today ? ", " + EnergyDay.Clock(nd.Value) : ""),
                    FontSize = Z(11.5), Foreground = T.Muted, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
                };
                var chip = new PillBorder { Height = Sp(34, 30), Background = T.Field, Padding = new Thickness(Sp(14, 12), 0, Sp(14, 12), 1), Margin = new Thickness(6, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = then, [ToolTip.TipProperty] = Tip("Then: " + n.Title + "\n" + InfoText(n)) };
                Grid.SetColumn(chip, 1); row.Children.Add(chip);
            }
            sp.Children.Add(row);
            focusBox.Child = sp;
            if (!promptOnly && !full) DrawActions();
        }

        /// Under the next task, a row of chips: "All tasks" (opens the list), add a task, and the energy view.
        /// A chip is forest green while what it opens is showing.
        void DrawActions()
        {
            if (actionRow == null) return;
            actionRow.Children.Clear(); actionRow.ColumnDefinitions.Clear();
            actionRow.ColumnDefinitions.Add(new ColumnDefinition());
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int open = data == null ? 0 : data.Tasks.Count(t => !t.Done);

            bool listOn = tab == 0 && allOpen;
            IBrush ink = listOn ? (IBrush)T.OnStrong : T.Text;
            var words = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var lead = Glyph(tab == 1 ? "M8.5,3.5 L5,7 L8.5,10.5" : "M2.5,4 H11.5 M2.5,7 H11.5 M2.5,10 H11.5", ink, 1.5, 14, 14);
            words.Children.Add(lead);
            words.Children.Add(new TextBlock { Text = tab == 1 ? "Tasks" : "All tasks", FontSize = Z(12), Foreground = ink, Margin = new Thickness(7, 0, 6, 1), VerticalAlignment = VerticalAlignment.Center });
            if (tab == 0) words.Children.Add(new TextBlock { Text = open.ToString(), FontSize = Z(11), Foreground = listOn ? (IBrush)T.OnStrongSoft : T.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) });
            var inside = new Grid { Margin = new Thickness(11, 0, 11, 0) };
            inside.Children.Add(words);
            if (tab == 0)
            {   // an arrow at the far end: across when the list is closed, down when it is open
                var arrow = Glyph(listOn ? "M3.5,5.5 L7,9 L10.5,5.5" : "M5.5,3.5 L9,7 L5.5,10.5", ink, 1.5, 14, 14);
                arrow.HorizontalAlignment = HorizontalAlignment.Right;
                inside.Children.Add(arrow);
            }
            actionRow.Children.Add(Chip(inside, listOn, null, () =>
            {
                if (tab == 1) { SetTab(0); return; }
                allOpen = !allOpen; Settings.Set("AllOpen", allOpen ? 1 : 0);
                FocusLayout();
            }));

            var add = RoundChip("M7,2.5 V11.5 M2.5,7 H11.5", tab == 0 && addOpen, "Add a task", () =>
            {
                if (tab != 0) { addOpen = true; SetTab(0); } else { addOpen = !addOpen; FocusLayout(); }
                if (addOpen) { Activate(); input.Focus(); }
            });
            Grid.SetColumn(add, 1); actionRow.Children.Add(add);
            var en = RoundChip("M1.5,9 C3.5,1 5.5,1 7,7 S10.5,12 12.5,4", tab == 1, "Energy", () => SetTab(tab == 1 ? 0 : 1));
            Grid.SetColumn(en, 2); actionRow.Children.Add(en);
        }

        /// A round chip holding one icon.
        static Control RoundChip(string path, bool on, string tip, Action click)
        {
            var b = Chip(Glyph(path, on ? (IBrush)T.OnStrong : T.Text, 1.5, 14, 14), on, tip, click);
            b.Width = 30; b.Margin = new Thickness(6, 0, 0, 0);
            return b;
        }

        /// What sits at the right of a task row: (in Full Mode only) effort and date, then its timer.
        /// In the corner panel effort, length and date are on the row's tooltip.
        /// The row being timed also gets a pale-lime ground, which follows the timer like the pill does.
        Control RowSide(TaskItem t, StackPanel chip, Grid main)
        {
            var ground = new Border { CornerRadius = new CornerRadius(Sp(12, 10)), Background = T.Soft, IsHitTestVisible = false };
            Grid.SetColumnSpan(ground, 5); main.Children.Insert(0, ground);
            Action paintGround = () => ground.IsVisible = t.Id == timerId ? true : false;
            paintGround(); rowTimers.Add(paintGround);

            var side = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Height = Sp(34, 26) };
            chip.IsVisible = full ? true : false;
            chip.Margin = new Thickness(0, 0, 4, 0);
            side.Children.Add(chip);
            var pill = TimerPill(t, false, rowTimers);
            pill.VerticalAlignment = VerticalAlignment.Center;
            side.Children.Add(pill);
            if (!full) { main[ToolTip.TipProperty] = Tip(t.Title + "\n" + InfoText(t)); ToolTip.SetShowDelay(main, 350); }
            Grid.SetColumn(side, 3);
            return side;
        }

        // ------------------------------------------------------------ timers

        string timerId;     // the task being timed (one at a time)
        DateTime timerEnd;  // when it runs out, while running
        double timerLeft;   // seconds left, while paused
        bool timerRunning;

        /// A task timer is counting down: the 1-10 check-in waits until it ends or the task is ticked off.
        bool InTask { get { return timerId != null && timerRunning; } }
        void HoldFeedback() { if (!rateNow && feedbackBox != null) feedbackBox.IsVisible = false; }

        double TimerSeconds() { return timerRunning ? (timerEnd - DateTime.Now).TotalSeconds : timerLeft; }

        string TimerText(TaskItem t)
        {
            int s = t.Id == timerId ? (int)Math.Ceiling(Math.Max(0, TimerSeconds())) : DurationMin(t) * 60;
            return string.Format("{0}:{1:00}", s / 60, s % 60); // always minutes and seconds ("90:00", "120:00"), so every timer has the same shape
        }

        /// The countdown shown beside a task. Click to start; click again to pause or carry on.
        /// The next task's timer is a pill: forest green, and lime while it runs. A row's timer is quiet text, at least as
        /// wide as "00:00" so the timers of a list form one straight column; the row being timed gets a lime pill.
        Control TimerPill(TaskItem t, bool big, List<Action> repaint)
        {
            double size = Z(big ? 13 : 11);
            var icon = Glyph("", T.Muted, big ? 1.6 : 1.3, 9, 10);
            var label = new TextBlock { FontFamily = UiFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(big ? 7 : 5, 0, 0, 1), TextAlignment = TextAlignment.Right, MinWidth = TextW("00:00", size, FontWeight.SemiBold) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(icon); sp.Children.Add(label);
            var b = new PillBorder { Child = sp, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top };
            if (big) { b.Height = Sp(34, 30); b.Padding = new Thickness(Sp(13, 11), 0, Sp(16, 14), 0); }
            else b.Padding = new Thickness(7, 2, 8, 3);
            bool hot = false;
            Action paint = () =>
            {
                bool mine = t.Id == timerId, running = mine && timerRunning;
                IBrush ground = big ? (running ? (IBrush)T.Accent : T.Strong) : mine ? (IBrush)T.Accent : Brushes.Transparent;
                IBrush ink = big ? (running ? (IBrush)T.OnAccent : T.OnStrong) : mine ? (IBrush)T.OnAccent : hot ? T.Text : T.Muted;
                label.Text = TimerText(t);
                b.Background = ground; label.Foreground = ink;
                label.FontWeight = big || mine ? SemiSmall : FontWeight.Normal;
                icon.Data = Geometry.Parse(running ? "M2.5,1.5 V8.5 M6.5,1.5 V8.5" : "M2,1.5 L7.5,5 L2,8.5 Z"); // pause bars, or a play triangle
                icon.Stroke = ink; icon.Fill = big && !running ? ink : Brushes.Transparent; // the next task's play triangle is solid
                b.Opacity = big && hot ? 0.9 : 1;
                b[ToolTip.TipProperty] = running ? "Pause the timer" : mine ? "Carry on" : "Start a " + DurationMin(t) + "-minute timer";
            };
            paint(); repaint.Add(paint);
            b.PointerEntered += (s, e) => { hot = true; paint(); };
            b.PointerExited += (s, e) => { hot = false; paint(); };
            b.OnLeftDown((s, e) => { e.Handled = true; TimerClick(t); });
            return b;
        }

        void RepaintTimers()
        {
            foreach (var p in focusTimers.ToList()) p();
            foreach (var p in rowTimers.ToList()) p();
        }

        void StartTimer(TaskItem t)
        {
            timerId = t.Id; timerEnd = DateTime.Now.AddMinutes(DurationMin(t)); timerRunning = true; timerLeft = 0;
            HoldFeedback();
        }

        void StopTimer() { timerId = null; timerRunning = false; timerLeft = 0; }

        void TimerClick(TaskItem t)
        {
            if (timerId != t.Id) StartTimer(t);
            else if (timerRunning) { timerLeft = Math.Max(0, TimerSeconds()); timerRunning = false; }
            else if (timerLeft > 0) { timerEnd = DateTime.Now.AddSeconds(timerLeft); timerRunning = true; HoldFeedback(); }
            else StartTimer(t);
            if (alertKind == 1) ClearAlert();
            RepaintTimers();
            DrawFocus(); // the timed task moves to the top
        }

        void TimerTick()
        {
            if (timerId == null) return;
            if (timerRunning && DateTime.Now >= timerEnd) { TimeUp(); return; }
            if (shown && timerRunning) RepaintTimers();
        }

        // ------------------------------------------------------------ prompts: time's up, and what next

        int alertKind;       // 0 none, 1 time's up, 2 ready for the next task?
        string alertTaskId;

        void TimeUp()
        {
            timerRunning = false; timerLeft = 0;
            var t = data.Tasks.FirstOrDefault(x => x.Id == timerId && !x.Done);
            if (t == null) { StopTimer(); return; }
            alertKind = 1; alertTaskId = t.Id;
            Os.Notify("Time's up", t.Title);
            if (shown) ShowFeedback(Now()); // a check-in that waited for the timer is asked now (a closed panel asks as it opens)
            ShowAlert();
        }

        /// Draws the prompt and makes sure it is seen: alone in the corner if the panel was closed, on top of it otherwise.
        /// It is the one dark box in the panel: forest green, with the answer that moves things on in lime.
        void ShowAlert()
        {
            var t = data.Tasks.FirstOrDefault(x => x.Id == alertTaskId && !x.Done);
            if (alertKind == 0 || t == null) { ClearAlert(); return; }

            var sp = new StackPanel();
            var cap = Caps(alertKind == 1 ? "Time's up" : "Ready for the next one?"); cap.Foreground = T.Accent; cap.Margin = new Thickness(0, 0, 0, 4);
            sp.Children.Add(cap);
            sp.Children.Add(new TextBlock { Text = t.Title, FontSize = Hd(15), FontWeight = FontWeight.SemiBold, Foreground = T.OnStrong, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 4, Sp(11, 9)) });
            var row = new WrapPanel();
            if (alertKind == 1)
            {
                // short names and close spacing in the corner panel, so all five fit on one line
                var five = new[]
                {
                    Btn("Done", 0, () => { alertKind = 0; CompleteTask(t); }, 2),
                    Btn(Slim ? "+5m" : "+5 min", 1, () => Extend(t, 5), 2),
                    Btn(Slim ? "+15m" : "+15 min", 1, () => Extend(t, 15), 2),
                    Btn("Restart", 2, () => { StartTimer(t); ClearAlert(); }, 2),
                    Btn("Pause", 2, () => { StopTimer(); ClearAlert(); }, 2)
                };
                for (int i = 0; i < five.Length; i++)
                {
                    if (Slim) { ((Border)five[i]).Padding = new Thickness(i > 2 ? 4 : 9, 5, i > 2 ? 4 : 9, 6); five[i].Margin = new Thickness(0, 0, i > 1 ? 1 : 4, 5); }
                    row.Children.Add(five[i]);
                }
            }
            else
            {
                row.Children.Add(Btn("Start " + DurationMin(t) + " min", 0, () => { StartTimer(t); ClearAlert(); }, 2));
                row.Children.Add(Btn("Not yet", 2, ClearAlert, 2));
            }
            sp.Children.Add(row);
            alertBox.Child = sp;
            alertBox.IsVisible = true;

            if (full) { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; FocusLayout(); RepaintTimers(); DrawFocus(); return; }
            if (!shown) { promptOnly = true; ShowPanel(0); }
            else { FocusLayout(); RepaintTimers(); DrawFocus(); }
        }

        void Extend(TaskItem t, int minutes)
        {
            timerId = t.Id; timerEnd = DateTime.Now.AddMinutes(minutes); timerRunning = true;
            HoldFeedback();
            ClearAlert();
        }

        void ClearAlert()
        {
            alertKind = 0; alertTaskId = null;
            alertBox.IsVisible = false;
            if (promptOnly && shown && !full && feedbackBox.IsVisible != true) { HidePanel(); return; }
            FocusLayout(); RepaintTimers(); DrawFocus();
        }

        /// A prompt keeps the panel open until it is answered.
        bool AlertHolds() { return alertKind != 0 && alertBox.IsVisible == true; }

        /// Marks a task done from the next-task box or a prompt.
        void CompleteTask(TaskItem t)
        {
            bool wasNext = IsFocus(t);
            t.Done = true; t.DoneAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            LogDone(t); // a row in done.csv (Stats.cs)
            if (whenOpenId == t.Id) whenOpenId = null;
            TaskCompleted(t, wasNext);
            Save(); Rebuild();
        }

        /// After any task is ticked off: stop its timer, and if it was the current one, offer the next.
        void TaskCompleted(TaskItem t, bool wasNext)
        {
            if (timerId == t.Id) { StopTimer(); wasNext = true; if (shown) ShowFeedback(Now()); } // the timed task is done: a waiting check-in is asked now
            if (alertTaskId == t.Id) alertKind = 0;
            if (!wasNext) { if (alertKind == 0) alertBox.IsVisible = false; return; }
            var next = Upcoming().FirstOrDefault();
            if (next == null) { ClearAlert(); return; }
            alertKind = 2; alertTaskId = next.Id;
            ShowAlert();
        }
    }
}
