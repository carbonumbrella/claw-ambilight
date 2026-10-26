using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ClawAmbilight
{
    /// <summary>MSI Claw 8 AI+ controller LEDs over the vendor HID interface (VID 0DB0, PID 1902,
    /// MI_00 COL02). Protocol from claw_ambilight.py (verified by live probe): report 0x0F, header
    /// 0x3C, command 0x21 (write RAM profile), firmware 2.30+ addresses 0x02/0x4A, 9 LEDs, 4 frames
    /// written so the static profile never cycles. RAM only: no flash wear.</summary>
    public sealed class HidLeds : IDisposable
    {
        public const int LedCount = 9;
        const byte ReportId = 0x0F, Header = 0x3C, CmdWriteRam = 0x21, ModeStatic = 0x01, Add1 = 0x02, Add2 = 0x4A;

        SafeFileHandle? _h;
        string? _path;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(SafeFileHandle h, byte[] buf, int len, out int written, IntPtr ovl);

        public static string? FindDevicePath()
        {
            using var hid = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\HID");
            if (hid == null) return null;
            foreach (string sub in hid.GetSubKeyNames())
            {
                string u = sub.ToUpperInvariant();
                if (!u.Contains("VID_0DB0&PID_1902&MI_00") || !u.Contains("COL02")) continue;
                using var inst = hid.OpenSubKey(sub);
                string[]? ids = inst?.GetSubKeyNames();
                if (ids == null || ids.Length == 0) continue;
                return $@"\\?\hid#{sub.ToLowerInvariant()}#{ids[0].ToLowerInvariant()}#{{4d1e55b2-f16f-11cf-88cb-001111000030}}";
            }
            return null;
        }

        bool Open()
        {
            _h?.Dispose();
            _path ??= FindDevicePath();
            if (_path == null) return false;
            _h = CreateFileW(_path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); // RW, share RW, OPEN_EXISTING
            return !_h.IsInvalid;
        }

        /// <summary>rgb: 27 bytes (9 LEDs x RGB). brightness 0..100.</summary>
        public bool Write(ReadOnlySpan<byte> rgb, int brightness)
        {
            if ((_h == null || _h.IsInvalid) && !Open()) return false;
            foreach (byte[] block in Blocks(rgb, brightness))
            {
                if (!WriteFile(_h!, block, block.Length, out _, IntPtr.Zero))
                {
                    if (!Open()) return false; // device re-enumerated (sleep, USB reset): reopen once
                    if (!WriteFile(_h!, block, block.Length, out _, IntPtr.Zero)) return false;
                }
            }
            return true;
        }

        public void Off() => Write(new byte[LedCount * 3], 0);

        static byte[][] Blocks(ReadOnlySpan<byte> rgb, int brightness)
        {
            var blocks = new byte[4][];
            var b0 = new byte[64];
            b0[0] = ReportId; b0[3] = Header; b0[4] = CmdWriteRam; b0[5] = 0x01; b0[6] = Add1; b0[7] = Add2;
            b0[8] = 0x20; b0[9] = 0x00; b0[10] = ModeStatic; b0[11] = LedCount; b0[12] = 0x03;
            b0[13] = (byte)Math.Clamp(brightness, 0, 100);
            rgb.CopyTo(b0.AsSpan(14));
            blocks[0] = b0;
            int[] offsets = { Add2 + 32, Add2 + 59, Add2 + 86 };
            for (int i = 0; i < 3; i++)
            {
                var b = new byte[64];
                b[0] = ReportId; b[3] = Header; b[4] = CmdWriteRam; b[5] = 0x01; b[6] = Add1;
                b[7] = (byte)(offsets[i] & 0xFF); b[8] = (byte)rgb.Length;
                rgb.CopyTo(b.AsSpan(9));
                blocks[i + 1] = b;
            }
            return blocks;
        }

        public void Dispose() => _h?.Dispose();
    }
}
