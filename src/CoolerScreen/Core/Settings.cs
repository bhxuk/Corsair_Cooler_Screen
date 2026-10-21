using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoolerScreen.Render;

namespace CoolerScreen.Core
{
    public sealed class PlaylistItem
    {
        public string ScreenId { get; set; } = "";
        public int Seconds { get; set; } = 30;
    }

    /// <summary>Show a warning screen when a reading goes too high.</summary>
    public sealed class AlertRule
    {
        public bool Enabled { get; set; } = true;
        public SensorId Sensor { get; set; } = SensorId.CpuTemp;
        public double Above { get; set; } = 90;
        public int ForSeconds { get; set; } = 5;
    }

    /// <summary>Show a screen while an app or game is running.</summary>
    public sealed class AppRule
    {
        public bool Enabled { get; set; } = true;
        public string Process { get; set; } = "";
        public string ScreenId { get; set; } = "";
    }

    public sealed class NightRule
    {
        public bool Enabled { get; set; }
        public string From { get; set; } = "23:00";
        public string To { get; set; } = "07:00";
        /// <summary>0 turns the screen off.</summary>
        public int Brightness { get; set; } = 15;
    }

    public sealed class LockRule
    {
        public bool Enabled { get; set; }
        /// <summary>Empty turns the screen off while locked.</summary>
        public string ScreenId { get; set; } = "";
    }

    public sealed class AppSettings
    {
        public int Version { get; set; } = 1;
        public bool FirstRun { get; set; } = true;

        // screens
        public List<ScreenSpec> Screens { get; set; } = new List<ScreenSpec>();
        public string ActiveId { get; set; } = "";

        // playlist
        public bool PlaylistOn { get; set; }
        public bool PlaylistShuffle { get; set; }
        public List<PlaylistItem> Playlist { get; set; } = new List<PlaylistItem>();

        // automations
        public List<AlertRule> Alerts { get; set; } = new List<AlertRule> { new AlertRule() };
        public List<AppRule> AppRules { get; set; } = new List<AppRule>();
        public NightRule Night { get; set; } = new NightRule();
        public LockRule Lock { get; set; } = new LockRule();

        // the cooler
        public int Brightness { get; set; } = 100;
        public int Rotation { get; set; }
        public int Fps { get; set; } = 20;
        public int Quality { get; set; } = 85;
        public bool HandBackOnExit { get; set; } = true;
        public bool ShowWhileEditing { get; set; } = true;

        // the app
        public bool StartWithWindows { get; set; } = true;
        public bool Fahrenheit { get; set; }
        public bool UseSector { get; set; } = true;
        public string Theme { get; set; } = "Dark";
        public string Accent { get; set; } = "Teal";
        public bool CheckForUpdates { get; set; } = true;
        public bool AutoUpdate { get; set; }
    }

    /// <summary>Loads and saves the settings file, and knows where Cooler Screen keeps its files.</summary>
    public static class Settings
    {
        public static AppSettings Current { get; private set; } = new AppSettings();

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoolerScreen");
        public static string MediaFolder => Path.Combine(Folder, "Media");
        public static string FilePath => Path.Combine(Folder, "settings.json");

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly object Gate = new object();

        public static event Action Changed;

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                    if (s != null) Current = s;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Settings unreadable, starting fresh: " + ex.Message);
                try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
                Current = new AppSettings();
            }
            Normalise();
        }

        /// <summary>Fills anything missing so the rest of the app can rely on it.</summary>
        private static void Normalise()
        {
            var s = Current;
            s.Screens ??= new List<ScreenSpec>();
            if (s.Screens.Count == 0) s.Screens.AddRange(Templates.Starter());
            if (s.Screens.All(x => x.Id != s.ActiveId)) s.ActiveId = s.Screens[0].Id;
            s.Playlist ??= new List<PlaylistItem>();
            s.Playlist.RemoveAll(p => s.Screens.All(x => x.Id != p.ScreenId));
            s.Alerts ??= new List<AlertRule>();
            s.AppRules ??= new List<AppRule>();
            s.Night ??= new NightRule();
            s.Lock ??= new LockRule();
            foreach (var sc in s.Screens) sc.Sensors ??= new List<SensorId> { SensorId.CpuTemp };
            s.Fps = Math.Max(5, Math.Min(30, s.Fps));
            s.Quality = Math.Max(50, Math.Min(95, s.Quality));
            s.Brightness = Math.Max(0, Math.Min(100, s.Brightness));
        }

        public static void Save()
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    string tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
                    File.Move(tmp, FilePath, true);
                }
                catch (Exception ex) { Log.Write("Couldn't save settings: " + ex.Message); }
            }
            try { Changed?.Invoke(); } catch { }
        }

        public static ScreenSpec Screen(string id) => Current.Screens.FirstOrDefault(s => s.Id == id);

        public static ScreenSpec Active => Screen(Current.ActiveId) ?? Current.Screens.FirstOrDefault();

        /// <summary>Copies a picture into Cooler Screen's own folder so the screen keeps working if the original moves.</summary>
        public static string ImportMedia(string source)
        {
            try
            {
                Directory.CreateDirectory(MediaFolder);
                string full = Path.GetFullPath(source);
                if (full.StartsWith(Path.GetFullPath(MediaFolder), StringComparison.OrdinalIgnoreCase)) return full;
                string name = Path.GetFileNameWithoutExtension(source);
                string ext = Path.GetExtension(source).ToLowerInvariant();
                string dest = Path.Combine(MediaFolder, name + ext);
                int n = 2;
                while (File.Exists(dest))
                {
                    if (new FileInfo(dest).Length == new FileInfo(source).Length) return dest;   // same file already copied
                    dest = Path.Combine(MediaFolder, name + " (" + n++ + ")" + ext);
                }
                File.Copy(source, dest);
                return dest;
            }
            catch (Exception ex)
            {
                Log.Write("Couldn't copy " + source + ": " + ex.Message);
                return source;
            }
        }
    }

    public static class Log
    {
        private static readonly object Gate = new object();

        public static string FilePath => Path.Combine(Settings.Folder, "coolerscreen.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Settings.Folder);
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000) File.Delete(FilePath);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
