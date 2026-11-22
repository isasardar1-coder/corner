// Corner - a notice in Corner's own look: a small card that slides in at the corner where the panel opens (or where
// it was dragged to), in the skin in use. Used for the energy-rise reminder (UiSleep.cs) instead of a Windows balloon.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
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
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Color = T.Glass ? T.GlassShadow : Color.FromRgb(60, 54, 34), Opacity = T.Glass ? T.GlassShadowOpacity : 0.3 } };
            var cardGrid = new Grid { Width = w, Margin = new Thickness(ToastM), LayoutTransform = new ScaleTransform(z, z), Cursor = System.Windows.Input.Cursors.Hand };
            cardGrid.Children.Add(bg);
            ImageBrush back = null;
            if (T.Glass)
            {
                if (Backdrop.Image != null) { back = new ImageBrush(Backdrop.Image) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute }; bg.Background = back; }
                else bg.Background = T.GlassBase;
                bg.BorderThickness = new Thickness(0);
                cardGrid.Children.Add(new Border { CornerRadius = new CornerRadius(18), Background = T.GlassTint, BorderBrush = T.GlassRim, BorderThickness = new Thickness(1), IsHitTestVisible = false,
                    Child = new Border { Height = T.GlassSheenHeight, VerticalAlignment = VerticalAlignment.Top, Background = T.GlassSheen, CornerRadius = new CornerRadius(17, 17, 0, 0) } });
            }

            var stack = new Grid { Margin = new Thickness(18, 16, 14, 17) };
            stack.ColumnDefinitions.Add(new ColumnDefinition()); stack.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            stack.Children.Add(new TextBlock { Text = line, FontSize = Hd(15.5), FontWeight = FontWeights.SemiBold, Foreground = T.Text, TextWrapping = TextWrapping.Wrap, LineHeight = Hd(15.5) * 1.3 });
            var x = new Path { Data = Geometry.Parse("M1.5,1.5 L8.5,8.5 M8.5,1.5 L1.5,8.5"), Stroke = T.Muted, StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 5, 2, 0) };
            Grid.SetColumn(x, 1); stack.Children.Add(x);
            cardGrid.Children.Add(stack);

            var shift = new TranslateTransform(OnRight ? 14 : -14, 0);
            var root = new Grid { Background = Brushes.Transparent, RenderTransform = shift, Opacity = 0 };
            TextElement.SetFontFamily(root, UiFont ?? new FontFamily("Segoe UI"));
            root.Children.Add(cardGrid);
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double ww = root.DesiredSize.Width, wh = root.DesiredSize.Height, cw = w * z, ch = wh - 2 * ToastM;

            // where the panel's card sits: its corner, or where it was dragged to (as Place and PlaceMoved do)
            var wa = SystemParameters.WorkArea;
            double pw = CardW * scale + Gap + Pad, pl = OnRight ? wa.Right - pw : wa.Left, pt = wa.Top;
            if (Settings.Get("Moved", 0) > 0.5)
            {
                pl = Math.Max(wa.Left - 20, Math.Min(wa.Right - pw + 20, Settings.Get("PosX", pl)));
                double lo = OnTop ? wa.Top : wa.Top - (wa.Height - 240), hi = OnTop ? wa.Top + wa.Height - 240 : wa.Top;
                pt = Math.Max(lo, Math.Min(hi, Settings.Get("PosY", pt)));
            }
            double cx = OnRight ? pl + pw - Gap - cw : pl + Gap;
            double cy = OnTop ? pt + Gap : pt + wa.Height - Gap - ch;
            if (shown && !full && card.ActualHeight > 0)
            {   // the panel is open: sit just past it, not on it
                try
                {
                    var k = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice.M11;
                    var a = card.PointToScreen(new Point(0, 0)); var b = card.PointToScreen(new Point(card.ActualWidth, card.ActualHeight));
                    cy = OnTop ? b.Y * k + 10 : a.Y * k - 10 - ch;
                }
                catch { }
            }
            cx = Math.Max(wa.Left + 4, Math.Min(wa.Right - cw - 4, cx));
            cy = Math.Max(wa.Top + 4, Math.Min(wa.Bottom - ch - 4, cy));

            var win = toast = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true,
                ShowActivated = false, ResizeMode = ResizeMode.NoResize, FontFamily = UiFont ?? new FontFamily("Segoe UI"),
                Width = ww, Height = wh, Left = cx - ToastM * z, Top = cy - ToastM * z, Content = root, Title = "Corner notice"
            };
            win.SourceInitialized += (s, e) =>
            {   // never takes the focus, never in Alt+Tab
                var h = new WindowInteropHelper(win).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80); // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            };
            win.ContentRendered += (s, e) =>
            {
                if (back != null) try
                {   // the part of the picture that is behind the card
                    var a = cardGrid.PointToScreen(new Point(0, 0)); var b = cardGrid.PointToScreen(new Point(cardGrid.ActualWidth, cardGrid.ActualHeight));
                    back.Viewbox = new Rect((a.X - Backdrop.Left) / Backdrop.K, (a.Y - Backdrop.Top) / Backdrop.K, Math.Max(0.5, (b.X - a.X) / Backdrop.K), Math.Max(0.5, (b.Y - a.Y) / Backdrop.K));
                }
                catch { }
                root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { EasingFunction = Out });
                shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = Out });
            };
            root.MouseLeftButtonUp += (s, e) => CloseToast(true);
            win.Show();
            if (!demoRun) Ping();
            Action wait = null;
            wait = () => { if (toast != win) return; if (root.IsMouseOver) After(3000, wait); else CloseToast(true); };
            After(ToastMs, wait);
        }

        /// The card's sound: "ping.mp3" or "ping.wav" beside Corner.exe if there is one, else Windows' own notification
        /// sound. The player is kept in a field: one that is only a local can be collected before it has played.
        static MediaPlayer pingPlayer;
        static void Ping()
        {
            try
            {
                string exe = IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), file = null;
                foreach (var name in new[] { "ping.mp3", "ping.wav", "ping.m4a", "ping.wma" })
                    if (IO.File.Exists(IO.Path.Combine(exe, name))) { file = IO.Path.Combine(exe, name); break; }
                if (file == null) file = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Media\Windows Notify System Generic.wav");
                if (!IO.File.Exists(file)) { System.Media.SystemSounds.Asterisk.Play(); return; }
                file = Louder(file);
                if (pingPlayer == null) pingPlayer = new MediaPlayer();
                pingPlayer.Open(new Uri(file));
                pingPlayer.Volume = 1;
                pingPlayer.Play();
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
            var root = (UIElement)win.Content;
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
                var wa = SystemParameters.WorkArea;
                double k = 1; try { k = PresentationSource.FromVisual(win).CompositionTarget.TransformToDevice.M11; } catch { }
                var content = (FrameworkElement)win.Content; win.Content = null;
                var screen = new Canvas { Width = wa.Width, Height = wa.Height, Background = new SolidColorBrush(Color.FromRgb(185, 177, 151)) };
                if (T.Glass && Backdrop.Sharp != null)
                    screen.Background = new ImageBrush(Backdrop.Sharp) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = new Rect((wa.Left * k - Backdrop.Left) / 2, (wa.Top * k - Backdrop.Top) / 2, wa.Width * k / 2, wa.Height * k / 2) };
                Canvas.SetLeft(content, win.Left - wa.Left); Canvas.SetTop(content, win.Top - wa.Top);
                screen.Children.Add(content);
                screen.Measure(new Size(wa.Width, wa.Height)); screen.Arrange(new Rect(0, 0, wa.Width, wa.Height)); screen.UpdateLayout();
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(wa.Width * k), (int)(wa.Height * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
                rtb.Render(screen);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using (var fs = IO.File.Create(path)) enc.Save(fs);
                win.Close(); tray.Visible = false;
                Application.Current.Shutdown();
            });
        }
    }
}
