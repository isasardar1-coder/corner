// Ported from corner/src/Corner.cs (WPF) to Avalonia: tools/port_ui.py, then fixed by hand.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using IO = System.IO;

namespace Corner
{
    /// Corner has two skins, chosen in Settings. Each colour is named for what it is used for, so a change here changes
    /// every place that plays that part; each has a value for Sage, then one for Glass.
    ///   Sage:  a pale sage panel with darker trays, white cards and pills, forest green for whatever is chosen or
    ///          switched on, and lime for the main action, the running timer and the peaks.
    ///   Glass: the panel is a frosted picture of whatever is behind it (Glass.cs) under a pale tint; trays, cards and
    ///          pills are layers of translucent white on it, each with a bright hairline rim.
    /// The brushes are never replaced: a change of skin changes their colours, and everything drawn with them follows.
    static class T
    {
        public static readonly string[] Names = { "Sage", "Glass" };
        public static int Skin;
        public static bool Glass { get { return Skin == 1; } }

        static readonly List<SolidColorBrush> brushes = new List<SolidColorBrush>();
        static readonly List<string[]> values = new List<string[]>();
        public static Color C(string hex) { return Color.Parse(hex); }
        static SolidColorBrush B(string sage, string glass) { var b = new SolidColorBrush(C(sage)); brushes.Add(b); values.Add(new[] { sage, glass }); return b; }

        public static readonly SolidColorBrush
            // surfaces, from the back forwards
            Bg = B("#E8ECDB", "#9EEDF7FA"),         // the panel (Glass: the tint's colour; the panel itself is drawn in Glass.cs)
            Edge = B("#D3D9C2", "#FFD9E7F0"),       // the panel's own edge, and a tooltip's
            Tray = B("#D9DFC7", "#73C3D0DD"),       // a box a step apart from the panel: the next task, the check-ins, the chart
            Field = B("#FFFFFF", "#D1F7FAFD"),      // the brightest surface: cards, chips, pills, boxes to type in
            Lift = B("#EEF1E4", "#B8F7FBFF"),       // a control sitting on a tray, and a Field chip under the mouse
            Raised = B("#E8ECDB", "#E6E4EEF4"),     // a control sitting on a Field card
            Ctl = B("#C6CFB1", "#999AAFBD"),        // tracks, rails, empty bars, the chart between its zones
            Line = B("#E3E7D6", "#C9C9D9E3"),       // a thin rule on a Field card
            Hover = B("#0F16281D", "#301F5268"),    // a task row under the mouse
            Ring = B("#D9DFC7", "#B3A9BFD0"),       // the empty part of the progress ring in the header
            Pop = B("#FFFFFF", "#F7F7FAFD"),        // a tooltip: on its own over anything, so never see-through
            // text; all three are readable on the panel, on a tray and on a card
            Text = B("#141A12", "#18232D"), Muted = B("#535C4B", "#465564"), Faint = B("#586150", "#4D5C6A"),
            // the dark colour: the next task's timer, whatever is chosen or on, the dip, and the prompts that need an answer
            Strong = B("#16281D", "#F21A2A38"), OnStrong = B("#FFFFFF", "#F8FCFF"), OnStrongSoft = B("#C9D3BE", "#D2E0EA"), StrongLine = B("#7C8C7D", "#A77E9AAA"), StrongLift = B("#24392C", "#A8385266"),
            // the accent: the main action, a running timer, the peaks. Soft is its pale form: today, the row being timed, footers
            Accent = B("#B4C12A", "#A9D5FF"), OnAccent = B("#141A12", "#142638"), Soft = B("#EEF2B4", "#CDE1F2FF"),
            Sand = B("#CDC6AB", "#CCD8D5C9"),       // the hours of expected sleep on the chart
            Danger = B("#A8401F", "#C04435"), DangerSoft = B("#F4DDD3", "#DDF3D7D4"); // overdue, and nothing else

        // ---- Glass only: the panel's layers over the picture of what is behind it, and the rim of everything on it
        static readonly GradientStop rimTop = new GradientStop(Colors.Transparent, 0), rimEnd = new GradientStop(Colors.Transparent, 1);
        /// The hairline round a box (RimBorder, Glass.cs): bright at the top, faint at the bottom; invisible in Sage.
        public static readonly Pen RimPen = new Pen(Wpf.Linear(new GradientStops { rimTop, rimEnd }, new Point(0, 0), new Point(0, 1)), 1);
        const string RimTopGlass = "#B8FFFFFF", RimEndGlass = "#5FDFEAF2";
        /// The panel's tint, a cool frost; and the light that falls on its top (a strip of fixed height, Glass.cs).
        public static readonly IBrush GlassTint = Wpf.Linear(C("#9EEDF7FA"), C("#9EF2F3F8"), 70);
        public static readonly IBrush GlassSheen = Wpf.Linear(C("#70FFFFFF"), C("#00FFFFFF"), 90);
        public const double GlassSheenHeight = 54;
        /// The panel's rim: light caught at the top left, lost along the sides, a little back at the bottom right.
        public static readonly IBrush GlassRim = Wpf.Linear(new GradientStops
            { new GradientStop(C("#D9FFFFFF"), 0), new GradientStop(C("#85F9FFFF"), 0.5), new GradientStop(C("#B6FFFFFF"), 1) }, new Point(0, 0), new Point(1, 1));
        public static readonly IBrush GlassBase = new SolidColorBrush(C("#E9EEF5")); // behind the tint when the screen cannot be read
        public static Color GlassShadow = C("#101923"); public static double GlassShadowOpacity = 0.3;

        public static void Use(int skin)
        {
            Skin = skin == 1 ? 1 : 0;
            for (int i = 0; i < brushes.Count; i++) brushes[i].Color = C(values[i][Skin]);
            rimTop.Color = Glass ? C(RimTopGlass) : Colors.Transparent; rimEnd.Color = Glass ? C(RimEndGlass) : Colors.Transparent;
            RimBorder.Reskin();
        }
    }

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


        static readonly IEasingFunction Out = Frozen(new QuinticEase { EasingMode = EasingMode.EaseOut });
        static readonly IEasingFunction Soft = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
        static readonly IEasingFunction Pop = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 });
        static IEasingFunction Frozen(EasingFunctionBase e) { e.Freeze(); return e; }

        Data data;
        readonly Stopwatch clock = Stopwatch.StartNew();
        Grid card; DropShadowDirectionEffect shadow;
        readonly TranslateTransform cardShift = new TranslateTransform();
        readonly ScaleTransform cardScale = new ScaleTransform(1, 1);
        TextBlock dayText, subText, inputHint, inputPreview;
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

        DispatcherTimer mouseTimer, remindTimer;

        // ------------------------------------------------------------ startup

        public Shell()
        {
            Title = "Corner";
            SystemDecorations = SystemDecorations.None; Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            ShowInTaskbar = false; Topmost = true; ShowActivated = false; CanResize = false;
            UseLayoutRounding = true;
            FontFamily = UiFont;
            Width = CardW + Gap + Pad; Height = 600; Left = -10000; Top = -10000;
            Opened += (s, e) => Os.FloatEverywhere(this); // every Space, over full-screen apps, out of Cmd+Tab
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
            remindTimer.Tick += (s, e) => { LogDays(); CheckReminders(); CheckInTick(); }; // LogDays: yesterday into days.csv (Stats.cs)
            remindTimer.Start();

            if (firstRun && (!test || Environment.GetEnvironmentVariable("CORNER_ONBOARD") == "1")) ShowOnboarding(); // a new install: name, sleep need, routines (Onboard.cs)
            else if (peek) ShowPanel(6000);
            CheckReminders();
        }

        // ------------------------------------------------------------ helpers

        static void Anim(AvaloniaObject o, AvaloniaProperty dp, double to, int ms, int delay = 0, double? from = null, IEasingFunction ease = null, Action done = null)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Soft };
            if (from != null) a.From = from;
            if (delay > 0) a.BeginTime = TimeSpan.FromMilliseconds(delay);
            if (done != null) a.Completed += (s, e) => done();
            o.BeginAnimation(dp, a);
        }

        static void Set(AvaloniaObject o, AvaloniaProperty dp, double v)
        {
            o.BeginAnimation(dp, null);
            o.SetValue(dp, v);
        }

        void After(int ms, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); a(); };
            t.Start();
        }

        static Path Glyph(string d, IBrush stroke, double th, double w, double h)
        {
            return new Path
            {
                Data = Geometry.Parse(d), Stroke = stroke, StrokeThickness = th, Width = w, Height = h,
                StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
            };
        }

        static TextBox Box(double size)
        {
            return new TextBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = T.Text, CaretBrush = T.Text,
                SelectionBrush = T.Accent, FontSize = Z(size), Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, ContextMenu = null
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

        // Which corner of the screen opens the list: 0 top right, 1 top left, 2 bottom right, 3 bottom left ("Corner" in settings.json)
        int corner;
        bool OnRight { get { return corner == 0 || corner == 2; } }
        bool OnTop { get { return corner < 2; } }

        void Place()
        {
            Restyle(); // spacing and text sizes for the corner panel or Full Mode (Ui.cs)
            var wa = Wpf.WorkArea;
            double z = full ? fullScale : scale;
            zoom.ScaleX = z; zoom.ScaleY = z;
            card.Width = full ? FullW : CardW;
            card.MinHeight = full ? Math.Min(FullMinH, (wa.Height - 2 * Pad - 40) / z) : 0;
            if (full) { PlaceFull(wa, z); return; }
            Width = CardW * scale + Gap + Pad; Height = wa.Height; Left = OnRight ? wa.Right - Width : wa.Left; Top = wa.Top;
            card.HorizontalAlignment = OnRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            card.VerticalAlignment = OnTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            card.Margin = new Thickness(OnRight ? 0 : Gap, OnTop ? Gap : 0, OnRight ? Gap : 0, OnTop ? 0 : Gap);
            card.RenderTransformOrigin = new RelativePoint(OnRight ? 1 : 0, OnTop ? 0 : 1, RelativeUnit.Relative);
            PlaceMoved(wa); // a dragged position, and the height that fits there (Checks.cs)
            SyncHost();
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
            UpdateTrayMenu();
        }

        bool paused;
        void Sleep()
        {
            if (full) { ExitFull(); return; } // in Full Mode the cross just closes the window
            paused = true; HidePanel();
            TrayTip("Corner is closed - click to reopen");
            Os.Notify("Corner is closed", "Click its menu-bar icon to bring it back.");
        }
        void Wake() { if (full) { Activate(); return; } paused = false; TrayTip("Corner"); ShowPanel(5000); }

        // ------------------------------------------------------------ static layout

        void Build()
        {
            card = new Grid
            {
                Width = CardW, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, Gap, Gap, 0), RenderTransformOrigin = new RelativePoint(1, 0, RelativeUnit.Relative), Opacity = 0
            };
            var tg = new TransformGroup(); tg.Children.Add(cardScale); tg.Children.Add(cardShift);
            card.RenderTransform = tg;

            shadow = new DropShadowDirectionEffect { BlurRadius = 34, ShadowDepth = 10, Direction = 270, Color = Color.FromRgb(60, 54, 34), Opacity = 0.3 };
            panelBg = new Border { CornerRadius = new CornerRadius(18), Background = T.Bg, BorderBrush = T.Edge, BorderThickness = new Thickness(1), Effect = shadow };
            card.Children.Add(panelBg);
            // the Glass skin's tint and rim, over the picture of what is behind the panel (Glass.cs)
            glassSheen = new Border { Height = T.GlassSheenHeight, VerticalAlignment = VerticalAlignment.Top, Background = T.GlassSheen };
            glassTint = new Border { Background = T.GlassTint, BorderBrush = T.GlassRim, BorderThickness = new Thickness(1), IsHitTestVisible = false, IsVisible = false, Child = glassSheen };
            card.Children.Add(glassTint);

            var dock = new DockPanel { LastChildFill = true };
            card.Children.Add(dock);

            // header: (Full Mode only) a round button back to the corner, then the date; at the right the progress ring and
            // the round white icon buttons. Sizes and margins for each mode are set in Restyle (Ui.cs).
            var header = new Grid { Background = Brushes.Transparent };
            headerRow = header;
            header.Cursor = Cursors.SizeAll; // drag the panel by its header, in both modes (Checks.cs)
            header.OnLeftDown((s, e) => BeginMove(e));
            header.PointerMoved += (s, e) => ContinueMove();
            header.OnLeftUp((s, e) => EndMove());
            header.PointerCaptureLost += (s, e) => EndMove();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headIcons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(headIcons, 2); header.Children.Add(headIcons);
            minBtn = HeadIcon("M8.5,5 H1.5 M4.5,2 L1.5,5 L4.5,8", 1.5, 10, "Back to the corner", ExitFull);
            headIcons.Children.Remove(minBtn); minBtn.IsVisible = false; minBtn.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(minBtn); // the first column, shown in Full Mode
            headText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            dayText = new TextBlock { FontSize = Hd(22), FontWeight = FontWeight.SemiBold, Foreground = T.Text };
            subText = new TextBlock { FontSize = Z(12), Foreground = T.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
            headText.Children.Add(dayText); headText.Children.Add(subText);
            Grid.SetColumn(headText, 1); header.Children.Add(headText);
            // how much of the list is done: a ring on a white disc, drawn at 24 and scaled to fit the disc
            var ring = new Grid { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            ring.Children.Add(new Ellipse { Width = 22, Height = 22, Stroke = T.Ring, StrokeThickness = 3 });
            ringArc = new Path { Stroke = T.Strong, StrokeThickness = 3, StrokeLineCap = PenLineCap.Round, Width = 24, Height = 24 };
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
            var addBox = new PillBorder { Margin = new Thickness(14, 0, 14, 8), Height = 40, Background = T.Field, BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Cursor = Cursors.IBeam };
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
            inputChip = new PillBorder { Background = T.Soft, Padding = new Thickness(7, 2, 7, 3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), IsVisible = false, IsHitTestVisible = false, Child = inputPreview };
            Grid.SetColumn(inputChip, 2); ag.Children.Add(inputChip);
            addBox.Child = ag;
            DockPanel.SetDock(addBox, Dock.Top); leftDock.Children.Add(addBox);
            tasksAdd = addBox;

            input.TextChanged += (s, e) =>
            {
                inputHint.IsVisible = input.Text.Length == 0 ? true : false;

                UpdateInputPreview();
                
            };
            input.GotFocus += (s, e) => addBox.BorderBrush = T.Strong; // the box being typed in is outlined
            input.LostFocus += (s, e) => addBox.BorderBrush = Brushes.Transparent;
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
            scroller.Content = null; listCard.Child = list; scroller.Content = listCard;
            leftDock.Children.Add(BuildBody(scroller));
            scroller.SizeChanged += (s, e) => ClipList(); // (Ui.cs)
            dock.Children.Add(BuildMain());

            card.OnLeftDown((s, e) => { Activate(); input.Focus(); });
            // the corner panel opens on the next task alone; the pointer on it brings in the header and the rest (Focus.cs)
            card.PointerEntered += (s, e) => { if (!demoRun) Reveal(); };
            card.PointerMoved += (s, e) => { if (bare && !demoRun) Reveal(); };
            card.OnPreviewWheel((s, e) =>
            {
                if (!Os.ZoomKey()) return; // Cmd + wheel resizes (Ctrl on Windows)
                e.Handled = true;
                SetScale(CurrentScale + (e.Delta.Y > 0 ? 0.05 : -0.05), true);
            });

            var root = new Grid();
            cardHost = new LayoutTransformControl { LayoutTransform = zoom, Child = card };
            root.Children.Add(cardHost);
            Content = root;
        }

        /// A round white button in the header. They share a size and a spacing, which Restyle sets for the mode.
        Border HeadIcon(string path, double thickness, double size, string tip, Action click)
        {
            var cell = new PillBorder { Background = T.Field, Cursor = Cursors.Hand, [ToolTip.TipProperty] = tip, Child = Glyph(path, T.Text, thickness, size, size) };
            cell.PointerEntered += (s, e) => cell.Background = T.Lift;
            cell.PointerExited += (s, e) => cell.Background = T.Field;
            cell.OnLeftDown((s, e) => { e.Handled = true; click(); });
            headIcons.Children.Add(cell);
            return cell;
        }

        void UpdateHeader()
        {
            var inv = CultureInfo.InvariantCulture;
            int open = data.Tasks.Count(t => !t.Done), done = data.Tasks.Count - open;
            dayText.Text = DateTime.Now.ToString("dddd", inv);
            dayText.IsVisible = full ? true : false; // the corner panel keeps only a small date line
            string count = open > 0 ? open + " open" : data.Tasks.Count > 0 ? "all done" : "";
            subText.Inlines.Clear();
            if (full) subText.Inlines.Add(new Run(DateTime.Now.ToString("MMMM d", inv) + (count.Length > 0 ? "  ·  " + count : "")));
            else
            {   // the corner panel: the date stands out, the count sits quietly beside it
                subText.Inlines.Add(new Run(DateTime.Now.ToString("ddd d MMM", inv)) { Foreground = T.Text, FontWeight = FontWeight.SemiBold });
                if (count.Length > 0) subText.Inlines.Add(new Run("   " + count) { FontSize = Z(11) });
            }

            DrawFocus(); // the next-task box follows every change (Focus.cs)
            double f = data.Tasks.Count == 0 ? 0 : (double)done / data.Tasks.Count;
            ringDisc[ToolTip.TipProperty] = data.Tasks.Count == 0 ? "Progress: fills as tasks are ticked off" : "Progress: " + done + " of " + data.Tasks.Count + " tasks done";
            const double c = 12, r = 9.5;
            if (f <= 0) ringArc.Data = null;
            else if (f >= 0.999) ringArc.Data = Wpf.Ellipse(new Point(c, c), r, r);
            else
            {
                double a = f * 2 * Math.PI;
                var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
                fig.Segments.Add(Wpf.Arc(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise));
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
            var done = data.Tasks.Where(t => t.Done).OrderByDescending(t => t.DoneAt ?? "").ToList();

            foreach (var t in active) { var r = MakeRow(t); rows.Add(r); list.Children.Add(r.Wrap); }
            if (active.Count == 0)
                list.Children.Add(new TextBlock
                {
                    Text = done.Count > 0 ? "All done. Enjoy it." : "Nothing here yet.",
                    FontSize = Z(12.5), Foreground = T.Faint, Margin = new Thickness(RowOver, 6, RowOver, 6)
                });
            if (done.Count > 0)
            {
                list.Children.Add(DoneHeader(done.Count));
                if (data.DoneOpen) foreach (var t in done) list.Children.Add(MakeRow(t).Wrap);
            }
            flashId = null;
            UpdateHeader();
        }

        Control DoneHeader(int count)
        {
            // a pale-lime bar across the list: the arrow starts where the rows' circles do, "Clear" ends where the timers do
            var g = new Grid { Height = Sp(32, 28) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var chev = Glyph("M2.5,1 L5.5,4 L2.5,7", T.Text, 1.5, 8, 8);
            chev.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            chev.RenderTransform = new RotateTransform(data.DoneOpen ? 90 : 0);
            sp.Children.Add(chev);
            sp.Children.Add(new TextBlock { Text = "DONE", FontSize = Z(10), FontWeight = SemiSmall, Foreground = T.Text, Margin = new Thickness(Sp(9, 7), 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = count.ToString(), FontSize = Z(Sp(10.5, 10)), Foreground = T.Muted, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(sp);
            if (data.DoneOpen)
            {
                var clear = new TextBlock { Text = "Clear", FontSize = Z(11.5), Foreground = T.Muted, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 4, 0, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                clear.PointerEntered += (s, e) => clear.Foreground = T.Danger;
                clear.PointerExited += (s, e) => clear.Foreground = T.Muted;
                clear.OnLeftDown((s, e) => { e.Handled = true; data.Tasks.RemoveAll(t => t.Done); Save(); Rebuild(); });
                g.Children.Add(clear);
            }
            var bar = new Border { CornerRadius = new CornerRadius(Sp(12, 10)), Background = T.Soft, Padding = new Thickness(RowOver + 2, 0, RowOver + 2, 0), Margin = new Thickness(0, Sp(6, 4), 0, 0), Child = g, Cursor = Cursors.Hand };
            bar.OnLeftDown((s, e) => { e.Handled = true; data.DoneOpen = !data.DoneOpen; Save(); Rebuild(); });
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
            var mark = new Grid { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(RowOver, 0, 0, 0), };
            mark.LayoutScale(ck / 18, 18, 18);
            var ringHot = new Ellipse { Width = 18, Height = 18, Fill = T.Soft, Stroke = T.Text, StrokeThickness = 1.5, Opacity = 0, IsHitTestVisible = false }; // under the mouse the circle fills pale lime
            var fillScale = new ScaleTransform(t.Done ? 1 : 0, t.Done ? 1 : 0);
            var fill = new Ellipse { Width = 18, Height = 18, Fill = t.Done ? (IBrush)T.Ctl : T.Accent, RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), RenderTransform = fillScale, IsHitTestVisible = false };
            var tick = Glyph("M5,9.4 L7.9,12.2 L13.2,6.2", t.Done ? (IBrush)T.Text : T.OnAccent, 1.8, 18, 18);
            tick.Opacity = t.Done ? 1 : 0;
            mark.Children.Add(new Ellipse { Width = 18, Height = 18, Stroke = t.Done ? (IBrush)T.Ctl : T.Text, StrokeThickness = 1.5, IsHitTestVisible = false });
            mark.Children.Add(ringHot); mark.Children.Add(fill); mark.Children.Add(tick);
            box.Children.Add(mark);
            box.PointerEntered += (s, e) => Anim(ringHot, OpacityProperty, 1, 100);
            box.PointerExited += (s, e) => Anim(ringHot, OpacityProperty, 0, 160);
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
            Control clockIcon = null, etag = null;
            if (!t.Done)
            {
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Height = rowH, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Cursor = Cursors.Hand };
                if (t.Routine != null) chip.Children.Add(RoutineMark());
                etag = EnergyTag(t); chip.Children.Add(etag); // deep / light / unset, one click cycles
                var due = t.DueAt;
                if (due != null)
                {
                    IBrush b = t.Overdue ? T.Danger : due.Value.Date == DateTime.Today ? (IBrush)T.Text : T.Muted;
                    if (t.HasTime)
                    {
                        var bell = Glyph("M5,1.2 C3.3,1.2 2.3,2.6 2.3,4.3 V6.2 L1.3,7.8 H8.7 L7.7,6.2 V4.3 C7.7,2.6 6.7,1.2 5,1.2 Z M4.2,9.3 H5.8", b, 1, 10, 10.5);
                        bell.Margin = new Thickness(0, 1, 4, 0);
                        chip.Children.Add(bell);
                    }
                    // overdue says so in words, not only in red
                    string when = When.Label(due.Value, t.HasTime);
                    if (t.Overdue) when = "Overdue · " + (due.Value.Date == DateTime.Today ? due.Value.ToString("t", CultureInfo.CurrentCulture) : when.StartsWith("Yesterday") ? "y" + when.Substring(1) : when);
                    var whenText = new TextBlock { Text = when, FontSize = Z(11.5), Foreground = b, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 1) }; if (t.Overdue) Lean(whenText);
                    if (t.Overdue || due.Value.Date == DateTime.Today)
                    {   // today and overdue are tags: pale lime, and a pale red
                        whenText.Margin = new Thickness(0, 0, 0, 1);
                        chip.Children.Add(new PillBorder { Background = t.Overdue ? T.DangerSoft : T.Soft, Padding = new Thickness(8, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center, Child = whenText });
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
                chip.OnLeftDown((s, e) => { e.Handled = true; whenOpenId = whenOpenId == t.Id ? null : t.Id; Rebuild(); });
                main.Children.Add(RowSide(t, chip, main)); // the timer; effort and date beside it in Full Mode, on the tooltip otherwise (Focus.cs)
            }

            // delete
            var del = new Grid { Height = rowH, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Cursor = Cursors.Hand, Opacity = 0, [ToolTip.TipProperty] = "Delete" };
            var cross = Glyph("M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5", T.Muted, 1.4, 10, 10);
            del.Children.Add(cross);
            del.PointerEntered += (s, e) => cross.Stroke = T.Danger;
            del.PointerExited += (s, e) => cross.Stroke = T.Muted;
            Grid.SetColumn(del, 2); main.Children.Add(del);

            main.PointerEntered += (s, e) =>
            {
                if (dragging) return;
                Anim(hover, OpacityProperty, 1, 90); Anim(del, OpacityProperty, 1, 90);
                if (clockIcon != null) Anim(clockIcon, OpacityProperty, 1, 90);
                if (etag is Grid) Anim(etag, OpacityProperty, 1, 90);
            };
            main.PointerExited += (s, e) =>
            {
                if (dragging) return;
                Anim(hover, OpacityProperty, 0, 180); Anim(del, OpacityProperty, 0, 180);
                if (clockIcon != null && whenOpenId != t.Id) Anim(clockIcon, OpacityProperty, 0, 180);
                if (etag is Grid) Anim(etag, OpacityProperty, 0, 180);
            };

            box.OnLeftDown((s, e) =>
            {
                e.Handled = true;
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
                tick.StrokeDashArray = new AvaloniaList<double> { 9, 9 };
                tick.StrokeDashOffset = 9; tick.Opacity = 1;
                Anim(tick, Shape.StrokeDashOffsetProperty, 0, 240, delay: 90);
                label.TextDecorations = TextDecorations.Strikethrough;
                Anim(label, OpacityProperty, 0.45, 260);
                pending++;
                After(520, () => Collapse(wrap, 220));
            });

            del.OnLeftDown((s, e) =>
            {
                e.Handled = true;
                data.Tasks.Remove(t);
                if (whenOpenId == t.Id) whenOpenId = null;
                Save(); UpdateHeader();
                main.IsHitTestVisible = false;
                pending++;
                Collapse(wrap, 180);
            });

            if (!t.Done)
            {
                main.OnLeftDown((s, e) =>
                {
                    e.Handled = true;
                    dragRow = row; dragging = false; dragOnLabel = label.IsPointerOver;
                    dragStart = e.GetPosition(list);
                    main.CaptureMouse();
                });
                main.PointerMoved += (s, e) =>
                {
                    if (dragRow != row || !main.IsCaptured()) return;
                    double dy = e.GetPosition(list).Y - dragStart.Y;
                    if (!dragging && Math.Abs(dy) > 5 && rows.Count > 1 && commitEdit == null) BeginDrag();
                    if (dragging) UpdateDrag(dy);
                };
                main.OnLeftUp((s, e) =>
                {
                    if (dragRow != row) return;
                    bool wasDrag = dragging, onLabel = dragOnLabel;
                    if (wasDrag) EndDrag();
                    dragRow = null;
                    main.ReleaseMouseCapture();
                    if (!wasDrag && onLabel) BeginEdit(row, label);
                });
                main.PointerCaptureLost += (s, e) => { if (dragRow == row && dragging) { dragging = false; dragRow = null; Rebuild(); } };

                if (whenOpenId == t.Id) wrap.Children.Add(WhenStrip(t));
            }
            return row;
        }

        void Collapse(Control el, int ms)
        {
            Anim(el, OpacityProperty, 0, ms);
            Anim(el, HeightProperty, 0, ms, from: el.Bounds.Height, done: () => { if (--pending <= 0) { pending = 0; Rebuild(); } });
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
                double h = w.Bounds.Height;
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
            label.Opacity = 0; label.IsHitTestVisible = false;
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
                Dispatcher.UIThread.Post(new Action(Rebuild));
            };
            commitEdit = () => finish(true);
            tb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { finish(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { finish(false); e.Handled = true; }
            };
            tb.LostFocus += (s, e) => finish(true);
            Activate(); tb.Focus(); tb.CaretIndex = tb.Text.Length;
        }

        Control WhenStrip(TaskItem t)
        {
            var strip = new WrapPanel { Margin = new Thickness(RowOver + Sp(18, 15) + Sp(10, 8), 0, RowOver, Sp(8, 4)) }; // under the title
            Action<DateTime> setDate = d =>
            {
                var old = t.DueAt;
                t.SetDue(t.HasTime && old != null ? d + old.Value.TimeOfDay : d, t.HasTime);
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
            tb.TextChanged += (s, e) => { hint.IsVisible = tb.Text.Length == 0 ? true : false; tb.Foreground = T.Text; };
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

        Control Pill(string text, Action click)
        {
            var label = new TextBlock { Text = text, FontSize = Z(11.5), Foreground = T.Text, Padding = new Thickness(0, 0, 1, 0) }; // 1 px of slack: layout rounding otherwise clips the last letter
            var b = new PillBorder { Background = T.Raised, Padding = Slim ? new Thickness(9, 3, 8, 4) : new Thickness(11, 4, 10, 5), Margin = new Thickness(0, 0, 6, 6), Child = label, Cursor = Cursors.Hand };
            b.PointerEntered += (s, e) => b.Background = T.Ctl;
            b.PointerExited += (s, e) => b.Background = T.Raised;
            b.OnLeftDown((s, e) => { e.Handled = true; click(); });
            return b;
        }

        // ------------------------------------------------------------ drag to reorder

        void BeginDrag()
        {
            dragging = true;
            if (whenOpenId != null) { /* keep layout stable: strips stay where they are */ }
            dIndex = rows.IndexOf(dragRow);
            dTops = rows.Select(r => r.Wrap.Tp(new Point(0, 0), list).Y).ToArray();
            dHeights = rows.Select(r => r.Wrap.Bounds.Height).ToArray();
            dTargets = new double[rows.Count];
            dragRow.Wrap.ZIndex = 5;
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

        static bool Busy() { return Os.Busy(); }

        void MouseTick()
        {
            Point p;
            if (demoRun) return; // a test render: the real pointer neither opens nor hides the panel
            if (full) return; // Full Mode stays until closed; the hot corner is idle meanwhile
            p = Os.Cursor();
            var sb = Wpf.ScreenBounds;
            bool inCorner = (OnRight ? p.X >= sb.Right - 4 && p.X < sb.Right : p.X >= sb.Left && p.X <= sb.Left + 3)
                         && (OnTop ? p.Y >= sb.Top && p.Y <= sb.Top + 3 : p.Y >= sb.Bottom - 4 && p.Y < sb.Bottom);
            bool btn = Os.LeftButton();
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

        bool InKeepZone(Point p)
        {
            try
            {
                var a = card.Pts(new Point(0, 0));
                var b = card.Pts(new Point(card.Bounds.Width, card.Bounds.Height));
                double m = 18 * (b.X - a.X) / Math.Max(1, card.Bounds.Width);
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
            Anim(card, OpacityProperty, 1, 160);
            Anim(cardShift, TranslateTransform.XProperty, 0, 320, from: OnRight ? 18 : -18, ease: Out);
            Anim(cardShift, TranslateTransform.YProperty, 0, 320, from: OnTop ? -12 : 12, ease: Out);
            Anim(cardScale, ScaleTransform.ScaleXProperty, 1, 320, from: 0.955, ease: Out);
            Anim(cardScale, ScaleTransform.ScaleYProperty, 1, 320, from: 0.955, ease: Out);
            int i = 0;
            foreach (Control el in list.Children)
            {
                if (i >= 14) break;
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
            Anim(cardShift, TranslateTransform.XProperty, OnRight ? 10 : -10, 140);
            Anim(cardShift, TranslateTransform.YProperty, OnTop ? -6 : 6, 140);
            Anim(card, OpacityProperty, 0, 140, done: () => { if (gen == g) Hide(); });
        }

        // Test hook (only with CORNER_DATA set): fills a throwaway list and renders the card to a PNG.
        public void Demo(string path, string mode)
        {
            if (mode.StartsWith("onboard")) { DemoOnboard(path, mode.Length > 7 ? mode[7] - '0' : 0); return; } // the welcome window (Onboard.cs)
            if (mode == "rise" || mode == "dip") { DemoToast(path, mode == "dip"); return; } // the energy notice where it would show (Toast.cs)
            if (mode == "riselive" || mode == "diplive") { ShowNudge(mode == "diplive" ? DipLine() : RiseLine()); After(ToastMs + 1500, () => { HideTray(); Wpf.Quit(); }); return; } // the same, on the real screen
            foreach (var s in new[] { "Book flights", "Finish the quarterly report and send it to the whole team friday", "Buy milk", "Call Sam tomorrow 9am", "Water the plants today", "Stretch in 0 min" }) { input.Text = s; AddTask(); }
            data.Tasks[5].Done = true; data.Tasks[5].DoneAt = "x"; data.DoneOpen = true; whenOpenId = data.Tasks[2].Id;
            data.Tasks[3].SetDue(DateTime.Today.AddDays(-1), false);
            input.Text = "Dentist nov 30 at 2pm";
            // "rest…" modes keep the corner panel as it opens (the next task alone); "resthover" then brings the rest in, as the pointer would
            bool rest = mode.StartsWith("rest"); if (rest && mode.Length > 4) mode = mode.Substring(4);
            demoRun = true;
            DemoSeed(mode);
            ShowPanel(600000);
            if (!rest) Reveal(); else if (mode == "hover") After(500, Reveal);
            DemoAfterShow(mode);
            After(mode == "drag" ? 8000 : 1500, () =>
            {
                var root = (Grid)Content;
                root.Background = new SolidColorBrush(Color.FromRgb(185, 177, 151)); // a sand backdrop for the test renders
                root.UpdateLayout();
                SavePng(root, path, Math.Min(root.Bounds.Height, 900));
                HideTray();
                Wpf.Quit();
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
            Os.Notify(n == 1 ? "Reminder" : n + " reminders", hit.Title);
            if (Busy() || paused) return;
            flashId = hit.Id;
            if (shown) Rebuild(); else ShowPanel(9000);
        }
    }
}
