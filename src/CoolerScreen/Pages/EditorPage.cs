using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CoolerScreen.Core;
using CoolerScreen.Render;
using CoolerScreen.Services;

namespace CoolerScreen.Pages
{
    /// <summary>Changes one screen, with a live preview that's also shown on the cooler.</summary>
    public sealed class EditorPage
    {
        private readonly HubWindow _hub;
        private readonly ScreenSpec _s;
        private readonly SpecPreview _preview;
        private readonly DispatcherTimer _saveTimer;
        private readonly ContentControl _bgBody = new ContentControl();
        private readonly ContentControl _widgetBody = new ContentControl();
        private readonly ContentControl _lookBody = new ContentControl();

        public static UIElement Build(HubWindow hub, ScreenSpec spec) => new EditorPage(hub, spec).Root;

        public UIElement Root { get; }

        private EditorPage(HubWindow hub, ScreenSpec spec)
        {
            _hub = hub;
            _s = spec;
            _preview = new SpecPreview(spec, 290);
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (o, e) => { _saveTimer.Stop(); Settings.Save(); };

            var engine = App.Instance.Engine;
            engine.Editing = spec;
            engine.Poke();

            // ---------- top bar ----------
            var back = FlatButton.IconOnly(Icons.Back, "Back to screens", () => { Flush(); hub.Navigate("screens"); }, 40);
            var name = UI.Committing(spec.Name, v => { _s.Name = string.IsNullOrWhiteSpace(v) ? "Screen" : v.Trim(); Changed(false); }, 300);
            name.FontSize = 18;
            name.Height = 40;
            var showBtn = FlatButton.Primary("Show on the cooler");
            var showing = UI.Badge("This is on the cooler", Theme.OnAccent, Theme.Accent);
            void UpdateShow()
            {
                bool active = Settings.Current.ActiveId == _s.Id;
                showBtn.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
                showing.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            }
            showBtn.Click += (o, e) => { Flush(); App.Instance.ShowScreen(_s.Id); UpdateShow(); };
            UpdateShow();
            var dup = FlatButton.IconOnly(Icons.Copy, "Duplicate", () =>
            {
                Flush();
                var copy = _s.Clone();
                copy.Name = _s.Name + " copy";
                Settings.Current.Screens.Insert(Settings.Current.Screens.IndexOf(_s) + 1, copy);
                Settings.Save();
                hub.Edit(copy);
            }, 40);
            var del = FlatButton.IconOnly(Icons.Trash, "Delete", () => { Flush(); ScreensPage.Delete(_s, hub); }, 40);
            var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 20) };
            DockPanel.SetDock(back, Dock.Left);
            back.Margin = new Thickness(0, 0, 12, 0);
            bar.Children.Add(back);
            name.Width = double.NaN;
            name.HorizontalAlignment = HorizontalAlignment.Stretch;
            name.MaxWidth = 460;
            bar.Children.Add(name);
            var actions = UI.H(8, showing, showBtn, dup, del);
            actions.HorizontalAlignment = HorizontalAlignment.Center;

            // ---------- sections ----------
            RebuildBackground();
            RebuildWidget();
            RebuildLook();
            var widgetChoice = UI.Choice(new (WidgetKind, string)[]
            {
                (WidgetKind.None, "Nothing"), (WidgetKind.Clock, "Clock"), (WidgetKind.Stats, "PC stats"), (WidgetKind.Text, "Message"),
                (WidgetKind.Countdown, "Countdown"), (WidgetKind.NowPlaying, "Now playing"), (WidgetKind.Weather, "Weather")
            }, _s.Widget, v => { _s.Widget = v; Changed(); RebuildWidget(); RebuildLook(); });
            var bgChoice = UI.Choice(new (BackgroundKind, string)[]
            {
                (BackgroundKind.Solid, "Colour"), (BackgroundKind.Gradient, "Gradient"), (BackgroundKind.Picture, "Picture or GIF"), (BackgroundKind.Slideshow, "Slideshow")
            }, _s.Background, v => { _s.Background = v; Changed(); RebuildBackground(); });

            var sections = UI.V(16,
                UI.Section("Background", "What fills the round screen.", bgChoice, _bgBody),
                UI.Section("On top", "One thing shown over the background.", widgetChoice, _widgetBody),
                _lookBody);

            var leftScroll = new ScrollViewer
            {
                Content = new Border { Padding = new Thickness(40, 30, 24, 40), Child = UI.V(0, bar, sections) },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            // ---------- preview ----------
            var live = new ToggleSwitch(Settings.Current.ShowWhileEditing, "Show on the cooler while editing", on =>
            {
                Settings.Current.ShowWhileEditing = on;
                Settings.Save();
                App.Instance.Engine.Poke();
            });
            var note = UI.Text("Changes are saved as you go.", 12, Theme.Muted);
            note.TextAlignment = TextAlignment.Center;
            var right = UI.V(18, actions, _preview, UI.Row("Show on the cooler while editing", null, live), note);
            right.Width = 340;
            var rightPanel = new Border { Background = Theme.Stage, BorderBrush = Theme.Line, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(22, 30, 22, 20), Child = right };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(leftScroll);
            Grid.SetColumn(rightPanel, 1);
            grid.Children.Add(rightPanel);
            grid.Unloaded += (o, e) => Flush();
            Root = grid;
        }

        private void Changed(bool redraw = true)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
            if (redraw)
            {
                _preview.Refresh();
                App.Instance.Engine.Poke();
            }
        }

        private void Flush()
        {
            if (_saveTimer.IsEnabled) { _saveTimer.Stop(); Settings.Save(); }
        }

        // ------------------------------------------------------------------ background

        private void RebuildBackground()
        {
            var items = new List<UIElement>();
            switch (_s.Background)
            {
                case BackgroundKind.Solid:
                    items.Add(UI.Field("Colour", UI.ColorPicker(_s.Color1, v => { _s.Color1 = v; Changed(); })));
                    break;
                case BackgroundKind.Gradient:
                    items.Add(UI.Field("From", UI.ColorPicker(_s.Color1, v => { _s.Color1 = v; Changed(); })));
                    items.Add(UI.Field("To", UI.ColorPicker(_s.Color2, v => { _s.Color2 = v; Changed(); })));
                    items.Add(UI.Row("Direction", null, UI.Slider(0, 360, _s.GradientAngle, 5, v => Math.Round(v) + "°", v => { _s.GradientAngle = v; Changed(); })));
                    break;
                case BackgroundKind.Picture:
                    items.Add(PictureChooser());
                    if (!string.IsNullOrEmpty(_s.MediaPath)) items.AddRange(FitControls(true));
                    break;
                case BackgroundKind.Slideshow:
                    items.Add(FolderChooser());
                    items.Add(UI.Row("Each picture for", null, UI.Slider(3, 300, _s.SlideSeconds, 1, v => Seconds(v), v => { _s.SlideSeconds = v; Changed(); })));
                    items.Add(UI.Row("Shuffle", "A different order each time round.", new ToggleSwitch(_s.Shuffle, "Shuffle", on => { _s.Shuffle = on; Changed(); })));
                    items.AddRange(FitControls(false));
                    break;
            }
            items.Add(UI.Row("Darken", "Makes text on top easier to read.", UI.Slider(0, 80, _s.Dim, 1, v => Math.Round(v) + "%", v => { _s.Dim = v; Changed(); })));
            _bgBody.Content = UI.V(10, items.ToArray());
        }

        private static string Seconds(double v)
        {
            int s = (int)Math.Round(v);
            return s < 60 ? s + " s" : (s / 60) + " min" + (s % 60 > 0 ? " " + s % 60 + " s" : "");
        }

        private UIElement PictureChooser()
        {
            string file = string.IsNullOrEmpty(_s.MediaPath) ? "No picture chosen" : Path.GetFileName(_s.MediaPath);
            var label = UI.Text(file, 13, Theme.Muted);
            label.VerticalAlignment = VerticalAlignment.Center;
            var choose = FlatButton.IconText(Icons.Image, string.IsNullOrEmpty(_s.MediaPath) ? "Choose a picture or GIF" : "Change", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Choose a picture or GIF",
                    Filter = "Pictures and GIFs|*.gif;*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*"
                };
                if (dlg.ShowDialog() != true) return;
                _s.MediaPath = Settings.ImportMedia(dlg.FileName);
                App.Instance.Cache.Forget(_s.MediaPath);
                _s.Zoom = 1; _s.OffsetX = 0; _s.OffsetY = 0;
                if (string.IsNullOrWhiteSpace(_s.Name) || _s.Name == "Picture" || _s.Name == "New screen") _s.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
                Changed();
                RebuildBackground();
            });
            var status = UI.Text("", 12, Theme.Muted);
            void UpdateStatus()
            {
                if (string.IsNullOrEmpty(_s.MediaPath)) { status.Text = "GIF, PNG, JPEG, WebP or BMP. Animated GIFs and WebPs play at full speed."; return; }
                var cache = App.Instance.Cache;
                string err = cache.ErrorFor(_s.MediaPath);
                if (err != null) { status.Text = "Can't open it: " + err; status.Foreground = Theme.Red; return; }
                if (cache.IsLoading(_s.MediaPath)) { status.Text = "Loading…"; return; }
                var pic = cache.Get(_s.MediaPath, _s.Fit, _s.Zoom, _s.OffsetX, _s.OffsetY);
                if (pic != null)
                    status.Text = pic.SourceWidth + " × " + pic.SourceHeight + (pic.Animated ? " · " + pic.Count + " frames · " + (pic.TotalMs / 1000.0).ToString("0.0") + " s" : "");
            }
            UpdateStatus();
            var box = UI.V(6, UI.H(12, choose, label), status);
            Pics.Every(box, TimeSpan.FromMilliseconds(700), UpdateStatus);
            return box;
        }

        private UIElement FolderChooser()
        {
            int count = MediaCache.FolderMedia(_s.FolderPath).Count;
            string text = string.IsNullOrEmpty(_s.FolderPath) ? "No folder chosen" : _s.FolderPath + "  ·  " + count + (count == 1 ? " picture" : " pictures");
            var label = UI.Text(text, 13, Theme.Muted);
            label.VerticalAlignment = VerticalAlignment.Center;
            var choose = FlatButton.IconText(Icons.Folder, string.IsNullOrEmpty(_s.FolderPath) ? "Choose a folder" : "Change", () =>
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder of pictures and GIFs", UseDescriptionForTitle = true, SelectedPath = _s.FolderPath ?? "" };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                _s.FolderPath = dlg.SelectedPath;
                Changed();
                RebuildBackground();
            });
            return UI.V(6, UI.H(12, choose, label), UI.Text("Pictures added to the folder later are picked up automatically.", 12, Theme.Muted));
        }

        private IEnumerable<UIElement> FitControls(bool picture)
        {
            yield return UI.Row("Fit", "Fill crops to the circle, Fit shows all of it.", UI.Choice(new (FitMode, string)[] { (FitMode.Fill, "Fill"), (FitMode.Fit, "Fit"), (FitMode.Stretch, "Stretch") }, _s.Fit, v => { _s.Fit = v; Changed(); }));
            yield return UI.Row("Zoom", null, UI.Slider(1, 3, _s.Zoom, 0.05, v => v.ToString("0.00") + "×", v => { _s.Zoom = v; Changed(); }));
            yield return UI.Row("Move left or right", null, UI.Slider(-1, 1, _s.OffsetX, 0.05, v => Math.Round(v * 100) + "", v => { _s.OffsetX = v; Changed(); }));
            yield return UI.Row("Move up or down", null, UI.Slider(-1, 1, _s.OffsetY, 0.05, v => Math.Round(v * 100) + "", v => { _s.OffsetY = v; Changed(); }));
            if (!picture || App.Instance.Cache.IsAnimation(_s.MediaPath))
                yield return UI.Row("GIF speed", null, UI.Slider(0.25, 4, _s.Speed, 0.25, v => v.ToString("0.##") + "×", v => { _s.Speed = v; Changed(); }));
            if (_s.Fit == FitMode.Fit)
                yield return UI.Field("Colour around the picture", UI.ColorPicker(_s.Color1, v => { _s.Color1 = v; Changed(); }));
        }

        // ------------------------------------------------------------------ on top

        private void RebuildWidget()
        {
            var items = new List<UIElement>();
            switch (_s.Widget)
            {
                case WidgetKind.None:
                    items.Add(UI.Text("Just the background.", 13, Theme.Muted));
                    break;
                case WidgetKind.Clock: ClockOptions(items); break;
                case WidgetKind.Stats: StatsOptions(items); break;
                case WidgetKind.Text: TextOptions(items); break;
                case WidgetKind.Countdown: CountdownOptions(items); break;
                case WidgetKind.NowPlaying:
                    items.Add(UI.Text("Shows the song playing in Spotify, a browser, Windows Media Player or any app that appears on Windows' volume pop-up.", 13, Theme.Muted));
                    items.Add(UI.Row("Artwork as the background", "Blurred album art behind everything.", new ToggleSwitch(_s.ArtBackground, "Artwork background", on => { _s.ArtBackground = on; Changed(); })));
                    break;
                case WidgetKind.Weather: WeatherOptions(items); break;
            }
            _widgetBody.Content = UI.V(10, items.ToArray());
        }

        private static readonly (string fmt, string text)[] DateFormats =
        {
            ("ddd d MMM", "Tue 21 Oct"), ("dddd d MMMM", "Tuesday 21 October"), ("dddd", "Tuesday"), ("d MMMM yyyy", "21 October 2026"),
            ("dd/MM/yyyy", "21/10/2026"), ("MM/dd/yyyy", "10/21/2026"), ("yyyy-MM-dd", "2026-10-21"), ("MMM d", "Oct 21")
        };

        private static List<(string, string)> Zones(bool local)
        {
            var list = new List<(string, string)>();
            if (local) list.Add(("", "This PC's time"));
            try { foreach (var z in TimeZoneInfo.GetSystemTimeZones()) list.Add((z.Id, z.DisplayName)); } catch { }
            return list;
        }

        private void ClockOptions(List<UIElement> items)
        {
            items.Add(UI.Field("Style", UI.Choice(new (ClockStyle, string)[]
            {
                (ClockStyle.Digital, "Digital"), (ClockStyle.Stacked, "Stacked"), (ClockStyle.Flip, "Flip"), (ClockStyle.SecondsRing, "Seconds ring"),
                (ClockStyle.AnalogClassic, "Analog"), (ClockStyle.AnalogMinimal, "Analog minimal"), (ClockStyle.DualZone, "Two time zones")
            }, _s.Clock, v => { _s.Clock = v; Changed(); RebuildWidget(); })));
            items.Add(UI.Row("24-hour clock", null, new ToggleSwitch(_s.Use24Hour, "24-hour", on => { _s.Use24Hour = on; Changed(); })));
            if (_s.Clock != ClockStyle.SecondsRing && _s.Clock != ClockStyle.Stacked && _s.Clock != ClockStyle.Flip && _s.Clock != ClockStyle.DualZone)
                items.Add(UI.Row("Seconds", null, new ToggleSwitch(_s.ShowSeconds, "Seconds", on => { _s.ShowSeconds = on; Changed(); })));
            if (_s.Clock != ClockStyle.DualZone)
            {
                items.Add(UI.Row("Date", null, new ToggleSwitch(_s.ShowDate, "Date", on => { _s.ShowDate = on; Changed(); })));
                items.Add(UI.Row("Date style", null, UI.Combo(DateFormats.Select(d => (d.fmt, d.text)), _s.DateFormat, v => { _s.DateFormat = v; Changed(); })));
            }
            items.Add(UI.Row(_s.Clock == ClockStyle.DualZone ? "First time zone" : "Time zone", null, UI.Combo(Zones(true), _s.TimeZone ?? "", v => { _s.TimeZone = v; Changed(); }, 300)));
            items.Add(UI.Row(_s.Clock == ClockStyle.DualZone ? "First label" : "Label", "Small text above the time, like a place name.", UI.Committing(_s.Label1, v => { _s.Label1 = v; Changed(); })));
            if (_s.Clock == ClockStyle.DualZone)
            {
                items.Add(UI.Row("Second time zone", null, UI.Combo(Zones(false), _s.SecondTimeZone ?? "", v => { _s.SecondTimeZone = v; Changed(); }, 300)));
                items.Add(UI.Row("Second label", null, UI.Committing(_s.Label2, v => { _s.Label2 = v; Changed(); })));
            }
        }

        private void StatsOptions(List<UIElement> items)
        {
            items.Add(UI.Field("Style", UI.Choice(new (StatsStyle, string)[]
            {
                (StatsStyle.Gauge, "Gauge"), (StatsStyle.DualGauge, "Two rings"), (StatsStyle.Grid, "Grid of four"),
                (StatsStyle.BigNumber, "Big number"), (StatsStyle.Bars, "Bars"), (StatsStyle.Graph, "Graph")
            }, _s.Stats, v => { _s.Stats = v; Changed(); RebuildWidget(); })));

            int slots;
            string[] names;
            int required;
            switch (_s.Stats)
            {
                case StatsStyle.Gauge: slots = 2; required = 1; names = new[] { "Reading", "Small reading underneath" }; break;
                case StatsStyle.DualGauge: slots = 2; required = 2; names = new[] { "Outer ring", "Inner ring" }; break;
                case StatsStyle.Grid: slots = 4; required = 2; names = new[] { "Top left", "Top right", "Bottom left", "Bottom right" }; break;
                case StatsStyle.Bars: slots = 4; required = 1; names = new[] { "First bar", "Second bar", "Third bar", "Fourth bar" }; break;
                default: slots = 1; required = 1; names = new[] { "Reading" }; break;
            }
            var all = Sensors.All.Select(x => ((SensorId?)x, Sensors.Name(x))).ToList();
            for (int i = 0; i < slots; i++)
            {
                int slot = i;
                var options = new List<(SensorId?, string)>();
                if (slot >= required) options.Add((null, "Nothing"));
                options.AddRange(all);
                SensorId? current = slot < _s.Sensors.Count ? _s.Sensors[slot] : (SensorId?)null;
                if (current == null && slot < required) current = slot == 0 ? SensorId.CpuTemp : SensorId.GpuTemp;
                items.Add(UI.Row(names[slot], null, UI.Combo(options, current, v => SetSensor(slot, v), 240)));
            }
            items.Add(UI.Row("Colour by temperature", "Amber when warm, red when hot.", new ToggleSwitch(_s.ColourByValue, "Colour by value", on => { _s.ColourByValue = on; Changed(); })));
            items.Add(UI.Row("Labels", null, new ToggleSwitch(_s.ShowLabels, "Labels", on => { _s.ShowLabels = on; Changed(); })));
            var src = App.Instance.Sensors;
            items.Add(UI.Text(src.SectorConnected && Settings.Current.UseSector
                ? "Temperatures come from Sector."
                : "CPU temperature from Windows is approximate. With Sector installed, Cooler Screen uses its exact readings.", 12, Theme.Muted));
        }

        private void SetSensor(int slot, SensorId? v)
        {
            var list = _s.Sensors.ToList();
            while (list.Count <= slot) list.Add(SensorId.CpuTemp);
            if (v == null)
            {
                // "Nothing" ends the list here.
                list = list.Take(slot).ToList();
            }
            else list[slot] = v.Value;
            _s.Sensors = list;
            Changed();
        }

        private void TextOptions(List<UIElement> items)
        {
            var tb = UI.Input(_s.Text, 420);
            tb.AcceptsReturn = true;
            tb.Height = 90;
            tb.TextWrapping = TextWrapping.Wrap;
            tb.VerticalContentAlignment = VerticalAlignment.Top;
            tb.Padding = new Thickness(10, 8, 10, 8);
            tb.TextChanged += (o, e) => { _s.Text = tb.Text; Changed(); };
            items.Add(UI.Field("Message", tb));
            items.Add(UI.Text("Each line is drawn as big as it fits. A second line uses your accent colour.", 12, Theme.Muted));
            items.Add(UI.Row("Scroll across", "One long line moving right to left.", new ToggleSwitch(_s.Marquee, "Scroll", on => { _s.Marquee = on; Changed(); RebuildWidget(); })));
            if (_s.Marquee)
                items.Add(UI.Row("Scroll speed", null, UI.Slider(30, 300, _s.MarqueeSpeed, 10, v => Math.Round(v) + "", v => { _s.MarqueeSpeed = v; Changed(); })));
        }

        private void CountdownOptions(List<UIElement> items)
        {
            var date = new DatePicker { SelectedDate = _s.Target.Date, Width = 180, Height = 36, FontSize = 14 };
            var time = UI.Committing(_s.Target.ToString("HH:mm"), v =>
            {
                if (TimeSpan.TryParse(v, out var ts)) { _s.Target = _s.Target.Date + ts; Changed(); }
            }, 90);
            date.SelectedDateChanged += (o, e) =>
            {
                if (date.SelectedDate is DateTime d) { _s.Target = d.Date + _s.Target.TimeOfDay; Changed(); }
            };
            items.Add(UI.Row("Date and time", null, UI.H(8, date, time)));
            items.Add(UI.Row("Label", "Shown above the count, like \"Holiday\".", UI.Committing(_s.Label1, v => { _s.Label1 = v; Changed(); })));
            items.Add(UI.Row("Count up instead", "Time since the date, like days since something started.", new ToggleSwitch(_s.CountUp, "Count up", on => { _s.CountUp = on; Changed(); })));
        }

        private void WeatherOptions(List<UIElement> items)
        {
            var current = UI.Text(string.IsNullOrWhiteSpace(_s.Place) ? "No town chosen yet" : _s.Place, 14, Theme.Text, FontWeights.SemiBold);
            var search = UI.Input("", 260);
            var results = new WrapPanel();
            var go = FlatButton.Primary("Find");
            async void Find()
            {
                go.IsEnabled = false;
                results.Children.Clear();
                var found = await WeatherService.Search(search.Text);
                go.IsEnabled = true;
                if (found.Count == 0) { results.Children.Add(UI.Text("Nothing found. Try a nearby town.", 13, Theme.Muted)); return; }
                foreach (var (label, lat, lon) in found)
                {
                    var chip = new Chip(label);
                    chip.Click += (o, e) =>
                    {
                        _s.Place = label;
                        _s.Latitude = lat;
                        _s.Longitude = lon;
                        current.Text = label;
                        results.Children.Clear();
                        App.Instance.Live.Weather = null;
                        App.Instance.Weather.Refresh();
                        Changed();
                    };
                    chip.Margin = new Thickness(0, 0, 8, 8);
                    results.Children.Add(chip);
                }
            }
            go.Click += (o, e) => Find();
            search.KeyDown += (o, e) => { if (e.Key == System.Windows.Input.Key.Enter) Find(); };
            items.Add(UI.Field("Town or city", UI.V(8, current, UI.H(8, search, go), results)));
            items.Add(UI.Text("Weather comes from Open-Meteo, a free service with no account needed. It's checked every 15 minutes. Temperatures follow your °C/°F setting.", 12, Theme.Muted));
        }

        // ------------------------------------------------------------------ look

        private static List<string> _fonts;

        private static List<string> Fonts()
        {
            if (_fonts != null) return _fonts;
            try
            {
                _fonts = System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct().OrderBy(f => f).ToList();
            }
            catch { _fonts = new List<string>(); }
            foreach (var must in new[] { "Segoe UI", "Bahnschrift", "Consolas" }) if (!_fonts.Contains(must)) _fonts.Insert(0, must);
            return _fonts;
        }

        private void RebuildLook()
        {
            if (_s.Widget == WidgetKind.None) { _lookBody.Content = null; return; }
            var items = new List<UIElement>
            {
                UI.Row("Font", null, UI.Combo(Fonts().Select(f => (f, f)), _s.Font, v => { _s.Font = v; Changed(); }, 240)),
                UI.Field("Weight", UI.Choice(new (int, string)[] { (300, "Light"), (400, "Regular"), (600, "Semibold"), (700, "Bold"), (900, "Black") }, _s.FontWeight, v => { _s.FontWeight = v; Changed(); })),
                UI.Field("Text colour", UI.ColorPicker(_s.TextColor, v => { _s.TextColor = v; Changed(); })),
                UI.Field("Accent colour", UI.ColorPicker(_s.AccentColor, v => { _s.AccentColor = v; Changed(); })),
                UI.Field("Soft colour (labels and dates)", UI.ColorPicker(_s.MutedColor, v => { _s.MutedColor = v; Changed(); })),
                UI.Row("Size", null, UI.Slider(0.6, 1.4, _s.Scale, 0.05, v => Math.Round(v * 100) + "%", v => { _s.Scale = v; Changed(); })),
                UI.Row("Move up or down", null, UI.Slider(-1, 1, _s.OffsetWidgetY, 0.05, v => Math.Round(v * 100) + "", v => { _s.OffsetWidgetY = v; Changed(); })),
                UI.Row("Shadow on pictures", "A soft shadow behind text on pictures and GIFs.", new ToggleSwitch(_s.Shadow, "Shadow", on => { _s.Shadow = on; Changed(); }))
            };
            _lookBody.Content = UI.Section("Look", "Fonts, colours and size of what's on top.", items.ToArray());
        }
    }
}
