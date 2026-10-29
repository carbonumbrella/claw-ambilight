using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ClawAmbilight
{
    public partial class MainWindow : Window
    {
        readonly Ellipse[] _ledDots = new Ellipse[HidLeds.LedCount];
        readonly DispatcherTimer _preview = new() { Interval = TimeSpan.FromMilliseconds(100) };
        readonly DispatcherTimer _saveDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
        bool _loading;

        public MainWindow()
        {
            InitializeComponent();
            ApplyTheme();
            BuildPreview();
            PickerLeft.ColorChanged += _ => Changed();
            PickerRight.ColorChanged += _ => Changed();
            JoyConBtn.Click += (_, _) =>
            {
                _loading = true;
                PickerLeft.SetHex(Settings.JoyConBlue);
                PickerRight.SetHex(Settings.JoyConRed);
                _loading = false;
                Changed();
            };
            foreach (var rb in new[] { ModeAmbient, ModeVibrant, ModeStatic, BrightDisplay, BrightManual, Fps15, Fps30, Fps60, BatNormal, BatEco, BatOff })
                rb.Checked += (_, _) => Changed();
            EnabledSwitch.Click += (_, _) => Changed();
            AutostartSwitch.Click += (_, _) => Changed();
            BrightSlider.ValueChanged += (_, _) => Changed();
            SatSlider.ValueChanged += (_, _) => Changed();
            SmoothSlider.ValueChanged += (_, _) => Changed();
            _saveDelay.Tick += (_, _) => { _saveDelay.Stop(); App.Apply(Collect()); };
            _preview.Tick += (_, _) => UpdatePreview();
            TitleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            CloseBtn.Click += (_, _) => Close();
            HideBtn.Click += (_, _) => Close();
            QuitBtn.Click += (_, _) => ((App)Application.Current).Quit();
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            IsVisibleChanged += (_, _) => { if (IsVisible) { _previewValid = false; _preview.Start(); } else _preview.Stop(); };
            // closing destroys the window (it is recreated on show): a hidden WPF window kept ~150 MB of
            // software-rendering buffers; display on/off notifications live in App's own hidden HWND
            UserPreferenceChangedEventHandler theme = (_, _) => Dispatcher.Invoke(ApplyTheme);
            SystemEvents.UserPreferenceChanged += theme;
            Closed += (_, _) => { _preview.Stop(); _saveDelay.Stop(); SystemEvents.UserPreferenceChanged -= theme; };
            LoadFrom(App.Engine.Settings);
            // never wider or taller than the screen, also after rotation (portrait Claw: ~690 DIP wide):
            // one column, the scroller takes the rest of the height, re-centered
            Responsive.Attach(this, 840, Scroller);
        }

        // ---------------- settings <-> controls

        public void LoadFrom(Settings s)
        {
            _loading = true;
            EnabledSwitch.IsChecked = s.Enabled;
            (s.Mode switch { LightMode.Vibrant => ModeVibrant, LightMode.Static => ModeStatic, _ => ModeAmbient }).IsChecked = true;
            (s.BrightnessSource == BrightnessSource.Manual ? BrightManual : BrightDisplay).IsChecked = true;
            BrightSlider.Value = s.Brightness;
            SatSlider.Value = s.Saturation;
            SmoothSlider.Value = Math.Round(1 + Math.Log(s.Smoothness / 0.03) / Math.Log(1.45));
            (s.Fps <= 15 ? Fps15 : s.Fps >= 60 ? Fps60 : Fps30).IsChecked = true;
            (s.OnBattery switch { BatteryPolicy.Normal => BatNormal, BatteryPolicy.Off => BatOff, _ => BatEco }).IsChecked = true;
            AutostartSwitch.IsChecked = s.Autostart;
            PickerLeft.SetHex(s.StaticLeft ?? s.StaticColor);
            PickerRight.SetHex(s.StaticRight ?? s.StaticColor);
            _loading = false;
            UpdateLabels();
        }

        Settings Collect()
        {
            var s = App.Engine.Settings.Clone();
            s.Enabled = EnabledSwitch.IsChecked == true;
            s.Mode = ModeVibrant.IsChecked == true ? LightMode.Vibrant : ModeStatic.IsChecked == true ? LightMode.Static : LightMode.Ambient;
            s.BrightnessSource = BrightManual.IsChecked == true ? BrightnessSource.Manual : BrightnessSource.Display;
            s.Brightness = (int)Math.Round(BrightSlider.Value);
            s.Saturation = Math.Round(SatSlider.Value, 1);
            s.Smoothness = Math.Round(0.03 * Math.Pow(1.45, SmoothSlider.Value - 1), 3);
            s.Fps = Fps15.IsChecked == true ? 15 : Fps60.IsChecked == true ? 60 : 30;
            s.OnBattery = BatNormal.IsChecked == true ? BatteryPolicy.Normal : BatOff.IsChecked == true ? BatteryPolicy.Off : BatteryPolicy.Eco;
            s.Autostart = AutostartSwitch.IsChecked == true;
            s.StaticLeft = PickerLeft.Hex;
            s.StaticRight = PickerRight.Hex;
            return s;
        }

        void Changed()
        {
            if (_loading) return;
            UpdateLabels();
            App.Engine.Settings = Collect();  // instant effect; the file write is debounced
            _saveDelay.Stop(); _saveDelay.Start();
        }

        void UpdateLabels()
        {
            bool manual = BrightManual.IsChecked == true;
            BrightText.Text = manual ? $"{BrightSlider.Value:0}%" : $"до {BrightSlider.Value:0}% экрана";
            SatText.Text = $"×{SatSlider.Value:0.0}";
            SmoothText.Text = SmoothSlider.Value <= 3 ? "плавно" : SmoothSlider.Value >= 8 ? "мгновенно" : "средне";
            SatSlider.IsEnabled = ModeAmbient.IsChecked == true;
            bool fixedColor = ModeStatic.IsChecked == true;
            Swatches.Visibility = fixedColor ? Visibility.Visible : Visibility.Collapsed;
            FpsBlock.Visibility = fixedColor ? Visibility.Collapsed : Visibility.Visible;  // no screen capture then
            SmoothSlider.IsEnabled = !fixedColor;
        }

        // ---------------- live preview (layout like the Claw: left stick top-left, ABXY top-right,
        // right stick bottom-right)

        void BuildPreview()
        {
            (double x, double y)[] stickCenters = { (256, 76), (86, 46) };  // right, left
            // LED order inside each ring: right 0=SW 1=SE 2=NE 3=NW, left 4=NE 5=NW 6=SW 7=SE
            double[][] angles = { new[] { 225.0, 315, 45, 135 }, new[] { 45.0, 135, 225, 315 } };
            for (int ring = 0; ring < 2; ring++)
            {
                var (cx, cy) = stickCenters[ring];
                var baseCircle = new Ellipse { Width = 46, Height = 46, Fill = (Brush)FindResource("Bg"), Stroke = (Brush)FindResource("Ghost"), StrokeThickness = 3 };
                Canvas.SetLeft(baseCircle, cx - 23); Canvas.SetTop(baseCircle, cy - 23);
                Preview.Children.Add(baseCircle);
                for (int i = 0; i < 4; i++)
                {
                    double a = angles[ring][i] * Math.PI / 180;
                    AddLed(ring * 4 + i, cx + 34 * Math.Cos(a), cy - 34 * Math.Sin(a), 16);
                }
            }
            AddLed(8, 318, 30, 22);
            var label = new TextBlock { Text = "ABXY", FontSize = 10, Foreground = (Brush)FindResource("Sub") };
            Canvas.SetLeft(label, 305); Canvas.SetTop(label, 46);
            Preview.Children.Add(label);
        }

        // The glow is a radial gradient, not a DropShadowEffect: blur effects in this AllowsTransparency window
        // are rendered in software for the whole window, 10x a second that cost ~23% of a core.
        void AddLed(int index, double x, double y, double size)
        {
            var glow = new Ellipse { Width = size * 2.6, Height = size * 2.6, Fill = Brushes.Transparent, IsHitTestVisible = false };
            Canvas.SetLeft(glow, x - size * 1.3); Canvas.SetTop(glow, y - size * 1.3);
            Preview.Children.Add(glow);
            var e = new Ellipse { Width = size, Height = size, Fill = Brushes.Black };
            Canvas.SetLeft(e, x - size / 2); Canvas.SetTop(e, y - size / 2);
            Preview.Children.Add(e);
            _ledDots[index] = e;
            _ledGlows[index] = glow;
        }

        readonly Ellipse[] _ledGlows = new Ellipse[HidLeds.LedCount];
        readonly Color[] _shown = new Color[HidLeds.LedCount];
        bool _previewValid;

        public void RenderPreviewOnce() { _previewValid = false; UpdatePreview(); }

        /// <summary>Repaints only what changed: an unchanged frame (static screen, lights off) costs nothing.</summary>
        void UpdatePreview()
        {
            var cur = App.Engine.Current;
            for (int i = 0; i < HidLeds.LedCount; i++)
            {
                var c = Color.FromRgb((byte)Math.Clamp(cur[i * 3], 0, 255), (byte)Math.Clamp(cur[i * 3 + 1], 0, 255), (byte)Math.Clamp(cur[i * 3 + 2], 0, 255));
                if (_previewValid && c == _shown[i]) continue;
                _shown[i] = c;
                var fill = new SolidColorBrush(c); fill.Freeze();
                _ledDots[i].Fill = fill;
                var glow = new RadialGradientBrush(Color.FromArgb(150, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B)); glow.Freeze();
                _ledGlows[i].Fill = glow;
            }
            _previewValid = true;
            string status = App.Engine.Status;
            if (StatusText.Text != status) StatusText.Text = status;
        }

        // ---------------- theme (follows Windows light/dark like the assistant's dialog)

        void ApplyTheme()
        {
            bool light = false;
            try { light = (int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) ?? 0) == 1; }
            catch { }
            void Set(string key, string hex) => Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            if (light) { Set("Bg", "#F7F7F8"); Set("Fg", "#1B1B1F"); Set("Sub", "#6B6B73"); Set("Card", "#FFFFFF"); Set("Border", "#E3E3E8"); Set("Ghost", "#E8E8ED"); }
            else { Set("Bg", "#202024"); Set("Fg", "#F3F3F5"); Set("Sub", "#A4A4AE"); Set("Card", "#2E2E35"); Set("Border", "#38383F"); Set("Ghost", "#34343B"); }
            Set("Accent", "#3E7BFA");
        }

        // ---------------- display on/off -> pause (the LEDs should not glow with the screen off)

        static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

        [DllImport("user32.dll")]
        static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

        /// <summary>A hidden HWND that only receives display on/off notifications (lives as long as the app).</summary>
        public static HwndSource CreatePowerWindow()
        {
            var src = new HwndSource(new HwndSourceParameters("ClawAmbilightPower") { Width = 0, Height = 0, WindowStyle = 0 });
            var guid = ConsoleDisplayState;
            RegisterPowerSettingNotification(src.Handle, ref guid, 0);
            src.AddHook(WndProc);
            return src;
        }

        static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_POWERBROADCAST = 0x218, PBT_POWERSETTINGCHANGE = 0x8013;
            if (msg == WM_POWERBROADCAST && wParam.ToInt32() == PBT_POWERSETTINGCHANGE)
            {
                var guid = Marshal.PtrToStructure<Guid>(lParam);
                if (guid == ConsoleDisplayState)
                    App.Engine.DisplayOff = Marshal.ReadInt32(lParam, 20) == 0;  // 0 off, 1 on, 2 dimmed
            }
            return IntPtr.Zero;
        }
    }
}
