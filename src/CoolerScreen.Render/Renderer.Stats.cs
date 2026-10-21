using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace CoolerScreen.Render
{
    public sealed partial class Renderer
    {
        private const float GaugeStart = 135, GaugeSweep = 270;

        private static double Fraction(SensorId s, double? v)
        {
            if (v == null || double.IsNaN(v.Value)) return 0;
            var (min, max) = Sensors.Range(s);
            return Math.Max(0, Math.Min(1, (v.Value - min) / (max - min)));
        }

        /// <summary>The colour for a reading: the accent, amber when warm, red when hot.</summary>
        private static SKColor LevelColor(Ctx x, SensorId s, double? v, SKColor normal)
        {
            if (!x.S.ColourByValue || v == null) return normal;
            var lv = Sensors.Levels(s);
            if (lv == null) return normal;
            if (v.Value >= lv.Value.crit) return Red;
            if (v.Value >= lv.Value.warn) return Amber;
            return normal;
        }

        private static string LabelFor(SensorId s) => (Sensors.Short(s) + " " + Sensors.Kind(s)).Trim();

        private List<SensorId> Pick(Ctx x, int max)
        {
            var list = (x.S.Sensors ?? new List<SensorId>()).Take(max).ToList();
            if (list.Count == 0) list.Add(SensorId.CpuTemp);
            return list;
        }

        private void DrawStats(SKCanvas c, Ctx x)
        {
            switch (x.S.Stats)
            {
                case StatsStyle.Gauge: StatsGauge(c, x); break;
                case StatsStyle.DualGauge: StatsDual(c, x); break;
                case StatsStyle.Grid: StatsGrid(c, x); break;
                case StatsStyle.BigNumber: StatsBig(c, x); break;
                case StatsStyle.Bars: StatsBars(c, x); break;
                case StatsStyle.Graph: StatsGraph(c, x); break;
            }
        }

        private static void Arc(SKCanvas c, float radius, float width, double fraction, SKColor color, SKColor track, bool glow)
        {
            var rect = new SKRect(C - radius, C - radius, C + radius, C + radius);
            using (var tp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, Color = track })
                c.DrawArc(rect, GaugeStart, GaugeSweep, false, tp);
            float sweep = (float)(GaugeSweep * Math.Max(0, Math.Min(1, fraction)));
            if (sweep < 0.5f) return;
            if (glow) Glow(c, g => c.DrawArc(rect, GaugeStart, sweep, false, g), color, width, 10);
            // A gentle gradient along the arc: slightly darker at the start.
            var start = Mix(color, SKColors.Black, 0.25);
            using var shader = SKShader.CreateSweepGradient(new SKPoint(C, C), new[] { start, color, color }, new[] { 0f, (float)((GaugeSweep * Math.Max(0.05, fraction)) / 360), 1f }, SKShaderTileMode.Clamp, GaugeStart, GaugeStart + 360);
            using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, Color = color, Shader = shader };
            c.DrawArc(rect, GaugeStart, sweep, false, p);
        }

        private void StatsGauge(SKCanvas c, Ctx x)
        {
            var list = Pick(x, 2);
            var s = list[0];
            double? v = x.D?.Get(s);
            var col = LevelColor(x, s, v, x.Accent);
            Arc(c, 204, 26, Fraction(s, v), col, x.Text.WithAlpha(22), true);

            var (val, unit) = Sensors.Format(s, v, x.D?.Fahrenheit ?? false);
            DrawValue(c, val, unit, C, 282, 132, x.S.Font, x.S.FontWeight, x.Text, col, x.Busy);
            if (x.S.ShowLabels)
            {
                using var lp = TextPaint(x, 600, 22, x.Muted);
                DrawSpaced(c, LabelFor(s), C, 160, lp, 4);
            }
            if (list.Count > 1)
            {
                var s2 = list[1];
                double? v2 = x.D?.Get(s2);
                var (val2, unit2) = Sensors.Format(s2, v2, x.D?.Fahrenheit ?? false);
                using var sp = TextPaint(x, 600, 26, LevelColor(x, s2, v2, x.Muted));
                string line = (x.S.ShowLabels ? LabelFor(s2) + "  " : "") + val2 + unit2;
                DrawCentered(c, line, C, 346, sp);
            }
            // The range at each end of the gauge.
            var (min, max) = Sensors.Range(s);
            using var ep = TextPaint(x, 500, 17, x.Muted.WithAlpha(170));
            var lo = Sensors.Format(s, min, x.D?.Fahrenheit ?? false).value;
            var hi = Sensors.Format(s, max, x.D?.Fahrenheit ?? false).value;
            DrawCentered(c, lo, C - 120, 420, ep);
            DrawCentered(c, hi, C + 120, 420, ep);
        }

        private void StatsDual(SKCanvas c, Ctx x)
        {
            var list = Pick(x, 2);
            if (list.Count < 2) list.Add(list[0] == SensorId.GpuTemp ? SensorId.CpuTemp : SensorId.GpuTemp);
            var a = list[0]; var b = list[1];
            double? va = x.D?.Get(a), vb = x.D?.Get(b);
            var ca = LevelColor(x, a, va, x.Accent);
            var cb = LevelColor(x, b, vb, x.Accent2);
            Arc(c, 214, 20, Fraction(a, va), ca, x.Text.WithAlpha(20), true);
            Arc(c, 180, 20, Fraction(b, vb), cb, x.Text.WithAlpha(20), true);

            bool f = x.D?.Fahrenheit ?? false;
            var (fa, ua) = Sensors.Format(a, va, f);
            var (fb, ub) = Sensors.Format(b, vb, f);
            using var la = TextPaint(x, 700, 19, ca);
            using var lb = TextPaint(x, 700, 19, cb);
            DrawSpaced(c, LabelFor(a), C, 168, la, 3);
            DrawValue(c, fa, ua, C, 240, 78, x.S.Font, x.S.FontWeight, x.Text, ca, x.Busy);
            DrawSpaced(c, LabelFor(b), C, 290, lb, 3);
            DrawValue(c, fb, ub, C, 362, 78, x.S.Font, x.S.FontWeight, x.Text, cb, x.Busy);
        }

        private void StatsGrid(SKCanvas c, Ctx x)
        {
            var list = Pick(x, 4);
            bool f = x.D?.Fahrenheit ?? false;
            if (list.Count == 1) { StatsBig(c, x); return; }

            // The cells sit inside the largest square that fits the circle.
            float half = 160;
            using (var line = new SKPaint { IsAntialias = true, Color = x.Text.WithAlpha(26), StrokeWidth = 2 })
            {
                if (list.Count >= 3) c.DrawLine(C - half + 20, C, C + half - 20, C, line);
                c.DrawLine(C, C - half + 20, C, list.Count >= 3 ? C + half - 20 : C + half - 20, line);
            }

            var cells = new List<SKRect>();
            if (list.Count == 2)
            {
                cells.Add(new SKRect(C - half, C - half, C, C + half));
                cells.Add(new SKRect(C, C - half, C + half, C + half));
            }
            else
            {
                cells.Add(new SKRect(C - half, C - half, C, C));
                cells.Add(new SKRect(C, C - half, C + half, C));
                if (list.Count == 3) cells.Add(new SKRect(C - half, C, C + half, C + half));
                else
                {
                    cells.Add(new SKRect(C - half, C, C, C + half));
                    cells.Add(new SKRect(C, C, C + half, C + half));
                }
            }

            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                var r = cells[i];
                double? v = x.D?.Get(s);
                var col = LevelColor(x, s, v, x.Accent);
                var (val, unit) = Sensors.Format(s, v, f);
                float midY = r.MidY + (i < 2 && list.Count > 2 ? 14 : list.Count > 2 ? -10 : 0);
                using var lp = TextPaint(x, 700, 16, x.Muted);
                DrawSpaced(c, LabelFor(s), r.MidX, midY - 40, lp, 2);
                DrawValue(c, val, unit, r.MidX, midY + 20, 62, x.S.Font, x.S.FontWeight, x.Text, col, x.Busy);
                // A small bar under each value.
                float bw = 88;
                using var track = new SKPaint { IsAntialias = true, Color = x.Text.WithAlpha(24), StrokeWidth = 6, StrokeCap = SKStrokeCap.Round };
                c.DrawLine(r.MidX - bw / 2, midY + 44, r.MidX + bw / 2, midY + 44, track);
                double fr = Fraction(s, v);
                if (fr > 0.01)
                {
                    using var bar = new SKPaint { IsAntialias = true, Color = col, StrokeWidth = 6, StrokeCap = SKStrokeCap.Round };
                    c.DrawLine(r.MidX - bw / 2, midY + 44, r.MidX - bw / 2 + (float)(bw * fr), midY + 44, bar);
                }
            }
        }

        private void StatsBig(SKCanvas c, Ctx x)
        {
            var s = Pick(x, 1)[0];
            double? v = x.D?.Get(s);
            var col = LevelColor(x, s, v, x.Accent);
            var (val, unit) = Sensors.Format(s, v, x.D?.Fahrenheit ?? false);
            if (x.S.ShowLabels)
            {
                using var lp = TextPaint(x, 700, 22, col);
                DrawSpaced(c, LabelFor(s), C, 128, lp, 5);
            }
            float size = FitSize(val, x.S.Font, Math.Max(700, x.S.FontWeight), 180, 300);
            DrawValue(c, val, unit, C, 272, size, x.S.Font, Math.Max(700, x.S.FontWeight), x.Text, col, x.Busy);
            Sparkline(c, x, s, new SKRect(C - 140, 312, C + 140, 382), col, 3);
        }

        private void StatsBars(SKCanvas c, Ctx x)
        {
            var list = Pick(x, 4);
            bool f = x.D?.Fahrenheit ?? false;
            float rowH = 78;
            float top = C - list.Count * rowH / 2;
            float w = 300;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                double? v = x.D?.Get(s);
                var col = LevelColor(x, s, v, x.Accent);
                var (val, unit) = Sensors.Format(s, v, f);
                float y = top + i * rowH;
                using var lp = TextPaint(x, 700, 18, x.Muted);
                using var vp = TextPaint(x, Math.Max(600, x.S.FontWeight), 30, x.Text);
                c.DrawText(LabelFor(s), C - w / 2, y + 30, lp);
                string text = val + unit;
                c.DrawText(text, C + w / 2 - vp.MeasureText(text), y + 32, vp);
                using var track = new SKPaint { IsAntialias = true, Color = x.Text.WithAlpha(24) };
                var rr = new SKRect(C - w / 2, y + 44, C + w / 2, y + 58);
                c.DrawRoundRect(rr, 7, 7, track);
                double fr = Fraction(s, v);
                if (fr > 0.01)
                {
                    using var shader = SKShader.CreateLinearGradient(new SKPoint(rr.Left, 0), new SKPoint(rr.Right, 0), new[] { Mix(col, SKColors.Black, 0.3), col }, null, SKShaderTileMode.Clamp);
                    using var bar = new SKPaint { IsAntialias = true, Shader = shader };
                    c.DrawRoundRect(new SKRect(rr.Left, rr.Top, rr.Left + Math.Max(14, (float)(w * fr)), rr.Bottom), 7, 7, bar);
                }
            }
        }

        private void StatsGraph(SKCanvas c, Ctx x)
        {
            var s = Pick(x, 1)[0];
            double? v = x.D?.Get(s);
            var col = LevelColor(x, s, v, x.Accent);
            var (val, unit) = Sensors.Format(s, v, x.D?.Fahrenheit ?? false);
            if (x.S.ShowLabels)
            {
                using var lp = TextPaint(x, 700, 20, x.Muted);
                DrawSpaced(c, LabelFor(s), C, 100, lp, 4);
            }
            DrawValue(c, val, unit, C, 196, 96, x.S.Font, x.S.FontWeight, x.Text, col, x.Busy);

            var area = new SKRect(58, 236, Size - 58, 382);
            c.Save();
            using (var clip = new SKPath()) { clip.AddCircle(C, C, 236); c.ClipPath(clip, SKClipOperation.Intersect, true); }
            using (var grid = new SKPaint { IsAntialias = true, Color = x.Text.WithAlpha(18), StrokeWidth = 1.5f })
                for (int i = 0; i <= 3; i++)
                {
                    float y = area.Top + area.Height * i / 3;
                    c.DrawLine(area.Left - 40, y, area.Right + 40, y, grid);
                }
            Sparkline(c, x, s, area, col, 4, true);
            c.Restore();
            using var cp = TextPaint(x, 500, 16, x.Muted.WithAlpha(180));
            DrawCentered(c, "LAST 2 MINUTES", C, 418, cp);
        }

        /// <summary>A smooth line of recent readings, with a soft fill underneath.</summary>
        private static void Sparkline(SKCanvas c, Ctx x, SensorId s, SKRect area, SKColor col, float width, bool showMinMax = false)
        {
            var hist = x.D?.HistoryOf(s) ?? Array.Empty<double?>();
            var pts = hist.Select((v, i) => (v, i)).Where(p => p.v != null && !double.IsNaN(p.v.Value)).ToList();
            if (pts.Count < 2) return;
            double lo = pts.Min(p => p.v.Value), hi = pts.Max(p => p.v.Value);
            var (rmin, rmax) = Sensors.Range(s);
            double span = Math.Max(hi - lo, (rmax - rmin) * 0.08);
            double mid = (hi + lo) / 2;
            lo = mid - span * 0.6; hi = mid + span * 0.6;
            int n = Math.Max(hist.Length, 60);
            var points = pts.Select(p => new SKPoint(
                area.Left + area.Width * (float)(p.i + (n - hist.Length)) / (n - 1),
                area.Bottom - area.Height * (float)((p.v.Value - lo) / (hi - lo)))).ToList();

            using var path = new SKPath();
            path.MoveTo(points[0]);
            for (int i = 1; i < points.Count; i++)
            {
                var p0 = points[i - 1]; var p1 = points[i];
                float mx = (p0.X + p1.X) / 2;
                path.CubicTo(mx, p0.Y, mx, p1.Y, p1.X, p1.Y);
            }
            using (var fill = new SKPath(path))
            {
                fill.LineTo(points[points.Count - 1].X, area.Bottom + 20);
                fill.LineTo(points[0].X, area.Bottom + 20);
                fill.Close();
                using var shader = SKShader.CreateLinearGradient(new SKPoint(0, area.Top), new SKPoint(0, area.Bottom + 20), new[] { col.WithAlpha(90), col.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
                using var fp = new SKPaint { IsAntialias = true, Shader = shader };
                c.DrawPath(fill, fp);
            }
            Glow(c, g => c.DrawPath(path, g), col, width + 2, 6);
            using (var lp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, Color = col })
                c.DrawPath(path, lp);
            var last = points[points.Count - 1];
            using (var dot = new SKPaint { IsAntialias = true, Color = col })
                c.DrawCircle(last, width + 3, dot);
            using (var dot2 = new SKPaint { IsAntialias = true, Color = x.Bg })
                c.DrawCircle(last, Math.Max(1.5f, width - 1), dot2);
        }
    }
}
