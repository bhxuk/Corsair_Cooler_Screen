using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CoolerScreen.Core;
using CoolerScreen.Device;
using CoolerScreen.Engine;
using CoolerScreen.Render;
using CoolerScreen.Services;
using Microsoft.Win32;

namespace CoolerScreen
{
    public static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Any(a => a.Equals("--quit", StringComparison.OrdinalIgnoreCase)))
            {
                // Used by Setup: ask a running copy to close properly, and wait for it.
                try
                {
                    using var quit = EventWaitHandle.OpenExisting("CoolerScreen.Quit.bhxuk");
                    quit.Set();
                    using var m = new Mutex(false, "CoolerScreen.SingleInstance.bhxuk");
                    try { if (m.WaitOne(5000)) m.ReleaseMutex(); } catch (AbandonedMutexException) { }
                }
                catch { }
                return 0;
            }

            bool restarting = args.Any(a => a.Equals("--restarting", StringComparison.OrdinalIgnoreCase));
            _mutex = new Mutex(true, "CoolerScreen.SingleInstance.bhxuk", out bool isNew);
            for (int i = 0; !isNew && restarting && i < 50; i++)
            {
                Thread.Sleep(200);
                _mutex.Dispose();
                _mutex = new Mutex(true, "CoolerScreen.SingleInstance.bhxuk", out isNew);
            }
            if (!isNew)
            {
                // Already running: ask that copy to open its window.
                try { using var ev = EventWaitHandle.OpenExisting("CoolerScreen.Show.bhxuk"); ev.Set(); } catch { }
                return 0;
            }

            var app = new App
            {
                Background = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase)),
                SelfTest = args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase))
            };
            int code = app.Run();
            GC.KeepAlive(_mutex);
            return code;
        }
    }

    public class App : Application
    {
        public bool Background { get; set; }
        /// <summary>Opens every page and every kind of screen in the editor, logs any problem and exits (used by the build).</summary>
        public bool SelfTest { get; set; }
        public static App Instance => (App)Current;

        public LiveData Live { get; private set; }
        public MediaCache Cache { get; private set; }
        public DeviceService Device { get; private set; }
        public SensorService Sensors { get; private set; }
        public MediaService Media { get; private set; }
        public WeatherService Weather { get; private set; }
        public DisplayEngine Engine { get; private set; }
        public Tray Tray { get; private set; }
        public HubWindow Hub { get; private set; }

        private EventWaitHandle _showEvent;
        private EventWaitHandle _quitEvent;
        private bool _quitting;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, ex) => { Log.Write("Unhandled: " + ex.Exception); ex.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log.Write("Fatal: " + ex.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, ex) => { Log.Write("Task: " + ex.Exception); ex.SetObserved(); };

            Settings.Load();
            AppStyles.Install();
            AutoStart.Apply(Settings.Current.StartWithWindows);
            Log.Write("Cooler Screen " + Updates.CurrentText + " starting");

            Live = new LiveData { Fahrenheit = Settings.Current.Fahrenheit };
            Cache = new MediaCache();
            Device = new DeviceService();
            Sensors = new SensorService(Live);
            Media = new MediaService(Live);
            Weather = new WeatherService(Live);
            Engine = new DisplayEngine(Device, Sensors, Media, Weather, Live, Cache);

            Sensors.Start();
            Media.Start();
            Weather.Start();
            Engine.Start();
            Tray = new Tray();

            Device.StateChanged += () => Dispatcher.BeginInvoke(new Action(() => Tray?.Refresh()));

            // Let go of the screen before sleep, pick it up again after.
            SystemEvents.PowerModeChanged += (s, ev) =>
            {
                if (ev.Mode == PowerModes.Suspend) Device.Suspend();
                else if (ev.Mode == PowerModes.Resume) { Device.Resume(); Engine.Poke(); }
            };
            SystemEvents.SessionSwitch += (s, ev) =>
            {
                if (ev.Reason == SessionSwitchReason.SessionLock) Engine.Locked = true;
                else if (ev.Reason == SessionSwitchReason.SessionUnlock) Engine.Locked = false;
                Engine.Poke();
            };
            SystemEvents.SessionEnding += (s, ev) => Quit();

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "CoolerScreen.Show.bhxuk");
            var t = new Thread(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(() => ShowHub()));
                }
            }) { IsBackground = true };
            t.Start();

            _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "CoolerScreen.Quit.bhxuk");
            var q = new Thread(() =>
            {
                _quitEvent.WaitOne();
                Dispatcher.Invoke(new Action(Quit));
            }) { IsBackground = true };
            q.Start();

            if (SelfTest) { Dispatcher.BeginInvoke(new Action(RunSelfTest), DispatcherPriority.ApplicationIdle); return; }

            if (Settings.Current.FirstRun || !Background) ShowHub();
            if (Settings.Current.FirstRun)
            {
                Settings.Current.FirstRun = false;
                Settings.Save();
            }

            if (Settings.Current.CheckForUpdates)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                timer.Tick += async (s, ev) =>
                {
                    timer.Interval = TimeSpan.FromHours(6);
                    if (Settings.Current.CheckForUpdates) await CheckForUpdate();
                };
                timer.Start();
            }
        }

        private void RunSelfTest()
        {
            int problems = 0;
            void Settle()
            {
                // Let layout, Loaded events and a few timer ticks run.
                var until = DateTime.UtcNow.AddMilliseconds(400);
                while (DateTime.UtcNow < until)
                {
                    var frame = new DispatcherFrame();
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                    Thread.Sleep(20);
                }
            }
            void Step(string name, Action a)
            {
                try { a(); Settle(); Log.Write("Selftest ok: " + name); }
                catch (Exception ex) { problems++; Log.Write("Selftest FAILED: " + name + ": " + ex); }
            }
            ShowHub();
            Hub.Width = 1320; Hub.Height = 860;
            Settle();
            string shots = Environment.GetEnvironmentVariable("COOLERSCREEN_SHOTS");
            void Shot(string name)
            {
                if (string.IsNullOrEmpty(shots)) return;
                try
                {
                    Settle(); Settle();
                    System.IO.Directory.CreateDirectory(shots);
                    var root = (FrameworkElement)Hub.Content;
                    int w = (int)root.ActualWidth, h = (int)root.ActualHeight;
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w * 2, h * 2, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                    var dv = new System.Windows.Media.DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(Theme.Bg, null, new Rect(0, 0, w, h));
                        dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, w, h));
                    }
                    rtb.Render(dv);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using var f = System.IO.File.Create(System.IO.Path.Combine(shots, name + ".png"));
                    enc.Save(f);
                }
                catch (Exception ex) { Log.Write("Screenshot " + name + ": " + ex.Message); }
            }
            foreach (var page in new[] { "home", "screens", "playlist", "automations", "device", "settings", "home" })
                Step("page " + page, () => { Hub.Navigate(page); Shot("app-" + page); });
            foreach (var (title, _, spec) in Templates.All())
            {
                var s = spec.Clone();
                Settings.Current.Screens.Add(s);
                Step("editor " + title, () => { Hub.Edit(s); if (title == "Analog" || title == "CPU and GPU" || title == "Weather") Shot("app-editor-" + title.ToLowerInvariant().Replace(' ', '-')); });
                Step("show " + title, () => ShowScreen(s.Id));
            }
            Step("playlist on", () => { Settings.Current.PlaylistOn = true; Hub.Navigate("playlist"); });
            Step("app rule", () => { Settings.Current.AppRules.Add(new AppRule { Process = "explorer", ScreenId = Settings.Current.Screens[0].Id }); Hub.Navigate("automations"); });
            Step("test pattern", () => Engine.ShowTestPattern(2));
            Settle(); Settle();
            Log.Write("Selftest finished: " + (problems == 0 ? "no problems" : problems + " problem(s)") + ". Device: " + Device.Message + ". Showing: " + Engine.NowShowing);
            Quit();
        }

        public void ShowHub(string page = null)
        {
            if (Hub == null)
            {
                Hub = new HubWindow();
                Hub.Closed += (s, e) => Hub = null;
            }
            if (page != null) Hub.Navigate(page);
            Hub.Show();
            if (Hub.WindowState == WindowState.Minimized) Hub.WindowState = WindowState.Normal;
            Hub.Activate();
        }

        /// <summary>Makes a screen the one the cooler shows.</summary>
        public void ShowScreen(string id)
        {
            if (Settings.Screen(id) == null) return;
            Settings.Current.ActiveId = id;
            Settings.Save();
            if (Engine.Paused) Engine.SetPaused(false);
            Engine.Poke();
            Tray?.Refresh();
        }

        private string _notifiedVersion;
        private bool _installing;

        public async Task CheckForUpdate()
        {
            if (!await Updates.Check()) return;
            if (Settings.Current.AutoUpdate)
            {
                await InstallUpdate();
                return;
            }
            Tray.ShowUpdateItem(Updates.LatestVersion);
            if (_notifiedVersion == Updates.LatestVersion) return;
            _notifiedVersion = Updates.LatestVersion;
            Tray.Notify("Cooler Screen " + Updates.LatestVersion + " is available", "Click here to install it. Cooler Screen reopens by itself.", () => _ = InstallUpdate());
        }

        public async Task<bool> InstallUpdate(Action<string> status = null)
        {
            if (_installing) return false;
            _installing = true;
            Tray.Notify("Updating Cooler Screen", "Downloading now. It will reopen in a few seconds.");
            bool ok = await Updates.DownloadAndInstall(status);
            if (!ok)
            {
                _installing = false;
                Tray.Notify("Couldn't update Cooler Screen", "Try again from Settings > Updates.");
            }
            return ok;
        }

        public void Restart()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--restarting") { UseShellExecute = false }); }
            catch (Exception ex) { Log.Write("Restart failed: " + ex.Message); return; }
            Quit();
        }

        public void Quit()
        {
            if (_quitting) return;
            _quitting = true;
            try { Engine?.Stop(Settings.Current.HandBackOnExit); } catch { }
            try { Sensors?.Dispose(); } catch { }
            try { Media?.Dispose(); } catch { }
            try { Weather?.Dispose(); } catch { }
            try { Device?.Dispose(); } catch { }
            Tray?.Dispose();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (!_quitting)
            {
                try { Engine?.Stop(Settings.Current.HandBackOnExit); } catch { }
                try { Device?.Dispose(); } catch { }
            }
            base.OnExit(e);
        }
    }

    /// <summary>The icon by the clock, with a right-click menu.</summary>
    public sealed class Tray : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon _icon;
        private readonly System.Windows.Forms.ToolStripMenuItem _pause;
        private readonly System.Windows.Forms.ToolStripMenuItem _screens;
        private readonly System.Windows.Forms.ToolStripMenuItem _status;
        private System.Windows.Forms.ToolStripMenuItem _update;
        private Action _onClick;

        public Tray()
        {
            _icon = new System.Windows.Forms.NotifyIcon { Text = "Cooler Screen", Visible = true };
            try { _icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath); }
            catch { _icon.Icon = System.Drawing.SystemIcons.Application; }

            var menu = new System.Windows.Forms.ContextMenuStrip();
            _status = new System.Windows.Forms.ToolStripMenuItem("Looking for the cooler…") { Enabled = false };
            menu.Items.Add(_status);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Open Cooler Screen", null, (s, e) => App.Instance.ShowHub());
            _screens = new System.Windows.Forms.ToolStripMenuItem("Show on the cooler");
            menu.Items.Add(_screens);
            menu.Items.Add("Next screen in the playlist", null, (s, e) => App.Instance.Engine.NextInPlaylist());
            _pause = new System.Windows.Forms.ToolStripMenuItem("Pause (cooler shows its own screen)");
            _pause.Click += (s, e) => { App.Instance.Engine.SetPaused(!App.Instance.Engine.Paused); Refresh(); };
            menu.Items.Add(_pause);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit Cooler Screen", null, (s, e) => App.Instance.Quit());
            menu.Opening += (s, e) => Refresh();
            _icon.ContextMenuStrip = menu;
            _icon.BalloonTipClicked += (s, e) => { var a = _onClick; _onClick = null; a?.Invoke(); };
            _icon.BalloonTipClosed += (s, e) => _onClick = null;
            _icon.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) App.Instance.ShowHub(); };
            Refresh();
        }

        public void Refresh()
        {
            try
            {
                var app = App.Instance;
                _status.Text = app.Device.Connected ? "Showing: " + (app.Engine.Paused ? "paused" : app.Engine.NowShowing) : app.Device.Message;
                string tip = "Cooler Screen: " + (app.Device.Connected ? (app.Engine.Paused ? "paused" : app.Engine.NowShowing) : "not connected");
                _icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
                _pause.Text = app.Engine.Paused ? "Resume" : "Pause (cooler shows its own screen)";
                _screens.DropDownItems.Clear();
                foreach (var s in Settings.Current.Screens)
                {
                    string id = s.Id;
                    var item = new System.Windows.Forms.ToolStripMenuItem(s.Name) { Checked = id == Settings.Current.ActiveId };
                    item.Click += (o, e) => app.ShowScreen(id);
                    _screens.DropDownItems.Add(item);
                }
            }
            catch { }
        }

        public void Notify(string title, string text, Action onClick = null)
        {
            _onClick = onClick;
            try { _icon.ShowBalloonTip(5000, title, text, System.Windows.Forms.ToolTipIcon.None); } catch { }
        }

        public void ShowUpdateItem(string version)
        {
            var menu = _icon.ContextMenuStrip;
            if (_update == null)
            {
                _update = new System.Windows.Forms.ToolStripMenuItem();
                _update.Click += (s, e) => _ = App.Instance.InstallUpdate();
                menu.Items.Insert(0, _update);
                menu.Items.Insert(1, new System.Windows.Forms.ToolStripSeparator());
            }
            _update.Text = "Install update " + version;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
