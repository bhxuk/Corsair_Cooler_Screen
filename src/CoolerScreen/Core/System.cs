using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CoolerScreen.Core
{
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "CoolerScreen";

        public static void Apply(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled) key.SetValue(Name, "\"" + Environment.ProcessPath + "\" --background");
                else if (key.GetValue(Name) != null) key.DeleteValue(Name);
            }
            catch (Exception ex) { Log.Write("Startup registry failed: " + ex.Message); }
        }
    }

    /// <summary>Checks GitHub for a newer Cooler Screen and installs it.</summary>
    public static class Updates
    {
        public const string Repo = "bhxuk/Corsair_Cooler_Screen";
        public const string SetupName = "CoolerScreen-Setup.exe";
        public static string LatestVersion { get; private set; }
        public static string LatestUrl { get; private set; } = "https://github.com/" + Repo + "/releases/latest";
        public static string LatestSetupUrl { get; private set; }

        public static Version Current => typeof(Updates).Assembly.GetName().Version ?? new Version(1, 0, 0);
        public static string CurrentText => Current.Major + "." + Current.Minor + "." + Math.Max(0, Current.Build);

        public static async Task<bool> Check()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("CoolerScreen/" + CurrentText);
                using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest"));
                string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                LatestUrl = doc.RootElement.GetProperty("html_url").GetString() ?? LatestUrl;
                LatestSetupUrl = null;
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                    foreach (var a in assets.EnumerateArray())
                        if ((a.GetProperty("name").GetString() ?? "").Equals(SetupName, StringComparison.OrdinalIgnoreCase))
                            LatestSetupUrl = a.GetProperty("browser_download_url").GetString();
                LatestVersion = tag.TrimStart('v', 'V');
                return Version.TryParse(LatestVersion, out var v) && v > new Version(Current.Major, Current.Minor, Math.Max(0, Current.Build));
            }
            catch (Exception ex)
            {
                Log.Write("Update check failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Downloads the new Setup.exe and runs it quietly. Setup closes Cooler Screen, updates it and starts it again.</summary>
        public static async Task<bool> DownloadAndInstall(Action<string> status = null)
        {
            try
            {
                if (string.IsNullOrEmpty(LatestSetupUrl) && !await Check()) return false;
                if (string.IsNullOrEmpty(LatestSetupUrl)) return false;
                status?.Invoke("Downloading Cooler Screen " + LatestVersion + "…");
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("CoolerScreen/" + CurrentText);
                var bytes = await http.GetByteArrayAsync(LatestSetupUrl);
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CoolerScreen");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "CoolerScreen-Setup-" + LatestVersion + ".exe");
                await System.IO.File.WriteAllBytesAsync(path, bytes);
                status?.Invoke("Installing…");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Update failed: " + ex.Message);
                status?.Invoke("Couldn't update: " + ex.Message);
                return false;
            }
        }
    }

    public static class Native
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void UseDarkTitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref v, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref v, sizeof(int));
            }
            catch { }
        }

        public static void RoundCorners(IntPtr hwnd)
        {
            try { int v = 2; DwmSetWindowAttribute(hwnd, 33, ref v, sizeof(int)); } catch { }
        }

        public static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open failed: " + ex.Message); }
        }
    }
}
