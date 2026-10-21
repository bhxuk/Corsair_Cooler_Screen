using System;
using System.Collections.Generic;
using SkiaSharp;

namespace CoolerScreen.Render
{
    /// <summary>Everything a screen might show that changes: sensor readings, music, weather.</summary>
    public sealed class LiveData
    {
        private readonly object _gate = new object();
        private readonly Dictionary<SensorId, double?> _values = new Dictionary<SensorId, double?>();
        private readonly Dictionary<SensorId, History> _history = new Dictionary<SensorId, History>();

        public bool Fahrenheit { get; set; }

        /// <summary>Overrides the clock; used for previews and screenshots.</summary>
        public DateTime? FixedTime { get; set; }
        public DateTime Now => FixedTime ?? DateTime.Now;

        public NowPlaying Media { get; set; }
        public WeatherNow Weather { get; set; }

        /// <summary>Where the temperatures are coming from, shown in the app.</summary>
        public string Source { get; set; } = "";

        public double? Get(SensorId s)
        {
            lock (_gate) return _values.TryGetValue(s, out var v) ? v : null;
        }

        public void Set(SensorId s, double? v)
        {
            lock (_gate)
            {
                _values[s] = v;
                if (!_history.TryGetValue(s, out var h)) _history[s] = h = new History(120);
                h.Add(v);
            }
        }

        /// <summary>The last readings (oldest first), up to two minutes at one per second.</summary>
        public double?[] HistoryOf(SensorId s)
        {
            lock (_gate) return _history.TryGetValue(s, out var h) ? h.ToArray() : Array.Empty<double?>();
        }

        private sealed class History
        {
            private readonly double?[] _buf;
            private int _start, _count;
            public History(int size) { _buf = new double?[size]; }
            public void Add(double? v)
            {
                int i = (_start + _count) % _buf.Length;
                _buf[i] = v;
                if (_count < _buf.Length) _count++; else _start = (_start + 1) % _buf.Length;
            }
            public double?[] ToArray()
            {
                var a = new double?[_count];
                for (int i = 0; i < _count; i++) a[i] = _buf[(_start + i) % _buf.Length];
                return a;
            }
        }
    }

    public sealed class NowPlaying
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string App { get; set; } = "";
        public bool Playing { get; set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; set; }
        /// <summary>When Position was read, so the progress keeps moving between updates.</summary>
        public DateTime PositionAt { get; set; } = DateTime.UtcNow;
        public SKImage Art { get; set; }
        /// <summary>The artwork blurred, made once per song for the background.</summary>
        public SKImage ArtBlurred { get; set; }

        public TimeSpan LivePosition
        {
            get
            {
                if (!Playing) return Position;
                var p = Position + (DateTime.UtcNow - PositionAt);
                return Duration > TimeSpan.Zero && p > Duration ? Duration : p;
            }
        }
    }

    public sealed class WeatherNow
    {
        public string Place { get; set; } = "";
        public double Temperature { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public int Code { get; set; }
        public bool IsDay { get; set; } = true;
        public DateTime Updated { get; set; }

        /// <summary>WMO weather code in words.</summary>
        public static string Describe(int code) => code switch
        {
            0 => "Clear",
            1 => "Mostly clear",
            2 => "Partly cloudy",
            3 => "Cloudy",
            45 or 48 => "Fog",
            51 or 53 or 55 => "Drizzle",
            56 or 57 => "Freezing drizzle",
            61 or 63 => "Rain",
            65 => "Heavy rain",
            66 or 67 => "Freezing rain",
            71 or 73 => "Snow",
            75 => "Heavy snow",
            77 => "Snow grains",
            80 or 81 => "Showers",
            82 => "Heavy showers",
            85 or 86 => "Snow showers",
            95 => "Thunderstorm",
            96 or 99 => "Storm with hail",
            _ => "Weather"
        };

        public enum Icon { Sun, Moon, PartSun, PartMoon, Cloud, Fog, Rain, Snow, Storm }

        public static Icon IconFor(int code, bool day)
        {
            if (code == 0 || code == 1) return day ? Icon.Sun : Icon.Moon;
            if (code == 2) return day ? Icon.PartSun : Icon.PartMoon;
            if (code == 3) return Icon.Cloud;
            if (code == 45 || code == 48) return Icon.Fog;
            if (code >= 71 && code <= 77 || code == 85 || code == 86) return Icon.Snow;
            if (code >= 95) return Icon.Storm;
            if (code >= 51) return Icon.Rain;
            return Icon.Cloud;
        }
    }
}
