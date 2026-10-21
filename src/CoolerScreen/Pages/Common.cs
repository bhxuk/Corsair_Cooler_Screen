using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoolerScreen.Core;
using CoolerScreen.Render;
using SkiaSharp;

namespace CoolerScreen.Pages
{
    public static class Pics
    {
        public static BitmapSource ToBitmap(SKImage img)
        {
            var info = new SKImageInfo(img.Width, img.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var buf = new byte[info.BytesSize];
            var pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try { img.ReadPixels(info, pin.AddrOfPinnedObject(), info.RowBytes, 0, 0); }
            finally { pin.Free(); }
            var bs = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Pbgra32, null, buf, info.RowBytes);
            bs.Freeze();
            return bs;
        }

        /// <summary>A screen drawn small, for lists.</summary>
        public static BitmapSource Thumbnail(ScreenSpec s, int size = 160)
        {
            try
            {
                using var full = App.Instance.Engine.RenderPreview(s, 1500);
                using var small = Renderer.Scaled(full, size);
                return ToBitmap(small);
            }
            catch (Exception ex)
            {
                Log.Write("Thumbnail: " + ex.Message);
                return null;
            }
        }

        /// <summary>Runs something on a timer while an element is on screen.</summary>
        public static void Every(FrameworkElement owner, TimeSpan interval, Action tick)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
            timer.Tick += (s, e) => { try { tick(); } catch (Exception ex) { Log.Write("Timer: " + ex.Message); } };
            owner.Loaded += (s, e) => timer.Start();
            owner.Unloaded += (s, e) => timer.Stop();
            if (owner.IsLoaded) timer.Start();
        }

        /// <summary>A round frame like the cooler's cap, holding an image.</summary>
        public static Grid RoundFrame(Image image, double size)
        {
            var g = new Grid { Width = size + 24, Height = size + 24 };
            // The black rim of the pump cap.
            g.Children.Add(new Border
            {
                Width = size + 24, Height = size + 24, CornerRadius = new CornerRadius((size + 24) / 2),
                Background = Theme.Brush(Color.FromRgb(0x05, 0x06, 0x08)),
                BorderBrush = Theme.Ring, BorderThickness = new Thickness(3),
                Effect = Theme.Shadow(40, 0.45, 12)
            });
            image.Width = size;
            image.Height = size;
            image.Clip = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2);
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            g.Children.Add(image);
            return g;
        }
    }

    /// <summary>Shows exactly what the cooler is showing, live.</summary>
    public sealed class LivePreview : Grid
    {
        private readonly WriteableBitmap _wb = new WriteableBitmap(Renderer.Size, Renderer.Size, 96, 96, PixelFormats.Pbgra32, null);
        private readonly byte[] _buf = new byte[Renderer.Size * Renderer.Size * 4];
        private long _version = -1;

        public LivePreview(double size)
        {
            var img = new Image { Source = _wb, Stretch = Stretch.UniformToFill };
            Children.Add(Pics.RoundFrame(img, size));
            Pics.Every(this, TimeSpan.FromMilliseconds(50), Update);
        }

        private void Update()
        {
            var engine = App.Instance.Engine;
            if (engine.FrameVersion == _version) return;
            if (!engine.CopyFrame(_buf, out long v)) return;
            _version = v;
            _wb.WritePixels(new Int32Rect(0, 0, Renderer.Size, Renderer.Size), _buf, Renderer.Size * 4, 0);
        }
    }

    /// <summary>Draws one screen in the app (the editor's preview), whether or not it's on the cooler.</summary>
    public sealed class SpecPreview : Grid
    {
        private readonly WriteableBitmap _wb = new WriteableBitmap(Renderer.Size, Renderer.Size, 96, 96, PixelFormats.Pbgra32, null);
        private readonly byte[] _buf = new byte[Renderer.Size * Renderer.Size * 4];
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private DateTime _next = DateTime.MinValue;
        public ScreenSpec Spec { get; set; }

        public SpecPreview(ScreenSpec spec, double size)
        {
            Spec = spec;
            var img = new Image { Source = _wb, Stretch = Stretch.UniformToFill };
            Children.Add(Pics.RoundFrame(img, size));
            Pics.Every(this, TimeSpan.FromMilliseconds(40), Update);
        }

        /// <summary>Redraw on the next tick (after a change).</summary>
        public void Refresh() => _next = DateTime.MinValue;

        private void Update()
        {
            if (Spec == null) return;
            var engine = App.Instance.Engine;
            bool animated = Spec.IsAnimated(engine.Cache) || engine.Cache.IsLoading(Spec.MediaPath);
            if (DateTime.UtcNow < _next) return;
            _next = DateTime.UtcNow.AddMilliseconds(animated ? 1000.0 / Math.Max(5, Math.Min(30, Settings.Current.Fps)) : 250);
            using var img = engine.RenderPreview(Spec, _clock.Elapsed.TotalMilliseconds);
            var info = new SKImageInfo(Renderer.Size, Renderer.Size, SKColorType.Bgra8888, SKAlphaType.Premul);
            var pin = GCHandle.Alloc(_buf, GCHandleType.Pinned);
            try { img.ReadPixels(info, pin.AddrOfPinnedObject(), info.RowBytes, 0, 0); }
            finally { pin.Free(); }
            _wb.WritePixels(new Int32Rect(0, 0, Renderer.Size, Renderer.Size), _buf, Renderer.Size * 4, 0);
        }
    }
}
