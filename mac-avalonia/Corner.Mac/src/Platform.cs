// What Corner needs from the operating system that Avalonia does not give: the pointer anywhere on the screen,
// the mouse button and modifier keys outside the window, time since the last input, a sound, a system notice,
// start at login, and where the data lives. One implementation for macOS, one for Windows (used for test renders).
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using IO = System.IO;

namespace Corner
{
    static class Os
    {
        public static readonly bool Mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        /// The data folder: CORNER_DATA when set (tests), else ~/Library/Application Support/Corner on a Mac.
        /// A downloaded .app is read-only (and may run from a random copy), so nothing is kept beside it.
        public static string DataDir()
        {
            string env = Environment.GetEnvironmentVariable("CORNER_DATA");
            if (!string.IsNullOrEmpty(env)) return env;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Mac) return IO.Path.Combine(home, "Library", "Application Support", "Corner");
            return IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Corner-Avalonia");
        }

        /// The pointer, in device-independent units from the top left of the primary screen.
        public static Point Cursor()
        {
            if (Mac)
            {
                var p = Mac_.MouseLocation(); // points, from the bottom left of the primary screen
                return new Point(p.X, Wpf.ScreenBounds.Height - p.Y);
            }
            Win.POINT q; if (!Win.GetCursorPos(out q)) return new Point(-1, -1);
            double k = Wpf.Scaling; return new Point(q.X / k, q.Y / k);
        }

        public static bool LeftButton()
        {
            if (Mac) return (Mac_.PressedButtons() & 1) != 0;
            return (Win.GetAsyncKeyState(1) & 0x8000) != 0;
        }

        /// The key that turns the wheel into text size: Command on a Mac, Ctrl on Windows.
        public static bool ZoomKey()
        {
            if (Mac) return (Mac_.ModifierFlags() & (1UL << 20)) != 0;
            return (Win.GetAsyncKeyState(0x11) & 0x8000) != 0;
        }
        public static string ZoomKeyName { get { return Mac ? "Cmd" : "Ctrl"; } }

        public static double IdleSeconds()
        {
            try
            {
                if (Mac) return Mac_.CGEventSourceSecondsSinceLastEventType(0, uint.MaxValue);
                var li = new Win.LASTINPUTINFO { cbSize = 8 };
                if (!Win.GetLastInputInfo(ref li)) return 0;
                return unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0;
            }
            catch { return 0; }
        }

        /// Presenting or full-screen: no panel and no prompts. macOS has no simple public answer, so never there.
        public static bool Busy()
        {
            if (Mac) return false;
            try { int st; return Win.SHQueryUserNotificationState(out st) == 0 && (st == 2 || st == 3 || st == 4); }
            catch { return false; }
        }

        public static void PlaySound(string file, double volume)
        {
            try
            {
                if (!IO.File.Exists(file)) return;
                if (Mac) Process.Start(new ProcessStartInfo("/usr/bin/afplay", "-v " + volume.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " \"" + file + "\"") { UseShellExecute = false, CreateNoWindow = true });
                else Win.PlaySound(file, IntPtr.Zero, 0x20001); // SND_FILENAME | SND_ASYNC
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// Shows a file selected in Finder (Explorer on Windows), or opens a folder.
        public static void Reveal(string path)
        {
            try
            {
                var psi = new ProcessStartInfo(Mac ? "/usr/bin/open" : "explorer.exe") { UseShellExecute = false };
                if (Mac) { if (IO.File.Exists(path)) psi.ArgumentList.Add("-R"); psi.ArgumentList.Add(path); }
                else psi.Arguments = IO.File.Exists(path) ? "/select,\"" + path + "\"" : "\"" + path + "\"";
                Process.Start(psi);
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// A system notice (Notification Centre on a Mac). Without a signed app it shows under Script Editor.
        public static void Notify(string title, string text)
        {
            if (!Mac) return;
            try
            {
                Func<string, string> q = s => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                var psi = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("-e"); psi.ArgumentList.Add("display notification " + q(text) + " with title " + q(title));
                Process.Start(psi);
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// Floats the window over every Space and over full-screen apps, and keeps it out of Cmd+Tab and the Dock.
        public static void FloatEverywhere(Window w)
        {
            if (!Mac) return;
            try
            {
                var h = w.TryGetPlatformHandle();
                var prop = h == null ? null : h.GetType().GetProperty("NSWindow");
                var ns = prop == null ? IntPtr.Zero : (IntPtr)prop.GetValue(h);
                if (ns == IntPtr.Zero) return;
                // CanJoinAllSpaces | Stationary | IgnoresCycle | FullScreenAuxiliary
                Mac_.Send(ns, "setCollectionBehavior:", 1UL | 16UL | 64UL | 256UL);
                Mac_.Send(ns, "setHidesOnDeactivate:", 0UL);
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        static IntPtr NsWindow(Window w)
        {
            var h = w.TryGetPlatformHandle();
            var prop = h == null ? null : h.GetType().GetProperty("NSWindow");
            return prop == null ? IntPtr.Zero : (IntPtr)prop.GetValue(h);
        }

        /// Rounds a borderless window's corners (its content and frame views clip to the radius).
        public static void RoundWindow(Window w, double radius)
        {
            if (!Mac) return;
            try
            {
                var ns = NsWindow(w); if (ns == IntPtr.Zero) return;
                Mac_.Send(ns, "setOpaque:", 0UL);
                Mac_.Send(ns, "setHasShadow:", 0UL);
                Mac_.Send(ns, "setBackgroundColor:", Mac_.Send(Mac_.Class("NSColor"), "clearColor"));
                var cv = Mac_.Send(ns, "contentView");
                foreach (var v in new[] { cv, cv == IntPtr.Zero ? IntPtr.Zero : Mac_.Send(cv, "superview") })
                {
                    if (v == IntPtr.Zero) continue;
                    Mac_.Send(v, "setWantsLayer:", 1UL);
                    var layer = Mac_.Send(v, "layer"); if (layer == IntPtr.Zero) continue;
                    Mac_.SendD(layer, "setCornerRadius:", radius);
                    Mac_.Send(layer, "setMasksToBounds:", 1UL);
                }
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// Puts `top` directly above `below` (both floating windows).
        public static void OrderAbove(Window top, Window below)
        {
            if (!Mac) { top.Activate(); return; }
            try
            {
                var a = NsWindow(top); var b = NsWindow(below);
                if (a == IntPtr.Zero || b == IntPtr.Zero) return;
                long num = (long)Mac_.SendL(b, "windowNumber");
                Mac_.SendLL(a, "orderWindow:relativeTo:", 1, num); // NSWindowAbove
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        /// No Dock icon (LSUIElement does this inside the .app; this covers a run from the command line).
        public static void Accessory()
        {
            if (!Mac) return;
            try { Mac_.Send(Mac_.Send(Mac_.Class("NSApplication"), "sharedApplication"), "setActivationPolicy:", 1UL); }
            catch (Exception ex) { Store.Log(ex); }
        }

        // ------------------------------------------------------------ objective-c and windows calls

        static class Mac_
        {
            const string Objc = "/usr/lib/libobjc.A.dylib";
            [StructLayout(LayoutKind.Sequential)] public struct CGPoint { public double X, Y; }
            [DllImport(Objc)] static extern IntPtr objc_getClass(string name);
            [DllImport(Objc)] static extern IntPtr sel_registerName(string name);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern CGPoint SendPoint(IntPtr r, IntPtr s);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern ulong SendU(IntPtr r, IntPtr s);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendP(IntPtr r, IntPtr s);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendPU(IntPtr r, IntPtr s, ulong a);
            [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
            public static extern double CGEventSourceSecondsSinceLastEventType(int state, uint type);

            [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendPD(IntPtr r, IntPtr s, double a);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern long SendPL(IntPtr r, IntPtr s);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendPLL(IntPtr r, IntPtr s, long a, long b);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendPP(IntPtr r, IntPtr s, IntPtr a);
            public static void SendD(IntPtr r, string sel, double a) { SendPD(r, sel_registerName(sel), a); }
            public static long SendL(IntPtr r, string sel) { return SendPL(r, sel_registerName(sel)); }
            public static void SendLL(IntPtr r, string sel, long a, long b) { SendPLL(r, sel_registerName(sel), a, b); }
            public static IntPtr Send(IntPtr r, string sel, IntPtr a) { return SendPP(r, sel_registerName(sel), a); }
            public static IntPtr Class(string n) { return objc_getClass(n); }
            public static IntPtr Send(IntPtr r, string sel) { return SendP(r, sel_registerName(sel)); }
            public static IntPtr Send(IntPtr r, string sel, ulong a) { return SendPU(r, sel_registerName(sel), a); }
            public static CGPoint MouseLocation() { return SendPoint(objc_getClass("NSEvent"), sel_registerName("mouseLocation")); }
            public static ulong PressedButtons() { return SendU(objc_getClass("NSEvent"), sel_registerName("pressedMouseButtons")); }
            public static ulong ModifierFlags() { return SendU(objc_getClass("NSEvent"), sel_registerName("modifierFlags")); }
        }

        static class Win
        {
            [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
            [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
            [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
            [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
            [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
            [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
            [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern bool PlaySound(string file, IntPtr mod, uint flags);
        }
    }

    /// Start at login. On a Mac: a LaunchAgent in ~/Library/LaunchAgents that opens the app (it needs no signing).
    static class Autostart
    {
        const string Label = "app.corner.todo";
        static string Plist { get { return IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist"); } }

        /// The .app folder when running from one, else the executable itself.
        static string AppPath
        {
            get
            {
                string exe = Environment.ProcessPath ?? "";
                int i = exe.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
                return i > 0 ? exe.Substring(0, i + 4) : exe;
            }
        }

        public static bool Enabled
        {
            get
            {
                if (!Os.Mac) return false;
                try { return IO.File.Exists(Plist); } catch { return false; }
            }
        }

        public static void Set(bool on)
        {
            if (!Os.Mac) return; // the Windows build of this app is only for test renders
            try
            {
                if (!on) { if (IO.File.Exists(Plist)) IO.File.Delete(Plist); return; }
                string app = AppPath;
                string args = app.EndsWith(".app")
                    ? "<string>/usr/bin/open</string><string>-a</string><string>" + Esc(app) + "</string>"
                    : "<string>" + Esc(app) + "</string>";
                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(Plist));
                IO.File.WriteAllText(Plist,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                    "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                    "<plist version=\"1.0\"><dict>\n" +
                    "  <key>Label</key><string>" + Label + "</string>\n" +
                    "  <key>ProgramArguments</key><array>" + args + "</array>\n" +
                    "  <key>RunAtLoad</key><true/>\n" +
                    "</dict></plist>\n");
            }
            catch (Exception ex) { Store.Log(ex); }
        }

        static string Esc(string s) { return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); }
    }
}
