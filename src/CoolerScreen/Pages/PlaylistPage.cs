using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CoolerScreen.Core;
using CoolerScreen.Render;

namespace CoolerScreen.Pages
{
    public static class PlaylistPage
    {
        public static UIElement Build(HubWindow hub)
        {
            var st = Settings.Current;
            var on = new ToggleSwitch(st.PlaylistOn, "Playlist on", v =>
            {
                st.PlaylistOn = v;
                if (v && st.Playlist.Count == 0)
                    foreach (var s in st.Screens.Take(3)) st.Playlist.Add(new PlaylistItem { ScreenId = s.Id, Seconds = 30 });
                Settings.Save();
                App.Instance.Engine.Poke();
                hub.Navigate("playlist");
            });
            var shuffle = new ToggleSwitch(st.PlaylistShuffle, "Shuffle", v => { st.PlaylistShuffle = v; Settings.Save(); });
            var top = UI.Card(UI.V(0,
                UI.Row("Rotate through screens", "When this is off, the cooler shows the screen you chose.", on),
                UI.Divider(),
                UI.Row("Shuffle", "Pick the next screen at random.", shuffle)), 20);

            var list = UI.V(10);
            for (int i = 0; i < st.Playlist.Count; i++) list.Children.Add(ItemRow(i, hub));
            if (st.Playlist.Count == 0) list.Children.Add(UI.Text("No screens in the playlist yet.", 13, Theme.Muted));

            var options = st.Screens.Select(s => (s.Id, s.Name)).ToList();
            string pick = options.Count > 0 ? options[0].Id : "";
            var combo = UI.Combo(options, pick, v => pick = v, 260);
            var add = FlatButton.Primary("Add", () =>
            {
                if (string.IsNullOrEmpty(pick)) return;
                st.Playlist.Add(new PlaylistItem { ScreenId = pick, Seconds = 30 });
                Settings.Save();
                hub.Navigate("playlist");
            });

            var listCard = UI.Section("Screens in the playlist", "Shown in this order, each for as long as you set.", list, UI.Divider(), UI.H(10, combo, add));
            return UI.V(16, top, listCard,
                UI.Text("Automations (too-hot alerts, apps and locking the PC) still take over when they need to.", 12, Theme.Muted));
        }

        private static FrameworkElement ItemRow(int index, HubWindow hub)
        {
            var st = Settings.Current;
            var item = st.Playlist[index];
            var spec = Settings.Screen(item.ScreenId);
            var img = new Image { Source = spec != null ? Pics.Thumbnail(spec, 96) : null, Width = 48, Height = 48, Clip = new EllipseGeometry(new Point(24, 24), 24, 24) };
            var name = UI.Text(spec?.Name ?? "(deleted)", 14, Theme.Text, FontWeights.SemiBold);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Width = 220;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.TextWrapping = TextWrapping.NoWrap;
            var seconds = UI.Slider(5, 600, item.Seconds, 5, v => FormatSecs(v), v => { item.Seconds = (int)v; Settings.Save(); }, 200);
            var up = FlatButton.IconOnly(Icons.Up, "Move up", () => Move(index, -1, hub));
            var down = FlatButton.IconOnly(Icons.Down, "Move down", () => Move(index, 1, hub));
            var remove = FlatButton.IconOnly(Icons.Close, "Remove", () => { st.Playlist.RemoveAt(index); Settings.Save(); hub.Navigate("playlist"); });
            up.IsEnabled = index > 0;
            down.IsEnabled = index < st.Playlist.Count - 1;
            var row = UI.H(14, img, name, seconds, up, down, remove);
            return row;
        }

        private static string FormatSecs(double v)
        {
            int s = (int)Math.Round(v);
            return s < 60 ? s + " s" : (s / 60) + " min" + (s % 60 > 0 ? " " + s % 60 + " s" : "");
        }

        private static void Move(int index, int by, HubWindow hub)
        {
            var list = Settings.Current.Playlist;
            int to = index + by;
            if (to < 0 || to >= list.Count) return;
            (list[index], list[to]) = (list[to], list[index]);
            Settings.Save();
            hub.Navigate("playlist");
        }
    }

    public static class AutomationsPage
    {
        public static UIElement Build(HubWindow hub)
        {
            return UI.V(16, Alerts(hub), Apps(hub), Night(), Lock());
        }

        private static List<(string, string)> ScreenOptions(bool withOff)
        {
            var list = new List<(string, string)>();
            if (withOff) list.Add(("", "Turn the screen off"));
            list.AddRange(Settings.Current.Screens.Select(s => (s.Id, s.Name)));
            return list;
        }

        private static UIElement Alerts(HubWindow hub)
        {
            var st = Settings.Current;
            var rows = UI.V(10);
            for (int i = 0; i < st.Alerts.Count; i++)
            {
                var rule = st.Alerts[i];
                int index = i;
                var en = new ToggleSwitch(rule.Enabled, "Alert on", v => { rule.Enabled = v; Settings.Save(); });
                var sensor = UI.Combo(Sensors.All.Select(s => (s, Sensors.Name(s))), rule.Sensor, v => { rule.Sensor = v; Settings.Save(); hub.Navigate("automations"); }, 220);
                string unit = Sensors.Unit(rule.Sensor, false);
                var above = UI.Committing(rule.Above.ToString("0"), v => { if (double.TryParse(v, out var d)) { rule.Above = d; Settings.Save(); } }, 70);
                var secs = UI.Committing(rule.ForSeconds.ToString(), v => { if (int.TryParse(v, out var n)) { rule.ForSeconds = Math.Max(0, n); Settings.Save(); } }, 60);
                var remove = FlatButton.IconOnly(Icons.Close, "Remove", () => { st.Alerts.RemoveAt(index); Settings.Save(); hub.Navigate("automations"); });
                var line = UI.H(10, en, sensor, Mid("over"), above, Mid(unit + " for"), secs, Mid("seconds"), remove);
                rows.Children.Add(line);
            }
            var add = FlatButton.IconText(Icons.Plus, "Add an alert", () => { st.Alerts.Add(new AlertRule { Sensor = SensorId.GpuTemp, Above = 85 }); Settings.Save(); hub.Navigate("automations"); });
            var test = FlatButton.Secondary("Preview the alert", () =>
            {
                // A pop-up showing the alert with a made-up reading just over the limit.
                var w = new Window { Title = "Alert preview", Width = 380, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = hub, Background = Theme.Bg, ResizeMode = ResizeMode.NoResize };
                var live = new LiveData { Fahrenheit = Settings.Current.Fahrenheit };
                var r = st.Alerts.FirstOrDefault()?.Sensor ?? SensorId.CpuTemp;
                live.Set(r, (st.Alerts.FirstOrDefault()?.Above ?? 90) + 4);
                var img = new Image { Width = 300, Height = 300 };
                var renderer = new Renderer(App.Instance.Cache);
                var clock = Stopwatch.StartNew();
                var frame = Pics.RoundFrame(img, 300);
                frame.Margin = new Thickness(0, 20, 0, 0);
                w.Content = frame;
                Pics.Every(frame, TimeSpan.FromMilliseconds(60), () =>
                {
                    using var a = renderer.RenderAlert(r, live.Get(r), live, clock.Elapsed.TotalMilliseconds);
                    img.Source = Pics.ToBitmap(a);
                });
                w.Closed += (o, e) => renderer.Dispose();
                w.Show();
            });
            return UI.Section("Too-hot alert", "Takes over the screen with a pulsing red warning while a reading is too high. It stays until the reading drops 3 below the limit.",
                rows, UI.H(10, add, test));
        }

        private static TextBlock Mid(string text)
        {
            var t = UI.Text(text, 13, Theme.Muted);
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        private static UIElement Apps(HubWindow hub)
        {
            var st = Settings.Current;
            var rows = UI.V(10);
            for (int i = 0; i < st.AppRules.Count; i++)
            {
                var rule = st.AppRules[i];
                int index = i;
                var en = new ToggleSwitch(rule.Enabled, "Rule on", v => { rule.Enabled = v; Settings.Save(); App.Instance.Engine.Poke(); });
                var proc = UI.Committing(rule.Process, v => { rule.Process = v.Trim(); Settings.Save(); }, 200);
                proc.ToolTip = "The program's name, like \"cs2\" or \"Spotify.exe\"";
                var pickRunning = FlatButton.Secondary("Pick…");
                pickRunning.MinHeight = 36;
                pickRunning.Height = 36;
                var menu = new ContextMenu();
                pickRunning.ContextMenu = menu;
                pickRunning.Click += (o, e) =>
                {
                    menu.Items.Clear();
                    foreach (var name in RunningApps())
                    {
                        var mi = new MenuItem { Header = name };
                        mi.Click += (a, b) => { rule.Process = name; proc.Text = name; Settings.Save(); };
                        menu.Items.Add(mi);
                    }
                    menu.PlacementTarget = pickRunning;
                    menu.IsOpen = true;
                };
                var screen = UI.Combo(ScreenOptions(false), rule.ScreenId, v => { rule.ScreenId = v; Settings.Save(); App.Instance.Engine.Poke(); }, 200);
                var remove = FlatButton.IconOnly(Icons.Close, "Remove", () => { st.AppRules.RemoveAt(index); Settings.Save(); hub.Navigate("automations"); });
                rows.Children.Add(UI.H(10, en, Mid("When"), proc, pickRunning, Mid("is running, show"), screen, remove));
            }
            if (st.AppRules.Count == 0) rows.Children.Add(UI.Text("No app rules yet. For example: show your GPU stats while a game is running.", 13, Theme.Muted));
            var add = FlatButton.IconText(Icons.Plus, "Add an app rule", () =>
            {
                st.AppRules.Add(new AppRule { ScreenId = st.Screens.FirstOrDefault(s => s.Widget == WidgetKind.Stats)?.Id ?? st.Screens[0].Id });
                Settings.Save();
                hub.Navigate("automations");
            });
            return UI.Section("When an app is running", "Show a screen while a game or program is open. The first matching rule wins.", rows, add);
        }

        /// <summary>Apps with a window, so the list is things you'd recognise.</summary>
        private static List<string> RunningApps()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try { if (p.MainWindowHandle != IntPtr.Zero && p.ProcessName != "CoolerScreen") names.Add(p.ProcessName); } catch { }
                p.Dispose();
            }
            return names.ToList();
        }

        private static UIElement Night()
        {
            var n = Settings.Current.Night;
            var on = new ToggleSwitch(n.Enabled, "Night time", v => { n.Enabled = v; Settings.Save(); App.Instance.Engine.Poke(); });
            var from = UI.Committing(n.From, v => { if (TimeSpan.TryParse(v, out _)) { n.From = v; Settings.Save(); App.Instance.Engine.Poke(); } }, 80);
            var to = UI.Committing(n.To, v => { if (TimeSpan.TryParse(v, out _)) { n.To = v; Settings.Save(); App.Instance.Engine.Poke(); } }, 80);
            var level = UI.Slider(0, 100, n.Brightness, 1, v => v < 1 ? "Off" : Math.Round(v) + "%", v => { n.Brightness = (int)Math.Round(v); Settings.Save(); App.Instance.Engine.Poke(); });
            return UI.Section("Night time", "Dim the screen, or turn it off, overnight.",
                UI.Row("Dim at night", null, on),
                UI.Row("From", "24-hour time, like 23:00.", UI.H(8, from, Mid("to"), to)),
                UI.Row("Brightness at night", "Slide to the left to turn it off.", level));
        }

        private static UIElement Lock()
        {
            var l = Settings.Current.Lock;
            var on = new ToggleSwitch(l.Enabled, "When locked", v => { l.Enabled = v; Settings.Save(); App.Instance.Engine.Poke(); });
            var screen = UI.Combo(ScreenOptions(true), l.ScreenId, v => { l.ScreenId = v; Settings.Save(); App.Instance.Engine.Poke(); }, 240);
            return UI.Section("When the PC is locked", "Press Windows+L and the cooler can switch to another screen or go dark.",
                UI.Row("Change the screen when locked", null, on),
                UI.Row("While locked", null, screen));
        }
    }
}
