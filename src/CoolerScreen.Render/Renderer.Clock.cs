using System;
using System.Globalization;
using SkiaSharp;

namespace CoolerScreen.Render
{
    public sealed partial class Renderer
    {
        /// <summary>The time in a time zone. Empty means this PC's own time.</summary>
        public static DateTime TimeIn(LiveData d, string zone)
        {
            var now = d?.Now ?? DateTime.Now;
            if (string.IsNullOrWhiteSpace(zone)) return now;
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
                var utc = d?.FixedTime != null ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(now, DateTimeKind.Unspecified), TimeZoneInfo.Local) : DateTime.UtcNow;
                return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            }
            catch { return now; }
        }

        private static string HourMinute(DateTime t, bool h24) => h24 ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : t.ToString("h:mm", CultureInfo.InvariantCulture);

        private static string DateText(DateTime t, string format)
        {
            try { return t.ToString(string.IsNullOrWhiteSpace(format) ? "ddd d MMM" : format, CultureInfo.CurrentCulture); }
            catch { return t.ToString("ddd d MMM", CultureInfo.CurrentCulture); }
        }

        private void DrawClock(SKCanvas c, Ctx x)
        {
            switch (x.S.Clock)
            {
                case ClockStyle.Digital: ClockDigital(c, x); break;
                case ClockStyle.Stacked: ClockStacked(c, x); break;
                case ClockStyle.Flip: ClockFlip(c, x); break;
                case ClockStyle.SecondsRing: ClockSecondsRing(c, x); break;
                case ClockStyle.AnalogClassic: ClockAnalog(c, x, true); break;
                case ClockStyle.AnalogMinimal: ClockAnalog(c, x, false); break;
                case ClockStyle.DualZone: ClockDual(c, x); break;
            }
        }

        // ------------------------------------------------------------------ digital

        private void ClockDigital(SKCanvas c, Ctx x)
        {
            var s = x.S;
            var t = TimeIn(x.D, s.TimeZone);
            string main = HourMinute(t, s.Use24Hour);
            string ampm = s.Use24Hour ? "" : t.ToString("tt", CultureInfo.InvariantCulture);
            string secs = s.ShowSeconds ? t.ToString("ss", CultureInfo.InvariantCulture) : "";
            bool side = ampm.Length > 0 || secs.Length > 0;

            float size = FitSize(main, s.Font, s.FontWeight, 150, side ? 300 : 360);
            using var mp = TextPaint(x, s.FontWeight, size, x.Text);
            using var sp = TextPaint(x, Math.Min(800, s.FontWeight + 100), size * 0.27f, x.Accent);
            float mw = mp.MeasureText(main);
            float sw = side ? Math.Max(sp.MeasureText(ampm), sp.MeasureText(secs)) : 0;
            float gap = side ? size * 0.08f : 0;
            float cap = CapHeight(mp);
            float baseline = C + cap / 2 - (s.ShowDate ? 12 : 0);
            float left = C - (mw + gap + sw) / 2;
            c.DrawText(main, left, baseline, mp);
            if (side)
            {
                float sx = left + mw + gap;
                float scap = CapHeight(sp);
                if (ampm.Length > 0 && secs.Length > 0)
                {
                    c.DrawText(ampm, sx, baseline - cap + scap, sp);
                    c.DrawText(secs, sx, baseline, sp);
                }
                else if (ampm.Length > 0) c.DrawText(ampm, sx, baseline - cap + scap, sp);
                else c.DrawText(secs, sx, baseline - cap + scap, sp);
            }

            if (!string.IsNullOrWhiteSpace(s.Label1))
            {
                using var lp = TextPaint(x, 600, 20, x.Muted);
                DrawSpaced(c, s.Label1.ToUpperInvariant(), C, baseline - cap - 34, lp, 4);
            }

            // A short accent line under the time.
            using (var bar = new SKPaint { IsAntialias = true, Color = x.Accent, StrokeCap = SKStrokeCap.Round, StrokeWidth = 5 })
                c.DrawLine(C - 26, baseline + 30, C + 26, baseline + 30, bar);

            if (s.ShowDate)
            {
                using var dp = TextPaint(x, 500, 30, x.Muted);
                DrawCentered(c, DateText(t, s.DateFormat), C, baseline + 78, dp);
            }
        }

        private void ClockStacked(SKCanvas c, Ctx x)
        {
            var s = x.S;
            var t = TimeIn(x.D, s.TimeZone);
            string h = s.Use24Hour ? t.ToString("HH", CultureInfo.InvariantCulture) : t.ToString("%h", CultureInfo.InvariantCulture).PadLeft(2, '0');
            string m = t.ToString("mm", CultureInfo.InvariantCulture);
            int weight = Math.Max(s.FontWeight, 700);
            float size = 170;
            using var hp = TextPaint(x, weight, size, x.Text);
            using var mp = TextPaint(x, weight, size, x.Accent);
            float cap = CapHeight(hp);
            float lineGap = 16;
            float top = C - (cap * 2 + lineGap) / 2 - (s.ShowDate ? 18 : 0);
            DrawCentered(c, h, C, top + cap, hp);
            DrawCentered(c, m, C, top + cap * 2 + lineGap, mp);
            float after = top + cap * 2 + lineGap;
            if (!s.Use24Hour || s.ShowDate)
            {
                using var dp = TextPaint(x, 600, 24, x.Muted);
                string line = (s.ShowDate ? DateText(t, s.DateFormat) : "") + (!s.Use24Hour ? (s.ShowDate ? "  ·  " : "") + t.ToString("tt", CultureInfo.InvariantCulture) : "");
                DrawCentered(c, line, C, after + 50, dp);
            }
        }

        private void ClockFlip(SKCanvas c, Ctx x)
        {
            var s = x.S;
            var t = TimeIn(x.D, s.TimeZone);
            string h = s.Use24Hour ? t.ToString("HH", CultureInfo.InvariantCulture) : t.ToString("%h", CultureInfo.InvariantCulture).PadLeft(2, '0');
            string m = t.ToString("mm", CultureInfo.InvariantCulture);
            float cw = 150, ch = 176, gap = 16;
            float top = C - ch / 2 - (s.ShowDate ? 16 : 0);
            var cardColor = Mix(x.Bg, SKColors.White, 0.09);
            var cardDark = Mix(x.Bg, SKColors.White, 0.05);
            for (int i = 0; i < 2; i++)
            {
                float left = C - gap / 2 - cw + i * (cw + gap);
                var rect = new SKRect(left, top, left + cw, top + ch);
                using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 120), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10) })
                    c.DrawRoundRect(new SKRect(rect.Left, rect.Top + 8, rect.Right, rect.Bottom + 8), 16, 16, shadow);
                using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, rect.Top), new SKPoint(0, rect.Bottom), new[] { cardColor, cardColor, cardDark, cardDark }, new[] { 0f, 0.5f, 0.5f, 1f }, SKShaderTileMode.Clamp))
                using (var card = new SKPaint { IsAntialias = true, Shader = shader })
                    c.DrawRoundRect(rect, 16, 16, card);

                string digits = i == 0 ? h : m;
                // Sized on "88" so the digits don't change size from minute to minute.
                float digitSize = FitSize("88", s.Font, Math.Max(700, s.FontWeight), 118, cw - 28);
                using var dp = TextPaint(x, Math.Max(700, s.FontWeight), digitSize, i == 0 ? x.Text : x.Accent);
                float cap = CapHeight(dp);
                DrawCentered(c, digits, rect.MidX, rect.MidY + cap / 2, dp);
                // The split across the middle of each card.
                using var split = new SKPaint { Color = x.Bg.WithAlpha(230), StrokeWidth = 3 };
                c.DrawLine(rect.Left, rect.MidY, rect.Right, rect.MidY, split);
                using var hinge = new SKPaint { IsAntialias = true, Color = Mix(x.Bg, SKColors.White, 0.18) };
                c.DrawCircle(rect.Left + 2, rect.MidY, 4, hinge);
                c.DrawCircle(rect.Right - 2, rect.MidY, 4, hinge);
            }
            if (!string.IsNullOrWhiteSpace(s.Label1))
            {
                using var lp = TextPaint(x, 600, 20, x.Muted);
                DrawSpaced(c, s.Label1.ToUpperInvariant(), C, top - 26, lp, 4);
            }
            if (s.ShowDate || !s.Use24Hour)
            {
                using var dp = TextPaint(x, 600, 26, x.Muted);
                string line = (s.ShowDate ? DateText(t, s.DateFormat) : "") + (!s.Use24Hour ? (s.ShowDate ? "  ·  " : "") + t.ToString("tt", CultureInfo.InvariantCulture) : "");
                DrawCentered(c, line, C, top + ch + 54, dp);
            }
        }

        private void ClockSecondsRing(SKCanvas c, Ctx x)
        {
            var s = x.S;
            var t = TimeIn(x.D, s.TimeZone);
            double sec = t.Second + t.Millisecond / 1000.0;
            // 60 ticks round the edge; the ones already passed this minute are lit.
            for (int i = 0; i < 60; i++)
            {
                double a = (i / 60.0) * Math.PI * 2 - Math.PI / 2;
                bool five = i % 5 == 0;
                float r1 = five ? 196 : 202, r2 = 222;
                bool lit = i < Math.Floor(sec) || (i == 0 && sec >= 0);
                bool head = i == (int)Math.Floor(sec) % 60;
                var col = head ? x.Accent : lit ? x.Accent.WithAlpha(190) : x.Text.WithAlpha(30);
                using var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeWidth = five ? 6 : 4, Color = col };
                var p1 = new SKPoint(C + (float)(Math.Cos(a) * r1), C + (float)(Math.Sin(a) * r1));
                var p2 = new SKPoint(C + (float)(Math.Cos(a) * r2), C + (float)(Math.Sin(a) * r2));
                if (head) Glow(c, g => c.DrawLine(p1, p2, g), x.Accent, 10, 6);
                c.DrawLine(p1, p2, p);
            }

            string main = HourMinute(t, s.Use24Hour);
            float size = FitSize(main, s.Font, s.FontWeight, 120, 280);
            using var mp = TextPaint(x, s.FontWeight, size, x.Text);
            float cap = CapHeight(mp);
            float baseline = C + cap / 2 - (s.ShowDate ? 10 : 0);
            DrawCentered(c, main, C, baseline, mp);
            if (!string.IsNullOrWhiteSpace(s.Label1) || !s.Use24Hour)
            {
                using var lp = TextPaint(x, 600, 20, x.Accent);
                string top = !string.IsNullOrWhiteSpace(s.Label1) ? s.Label1.ToUpperInvariant() : t.ToString("tt", CultureInfo.InvariantCulture);
                DrawSpaced(c, top, C, baseline - cap - 26, lp, 4);
            }
            if (s.ShowDate)
            {
                using var dp = TextPaint(x, 500, 26, x.Muted);
                DrawCentered(c, DateText(t, s.DateFormat), C, baseline + 50, dp);
            }
        }

        // ------------------------------------------------------------------ analog

        private void ClockAnalog(SKCanvas c, Ctx x, bool classic)
        {
            var s = x.S;
            var t = TimeIn(x.D, s.TimeZone);
            double secs = t.Second + (s.ShowSeconds ? t.Millisecond / 1000.0 : 0);
            double mins = t.Minute + secs / 60;
            double hours = t.Hour % 12 + mins / 60;

            if (classic)
            {
                using (var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3, Color = x.Text.WithAlpha(40) })
                    c.DrawCircle(C, C, 226, rim);
                for (int i = 0; i < 60; i++)
                {
                    double a = i / 60.0 * Math.PI * 2 - Math.PI / 2;
                    bool hour = i % 5 == 0;
                    float r1 = hour ? 192 : 206, r2 = 216;
                    using var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeWidth = hour ? 6 : 2, Color = hour ? x.Text.WithAlpha(230) : x.Text.WithAlpha(80) };
                    c.DrawLine(C + (float)(Math.Cos(a) * r1), C + (float)(Math.Sin(a) * r1), C + (float)(Math.Cos(a) * r2), C + (float)(Math.Sin(a) * r2), p);
                }
                using var np = TextPaint(x, 600, 34, x.Text);
                float ncap = CapHeight(np);
                for (int n = 1; n <= 12; n++)
                {
                    double a = n / 12.0 * Math.PI * 2 - Math.PI / 2;
                    float r = 158;
                    DrawCentered(c, n.ToString(CultureInfo.InvariantCulture), C + (float)(Math.Cos(a) * r), C + (float)(Math.Sin(a) * r) + ncap / 2, np);
                }
            }
            else
            {
                for (int i = 0; i < 12; i++)
                {
                    double a = i / 12.0 * Math.PI * 2 - Math.PI / 2;
                    float r = 208;
                    using var p = new SKPaint { IsAntialias = true, Color = i == 0 ? x.Accent : x.Text.WithAlpha(i % 3 == 0 ? (byte)200 : (byte)90) };
                    c.DrawCircle(C + (float)(Math.Cos(a) * r), C + (float)(Math.Sin(a) * r), i == 0 ? 9 : i % 3 == 0 ? 6 : 4, p);
                }
            }

            if (!string.IsNullOrWhiteSpace(s.Label1))
            {
                using var lp = TextPaint(x, 600, 18, x.Muted);
                DrawSpaced(c, s.Label1.ToUpperInvariant(), C, classic ? 168 : 150, lp, 4);
            }
            if (s.ShowDate)
            {
                using var dp = TextPaint(x, 600, 20, classic ? x.Accent : x.Muted);
                DrawSpaced(c, t.ToString("ddd d", CultureInfo.CurrentCulture).ToUpperInvariant(), C, classic ? 330 : 340, dp, 3);
            }

            void Hand(double fraction, float length, float tail, float width, SKColor col, bool shadow)
            {
                double a = fraction * Math.PI * 2 - Math.PI / 2;
                var tip = new SKPoint(C + (float)(Math.Cos(a) * length), C + (float)(Math.Sin(a) * length));
                var back = new SKPoint(C - (float)(Math.Cos(a) * tail), C - (float)(Math.Sin(a) * tail));
                if (shadow)
                {
                    using var sh = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeWidth = width, Color = new SKColor(0, 0, 0, 110), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5) };
                    c.DrawLine(back.X + 2, back.Y + 5, tip.X + 2, tip.Y + 5, sh);
                }
                using var p = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeWidth = width, Color = col };
                c.DrawLine(back, tip, p);
            }

            Hand(hours / 12, classic ? 112 : 120, classic ? 18 : 0, classic ? 12 : 10, x.Text, true);
            Hand(mins / 60, classic ? 172 : 184, classic ? 22 : 0, classic ? 8 : 6, x.Text, true);
            if (s.ShowSeconds) Hand(secs / 60, 196, 34, 3, x.Accent, true);
            using (var cap1 = new SKPaint { IsAntialias = true, Color = s.ShowSeconds ? x.Accent : x.Text })
                c.DrawCircle(C, C, 10, cap1);
            using (var cap2 = new SKPaint { IsAntialias = true, Color = x.Bg })
                c.DrawCircle(C, C, 4, cap2);
        }

        // ------------------------------------------------------------------ two time zones

        private void ClockDual(SKCanvas c, Ctx x)
        {
            var s = x.S;
            var a = TimeIn(x.D, s.TimeZone);
            var b = TimeIn(x.D, s.SecondTimeZone);

            void Row(DateTime t, string label, float baseline, SKColor labelColor, string note)
            {
                string time = HourMinute(t, s.Use24Hour) + (s.Use24Hour ? "" : " " + t.ToString("tt", CultureInfo.InvariantCulture));
                float size = FitSize(time, s.Font, s.FontWeight, 100, 300);
                using var tp = TextPaint(x, s.FontWeight, size, x.Text);
                DrawCentered(c, time, C, baseline, tp);
                float cap = CapHeight(tp);
                using var lp = TextPaint(x, 600, 19, labelColor);
                DrawSpaced(c, (string.IsNullOrWhiteSpace(label) ? "HERE" : label.ToUpperInvariant()) + (string.IsNullOrEmpty(note) ? "" : "  " + note), C, baseline - cap - 18, lp, 3);
            }

            double diff = (b - a).TotalHours;
            string note = Math.Abs(diff) < 0.01 ? "" : (diff > 0 ? "+" : "−") + FormatHours(Math.Abs(diff));
            if (b.Date > a.Date) note += " TOMORROW";
            else if (b.Date < a.Date) note += " YESTERDAY";

            Row(a, s.Label1, 206, x.Accent, "");
            using (var line = new SKPaint { IsAntialias = true, Color = x.Text.WithAlpha(40), StrokeWidth = 2 })
                c.DrawLine(C - 120, 244, C + 120, 244, line);
            Row(b, string.IsNullOrWhiteSpace(s.Label2) ? "THERE" : s.Label2, 360, x.Accent2, note.Trim());
        }

        private static string FormatHours(double h)
        {
            int whole = (int)Math.Floor(h);
            int mins = (int)Math.Round((h - whole) * 60);
            return mins == 0 ? whole + "H" : whole + ":" + mins.ToString("00") + "H";
        }
    }
}
