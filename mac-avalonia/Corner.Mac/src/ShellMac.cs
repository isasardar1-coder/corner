// The parts of Shell that the Mac does its own way: the menu-bar icon and its menu (the Windows tray), window
// position in device-independent units, the zoom host around the card, and saving a test render as a PNG.
using System;
using System.Linq;
using IO = System.IO;

namespace Corner
{
    partial class Shell
    {
        TrayIcon tray;
        NativeMenuItem autoItem;
        NativeMenu sizeMenu, cornerMenu;
        static readonly double[] Sizes = { 0.8, 0.9, 1.0, 1.15, 1.3 };
        static readonly string[] CornerNames = { "Top right", "Top left", "Bottom right", "Bottom left" };

        /// The card sits in this, which applies the text-size zoom to its layout (WPF's LayoutTransform).
        LayoutTransformControl cardHost;

        // WPF's Left and Top, in device-independent units; Avalonia positions windows in pixels.
        double left, top;
        public double Left { get { return left; } set { left = value; Wpf.MoveTo(this, left, top); } }
        public double Top { get { return top; } set { top = value; Wpf.MoveTo(this, left, top); } }

        /// The card's place in the window is set on the card (as in WPF); the zoom host is what the window lays out.
        void SyncHost()
        {
            if (cardHost == null) return;
            cardHost.HorizontalAlignment = card.HorizontalAlignment; cardHost.VerticalAlignment = card.VerticalAlignment;
            cardHost.Margin = card.Margin;
            card.Margin = new Thickness(0);
        }

        void InitTray()
        {
            var menu = new NativeMenu();
            menu.Add(Item("Show list", Wake));
            menu.Add(Item("View Full Mode", EnterFull));
            menu.Add(new NativeMenuItemSeparator());

            cornerMenu = new NativeMenu();
            for (int i = 0; i < CornerNames.Length; i++)
            {
                int v = i;
                var it = Item(CornerNames[i], () =>
                {
                    corner = v; Settings.Set("Corner", v); Settings.Set("Moved", 0);
                    UpdateTrayMenu();
                    if (shown) Place();
                    Wake();
                });
                it.ToggleType = NativeMenuItemToggleType.Radio;
                cornerMenu.Add(it);
            }
            menu.Add(new NativeMenuItem("Opens from") { Menu = cornerMenu });

            sizeMenu = new NativeMenu();
            foreach (var p in Sizes)
            {
                double v = p;
                var it = Item((int)Math.Round(v * 100) + "%", () => { SetScale(v, true); Wake(); });
                it.ToggleType = NativeMenuItemToggleType.Radio;
                sizeMenu.Add(it);
            }
            menu.Add(new NativeMenuItem("Text size") { Menu = sizeMenu });

            autoItem = Item("Open at login", () => { Autostart.Set(!Autostart.Enabled); UpdateTrayMenu(); });
            autoItem.ToggleType = NativeMenuItemToggleType.CheckBox;
            menu.Add(autoItem);
            menu.Add(Item("Reset position", () => { ResetPosition(); Wake(); }));
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(Item("Restart", () => { quitting = true; HideTray(); Program.Restart(); }));
            menu.Add(Item("Quit Corner", () => { quitting = true; HideTray(); Wpf.Quit(); }));
            menu.NeedsUpdate += (s, e) => UpdateTrayMenu(); // the same settings change inside the panel: read them fresh

            tray = new TrayIcon { ToolTipText = "Corner", Menu = menu };
            try
            {
                using (var st = Avalonia.Platform.AssetLoader.Open(new Uri(Os.Mac ? "avares://Corner/Assets/tray.png" : "avares://Corner/Assets/icon.png")))
                    tray.Icon = new WindowIcon(st);
            }
            catch (Exception ex) { Store.Log(ex); }
            if (Os.Mac) MacOSProperties.SetIsTemplateIcon(tray, true); // drawn black or white to suit the menu bar
            tray.Clicked += (s, e) => Wake();
            TrayIcon.SetIcons(Application.Current, new TrayIcons { tray });
            UpdateTrayMenu();
        }

        static NativeMenuItem Item(string text, Action click)
        {
            var it = new NativeMenuItem(text);
            it.Click += (s, e) => click();
            return it;
        }

        void UpdateTrayMenu()
        {
            if (sizeMenu == null) return;
            for (int i = 0; i < sizeMenu.Items.Count; i++) ((NativeMenuItem)sizeMenu.Items[i]).IsChecked = Math.Abs(Sizes[i] - CurrentScale) < 0.001;
            for (int i = 0; i < cornerMenu.Items.Count; i++) ((NativeMenuItem)cornerMenu.Items[i]).IsChecked = corner == i;
            autoItem.IsChecked = Autostart.Enabled;
        }

        void TrayTip(string text) { if (tray != null) tray.ToolTipText = text; }
        void HideTray() { if (tray != null) tray.IsVisible = false; }

        /// A test render: the element at twice its size, as the Windows build's RenderTargetBitmap does.
        static void SavePng(Control root, string path, double height)
        {
            try
            {
                var size = new PixelSize((int)Math.Ceiling(root.Bounds.Width * 2), (int)Math.Ceiling(Math.Max(1, height) * 2));
                using (var rtb = new RenderTargetBitmap(size, new Vector(192, 192)))
                {
                    rtb.Render(root);
                    rtb.Save(path);
                }
            }
            catch (Exception ex) { Store.Log(ex); }
        }
    }
}
