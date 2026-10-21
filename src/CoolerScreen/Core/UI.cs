using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace CoolerScreen.Core
{
    /// <summary>The colour palette. Light or dark and the accent colour are read once at start-up.</summary>
    public static class Theme
    {
        public static readonly bool IsLight = ResolveLight();
        public static readonly Color AccentColor = AccentFor(SafeAccent());

        public static readonly string[] AccentNames = { "Teal", "Amber", "Blue", "Pink", "Green", "Purple" };

        public static Color AccentFor(string name) => name switch
        {
            "Amber" => Color.FromRgb(0xFF, 0xB5, 0x47),
            "Blue" => Color.FromRgb(0x5B, 0x8C, 0xFF),
            "Pink" => Color.FromRgb(0xF0, 0x6A, 0x9A),
            "Green" => Color.FromRgb(0x7C, 0xC8, 0x5A),
            "Purple" => Color.FromRgb(0xA0, 0x7C, 0xF0),
            _ => Color.FromRgb(0x3F, 0xC4, 0xB2)
        };

        private static string SafeAccent() { try { return Settings.Current?.Accent ?? "Teal"; } catch { return "Teal"; } }

        private static bool ResolveLight()
        {
            string t = "Dark";
            try { t = Settings.Current?.Theme ?? "Dark"; } catch { }
            if (t == "Light") return true;
            if (t == "System")
            {
                try
                {
                    using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
                }
                catch { }
            }
            return false;
        }

        public static readonly Color BgColor = IsLight ? Color.FromRgb(0xF6, 0xF5, 0xF2) : Color.FromRgb(0x0F, 0x11, 0x15);
        public static readonly Color PanelColor = IsLight ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x18, 0x1B, 0x22);
        public static readonly Color SideColor = IsLight ? Color.FromRgb(0xEE, 0xEC, 0xE8) : Color.FromRgb(0x14, 0x17, 0x1D);
        public static readonly Color TextColor = IsLight ? Color.FromRgb(0x17, 0x19, 0x1F) : Color.FromRgb(0xF3, 0xF1, 0xEC);
        public static readonly Color MutedColor = IsLight ? Color.FromRgb(0x5B, 0x60, 0x6B) : Color.FromRgb(0xA7, 0xAB, 0xB5);

        public static readonly SolidColorBrush Bg = Freeze(new SolidColorBrush(BgColor));
        public static readonly SolidColorBrush Panel = Freeze(new SolidColorBrush(PanelColor));
        public static readonly SolidColorBrush Side = Freeze(new SolidColorBrush(SideColor));
        public static readonly SolidColorBrush Text = Freeze(new SolidColorBrush(TextColor));
        public static readonly SolidColorBrush Muted = Freeze(new SolidColorBrush(MutedColor));
        public static readonly SolidColorBrush Accent = Freeze(new SolidColorBrush(AccentColor));
        public static readonly SolidColorBrush AccentTint = Freeze(new SolidColorBrush(Color.FromArgb(IsLight ? (byte)0x30 : (byte)0x29, AccentColor.R, AccentColor.G, AccentColor.B)));
        public static readonly SolidColorBrush OnAccent = Freeze(new SolidColorBrush(Color.FromRgb(0x08, 0x14, 0x13)));
        public static readonly SolidColorBrush Amber = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB5, 0x47)));
        public static readonly SolidColorBrush AmberTint = Freeze(new SolidColorBrush(Color.FromArgb(0x29, 0xFF, 0xB5, 0x47)));
        public static readonly SolidColorBrush Green = Freeze(new SolidColorBrush(IsLight ? Color.FromRgb(0x4E, 0xA0, 0x3A) : Color.FromRgb(0x8F, 0xD1, 0x6A)));
        public static readonly SolidColorBrush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xE2, 0x58, 0x5F)));
        public static readonly SolidColorBrush RedTint = Freeze(new SolidColorBrush(Color.FromArgb(0x26, 0xE2, 0x58, 0x5F)));
        public static readonly SolidColorBrush Line = Freeze(new SolidColorBrush(IsLight ? Color.FromArgb(0x1A, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
        public static readonly SolidColorBrush Subtle = Freeze(new SolidColorBrush(IsLight ? Color.FromArgb(0x0D, 0, 0, 0) : Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)));
        public static readonly SolidColorBrush Off = Freeze(new SolidColorBrush(IsLight ? Color.FromRgb(0xC4, 0xC8, 0xD0) : Color.FromRgb(0x3A, 0x3F, 0x4B)));
        public static readonly SolidColorBrush Stage = Freeze(new SolidColorBrush(IsLight ? Color.FromRgb(0xE4, 0xE2, 0xDE) : Color.FromRgb(0x0B, 0x0D, 0x11)));
        public static readonly SolidColorBrush Ring = Freeze(new SolidColorBrush(IsLight ? Color.FromRgb(0x2A, 0x2D, 0x34) : Color.FromRgb(0x24, 0x28, 0x31)));

        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        public static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static SolidColorBrush Brush(string hex)
        {
            try { return Brush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return Brush(Colors.Black); }
        }

        public static DropShadowEffect Shadow(double blur = 30, double opacity = 0.35, double depth = 10)
            => new DropShadowEffect { BlurRadius = blur, Opacity = opacity, ShadowDepth = depth, Direction = 270, Color = Colors.Black };
    }

    /// <summary>Slim, rounded scroll bars for every window.</summary>
    public static class AppStyles
    {
        public static void Install()
        {
            try
            {
                string thumb = Theme.IsLight ? "#38000000" : "#33FFFFFF";
                string hover = Theme.IsLight ? "#66000000" : "#66FFFFFF";
                string xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='CsThumb' TargetType='Thumb'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Thumb'><Border CornerRadius='4' Background='{TemplateBinding Background}'/></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='CsPage' TargetType='RepeatButton'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
  </Style>
  <ControlTemplate x:Key='CsVertical' TargetType='ScrollBar'>
    <Grid Background='Transparent'>
      <Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource CsPage}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource CsPage}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb x:Name='thumb' Style='{StaticResource CsThumb}' Background='" + thumb + @"' Width='5' Margin='0,6' HorizontalAlignment='Center'/></Track.Thumb>
      </Track>
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='thumb' Property='Width' Value='8'/><Setter TargetName='thumb' Property='Background' Value='" + hover + @"'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <ControlTemplate x:Key='CsHorizontal' TargetType='ScrollBar'>
    <Grid Background='Transparent'>
      <Track x:Name='PART_Track' IsDirectionReversed='False'>
        <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource CsPage}' Command='ScrollBar.PageLeftCommand'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource CsPage}' Command='ScrollBar.PageRightCommand'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb x:Name='thumb' Style='{StaticResource CsThumb}' Background='" + thumb + @"' Height='5' Margin='6,0' VerticalAlignment='Center'/></Track.Thumb>
      </Track>
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='thumb' Property='Height' Value='8'/><Setter TargetName='thumb' Property='Background' Value='" + hover + @"'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <Style TargetType='ScrollBar'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='14'/>
    <Setter Property='MinWidth' Value='14'/>
    <Setter Property='Template' Value='{StaticResource CsVertical}'/>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/><Setter Property='MinWidth' Value='0'/><Setter Property='Height' Value='14'/><Setter Property='MinHeight' Value='14'/>
        <Setter Property='Template' Value='{StaticResource CsHorizontal}'/>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>";
                Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
            }
            catch (Exception ex) { Log.Write("Styles: " + ex.Message); }
        }
    }

    /// <summary>Small factory helpers so the code-built UI stays readable.</summary>
    public static class UI
    {
        public static TextBlock Text(string text, double size = 14, Brush fg = null, FontWeight? weight = null, bool display = false)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = fg ?? Theme.Text,
                FontWeight = weight ?? FontWeights.Normal,
                FontFamily = display ? Theme.Display : Theme.Font,
                TextWrapping = TextWrapping.Wrap
            };
        }

        public static TextBlock Label(string text)
        {
            var t = Text(text.ToUpperInvariant(), 11, Theme.Muted, FontWeights.SemiBold);
            t.TextWrapping = TextWrapping.NoWrap;
            return t;
        }

        public static Border Card(UIElement child, double padding = 20, Brush bg = null, double radius = 18)
        {
            return new Border
            {
                Background = bg ?? Theme.Panel,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(radius),
                Padding = new Thickness(padding),
                Child = child
            };
        }

        /// <summary>A card with a heading and a short description above its contents.</summary>
        public static Border Section(string title, string description, params UIElement[] children)
        {
            var sp = V(4, Text(title, 17, Theme.Text, FontWeights.SemiBold, true));
            if (!string.IsNullOrEmpty(description))
            {
                var d = Text(description, 13, Theme.Muted);
                d.Margin = new Thickness(0, 0, 0, 6);
                sp.Children.Add(d);
            }
            foreach (var c in children) if (c != null) sp.Children.Add(c);
            return Card(sp, 22);
        }

        public static StackPanel V(double gap, params UIElement[] children)
        {
            var sp = new GapStack { Orientation = Orientation.Vertical, Gap = gap };
            foreach (var c in children) if (c != null) sp.Children.Add(c);
            return sp;
        }

        public static StackPanel H(double gap, params UIElement[] children)
        {
            var sp = new GapStack { Orientation = Orientation.Horizontal, Gap = gap };
            foreach (var c in children) if (c != null) sp.Children.Add(c);
            return sp;
        }

        public static WrapPanel Wrap(double gap, IEnumerable<UIElement> children)
        {
            var wp = new WrapPanel();
            foreach (var c in children)
            {
                if (c is FrameworkElement fe) fe.Margin = new Thickness(0, 0, gap, gap);
                wp.Children.Add(c);
            }
            return wp;
        }

        public static FrameworkElement Icon(string data, double size = 18, Brush stroke = null, double thickness = 1.8)
        {
            var path = new Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke ?? Theme.Text,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
        }

        public static Border Divider() => new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 6, 0, 6) };

        /// <summary>A setting row: title, optional description and a control on the right.</summary>
        public static Grid Row(string title, string description, UIElement control)
        {
            var g = new Grid { MinHeight = 52, Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = V(2, Text(title, 14, Theme.Text, FontWeights.SemiBold), string.IsNullOrEmpty(description) ? null : Text(description, 12, Theme.Muted));
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(0, 6, 16, 6);
            g.Children.Add(left);
            if (control != null)
            {
                if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 1);
                g.Children.Add(control);
            }
            return g;
        }

        /// <summary>A label above a control, for stacked forms.</summary>
        public static StackPanel Field(string label, UIElement control) => V(6, Label(label), control);

        public static ComboBox Combo<T>(IEnumerable<(T value, string text)> options, T selected, Action<T> changed, double width = 220)
        {
            var list = options.ToList();
            var cb = new ComboBox { Width = width, Height = 36, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var o in list) cb.Items.Add(o.text);
            int idx = list.FindIndex(o => EqualityComparer<T>.Default.Equals(o.value, selected));
            cb.SelectedIndex = idx >= 0 ? idx : (list.Count > 0 ? 0 : -1);
            cb.SelectionChanged += (s, e) => { if (cb.SelectedIndex >= 0) changed(list[cb.SelectedIndex].value); };
            return cb;
        }

        public static TextBox Input(string value, double width = 220)
        {
            return new TextBox
            {
                Text = value ?? "",
                Width = width,
                Height = 36,
                FontSize = 14,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Theme.Subtle,
                Foreground = Theme.Text,
                CaretBrush = Theme.Text,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1)
            };
        }

        /// <summary>A text box that commits when you press Enter or click away.</summary>
        public static TextBox Committing(string value, Action<string> commit, double width = 220)
        {
            var tb = Input(value, width);
            string last = value ?? "";
            void Commit() { if (tb.Text != last) { last = tb.Text; commit(tb.Text); } }
            tb.LostKeyboardFocus += (s, e) => Commit();
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            return tb;
        }

        /// <summary>A slider with its value shown beside it.</summary>
        public static FrameworkElement Slider(double min, double max, double value, double step, Func<double, string> format, Action<double> changed, double width = 220)
        {
            var s = new MiniSlider(min, max, value, null, width) { Step = step };
            var t = Text(format(value), 13, Theme.Muted, FontWeights.SemiBold);
            t.Width = 64;
            t.TextAlignment = TextAlignment.Right;
            t.VerticalAlignment = VerticalAlignment.Center;
            s.ValueChanged += v => { t.Text = format(v); changed(v); };
            return H(10, s, t);
        }

        /// <summary>One-of-many buttons that look like chips.</summary>
        public static WrapPanel Choice<T>(IEnumerable<(T value, string text)> options, T selected, Action<T> changed)
        {
            var list = options.ToList();
            var chips = new List<Chip>();
            foreach (var o in list)
            {
                var chip = new Chip(o.text, EqualityComparer<T>.Default.Equals(o.value, selected));
                var val = o.value;
                chip.Click += (s, e) =>
                {
                    foreach (var c in chips) c.Selected = c == chip;
                    changed(val);
                };
                chips.Add(chip);
            }
            return Wrap(8, chips);
        }

        public static readonly string[] Swatches =
        {
            "#F4F2EE", "#9AA0AB", "#07080D", "#1D2433", "#5ED3C3", "#3FC4B2", "#5B8CFF", "#A07CF0",
            "#F06A9A", "#E2585F", "#FFB547", "#FFD166", "#7CC85A", "#0E2236", "#3A1C40", "#000000"
        };

        /// <summary>Colour swatches, a hex box and a "More" button for the full Windows colour picker.</summary>
        public static FrameworkElement ColorPicker(string value, Action<string> changed)
        {
            var preview = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Background = Theme.Brush(value ?? "#000000") };
            TextBox hex = null;
            void Set(string h, bool fromBox = false)
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(h);
                    string norm = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                    preview.Background = Theme.Brush(c);
                    if (!fromBox && hex != null) hex.Text = norm;
                    changed(norm);
                }
                catch { }
            }
            hex = Committing(value ?? "#000000", h => Set(h.StartsWith("#") ? h : "#" + h, true), 100);
            var swatches = new WrapPanel { MaxWidth = 300 };
            foreach (var sw in Swatches)
            {
                string colour = sw;
                var b = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 6, 6),
                    Background = Theme.Brush(sw), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                    ToolTip = sw
                };
                b.MouseLeftButtonUp += (s, e) => Set(colour);
                swatches.Children.Add(b);
            }
            var more = new FlatButton { Content = "More…", MinHeight = 36, Height = 36, Padding = new Thickness(12, 0, 12, 0), FontSize = 13 };
            more.Click += (s, e) =>
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(hex.Text);
                    using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) };
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        Set("#" + dlg.Color.R.ToString("X2") + dlg.Color.G.ToString("X2") + dlg.Color.B.ToString("X2"));
                }
                catch { }
            };
            return V(8, H(8, preview, hex, more), swatches);
        }

        public static Border Badge(string text, Brush fg, Brush bg)
        {
            return new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 3, 10, 4),
                Child = Text(text, 12, fg, FontWeights.SemiBold),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }
    }

    /// <summary>StackPanel with a gap between children.</summary>
    public class GapStack : StackPanel
    {
        public double Gap { get; set; }

        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            for (int i = 0; i < Children.Count; i++)
            {
                if (!(Children[i] is FrameworkElement fe)) continue;
                var m = fe.Margin;
                double g = i == 0 ? 0 : Gap;
                fe.Margin = Orientation == Orientation.Vertical ? new Thickness(m.Left, g, m.Right, m.Bottom) : new Thickness(g, m.Top, m.Right, m.Bottom);
            }
        }
    }

    /// <summary>A flat button with rounded corners and a soft hover.</summary>
    public class FlatButton : Button
    {
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(FlatButton), new PropertyMetadata(new CornerRadius(12)));

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        private static ControlTemplate _template;

        private static ControlTemplate BuildTemplate()
        {
            string xaml =
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>" +
                "<Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' " +
                "CornerRadius='{Binding CornerRadius, RelativeSource={RelativeSource TemplatedParent}}' SnapsToDevicePixels='True'>" +
                "<ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='False'/>" +
                "</Border><ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.86'/></Trigger>" +
                "<Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.7'/></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.4'/></Trigger>" +
                "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#5ED3C3'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        public FlatButton()
        {
            _template ??= BuildTemplate();
            Template = _template;
            Background = Theme.Subtle;
            Foreground = Theme.Text;
            BorderBrush = Brushes.Transparent;
            BorderThickness = new Thickness(1);
            Padding = new Thickness(16, 0, 16, 0);
            MinHeight = 40;
            FontSize = 14;
            FontWeight = FontWeights.SemiBold;
            FontFamily = Theme.Font;
            Cursor = Cursors.Hand;
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
            FocusVisualStyle = null;
        }

        public static FlatButton Primary(string text, Action onClick = null)
        {
            var b = new FlatButton { Content = text, Background = Theme.Accent, Foreground = Theme.OnAccent, MinHeight = 42, Padding = new Thickness(20, 0, 20, 0) };
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }

        public static FlatButton Secondary(string text, Action onClick = null)
        {
            var b = new FlatButton { Content = text, Background = Brushes.Transparent, BorderBrush = Theme.Line, MinHeight = 42 };
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }

        public static FlatButton IconText(string icon, string text, Action onClick = null)
        {
            var b = new FlatButton { Content = UI.H(8, UI.Icon(icon, 16), UI.Text(text, 14, Theme.Text, FontWeights.SemiBold)), MinHeight = 40 };
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }

        public static FlatButton IconOnly(string icon, string tooltip, Action onClick = null, double size = 36)
        {
            var b = new FlatButton { Content = UI.Icon(icon, 16), Width = size, Height = size, MinHeight = size, Padding = new Thickness(0), ToolTip = tooltip };
            System.Windows.Automation.AutomationProperties.SetName(b, tooltip);
            if (onClick != null) b.Click += (s, e) => onClick();
            return b;
        }
    }

    /// <summary>An on/off switch.</summary>
    public class ToggleSwitch : FlatButton
    {
        private readonly Border _knob;
        private bool _isOn;

        public event Action<bool> Toggled;

        public bool IsOn
        {
            get => _isOn;
            set { _isOn = value; Render(); }
        }

        public ToggleSwitch(bool on = false, string name = "Toggle", Action<bool> toggled = null)
        {
            Width = 46;
            Height = 26;
            MinHeight = 26;
            Padding = new Thickness(3);
            CornerRadius = new CornerRadius(13);
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            _knob = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Brushes.White };
            Content = _knob;
            System.Windows.Automation.AutomationProperties.SetName(this, name);
            _isOn = on;
            Render();
            if (toggled != null) Toggled += toggled;
            Click += (s, e) => { IsOn = !IsOn; Toggled?.Invoke(IsOn); };
        }

        private void Render()
        {
            Background = _isOn ? Theme.Accent : Theme.Off;
            _knob.HorizontalAlignment = _isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
    }

    /// <summary>A simple slider.</summary>
    public class MiniSlider : Grid
    {
        private readonly Border _fill;
        private readonly Border _thumb;
        private double _value;
        private bool _dragging;

        public double Minimum { get; set; }
        public double Maximum { get; set; } = 100;
        public double Step { get; set; } = 1;
        public event Action<double> ValueChanged;

        public double Value
        {
            get => _value;
            set { _value = Math.Max(Minimum, Math.Min(Maximum, value)); Layout(); }
        }

        public MiniSlider(double min, double max, double value, Brush color = null, double width = 220)
        {
            Minimum = min;
            Maximum = max;
            Width = width;
            Height = 28;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            Focusable = true;
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Theme.Off, VerticalAlignment = VerticalAlignment.Center };
            _fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = color ?? Theme.Accent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            _thumb = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Effect = Theme.Shadow(6, 0.3, 1) };
            Children.Add(track);
            Children.Add(_fill);
            Children.Add(_thumb);
            _value = Math.Max(min, Math.Min(max, value));
            SizeChanged += (s, e) => Layout();
            MouseLeftButtonDown += (s, e) => { _dragging = true; CaptureMouse(); Focus(); SetFromMouse(e.GetPosition(this).X); };
            MouseMove += (s, e) => { if (_dragging) SetFromMouse(e.GetPosition(this).X); };
            MouseLeftButtonUp += (s, e) => { _dragging = false; ReleaseMouseCapture(); };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Left || e.Key == Key.Down) { Value -= Step; ValueChanged?.Invoke(_value); e.Handled = true; }
                if (e.Key == Key.Right || e.Key == Key.Up) { Value += Step; ValueChanged?.Invoke(_value); e.Handled = true; }
            };
        }

        private void SetFromMouse(double x)
        {
            double w = Math.Max(1, ActualWidth - 18);
            double f = Math.Max(0, Math.Min(1, (x - 9) / w));
            double v = Minimum + f * (Maximum - Minimum);
            v = Math.Round(v / Step) * Step;
            v = Math.Max(Minimum, Math.Min(Maximum, v));
            if (Math.Abs(v - _value) > 0.00001)
            {
                _value = v;
                Layout();
                ValueChanged?.Invoke(_value);
            }
        }

        private void Layout()
        {
            double w = Math.Max(1, ActualWidth - 18);
            double f = Maximum > Minimum ? (_value - Minimum) / (Maximum - Minimum) : 0;
            _fill.Width = 9 + f * w;
            _thumb.Margin = new Thickness(f * w, 0, 0, 0);
        }
    }

    /// <summary>A rounded "chip" used for choices.</summary>
    public class Chip : FlatButton
    {
        private bool _selected;

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                Background = value ? Theme.Accent : Brushes.Transparent;
                Foreground = value ? Theme.OnAccent : Theme.Text;
                BorderBrush = value ? Theme.Accent : Theme.Line;
            }
        }

        public Chip(string text, bool selected = false)
        {
            Content = text;
            MinHeight = 34;
            Height = 34;
            CornerRadius = new CornerRadius(17);
            Padding = new Thickness(14, 0, 14, 0);
            FontSize = 13;
            Selected = selected;
        }
    }

    public static class Icons
    {
        public const string Home = "M3 11l9-7 9 7M5 10v10h14V10";
        public const string Screens = "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8";
        public const string Playlist = "M4 6h12M4 11h12M4 16h7M16 15l5 3-5 3z";
        public const string Auto = "M13 3L5 14h6l-1 7 8-11h-6z";
        public const string Device = "M7 4h10a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3zM12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6";
        public const string Settings = "M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M4.9 19.1L7 17M17 7l2.1-2.1";
        public const string Plus = "M12 5v14M5 12h14";
        public const string Back = "M15 5l-7 7 7 7";
        public const string Trash = "M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13";
        public const string Copy = "M8 8h12v12H8zM4 16V4h12";
        public const string Play = "M7 4l13 8-13 8z";
        public const string Pause = "M8 5v14M16 5v14";
        public const string Up = "M12 19V5M5 12l7-7 7 7";
        public const string Down = "M12 5v14M5 12l7 7 7-7";
        public const string Folder = "M3 7a1 1 0 0 1 1-1h5l2 2h9a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z";
        public const string Image = "M3 5h18v14H3zM3 16l5-5 4 4 3-3 6 6M15.5 8.5a1.5 1.5 0 1 0 0 .01";
        public const string Refresh = "M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7";
        public const string Check = "M5 12l5 5 9-10";
        public const string Close = "M6 6l12 12M18 6L6 18";
        public const string Sun = "M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4";
        public const string Export = "M12 3v12M7 8l5-5 5 5M5 15v5h14v-5";
        public const string Import = "M12 15V3M7 10l5 5 5-5M5 15v5h14v-5";
        public const string Warning = "M12 3l10 18H2zM12 10v5M12 18v.01";
        public const string Link = "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1";
        public const string Edit = "M4 20h4L19 9l-4-4L4 16zM14 6l4 4";
    }
}
