// Ported from corner/src/Toast.cs (WPF) to Avalonia: tools/port_ui.py, then fixed by hand.
using System;
using IO = System.IO;

namespace Corner
{
    partial class Shell
    {
        const double ToastM = 28;   // room round the card for its shadow
        const int ToastMs = 13000;  // how long it stays; longer while the pointer is on it
        Window toast;

        /// Shows the notice: one line, in bold. Click it, or wait, to dismiss.
        void ShowToast(string line)
        {
            CloseToast(false);
            if (T.Glass && !IsVisible) GrabBackdrop(); // a fresh picture of what is behind (Glass.cs)

            double z = scale, w = CardW;
            var bg = new Border { CornerRadius = new CornerRadius(18), Background = T.Bg, BorderBrush = T.Edge, BorderThickness = new Thickness(1),
                Effect = new DropShadowDirectionEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Color = T.Glass ? T.GlassShadow : Color.FromRgb(60, 54, 34), Opacity = T.Glass ? T.GlassShadowOpacity : 0.3 } };
            var cardGrid = new Grid { Width = w, Cursor = Cursors.Hand };
            cardGrid.Children.Add(bg);
            if (T.Glass)
            {
                bg.Background = T.GlassBase;
                bg.BorderThickness = new Thickness(0);
                cardGrid.Children.Add(new Border { CornerRadius = new CornerRadius(18), Background = T.GlassTint, BorderBrush = T.GlassRim, BorderThickness = new Thickness(1), IsHitTestVisible = false,
                    Child = new Border { Height = T.GlassSheenHeight, VerticalAlignment = VerticalAlignment.Top, Background = T.GlassSheen, CornerRadius = new CornerRadius(17, 17, 0, 0) } });
            }

            var stack = new Grid { Margin = new Thickness(18, 16, 14, 17) };
            stack.ColumnDefinitions.Add(new ColumnDefinition()); stack.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            stack.Children.Add(new TextBlock { Text = line, FontSize = Hd(15.5), FontWeight = FontWeight.SemiBold, Foreground = T.Text, TextWrapping = TextWrapping.Wrap, LineHeight = Hd(15.5) * 1.3 });
            var x = new Path { Data = Geometry.Parse("M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5"), Stroke = T.Muted, StrokeThickness = 1.4, StrokeLineCap = PenLineCap.Round,
                Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 5, 2, 0) };
            Grid.SetColumn(x, 1); stack.Children.Add(x);
            cardGrid.Children.Add(stack);

            var shift = new TranslateTransform(OnRight ? 14 : -14, 0);
            var root = new Grid { Background = Brushes.Transparent, RenderTransform = shift, Opacity = 0 };
            TextElement.SetFontFamily(root, UiFont ?? new FontFamily("Segoe UI"));
            root.Children.Add(new LayoutTransformControl { LayoutTransform = new ScaleTransform(z, z), Margin = new Thickness(ToastM), Child = cardGrid });
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double ww = root.DesiredSize.Width, wh = root.DesiredSize.Height, cw = w * z, ch = wh - 2 * ToastM;

            // where the panel's card sits: its corner, or where it was dragged to (as Place and PlaceMoved do)
            var wa = Wpf.WorkArea;
            double pw = CardW * scale + Gap + Pad, pl = OnRight ? wa.Right - pw : wa.Left, pt = wa.Top;
            if (Settings.Get("Moved", 0) > 0.5)
            {
                pl = Math.Max(wa.Left - 20, Math.Min(wa.Right - pw + 20, Settings.Get("PosX", pl)));
                double lo = OnTop ? wa.Top : wa.Top - (wa.Height - 240), hi = OnTop ? wa.Top + wa.Height - 240 : wa.Top;
                pt = Math.Max(lo, Math.Min(hi, Settings.Get("PosY", pt)));
            }
            double cx = OnRight ? pl + pw - Gap - cw : pl + Gap;
            double cy = OnTop ? pt + Gap : pt + wa.Height - Gap - ch;
            if (shown && !full && card.Bounds.Height > 0)
            {   // the panel is open: sit just past it, not on it
                try
                {
                    const double k = 1; // Pts is already in device-independent units
                    var a = card.Pts(new Point(0, 0)); var b = card.Pts(new Point(card.Bounds.Width, card.Bounds.Height));
                    cy = OnTop ? b.Y * k + 10 : a.Y * k - 10 - ch;
                }
                catch { }
            }
            cx = Math.Max(wa.Left + 4, Math.Min(wa.Right - cw - 4, cx));
            cy = Math.Max(wa.Top + 4, Math.Min(wa.Bottom - ch - 4, cy));

            var win = new FloatWindow
            {
                FontFamily = UiFont,
                Width = ww, Height = wh, Left = cx - ToastM * z, Top = cy - ToastM * z, Content = root, Title = "Corner notice"
            };
            toast = win;
            if (T.Glass) GlassWindow(win, 18 * z, ToastM * z);
            win.ContentRendered += (s, e) =>
            {
                root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = Out });
                shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = Out });
            };
            root.OnLeftUp((s, e) => CloseToast(true));
            win.Show();
            if (!demoRun) Ping();
            Action wait = null;
            wait = () => { if (toast != win) return; if (root.IsPointerOver) After(3000, wait); else CloseToast(true); };
            After(ToastMs, wait);
        }

        /// The card's sound: "ping.mp3" or "ping.wav" beside Corner.exe if there is one, else Windows' own notification
        /// sound. The player is kept in a field: one that is only a local can be collected before it has played.
        static void Ping()
        {
            try
            {
                string dir = AppContext.BaseDirectory, file = null;
                foreach (var name in new[] { "ping.mp3", "ping.wav", "ping.m4a", "ping.aiff" })
                    if (IO.File.Exists(IO.Path.Combine(dir, name))) { file = IO.Path.Combine(dir, name); break; }
                if (file == null) file = Os.Mac ? "/System/Library/Sounds/Glass.aiff" : IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Media\Windows Notify System Generic.wav");
                Os.PlaySound(Louder(file), 1);
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// A copy of a 16-bit PCM .wav raised until its loudest moment is just under full scale (Windows' own sound
        /// peaks near a third of it), kept in the temp folder. Anything else, or anything that goes wrong: the file as it is.
        static string Louder(string wav)
        {
            if (!wav.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return wav;
            try
            {
                var src = new IO.FileInfo(wav);
                string outPath = IO.Path.Combine(IO.Path.GetTempPath(), "corner-ping-" + (src.FullName.ToLowerInvariant().GetHashCode() ^ src.Length.GetHashCode() ^ src.LastWriteTimeUtc.Ticks.GetHashCode()).ToString("x") + ".wav");
                if (IO.File.Exists(outPath)) return outPath;
                byte[] b = IO.File.ReadAllBytes(wav);
                if (b.Length < 44 || System.Text.Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || System.Text.Encoding.ASCII.GetString(b, 8, 4) != "WAVE") return wav;
                int pos = 12, bits = 0, fmt = 0, dataAt = -1, dataLen = 0;
                while (pos + 8 <= b.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(b, pos, 4); int len = BitConverter.ToInt32(b, pos + 4);
                    if (id == "fmt ") { fmt = BitConverter.ToInt16(b, pos + 8); bits = BitConverter.ToInt16(b, pos + 22); }
                    if (id == "data") { dataAt = pos + 8; dataLen = Math.Min(len, b.Length - dataAt); break; }
                    pos += 8 + len + (len & 1);
                }
                if ((fmt != 1 && fmt != -2) || (bits != 16 && bits != 24) || dataAt < 0) return wav; // PCM (or extensible), 16 or 24 bit
                int n = bits / 8, full = bits == 16 ? 32767 : 8388607, end = dataAt + dataLen - n + 1;
                Func<int, int> read = i => n == 2 ? BitConverter.ToInt16(b, i) : ((b[i] | (b[i + 1] << 8) | (b[i + 2] << 16)) << 8) >> 8;
                int peak = 1;
                for (int i = dataAt; i < end; i += n) peak = Math.Max(peak, Math.Abs(read(i)));
                double gain = Math.Min(6, full * 0.946 / peak); // to about -0.5 dB
                if (gain < 1.1) return wav;
                for (int i = dataAt; i < end; i += n)
                {
                    int v = (int)Math.Round(read(i) * gain);
                    v = Math.Max(-full - 1, Math.Min(full, v));
                    for (int j = 0; j < n; j++) b[i + j] = (byte)((v >> (8 * j)) & 0xFF);
                }
                IO.File.WriteAllBytes(outPath, b);
                return outPath;
            }
            catch (Exception ex) { Store.Log(ex); return wav; }
        }

        void CloseToast(bool fade)
        {
            var win = toast; toast = null;
            if (win == null) return;
            if (!fade) { win.Close(); return; }
            var root = (Control)win.Content;
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            a.Completed += (s, e) => win.Close();
            root.BeginAnimation(OpacityProperty, a);
        }

        /// Test render (mode "rise" or "dip"): the work area with the notice where it would be, saved to `path`.
        /// It uses the longest line, so the render shows the tallest the card gets.
        void DemoToast(string path, bool dip)
        {
            demoRun = true;
            var lines = dip ? DipLines : RiseLines;
            string longest = lines[0]; foreach (var l in lines) if (l.Length > longest.Length) longest = l;
            ShowToast(longest);
            After(1200, () =>
            {
                var win = toast; if (win == null) return;
                var wa = Wpf.WorkArea;
                var content = (Control)win.Content; win.Content = null;
                var screen = new Canvas { Width = wa.Width, Height = wa.Height, Background = new SolidColorBrush(Color.FromRgb(185, 177, 151)) };
                Canvas.SetLeft(content, ((FloatWindow)win).Left - wa.Left); Canvas.SetTop(content, ((FloatWindow)win).Top - wa.Top);
                screen.Children.Add(content);
                screen.Measure(new Size(wa.Width, wa.Height)); screen.Arrange(new Rect(0, 0, wa.Width, wa.Height)); screen.UpdateLayout();
                SavePng(screen, path, wa.Height);
                win.Close(); HideTray();
                Wpf.Quit();
            });
        }
    }
}
