// Corner for macOS: start-up, one copy at a time, and the Avalonia application.
using System;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using IO = System.IO;

namespace Corner
{
    static class Program
    {
        /// A test run: CORNER_DATA was set by whoever started it. Real runs set it themselves to the data folder.
        public static bool Test;
        static IO.FileStream instanceLock;
        static string[] startArgs = new string[0];

        public static void Restart()
        {
            try { instanceLock?.Dispose(); } catch { }
            try
            {
                string exe = Environment.ProcessPath;
                int i = exe == null ? -1 : exe.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
                if (i > 0) System.Diagnostics.Process.Start("/usr/bin/open", "-n \"" + exe.Substring(0, i + 4) + "\"");
                else System.Diagnostics.Process.Start(exe);
            }
            catch (Exception ex) { Store.Log(ex); }
            Wpf.Quit();
        }

        [STAThread]
        static int Main(string[] args)
        {
            startArgs = args;
            Test = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CORNER_DATA"));
            string dir = Os.DataDir();
            Environment.SetEnvironmentVariable("CORNER_DATA", dir); // Store.Dir and the rest of the core read it
            try { IO.Directory.CreateDirectory(dir); } catch { }
            try { instanceLock = new IO.FileStream(IO.Path.Combine(dir, ".corner.lock"), IO.FileMode.OpenOrCreate, IO.FileAccess.ReadWrite, IO.FileShare.None); }
            catch (IO.IOException) { return 0; } // already running for this data folder
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new MacOSPlatformOptions { ShowInDock = false, DisableDefaultApplicationMenuItems = true })
                .LogToTrace();
        }

        public static void Started()
        {
            var args = startArgs;
            Os.Accessory();
            var shell = new Shell();
            Wpf.Main = shell;
            shell.Start(args.Contains("--peek"), Test);
            if (Test && args.Length >= 2 && args[0] == "--demo") shell.Demo(args[1], args.Length > 2 ? args[2] : "tasks");
        }
    }

    class App : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            Styles.Add(Look.Plain());
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            Name = "Corner";
        }

        public override void OnFrameworkInitializationCompleted()
        {
            base.OnFrameworkInitializationCompleted();
            Dispatcher.UIThread.UnhandledException += (s, e) => { Store.Log(e.Exception); e.Handled = true; };
            Program.Started();
        }
    }

    /// The Glass skin's picture of what is behind the panel. The Windows build grabs and blurs the screen; on a Mac
    /// the window asks for the system's own blur instead (Glass.cs), so there is no picture: Image stays null.
    static class Backdrop
    {
        public static Bitmap Image, Sharp;
        public static double Left, Top, K = 8;
        public static void Grab(string fake) { }
    }
}
