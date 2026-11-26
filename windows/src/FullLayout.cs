// Full Mode's arrangement: two columns of sections, each of which can be dragged to another place in either column by
// the dots at its right edge. To begin with (Settings has "Reset"):
//   tasks column:  the open tasks (with "Today's plan" upright beside the ones a plan placed), add a task, then Plan
//   energy column: the chart, the icons (slept, nap, debt, rate, need) with what they open, routines, the done tasks
// The corner panel keeps its own order. "Energy left" in Settings swaps the two columns as they are.

using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Corner
{
    partial class Shell
    {
        ScrollViewer planScroll; // the plan scrolls by itself when a long proposal would take more than half the column
        StackPanel planHost;     // Full Mode: today's plan, under the add box
        StackPanel doneHost;     // Full Mode: the done tasks, in a card under the routines
        Border doneCard;

        // ------------------------------------------------------------ the sections

        const int SecList = 0, SecAdd = 1, SecPlan = 2, SecChart = 3, SecStats = 4, SecRoutines = 5, SecDone = 6, SecCount = 7;
        static readonly string[] SecKeys = { "SecList", "SecAdd", "SecPlan", "SecChart", "SecStats", "SecRoutines", "SecDone" };
        static readonly int[] SecHome = { 0, 1, 2, 100, 101, 102, 103 }; // where each stands: the column (0 tasks, 1 energy) times 100, plus its place in it

        FitStack[] cols;         // the two columns
        ColumnScroll flowScroll; // the tasks column scrolls in this one, the energy column in energyScroll
        Grid[] secs;             // the sections: what they show, and the dots they are dragged by
        Border[] grips;
        StackPanel[] hosts;      // what DrawEnergy draws a section into (from SecChart on)
        Panel drawTo;            // the section being drawn (Full Mode); none in the corner panel

        Panel Into { get { return drawTo ?? energyView; } }
        void ToSection(int k) { drawTo = FullArranged ? hosts[k] : null; sect = Into; }
        void ClearSections() { if (hosts != null) foreach (var h in hosts) if (h != null) h.Children.Clear(); }

        bool FullArranged { get { return full && cols != null && tasksScroll != null && tasksScroll.Parent == secs[SecList]; } }

        void BuildSections()
        {
            planHost = new StackPanel();
            planScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = planHost };
            secs = new Grid[SecCount]; grips = new Border[SecCount]; hosts = new StackPanel[SecCount];
            for (int i = 0; i < SecCount; i++)
            {
                int k = i;
                var six = new GeometryGroup();
                for (int d = 0; d < 6; d++) six.Children.Add(new EllipseGeometry(new Point(1 + 4 * (d % 2), 1 + 5 * (d / 2)), 1, 1));
                var dots = new Path { Fill = T.Muted, Data = six, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var grip = new Border { Width = 12, Height = 26, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 1, i < SecPlan ? 0 : GapY), Child = dots, ToolTip = "Drag to move this section" };
                grip.MouseLeftButtonDown += (s, e) => { e.Handled = true; BeginSection(k, e.GetPosition(mainGrid)); };
                grips[i] = grip;
                // (what DrawEnergy draws ends with its own gap; the list and the add box get theirs here)
                var g = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, i < SecPlan ? GapY : 0) };
                g.MouseEnter += (s, e) => { if (dragSec < 0) Anim(grip, UIElement.OpacityProperty, 1, 120); };
                g.MouseLeave += (s, e) => Anim(grip, UIElement.OpacityProperty, 0, 160);
                if (i >= SecChart) { hosts[i] = new StackPanel(); g.Children.Add(hosts[i]); }
                if (i == SecPlan) g.Children.Add(planScroll);
                Panel.SetZIndex(grip, 5); g.Children.Add(grip);
                secs[i] = g;
            }
            cols = new[] { new FitStack(), new FitStack() };
            foreach (var c in cols) { c.Flex = secs[SecList]; c.Half = secs[SecPlan]; }
            flowScroll = new ColumnScroll { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = cols[0] };
            // inside a column that scrolls, the list and the plan hand the wheel on once they are at their end
            foreach (var sv in new[] { tasksScroll, planScroll })
            {
                var inner = sv;
                inner.PreviewMouseWheel += (s, e) =>
                {
                    if (!FullArranged || (Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
                    if (e.Delta < 0 ? inner.VerticalOffset < inner.ScrollableHeight - 0.5 : inner.VerticalOffset > 0.5) return;
                    ScrollViewer outer = ColumnOf(inner == planScroll ? SecPlan : SecList) == 0 ? flowScroll : energyScroll;
                    outer.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta * 0.4); e.Handled = true;
                };
            }
        }

        /// From FocusLayout: the sections into the two columns of Full Mode; and the list and the add box back to the corner panel.
        void ArrangeFull(bool on)
        {
            if (tasksScroll == null || focusBox == null || listCard == null || bodyGrid == null) return;
            if (cols == null) { if (!on) return; BuildSections(); }
            if (on)
            {
                if (tasksScroll.Parent != secs[SecList])
                {
                    Unparent(tasksScroll); Unparent(tasksAdd);
                    secs[SecList].Children.Insert(0, tasksScroll); secs[SecAdd].Children.Insert(0, tasksAdd);
                    bodyGrid.Children.Insert(0, flowScroll);
                    energyView.Children.Clear();
                    energyScroll.Content = cols[1];
                }
                tasksScroll.Margin = new Thickness(0); tasksAdd.Margin = new Thickness(0); listCard.Margin = new Thickness(0); // the column brings the sides
                flowScroll.Margin = energyScroll.Margin = new Thickness(0, 0, 0, Side - GapY); // (the last section ends with its gap, which its shadow stands in)
                cols[0].Margin = new Thickness(TaskL, 0, TaskR, 0); cols[1].Margin = new Thickness(TaskR, 0, TaskL, 0);
                foreach (var g in grips) ((Path)g.Child).Fill = T.Muted;
                PlaceSections();
            }
            else if (tasksScroll.Parent == secs[SecList])
            {
                EndSection(false);
                Unparent(tasksScroll); Unparent(tasksAdd);
                bodyGrid.Children.Remove(flowScroll);
                planHost.Children.Clear(); ClearSections(); drawTo = null;
                energyScroll.Content = energyView;
                energyScroll.Margin = new Thickness(0, 0, 0, Side);
                bodyGrid.Children.Insert(0, tasksScroll);
                leftDock.Children.Insert(leftDock.Children.Count - 1, tasksAdd); // its place above the list
                listCard.Margin = new Thickness(TaskL, 0, TaskR, 0);
                ClipList();
            }
        }

        static void Unparent(FrameworkElement el) { var host = el.Parent as Panel; if (host != null) host.Children.Remove(el); }

        static int SecPos(int k) { return (int)Settings.Get(SecKeys[k], SecHome[k]); }
        static int ColumnOf(int k) { return SecPos(k) >= 100 ? 1 : 0; }
        static List<int> ColumnList(int col) { return Enumerable.Range(0, SecCount).Where(k => ColumnOf(k) == col).OrderBy(k => SecPos(k)).ToList(); }

        void PlaceSections()
        {
            var want = new[] { ColumnList(0), ColumnList(1) };
            for (int c = 0; c < 2; c++)
            {
                var els = want[c].Select(k => (UIElement)secs[k]).ToList();
                if (cols[c].Children.Cast<UIElement>().SequenceEqual(els)) continue;
                cols[c].Children.Clear();
                foreach (var el in els) { Unparent((FrameworkElement)el); cols[c].Children.Add(el); }
            }
            ShowSections();
        }

        /// A section with nothing in it takes no room and cannot be picked up.
        void ShowSections()
        {
            if (!FullArranged) return;
            for (int k = SecPlan; k < SecCount; k++)
            {
                bool any = k == SecPlan ? planHost.Children.Count > 0 : hosts[k].Children.Cast<UIElement>().Any(x => x.Visibility == Visibility.Visible);
                secs[k].Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void ResetSections()
        {
            for (int k = 0; k < SecCount; k++) Settings.Set(SecKeys[k], SecHome[k]);
            if (FullArranged) PlaceSections();
        }

        /// Puts section k into a column, before the section `before` (-1: at the end).
        void MoveSection(int k, int col, int before)
        {
            var places = new Dictionary<int, int>(); // read off the old places first, then written together
            for (int c = 0; c < 2; c++)
            {
                var order = ColumnList(c); order.Remove(k);
                if (c == col) { int at = before < 0 ? -1 : order.IndexOf(before); order.Insert(at < 0 ? order.Count : at, k); }
                for (int i = 0; i < order.Count; i++) places[order[i]] = c * 100 + i;
            }
            foreach (var p in places) if (SecPos(p.Key) != p.Value) Settings.Set(SecKeys[p.Key], p.Value);
            PlaceSections();
        }

        // ------------------------------------------------------------ dragging a section

        int dragSec = -1, dropCol, dropBefore;
        Canvas dragLayer; Image dragGhost; Border dropLine; Vector dragGrab;

        void BeginSection(int k, Point p)
        {
            if (dragSec >= 0 || !FullArranged) return;
            if (dragLayer == null)
            {
                dragGhost = new Image { Opacity = 0.8, Stretch = Stretch.Fill };
                dropLine = new Border { Height = 4, CornerRadius = new CornerRadius(2) };
                dragLayer = new Canvas { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Visibility = Visibility.Collapsed };
                dragLayer.Children.Add(dragGhost); dragLayer.Children.Add(dropLine);
                Grid.SetColumnSpan(dragLayer, 2); Panel.SetZIndex(dragLayer, 50); mainGrid.Children.Add(dragLayer);
                dragLayer.MouseMove += (s, e) => DragSection(e.GetPosition(mainGrid));
                dragLayer.MouseLeftButtonUp += (s, e) => { e.Handled = true; EndSection(true); };
                dragLayer.LostMouseCapture += (s, e) => EndSection(false);
                PreviewKeyDown += (s, e) => { if (dragSec >= 0 && e.Key == Key.Escape) { e.Handled = true; EndSection(false); } };
            }
            var el = secs[k]; var at = el.TranslatePoint(new Point(0, 0), mainGrid);
            double w = el.ActualWidth, h = Math.Min(el.ActualHeight, 320);
            if (w < 1 || h < 1) return;
            // a picture of the section, which follows the pointer
            double dpi = 1; var src = PresentationSource.FromVisual(this);
            if (src != null && src.CompositionTarget != null) dpi = src.CompositionTarget.TransformToDevice.M11;
            double gap = k < SecPlan ? 0 : GapY; h = Math.Max(1, Math.Min(el.ActualHeight - gap, 320)); // (without the gap under it)
            var all = new RenderTargetBitmap((int)Math.Ceiling(rootGrid.ActualWidth * dpi), (int)Math.Ceiling(rootGrid.ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            all.Render(rootGrid);
            var part = el.TransformToAncestor(rootGrid).TransformBounds(new Rect(0, 0, w, h));
            int x0 = Math.Max(0, (int)(part.X * dpi)), y0 = Math.Max(0, (int)(part.Y * dpi));
            int pw = Math.Min(all.PixelWidth - x0, (int)(part.Width * dpi)), ph = Math.Min(all.PixelHeight - y0, (int)(part.Height * dpi));
            if (pw < 1 || ph < 1) return;
            var bmp = new CroppedBitmap(all, new Int32Rect(x0, y0, pw, ph));
            if (ph < (int)(part.Height * dpi)) h *= ph / (part.Height * dpi); // (its end is outside the window)
            dragGhost.Source = bmp; dragGhost.Width = w; dragGhost.Height = h; dragGhost.Clip = new RectangleGeometry(new Rect(0, 0, w, h), Math.Min(Radius, h / 2), Math.Min(Radius, h / 2));
            dragGrab = new Vector(w - 8, Math.Min(h / 2, Math.Max(0, p.Y - at.Y)));
            dropLine.Background = T.Strong;
            dragSec = k; el.Opacity = 0.3;
            grips[k].BeginAnimation(UIElement.OpacityProperty, null); grips[k].Opacity = 0;
            dragLayer.Visibility = Visibility.Visible;
            dragLayer.CaptureMouse();
            DragSection(p);
        }

        void DragSection(Point p)
        {
            if (dragSec < 0) return;
            Canvas.SetLeft(dragGhost, p.X - dragGrab.X); Canvas.SetTop(dragGhost, p.Y - dragGrab.Y);
            bool left = p.X < mainGrid.ActualWidth / 2;
            dropCol = left != Swapped ? 0 : 1; // (swapped, the energy column is on the left)
            var col = cols[dropCol];
            ScrollViewer view = dropCol == 0 ? flowScroll : energyScroll;
            double top = view.TranslatePoint(new Point(0, 0), mainGrid).Y, bottom = top + view.ActualHeight;
            if (p.Y < top + 24) view.ScrollToVerticalOffset(view.VerticalOffset - 10);
            else if (p.Y > bottom - 24) view.ScrollToVerticalOffset(view.VerticalOffset + 10);
            dropBefore = -1; double line = top;
            foreach (int k in ColumnList(dropCol))
            {
                if (k == dragSec || secs[k].Visibility != Visibility.Visible) continue;
                double y = secs[k].TranslatePoint(new Point(0, 0), mainGrid).Y;
                if (p.Y < y + secs[k].ActualHeight / 2) { dropBefore = k; line = y - GapY / 2; break; }
                line = y + secs[k].ActualHeight + (k < SecPlan ? GapY / 2 : -GapY / 2);
            }
            dropLine.Width = Math.Max(0, col.ActualWidth - 2 * Radius);
            Canvas.SetLeft(dropLine, col.TranslatePoint(new Point(0, 0), mainGrid).X + Radius);
            Canvas.SetTop(dropLine, Math.Max(top, Math.Min(bottom - 4, line - 2)));
        }

        void EndSection(bool drop)
        {
            if (dragSec < 0) return;
            int k = dragSec; dragSec = -1; // (first: letting go of the mouse calls this again)
            secs[k].Opacity = 1;
            dragLayer.Visibility = Visibility.Collapsed; dragGhost.Source = null;
            dragLayer.ReleaseMouseCapture();
            if (!drop) return;
            MoveSection(k, dropCol, dropBefore);
            Dispatcher.BeginInvoke(new Action(() => { if (FullArranged) { ClipList(); PlacePlanStrip(); } }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// Test renders: "grips" shows every section's dots, "secmove" and the like have moved some, "secdrag" holds one in mid-drag.
        void DemoSections(string mode)
        {
            if (mode.StartsWith("sec") || mode == "grips" || mode == "napform") After(mode == "secdrag" ? 2000 : 600, () => { if (FullArranged) DemoSectionsNow(mode); });
        }

        void DemoSectionsNow(string mode)
        {
            if (mode == "grips") foreach (var g in grips) g.Opacity = 1;
            if (mode == "secmove") { MoveSection(SecChart, 0, SecList); MoveSection(SecAdd, 1, SecStats); MoveSection(SecPlan, 1, -1); }
            if (mode == "secall") { for (int k = SecChart; k < SecCount; k++) MoveSection(k, 0, -1); }
            if (mode == "secmix") { MoveSection(SecList, 1, SecRoutines); MoveSection(SecStats, 0, SecAdd); }
            if (mode == "secreset") { MoveSection(SecChart, 0, SecList); MoveSection(SecList, 1, -1); ResetSections(); }
            if (mode == "secexit") { MoveSection(SecList, 1, -1); After(700, ExitFull); }
            if (mode == "secagain") { MoveSection(SecList, 1, -1); After(300, ExitFull); After(1100, () => EnterFull()); }
            if (mode == "secsettings") ToggleSettings();
            if (mode == "secdrag" || mode == "secdrop") After(1, () =>
            {
                var at = secs[SecRoutines].TranslatePoint(new Point(0, 0), mainGrid);
                BeginSection(SecRoutines, new Point(at.X + secs[SecRoutines].ActualWidth - 8, at.Y + 15));
                var to = secs[SecAdd].TranslatePoint(new Point(0, 0), mainGrid);
                DragSection(new Point(to.X + secs[SecAdd].ActualWidth - 40, to.Y - 4));
                if (mode == "secdrop") EndSection(true);
            });
            if (mode == "napform") { napOpen = true; statOpen[0] = statOpen[2] = true; DrawEnergy(); }
        }

        /// From DrawPlan, after the routines: an empty card that FillDone fills (Rebuild calls it too).
        /// It is the Routines box again in the pale shade: one slim line while closed, the done tasks under it when open.
        void DrawDoneCard()
        {
            doneHost = new StackPanel();
            doneCard = new RimBorder { CornerRadius = new CornerRadius(Radius), Background = T.Soft, Padding = new Thickness(PadX, Sp(5, 4), PadX, Sp(5, 4)), Margin = new Thickness(0, 0, 0, GapY), Child = doneHost };
            ToSection(SecDone);
            Into.Children.Add(doneCard);
            FillDone();
        }

        // (FillDone is in Work.cs, with the days before today)

        // ------------------------------------------------------------ "Today's plan", beside the planned tasks

        Grid listHost;          // the task list and, over its left edge, the strip
        Border planStrip;
        TextBlock planStripText;
        const double PlanStripW = 18;

        /// A task that the plan applied today has placed (Plan.cs), and that is still to do.
        bool InTodaysPlan(TaskItem t)
        {
            var d = t.DueAt;
            if (PriorityMode) return OrderToday && !t.Done && (d == null || d.Value.Date <= System.DateTime.Today); // by priority: the tasks the order covers (Work.cs)
            return t.Planned && !t.Done && t.HasTime && d != null && d.Value.Date == System.DateTime.Today;
        }

        void BuildPlanStrip()
        {
            planStripText = new TextBlock { Text = "TODAY'S PLAN", LayoutTransform = new System.Windows.Media.RotateTransform(-90), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            planStrip = new Border { Width = PlanStripW, CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Child = planStripText };
            listHost.Children.Add(planStrip);
            list.SizeChanged += (s, e) => PlacePlanStrip(); // a row that opens or wraps moves the ones under it
        }

        /// From Rebuild. Full Mode, once a plan has been applied: a lime strip runs down the left of the list beside the
        /// tasks it placed, from the first to the last. It stands mostly in the card's own edge, so the list moves over
        /// only a little for it (the rows' titles have no room to give).
        void PlacePlanStrip()
        {
            if (listHost == null || planStrip == null) return;
            var mine = FullArranged ? rows.Where(r => InTodaysPlan(r.Task)).ToList() : null;
            bool on = mine != null && mine.Count > 0;
            double pad = listCard.Padding.Left, stripX = 4 - pad; // 4 in from the card's edge
            double left = on ? System.Math.Max(0, stripX + PlanStripW + 3 - RowOver) : 0; // the circles start 3 after it
            if (list.Margin.Left != left) list.Margin = new Thickness(left, list.Margin.Top, list.Margin.Right, list.Margin.Bottom);
            if (!on) { planStrip.Visibility = Visibility.Collapsed; return; }
            listHost.UpdateLayout();
            double top = double.MaxValue, bottom = 0;
            foreach (var r in mine)
            {
                if (!listHost.IsAncestorOf(r.Wrap)) continue;
                double y = r.Wrap.TranslatePoint(new Point(0, 0), listHost).Y;
                top = System.Math.Min(top, y); bottom = System.Math.Max(bottom, y + r.Wrap.ActualHeight);
            }
            if (bottom - top < 12) { planStrip.Visibility = Visibility.Collapsed; return; }
            planStrip.Background = T.Accent; planStripText.Foreground = T.OnAccent;
            planStripText.FontSize = Z(9); planStripText.FontWeight = SemiSmall;
            planStrip.Margin = new Thickness(stripX, top + 2, 0, 0); planStrip.Height = bottom - top - 4;
            planStripText.Visibility = planStrip.Height >= TextW(planStripText.Text, Z(9), SemiSmall) + 14 ? Visibility.Visible : Visibility.Collapsed; // (too short for its name: a plain bar)
            planStrip.Visibility = Visibility.Visible;
        }
    }

    /// A column of sections, one under the other. The task list (Flex) takes the room the others leave and scrolls when it
    /// needs more, but keeps some height; the plan (Half) takes up to half. If that is still too much, the column scrolls.
    class FitStack : Panel
    {
        public UIElement Flex, Half;
        public double View = double.PositiveInfinity; // the height of what the column scrolls in, from ColumnScroll
        const double FlexLeast = 170;

        protected override Size MeasureOverride(Size avail)
        {
            double view = View - Margin.Top - Margin.Bottom, used = 0, w = 0;
            foreach (UIElement c in InternalChildren)
            {
                if (c == Flex) continue;
                c.Measure(new Size(avail.Width, c == Half ? view * 0.5 : double.PositiveInfinity));
                used += c.DesiredSize.Height; w = Math.Max(w, c.DesiredSize.Width);
            }
            if (Flex != null && InternalChildren.Contains(Flex))
            {
                Flex.Measure(new Size(avail.Width, Math.Max(FlexLeast, view - used)));
                used += Flex.DesiredSize.Height; w = Math.Max(w, Flex.DesiredSize.Width);
            }
            return new Size(w, used);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double y = 0;
            foreach (UIElement c in InternalChildren) { c.Arrange(new Rect(0, y, final.Width, c.DesiredSize.Height)); y += c.DesiredSize.Height; }
            return final;
        }
    }

    /// The scroller around a column: tells it how high the view is, which a scroller's content cannot see by itself.
    class ColumnScroll : ScrollViewer
    {
        protected override Size MeasureOverride(Size room)
        {
            var col = Content as FitStack;
            if (col != null && col.View != room.Height) { col.View = room.Height; col.InvalidateMeasure(); }
            return base.MeasureOverride(room);
        }
    }
}
