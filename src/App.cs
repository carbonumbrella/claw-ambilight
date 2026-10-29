using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ClawAmbilight
{
    /// <summary>Entry point: single instance, tray icon, settings file watcher, engine lifetime.
    /// "--tray" starts hidden (autostart). A second launch shows the existing window.</summary>
    public sealed class App : Application
    {
        const string InstanceMutex = "Local\\ClawAmbilightApp";
        const string ShowEvent = "Local\\ClawAmbilightShow";
        const string QuitEvent = "Local\\ClawAmbilightQuit";  // "--quit": graceful exit, LEDs off
        const string PythonMutex = "Global\\MSIClawAmbilightSingletonMutex"; // keeps claw_ambilight.py from starting

        public static Engine Engine = null!;
        static Mutex? _mutex, _pyMutex;
        Forms.NotifyIcon _tray = null!;
        MainWindow? _win;  // only while the settings window is open

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
        System.Windows.Interop.HwndSource? _power;
        FileSystemWatcher? _watcher;
        public static DateTime IgnoreWatcherUntil;

        [STAThread]
        public static void Main(string[] args)
        {
            int ri = Array.IndexOf(args, "--render");
            if (ri >= 0 && ri + 1 < args.Length) { Render(args[ri + 1], args.Length > ri + 2 ? args[ri + 2] : ""); return; }
            _mutex = new Mutex(true, InstanceMutex, out bool first);
            bool quit = Array.IndexOf(args, "--quit") >= 0;
            if (!first)
            {
                try { EventWaitHandle.OpenExisting(quit ? QuitEvent : ShowEvent).Set(); } catch { }
                if (quit)  // wait until the running copy has really exited (uninstaller deletes the exe next)
                {
                    try { _mutex.WaitOne(5000); }
                    catch (AbandonedMutexException) { }  // the owner exited without releasing: done
                }
                return;
            }
            if (quit) return;  // nothing running
            StopPythonVersion();
            _pyMutex = new Mutex(true, PythonMutex, out _);
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Startup += (_, _) => app.Start(Array.IndexOf(args, "--tray") >= 0);
            app.Run();
        }

        /// <summary>Draws the settings window to a PNG without showing it (UI checks). Optional
        /// "static" / "battery" variants switch the visible state.</summary>
        static void Render(string png, string variant)
        {
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Startup += (_, _) =>
            {
                var s = Settings.Load();
                if (variant == "ambient") s.Mode = LightMode.Ambient;
                if (variant is "static" or "picker") { s.Mode = LightMode.Static; s.StaticLeft = Settings.JoyConBlue; s.StaticRight = Settings.JoyConRed; s.BrightnessSource = BrightnessSource.Manual; s.Brightness = 60; }
                Engine = new Engine(s, run: false);
                float[] demo = { 255, 80, 40, 255, 140, 0, 60, 200, 255, 120, 60, 255, 40, 220, 120, 0, 160, 255, 255, 40, 120, 250, 200, 40, 60, 200, 255 };
                if (variant == "static")
                    for (int i = 0; i < 9; i++)
                    {
                        bool left = i is >= 4 and <= 7;
                        demo[i * 3] = left ? 10 : 255; demo[i * 3 + 1] = left ? 185 : 60; demo[i * 3 + 2] = left ? 230 : 40;
                    }
                Array.Copy(demo, Engine.Current, 27);
                Engine.Status = "";
                var w = new MainWindow();
                var root = (System.Windows.FrameworkElement)w.Content;
                w.Content = null;
                w.RenderPreviewOnce();
                var holder = new System.Windows.Controls.Border { Background = System.Windows.Media.Brushes.DimGray };
                foreach (var k in w.Resources.Keys) holder.Resources[k] = w.Resources[k];  // styles and theme brushes
                holder.Child = variant == "picker" ? w.PickerLeft.TakePopupContent() : root;
                holder.Measure(new System.Windows.Size(variant == "picker" ? 300 : 840, double.PositiveInfinity));
                holder.Arrange(new System.Windows.Rect(holder.DesiredSize));
                holder.UpdateLayout();
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)holder.ActualWidth, (int)holder.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(holder);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var f = File.Create(png)) enc.Save(f);
                app.Shutdown();
            };
            app.Run();
        }

        static void StopPythonVersion()
        {
            try
            {
                using var q = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE 'python%'");
                foreach (ManagementObject o in q.Get())
                    if ((o["CommandLine"] as string ?? "").Contains("claw_ambilight", StringComparison.OrdinalIgnoreCase))
                    {
                        Process.GetProcessById(Convert.ToInt32(o["ProcessId"])).Kill();
                        Log.Write("stopped the Python ambilight");
                    }
            }
            catch (Exception e) { Log.Write("stop python: " + e.Message); }
        }

        void Start(bool hidden)
        {
            var s = Settings.Load();
            if (!File.Exists(Settings.FilePath))
            {
                s.Autostart = Autostart.IsSet();  // first run: keep what the installer's checkbox decided
                s.Save();
            }
            Autostart.Apply(s.Autostart);
            Engine = new Engine(s);
            _power = ClawAmbilight.MainWindow.CreatePowerWindow();  // display on/off -> pause, without any window open

            _tray = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "Подсветка Claw", Visible = true };
            _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowWindow(); };
            _tray.ContextMenuStrip = BuildMenu();

            _watcher = new FileSystemWatcher(Settings.Dir, "settings.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            _watcher.Changed += (_, _) => OnExternalChange();
            _watcher.Renamed += (_, _) => OnExternalChange();
            _watcher.EnableRaisingEvents = true;

            var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEvent);
            new Thread(() => { while (showSignal.WaitOne()) Dispatcher.Invoke(ShowWindow); }) { IsBackground = true }.Start();
            var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEvent);
            new Thread(() => { quitSignal.WaitOne(); Dispatcher.Invoke(Quit); }) { IsBackground = true }.Start();

            if (!hidden) ShowWindow();
        }

        void OnExternalChange()
        {
            if (DateTime.Now < IgnoreWatcherUntil) return;
            Thread.Sleep(150);  // let the writer finish
            Dispatcher.Invoke(() =>
            {
                var s = Settings.Load();
                Engine.Settings = s;
                Autostart.Apply(s.Autostart);
                _win?.LoadFrom(s);
                Log.Write("settings changed externally");
            });
        }

        /// <summary>Called by the window and the tray menu.</summary>
        public static void Apply(Settings s)
        {
            Engine.Settings = s;
            IgnoreWatcherUntil = DateTime.Now.AddMilliseconds(800);
            try { s.Save(); } catch (Exception e) { Log.Write("save: " + e.Message); }
            Autostart.Apply(s.Autostart);
        }

        void ShowWindow()
        {
            if (_win == null)
            {
                _win = new MainWindow();
                _win.Closed += (_, _) =>
                {
                    _win = null;
                    GC.Collect();  // give the window's buffers back now, not at some later collection
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    // WPF keeps ~80 MB of native render buffers after the first window; nothing uses them
                    // while the window is closed, so let Windows page them out of RAM
                    SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                };
            }
            _win.Show();
            if (_win.WindowState == WindowState.Minimized) _win.WindowState = WindowState.Normal;
            _win.Activate();
        }

        Forms.ContextMenuStrip BuildMenu()
        {
            var m = new Forms.ContextMenuStrip();
            var toggle = new Forms.ToolStripMenuItem("Подсветка включена") { CheckOnClick = false };
            toggle.Click += (_, _) => { var s = Engine.Settings.Clone(); s.Enabled = !s.Enabled; Apply(s); _win?.LoadFrom(s); };
            m.Items.Add(toggle);
            m.Items.Add(new Forms.ToolStripSeparator());
            foreach (var (mode, name) in new[] { (LightMode.Ambient, "Мягкий"), (LightMode.Vibrant, "Яркий"), (LightMode.Static, "Один цвет") })
            {
                var item = new Forms.ToolStripMenuItem(name);
                item.Click += (_, _) => { var s = Engine.Settings.Clone(); s.Mode = mode; s.Enabled = true; Apply(s); _win?.LoadFrom(s); };
                item.Tag = mode;
                m.Items.Add(item);
            }
            m.Items.Add(new Forms.ToolStripSeparator());
            m.Items.Add("Настройки", null, (_, _) => ShowWindow());
            m.Items.Add("Выход", null, (_, _) => Quit());
            m.Opening += (_, _) =>
            {
                var s = Engine.Settings;
                toggle.Checked = s.Enabled;
                foreach (var it in m.Items) if (it is Forms.ToolStripMenuItem mi && mi.Tag is LightMode lm) mi.Checked = lm == s.Mode;
            };
            return m;
        }

        public void Quit()
        {
            _tray.Visible = false;
            _tray.Dispose();
            Engine.Dispose();  // turns the LEDs off
            Shutdown();
        }

        static Icon MakeIcon()
        {
            try
            {
                var own = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
                if (own != null) return new Icon(own, Forms.SystemInformation.SmallIconSize);
            }
            catch { }
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var ring = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(62, 123, 250), Color.FromArgb(229, 72, 77), 45f);
                g.FillEllipse(ring, 2, 2, 28, 28);
                g.FillEllipse(Brushes.Black, 9, 9, 14, 14);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    static class Autostart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsSet()
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue("ClawAmbilight") != null;
        }
        public static void Apply(bool on)
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, true);
                if (k == null) return;
                if (on) k.SetValue("ClawAmbilight", $"\"{Environment.ProcessPath}\" --tray");
                else if (k.GetValue("ClawAmbilight") != null) k.DeleteValue("ClawAmbilight");
            }
            catch (Exception e) { Log.Write("autostart: " + e.Message); }
        }
    }
}
