// Ported from corner/src/Checks.cs (WPF) to Avalonia: tools/port_ui.py, then fixed by hand.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IO = System.IO;

namespace Corner
{
    partial class Shell
    {
        // ------------------------------------------------------------ time at the PC

        static double IdleSeconds() { return Os.IdleSeconds(); }

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
            if (shown) { if (full && feedbackBox.IsVisible != true) ShowFeedback(now); return; }
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
            if (settingsOpen) { settingsOpen = false; settingsScroll.IsVisible = false; leftDock.IsVisible = true; } // every opening starts on the lists
            promptOnly = promptOnly && !full && (feedbackBox.IsVisible == true || alertBox.IsVisible == true);
            var rest = promptOnly ? false : true;
            headerRow.IsVisible = rest; bodyGrid.IsVisible = rest;
            tabsRow.IsVisible = promptOnly || full ? false : true;
            if (promptOnly) { tasksAdd.IsVisible = false; checkIn.IsVisible = false; }
            if (Bare) checkIn.IsVisible = false; // at rest the corner panel is the next task alone; Reveal asks again (Focus.cs)
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
                if (b == null || b.IsVisible != true) continue;
                if (first == null) first = b;
                last = b;
            }
            foreach (var b in boxes)
                if (b != null) b.Margin = new Thickness(Side, alone && b == first ? Side : 0, SideMid, alone && b == last ? Side : GapY);
        }

        // Layout under the header: everything sits in the left column; in Full Mode the energy view moves to a second column.
        DockPanel leftDock;
        Grid mainGrid;

        Control BuildMain()
        {
            mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition());
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mainGrid.Children.Add(leftDock);
            settingsView = new StackPanel { Margin = new Thickness(20, 2, 20, 16), MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left };
            settingsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false, Content = settingsView, IsVisible = false
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

        const double FullW = 800, FullMinH = 540; // two equal columns of FullW / 2: tasks and energy
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
            card.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
            card.MaxHeight = Math.Max(300, (wa.Bottom - Top - 2 * Pad) / z);
            SyncHost();
        }

        // ------------------------------------------------------------ moving the panel (both modes)

        bool moving, floating; // floating: the corner panel has been dragged away from its corner
        Point moveFrom; double moveLeft, moveTop;

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
        void BeginMove(PointerPressedEventArgs e)
        {
            var p = Os.Cursor();
            moving = true; moveFrom = p; moveLeft = Left; moveTop = Top;
            headerRow.CaptureMouse();
            e.Handled = true;
        }

        void ContinueMove()
        {
            if (!moving) return;
            var p = Os.Cursor(); const double k = 1; // already in device-independent units
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

        /// Gilroy is built into the app (Assets). Null if it cannot be loaded, and the system font is used.
        static FontFamily FindGilroy(bool italic)
        {
            try
            {
                var f = new FontFamily("avares://Corner/Assets/fonts#Gilroy");
                var tf = new Typeface(f, italic ? FontStyle.Italic : FontStyle.Normal, FontWeight.SemiBold);
                IGlyphTypeface g;
                return FontManager.Current.TryGetGlyphTypeface(tf, out g) ? f : null;
            }
            catch (Exception ex) { Store.Log(ex); return null; }
        }

        void LoadLook()
        {
            fullScale = Math.Max(0.7, Math.Min(1.6, Settings.Get("FullScale", 1)));
            T.Use((int)Settings.Get("Skin", 0)); // Sage or Glass (Glass.cs)
            var gilroy = FindGilroy(false); UiItalic = FindGilroy(true);
            FontFamily = UiFont = gilroy ?? FontFamily.Default;
            FontK = gilroy != null ? GilroyFit : 1; HasSemi = true;
        }

        // ------------------------------------------------------------ Settings, inside the panel

        bool settingsOpen;

        Control SectionHead(string text, string count, bool open, Action toggle)
        {
            var head = new Grid { Height = Sp(24, 22), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var hs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var chev = Glyph("M2.5,1 L5.5,4 L2.5,7", T.Muted, 1.4, 8, 8);
            chev.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative); chev.RenderTransform = new RotateTransform(open ? 90 : 0);
            hs.Children.Add(chev);
            hs.Children.Add(new TextBlock { Text = text.ToUpperInvariant(), FontSize = Z(10), FontWeight = SemiSmall, Foreground = T.Muted, Margin = new Thickness(9, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            if (count != null) hs.Children.Add(new TextBlock { Text = count, FontSize = Z(Sp(10.5, 10)), Foreground = T.Faint, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(hs);
            head.OnLeftDown((s, e) => { e.Handled = true; toggle(); });
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
                leftDock.IsVisible = false; energyScroll.IsVisible = false; tabsRow.IsVisible = false;
                settingsScroll.IsVisible = true;
                DrawSettings();
            }
            else CloseSettings();
        }

        void CloseSettings()
        {
            settingsOpen = false;
            settingsScroll.IsVisible = false;
            leftDock.IsVisible = true;
            SetTab(tab);
        }



        /// A normal window: wider, tasks and energy side by side, on the taskbar, and it stays until closed.
        void EnterFull()
        {
            if (full) { Activate(); return; }
            shown = false; gen++; Hide();
            full = true; paused = false; promptOnly = false; TrayTip("Corner");
            ShowInTaskbar = true; Topmost = false; // an app window while in Full Mode
            ShowPanel(0);
            Activate();
        }

        void ExitFull()
        {
            if (!full) return;
            full = false; shown = false; gen++;
            Hide();
            ShowInTaskbar = false; Topmost = true;
            card.BeginAnimation(OpacityProperty, null); card.Opacity = 0;
        }
    }
}
