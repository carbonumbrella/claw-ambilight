using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClawAmbilight
{
    /// <summary>Background loop: capture -> colors -> smoothing -> HID. Applies the battery policy,
    /// pauses on lock / display off, follows the display backlight when asked.</summary>
    public sealed class Engine : IDisposable
    {
        public volatile Settings Settings;
        public readonly float[] Current = new float[27];   // smoothed LED colors (for the UI preview)
        public volatile string Status = "";
        public volatile bool DisplayOff;

        readonly HidLeds _leds = new();
        readonly Thread _thread;
        volatile bool _run = true, _locked;
        int _displayBrightness = 100;
        long _brightnessCheckedAt;

        public Engine(Settings s, bool run = true)
        {
            Settings = s;
            _thread = new Thread(Loop) { IsBackground = true, Name = "ambilight", Priority = ThreadPriority.BelowNormal };
            if (!run) { _run = false; return; }  // --render: settings holder only, no capture, no LEDs
            SystemEvents.SessionSwitch += (_, e) =>
                _locked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect
                    || (_locked && e.Reason is not (SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect));
            _thread.Start();
        }

        public static bool OnBattery => SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;

        int DisplayBrightness()
        {
            long now = Environment.TickCount64;
            if (now - _brightnessCheckedAt < 1000) return _displayBrightness;  // WMI at most once a second
            _brightnessCheckedAt = now;
            try
            {
                using var q = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in q.Get()) { _displayBrightness = Convert.ToInt32(o["CurrentBrightness"]); break; }
            }
            catch { }
            return _displayBrightness;
        }

        void Loop()
        {
            using var cap = new Capture();
            float[]? targets = null;
            byte[]? lastSent = null; int lastBright = -1;
            bool dark = false;
            var sw = Stopwatch.StartNew();
            while (_run)
            {
                var s = Settings;
                bool battery = OnBattery;
                bool active = s.Enabled && !_locked && !DisplayOff && !(battery && s.OnBattery == BatteryPolicy.Off);
                if (!active)
                {
                    if (!dark) { _leds.Off(); dark = true; lastSent = null; Array.Clear(Current); }
                    Status = !s.Enabled ? "Выключено" : _locked ? "Пауза: экран заблокирован"
                        : DisplayOff ? "Пауза: экран погашен" : "Выключено на батарее";
                    Thread.Sleep(300);
                    continue;
                }
                dark = false;
                int fps = battery && s.OnBattery == BatteryPolicy.Eco ? Math.Min(s.Fps, 10) : s.Fps;
                long frameStart = sw.ElapsedMilliseconds;

                if (s.Mode == LightMode.Static)
                {
                    targets = StaticTargets(s.StaticLeft ?? s.StaticColor, s.StaticRight ?? s.StaticColor);
                    Status = "";  // header shows only pauses and problems
                    Thread.Sleep(50);
                }
                else
                {
                    try
                    {
                        float[]? grid = cap.Next(Math.Max(5, 1000 / fps));
                        if (grid != null) targets = ColorEngine.Targets(grid, cap.Width, cap.Height, s.Mode, s.Saturation);
                        Status = battery && s.OnBattery == BatteryPolicy.Eco ? "Экономно на батарее" : "";
                    }
                    catch (Exception e)
                    {
                        Status = "Захват экрана недоступен, повтор...";
                        Log.Write("capture: " + e.Message);
                        Thread.Sleep(1000);
                        continue;
                    }
                }
                if (targets == null) continue;

                // Smoothing with deadband (from the Python version): move toward the target only when the
                // change is visible, snap tiny tails so values settle and USB writes stop.
                float maxDiff = 0;
                for (int i = 0; i < 27; i++) maxDiff = Math.Max(maxDiff, Math.Abs(targets[i] - Current[i]));
                float k = s.Mode == LightMode.Static ? 0.25f : (float)s.Smoothness;
                for (int i = 0; i < 27; i++)
                {
                    float d = targets[i] - Current[i];
                    if (maxDiff >= 3f) Current[i] += d * k;
                    else if (Math.Abs(d) < 0.5f) Current[i] = targets[i];
                }

                int bright = s.BrightnessSource == BrightnessSource.Display ? DisplayBrightness() * s.Brightness / 100 : s.Brightness;
                var bytes = new byte[27];
                for (int i = 0; i < 27; i++) bytes[i] = (byte)Math.Clamp((int)Current[i], 0, 255);
                if (lastSent == null || bright != lastBright || !bytes.AsSpan().SequenceEqual(lastSent))
                {
                    if (_leds.Write(bytes, bright))
                    {
                        if (lastSent == null) Log.Write($"LEDs updated (brightness {bright}%)");
                        lastSent = bytes; lastBright = bright;
                    }
                    else { Status = "Контроллер подсветки не найден"; Log.Write("HID write failed"); Thread.Sleep(1000); }
                }

                // While colors are still easing toward the target keep ticking at the frame rate;
                // the capture call itself blocks until the screen changes.
                int spent = (int)(sw.ElapsedMilliseconds - frameStart), budget = 1000 / fps;
                if (maxDiff >= 3f && spent < budget) Thread.Sleep(budget - spent);
            }
            _leds.Off();
        }

        /// <summary>LEDs 4..7 = left stick, 0..3 + 8 = right stick and ABXY (Joy-Con style split).</summary>
        static float[] StaticTargets(string left, string right)
        {
            var t = new float[27];
            try
            {
                var l = System.Drawing.ColorTranslator.FromHtml(left);
                var r = System.Drawing.ColorTranslator.FromHtml(right);
                for (int i = 0; i < 9; i++)
                {
                    var c = i is >= 4 and <= 7 ? l : r;
                    t[i * 3] = c.R; t[i * 3 + 1] = c.G; t[i * 3 + 2] = c.B;
                }
            }
            catch { }
            return t;
        }

        public void Dispose()
        {
            if (!_thread.IsAlive) return;
            _run = false;
            _thread.Join(2000);
            _leds.Dispose();
        }
    }
}
