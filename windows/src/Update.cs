// Updates: Corner looks for a newer release on GitHub a minute after it starts and then every six hours. When there
// is one it downloads Corner-Windows.zip, takes Corner.exe out of it, puts it in place of the running one (Windows
// lets a running exe be renamed, so the old one becomes Corner.old.exe), and restarts as soon as the panel is closed
// and no timer is running. Your notes (tasks.json and the rest), ping.wav and the fonts are never touched.
// Settings > Updates turns this off, and "Check now" asks straight away.

using System;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IO = System.IO;

namespace Corner
{
    static class Updater
    {
        public const string Version = "1.1.5";                   // this build; the release tag is "v" + this
        public const string Repo = "isasardar1-coder/corner";
        const string Asset = "Corner-Windows.zip";
        public const int FirstCheckSec = 60, EveryHours = 6;

        static string Exe { get { return System.Reflection.Assembly.GetEntryAssembly().Location; } }
        static string OldExe { get { return IO.Path.Combine(IO.Path.GetDirectoryName(Exe), "Corner.old.exe"); } }

        /// "v1.2.0" or "1.2" as a Version; null when it is not one.
        public static Version Parse(string tag)
        {
            if (tag == null) return null;
            Version v;
            return System.Version.TryParse(tag.Trim().TrimStart('v', 'V'), out v) ? v : null;
        }

        public static bool IsNewer(string tag) { var v = Parse(tag); return v != null && v > Parse(Version); }

        /// A version as one number for settings.json (which keeps numbers only): 1.2.3 -> 10203.
        public static double Number(string version) { var v = Parse(version); return v == null ? 0 : v.Major * 10000 + v.Minor * 100 + Math.Max(0, v.Build); }

        /// The latest release's tag and the download address of its Windows zip, from GitHub's API.
        /// CORNER_UPDATE_URL points it at another address (a file:// one in tests).
        public static bool Latest(out string tag, out string zipUrl)
        {
            tag = zipUrl = null;
            string url = Environment.GetEnvironmentVariable("CORNER_UPDATE_URL") ?? "https://api.github.com/repos/" + Repo + "/releases/latest";
            string json;
            using (var wc = Client()) json = wc.DownloadString(url);
            return ParseRelease(json, out tag, out zipUrl);
        }

        public static bool ParseRelease(string json, out string tag, out string zipUrl)
        {
            tag = zipUrl = null;
            var t = Regex.Match(json ?? "", "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            var z = Regex.Match(json ?? "", "\"browser_download_url\"\\s*:\\s*\"([^\"]*/" + Regex.Escape(Asset) + ")\"");
            if (!t.Success || !z.Success) return false;
            tag = t.Groups[1].Value; zipUrl = z.Groups[1].Value;
            return true;
        }

        static WebClient Client()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { } // TLS 1.2, which GitHub requires
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "Corner-updater";
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return wc;
        }

        /// Downloads the zip and takes Corner.exe out of it, into the temp folder. Returns its path.
        public static string Fetch(string zipUrl)
        {
            string dir = IO.Path.Combine(IO.Path.GetTempPath(), "corner-update");
            IO.Directory.CreateDirectory(dir);
            string zip = IO.Path.Combine(dir, Asset), exe = IO.Path.Combine(dir, "Corner.exe");
            foreach (var f in new[] { zip, exe }) if (IO.File.Exists(f)) IO.File.Delete(f);
            using (var wc = Client()) wc.DownloadFile(zipUrl, zip);
            using (var a = ZipFile.OpenRead(zip))
            {
                var e = a.Entries.FirstOrDefault(x => string.Equals(x.Name, "Corner.exe", StringComparison.OrdinalIgnoreCase));
                if (e == null) throw new Exception("no Corner.exe in " + Asset);
                e.ExtractToFile(exe, true);
            }
            System.Reflection.AssemblyName.GetAssemblyName(exe); // throws unless it is a .NET program
            return exe;
        }

        /// Puts the new exe where the running one is. The running one is renamed, not deleted (Windows allows that).
        public static void Swap(string newExe)
        {
            string exe = Exe, old = OldExe;
            if (IO.File.Exists(old)) IO.File.Delete(old);
            IO.File.Move(exe, old);
            try { IO.File.Copy(newExe, exe); }
            catch { IO.File.Move(old, exe); throw; } // put the old one back
        }

        /// The previous exe, left behind by the last update; gone once the new one runs.
        public static void CleanUp() { try { if (IO.File.Exists(OldExe)) IO.File.Delete(OldExe); } catch { } }
    }

    partial class Shell
    {
        DispatcherTimer updateTimer;
        string updateReady;   // the version that is in place and waits for a restart
        string updateNote;    // what Settings shows under Updates after a check
        bool updating;

        static bool UpdatesOn { get { return Settings.Get("AutoUpdate", 1) > 0.5; } }

        /// From Start: tidy up after the last update, say so once, and start checking.
        void InitUpdates(bool test)
        {
            Updater.CleanUp();
            double was = Settings.Get("LastVersion", 0), now = Updater.Number(Updater.Version);
            if (!test || Environment.GetEnvironmentVariable("CORNER_UPDATE_URL") != null)
            {
                if (was > 0 && was < now) After(2500, () => ShowNudge("Corner is up to date: version " + Updater.Version + "."));
                if (was != now) Settings.Set("LastVersion", now);
            }
            if (test && Environment.GetEnvironmentVariable("CORNER_UPDATE_URL") == null) return; // test copies never look for updates
            updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(test ? 3 : Updater.FirstCheckSec) }; // a test against CORNER_UPDATE_URL checks at once
            updateTimer.Tick += (s, e) =>
            {
                updateTimer.Interval = TimeSpan.FromHours(Updater.EveryHours);
                if (updateReady != null) { RestartWhenFree(); return; }
                if (UpdatesOn) CheckForUpdate(false);
            };
            updateTimer.Start();
        }

        /// manual: from Settings' "Check now", which also reports "you have the latest".
        void CheckForUpdate(bool manual)
        {
            if (updating || updateReady != null) return;
            updating = true;
            if (manual) { updateNote = "Checking…"; if (settingsOpen) DrawSettings(); }
            Task.Factory.StartNew(() =>
            {
                string tag, url, note = null, ready = null;
                try
                {
                    if (!Updater.Latest(out tag, out url)) note = "Couldn't read the latest release.";
                    else if (!Updater.IsNewer(tag)) note = "You have the latest version.";
                    else
                    {
                        var exe = Updater.Fetch(url);
                        Updater.Swap(exe);
                        ready = tag.TrimStart('v', 'V');
                    }
                }
                catch (Exception ex) { note = "Couldn't check for updates (no connection?)."; Store.Log(ex); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    updating = false;
                    updateNote = ready != null ? "Version " + ready + " is ready; Corner restarts in a moment." : note;
                    if (ready != null)
                    {
                        updateReady = ready;
                        ShowNudge("Corner " + ready + " is ready. It restarts as soon as you're not using it.");
                        if (updateTimer != null) updateTimer.Interval = TimeSpan.FromSeconds(30);
                        After(16000, RestartWhenFree);
                    }
                    if (settingsOpen) DrawSettings();
                }));
            });
        }

        /// Restarts into the new exe when the panel is closed and no task timer is running; otherwise waits.
        void RestartWhenFree()
        {
            if (updateReady == null) return;
            if (shown || full || (timerId != null && (timerRunning || !PriorityMode)) ||(Settings.Get("Onboarded", 1) < 0.5)) { if (updateTimer != null) updateTimer.Interval = TimeSpan.FromSeconds(30); return; }
            try { Save(); } catch { }
            tray.Visible = false;
            Program.Restart();
        }

        /// Settings: the version, "Automatic" on or off, and "Check now".
        void DrawUpdates(System.Windows.Controls.StackPanel card)
        {
            bool on = UpdatesOn;
            SetLine(card, SettingLine("Updates", on ? "Automatic: Corner updates itself" : "Off: Corner doesn't look for updates",
                Switch(on, () => { Settings.Set("AutoUpdate", on ? 0 : 1); DrawSettings(); })));
            SetLine(card, SettingLine("Version " + Updater.Version, updateNote ?? "Checks every " + Updater.EveryHours + " hours",
                Btn(updating ? "Checking…" : "Check now", 1, () => CheckForUpdate(true), 1)));
        }
    }
}
