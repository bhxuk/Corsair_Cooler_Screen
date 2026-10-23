using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CoolerScreen.Core;
using CoolerScreen.Device;

namespace CoolerScreen.Pages
{
    public static class DevicePage
    {
        public static UIElement Build(HubWindow hub)
        {
            var app = App.Instance;
            var st = Settings.Current;

            // ---------- status ----------
            var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
            var title = UI.Text("", 18, Theme.Text, FontWeights.SemiBold, true);
            var msg = UI.Text("", 13, Theme.Muted);
            var details = new Grid();
            details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var values = new TextBlock[7];
            string[] names = { "Product", "USB ID", "Serial number", "Firmware", "Frames a second", "Last picture", "Sent so far" };
            for (int i = 0; i < names.Length; i++)
            {
                details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var n = UI.Text(names[i], 13, Theme.Muted);
                n.Margin = new Thickness(0, 3, 0, 3);
                Grid.SetRow(n, i);
                details.Children.Add(n);
                values[i] = UI.Text("", 13, Theme.Text, FontWeights.SemiBold);
                values[i].Margin = new Thickness(0, 3, 0, 3);
                Grid.SetRow(values[i], i);
                Grid.SetColumn(values[i], 1);
                details.Children.Add(values[i]);
            }
            var reconnect = FlatButton.IconText(Icons.Refresh, "Reconnect", () => app.Device.Reconnect());
            var test = FlatButton.Secondary("Show a test pattern", () => app.Engine.ShowTestPattern(8));
            var copy = FlatButton.Secondary("Copy details", () =>
            {
                try { Clipboard.SetText(Details()); } catch { }
            });
            var statusCard = UI.Card(UI.V(12, UI.H(10, dot, title), msg, UI.Divider(), details, UI.H(10, reconnect, test, copy)), 22);

            void Update()
            {
                var d = app.Device;
                dot.Background = d.State == DeviceState.Connected ? Theme.Green : d.State == DeviceState.Problem ? Theme.Red : Theme.Amber;
                title.Text = d.Connected ? "Connected" : d.State == DeviceState.Problem ? "There's a problem" : "Not connected";
                msg.Text = d.Message;
                values[0].Text = d.Connected ? (string.IsNullOrEmpty(d.Product) ? "NAUTILUS RS LCD" : d.Product) : "–";
                values[1].Text = d.Connected ? "1B1C:" + d.ProductId.ToString("X4") : "1B1C:0C57";
                values[2].Text = d.Connected && !string.IsNullOrEmpty(d.Serial) ? d.Serial : "–";
                values[3].Text = d.Connected && !string.IsNullOrEmpty(d.Firmware) ? d.Firmware : "–";
                values[4].Text = d.Connected ? d.Fps.ToString("0.0") : "–";
                values[5].Text = d.LastFrameBytes > 0 ? (d.LastFrameBytes / 1024.0).ToString("0") + " KB" : "–";
                values[6].Text = d.FramesSent > 0 ? d.FramesSent.ToString("N0") + " pictures, " + (d.BytesSent / 1048576.0).ToString("0.0") + " MB" : "–";
            }
            Update();
            Pics.Every(statusCard, TimeSpan.FromSeconds(1), Update);

            // ---------- screen settings ----------
            var bright = UI.Slider(10, 100, st.Brightness, 1, v => Math.Round(v) + "%", v => { st.Brightness = (int)Math.Round(v); Settings.Save(); app.Engine.Poke(); });
            var rotation = UI.Choice(new (int, string)[] { (0, "Normal"), (1, "90°"), (2, "180°"), (3, "270°") }, st.Rotation, v => { st.Rotation = v; Settings.Save(); app.Engine.Poke(); });
            var fps = UI.Choice(new (int, string)[] { (10, "10"), (15, "15"), (20, "20"), (30, "30") }, st.Fps, v => { st.Fps = v; Settings.Save(); app.Engine.Poke(); });
            var quality = UI.Slider(50, 95, st.Quality, 5, v => Math.Round(v) + "", v => { st.Quality = (int)Math.Round(v); Settings.Save(); });
            var handBack = new ToggleSwitch(st.HandBackOnExit, "Hand back on exit", v => { st.HandBackOnExit = v; Settings.Save(); });
            var screenCard = UI.Section("Screen", null,
                UI.Row("Brightness", null, bright),
                UI.Row("Rotation", "Turn the picture if the cooler is mounted sideways or upside down.", rotation),
                UI.Row("Frames a second for GIFs", "Higher is smoother. The screen tops out at 30.", fps),
                UI.Row("Picture quality", "Lower sends less data over USB. 85 looks the same to most eyes.", quality),
                UI.Row("When Cooler Screen closes, give the screen back to the cooler", "The cooler goes back to its own built-in display.", handBack));

            // ---------- reassurance ----------
            var safe = UI.Section("How it works", null,
                UI.Text("Cooler Screen only ever opens one device: the cooler's screen, USB ID 1B1C:0C57 (or 0C55). It finds it by that ID before opening anything.", 13, Theme.Muted),
                UI.Text("It uses Windows' own built-in USB support. No drivers, no background services, no admin rights. The screen is opened shared, so nothing else is locked out.", 13, Theme.Muted),
                UI.Text("Fans and the pump are on your motherboard's PWM headers, so they keep following your BIOS fan curves whether Cooler Screen is running or not.", 13, Theme.Muted));

            var conflicts = DeviceService.ConflictingApps();
            UIElement warn = null;
            if (conflicts.Length > 0)
                warn = UI.Card(UI.V(4,
                    UI.H(8, UI.Icon(Icons.Warning, 16, Theme.Amber), UI.Text(string.Join(", ", conflicts) + " is running", 15, Theme.Text, FontWeights.SemiBold)),
                    UI.Text("It may be sending pictures to the same screen, which makes it flicker between the two. Close it, or turn off its LCD control.", 13, Theme.Muted)), 18, Theme.AmberTint);

            return UI.V(16, warn, statusCard, screenCard, safe);
        }

        public static string Details()
        {
            var app = App.Instance;
            var d = app.Device;
            var sb = new StringBuilder();
            sb.AppendLine("Cooler Screen " + Updates.CurrentText);
            sb.AppendLine("Windows " + Environment.OSVersion.Version);
            sb.AppendLine("State: " + d.State + " (" + d.Message + ")");
            sb.AppendLine("Product: " + d.Product + "  USB 1B1C:" + d.ProductId.ToString("X4"));
            sb.AppendLine("Serial: " + d.Serial + "  Firmware: " + d.Firmware);
            sb.AppendLine("Report size: " + d.ReportSize + "  Path: " + d.Path);
            sb.AppendLine("Frames: " + d.FramesSent + "  Errors: " + d.Errors + "  Fps: " + d.Fps.ToString("0.0"));
            sb.AppendLine("Sensors: " + app.Live.Source + "  Sector: " + app.Sensors.SectorStatus + "  Windows: " + app.Sensors.WindowsNotes);
            var c = DeviceService.ConflictingApps();
            if (c.Length > 0) sb.AppendLine("Also running: " + string.Join(", ", c));
            return sb.ToString();
        }
    }

    public static class SettingsPage
    {
        public static UIElement Build(HubWindow hub)
        {
            var app = App.Instance;
            var st = Settings.Current;

            var startup = new ToggleSwitch(st.StartWithWindows, "Start with Windows", v => { st.StartWithWindows = v; Settings.Save(); AutoStart.Apply(v); });
            var units = UI.Choice(new (bool, string)[] { (false, "°C"), (true, "°F") }, st.Fahrenheit, v => { st.Fahrenheit = v; app.Live.Fahrenheit = v; Settings.Save(); app.Engine.Poke(); });
            var general = UI.Section("General", null,
                UI.Row("Start with Windows", "Runs quietly by the clock and puts your screen up as soon as you sign in.", startup),
                UI.Row("Temperatures in", null, units));

            var sectorStatus = UI.Text(app.Sensors.SectorStatus, 12, Theme.Muted);
            var useSector = new ToggleSwitch(st.UseSector, "Use Sector", v => { st.UseSector = v; Settings.Save(); });
            Pics.Every(sectorStatus, TimeSpan.FromSeconds(2), () => sectorStatus.Text = app.Sensors.SectorStatus);
            var sensors = UI.Section("Temperatures", "Windows doesn't let normal apps read CPU temperatures directly. Rather than install a driver, Cooler Screen borrows them from Sector if it's installed on this PC.",
                UI.Row("Use temperatures from Sector", null, useSector),
                sectorStatus,
                UI.H(10, FlatButton.Secondary("Get Sector", () => Native.OpenUrl("https://github.com/bhxuk/Sector"))));

            var theme = UI.Choice(new (string, string)[] { ("Dark", "Dark"), ("Light", "Light"), ("System", "Match Windows") }, st.Theme, v => { st.Theme = v; Settings.Save(); RestartPrompt(hub); });
            var accent = UI.Choice(Theme.AccentNames.Select(a => (a, a)), st.Accent, v => { st.Accent = v; Settings.Save(); RestartPrompt(hub); });
            var look = UI.Section("Appearance", "Changes apply when Cooler Screen restarts.", UI.Row("Theme", null, theme), UI.Field("Accent colour", accent));

            var check = new ToggleSwitch(st.CheckForUpdates, "Check for updates", v => { st.CheckForUpdates = v; Settings.Save(); });
            var auto = new ToggleSwitch(st.AutoUpdate, "Install automatically", v => { st.AutoUpdate = v; Settings.Save(); });
            var updState = UI.Text("You have " + Updates.CurrentText + ".", 13, Theme.Muted);
            var checkNow = FlatButton.Secondary("Check now");
            checkNow.Click += async (s, e) =>
            {
                checkNow.IsEnabled = false;
                updState.Text = "Checking…";
                bool newer = await Updates.Check();
                checkNow.IsEnabled = true;
                if (newer)
                {
                    updState.Text = "Cooler Screen " + Updates.LatestVersion + " is available.";
                    var install = FlatButton.Primary("Install " + Updates.LatestVersion);
                    install.Click += async (o, ev) => await app.InstallUpdate(t => updState.Text = t);
                    ((Panel)checkNow.Parent).Children.Add(install);
                }
                else updState.Text = Updates.LatestVersion != null ? "You're up to date (" + Updates.CurrentText + ")." : "Couldn't reach GitHub just now.";
            };
            var updates = UI.Section("Updates", null,
                UI.Row("Check for updates", "Looks once every few hours.", check),
                UI.Row("Install updates automatically", null, auto),
                updState, UI.H(10, checkNow, FlatButton.Secondary("What's new", () => Native.OpenUrl("https://github.com/" + Updates.Repo + "/blob/main/CHANGELOG.md"))));

            var backup = UI.Section("Backup", "Everything (screens, pictures, playlist, automations and settings) in one file. Use it to move to a new PC.",
                UI.H(10,
                    FlatButton.IconText(Icons.Export, "Save a backup", () => SaveBackup(hub)),
                    FlatButton.IconText(Icons.Import, "Restore a backup", () => RestoreBackup(hub)),
                    FlatButton.IconText(Icons.Folder, "Open the data folder", () => Native.OpenUrl(Settings.Folder))));

            var about = UI.Section("About", null,
                UI.Text("Cooler Screen " + Updates.CurrentText + ". Shows pictures, GIFs, clocks and live stats on the CORSAIR NAUTILUS RS LCD cooler without iCUE.", 13, Theme.Muted),
                UI.Text("Not made by or connected with Corsair. CORSAIR, NAUTILUS and iCUE are Corsair's trademarks.", 12, Theme.Muted),
                UI.H(10,
                    FlatButton.Secondary("Website", () => Native.OpenUrl("https://bhxuk.github.io/Corsair_Cooler_Screen/")),
                    FlatButton.Secondary("Report a problem", () => Native.OpenUrl("https://github.com/" + Updates.Repo + "/issues")),
                    FlatButton.Secondary("Log file", () => Native.OpenUrl(Log.FilePath))));

            return UI.V(16, general, sensors, look, updates, backup, about);
        }

        private static void RestartPrompt(HubWindow hub)
        {
            if (MessageBox.Show(hub, "Restart Cooler Screen now to use the new look?", "Cooler Screen", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                App.Instance.Restart();
        }

        private static void SaveBackup(HubWindow hub)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "Cooler Screen backup " + DateTime.Now.ToString("yyyy-MM-dd") + ".zip", Filter = "Backup (*.zip)|*.zip" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                Settings.Save();
                if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                using var zip = ZipFile.Open(dlg.FileName, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(Settings.FilePath, "settings.json");
                if (Directory.Exists(Settings.MediaFolder))
                    foreach (var f in Directory.GetFiles(Settings.MediaFolder))
                        zip.CreateEntryFromFile(f, "Media/" + Path.GetFileName(f));
                MessageBox.Show(hub, "Backup saved.", "Cooler Screen");
            }
            catch (Exception ex) { MessageBox.Show(hub, "Couldn't save the backup: " + ex.Message, "Cooler Screen"); }
        }

        private static void RestoreBackup(HubWindow hub)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Backup (*.zip)|*.zip" };
            if (dlg.ShowDialog() != true) return;
            if (MessageBox.Show(hub, "Replace all your screens and settings with this backup?", "Cooler Screen", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                using var zip = ZipFile.OpenRead(dlg.FileName);
                var settingsEntry = zip.GetEntry("settings.json") ?? throw new InvalidDataException("This isn't a Cooler Screen backup.");
                Directory.CreateDirectory(Settings.MediaFolder);
                foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("Media/") && e.Name.Length > 0))
                    e.ExtractToFile(Path.Combine(Settings.MediaFolder, Path.GetFileName(e.Name)), true);
                using (var r = new StreamReader(settingsEntry.Open()))
                {
                    string json = r.ReadToEnd();
                    // Pictures now live in this PC's media folder.
                    var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, Settings.Json);
                    if (restored == null) throw new InvalidDataException("The backup's settings couldn't be read.");
                    foreach (var s in restored.Screens)
                        if (!string.IsNullOrEmpty(s.MediaPath)) s.MediaPath = Path.Combine(Settings.MediaFolder, Path.GetFileName(s.MediaPath));
                    File.WriteAllText(Settings.FilePath, System.Text.Json.JsonSerializer.Serialize(restored, Settings.Json));
                }
                MessageBox.Show(hub, "Backup restored. Cooler Screen will restart.", "Cooler Screen");
                App.Instance.Restart();
            }
            catch (Exception ex) { MessageBox.Show(hub, "Couldn't restore: " + ex.Message, "Cooler Screen"); }
        }
    }
}
