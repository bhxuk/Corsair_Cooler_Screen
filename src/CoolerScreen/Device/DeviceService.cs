using System;
using System.Diagnostics;
using System.Linq;
using CoolerScreen.Core;

namespace CoolerScreen.Device
{
    public enum DeviceState { Searching, Connected, Problem }

    /// <summary>Keeps a connection to the cooler's screen: finds it, reconnects after unplugging or sleep, sends frames.</summary>
    public sealed class DeviceService : IDisposable
    {
        private readonly object _gate = new object();
        private NautilusLcd _lcd;
        private DateTime _nextTry = DateTime.MinValue;
        private int _appliedBrightness = -1;
        private int _appliedRotation = -1;
        private bool _suspended;

        public DeviceState State { get; private set; } = DeviceState.Searching;
        public string Message { get; private set; } = "Looking for the cooler's screen…";
        public string Product => _lcd?.Product ?? "";
        public string Serial => _lcd?.Serial ?? "";
        public string Firmware => _lcd?.Firmware ?? "";
        public string Path => _lcd?.Path ?? "";
        public ushort ProductId => _lcd?.ProductId ?? 0;
        public int ReportSize => _lcd?.OutputReportLength ?? 0;
        public bool Connected => _lcd != null;

        public long FramesSent { get; private set; }
        public long BytesSent { get; private set; }
        public int LastFrameBytes { get; private set; }
        public double Fps { get; private set; }
        public int Errors { get; private set; }

        private readonly Stopwatch _fpsClock = Stopwatch.StartNew();
        private int _fpsCount;

        /// <summary>Raised when connected, disconnected or something goes wrong.</summary>
        public event Action StateChanged;

        private void SetState(DeviceState s, string msg)
        {
            bool changed = s != State || msg != Message;
            State = s;
            Message = msg;
            if (changed)
            {
                Log.Write("Device: " + msg);
                try { StateChanged?.Invoke(); } catch { }
            }
        }

        /// <summary>Tries to connect if not connected. Cheap to call often: it waits between attempts.</summary>
        public bool EnsureConnected()
        {
            lock (_gate)
            {
                if (_suspended) return false;
                if (_lcd != null) return true;
                if (DateTime.UtcNow < _nextTry) return false;
                _nextTry = DateTime.UtcNow.AddSeconds(3);
                try
                {
                    var paths = NautilusLcd.Find();
                    if (paths.Length == 0)
                    {
                        SetState(DeviceState.Searching, "Cooler screen not found. Check its USB cable is on a USB 2.0 header.");
                        return false;
                    }
                    Exception last = null;
                    foreach (var p in paths)
                    {
                        try
                        {
                            _lcd = NautilusLcd.Open(p);
                            break;
                        }
                        catch (Exception ex) { last = ex; }
                    }
                    if (_lcd == null)
                    {
                        SetState(DeviceState.Problem, last?.Message ?? "Couldn't open the cooler's screen.");
                        return false;
                    }
                    _appliedBrightness = -1;
                    _appliedRotation = -1;
                    Apply(Settings.Current.Brightness, Settings.Current.Rotation);
                    string name = string.IsNullOrWhiteSpace(_lcd.Product) ? "NAUTILUS RS LCD" : _lcd.Product.Replace("CORSAIR ", "");
                    SetState(DeviceState.Connected, "Connected to " + name);
                    return true;
                }
                catch (Exception ex)
                {
                    SetState(DeviceState.Problem, ex.Message);
                    return false;
                }
            }
        }

        /// <summary>Sets brightness and rotation if they've changed.</summary>
        public void Apply(int brightness, int rotation)
        {
            lock (_gate)
            {
                if (_lcd == null) return;
                try
                {
                    if (rotation != _appliedRotation && _lcd.SetRotation(rotation)) _appliedRotation = rotation;
                    if (brightness != _appliedBrightness && _lcd.SetBrightness(brightness)) _appliedBrightness = brightness;
                }
                catch (Exception ex) { Log.Write("Brightness/rotation failed: " + ex.Message); }
            }
        }

        public bool Send(byte[] jpeg)
        {
            if (!EnsureConnected()) return false;
            lock (_gate)
            {
                if (_lcd == null) return false;
                try
                {
                    _lcd.SendJpeg(jpeg);
                    FramesSent++;
                    BytesSent += jpeg.Length;
                    LastFrameBytes = jpeg.Length;
                    _fpsCount++;
                    double secs = _fpsClock.Elapsed.TotalSeconds;
                    if (secs >= 2)
                    {
                        Fps = _fpsCount / secs;
                        _fpsCount = 0;
                        _fpsClock.Restart();
                    }
                    if (State != DeviceState.Connected) SetState(DeviceState.Connected, "Connected");
                    return true;
                }
                catch (Exception ex)
                {
                    Errors++;
                    Log.Write("Send failed: " + ex.Message);
                    CloseLocked();
                    SetState(DeviceState.Searching, "Lost the cooler's screen. Reconnecting…");
                    _nextTry = DateTime.UtcNow.AddSeconds(2);
                    return false;
                }
            }
        }

        /// <summary>Hands the screen back to the cooler's built-in display and lets go of it.</summary>
        public void HandBack()
        {
            lock (_gate)
            {
                try { _lcd?.HandBack(); } catch (Exception ex) { Log.Write("Hand back failed: " + ex.Message); }
            }
        }

        /// <summary>Drops the connection (before sleep) and stops trying until Resume.</summary>
        public void Suspend()
        {
            lock (_gate)
            {
                _suspended = true;
                CloseLocked();
                SetState(DeviceState.Searching, "Paused while the PC sleeps");
            }
        }

        public void Resume()
        {
            lock (_gate)
            {
                _suspended = false;
                _nextTry = DateTime.UtcNow.AddSeconds(2);
            }
        }

        /// <summary>Closes and reconnects straight away (the Reconnect button).</summary>
        public void Reconnect()
        {
            lock (_gate)
            {
                CloseLocked();
                _nextTry = DateTime.MinValue;
            }
            EnsureConnected();
        }

        private void CloseLocked()
        {
            try { _lcd?.Dispose(); } catch { }
            _lcd = null;
            Fps = 0;
        }

        /// <summary>Other software that also drives Corsair screens, which would fight with Cooler Screen.</summary>
        public static string[] ConflictingApps()
        {
            string[] names = { "iCUE", "Corsair.Service", "CorsairDeviceControlService", "Corsair.Service.CpuIdRemote64", "SignalRgb", "SignalRgbLauncher" };
            try
            {
                return Process.GetProcesses()
                    .Select(p => { try { return p.ProcessName; } catch { return ""; } })
                    .Where(n => names.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        public void Dispose()
        {
            lock (_gate) CloseLocked();
        }
    }
}
