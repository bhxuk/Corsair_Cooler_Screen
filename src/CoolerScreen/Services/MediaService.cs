using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CoolerScreen.Core;
using CoolerScreen.Render;
using SkiaSharp;
using Windows.Media.Control;

namespace CoolerScreen.Services
{
    /// <summary>
    /// What's playing on the PC (Spotify, a browser, Windows Media Player…), read from the same
    /// media controls Windows shows on the volume pop-up. Only runs while a screen needs it.
    /// </summary>
    public sealed class MediaService : IDisposable
    {
        private readonly LiveData _live;
        private GlobalSystemMediaTransportControlsSessionManager _manager;
        private Timer _timer;
        private string _artKey = "";
        private int _busy;

        public bool Wanted { get; set; }

        public MediaService(LiveData live) { _live = live; }

        public void Start()
        {
            _timer = new Timer(_ => Poll(), null, 500, 1000);
        }

        private async void Poll()
        {
            if (!Wanted || Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = _manager.GetCurrentSession();
                if (session == null) { _live.Media = null; _artKey = ""; return; }

                var props = await session.TryGetMediaPropertiesAsync();
                var timeline = session.GetTimelineProperties();
                var playback = session.GetPlaybackInfo();

                var np = new NowPlaying
                {
                    Title = props?.Title ?? "",
                    Artist = props?.Artist ?? props?.AlbumArtist ?? "",
                    Album = props?.AlbumTitle ?? "",
                    App = session.SourceAppUserModelId ?? "",
                    Playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    Duration = timeline != null ? timeline.EndTime - timeline.StartTime : TimeSpan.Zero,
                    Position = timeline?.Position ?? TimeSpan.Zero,
                    PositionAt = timeline != null ? timeline.LastUpdatedTime.UtcDateTime : DateTime.UtcNow
                };
                if (np.Duration < TimeSpan.Zero) np.Duration = TimeSpan.Zero;

                // Keep the artwork from last time if the song hasn't changed.
                string key = np.Title + "|" + np.Artist + "|" + np.Album;
                var old = _live.Media;
                if (key == _artKey && old != null)
                {
                    np.Art = old.Art;
                    np.ArtBlurred = old.ArtBlurred;
                }
                else if (props?.Thumbnail != null)
                {
                    _artKey = key;
                    try
                    {
                        using var ras = await props.Thumbnail.OpenReadAsync();
                        using var s = ras.AsStreamForRead();
                        using var ms = new MemoryStream();
                        await s.CopyToAsync(ms);
                        using var data = SKData.CreateCopy(ms.ToArray());
                        var art = SKImage.FromEncodedData(data);
                        if (art != null)
                        {
                            np.Art = art.ToRasterImage(true);
                            np.ArtBlurred = Blur(np.Art);
                        }
                    }
                    catch (Exception ex) { Log.Write("Artwork: " + ex.Message); }
                }
                else _artKey = key;
                _live.Media = np;
            }
            catch (Exception ex)
            {
                Log.Write("Now playing: " + ex.Message);
                _manager = null;
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        /// <summary>The artwork blown up to fill the screen and blurred, for the background.</summary>
        private static SKImage Blur(SKImage art)
        {
            var info = new SKImageInfo(Renderer.Size, Renderer.Size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            float sz = Math.Min(art.Width, art.Height);
            var src = new SKRect((art.Width - sz) / 2, (art.Height - sz) / 2, (art.Width + sz) / 2, (art.Height + sz) / 2);
            using var p = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(28, 28), FilterQuality = SKFilterQuality.Medium };
            surface.Canvas.Clear(SKColors.Black);
            surface.Canvas.DrawImage(art, src, new SKRect(-40, -40, Renderer.Size + 40, Renderer.Size + 40), p);
            return surface.Snapshot().ToRasterImage(true);
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }

    /// <summary>Weather from Open-Meteo (free, no account), refreshed every 15 minutes while a screen shows it.</summary>
    public sealed class WeatherService : IDisposable
    {
        private readonly LiveData _live;
        private readonly System.Net.Http.HttpClient _http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private Timer _timer;
        private string _key = "";
        private DateTime _next = DateTime.MinValue;
        private int _busy;

        /// <summary>The place the showing screen wants, set by the display engine.</summary>
        public (string place, double lat, double lon)? Wanted { get; set; }

        public WeatherService(LiveData live)
        {
            _live = live;
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CoolerScreen/" + Updates.CurrentText);
        }

        public void Start() => _timer = new Timer(_ => Poll(), null, 1000, 5000);

        private async void Poll()
        {
            var w = Wanted;
            if (w == null || (w.Value.lat == 0 && w.Value.lon == 0)) return;
            string key = w.Value.lat.ToString("0.000") + "," + w.Value.lon.ToString("0.000");
            if (key == _key && DateTime.UtcNow < _next) return;
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                string url = "https://api.open-meteo.com/v1/forecast?latitude=" + w.Value.lat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "&longitude=" + w.Value.lon.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1";
                using var doc = System.Text.Json.JsonDocument.Parse(await _http.GetStringAsync(url));
                var cur = doc.RootElement.GetProperty("current");
                var daily = doc.RootElement.GetProperty("daily");
                _live.Weather = new WeatherNow
                {
                    Place = w.Value.place,
                    Temperature = cur.GetProperty("temperature_2m").GetDouble(),
                    Code = cur.GetProperty("weather_code").GetInt32(),
                    IsDay = cur.GetProperty("is_day").GetInt32() == 1,
                    High = daily.GetProperty("temperature_2m_max")[0].GetDouble(),
                    Low = daily.GetProperty("temperature_2m_min")[0].GetDouble(),
                    Updated = DateTime.Now
                };
                _key = key;
                _next = DateTime.UtcNow.AddMinutes(15);
            }
            catch (Exception ex)
            {
                Log.Write("Weather: " + ex.Message);
                _next = DateTime.UtcNow.AddMinutes(2);
                _key = key;
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        /// <summary>Looks up a town or city by name. Returns a few matches.</summary>
        public static async Task<System.Collections.Generic.List<(string name, double lat, double lon)>> Search(string text)
        {
            var list = new System.Collections.Generic.List<(string, double, double)>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string url = "https://geocoding-api.open-meteo.com/v1/search?count=6&language=en&format=json&name=" + Uri.EscapeDataString(text.Trim());
                using var doc = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(url));
                if (!doc.RootElement.TryGetProperty("results", out var results)) return list;
                foreach (var r in results.EnumerateArray())
                {
                    string name = r.GetProperty("name").GetString() ?? "";
                    string admin = r.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "";
                    string country = r.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "";
                    string label = name + (admin.Length > 0 && admin != name ? ", " + admin : "") + (country.Length > 0 ? ", " + country : "");
                    list.Add((label, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble()));
                }
            }
            catch (Exception ex) { Log.Write("Place search: " + ex.Message); }
            return list;
        }

        /// <summary>Forget the current weather so it's fetched again now.</summary>
        public void Refresh() { _next = DateTime.MinValue; }

        public void Dispose()
        {
            _timer?.Dispose();
            _http.Dispose();
        }
    }
}
