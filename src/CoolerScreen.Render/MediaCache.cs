using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;

namespace CoolerScreen.Render
{
    /// <summary>A picture or animation, already fitted to the screen.</summary>
    public sealed class Picture : IDisposable
    {
        private readonly SKImage[] _raw;
        private readonly SKData[] _jpeg;
        private readonly int[] _start;
        private SKImage _lastDecoded;
        private int _lastIndex = -1;

        /// <summary>Where the frames are drawn on the 480x480 screen.</summary>
        public SKRect Dest { get; }
        public int Count { get; }
        public int TotalMs { get; }
        public bool Animated => Count > 1;
        public int SourceWidth { get; }
        public int SourceHeight { get; }

        internal Picture(List<SKImage> frames, List<int> durations, SKRect dest, int sw, int sh, bool compress)
        {
            Count = frames.Count;
            Dest = dest;
            SourceWidth = sw;
            SourceHeight = sh;
            _start = new int[Count];
            int t = 0;
            for (int i = 0; i < Count; i++) { _start[i] = t; t += durations[i]; }
            TotalMs = Math.Max(1, t);
            if (compress)
            {
                // Long animations are kept as JPEGs (about 30x smaller) and unpacked one frame at a time.
                _jpeg = frames.Select(f => { var d = f.Encode(SKEncodedImageFormat.Jpeg, 92); f.Dispose(); return d; }).ToArray();
            }
            else _raw = frames.ToArray();
        }

        /// <summary>The frame to show at a moment in the animation.</summary>
        public int FrameAt(double ms)
        {
            if (Count <= 1) return 0;
            double m = ms % TotalMs;
            if (m < 0) m += TotalMs;
            int lo = 0, hi = Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_start[mid] <= m) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        public SKImage Frame(int i)
        {
            i = Math.Max(0, Math.Min(Count - 1, i));
            if (_raw != null) return _raw[i];
            lock (this)
            {
                if (i == _lastIndex && _lastDecoded != null) return _lastDecoded;
                _lastDecoded?.Dispose();
                _lastDecoded = SKImage.FromEncodedData(_jpeg[i]);
                _lastIndex = i;
                return _lastDecoded;
            }
        }

        public void Dispose()
        {
            if (_raw != null) foreach (var f in _raw) f.Dispose();
            if (_jpeg != null) foreach (var f in _jpeg) f.Dispose();
            _lastDecoded?.Dispose();
        }
    }

    /// <summary>Loads pictures and GIFs in the background and keeps the last few ready to draw.</summary>
    public sealed class MediaCache
    {
        public const int MaxFrames = 1500;
        private const long RawBudgetBytes = 160L * 1024 * 1024;

        private readonly object _gate = new object();
        private readonly Dictionary<string, Picture> _ready = new Dictionary<string, Picture>();
        private readonly LinkedList<string> _order = new LinkedList<string>();
        private readonly HashSet<string> _loading = new HashSet<string>();
        private readonly Dictionary<string, string> _errors = new Dictionary<string, string>();
        private readonly Dictionary<string, bool> _animated = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly int _size;
        private readonly int _keep;

        /// <summary>Raised (on a background thread) when something finishes loading.</summary>
        public event Action Loaded;

        public MediaCache(int size = 480, int keep = 6)
        {
            _size = size;
            _keep = keep;
        }

        public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".wbmp" };

        public static bool IsMediaFile(string path) => Extensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

        /// <summary>Quick check from the file header: does it have more than one frame?</summary>
        public bool IsAnimation(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            lock (_gate) if (_animated.TryGetValue(path, out var a)) return a;
            bool result = false;
            try
            {
                using var codec = SKCodec.Create(path);
                result = codec != null && codec.FrameCount > 1;
            }
            catch { }
            lock (_gate) _animated[path] = result;
            return result;
        }

        public string ErrorFor(string path)
        {
            lock (_gate) return _errors.TryGetValue(path ?? "", out var e) ? e : null;
        }

        public bool IsLoading(string path)
        {
            lock (_gate) return _loading.Any(k => k.StartsWith((path ?? "") + "|", StringComparison.Ordinal));
        }

        /// <summary>Returns the fitted picture, or null while it loads (or if it can't be read).</summary>
        public Picture Get(string path, FitMode fit, double zoom, double ox, double oy, bool wait = false)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string key = path + "|" + fit + "|" + zoom.ToString("0.000") + "|" + ox.ToString("0.000") + "|" + oy.ToString("0.000");
            lock (_gate)
            {
                if (_ready.TryGetValue(key, out var p))
                {
                    _order.Remove(key);
                    _order.AddFirst(key);
                    return p;
                }
                if (_errors.ContainsKey(path)) return null;
                if (_loading.Contains(key))
                {
                    if (!wait) return null;
                }
                else _loading.Add(key);
            }

            if (wait)
            {
                var loaded = LoadNow(path, fit, zoom, ox, oy, key);
                return loaded;
            }

            Task.Run(() => LoadNow(path, fit, zoom, ox, oy, key));
            return null;
        }

        private Picture LoadNow(string path, FitMode fit, double zoom, double ox, double oy, string key)
        {
            Picture pic = null;
            try
            {
                lock (_gate) if (_ready.TryGetValue(key, out var existing)) return existing;
                pic = Load(path, fit, zoom, ox, oy, _size);
                lock (_gate)
                {
                    _errors.Remove(path);
                    _ready[key] = pic;
                    _order.AddFirst(key);
                    _animated[path] = pic.Animated;
                    while (_order.Count > _keep)
                    {
                        string old = _order.Last.Value;
                        _order.RemoveLast();
                        if (_ready.TryGetValue(old, out var gone))
                        {
                            _ready.Remove(old);
                            // Give any frame being drawn right now time to finish.
                            Task.Delay(3000).ContinueWith(_ => gone.Dispose());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_gate) _errors[path] = ex.Message;
            }
            finally
            {
                lock (_gate) _loading.Remove(key);
            }
            try { Loaded?.Invoke(); } catch { }
            return pic;
        }

        /// <summary>Forgets a file so it's read again (after it changes on disk).</summary>
        public void Forget(string path)
        {
            lock (_gate)
            {
                _errors.Remove(path ?? "");
                _animated.Remove(path ?? "");
                foreach (var k in _ready.Keys.Where(k => k.StartsWith((path ?? "") + "|", StringComparison.Ordinal)).ToList())
                {
                    var p = _ready[k];
                    _ready.Remove(k);
                    _order.Remove(k);
                    Task.Delay(3000).ContinueWith(_ => p.Dispose());
                }
            }
        }

        // ------------------------------------------------------------------ loading

        private static Picture Load(string path, FitMode fit, double zoom, double ox, double oy, int size)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("The file isn't there any more.", path);
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec == null) throw new InvalidDataException("This file isn't a picture Cooler Screen can read.");

            int sw = codec.Info.Width, sh = codec.Info.Height;
            var origin = codec.EncodedOrigin;
            bool swap = origin == SKEncodedOrigin.LeftTop || origin == SKEncodedOrigin.RightTop || origin == SKEncodedOrigin.RightBottom || origin == SKEncodedOrigin.LeftBottom;
            int ow = swap ? sh : sw, oh = swap ? sw : sh;

            var (src, dest) = Placement(ow, oh, size, fit, zoom, ox, oy);
            var frames = new List<SKImage>();
            var durations = new List<int>();

            int count = Math.Max(1, codec.FrameCount);
            if (count > MaxFrames) count = MaxFrames;
            long bytesPerFrame = (long)Math.Max(1, (int)dest.Width) * Math.Max(1, (int)dest.Height) * 4;
            bool compress = count > 1 && bytesPerFrame * count > RawBudgetBytes;

            var info = new SKImageInfo(sw, sh, SKColorType.Rgba8888, SKAlphaType.Premul);
            if (count == 1)
            {
                using var bmp = new SKBitmap(info);
                var r = codec.GetPixels(info, bmp.GetPixels());
                if (r != SKCodecResult.Success && r != SKCodecResult.IncompleteInput) throw new InvalidDataException("Couldn't read this picture (" + r + ").");
                using var oriented = Orient(bmp, origin);
                frames.Add(Fit(oriented, src, dest, compress));
                durations.Add(1000);
            }
            else
            {
                var fi = codec.FrameInfo;
                // Frames that later frames are drawn on top of. Kept until no longer needed.
                var needed = new HashSet<int>();
                for (int i = 0; i < count; i++) if (fi[i].RequiredFrame >= 0) needed.Add(fi[i].RequiredFrame);
                var keep = new Dictionary<int, SKBitmap>();
                for (int i = 0; i < count; i++)
                {
                    var bmp = new SKBitmap(info);
                    int req = fi[i].RequiredFrame;
                    if (req >= 0 && keep.TryGetValue(req, out var under))
                        under.CopyTo(bmp);
                    else
                        bmp.Erase(SKColors.Transparent);
                    var opts = new SKCodecOptions(i, req >= 0 && keep.ContainsKey(req) ? req : -1);
                    var res = codec.GetPixels(info, bmp.GetPixels(), opts);
                    if (res != SKCodecResult.Success && res != SKCodecResult.IncompleteInput)
                    {
                        bmp.Dispose();
                        if (frames.Count > 0) break;   // a damaged tail: keep what we have
                        throw new InvalidDataException("Couldn't read this animation (" + res + ").");
                    }
                    frames.Add(Fit(bmp, src, dest, compress));
                    int d = fi[i].Duration;
                    durations.Add(d <= 10 ? 100 : d);   // same as browsers: 0 or 10ms means 100ms
                    if (needed.Contains(i)) keep[i] = bmp; else bmp.Dispose();
                    // Drop kept frames nothing later depends on.
                    foreach (var k in keep.Keys.ToList())
                    {
                        bool stillNeeded = false;
                        for (int j = i + 1; j < count && !stillNeeded; j++) if (fi[j].RequiredFrame == k) stillNeeded = true;
                        if (!stillNeeded) { keep[k].Dispose(); keep.Remove(k); }
                    }
                }
                foreach (var b in keep.Values) b.Dispose();
            }
            return new Picture(frames, durations, dest, ow, oh, compress);
        }

        /// <summary>Works out which part of the picture is shown, and where on the screen.</summary>
        public static (SKRect src, SKRect dest) Placement(int w, int h, int size, FitMode fit, double zoom, double ox, double oy)
        {
            zoom = Math.Max(1, Math.Min(4, zoom));
            ox = Math.Max(-1, Math.Min(1, ox));
            oy = Math.Max(-1, Math.Min(1, oy));
            var full = new SKRect(0, 0, w, h);
            if (fit == FitMode.Stretch)
            {
                // Zoom still crops from the middle.
                float cw = (float)(w / zoom), ch = (float)(h / zoom);
                float cx = (float)((w - cw) / 2 * (1 + ox)), cy = (float)((h - ch) / 2 * (1 + oy));
                return (new SKRect(cx, cy, cx + cw, cy + ch), new SKRect(0, 0, size, size));
            }
            double scale = fit == FitMode.Fill ? Math.Max((double)size / w, (double)size / h) : Math.Min((double)size / w, (double)size / h);
            scale *= zoom;
            double dw = w * scale, dh = h * scale;
            // The part of the scaled picture that overflows the screen is cropped, centred, then moved by the offset.
            double overX = Math.Max(0, dw - size), overY = Math.Max(0, dh - size);
            double left = -overX / 2 * (1 + ox) + Math.Max(0, (size - dw) / 2);
            double top = -overY / 2 * (1 + oy) + Math.Max(0, (size - dh) / 2);
            var destFull = new SKRect((float)left, (float)top, (float)(left + dw), (float)(top + dh));
            // Clip to the screen and map back to the source.
            var visible = SKRect.Intersect(destFull, new SKRect(0, 0, size, size));
            float sx0 = (float)((visible.Left - left) / scale), sy0 = (float)((visible.Top - top) / scale);
            float sx1 = (float)((visible.Right - left) / scale), sy1 = (float)((visible.Bottom - top) / scale);
            var src = new SKRect(Math.Max(0, sx0), Math.Max(0, sy0), Math.Min(w, sx1), Math.Min(h, sy1));
            _ = full;
            return (src, visible);
        }

        private static SKImage Fit(SKBitmap frame, SKRect src, SKRect dest, bool forJpeg)
        {
            int dw = Math.Max(1, (int)Math.Round(dest.Width)), dh = Math.Max(1, (int)Math.Round(dest.Height));
            var info = new SKImageInfo(dw, dh, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var c = surface.Canvas;
            c.Clear(forJpeg ? SKColors.Black : SKColors.Transparent);
            using var img = SKImage.FromBitmap(frame);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            c.DrawImage(img, src, new SKRect(0, 0, dw, dh), paint);
            return surface.Snapshot().ToRasterImage(true);
        }

        private static SKBitmap Orient(SKBitmap bmp, SKEncodedOrigin origin)
        {
            if (origin == SKEncodedOrigin.TopLeft || origin == SKEncodedOrigin.Default) return bmp.Copy();
            bool swap = origin == SKEncodedOrigin.LeftTop || origin == SKEncodedOrigin.RightTop || origin == SKEncodedOrigin.RightBottom || origin == SKEncodedOrigin.LeftBottom;
            var outBmp = new SKBitmap(swap ? bmp.Height : bmp.Width, swap ? bmp.Width : bmp.Height);
            using var c = new SKCanvas(outBmp);
            float w = bmp.Width, h = bmp.Height;
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: c.Scale(-1, 1); c.Translate(-w, 0); break;
                case SKEncodedOrigin.BottomRight: c.RotateDegrees(180); c.Translate(-w, -h); break;
                case SKEncodedOrigin.BottomLeft: c.Scale(1, -1); c.Translate(0, -h); break;
                case SKEncodedOrigin.LeftTop: c.Scale(-1, 1); c.RotateDegrees(90); break;                 // transpose
                case SKEncodedOrigin.RightTop: c.Translate(h, 0); c.RotateDegrees(90); break;             // 90° clockwise
                case SKEncodedOrigin.RightBottom: c.Translate(h, w); c.Scale(-1, 1); c.RotateDegrees(-90); break; // transverse
                case SKEncodedOrigin.LeftBottom: c.Translate(0, w); c.RotateDegrees(-90); break;          // 90° anticlockwise
            }
            c.DrawBitmap(bmp, 0, 0);
            return outBmp;
        }

        /// <summary>Pictures and GIFs in a folder, in name order.</summary>
        public static List<string> FolderMedia(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return new List<string>();
                return Directory.EnumerateFiles(folder).Where(IsMediaFile).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { return new List<string>(); }
        }
    }
}
