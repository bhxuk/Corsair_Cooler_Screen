using System;
using System.Collections.Generic;
using System.Linq;

namespace CoolerScreen.Render
{
    /// <summary>What fills the screen behind everything else.</summary>
    public enum BackgroundKind { Solid, Gradient, Picture, Slideshow }

    /// <summary>How a picture or GIF is fitted to the round screen.</summary>
    public enum FitMode { Fill, Fit, Stretch }

    /// <summary>What sits on top of the background.</summary>
    public enum WidgetKind { None, Clock, Stats, Text, Countdown, NowPlaying, Weather }

    public enum ClockStyle { Digital, Stacked, Flip, SecondsRing, AnalogClassic, AnalogMinimal, DualZone }

    public enum StatsStyle { Gauge, DualGauge, Grid, BigNumber, Bars, Graph }

    public enum SensorId
    {
        CpuTemp, GpuTemp, CpuLoad, GpuLoad, RamLoad, VramLoad,
        CpuPower, GpuPower, CpuClock, BoardTemp, DriveTemp, NetDown, NetUp
    }

    /// <summary>One saved screen: a background plus one thing on top.</summary>
    public sealed class ScreenSpec
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New screen";

        // ---------- background ----------
        public BackgroundKind Background { get; set; } = BackgroundKind.Solid;
        public string Color1 { get; set; } = "#0B0D12";
        public string Color2 { get; set; } = "#1D2433";
        public double GradientAngle { get; set; } = 135;
        /// <summary>A picture or animated GIF/WebP, copied into the app's media folder.</summary>
        public string MediaPath { get; set; } = "";
        /// <summary>A folder of pictures and GIFs for the slideshow.</summary>
        public string FolderPath { get; set; } = "";
        public double SlideSeconds { get; set; } = 10;
        public bool Shuffle { get; set; }
        public FitMode Fit { get; set; } = FitMode.Fill;
        /// <summary>1 = fitted, up to 3 = zoomed in.</summary>
        public double Zoom { get; set; } = 1;
        /// <summary>-1..1: which part of the picture shows when it's zoomed or cropped.</summary>
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        /// <summary>0..80: darkens the background so text on top stays readable.</summary>
        public double Dim { get; set; }
        /// <summary>GIF speed: 0.25x to 4x.</summary>
        public double Speed { get; set; } = 1;

        // ---------- on top ----------
        public WidgetKind Widget { get; set; } = WidgetKind.None;
        public string TextColor { get; set; } = "#F4F2EE";
        public string AccentColor { get; set; } = "#5ED3C3";
        public string MutedColor { get; set; } = "#9AA0AB";
        public string Font { get; set; } = "Segoe UI";
        public int FontWeight { get; set; } = 600;
        /// <summary>0.6 to 1.4: makes the whole widget smaller or bigger.</summary>
        public double Scale { get; set; } = 1;
        /// <summary>-1..1: moves the widget up or down.</summary>
        public double OffsetWidgetY { get; set; }
        public bool Shadow { get; set; } = true;

        // clock
        public ClockStyle Clock { get; set; } = ClockStyle.Digital;
        public bool Use24Hour { get; set; } = true;
        public bool ShowSeconds { get; set; }
        public bool ShowDate { get; set; } = true;
        public string DateFormat { get; set; } = "ddd d MMM";
        public string TimeZone { get; set; } = "";
        public string SecondTimeZone { get; set; } = "";
        public string Label1 { get; set; } = "";
        public string Label2 { get; set; } = "";

        // stats
        public StatsStyle Stats { get; set; } = StatsStyle.Gauge;
        public List<SensorId> Sensors { get; set; } = new List<SensorId> { SensorId.CpuTemp, SensorId.GpuTemp, SensorId.CpuLoad, SensorId.GpuLoad };
        public bool ColourByValue { get; set; } = true;
        public bool ShowLabels { get; set; } = true;

        // text
        public string Text { get; set; } = "Hello";
        public bool Marquee { get; set; }
        public double MarqueeSpeed { get; set; } = 90;

        // countdown
        public DateTime Target { get; set; } = DateTime.Today.AddDays(30);
        public bool CountUp { get; set; }

        // now playing
        public bool ArtBackground { get; set; } = true;

        // weather
        public string Place { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public ScreenSpec Clone()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(this);
            var c = System.Text.Json.JsonSerializer.Deserialize<ScreenSpec>(json);
            c.Id = Guid.NewGuid().ToString("N");
            return c;
        }

        /// <summary>True when the screen changes more often than once a second.</summary>
        public bool IsAnimated(MediaCache cache)
        {
            if (Background == BackgroundKind.Picture && cache != null && cache.IsAnimation(MediaPath)) return true;
            if (Background == BackgroundKind.Slideshow) return true;
            if (Widget == WidgetKind.Text && Marquee) return true;
            if (Widget == WidgetKind.Clock && (Clock == ClockStyle.AnalogClassic || Clock == ClockStyle.AnalogMinimal) && ShowSeconds) return true;
            if (Widget == WidgetKind.Clock && Clock == ClockStyle.SecondsRing) return true;
            return false;
        }
    }

    public static class Sensors
    {
        public static readonly SensorId[] All = (SensorId[])Enum.GetValues(typeof(SensorId));

        public static string Name(SensorId s) => s switch
        {
            SensorId.CpuTemp => "CPU temperature",
            SensorId.GpuTemp => "GPU temperature",
            SensorId.CpuLoad => "CPU load",
            SensorId.GpuLoad => "GPU load",
            SensorId.RamLoad => "Memory in use",
            SensorId.VramLoad => "GPU memory in use",
            SensorId.CpuPower => "CPU power",
            SensorId.GpuPower => "GPU power",
            SensorId.CpuClock => "CPU clock",
            SensorId.BoardTemp => "Motherboard temperature",
            SensorId.DriveTemp => "Hottest drive",
            SensorId.NetDown => "Download speed",
            SensorId.NetUp => "Upload speed",
            _ => s.ToString()
        };

        /// <summary>The short label drawn on the screen.</summary>
        public static string Short(SensorId s) => s switch
        {
            SensorId.CpuTemp => "CPU",
            SensorId.GpuTemp => "GPU",
            SensorId.CpuLoad => "CPU",
            SensorId.GpuLoad => "GPU",
            SensorId.RamLoad => "RAM",
            SensorId.VramLoad => "VRAM",
            SensorId.CpuPower => "CPU",
            SensorId.GpuPower => "GPU",
            SensorId.CpuClock => "CPU",
            SensorId.BoardTemp => "BOARD",
            SensorId.DriveTemp => "DRIVE",
            SensorId.NetDown => "DOWN",
            SensorId.NetUp => "UP",
            _ => s.ToString()
        };

        public static bool IsTemperature(SensorId s) => s == SensorId.CpuTemp || s == SensorId.GpuTemp || s == SensorId.BoardTemp || s == SensorId.DriveTemp;
        public static bool IsPercent(SensorId s) => s == SensorId.CpuLoad || s == SensorId.GpuLoad || s == SensorId.RamLoad || s == SensorId.VramLoad;
        public static bool IsNetwork(SensorId s) => s == SensorId.NetDown || s == SensorId.NetUp;

        /// <summary>The range a gauge covers, in the sensor's own units (°C, %, W, MHz, bytes/s).</summary>
        public static (double min, double max) Range(SensorId s) => s switch
        {
            SensorId.CpuTemp => (20, 100),
            SensorId.GpuTemp => (20, 95),
            SensorId.BoardTemp => (20, 80),
            SensorId.DriveTemp => (20, 70),
            SensorId.CpuPower => (0, 250),
            SensorId.GpuPower => (0, 450),
            SensorId.CpuClock => (0, 6000),
            SensorId.NetDown => (0, 125_000_000),
            SensorId.NetUp => (0, 50_000_000),
            _ => (0, 100)
        };

        /// <summary>Warn and danger levels used to colour a reading. Null when the sensor has none.</summary>
        public static (double warn, double crit)? Levels(SensorId s) => s switch
        {
            SensorId.CpuTemp => (75, 88),
            SensorId.GpuTemp => (75, 85),
            SensorId.BoardTemp => (55, 70),
            SensorId.DriveTemp => (50, 60),
            SensorId.CpuLoad => (80, 95),
            SensorId.GpuLoad => (85, 98),
            SensorId.RamLoad => (80, 92),
            SensorId.VramLoad => (85, 95),
            _ => null
        };

        /// <summary>Formats a reading: value and unit separately so the unit can be drawn smaller.</summary>
        public static (string value, string unit) Format(SensorId s, double? v, bool fahrenheit)
        {
            if (v == null || double.IsNaN(v.Value)) return ("--", Unit(s, fahrenheit));
            double x = v.Value;
            if (IsTemperature(s))
            {
                if (fahrenheit) x = x * 9 / 5 + 32;
                return (Math.Round(x).ToString("0"), fahrenheit ? "°F" : "°C");
            }
            if (IsPercent(s)) return (Math.Round(x).ToString("0"), "%");
            if (s == SensorId.CpuPower || s == SensorId.GpuPower) return (Math.Round(x).ToString("0"), "W");
            if (s == SensorId.CpuClock) return x >= 1000 ? ((x / 1000).ToString("0.00"), "GHz") : (Math.Round(x).ToString("0"), "MHz");
            if (IsNetwork(s))
            {
                double bits = x * 8;
                if (bits >= 1_000_000_000) return ((bits / 1_000_000_000).ToString("0.0"), "Gb/s");
                if (bits >= 1_000_000) return ((bits / 1_000_000).ToString(bits >= 100_000_000 ? "0" : "0.0"), "Mb/s");
                if (bits >= 1_000) return ((bits / 1_000).ToString("0"), "kb/s");
                return (Math.Round(bits).ToString("0"), "b/s");
            }
            return (Math.Round(x).ToString("0"), "");
        }

        public static string Unit(SensorId s, bool fahrenheit)
        {
            if (IsTemperature(s)) return fahrenheit ? "°F" : "°C";
            if (IsPercent(s)) return "%";
            if (s == SensorId.CpuPower || s == SensorId.GpuPower) return "W";
            if (s == SensorId.CpuClock) return "GHz";
            if (IsNetwork(s)) return "Mb/s";
            return "";
        }

        /// <summary>A short kind word shown under a reading ("TEMP", "LOAD").</summary>
        public static string Kind(SensorId s)
        {
            if (IsTemperature(s)) return "TEMP";
            if (IsPercent(s)) return s == SensorId.RamLoad || s == SensorId.VramLoad ? "USED" : "LOAD";
            if (s == SensorId.CpuPower || s == SensorId.GpuPower) return "POWER";
            if (s == SensorId.CpuClock) return "CLOCK";
            return "";
        }
    }

    /// <summary>Ready-made screens offered when you add a new one.</summary>
    public static class Templates
    {
        public static List<(string title, string description, ScreenSpec spec)> All()
        {
            var list = new List<(string, string, ScreenSpec)>();

            list.Add(("Clock", "A big, clean digital clock with the date.", new ScreenSpec
            {
                Name = "Clock", Widget = WidgetKind.Clock, Clock = ClockStyle.Digital,
                Background = BackgroundKind.Gradient, Color1 = "#0B0D12", Color2 = "#1B2230", AccentColor = "#5ED3C3"
            }));
            list.Add(("Seconds ring", "The time, with the seconds racing round the edge.", new ScreenSpec
            {
                Name = "Seconds ring", Widget = WidgetKind.Clock, Clock = ClockStyle.SecondsRing,
                Background = BackgroundKind.Solid, Color1 = "#07080D", AccentColor = "#FFB547"
            }));
            list.Add(("Analog", "A classic watch face.", new ScreenSpec
            {
                Name = "Analog", Widget = WidgetKind.Clock, Clock = ClockStyle.AnalogClassic, ShowSeconds = true,
                Background = BackgroundKind.Gradient, Color1 = "#10131A", Color2 = "#232A38", AccentColor = "#E2585F"
            }));
            list.Add(("Flip clock", "Two flip cards, like an old bedside clock.", new ScreenSpec
            {
                Name = "Flip clock", Widget = WidgetKind.Clock, Clock = ClockStyle.Flip,
                Background = BackgroundKind.Solid, Color1 = "#0A0A0C", AccentColor = "#FFB547", FontWeight = 700
            }));
            list.Add(("CPU gauge", "Your CPU temperature on a gauge that turns amber, then red.", new ScreenSpec
            {
                Name = "CPU temperature", Widget = WidgetKind.Stats, Stats = StatsStyle.Gauge,
                Sensors = new List<SensorId> { SensorId.CpuTemp, SensorId.CpuLoad },
                Background = BackgroundKind.Solid, Color1 = "#07080D", AccentColor = "#5ED3C3"
            }));
            list.Add(("CPU and GPU", "Two rings: CPU outside, GPU inside.", new ScreenSpec
            {
                Name = "CPU and GPU", Widget = WidgetKind.Stats, Stats = StatsStyle.DualGauge,
                Sensors = new List<SensorId> { SensorId.CpuTemp, SensorId.GpuTemp },
                Background = BackgroundKind.Gradient, Color1 = "#07080D", Color2 = "#141A24", AccentColor = "#5ED3C3"
            }));
            list.Add(("System grid", "Four readings at a glance.", new ScreenSpec
            {
                Name = "System", Widget = WidgetKind.Stats, Stats = StatsStyle.Grid,
                Sensors = new List<SensorId> { SensorId.CpuTemp, SensorId.GpuTemp, SensorId.CpuLoad, SensorId.RamLoad },
                Background = BackgroundKind.Solid, Color1 = "#07080D", AccentColor = "#FFB547"
            }));
            list.Add(("Temperature graph", "The last two minutes of a sensor as a graph.", new ScreenSpec
            {
                Name = "CPU graph", Widget = WidgetKind.Stats, Stats = StatsStyle.Graph,
                Sensors = new List<SensorId> { SensorId.CpuTemp },
                Background = BackgroundKind.Solid, Color1 = "#07080D", AccentColor = "#A07CF0"
            }));
            list.Add(("Picture or GIF", "Any picture or animated GIF, filled to the round screen.", new ScreenSpec
            {
                Name = "Picture", Background = BackgroundKind.Picture, Widget = WidgetKind.None
            }));
            list.Add(("GIF with clock", "A GIF behind a clock, darkened slightly so the time stands out.", new ScreenSpec
            {
                Name = "GIF and clock", Background = BackgroundKind.Picture, Dim = 35, Widget = WidgetKind.Clock, Clock = ClockStyle.Digital
            }));
            list.Add(("Slideshow", "A folder of pictures and GIFs, one after another.", new ScreenSpec
            {
                Name = "Slideshow", Background = BackgroundKind.Slideshow, Widget = WidgetKind.None
            }));
            list.Add(("Now playing", "The song playing on your PC, with its artwork.", new ScreenSpec
            {
                Name = "Now playing", Widget = WidgetKind.NowPlaying, Background = BackgroundKind.Solid, Color1 = "#07080D", AccentColor = "#7CC85A"
            }));
            list.Add(("Weather", "The weather where you are, from Open-Meteo.", new ScreenSpec
            {
                Name = "Weather", Widget = WidgetKind.Weather, Background = BackgroundKind.Gradient, Color1 = "#0E2236", Color2 = "#1F4766", AccentColor = "#FFB547"
            }));
            list.Add(("Countdown", "Days, hours and minutes to a date.", new ScreenSpec
            {
                Name = "Countdown", Widget = WidgetKind.Countdown, Label1 = "Holiday", Background = BackgroundKind.Gradient,
                Color1 = "#1A0F1F", Color2 = "#3A1C40", AccentColor = "#F06A9A", Target = DateTime.Today.AddDays(21).AddHours(9)
            }));
            list.Add(("Message", "Your own words, still or scrolling.", new ScreenSpec
            {
                Name = "Message", Widget = WidgetKind.Text, Text = "GG", Background = BackgroundKind.Solid, Color1 = "#07080D", FontWeight = 800
            }));
            list.Add(("World clock", "Here and one other time zone.", new ScreenSpec
            {
                Name = "World clock", Widget = WidgetKind.Clock, Clock = ClockStyle.DualZone, Label1 = "London", SecondTimeZone = "Eastern Standard Time", Label2 = "New York",
                Background = BackgroundKind.Gradient, Color1 = "#0B0D12", Color2 = "#1B2230", AccentColor = "#5B8CFF"
            }));
            return list;
        }

        /// <summary>The screens a brand new install starts with.</summary>
        public static List<ScreenSpec> Starter()
        {
            var t = All();
            string[] names = { "Clock", "CPU and GPU", "System grid", "Seconds ring", "Analog" };
            return names.Select(n => t.First(x => x.title == n).spec).ToList();
        }
    }
}
