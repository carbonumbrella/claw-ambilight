using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ClawAmbilight
{
    /// <summary>Which part of the primary screen feeds the LEDs: the whole screen while the foreground
    /// window covers it (games, fullscreen video, F11 browser), otherwise the work area, so the taskbar is
    /// cut wherever it docks (top, bottom, left, right; auto-hide = nothing to cut). Fractions 0..1 of the
    /// screen in desktop orientation, re-read at most twice a second.</summary>
    public static class ScreenArea
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT Monitor, Work; public int Flags; }

        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, int flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfoW(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);

        static long _at = -10_000;  // not long.MinValue: now - MinValue overflows and the area never refreshed
        static (double X0, double Y0, double X1, double Y1) _crop = (0, 0, 1, 1);
        public static bool Fullscreen { get; private set; }

        public static (double X0, double Y0, double X1, double Y1) Crop()
        {
            long now = Environment.TickCount64;
            if (now - _at < 500) return _crop;
            _at = now;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(MonitorFromPoint(default, 1), ref mi)) return _crop = (0, 0, 1, 1); // PRIMARY
            RECT m = mi.Monitor, w = mi.Work;
            Fullscreen = CoversMonitor(m);
            double mw = Math.Max(1, m.R - m.L), mh = Math.Max(1, m.B - m.T);
            var crop = Fullscreen ? (0.0, 0.0, 1.0, 1.0)
                : ((w.L - m.L) / mw, (w.T - m.T) / mh, (w.R - m.L) / mw, (w.B - m.T) / mh);
            if (crop != _crop) Log.Write($"screen area: {(Fullscreen ? "fullscreen" : "work area")} " +
                                         $"{crop.Item1:0.000},{crop.Item2:0.000}..{crop.Item3:0.000},{crop.Item4:0.000}");
            return _crop = crop;
        }

        static bool CoversMonitor(RECT m)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || !GetWindowRect(fg, out RECT r)) return false;
            var cls = new StringBuilder(64);
            GetClassNameW(fg, cls, 64);
            if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false; // desktop, taskbar
            return r.L <= m.L && r.T <= m.T && r.R >= m.R && r.B >= m.B;
        }
    }
}
