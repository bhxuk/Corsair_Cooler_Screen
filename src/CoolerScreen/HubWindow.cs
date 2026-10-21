using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoolerScreen.Core;
using CoolerScreen.Pages;
using CoolerScreen.Render;

namespace CoolerScreen
{
    /// <summary>The main window: a sidebar and a page for each part of the app.</summary>
    public class HubWindow : Window
    {
        private readonly StackPanel _nav;
        private readonly ContentControl _page;
        private readonly ScrollViewer _scroll;
        private readonly ContentControl _main = new ContentControl();
        private readonly Dictionary<string, FlatButton> _navButtons = new Dictionary<string, FlatButton>();
        private string _current = "home";

        public HubWindow()
        {
            Title = "Cooler Screen";
            Width = 1160;
            Height = 800;
            MinWidth = 940;
            MinHeight = 620;
            Background = Theme.Bg;
            Foreground = Theme.Text;
            FontFamily = Theme.Font;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/coolerscreen.ico")); } catch { }

            _nav = new StackPanel { Margin = new Thickness(14, 24, 14, 0) };
            Image logoImg = null;
            try { logoImg = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/coolerscreen-256.png")), Width = 30, Height = 30 }; } catch { }
            var logo = UI.H(10, logoImg, UI.Text("Cooler Screen", 19, Theme.Text, FontWeights.Bold, true));
            logo.Margin = new Thickness(10, 0, 0, 22);
            _nav.Children.Add(logo);
            AddNav("home", "Home", Icons.Home);
            AddNav("screens", "Screens", Icons.Screens);
            AddNav("playlist", "Playlist", Icons.Playlist);
            AddNav("automations", "Automations", Icons.Auto);
            AddNav("device", "Cooler", Icons.Device);

            var settingsBtn = NavButton("settings", "Settings", Icons.Settings);
            settingsBtn.Margin = new Thickness(14, 0, 14, 20);
            var side = new DockPanel { Width = 230, Background = Theme.Side };
            DockPanel.SetDock(settingsBtn, Dock.Bottom);
            side.Children.Add(settingsBtn);
            side.Children.Add(_nav);

            _page = new ContentControl();
            _scroll = new ScrollViewer
            {
                Content = new Border { Padding = new Thickness(40, 34, 40, 40), Child = _page },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var root = new DockPanel();
            DockPanel.SetDock(side, Dock.Left);
            root.Children.Add(side);
            var line = new Border { Width = 1, Background = Theme.Line };
            DockPanel.SetDock(line, Dock.Left);
            root.Children.Add(line);
            _main.Content = _scroll;
            root.Children.Add(_main);
            Content = root;

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.UseDarkTitleBar(h, !Theme.IsLight);
                Native.RoundCorners(h);
            };
            Closed += (s, e) => App.Instance.Engine.Editing = null;
            Navigate("home");
        }

        private void AddNav(string id, string name, string icon) => _nav.Children.Add(NavButton(id, name, icon));

        private FlatButton NavButton(string id, string name, string icon)
        {
            var b = new FlatButton
            {
                Content = UI.H(12, UI.Icon(icon, 18), UI.Text(name, 14, Theme.Text, FontWeights.Medium)),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 0, 12, 0),
                MinHeight = 44,
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 4)
            };
            b.Click += (s, e) => Navigate(id);
            _navButtons[id] = b;
            return b;
        }

        public void Navigate(string id)
        {
            // Leaving the editor stops showing the half-edited screen on the cooler.
            App.Instance.Engine.Editing = null;
            App.Instance.Engine.Poke();
            _current = id;
            _main.Content = _scroll;
            foreach (var kv in _navButtons) kv.Value.Background = kv.Key == id ? Theme.AccentTint : Brushes.Transparent;

            string title, subtitle;
            UIElement body;
            try
            {
                switch (id)
                {
                    case "screens": title = "Screens"; subtitle = "Everything your cooler can show. Click one to change it, or add a new one."; body = ScreensPage.Build(this); break;
                    case "playlist": title = "Playlist"; subtitle = "Show your screens one after another."; body = PlaylistPage.Build(this); break;
                    case "automations": title = "Automations"; subtitle = "Change what's shown when something happens."; body = AutomationsPage.Build(this); break;
                    case "device": title = "Cooler"; subtitle = "The NAUTILUS RS LCD screen itself."; body = DevicePage.Build(this); break;
                    case "settings": title = "Settings"; subtitle = "Cooler Screen " + Updates.CurrentText; body = SettingsPage.Build(this); break;
                    default: title = "Cooler Screen"; subtitle = "Pictures, GIFs, clocks and live stats on your cooler, without iCUE."; body = HomePage.Build(this); id = "home"; break;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Page " + id + " failed: " + ex);
                title = "Something went wrong";
                subtitle = "Details are in the log file.";
                body = UI.Text(ex.Message, 13, Theme.Muted);
            }
            Show(title, subtitle, body);
        }

        /// <summary>Opens the editor for one screen.</summary>
        public void Edit(ScreenSpec spec)
        {
            foreach (var kv in _navButtons) kv.Value.Background = kv.Key == "screens" ? Theme.AccentTint : Brushes.Transparent;
            _current = "editor";
            UIElement body;
            try { body = EditorPage.Build(this, spec); }
            catch (Exception ex)
            {
                Log.Write("Editor failed: " + ex);
                body = UI.Text("Couldn't open the editor: " + ex.Message, 14, Theme.Muted);
            }
            // The editor has its own layout: settings scroll on the left, the preview stays put on the right.
            _main.Content = body;
        }

        private void Show(string title, string subtitle, UIElement body)
        {
            var header = UI.V(6, UI.Text(title, 30, Theme.Text, FontWeights.SemiBold, true), UI.Text(subtitle, 15, Theme.Muted));
            header.Margin = new Thickness(0, 0, 0, 24);
            var page = new StackPanel { MaxWidth = 1100 };
            page.Children.Add(header);
            page.Children.Add(body);
            _page.Content = page;
            _scroll.ScrollToTop();
        }

        public void Refresh() { if (_current != "editor") Navigate(_current); }
    }
}
