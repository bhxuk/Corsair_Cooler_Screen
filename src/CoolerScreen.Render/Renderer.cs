using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace CoolerScreen.Render
{
    /// <summary>Draws a screen at 480x480, ready to be sent to the cooler or shown as a preview.</summary>
    public sealed partial class Renderer : IDisposable
    {
        public const int Size = 480;
        public const float C = Size / 2f;

        /// <summary>Swaps a font name for another one, e.g. when rendering previews on a PC without Segoe UI.</summary>
        public static Func<string, string> FontAlias { get; set; } = f => f;

        private static readonly ConcurrentDictionary<string, SKTypeface> Typefaces = new ConcurrentDictionary<string, SKTypeface>();

        private readonly MediaCache _cache;
        private readonly object _gate = new object();
        private SKSurface _surface;

        public static readonly SKColor Amber = new SKColor(0xFF, 0xB5, 0x47);
        public static readonly SKColor Red = new SKColor(0xE2, 0x58, 0x5F);

        public Renderer(MediaCache cache)
        {
            _cache = cache;
        }

        public MediaCache Cache => _cache;

        /// <summary>Draws a frame. t is milliseconds since this screen started showing (moves GIFs and scrolling text).</summary>
        public SKImage Render(ScreenSpec s, LiveData d, double t)
        {
            lock (_gate)
            {
                var c = Begin();
                try { Draw(c, s, d, t); }
                catch (Exception ex) { DrawProblem(c, "Couldn't draw this screen", ex.Message); }
                return End();
            }
        }

        /// <summary>A smaller copy for lists and thumbnails.</summary>
        public static SKImage Scaled(SKImage img, int size)
        {
            var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surf = SKSurface.Create(info);
            using var p = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            surf.Canvas.Clear(SKColors.Transparent);
            surf.Canvas.DrawImage(img, new SKRect(0, 0, size, size), p);
            return surf.Snapshot().ToRasterImage(true);
        }

        public static byte[] ToJpeg(SKImage img, int quality)
        {
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, Math.Max(40, Math.Min(100, quality)));
            return data.ToArray();
        }

        public static byte[] ToPng(SKImage img)
        {
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private SKCanvas Begin()
        {
            if (_surface == null)
                _surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
            var c = _surface.Canvas;
            c.ResetMatrix();
            c.Clear(SKColors.Black);
            return c;
        }

        private SKImage End()
        {
            _surface.Canvas.Flush();
            return _surface.Snapshot().ToRasterImage(true);
        }

        public void Draw(SKCanvas c, ScreenSpec s, LiveData d, double t)
        {
            bool busy = DrawBackground(c, s, d, t);
            if (s.Widget == WidgetKind.None) return;

            var ctx = new Ctx(s, d, t, busy);
            c.Save();
            double scale = Math.Max(0.5, Math.Min(1.5, s.Scale));
            c.Translate(C, C + (float)(Math.Max(-1, Math.Min(1, s.OffsetWidgetY)) * 120));
            c.Scale((float)scale);
            c.Translate(-C, -C);
            switch (s.Widget)
            {
                case WidgetKind.Clock: DrawClock(c, ctx); break;
                case WidgetKind.Stats: DrawStats(c, ctx); break;
                case WidgetKind.Text: DrawText(c, ctx); break;
                case WidgetKind.Countdown: DrawCountdown(c, ctx); break;
                case WidgetKind.NowPlaying: DrawNowPlaying(c, ctx); break;
                case WidgetKind.Weather: DrawWeather(c, ctx); break;
            }
            c.Restore();
        }

        /// <summary>Everything a widget needs while it draws.</summary>
        private sealed class Ctx
        {
            public readonly ScreenSpec S;
            public readonly LiveData D;
            public readonly double T;
            /// <summary>The background is a photo or GIF, so text gets a soft shadow.</summary>
            public readonly bool Busy;
            public readonly SKColor Text, Accent, Muted, Accent2, Bg;

            public Ctx(ScreenSpec s, LiveData d, double t, bool busy)
            {
                S = s; D = d; T = t; Busy = busy && s.Shadow;
                Text = Col(s.TextColor, new SKColor(0xF4, 0xF2, 0xEE));
                Accent = Col(s.AccentColor, new SKColor(0x5E, 0xD3, 0xC3));
                Muted = Col(s.MutedColor, new SKColor(0x9A, 0xA0, 0xAB));
                Bg = Col(s.Color1, new SKColor(0x0B, 0x0D, 0x12));
                Accent2 = Shift(Accent, 150);
            }
        }

        // ------------------------------------------------------------------ backgrounds

        /// <summary>Draws the background. Returns true when it's a picture (so text needs a shadow).</summary>
        private bool DrawBackground(SKCanvas c, ScreenSpec s, LiveData d, double t)
        {
            var c1 = Col(s.Color1, new SKColor(0x0B, 0x0D, 0x12));
            var c2 = Col(s.Color2, new SKColor(0x1D, 0x24, 0x33));
            bool busy = false;

            // Now playing can use the song's artwork as the whole background.
            if (s.Widget == WidgetKind.NowPlaying && s.ArtBackground && d?.Media?.ArtBlurred != null && !string.IsNullOrEmpty(d.Media.Title))
            {
                c.Clear(c1);
                using var p = new SKPaint { FilterQuality = SKFilterQuality.Medium };
                c.DrawImage(d.Media.ArtBlurred, new SKRect(0, 0, Size, Size), p);
                using var shade = new SKPaint { Color = new SKColor(0, 0, 0, 110) };
                c.DrawRect(0, 0, Size, Size, shade);
                busy = true;
            }
            else switch (s.Background)
            {
                case BackgroundKind.Solid:
                    c.Clear(c1);
                    break;
                case BackgroundKind.Gradient:
                    DrawGradient(c, c1, c2, s.GradientAngle);
                    break;
                case BackgroundKind.Picture:
                    c.Clear(c1);
                    busy = DrawPicture(c, s, s.MediaPath, t, 255);
                    break;
                case BackgroundKind.Slideshow:
                    c.Clear(c1);
                    busy = DrawSlideshow(c, s, t);
                    break;
            }

            if (s.Dim > 0)
            {
                byte a = (byte)Math.Round(Math.Max(0, Math.Min(85, s.Dim)) / 100 * 255);
                using var p = new SKPaint { Color = new SKColor(0, 0, 0, a) };
                c.DrawRect(0, 0, Size, Size, p);
            }
            return busy;
        }

        private static void DrawGradient(SKCanvas c, SKColor a, SKColor b, double angle)
        {
            double rad = angle * Math.PI / 180;
            float dx = (float)(Math.Cos(rad) * C), dy = (float)(Math.Sin(rad) * C);
            using var shader = SKShader.CreateLinearGradient(new SKPoint(C - dx, C - dy), new SKPoint(C + dx, C + dy), new[] { a, b }, null, SKShaderTileMode.Clamp);
            using var p = new SKPaint { Shader = shader, IsDither = true };
            c.DrawRect(0, 0, Size, Size, p);
        }

        private bool DrawPicture(SKCanvas c, ScreenSpec s, string path, double t, byte alpha)
        {
            if (string.IsNullOrEmpty(path))
            {
                if (s.Widget == WidgetKind.None) DrawHint(c, s, "Choose a picture or GIF");
                return false;
            }
            var pic = _cache?.Get(path, s.Fit, s.Zoom, s.OffsetX, s.OffsetY);
            if (pic == null)
            {
                string err = _cache?.ErrorFor(path);
                if (err != null) DrawHint(c, s, "Can't open this file", err);
                else DrawSpinner(c, t);
                return false;
            }
            int i = pic.FrameAt(t * Math.Max(0.1, Math.Min(4, s.Speed)));
            var img = pic.Frame(i);
            using var p = new SKPaint { FilterQuality = SKFilterQuality.Medium, Color = new SKColor(255, 255, 255, alpha) };
            c.DrawImage(img, pic.Dest, p);
            return true;
        }

        private readonly Dictionary<string, (List<string> files, DateTime read)> _folders = new Dictionary<string, (List<string>, DateTime)>();

        private List<string> FolderFiles(string folder)
        {
            if (_folders.TryGetValue(folder ?? "", out var f) && (DateTime.UtcNow - f.read).TotalSeconds < 30) return f.files;
            var files = MediaCache.FolderMedia(folder);
            _folders[folder ?? ""] = (files, DateTime.UtcNow);
            return files;
        }

        private bool DrawSlideshow(SKCanvas c, ScreenSpec s, double t)
        {
            var files = FolderFiles(s.FolderPath);
            if (files.Count == 0)
            {
                if (s.Widget == WidgetKind.None) DrawHint(c, s, string.IsNullOrEmpty(s.FolderPath) ? "Choose a folder of pictures" : "No pictures in this folder");
                return false;
            }
            double slideMs = Math.Max(2, s.SlideSeconds) * 1000;
            long n = (long)Math.Floor(t / slideMs);
            double into = t - n * slideMs;
            string Pick(long k)
            {
                if (!s.Shuffle) return files[(int)(((k % files.Count) + files.Count) % files.Count)];
                // A fresh shuffle each time round the folder, the same on every frame.
                long round = (long)Math.Floor((double)k / files.Count);
                int pos = (int)(((k % files.Count) + files.Count) % files.Count);
                var order = Enumerable.Range(0, files.Count).ToArray();
                var rng = new Random(unchecked((int)(round * 7919 + files.Count)));
                for (int i = order.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
                return files[order[pos]];
            }
            string cur = Pick(n);
            // Start loading the next one early so it's ready.
            if (into > slideMs - 2500) _cache?.Get(Pick(n + 1), s.Fit, s.Zoom, s.OffsetX, s.OffsetY);

            const double fade = 700;
            bool drew;
            if (n > 0 && into < fade)
            {
                string prev = Pick(n - 1);
                DrawPicture(c, s, prev, t, 255);
                byte a = (byte)(255 * Ease(into / fade));
                drew = DrawPictureQuiet(c, s, cur, into, a);
            }
            else drew = DrawPictureQuiet(c, s, cur, into, 255);
            return drew;
        }

        private bool DrawPictureQuiet(SKCanvas c, ScreenSpec s, string path, double t, byte alpha)
        {
            var pic = _cache?.Get(path, s.Fit, s.Zoom, s.OffsetX, s.OffsetY);
            if (pic == null) return false;
            var img = pic.Frame(pic.FrameAt(t * Math.Max(0.1, Math.Min(4, s.Speed))));
            using var p = new SKPaint { FilterQuality = SKFilterQuality.Medium, Color = new SKColor(255, 255, 255, alpha) };
            c.DrawImage(img, pic.Dest, p);
            return true;
        }

        private static double Ease(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }

        private void DrawHint(SKCanvas c, ScreenSpec s, string text, string sub = null)
        {
            var muted = Col(s.MutedColor, new SKColor(0x9A, 0xA0, 0xAB));
            using var p = TextPaint(s.Font, 500, 26, muted);
            DrawCentered(c, text, C, C + (sub == null ? 9 : -6), p);
            if (sub != null)
            {
                using var p2 = TextPaint(s.Font, 400, 17, muted.WithAlpha(170));
                DrawCentered(c, Ellipsize(sub, p2, 320), C, C + 26, p2);
            }
        }

        private static void DrawSpinner(SKCanvas c, double t)
        {
            using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 6, StrokeCap = SKStrokeCap.Round, Color = new SKColor(255, 255, 255, 140) };
            float a = (float)(t / 3 % 360);
            c.DrawArc(new SKRect(C - 26, C - 26, C + 26, C + 26), a, 100, false, p);
        }

        // ------------------------------------------------------------------ special screens

        /// <summary>Shown when a temperature is too high: pulsing red with the reading.</summary>
        public SKImage RenderAlert(SensorId sensor, double? value, LiveData d, double t, string font = "Segoe UI")
        {
            lock (_gate)
            {
                var c = Begin();
                double pulse = (Math.Sin(t / 1000 * Math.PI * 1.6) + 1) / 2;
                var deep = new SKColor(0x2A, 0x05, 0x08);
                var hot = new SKColor(0x8C, 0x10, 0x18);
                using (var shader = SKShader.CreateRadialGradient(new SKPoint(C, C), C * 1.05f, new[] { Mix(deep, hot, pulse * 0.8), deep }, null, SKShaderTileMode.Clamp))
                using (var p = new SKPaint { Shader = shader })
                    c.DrawRect(0, 0, Size, Size, p);

                using (var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 10, Color = Red.WithAlpha((byte)(120 + 135 * pulse)) })
                    c.DrawCircle(C, C, 222, ring);

                var (val, unit) = Sensors.Format(sensor, value, d?.Fahrenheit ?? false);
                using (var lp = TextPaint(font, 600, 22, new SKColor(255, 210, 210)))
                    DrawSpaced(c, (Sensors.Short(sensor) + " " + Sensors.Kind(sensor)).Trim(), C, 150, lp, 4);
                DrawValue(c, val, unit, C, 300, 140, font, 700, SKColors.White, new SKColor(255, 200, 200), false);
                using (var sp = TextPaint(font, 600, 26, new SKColor(255, 220, 220)))
                    DrawCentered(c, "Running hot", C, 360, sp);
                return End();
            }
        }

        /// <summary>A pattern for checking the screen: colour wheel, crosshair and the size.</summary>
        public SKImage RenderTestPattern(double t, string font = "Segoe UI")
        {
            lock (_gate)
            {
                var c = Begin();
                c.Clear(new SKColor(0x08, 0x09, 0x0D));
                var colors = new[] { new SKColor(255, 64, 64), new SKColor(255, 200, 40), new SKColor(80, 220, 90), new SKColor(40, 200, 255), new SKColor(90, 90, 255), new SKColor(230, 70, 230), new SKColor(255, 64, 64) };
                float rot = (float)(t / 20 % 360);
                using (var sweep = SKShader.CreateSweepGradient(new SKPoint(C, C), colors, null))
                using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 34, Shader = sweep })
                {
                    c.Save();
                    c.RotateDegrees(rot, C, C);
                    c.DrawCircle(C, C, 218, p);
                    c.Restore();
                }
                using (var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = new SKColor(255, 255, 255, 90) })
                {
                    c.DrawLine(C, 40, C, Size - 40, line);
                    c.DrawLine(40, C, Size - 40, C, line);
                    c.DrawCircle(C, C, 150, line);
                    c.DrawCircle(C, C, 80, line);
                }
                // Greys from black to white across the middle.
                for (int i = 0; i < 8; i++)
                {
                    byte g = (byte)(i * 255 / 7);
                    using var p = new SKPaint { Color = new SKColor(g, g, g) };
                    c.DrawRect(C - 120 + i * 30, C + 30, 30, 30, p);
                }
                using (var tp = TextPaint(font, 700, 44, SKColors.White))
                    DrawCentered(c, "480 × 480", C, C - 30, tp);
                using (var tp2 = TextPaint(font, 500, 20, new SKColor(200, 205, 215)))
                    DrawCentered(c, "Cooler Screen test", C, C + 100, tp2);
                return End();
            }
        }

        /// <summary>A plain message screen (used while the app starts or is paused).</summary>
        public SKImage RenderMessage(string title, string sub, string font = "Segoe UI")
        {
            lock (_gate)
            {
                var c = Begin();
                c.Clear(new SKColor(0x07, 0x08, 0x0D));
                using (var tp = TextPaint(font, 700, 38, new SKColor(0xF4, 0xF2, 0xEE)))
                    DrawCentered(c, title, C, C + 4, tp);
                if (!string.IsNullOrEmpty(sub))
                    using (var sp = TextPaint(font, 500, 20, new SKColor(0x9A, 0xA0, 0xAB)))
                        DrawCentered(c, sub, C, C + 42, sp);
                return End();
            }
        }

        private static void DrawProblem(SKCanvas c, string title, string detail)
        {
            c.Clear(new SKColor(0x10, 0x08, 0x0A));
            using var tp = TextPaint("Segoe UI", 600, 24, SKColors.White);
            DrawCentered(c, title, C, C, tp);
            using var dp = TextPaint("Segoe UI", 400, 15, new SKColor(255, 200, 200));
            DrawCentered(c, Ellipsize(detail ?? "", dp, 340), C, C + 28, dp);
        }

        // ------------------------------------------------------------------ drawing helpers

        public static SKColor Col(string hex, SKColor fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            return SKColor.TryParse(hex.Trim(), out var c) ? c : fallback;
        }

        public static SKColor Mix(SKColor a, SKColor b, double f)
        {
            f = Math.Max(0, Math.Min(1, f));
            return new SKColor((byte)(a.Red + (b.Red - a.Red) * f), (byte)(a.Green + (b.Green - a.Green) * f), (byte)(a.Blue + (b.Blue - a.Blue) * f), (byte)(a.Alpha + (b.Alpha - a.Alpha) * f));
        }

        /// <summary>The same colour with its hue turned, for a second ring that goes with the first.</summary>
        public static SKColor Shift(SKColor c, float degrees)
        {
            c.ToHsl(out float h, out float s, out float l);
            return SKColor.FromHsl((h + degrees) % 360, Math.Max(55, s), Math.Max(58, Math.Min(72, l)));
        }

        public static SKTypeface Face(string family, int weight)
        {
            string fam = FontAlias?.Invoke(family) ?? family;
            string key = fam + "|" + weight;
            return Typefaces.GetOrAdd(key, _ =>
                SKTypeface.FromFamilyName(fam, new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright))
                ?? SKTypeface.Default);
        }

        private static SKPaint TextPaint(string family, int weight, float size, SKColor color, bool shadow = false)
        {
            var p = new SKPaint
            {
                Typeface = Face(family, weight),
                TextSize = size,
                IsAntialias = true,
                SubpixelText = true,
                LcdRenderText = false,
                Color = color,
                TextAlign = SKTextAlign.Left
            };
            if (shadow) p.ImageFilter = SKImageFilter.CreateDropShadow(0, 2, 5, 5, new SKColor(0, 0, 0, 170));
            return p;
        }

        private static SKPaint TextPaint(Ctx x, int weight, float size, SKColor color) => TextPaint(x.S.Font, weight, size, color, x.Busy);

        private static void DrawCentered(SKCanvas c, string text, float cx, float baseline, SKPaint p)
        {
            if (string.IsNullOrEmpty(text)) return;
            float w = p.MeasureText(text);
            c.DrawText(text, cx - w / 2, baseline, p);
        }

        /// <summary>Capitals with extra space between letters, centred.</summary>
        private static void DrawSpaced(SKCanvas c, string text, float cx, float baseline, SKPaint p, float spacing)
        {
            if (string.IsNullOrEmpty(text)) return;
            float total = 0;
            var widths = new float[text.Length];
            for (int i = 0; i < text.Length; i++) { widths[i] = p.MeasureText(text[i].ToString()); total += widths[i]; }
            total += spacing * (text.Length - 1);
            float x = cx - total / 2;
            for (int i = 0; i < text.Length; i++)
            {
                c.DrawText(text[i].ToString(), x, baseline, p);
                x += widths[i] + spacing;
            }
        }

        private static string Ellipsize(string text, SKPaint p, float max)
        {
            if (string.IsNullOrEmpty(text) || p.MeasureText(text) <= max) return text;
            string t = text;
            while (t.Length > 1 && p.MeasureText(t + "…") > max) t = t.Substring(0, t.Length - 1);
            return t.TrimEnd() + "…";
        }

        /// <summary>A number with a smaller unit after it, centred as one. Returns the cap height used.</summary>
        private static float DrawValue(SKCanvas c, string value, string unit, float cx, float baseline, float size, string font, int weight, SKColor color, SKColor unitColor, bool shadow)
        {
            using var vp = TextPaint(font, weight, size, color, shadow);
            using var up = TextPaint(font, Math.Max(400, weight - 200), size * 0.34f, unitColor, shadow);
            float vw = vp.MeasureText(value);
            float uw = string.IsNullOrEmpty(unit) ? 0 : up.MeasureText(unit);
            float gap = uw > 0 ? size * 0.05f : 0;
            float x = cx - (vw + gap + uw) / 2;
            c.DrawText(value, x, baseline, vp);
            if (uw > 0)
            {
                // Unit sits level with the top of the number.
                c.DrawText(unit, x + vw + gap, baseline - CapHeight(vp) + CapHeight(up), up);
            }
            return CapHeight(vp);
        }

        private static float CapHeight(SKPaint p)
        {
            float cap = Math.Abs(p.FontMetrics.CapHeight);
            return cap > 0.5f ? cap : p.TextSize * 0.7f;
        }

        /// <summary>Largest text size (up to max) that fits the width.</summary>
        private static float FitSize(string text, string font, int weight, float maxSize, float width)
        {
            using var p = TextPaint(font, weight, 100, SKColors.White);
            float w = p.MeasureText(string.IsNullOrEmpty(text) ? " " : text);
            if (w <= 0) return maxSize;
            return Math.Min(maxSize, 100 * width / w);
        }

        /// <summary>Half the width of the round screen at a given height, so things can be kept inside it.</summary>
        public static float Chord(float y, float radius = C)
        {
            float dy = Math.Abs(y - C);
            return dy >= radius ? 0 : (float)Math.Sqrt(radius * radius - dy * dy);
        }

        private static void Glow(SKCanvas c, Action<SKPaint> draw, SKColor color, float width, float blur)
        {
            using var g = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round,
                Color = color.WithAlpha(110), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur)
            };
            draw(g);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _surface?.Dispose();
                _surface = null;
            }
        }
    }
}
