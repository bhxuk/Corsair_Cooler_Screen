using System;
using System.Globalization;
using System.Linq;
using SkiaSharp;

namespace CoolerScreen.Render
{
    public sealed partial class Renderer
    {
        // ------------------------------------------------------------------ text

        private void DrawText(SKCanvas c, Ctx x)
        {
            var s = x.S;
            string text = (s.Text ?? "").Replace("\r", "");
            if (text.Trim().Length == 0) return;

            if (s.Marquee)
            {
                string line = string.Join("   •   ", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
                using var p = TextPaint(x, s.FontWeight, 120, x.Text);
                float w = p.MeasureText(line);
                float gap = 140;
                float span = w + gap;
                float offset = (float)(x.T / 1000 * Math.Max(20, s.MarqueeSpeed) % span);
                float cap = CapHeight(p);
                float baseline = C + cap / 2;
                for (float startX = Size - offset; startX > -span; startX -= span)
                    c.DrawText(line, startX, baseline, p);
                for (float startX = Size - offset + span; startX < Size; startX += span)
                    c.DrawText(line, startX, baseline, p);
                return;
            }

            var lines = text.Split('\n').Select(l => l.TrimEnd()).ToArray();
            // Biggest size where every line fits inside the circle.
            float size = 220;
            for (; size > 14; size -= 2)
            {
                using var p = TextPaint(s.Font, s.FontWeight, size, x.Text);
                float lh = size * 1.12f;
                float total = lh * lines.Length;
                float top = C - total / 2;
                bool ok = true;
                for (int i = 0; i < lines.Length && ok; i++)
                {
                    float mid = top + lh * i + lh / 2;
                    float chord = Math.Min(Chord(mid - lh * 0.35f), Chord(mid + lh * 0.35f)) * 2 - 40;
                    if (p.MeasureText(lines[i]) > chord) ok = false;
                }
                if (ok && total < 380) break;
            }
            using (var p = TextPaint(x, s.FontWeight, size, x.Text))
            {
                float lh = size * 1.12f;
                float top = C - lh * lines.Length / 2;
                float cap = CapHeight(p);
                for (int i = 0; i < lines.Length; i++)
                {
                    var paint = p;
                    if (i > 0 && lines.Length > 1 && i == lines.Length - 1 && !string.IsNullOrEmpty(s.AccentColor)) paint.Color = x.Accent;
                    DrawCentered(c, lines[i], C, top + lh * i + lh / 2 + cap / 2, paint);
                }
            }
        }

        // ------------------------------------------------------------------ countdown

        private void DrawCountdown(SKCanvas c, Ctx x)
        {
            var s = x.S;
            var now = x.D?.Now ?? DateTime.Now;
            TimeSpan left = s.CountUp ? now - s.Target : s.Target - now;
            bool done = !s.CountUp && left <= TimeSpan.Zero;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;

            // The ring shows how far through the current minute we are.
            double minuteFrac = s.CountUp ? now.Second / 60.0 : (60 - now.Second) / 60.0;
            if (!done)
            {
                var rect = new SKRect(C - 222, C - 222, C + 222, C + 222);
                using var track = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8, Color = x.Text.WithAlpha(20) };
                c.DrawCircle(C, C, 222, track);
                using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8, StrokeCap = SKStrokeCap.Round, Color = x.Accent };
                c.DrawArc(rect, -90, (float)(360 * minuteFrac), false, ring);
            }

            string label = string.IsNullOrWhiteSpace(s.Label1) ? (s.CountUp ? "SINCE" : "COUNTDOWN") : s.Label1.ToUpperInvariant();
            using (var lp = TextPaint(x, 700, 22, x.Accent))
                DrawSpaced(c, label, C, 130, lp, 5);

            if (done)
            {
                using var dp = TextPaint(x, Math.Max(700, s.FontWeight), 92, x.Text);
                DrawCentered(c, "It's here", C, C + 30, dp);
                using var sp = TextPaint(x, 500, 24, x.Muted);
                DrawCentered(c, s.Target.ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture), C, C + 80, sp);
                return;
            }

            int days = (int)Math.Floor(left.TotalDays);
            string hms = left.Hours.ToString("00") + ":" + left.Minutes.ToString("00") + ":" + left.Seconds.ToString("00");
            if (days > 0)
            {
                string d = days.ToString(CultureInfo.InvariantCulture);
                float size = FitSize(d, s.Font, Math.Max(700, s.FontWeight), 160, 280);
                using var dp = TextPaint(x, Math.Max(700, s.FontWeight), size, x.Text);
                DrawCentered(c, d, C, 268, dp);
                using var up = TextPaint(x, 700, 20, x.Muted);
                DrawSpaced(c, days == 1 ? "DAY" : "DAYS", C, 302, up, 5);
                using var hp = TextPaint(x, 600, 40, x.Text.WithAlpha(220));
                DrawCentered(c, hms, C, 366, hp);
            }
            else
            {
                float size = FitSize(hms, s.Font, s.FontWeight, 96, 330);
                using var hp = TextPaint(x, s.FontWeight, size, x.Text);
                DrawCentered(c, hms, C, C + CapHeight(hp) / 2, hp);
                using var sp = TextPaint(x, 500, 22, x.Muted);
                DrawCentered(c, s.Target.ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture), C, C + 80, sp);
            }
        }

        // ------------------------------------------------------------------ now playing

        private void DrawNowPlaying(SKCanvas c, Ctx x)
        {
            var m = x.D?.Media;
            if (m == null || string.IsNullOrWhiteSpace(m.Title))
            {
                DrawNote(c, C, 200, 70, x.Muted.WithAlpha(160));
                using var p = TextPaint(x, 600, 28, x.Muted);
                DrawCentered(c, "Nothing playing", C, 320, p);
                return;
            }

            // Progress round the edge of the screen.
            if (m.Duration > TimeSpan.Zero)
            {
                double frac = Math.Max(0, Math.Min(1, m.LivePosition.TotalSeconds / m.Duration.TotalSeconds));
                var rect = new SKRect(C - 226, C - 226, C + 226, C + 226);
                using var track = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8, Color = x.Text.WithAlpha(34) };
                c.DrawCircle(C, C, 226, track);
                using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 8, StrokeCap = SKStrokeCap.Round, Color = x.Accent };
                if (frac > 0.002) c.DrawArc(rect, -90, (float)(360 * frac), false, ring);
            }

            var art = new SKRect(C - 92, 74, C + 92, 258);
            if (m.Art != null)
            {
                using (var sh = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 150), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14) })
                    c.DrawRoundRect(new SKRect(art.Left, art.Top + 10, art.Right, art.Bottom + 10), 18, 18, sh);
                c.Save();
                using (var rr = new SKRoundRect(art, 18)) c.ClipRoundRect(rr, SKClipOperation.Intersect, true);
                using (var ip = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
                {
                    // Square crop from the middle of the artwork.
                    float sz = Math.Min(m.Art.Width, m.Art.Height);
                    var src = new SKRect((m.Art.Width - sz) / 2, (m.Art.Height - sz) / 2, (m.Art.Width + sz) / 2, (m.Art.Height + sz) / 2);
                    c.DrawImage(m.Art, src, art, ip);
                }
                c.Restore();
            }
            else
            {
                using (var box = new SKPaint { IsAntialias = true, Color = x.Text.WithAlpha(22) })
                    c.DrawRoundRect(art, 18, 18, box);
                DrawNote(c, art.MidX, art.MidY, 60, x.Muted);
            }

            using (var tp = TextPaint(x, 700, 30, x.Text))
                DrawCentered(c, Ellipsize(m.Title, tp, 330), C, 304, tp);
            if (!string.IsNullOrWhiteSpace(m.Artist))
                using (var ap = TextPaint(x, 500, 22, x.Muted))
                    DrawCentered(c, Ellipsize(m.Artist, ap, 300), C, 338, ap);
            if (m.Duration > TimeSpan.Zero)
            {
                using var tp2 = TextPaint(x, 600, 18, x.Muted.WithAlpha(210));
                string time = Clock(m.LivePosition) + "  /  " + Clock(m.Duration);
                DrawCentered(c, (m.Playing ? "" : "Paused   ") + time, C, 384, tp2);
            }
            else if (!m.Playing)
            {
                using var tp2 = TextPaint(x, 600, 18, x.Muted);
                DrawCentered(c, "Paused", C, 384, tp2);
            }
        }

        private static string Clock(TimeSpan t) => t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : ((int)t.TotalMinutes) + ":" + t.Seconds.ToString("00");

        private static void DrawNote(SKCanvas c, float cx, float cy, float size, SKColor col)
        {
            float u = size / 24f;
            using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f * u, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = col };
            using var path = SKPath.ParseSvgPathData("M9 18V5l11-2v13M9 18a3 3 0 1 1-6 0a3 3 0 1 1 6 0M20 16a3 3 0 1 1-6 0a3 3 0 1 1 6 0");
            c.Save();
            c.Translate(cx - 12 * u, cy - 12 * u);
            c.Scale(u);
            p.StrokeWidth = 2.2f;
            c.DrawPath(path, p);
            c.Restore();
        }

        // ------------------------------------------------------------------ weather

        private void DrawWeather(SKCanvas c, Ctx x)
        {
            var w = x.D?.Weather;
            bool f = x.D?.Fahrenheit ?? false;
            if (w == null)
            {
                using var p = TextPaint(x, 600, 26, x.Muted);
                DrawCentered(c, string.IsNullOrWhiteSpace(x.S.Place) ? "Choose your town" : "Getting the weather…", C, C, p);
                return;
            }
            string place = string.IsNullOrWhiteSpace(w.Place) ? x.S.Place : w.Place;
            if (!string.IsNullOrWhiteSpace(place))
                using (var pp = TextPaint(x, 700, 19, x.Muted))
                    DrawSpaced(c, place.ToUpperInvariant(), C, 92, pp, 4);

            DrawWeatherIcon(c, WeatherNow.IconFor(w.Code, w.IsDay), C, 162, 104, x.Accent, x.Text);

            double T(double v) => f ? v * 9 / 5 + 32 : v;
            DrawValue(c, Math.Round(T(w.Temperature)).ToString("0"), "°", C, 312, 112, x.S.Font, x.S.FontWeight, x.Text, x.Accent, x.Busy);
            using (var cp = TextPaint(x, 600, 28, x.Text))
                DrawCentered(c, WeatherNow.Describe(w.Code), C, 356, cp);
            using (var hp = TextPaint(x, 600, 21, x.Muted))
                DrawCentered(c, "H " + Math.Round(T(w.High)) + "°   L " + Math.Round(T(w.Low)) + "°", C, 392, hp);
        }

        private static void DrawWeatherIcon(SKCanvas c, WeatherNow.Icon icon, float cx, float cy, float size, SKColor accent, SKColor text)
        {
            float u = size / 100f;
            var sun = new SKColor(0xFF, 0xC4, 0x4D);
            var cloud = text.WithAlpha(235);
            var rain = new SKColor(0x6E, 0xB6, 0xFF);

            void Sun(float x, float y, float r)
            {
                using var p = new SKPaint { IsAntialias = true, Color = sun };
                c.DrawCircle(x, y, r, p);
                using var ray = new SKPaint { IsAntialias = true, Color = sun, StrokeWidth = 5 * u, StrokeCap = SKStrokeCap.Round };
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    c.DrawLine(x + (float)Math.Cos(a) * (r + 9 * u), y + (float)Math.Sin(a) * (r + 9 * u), x + (float)Math.Cos(a) * (r + 18 * u), y + (float)Math.Sin(a) * (r + 18 * u), ray);
                }
            }
            void Moon(float x, float y, float r)
            {
                using var outer = new SKPath(); outer.AddCircle(x, y, r);
                using var cut = new SKPath(); cut.AddCircle(x + r * 0.55f, y - r * 0.35f, r * 0.9f);
                using var moon = outer.Op(cut, SKPathOp.Difference);
                using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0xF2, 0xE8, 0xC8) };
                if (moon != null) c.DrawPath(moon, p);
            }
            void Cloud(float x, float y, float s, SKColor col)
            {
                using var path = new SKPath();
                path.AddCircle(x - 22 * s, y + 6 * s, 20 * s);
                path.AddCircle(x + 2 * s, y - 8 * s, 28 * s);
                path.AddCircle(x + 26 * s, y + 6 * s, 20 * s);
                path.AddRoundRect(new SKRect(x - 42 * s, y + 4 * s, x + 46 * s, y + 26 * s), 11 * s, 11 * s);
                using var simple = path.Simplify() ?? path;
                using var sh = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 70), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6) };
                c.Save(); c.Translate(0, 5 * s); c.DrawPath(simple, sh); c.Restore();
                using var p = new SKPaint { IsAntialias = true, Color = col };
                c.DrawPath(simple, p);
            }

            switch (icon)
            {
                case WeatherNow.Icon.Sun: Sun(cx, cy, 26 * u); break;
                case WeatherNow.Icon.Moon: Moon(cx, cy, 34 * u); break;
                case WeatherNow.Icon.PartSun: Sun(cx - 16 * u, cy - 14 * u, 20 * u); Cloud(cx + 8 * u, cy + 10 * u, u, cloud); break;
                case WeatherNow.Icon.PartMoon: Moon(cx - 14 * u, cy - 14 * u, 24 * u); Cloud(cx + 8 * u, cy + 10 * u, u, cloud); break;
                case WeatherNow.Icon.Cloud: Cloud(cx, cy, 1.1f * u, cloud); break;
                case WeatherNow.Icon.Fog:
                {
                    using var p = new SKPaint { IsAntialias = true, Color = cloud, StrokeWidth = 8 * u, StrokeCap = SKStrokeCap.Round };
                    c.DrawLine(cx - 40 * u, cy - 18 * u, cx + 40 * u, cy - 18 * u, p);
                    c.DrawLine(cx - 30 * u, cy, cx + 46 * u, cy, p);
                    c.DrawLine(cx - 44 * u, cy + 18 * u, cx + 30 * u, cy + 18 * u, p);
                    break;
                }
                case WeatherNow.Icon.Rain:
                {
                    Cloud(cx, cy - 10 * u, u, cloud);
                    using var p = new SKPaint { IsAntialias = true, Color = rain, StrokeWidth = 6 * u, StrokeCap = SKStrokeCap.Round };
                    for (int i = -1; i <= 1; i++) c.DrawLine(cx + i * 22 * u + 4 * u, cy + 28 * u, cx + i * 22 * u - 4 * u, cy + 44 * u, p);
                    break;
                }
                case WeatherNow.Icon.Snow:
                {
                    Cloud(cx, cy - 10 * u, u, cloud);
                    using var p = new SKPaint { IsAntialias = true, Color = SKColors.White };
                    for (int i = -1; i <= 1; i++) { c.DrawCircle(cx + i * 22 * u, cy + 34 * u, 5 * u, p); c.DrawCircle(cx + i * 22 * u + 11 * u, cy + 48 * u, 4 * u, p); }
                    break;
                }
                case WeatherNow.Icon.Storm:
                {
                    Cloud(cx, cy - 12 * u, u, cloud);
                    using var bolt = new SKPath();
                    bolt.MoveTo(cx + 4 * u, cy + 18 * u);
                    bolt.LineTo(cx - 12 * u, cy + 42 * u);
                    bolt.LineTo(cx + 2 * u, cy + 42 * u);
                    bolt.LineTo(cx - 6 * u, cy + 62 * u);
                    bolt.LineTo(cx + 16 * u, cy + 34 * u);
                    bolt.LineTo(cx + 2 * u, cy + 34 * u);
                    bolt.Close();
                    using var p = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF, 0xC4, 0x4D) };
                    c.DrawPath(bolt, p);
                    break;
                }
            }
            _ = accent;
        }
    }
}
