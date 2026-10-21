using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoolerScreen.Core;
using CoolerScreen.Render;

namespace CoolerScreen.Services
{
    /// <summary>
    /// Reads the PC's temperatures, loads and network speed once a second.
    /// If Sector is installed its agent supplies the temperatures (it has the hardware access);
    /// everything else comes from Windows itself, with no drivers and no admin rights.
    /// </summary>
    public sealed class SensorService : IDisposable
    {
        public LiveData Live { get; }
        private Thread _thread;
        private volatile bool _running;
        private readonly SectorSource _sector = new SectorSource();
        private readonly WindowsSource _windows = new WindowsSource();

        public bool SectorConnected => _sector.Connected;
        public string SectorStatus => _sector.Status;
        public string WindowsNotes => _windows.Notes;

        public SensorService(LiveData live) { Live = live; }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Sensors", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        private void Loop()
        {
            var sw = Stopwatch.StartNew();
            while (_running)
            {
                long started = sw.ElapsedMilliseconds;
                try { Tick(); } catch (Exception ex) { Log.Write("Sensors: " + ex.Message); }
                long wait = 1000 - (sw.ElapsedMilliseconds - started);
                if (wait > 0) Thread.Sleep((int)wait);
            }
        }

        private void Tick()
        {
            Live.Fahrenheit = Settings.Current.Fahrenheit;
            var values = new Dictionary<SensorId, double?>();
            _windows.Read(values);
            if (Settings.Current.UseSector && _sector.Read(out var fromSector))
            {
                // Sector's readings come straight from the hardware, so they win.
                foreach (var kv in fromSector) if (kv.Value != null) values[kv.Key] = kv.Value;
                Live.Source = "Sector";
            }
            else Live.Source = "Windows";
            foreach (var s in Sensors.All) Live.Set(s, values.TryGetValue(s, out var v) ? v : null);
        }

        public void Dispose()
        {
            _running = false;
            _windows.Dispose();
        }
    }

    /// <summary>Talks to Sector's agent on this PC (127.0.0.1), using the key Sector stores in ProgramData.</summary>
    internal sealed class SectorSource
    {
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(900) };
        private DateTime _nextConfigRead = DateTime.MinValue;
        private DateTime _retryAfter = DateTime.MinValue;
        private int _port = 7311;
        private string _key = "";

        public bool Connected { get; private set; }
        public string Status { get; private set; } = "Not checked yet";

        private static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Sector", "agent.json");

        public bool Read(out Dictionary<SensorId, double?> values)
        {
            values = null;
            if (DateTime.UtcNow < _retryAfter) return false;
            try
            {
                if (DateTime.UtcNow >= _nextConfigRead)
                {
                    _nextConfigRead = DateTime.UtcNow.AddSeconds(30);
                    if (!File.Exists(ConfigPath))
                    {
                        Fail("Sector isn't installed on this PC", 60);
                        return false;
                    }
                    using var cfg = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                    var root = cfg.RootElement;
                    if (Prop(root, "port") is JsonElement p && p.ValueKind == JsonValueKind.Number) _port = p.GetInt32();
                    if (Prop(root, "key") is JsonElement k && k.ValueKind == JsonValueKind.String) _key = k.GetString() ?? "";
                }
                using var req = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + _port + "/api/snapshot");
                req.Headers.Add("X-Sector-Key", _key);
                using var resp = _http.Send(req);
                if (!resp.IsSuccessStatusCode)
                {
                    Fail("Sector's agent answered " + (int)resp.StatusCode, 20);
                    _nextConfigRead = DateTime.MinValue;
                    return false;
                }
                using var stream = resp.Content.ReadAsStream();
                using var doc = JsonDocument.Parse(stream);
                values = Parse(doc.RootElement);
                Connected = true;
                Status = "Using temperatures from Sector";
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                Fail("Sector is installed but its settings file can't be read", 60);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException)
            {
                Fail("Sector's agent isn't running", 15);
            }
            catch (Exception ex)
            {
                Fail("Sector: " + ex.Message, 30);
            }
            return false;
        }

        private void Fail(string why, int retrySeconds)
        {
            Connected = false;
            Status = why;
            _retryAfter = DateTime.UtcNow.AddSeconds(retrySeconds);
        }

        private static JsonElement? Prop(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in e.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return null;
        }

        private static double? Num(JsonElement e, string name)
        {
            var v = Prop(e, name);
            if (v == null || v.Value.ValueKind != JsonValueKind.Number) return null;
            double d = v.Value.GetDouble();
            return double.IsNaN(d) ? null : d;
        }

        private static Dictionary<SensorId, double?> Parse(JsonElement root)
        {
            var d = new Dictionary<SensorId, double?>();
            if (Prop(root, "summary") is JsonElement s)
            {
                d[SensorId.CpuTemp] = Num(s, "cpuTemp");
                d[SensorId.GpuTemp] = Num(s, "gpuTemp");
                d[SensorId.CpuLoad] = Num(s, "cpuLoad");
                d[SensorId.GpuLoad] = Num(s, "gpuLoad");
                d[SensorId.RamLoad] = Num(s, "memoryLoad");
                d[SensorId.CpuPower] = Num(s, "cpuPower");
                d[SensorId.GpuPower] = Num(s, "gpuPower");
                d[SensorId.CpuClock] = Num(s, "cpuClock");
                d[SensorId.BoardTemp] = Num(s, "motherboardTemp");
                var used = Num(s, "gpuMemoryUsed");
                var total = Num(s, "gpuMemoryTotal");
                if (used != null && total != null && total > 0) d[SensorId.VramLoad] = used / total * 100;
            }
            if (Prop(root, "drives") is JsonElement drives && drives.ValueKind == JsonValueKind.Array)
            {
                double? hottest = null;
                foreach (var dr in drives.EnumerateArray())
                {
                    var t = Num(dr, "temperature");
                    if (t != null && t > 0 && (hottest == null || t > hottest)) hottest = t;
                }
                d[SensorId.DriveTemp] = hottest;
            }
            return d;
        }
    }

    /// <summary>Readings Windows gives any app: CPU and GPU load, memory, network, NVIDIA GPU sensors, and a rough CPU temperature.</summary>
    internal sealed class WindowsSource : IDisposable
    {
        private PerformanceCounter _cpu;
        private PerformanceCounter _cpuPerf;
        private bool _cpuTried;
        private double _baseMhz;
        private readonly Dictionary<string, CounterSample> _gpuPrev = new Dictionary<string, CounterSample>();
        private PerformanceCounterCategory _gpuCat;
        private bool _gpuTried;
        private PerformanceCounterCategory _thermalCat;
        private bool _thermalTried;
        private long _lastRx, _lastTx;
        private DateTime _lastNet = DateTime.MinValue;
        private readonly Nvml _nvml = new Nvml();

        public string Notes { get; private set; } = "";

        public void Read(Dictionary<SensorId, double?> v)
        {
            var notes = new List<string>();
            ReadCpu(v);
            v[SensorId.RamLoad] = MemoryLoad();
            ReadGpu(v);
            ReadNetwork(v);
            if (_nvml.Read(out double? gTemp, out double? gLoad, out double? gMem, out double? gPower))
            {
                v[SensorId.GpuTemp] = gTemp;
                if (gLoad != null) v[SensorId.GpuLoad] = gLoad;
                v[SensorId.VramLoad] = gMem;
                v[SensorId.GpuPower] = gPower;
                notes.Add("NVIDIA GPU sensors");
            }
            var t = ThermalZone();
            if (t != null) { v[SensorId.CpuTemp] = t; notes.Add("CPU temperature from Windows (approximate)"); }
            Notes = string.Join(", ", notes);
        }

        private void ReadCpu(Dictionary<SensorId, double?> v)
        {
            try
            {
                if (!_cpuTried)
                {
                    _cpuTried = true;
                    try { _cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true); _cpu.NextValue(); }
                    catch { _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", true); _cpu.NextValue(); }
                    try
                    {
                        _cpuPerf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                        _cpuPerf.NextValue();
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                        if (key?.GetValue("~MHz") is int mhz) _baseMhz = mhz;
                    }
                    catch { _cpuPerf = null; }
                }
                if (_cpu != null) v[SensorId.CpuLoad] = Math.Min(100, Math.Max(0, _cpu.NextValue()));
                if (_cpuPerf != null && _baseMhz > 0) v[SensorId.CpuClock] = _cpuPerf.NextValue() / 100 * _baseMhz;
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        private static double? MemoryLoad()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m) || m.ullTotalPhys == 0) return null;
            return 100.0 * (m.ullTotalPhys - m.ullAvailPhys) / m.ullTotalPhys;
        }

        /// <summary>GPU load the way Task Manager works it out: the busiest graphics card's 3D engine use.</summary>
        private void ReadGpu(Dictionary<SensorId, double?> v)
        {
            try
            {
                if (!_gpuTried)
                {
                    _gpuTried = true;
                    if (PerformanceCounterCategory.Exists("GPU Engine")) _gpuCat = new PerformanceCounterCategory("GPU Engine");
                }
                if (_gpuCat == null) return;
                var data = _gpuCat.ReadCategory();
                var util = data["Utilization Percentage"];
                if (util == null) return;
                var perAdapter = new Dictionary<string, double>();
                var seen = new HashSet<string>();
                foreach (InstanceData inst in util.Values)
                {
                    string name = inst.InstanceName ?? "";
                    if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
                    seen.Add(name);
                    var now = inst.Sample;
                    if (_gpuPrev.TryGetValue(name, out var before))
                    {
                        double pct = CounterSample.Calculate(before, now);
                        int i = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                        string adapter = i >= 0 ? name.Substring(i, Math.Min(name.Length - i, 26)) : "gpu";
                        perAdapter[adapter] = (perAdapter.TryGetValue(adapter, out var sum) ? sum : 0) + pct;
                    }
                    _gpuPrev[name] = now;
                }
                foreach (var gone in _gpuPrev.Keys.Where(k => !seen.Contains(k)).ToList()) _gpuPrev.Remove(gone);
                if (perAdapter.Count > 0) v[SensorId.GpuLoad] = Math.Min(100, perAdapter.Values.Max());
            }
            catch { }
        }

        /// <summary>ACPI thermal zones. Not the exact CPU package temperature, but close on many PCs.</summary>
        private double? ThermalZone()
        {
            try
            {
                if (!_thermalTried)
                {
                    _thermalTried = true;
                    if (PerformanceCounterCategory.Exists("Thermal Zone Information")) _thermalCat = new PerformanceCounterCategory("Thermal Zone Information");
                }
                if (_thermalCat == null) return null;
                var data = _thermalCat.ReadCategory();
                double best = 0;
                var hp = data["High Precision Temperature"];
                if (hp != null)
                    foreach (InstanceData i in hp.Values) best = Math.Max(best, i.RawValue / 10.0 - 273.15);
                if (best <= 0)
                {
                    var t = data["Temperature"];
                    if (t != null) foreach (InstanceData i in t.Values) best = Math.Max(best, i.RawValue - 273.15);
                }
                // Many desktop boards report a fixed 27.8 °C here, which isn't a real reading.
                if (best < 29 || best > 125) return null;
                return best;
            }
            catch { return null; }
        }

        private void ReadNetwork(Dictionary<SensorId, double?> v)
        {
            try
            {
                long rx = 0, tx = 0;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var st = ni.GetIPv4Statistics();
                    rx += st.BytesReceived;
                    tx += st.BytesSent;
                }
                var now = DateTime.UtcNow;
                if (_lastNet != DateTime.MinValue)
                {
                    double secs = Math.Max(0.2, (now - _lastNet).TotalSeconds);
                    v[SensorId.NetDown] = Math.Max(0, (rx - _lastRx) / secs);
                    v[SensorId.NetUp] = Math.Max(0, (tx - _lastTx) / secs);
                }
                _lastRx = rx; _lastTx = tx; _lastNet = now;
            }
            catch { }
        }

        public void Dispose()
        {
            _cpu?.Dispose();
            _cpuPerf?.Dispose();
            _nvml.Dispose();
        }
    }

    /// <summary>NVIDIA's own monitoring library, installed with the graphics driver. Needs no admin rights.</summary>
    internal sealed class Nvml : IDisposable
    {
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] private static extern int Init();
        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")] private static extern int Shutdown();
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] private static extern int GetHandle(uint index, out IntPtr device);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")] private static extern int GetTemperature(IntPtr device, int sensor, out uint temp);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")] private static extern int GetUtilization(IntPtr device, out Utilization util);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")] private static extern int GetMemory(IntPtr device, out Memory mem);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")] private static extern int GetPower(IntPtr device, out uint milliwatts);

        [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
        [StructLayout(LayoutKind.Sequential)] private struct Memory { public ulong Total, Free, Used; }

        private bool _tried, _ok;
        private IntPtr _device;

        public bool Read(out double? temp, out double? load, out double? mem, out double? power)
        {
            temp = load = mem = power = null;
            try
            {
                if (!_tried)
                {
                    _tried = true;
                    _ok = Init() == 0 && GetHandle(0, out _device) == 0;
                }
                if (!_ok) return false;
                if (GetTemperature(_device, 0, out uint t) == 0) temp = t;
                if (GetUtilization(_device, out var u) == 0) load = u.Gpu;
                if (GetMemory(_device, out var m) == 0 && m.Total > 0) mem = 100.0 * m.Used / m.Total;
                if (GetPower(_device, out uint mw) == 0) power = mw / 1000.0;
                return temp != null || load != null;
            }
            catch (DllNotFoundException) { _ok = false; return false; }
            catch (EntryPointNotFoundException) { _ok = false; return false; }
            catch { return false; }
        }

        public void Dispose()
        {
            try { if (_ok) Shutdown(); } catch { }
        }
    }
}
