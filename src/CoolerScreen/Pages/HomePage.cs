using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CoolerScreen.Core;
using CoolerScreen.Device;
using CoolerScreen.Render;

namespace CoolerScreen.Pages
{
    public static class HomePage
    {
        public static UIElement Build(HubWindow hub)
        {
            var app = App.Instance;
            var preview = new LivePreview(300) { HorizontalAlignment = HorizontalAlignment.Center };

            var showing = UI.Text("", 22, Theme.Text, FontWeights.SemiBold, true);
            showing.TextAlignment = TextAlignment.Center;
            var why = UI.Text("", 13, Theme.Muted);
            why.TextAlignment = TextAlignment.Center;
            var pause = FlatButton.IconText(Icons.Pause, "Pause");
            pause.Click += (s, e) => { app.Engine.SetPaused(!app.Engine.Paused); };
            var next = FlatButton.IconText(Icons.Play, "Next in playlist", () => app.Engine.NextInPlaylist());
            var buttons = UI.H(8, pause, next);
            buttons.HorizontalAlignment = HorizontalAlignment.Center;

            var left = UI.V(18, preview, UI.V(4, showing, why), buttons);
            left.HorizontalAlignment = HorizontalAlignment.Center;
            var stage = new Border { Background = Theme.Stage, CornerRadius = new CornerRadius(22), Padding = new Thickness(28, 30, 28, 26), Child = left, BorderBrush = Theme.Line, BorderThickness = new Thickness(1) };

            // Status cards on the right.
            var devTitle = UI.Text("", 15, Theme.Text, FontWeights.SemiBold);
            var devText = UI.Text("", 13, Theme.Muted);
            var devDot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
            var devCard = UI.Card(UI.V(4, UI.H(8, devDot, devTitle), devText), 18);
            devCard.Cursor = Cursors.Hand;
            devCard.MouseLeftButtonUp += (s, e) => hub.Navigate("device");

            var sensorTitle = UI.Text("", 15, Theme.Text, FontWeights.SemiBold);
            var sensorText = UI.Text("", 13, Theme.Muted);
            var readings = UI.Text("", 13, Theme.Text);
            var sensorCard = UI.Card(UI.V(4, sensorTitle, sensorText, readings), 18);

            var bright = UI.Slider(10, 100, Settings.Current.Brightness, 1, v => Math.Round(v) + "%", v =>
            {
                Settings.Current.Brightness = (int)Math.Round(v);
                Settings.Save();
                app.Engine.Poke();
            }, 200);
            var brightCard = UI.Card(UI.V(8, UI.Text("Brightness", 15, Theme.Text, FontWeights.SemiBold), bright,
                Settings.Current.Night.Enabled ? UI.Text("Night time dims it from " + Settings.Current.Night.From + " to " + Settings.Current.Night.To + ".", 12, Theme.Muted) : null), 18);

            var conflictCard = UI.Card(UI.V(4,
                UI.H(8, UI.Icon(Icons.Warning, 16, Theme.Amber), UI.Text("Another app is driving Corsair screens", 15, Theme.Text, FontWeights.SemiBold)),
                UI.Text("", 13, Theme.Muted)), 18, Theme.AmberTint);
            conflictCard.Visibility = Visibility.Collapsed;

            var right = UI.V(12, devCard, sensorCard, brightCard, conflictCard);

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.Children.Add(stage);
            Grid.SetColumn(right, 2);
            top.Children.Add(right);

            // Quick picks: click to show on the cooler.
            var picks = new WrapPanel();
            foreach (var s in Settings.Current.Screens)
                picks.Children.Add(PickTile(s, hub));
            var add = new FlatButton { Width = 120, Height = 150, CornerRadius = new CornerRadius(16), Content = UI.V(6, UI.Icon(Icons.Plus, 26, Theme.Muted), UI.Text("New screen", 13, Theme.Muted)), Margin = new Thickness(0, 0, 12, 12) };
            add.Click += (s, e) => hub.Navigate("screens");
            picks.Children.Add(add);

            var page = UI.V(24, top, UI.V(10, UI.Text("Show on the cooler", 17, Theme.Text, FontWeights.SemiBold, true), picks));

            int conflictCheck = 0;
            string[] conflicts = Array.Empty<string>();
            void Update()
            {
                var e = app.Engine;
                var d = app.Device;
                showing.Text = d.Connected || e.Paused ? e.NowShowing : "Not connected";
                why.Text = e.Paused ? "Cooler Screen has handed the screen back to the cooler." :
                           !d.Connected ? "The preview shows what the cooler will show." :
                           string.IsNullOrEmpty(e.Reason) ? "Chosen screen" + (e.IsNight ? " · night brightness" : "") :
                           e.Reason + (e.IsNight ? " · night brightness" : "");
                ((TextBlock)((StackPanel)pause.Content).Children[1]).Text = e.Paused ? "Resume" : "Pause";
                next.Visibility = Settings.Current.PlaylistOn ? Visibility.Visible : Visibility.Collapsed;

                devDot.Background = d.State == DeviceState.Connected ? Theme.Green : d.State == DeviceState.Problem ? Theme.Red : Theme.Amber;
                devTitle.Text = d.Connected ? "Cooler connected" : d.State == DeviceState.Problem ? "Can't use the cooler's screen" : "Looking for the cooler";
                devText.Text = d.Connected ? (string.IsNullOrEmpty(d.Product) ? "NAUTILUS RS LCD" : d.Product) + " · " + Math.Round(d.Fps, 1) + " frames a second" : d.Message;

                bool sector = app.Sensors.SectorConnected && Settings.Current.UseSector;
                sensorTitle.Text = sector ? "Temperatures from Sector" : "Readings from Windows";
                sensorText.Text = sector ? "Sector's agent is supplying hardware sensors." :
                    "Install Sector for exact CPU and drive temperatures. " + (string.IsNullOrEmpty(app.Sensors.WindowsNotes) ? "" : "Using: " + app.Sensors.WindowsNotes + ".");
                var l = app.Live;
                string R(SensorId id) { var (v, u) = Sensors.Format(id, l.Get(id), l.Fahrenheit); return v + u; }
                readings.Text = "CPU " + R(SensorId.CpuTemp) + " · " + R(SensorId.CpuLoad) + "    GPU " + R(SensorId.GpuTemp) + " · " + R(SensorId.GpuLoad) + "    RAM " + R(SensorId.RamLoad);

                if (conflictCheck-- <= 0)
                {
                    conflictCheck = 20;
                    conflicts = DeviceService.ConflictingApps();
                }
                conflictCard.Visibility = conflicts.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (conflicts.Length > 0)
                    ((TextBlock)((StackPanel)conflictCard.Child).Children[1]).Text = string.Join(", ", conflicts) + " is running. Two apps sending pictures to the same screen will make it flicker. Close it or turn off its LCD control.";
            }
            Update();
            Pics.Every(page, TimeSpan.FromMilliseconds(500), Update);
            return page;
        }

        private static FrameworkElement PickTile(ScreenSpec s, HubWindow hub)
        {
            bool active = s.Id == Settings.Current.ActiveId;
            var img = new Image { Source = Pics.Thumbnail(s, 160), Width = 96, Height = 96, Clip = new EllipseGeometry(new Point(48, 48), 48, 48) };
            var name = UI.Text(s.Name, 13, Theme.Text, FontWeights.SemiBold);
            name.TextAlignment = TextAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.TextWrapping = TextWrapping.NoWrap;
            name.MaxWidth = 104;
            var content = UI.V(10, img, name);
            content.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new FlatButton
            {
                Width = 120, Height = 150, CornerRadius = new CornerRadius(16), Padding = new Thickness(8, 12, 8, 8),
                Content = content, Margin = new Thickness(0, 0, 12, 12),
                Background = active ? Theme.AccentTint : Theme.Panel,
                BorderBrush = active ? Theme.Accent : Theme.Line,
                VerticalContentAlignment = VerticalAlignment.Top,
                ToolTip = active ? "Showing now" : "Show " + s.Name + " on the cooler"
            };
            b.Click += (o, e) => { App.Instance.ShowScreen(s.Id); hub.Navigate("home"); };
            b.MouseRightButtonUp += (o, e) => hub.Edit(s);
            return b;
        }
    }
}
