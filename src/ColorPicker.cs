using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ClawAmbilight
{
    /// <summary>Compact color picker: a chip with the current color and a HEX box; clicking the chip
    /// opens a popup with a saturation/value square, a hue strip and quick presets. Any 24-bit color.</summary>
    public sealed class ColorPicker : Grid
    {
        public event Action<string>? ColorChanged;

        static readonly string[] Presets = { Settings.JoyConBlue, Settings.JoyConRed, "#E6FF00", "#FF3278", "#1EDC00",
            "#FF9500", "#AF52DE", "#3E7BFA", "#FFFFFF" };  // Joy-Con neon blue/red/yellow/pink/green + basics
        const double SvW = 228, SvH = 150, HueH = 18;

        readonly Border _chip = new() { Width = 46, Height = 30, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1.5) };
        readonly TextBox _hexBox = new() { Width = 86, Height = 30, VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 13,
            BorderThickness = new Thickness(0), MaxLength = 7 };
        readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom, VerticalOffset = 6 };
        readonly Rectangle _svHue = new() { Width = SvW, Height = SvH, RadiusX = 8, RadiusY = 8 };
        readonly Ellipse _svMark = new() { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
        readonly Ellipse _hueMark = new() { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
        double _h, _s = 1, _v = 1;
        bool _updating;

        public string Hex { get; private set; } = "#FFFFFF";

        /// <summary>For --render picker: detaches the popup content so it can be drawn offscreen.</summary>
        public UIElement TakePopupContent() { var c = (UIElement)_popup.Child; _popup.Child = null; return c; }

        public ColorPicker()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Children.Add(_chip);
            SetColumn(_hexBox, 2);
            Children.Add(_hexBox);
            _hexBox.SetResourceReference(Control.BackgroundProperty, "Ghost");
            _hexBox.SetResourceReference(Control.ForegroundProperty, "Fg");
            _hexBox.SetResourceReference(TextBoxBase.CaretBrushProperty, "Fg");
            _chip.SetResourceReference(Border.BorderBrushProperty, "Border");
            _hexBox.Template = RoundBoxTemplate();
            _chip.MouseLeftButtonUp += (_, _) => { _popup.PlacementTarget = _chip; _popup.IsOpen = true; };
            _hexBox.TextChanged += (_, _) => { if (!_updating && TryParse(_hexBox.Text, out var c)) SetFromColor(c, true, false); };
            _hexBox.LostFocus += (_, _) => _hexBox.Text = Hex;
            _popup.Child = BuildPopup();
        }

        public void SetHex(string? hex)
        {
            if (TryParse(hex, out var c)) SetFromColor(c, false, true);
        }

        UIElement BuildPopup()
        {
            var root = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12), BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35 } };
            root.SetResourceReference(Border.BackgroundProperty, "Bg");
            root.SetResourceReference(Border.BorderBrushProperty, "Border");
            var stack = new StackPanel();

            // saturation/value square: hue color, white fades in from the left, black from the bottom
            var sv = new Grid { Width = SvW, Height = SvH, Cursor = Cursors.Cross, ClipToBounds = true };
            sv.Children.Add(_svHue);
            sv.Children.Add(new Rectangle { RadiusX = 8, RadiusY = 8, Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0) });
            sv.Children.Add(new Rectangle { RadiusX = 8, RadiusY = 8, Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) });
            var svCanvas = new Canvas { IsHitTestVisible = false };
            svCanvas.Children.Add(_svMark);
            sv.Children.Add(svCanvas);
            void PickSv(Point p) { _s = Math.Clamp(p.X / SvW, 0, 1); _v = 1 - Math.Clamp(p.Y / SvH, 0, 1); FromHsv(); }
            sv.MouseLeftButtonDown += (_, e) => { sv.CaptureMouse(); PickSv(e.GetPosition(sv)); };
            sv.MouseMove += (_, e) => { if (sv.IsMouseCaptured) PickSv(e.GetPosition(sv)); };
            sv.MouseLeftButtonUp += (_, _) => sv.ReleaseMouseCapture();
            stack.Children.Add(sv);

            // hue strip
            var hueStops = new GradientStopCollection();
            for (int i = 0; i <= 6; i++) hueStops.Add(new GradientStop(HsvToColor(i * 60 % 360, 1, 1), i / 6.0));
            var hue = new Grid { Width = SvW, Height = HueH, Margin = new Thickness(0, 10, 0, 0), Cursor = Cursors.Hand };
            hue.Children.Add(new Rectangle { RadiusX = 9, RadiusY = 9, Fill = new LinearGradientBrush(hueStops, 0) });
            var hueCanvas = new Canvas { IsHitTestVisible = false };
            hueCanvas.Children.Add(_hueMark);
            hue.Children.Add(hueCanvas);
            void PickHue(Point p) { _h = Math.Clamp(p.X / SvW, 0, 0.9999) * 360; FromHsv(); }
            hue.MouseLeftButtonDown += (_, e) => { hue.CaptureMouse(); PickHue(e.GetPosition(hue)); };
            hue.MouseMove += (_, e) => { if (hue.IsMouseCaptured) PickHue(e.GetPosition(hue)); };
            hue.MouseLeftButtonUp += (_, _) => hue.ReleaseMouseCapture();
            stack.Children.Add(hue);

            // quick presets
            var presets = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), Width = SvW };
            foreach (string hex in Presets)
            {
                TryParse(hex, out var c);
                var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 3, 0),
                    Background = new SolidColorBrush(c), Cursor = Cursors.Hand, ToolTip = hex, BorderThickness = new Thickness(1) };
                dot.SetResourceReference(Border.BorderBrushProperty, "Border");
                dot.MouseLeftButtonUp += (_, _) => SetFromColor(c, true, true);
                presets.Children.Add(dot);
            }
            stack.Children.Add(presets);
            root.Child = stack;
            return root;
        }

        void FromHsv() => Apply(HsvToColor(_h, _s, _v), true, true, keepHsv: true);

        void SetFromColor(Color c, bool notify, bool updateText)
        {
            ColorToHsv(c, out double h, out _s, out _v);
            if (_s > 0.001 && _v > 0.001) _h = h;  // keep the hue on grays/black so the marker does not jump
            Apply(c, notify, updateText, keepHsv: true);
        }

        void Apply(Color c, bool notify, bool updateText, bool keepHsv)
        {
            Hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            _chip.Background = new SolidColorBrush(c);
            _svHue.Fill = new SolidColorBrush(HsvToColor(_h, 1, 1));
            Canvas.SetLeft(_svMark, _s * SvW - 7); Canvas.SetTop(_svMark, (1 - _v) * SvH - 7);
            Canvas.SetLeft(_hueMark, _h / 360 * SvW - 8); Canvas.SetTop(_hueMark, 1);
            _svMark.Stroke = _v > 0.55 && _s < 0.45 ? Brushes.Black : Brushes.White;
            if (updateText)
            {
                _updating = true;
                _hexBox.Text = Hex;
                _updating = false;
            }
            if (notify) ColorChanged?.Invoke(Hex);
        }

        static bool TryParse(string? s, out Color c)
        {
            c = Colors.White;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().TrimStart('#');
            if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, null, out int v)) return false;
            c = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }

        static Color HsvToColor(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            (double r, double g, double b) = (h % 360) switch
            {
                < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
                < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x),
            };
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        static void ColorToHsv(Color col, out double h, out double s, out double v)
        {
            double r = col.R / 255.0, g = col.G / 255.0, b = col.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
            s = max == 0 ? 0 : d / max;
            v = max;
        }

        static ControlTemplate RoundBoxTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
            host.SetValue(MarginProperty, new Thickness(6, 0, 6, 0));
            host.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(host);
            return new ControlTemplate(typeof(TextBox)) { VisualTree = border };
        }
    }
}
