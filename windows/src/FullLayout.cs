// Full Mode's arrangement (from the user's mockup, 2026-11-23):
//   tasks column:  the open tasks (with "Today's plan" upright beside the ones a plan placed), add a task, then Plan
//   energy column: the next task with its timer (as the corner panel opens on it), the chart, slept / debt / need,
//                  nap and rate, routines, then the done tasks
// The corner panel keeps its own order. "Energy left" in Settings swaps the two columns as they are.

using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Corner
{
    partial class Shell
    {
        FitStack fullFlow;       // Full Mode, the tasks column: the list (which scrolls when it is long), the add box, the plan
        ScrollViewer planScroll; // the plan scrolls by itself when a long proposal would take more than half the column
        StackPanel planHost;     // Full Mode: today's plan, under the add box
        StackPanel doneHost;     // Full Mode: the done tasks, in a card under the routines
        Border doneCard;

        /// From FocusLayout: in Full Mode the add box and the plan go under the list, and the next-task box on top of the
        /// energy column (DrawEnergy puts it back there each time it redraws the column); and back.
        void ArrangeFull(bool on)
        {
            if (tasksScroll == null || focusBox == null || listCard == null || bodyGrid == null) return;
            if (fullFlow == null)
            {
                fullFlow = new FitStack(); planHost = new StackPanel();
                planScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Content = planHost };
            }
            if (on)
            {
                if (tasksScroll.Parent != fullFlow)
                {
                    Unparent(tasksScroll); Unparent(tasksAdd);
                    fullFlow.Children.Clear();
                    fullFlow.Children.Add(tasksScroll); fullFlow.Children.Add(tasksAdd); fullFlow.Children.Add(planScroll);
                    bodyGrid.Children.Insert(0, fullFlow);
                }
                tasksScroll.Margin = new Thickness(0);
                tasksAdd.Margin = new Thickness(TaskL, GapY, TaskR, 0);
                planHost.Margin = new Thickness(TaskL, GapY, TaskR, Side - GapY); // (the plan's card brings GapY of its own)
                FocusOnTop();
            }
            else if (tasksScroll.Parent == fullFlow)
            {
                fullFlow.Children.Clear(); bodyGrid.Children.Remove(fullFlow);
                planHost.Children.Clear();
                bodyGrid.Children.Insert(0, tasksScroll);
                Unparent(focusBox);
                leftDock.Children.Insert(leftDock.Children.IndexOf(actionRow), focusBox); // its place above the chips
                leftDock.Children.Insert(leftDock.Children.Count - 1, tasksAdd);           // and the add box's, above the list
            }
        }

        static void Unparent(FrameworkElement el) { var host = el.Parent as Panel; if (host != null) host.Children.Remove(el); }

        /// Full Mode: the next-task box as the first thing in the energy column, above the chart.
        void FocusOnTop()
        {
            if (energyView == null || focusBox == null) return;
            if (focusBox.Parent != energyView || energyView.Children.IndexOf(focusBox) != 0)
            {
                Unparent(focusBox);
                energyView.Children.Insert(0, focusBox);
            }
            focusBox.Margin = new Thickness(0, 0, 0, GapY);
        }

        bool FullArranged { get { return full && fullFlow != null && tasksScroll != null && tasksScroll.Parent == fullFlow; } }

        /// From DrawPlan, after the routines: an empty card that FillDone fills (Rebuild calls it too).
        /// It is the Routines box again in the pale shade: one slim line while closed, the done tasks under it when open.
        void DrawDoneCard()
        {
            doneHost = new StackPanel();
            doneCard = new RimBorder { CornerRadius = new CornerRadius(Radius), Background = T.Soft, Padding = new Thickness(PadX, Sp(5, 4), PadX, Sp(5, 4)), Margin = new Thickness(0, 0, 0, GapY), Child = doneHost };
            energyView.Children.Add(doneCard);
            FillDone();
        }

        void FillDone()
        {
            if (doneHost == null || doneCard == null) return;
            doneHost.Children.Clear();
            var done = data.Tasks.Where(t => t.Done).OrderByDescending(t => t.DoneAt ?? "").ToList();
            doneCard.Visibility = full && done.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!full || done.Count == 0) return;
            var head = (Grid)SectionHead("Done", done.Count.ToString(), data.DoneOpen, () => { data.DoneOpen = !data.DoneOpen; Save(); Rebuild(); });
            if (data.DoneOpen)
            {
                var clear = new TextBlock { Text = "Clear", FontSize = Z(11.5), Foreground = T.Muted, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 2, 0, 2), Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
                clear.MouseEnter += (s, e) => clear.Foreground = T.Danger;
                clear.MouseLeave += (s, e) => clear.Foreground = T.Muted;
                clear.MouseLeftButtonDown += (s, e) => { e.Handled = true; data.Tasks.RemoveAll(t => t.Done); Save(); Rebuild(); };
                head.Children.Add(clear);
            }
            doneHost.Children.Add(head);
            if (!data.DoneOpen) return;
            var rowsHost = new StackPanel { Margin = new Thickness(-RowOver, Sp(4, 3), -RowOver, Sp(4, 3)) }; // (a row reaches RowOver past its content)
            foreach (var t in done) rowsHost.Children.Add(MakeRow(t).Wrap);
            doneHost.Children.Add(rowsHost);
        }

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

    /// A column whose first child takes the room the others leave, and so scrolls when it needs more; the last child
    /// may take up to half. With room to spare they simply stand one under the other.
    class FitStack : Panel
    {
        protected override Size MeasureOverride(Size avail)
        {
            bool open = double.IsInfinity(avail.Height);
            double used = 0, w = 0; int n = InternalChildren.Count;
            for (int i = n - 1; i >= 0; i--)
            {
                var c = InternalChildren[i];
                double room = open ? avail.Height : System.Math.Max(0, i == n - 1 && n > 1 ? avail.Height * 0.5 : avail.Height - used);
                c.Measure(new Size(avail.Width, room));
                used += c.DesiredSize.Height; w = System.Math.Max(w, c.DesiredSize.Width);
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
}
