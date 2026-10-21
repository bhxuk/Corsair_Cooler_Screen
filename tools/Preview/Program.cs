using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoolerScreen.Render;
using SkiaSharp;

namespace Preview
{
    /// <summary>
    /// Renders every kind of screen with sample readings, for the website and to check the drawing.
    /// Usage: Preview [output folder]
    /// </summary>
    public static class Program
    {
        private static string _out;

        public static int Main(string[] args)
        {
            _out = args.Length > 0 ? args[0] : "previews";
            Directory.CreateDirectory(_out);

            // Previews are made on Linux, which has no Segoe UI: Inter is the closest match.
            var families = SKFontManager.Default.FontFamilies.ToList();
            string stand = families.FirstOrDefault(f => f == "Inter") ?? families.FirstOrDefault(f => f.Contains("Inter")) ?? "DejaVu Sans";
            Renderer.FontAlias = f => f == "Segoe UI" || f == "Bahnschrift" ? stand : f;
            Console.WriteLine("Font: " + stand);

            var cache = new MediaCache();
            var r = new Renderer(cache);
            var live = SampleData();

            int failures = 0;
            failures += TestGif(cache);

            var art = MakeArt(Path.Combine(_out, "_art.png"));
            var gif = MakeGif(Path.Combine(_out, "_waves.gif"));

            var shots = new List<(string name, ScreenSpec spec, double t)>();
            foreach (var (title, _, spec) in Templates.All())
            {
                var s = spec;
                if (s.Background == BackgroundKind.Picture) s.MediaPath = title.Contains("GIF") ? gif : art;
                if (s.Background == BackgroundKind.Slideshow) { s.Background = BackgroundKind.Picture; s.MediaPath = art; }
                if (s.Widget == WidgetKind.Weather) s.Place = "London";
                shots.Add(("template-" + Slug(title), s, 1200));
            }
            foreach (ClockStyle c in Enum.GetValues(typeof(ClockStyle)))
                shots.Add(("clock-" + Slug(c.ToString()), new ScreenSpec { Widget = WidgetKind.Clock, Clock = c, ShowSeconds = true, Background = BackgroundKind.Gradient, Color1 = "#0B0D12", Color2 = "#1B2230", Label1 = c == ClockStyle.DualZone ? "London" : "", Label2 = "New York", SecondTimeZone = "Eastern Standard Time", AccentColor = "#5ED3C3" }, 0));
            foreach (StatsStyle st in Enum.GetValues(typeof(StatsStyle)))
                shots.Add(("stats-" + Slug(st.ToString()), new ScreenSpec { Widget = WidgetKind.Stats, Stats = st, Background = BackgroundKind.Solid, Color1 = "#07080D", AccentColor = "#5ED3C3", Sensors = new List<SensorId> { SensorId.CpuTemp, SensorId.GpuTemp, SensorId.CpuLoad, SensorId.RamLoad } }, 0));
            shots.Add(("hot-gauge", new ScreenSpec { Widget = WidgetKind.Stats, Stats = StatsStyle.Gauge, Sensors = new List<SensorId> { SensorId.GpuTemp, SensorId.GpuLoad }, Background = BackgroundKind.Solid, Color1 = "#07080D" }, 0));
            shots.Add(("text-two-lines", new ScreenSpec { Widget = WidgetKind.Text, Text = "Good\nmorning", Background = BackgroundKind.Gradient, Color1 = "#1A0F1F", Color2 = "#3A1C40", AccentColor = "#F06A9A", FontWeight = 800 }, 0));
            shots.Add(("text-marquee", new ScreenSpec { Widget = WidgetKind.Text, Text = "Now streaming", Marquee = true, Background = BackgroundKind.Solid, Color1 = "#07080D", FontWeight = 800 }, 2600));
            shots.Add(("picture-fit", new ScreenSpec { Background = BackgroundKind.Picture, MediaPath = art, Fit = FitMode.Fit, Color1 = "#101318" }, 0));
            shots.Add(("picture-zoom", new ScreenSpec { Background = BackgroundKind.Picture, MediaPath = art, Zoom = 2, OffsetX = 0.6, OffsetY = -0.4 }, 0));
            shots.Add(("gif-stats", new ScreenSpec { Background = BackgroundKind.Picture, MediaPath = gif, Dim = 40, Widget = WidgetKind.Stats, Stats = StatsStyle.DualGauge, Sensors = new List<SensorId> { SensorId.CpuTemp, SensorId.GpuTemp } }, 900));
            shots.Add(("nothing-playing", new ScreenSpec { Widget = WidgetKind.NowPlaying, Background = BackgroundKind.Solid, Color1 = "#07080D" }, 0));

            foreach (var (name, spec, t) in shots)
            {
                // Pictures load in the background: wait for them so the preview isn't a spinner.
                if (spec.Background == BackgroundKind.Picture && !string.IsNullOrEmpty(spec.MediaPath))
                    cache.Get(spec.MediaPath, spec.Fit, spec.Zoom, spec.OffsetX, spec.OffsetY, wait: true);
                var data = name == "nothing-playing" ? new LiveData { FixedTime = live.FixedTime } : live;
                using var img = r.Render(spec, data, t);
                Save(img, name);
                var jpeg = Renderer.ToJpeg(img, 85);
                if (jpeg.Length > 250 * 1016) { Console.WriteLine("TOO BIG for the cooler: " + name + " " + jpeg.Length); failures++; }
            }

            using (var a = r.RenderAlert(SensorId.CpuTemp, 93, live, 400)) Save(a, "alert");
            using (var tp = r.RenderTestPattern(0)) Save(tp, "test-pattern");
            using (var m = r.RenderMessage("Paused", "The cooler is showing its own screen")) Save(m, "message");

            // A strip of frames from the GIF screen, to check the animation moves.
            for (int i = 0; i < 4; i++)
                using (var f = r.Render(new ScreenSpec { Background = BackgroundKind.Picture, MediaPath = gif }, live, i * 250))
                    Save(f, "gif-frame-" + i);

            ContactSheet();
            Console.WriteLine(failures == 0 ? "All good." : failures + " problem(s).");
            return failures == 0 ? 0 : 1;
        }

        private static string Slug(string s) => new string(s.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-').Replace("--", "-");

        private static void Save(SKImage img, string name)
        {
            File.WriteAllBytes(Path.Combine(_out, name + ".png"), Renderer.ToPng(img));
            Console.WriteLine("  " + name);
        }

        private static LiveData SampleData()
        {
            var d = new LiveData { FixedTime = new DateTime(2026, 10, 21, 22, 47, 38) };
            var rng = new Random(7);
            // Two minutes of believable history so the graphs have a shape.
            for (int i = 0; i < 120; i++)
            {
                double wave = Math.Sin(i / 14.0) * 6 + Math.Sin(i / 5.0) * 2;
                d.Set(SensorId.CpuTemp, 58 + wave + rng.NextDouble() * 1.5);
                d.Set(SensorId.GpuTemp, 64 + wave * 0.8 + rng.NextDouble());
                d.Set(SensorId.CpuLoad, 28 + wave * 3 + rng.NextDouble() * 6);
                d.Set(SensorId.GpuLoad, 71 + wave * 2 + rng.NextDouble() * 4);
                d.Set(SensorId.RamLoad, 46 + rng.NextDouble());
                d.Set(SensorId.VramLoad, 38 + rng.NextDouble());
                d.Set(SensorId.CpuPower, 62 + wave * 4);
                d.Set(SensorId.GpuPower, 211 + wave * 9);
                d.Set(SensorId.CpuClock, 4650 + wave * 20);
                d.Set(SensorId.BoardTemp, 39);
                d.Set(SensorId.DriveTemp, 41);
                d.Set(SensorId.NetDown, 3_400_000 + wave * 100_000);
                d.Set(SensorId.NetUp, 220_000);
            }
            d.Set(SensorId.CpuTemp, 61);
            d.Set(SensorId.GpuTemp, 79);   // warm, so the colour-by-value shows amber
            d.Weather = new WeatherNow { Place = "London", Temperature = 12.4, High = 14, Low = 7, Code = 2, IsDay = true, Updated = DateTime.Now };
            var art = SKImage.FromEncodedData(File.ReadAllBytes(MakeArt(Path.Combine(_out, "_cover.png"), true)));
            d.Media = new NowPlaying
            {
                Title = "Midnight Static", Artist = "The Long Weekends", Album = "Signals", Playing = true,
                Duration = TimeSpan.FromSeconds(214), Position = TimeSpan.FromSeconds(83), PositionAt = DateTime.UtcNow,
                Art = art, ArtBlurred = BlurFill(art)
            };
            return d;
        }

        private static SKImage BlurFill(SKImage art)
        {
            using var s = SKSurface.Create(new SKImageInfo(480, 480));
            using var p = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(28, 28) };
            s.Canvas.DrawImage(art, new SKRect(-40, -40, 520, 520), p);
            return s.Snapshot();
        }

        /// <summary>Abstract artwork (no photos needed): soft blobs of colour.</summary>
        private static string MakeArt(string path, bool cover = false)
        {
            int w = cover ? 600 : 960, h = cover ? 600 : 640;
            using var s = SKSurface.Create(new SKImageInfo(w, h));
            var c = s.Canvas;
            c.Clear(cover ? new SKColor(0x1B, 0x10, 0x2E) : new SKColor(0x0B, 0x1A, 0x2B));
            var rng = new Random(cover ? 3 : 11);
            SKColor[] palette = cover
                ? new[] { new SKColor(0xF0, 0x6A, 0x9A), new SKColor(0xFF, 0xB5, 0x47), new SKColor(0x5B, 0x8C, 0xFF) }
                : new[] { new SKColor(0x5E, 0xD3, 0xC3), new SKColor(0x5B, 0x8C, 0xFF), new SKColor(0xA0, 0x7C, 0xF0), new SKColor(0xFF, 0xB5, 0x47) };
            for (int i = 0; i < 9; i++)
            {
                var col = palette[i % palette.Length];
                float x = (float)rng.NextDouble() * w, y = (float)rng.NextDouble() * h, r = 120 + (float)rng.NextDouble() * 220;
                using var shader = SKShader.CreateRadialGradient(new SKPoint(x, y), r, new[] { col.WithAlpha(200), col.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
                using var p = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Screen };
                c.DrawCircle(x, y, r, p);
            }
            // A horizon of hills so zoom and position are easy to see.
            using (var hills = new SKPath())
            {
                hills.MoveTo(0, h * 0.72f);
                for (int x = 0; x <= w; x += 20) hills.LineTo(x, h * 0.72f - (float)(Math.Sin(x / 90.0) * 30 + Math.Sin(x / 33.0) * 10));
                hills.LineTo(w, h); hills.LineTo(0, h); hills.Close();
                using var p = new SKPaint { Color = new SKColor(0x05, 0x07, 0x0C, 220), IsAntialias = true };
                c.DrawPath(hills, p);
            }
            using var img = s.Snapshot();
            File.WriteAllBytes(path, img.Encode(SKEncodedImageFormat.Png, 100).ToArray());
            return path;
        }

        /// <summary>An animated GIF made with ffmpeg if it's there, otherwise skipped.</summary>
        private static string MakeGif(string path)
        {
            if (File.Exists(path)) return path;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("ffmpeg",
                    "-y -loglevel error -f lavfi -i \"mandelbrot=s=320x320:rate=12\" -t 3 -vf \"hue=h=t*40,format=rgb24\" \"" + path + "\"")
                { UseShellExecute = false };
                using var p = System.Diagnostics.Process.Start(psi);
                p.WaitForExit(60000);
            }
            catch (Exception ex) { Console.WriteLine("No ffmpeg: " + ex.Message); }
            return path;
        }

        /// <summary>Checks the GIF loader composites frames: a 2-frame GIF where frame 2 only changes part of the picture.</summary>
        private static int TestGif(MediaCache cache)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "testdata", "test.gif");
            if (!File.Exists(path)) { Console.WriteLine("GIF test FAILED: test.gif missing"); return 1; }
            var pic = cache.Get(path, FitMode.Stretch, 1, 0, 0, wait: true);
            if (pic == null) { Console.WriteLine("GIF test FAILED: couldn't load: " + cache.ErrorFor(path)); return 1; }
            if (pic.Count != 3) { Console.WriteLine("GIF test FAILED: expected 3 frames, got " + pic.Count); return 1; }
            using var bmp = SKBitmap.FromImage(pic.Frame(2));
            // Frame 3 only paints the right half blue; the left half must still be red from frame 1.
            var left = bmp.GetPixel(bmp.Width / 4, bmp.Height / 2);
            var right = bmp.GetPixel(bmp.Width * 3 / 4, bmp.Height / 2);
            bool ok = left.Red > 200 && left.Blue < 60 && right.Blue > 200 && right.Red < 60;
            Console.WriteLine("GIF test " + (ok ? "passed" : "FAILED") + ": frames=" + pic.Count + " total=" + pic.TotalMs + "ms left=" + left + " right=" + right);
            return ok ? 0 : 1;
        }

        private static void ContactSheet()
        {
            var files = Directory.GetFiles(_out, "*.png").Where(f => !Path.GetFileName(f).StartsWith("_") && !f.EndsWith("contact-sheet.png")).OrderBy(f => f).ToList();
            int cols = 6, cell = 240, pad = 20, label = 26;
            int rows = (files.Count + cols - 1) / cols;
            using var s = SKSurface.Create(new SKImageInfo(cols * (cell + pad) + pad, rows * (cell + pad + label) + pad));
            var c = s.Canvas;
            c.Clear(new SKColor(0x12, 0x14, 0x19));
            using var tp = new SKPaint { Color = new SKColor(200, 205, 215), TextSize = 14, IsAntialias = true, Typeface = SKTypeface.FromFamilyName("DejaVu Sans") };
            for (int i = 0; i < files.Count; i++)
            {
                int x = pad + (i % cols) * (cell + pad), y = pad + (i / cols) * (cell + pad + label);
                using var img = SKImage.FromEncodedData(File.ReadAllBytes(files[i]));
                c.Save();
                using (var clip = new SKPath()) { clip.AddCircle(x + cell / 2f, y + cell / 2f, cell / 2f); c.ClipPath(clip, SKClipOperation.Intersect, true); }
                using (var p = new SKPaint { FilterQuality = SKFilterQuality.High }) c.DrawImage(img, new SKRect(x, y, x + cell, y + cell), p);
                c.Restore();
                c.DrawText(Path.GetFileNameWithoutExtension(files[i]), x, y + cell + 18, tp);
            }
            using var sheet = s.Snapshot();
            File.WriteAllBytes(Path.Combine(_out, "contact-sheet.png"), sheet.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        }
    }
}
