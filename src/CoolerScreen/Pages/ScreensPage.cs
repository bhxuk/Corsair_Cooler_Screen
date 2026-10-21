using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CoolerScreen.Core;
using CoolerScreen.Render;

namespace CoolerScreen.Pages
{
    public static class ScreensPage
    {
        public static UIElement Build(HubWindow hub)
        {
            var grid = new WrapPanel();
            foreach (var s in Settings.Current.Screens) grid.Children.Add(Card(s, hub));

            var gallery = new WrapPanel();
            foreach (var (title, description, spec) in Templates.All())
                gallery.Children.Add(TemplateCard(title, description, spec, hub));
            var galleryBox = UI.V(10, UI.Text("Start from", 17, Theme.Text, FontWeights.SemiBold, true), gallery);
            galleryBox.Visibility = Settings.Current.Screens.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var add = FlatButton.Primary("New screen");
            add.Click += (s, e) => galleryBox.Visibility = galleryBox.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            var import = FlatButton.IconText(Icons.Import, "Import", () => Import(hub));
            var bar = UI.H(10, add, import);

            return UI.V(20, bar, galleryBox, grid);
        }

        private static string Summary(ScreenSpec s)
        {
            string bg = s.Background switch
            {
                BackgroundKind.Picture => string.IsNullOrEmpty(s.MediaPath) ? "Picture" : (App.Instance.Cache.IsAnimation(s.MediaPath) ? "GIF" : "Picture"),
                BackgroundKind.Slideshow => "Slideshow",
                BackgroundKind.Gradient => "Gradient",
                _ => "Colour"
            };
            string top = s.Widget switch
            {
                WidgetKind.Clock => "clock",
                WidgetKind.Stats => s.Sensors.Count > 0 ? Sensors.Name(s.Sensors[0]) : "stats",
                WidgetKind.Text => "message",
                WidgetKind.Countdown => "countdown",
                WidgetKind.NowPlaying => "now playing",
                WidgetKind.Weather => "weather",
                _ => ""
            };
            return top.Length == 0 ? bg : bg + " with " + top;
        }

        private static FrameworkElement Card(ScreenSpec s, HubWindow hub)
        {
            bool active = s.Id == Settings.Current.ActiveId;
            var img = new Image { Source = Pics.Thumbnail(s, 200), Width = 150, Height = 150, Clip = new EllipseGeometry(new Point(75, 75), 75, 75) };
            var ring = new Border { Width = 162, Height = 162, CornerRadius = new CornerRadius(81), Background = Theme.Brush(Color.FromRgb(5, 6, 8)), BorderBrush = active ? Theme.Accent : Theme.Ring, BorderThickness = new Thickness(active ? 3 : 2), Child = img };
            var name = UI.Text(s.Name, 15, Theme.Text, FontWeights.SemiBold);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.TextWrapping = TextWrapping.NoWrap;
            var summary = UI.Text(Summary(s), 12, Theme.Muted);
            var show = active ? (FrameworkElement)UI.Badge("Showing", Theme.OnAccent, Theme.Accent) : FlatButton.Primary("Show", () => { App.Instance.ShowScreen(s.Id); hub.Navigate("screens"); });
            if (show is FlatButton fb) { fb.MinHeight = 34; fb.Height = 34; fb.FontSize = 13; fb.Padding = new Thickness(14, 0, 14, 0); }
            var edit = new FlatButton { Content = "Edit", MinHeight = 34, Height = 34, FontSize = 13, Padding = new Thickness(14, 0, 14, 0) };
            edit.Click += (o, e) => hub.Edit(s);
            var more = FlatButton.IconOnly("M5 12h.01M12 12h.01M19 12h.01", "More", null, 34);
            var menu = new ContextMenu();
            menu.Items.Add(MenuItem("Duplicate", () =>
            {
                var copy = s.Clone();
                copy.Name = s.Name + " copy";
                int i = Settings.Current.Screens.IndexOf(s);
                Settings.Current.Screens.Insert(i + 1, copy);
                Settings.Save();
                hub.Navigate("screens");
            }));
            menu.Items.Add(MenuItem("Export…", () => Export(s)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Delete", () => Delete(s, hub)));
            more.ContextMenu = menu;
            more.Click += (o, e) => { menu.PlacementTarget = more; menu.IsOpen = true; };

            var actions = UI.H(6, show, edit, more);
            var body = UI.V(12, ring, UI.V(2, name, summary), actions);
            ring.HorizontalAlignment = HorizontalAlignment.Center;
            var card = UI.Card(body, 16);
            card.Width = 210;
            card.Margin = new Thickness(0, 0, 14, 14);
            if (active) card.BorderBrush = Theme.Accent;
            img.MouseLeftButtonUp += (o, e) => hub.Edit(s);
            img.Cursor = System.Windows.Input.Cursors.Hand;
            return card;
        }

        private static MenuItem MenuItem(string text, Action click)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (s, e) => click();
            return mi;
        }

        public static void Delete(ScreenSpec s, HubWindow hub)
        {
            if (Settings.Current.Screens.Count <= 1)
            {
                MessageBox.Show(hub, "You need at least one screen.", "Cooler Screen", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show(hub, "Delete \"" + s.Name + "\"?", "Cooler Screen", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            Settings.Current.Screens.Remove(s);
            Settings.Current.Playlist.RemoveAll(p => p.ScreenId == s.Id);
            Settings.Current.AppRules.RemoveAll(r => r.ScreenId == s.Id);
            if (Settings.Current.ActiveId == s.Id) Settings.Current.ActiveId = Settings.Current.Screens[0].Id;
            Settings.Save();
            App.Instance.Engine.Poke();
            hub.Navigate("screens");
        }

        private static FrameworkElement TemplateCard(string title, string description, ScreenSpec spec, HubWindow hub)
        {
            var img = new Image { Source = Pics.Thumbnail(spec, 160), Width = 92, Height = 92, Clip = new EllipseGeometry(new Point(46, 46), 46, 46) };
            var text = UI.V(2, UI.Text(title, 14, Theme.Text, FontWeights.SemiBold), UI.Text(description, 12, Theme.Muted));
            text.Width = 150;
            text.VerticalAlignment = VerticalAlignment.Center;
            var b = new FlatButton
            {
                Content = UI.H(12, img, text), HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10), Width = 280, Height = 116, Margin = new Thickness(0, 0, 12, 12),
                Background = Theme.Panel, BorderBrush = Theme.Line, CornerRadius = new CornerRadius(16)
            };
            b.Click += (s, e) =>
            {
                var fresh = spec.Clone();
                Settings.Current.Screens.Add(fresh);
                Settings.Save();
                hub.Edit(fresh);
            };
            return b;
        }

        // ------------------------------------------------------------------ export / import

        private sealed class ExportFile
        {
            public string App { get; set; } = "Cooler Screen";
            public int Format { get; set; } = 1;
            public ScreenSpec Screen { get; set; }
            public string MediaName { get; set; }
            public string Media { get; set; }
        }

        private static void Export(ScreenSpec s)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = s.Name + ".coolerscreen", Filter = "Cooler Screen file (*.coolerscreen)|*.coolerscreen" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var file = new ExportFile { Screen = s };
                // A picture or GIF travels inside the file (up to 25 MB).
                if (s.Background == BackgroundKind.Picture && File.Exists(s.MediaPath) && new FileInfo(s.MediaPath).Length < 25 * 1024 * 1024)
                {
                    file.MediaName = Path.GetFileName(s.MediaPath);
                    file.Media = Convert.ToBase64String(File.ReadAllBytes(s.MediaPath));
                }
                File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(file, Settings.Json));
            }
            catch (Exception ex) { MessageBox.Show("Couldn't export: " + ex.Message, "Cooler Screen"); }
        }

        private static void Import(HubWindow hub)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Cooler Screen file (*.coolerscreen)|*.coolerscreen|All files|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var file = JsonSerializer.Deserialize<ExportFile>(File.ReadAllText(dlg.FileName), Settings.Json);
                if (file?.Screen == null) throw new InvalidDataException("This isn't a Cooler Screen file.");
                var spec = file.Screen.Clone();
                if (!string.IsNullOrEmpty(file.Media) && !string.IsNullOrEmpty(file.MediaName))
                {
                    Directory.CreateDirectory(Settings.MediaFolder);
                    string tmp = Path.Combine(Path.GetTempPath(), Path.GetFileName(file.MediaName));
                    File.WriteAllBytes(tmp, Convert.FromBase64String(file.Media));
                    spec.MediaPath = Settings.ImportMedia(tmp);
                    try { File.Delete(tmp); } catch { }
                }
                Settings.Current.Screens.Add(spec);
                Settings.Save();
                hub.Edit(spec);
            }
            catch (Exception ex) { MessageBox.Show("Couldn't import: " + ex.Message, "Cooler Screen"); }
        }
    }
}
