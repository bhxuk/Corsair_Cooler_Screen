using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CoolerScreen.Core;
using CoolerScreen.Device;
using CoolerScreen.Render;
using CoolerScreen.Services;
using SkiaSharp;

namespace CoolerScreen.Engine
{
    /// <summary>
    /// Decides what the cooler shows right now, draws it and sends it.
    /// Order of importance: test pattern, too-hot alert, the screen being edited, locked PC,
    /// an app that's running, the playlist, then the chosen screen.
    /// </summary>
    public sealed class DisplayEngine : IDisposable
    {
        private readonly DeviceService _device;
        private readonly SensorService _sensors;
        private readonly MediaService _media;
        private readonly WeatherService _weather;
        private readonly Renderer _renderer;
        private readonly object _frameGate = new object();
        private Thread _thread;
        private volatile bool _running;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        public LiveData Live { get; }
        public MediaCache Cache { get; }

        /// <summary>The screen open in the editor, shown live on the cooler while it's set.</summary>
        public ScreenSpec Editing { get; set; }

        /// <summary>Stops sending and gives the screen back to the cooler.</summary>
        public bool Paused { get; private set; }

        public bool Locked { get; set; }

        /// <summary>What's showing and why, for the app's status line.</summary>
        public string NowShowing { get; private set; } = "";
        public string Reason { get; private set; } = "";

        // The latest frame as BGRA pixels, for the preview in the app.
        private byte[] _pixels;
        private long _frameVersion;
        public long FrameVersion => Interlocked.Read(ref _frameVersion);

        private DateTime _testUntil = DateTime.MinValue;
        private string _shownKey = "";
        private readonly Stopwatch _screenClock = Stopwatch.StartNew();
        private byte[] _lastJpeg;
        private DateTime _lastSend = DateTime.MinValue;

        // playlist
        private int _playIndex;
        private DateTime _playSwitch = DateTime.MinValue;
        private readonly Random _rng = new Random();

        // alerts: when each rule first went over its limit
        private readonly Dictionary<AlertRule, DateTime> _overSince = new Dictionary<AlertRule, DateTime>();
        private AlertRule _activeAlert;

        // app rules
        private DateTime _nextProcessCheck = DateTime.MinValue;
        private HashSet<string> _running_ = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public DisplayEngine(DeviceService device, SensorService sensors, MediaService media, WeatherService weather, LiveData live, MediaCache cache)
        {
            _device = device;
            _sensors = sensors;
            _media = media;
            _weather = weather;
            Live = live;
            Cache = cache;
            _renderer = new Renderer(cache);
            cache.Loaded += () => _wake.Set();
        }

        public Renderer Renderer => _renderer;

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Display", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        /// <summary>Redraw now (after a setting changes) instead of waiting for the next tick.</summary>
        public void Poke() => _wake.Set();

        public void ShowTestPattern(int seconds = 8)
        {
            _testUntil = DateTime.UtcNow.AddSeconds(seconds);
            Poke();
        }

        public void SetPaused(bool paused)
        {
            Paused = paused;
            if (paused) _device.HandBack();
            Poke();
        }

        /// <summary>Copies the latest frame's pixels (480x480 BGRA). Returns false if there isn't one yet.</summary>
        public bool CopyFrame(byte[] into, out long version)
        {
            lock (_frameGate)
            {
                version = _frameVersion;
                if (_pixels == null || into == null || into.Length < _pixels.Length) return false;
                Buffer.BlockCopy(_pixels, 0, into, 0, _pixels.Length);
                return true;
            }
        }

        private void Loop()
        {
            while (_running)
            {
                int wait = 1000;
                try { wait = Frame(); }
                catch (Exception ex)
                {
                    Log.Write("Display: " + ex);
                    wait = 1000;
                }
                _wake.WaitOne(Math.Max(5, wait));
            }
        }

        /// <summary>Draws and sends one frame. Returns how long to wait before the next.</summary>
        private int Frame()
        {
            var st = Settings.Current;
            int brightness = NightBrightness(st);

            if (Paused)
            {
                NowShowing = "Paused";
                Reason = "The cooler shows its own screen";
                using (var msg = _renderer.RenderMessage("Paused", "The cooler is showing its own screen")) SetPreview(msg);
                return 1000;
            }

            var (spec, reason, alert) = Choose(st);
            string key = alert != null ? "alert" : spec == null ? (reason == "test" ? "test" : "off") : spec.Id;
            if (key != _shownKey)
            {
                _shownKey = key;
                _screenClock.Restart();
                _lastJpeg = null;
            }
            double t = _screenClock.Elapsed.TotalMilliseconds;

            // Only fetch music or weather while a screen shows them.
            _media.Wanted = spec?.Widget == WidgetKind.NowPlaying || IsUsedAnywhere(st, WidgetKind.NowPlaying) && st.PlaylistOn;
            if (spec?.Widget == WidgetKind.Weather) _weather.Wanted = ((spec.Place ?? "").Split(',')[0].Trim(), spec.Latitude, spec.Longitude);

            SKImage img;
            bool animated;
            bool off = false;
            if (reason == "test")
            {
                img = _renderer.RenderTestPattern(t, spec?.Font ?? "Segoe UI");
                NowShowing = "Test pattern";
                Reason = "";
                animated = true;
            }
            else if (alert != null)
            {
                img = _renderer.RenderAlert(alert.Sensor, Live.Get(alert.Sensor), Live, t);
                NowShowing = "Too hot: " + Sensors.Name(alert.Sensor);
                Reason = "Alert";
                animated = true;
            }
            else if (spec == null)
            {
                // Locked with "turn the screen off".
                img = _renderer.RenderMessage("", "");
                NowShowing = "Screen off";
                Reason = reason;
                animated = false;
                off = true;
            }
            else
            {
                img = _renderer.Render(spec, Live, t);
                NowShowing = spec.Name;
                Reason = reason;
                animated = spec.IsAnimated(Cache) || Cache.IsLoading(spec.MediaPath);
            }

            using (img)
            {
                SetPreview(img);
                _device.Apply(off ? 0 : (alert != null ? Math.Max(brightness, 60) : brightness), st.Rotation);
                byte[] jpeg = Encode(img, st.Quality);
                bool same = _lastJpeg != null && jpeg.Length == _lastJpeg.Length && jpeg.AsSpan().SequenceEqual(_lastJpeg);
                // Unchanged frames are still re-sent once a second so the cooler knows we're here.
                if (!same || (DateTime.UtcNow - _lastSend).TotalMilliseconds > 950)
                {
                    if (_device.Send(jpeg)) { _lastSend = DateTime.UtcNow; _lastJpeg = jpeg; }
                }
            }

            if (animated) return 1000 / Math.Max(5, Math.Min(30, st.Fps));
            // Wake just after the next whole second, so clocks tick on time.
            var now = DateTime.Now;
            return 1000 - now.Millisecond + 15;
        }

        private static bool IsUsedAnywhere(AppSettings st, WidgetKind kind) => st.Screens.Any(s => s.Widget == kind);

        /// <summary>JPEG small enough to fit the cooler's 255-packet limit.</summary>
        private static byte[] Encode(SKImage img, int quality)
        {
            byte[] jpeg = Renderer.ToJpeg(img, quality);
            int q = quality;
            while (jpeg.Length > 250 * 1016 && q > 50)
            {
                q -= 10;
                jpeg = Renderer.ToJpeg(img, q);
            }
            return jpeg;
        }

        private void SetPreview(SKImage img)
        {
            var info = new SKImageInfo(Renderer.Size, Renderer.Size, SKColorType.Bgra8888, SKAlphaType.Premul);
            lock (_frameGate)
            {
                _pixels ??= new byte[info.BytesSize];
                var pin = System.Runtime.InteropServices.GCHandle.Alloc(_pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { img.ReadPixels(info, pin.AddrOfPinnedObject(), info.RowBytes, 0, 0); }
                finally { pin.Free(); }
                _frameVersion++;
            }
        }

        // ------------------------------------------------------------------ choosing

        private (ScreenSpec spec, string reason, AlertRule alert) Choose(AppSettings st)
        {
            if (DateTime.UtcNow < _testUntil) return (null, "test", null);

            var alert = CheckAlerts(st);
            if (alert != null) return (null, "Alert", alert);

            if (Editing != null && st.ShowWhileEditing) return (Editing, "Editing", null);

            if (Locked && st.Lock.Enabled)
            {
                var ls = Settings.Screen(st.Lock.ScreenId);
                return (ls, "PC locked", null);
            }

            var app = CheckApps(st);
            if (app != null) return app.Value;

            if (st.PlaylistOn && st.Playlist.Count > 0)
            {
                var items = st.Playlist.Where(p => Settings.Screen(p.ScreenId) != null).ToList();
                if (items.Count > 0)
                {
                    if (_playIndex >= items.Count) _playIndex = 0;
                    if (DateTime.UtcNow >= _playSwitch)
                    {
                        if (_playSwitch != DateTime.MinValue)
                            _playIndex = st.PlaylistShuffle && items.Count > 1 ? NextRandom(_playIndex, items.Count) : (_playIndex + 1) % items.Count;
                        _playSwitch = DateTime.UtcNow.AddSeconds(Math.Max(5, items[_playIndex].Seconds));
                    }
                    return (Settings.Screen(items[_playIndex].ScreenId), "Playlist", null);
                }
            }

            return (Settings.Active, "", null);
        }

        private int NextRandom(int current, int count)
        {
            int n;
            do n = _rng.Next(count); while (n == current);
            return n;
        }

        /// <summary>Skips to the next screen in the playlist.</summary>
        public void NextInPlaylist()
        {
            _playSwitch = DateTime.UtcNow;
            Poke();
        }

        private AlertRule CheckAlerts(AppSettings st)
        {
            var now = DateTime.UtcNow;
            AlertRule firing = null;
            foreach (var rule in st.Alerts)
            {
                if (!rule.Enabled) { _overSince.Remove(rule); continue; }
                double? v = Live.Get(rule.Sensor);
                // Once showing, it stays until the reading is 3 below the limit, so it doesn't flicker.
                double limit = rule == _activeAlert ? rule.Above - 3 : rule.Above;
                if (v != null && v.Value >= limit)
                {
                    if (!_overSince.ContainsKey(rule)) _overSince[rule] = now;
                    if (rule == _activeAlert || (now - _overSince[rule]).TotalSeconds >= Math.Max(0, rule.ForSeconds)) firing ??= rule;
                }
                else _overSince.Remove(rule);
            }
            _activeAlert = firing;
            return firing;
        }

        private (ScreenSpec, string, AlertRule)? CheckApps(AppSettings st)
        {
            var rules = st.AppRules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Process) && Settings.Screen(r.ScreenId) != null).ToList();
            if (rules.Count == 0) return null;
            if (DateTime.UtcNow >= _nextProcessCheck)
            {
                _nextProcessCheck = DateTime.UtcNow.AddSeconds(4);
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in Process.GetProcesses())
                {
                    try { set.Add(p.ProcessName); } catch { }
                    p.Dispose();
                }
                _running_ = set;
            }
            foreach (var r in rules)
            {
                string name = r.Process.Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                if (_running_.Contains(name)) return (Settings.Screen(r.ScreenId), name + " is running", null);
            }
            return null;
        }

        /// <summary>Brightness, dimmed (or off) during night hours.</summary>
        private static int NightBrightness(AppSettings st)
        {
            if (!st.Night.Enabled) return st.Brightness;
            if (!TimeSpan.TryParse(st.Night.From, out var from) || !TimeSpan.TryParse(st.Night.To, out var to)) return st.Brightness;
            var now = DateTime.Now.TimeOfDay;
            bool night = from <= to ? now >= from && now < to : now >= from || now < to;
            return night ? Math.Min(st.Brightness, Math.Max(0, st.Night.Brightness)) : st.Brightness;
        }

        public bool IsNight => NightBrightness(Settings.Current) != Settings.Current.Brightness;

        /// <summary>Draws a screen for a thumbnail or the editor preview (not sent to the cooler).</summary>
        public SKImage RenderPreview(ScreenSpec spec, double t = 0)
        {
            lock (_thumbGate)
            {
                _thumbRenderer ??= new Renderer(Cache);
                return _thumbRenderer.Render(spec, Live, t);
            }
        }

        private readonly object _thumbGate = new object();
        private Renderer _thumbRenderer;

        /// <summary>Gives the screen back to the cooler (on exit, if wanted).</summary>
        public void Stop(bool handBack)
        {
            _running = false;
            _wake.Set();
            try { _thread?.Join(1500); } catch { }
            if (handBack) _device.HandBack();
        }

        public void Dispose()
        {
            _running = false;
            _wake.Set();
            _renderer.Dispose();
        }
    }
}
