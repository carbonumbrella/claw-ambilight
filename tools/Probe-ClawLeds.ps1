# Finds the MSI Claw controller's LED interface for ClawAmbilight on models it was not tested on (Claw A8, A1M).
#   Probe-ClawLeds.ps1          read-only: every MSI (VID 0DB0) device and HID interface with its report sizes
#   Probe-ClawLeds.ps1 -Test    also lights each candidate interface red with ClawAmbilight's own command and asks
#                               whether the sticks lit up (RAM only: a reboot or ClawAmbilight resets it)
# No admin needed. The report goes to Desktop\claw-leds-probe.txt.
param([switch]$Test)
$ErrorActionPreference = 'Stop'
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) 'claw-leds-probe.txt'
$lines = New-Object System.Collections.Generic.List[string]
function Say([string]$s) { $lines.Add($s); Write-Host $s }

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class ClawProbe
{
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevsW(ref Guid g, IntPtr e, IntPtr h, int f);
    [StructLayout(LayoutKind.Sequential)] struct IfData { public int cbSize; public Guid g; public int flags; public IntPtr r; }
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref IfData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr s, ref IfData d, IntPtr buf, int size, out int req, IntPtr di);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string n, uint a, uint s, IntPtr sec, uint d, uint f, IntPtr t);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, byte[] b, int l, out int w, IntPtr o);
    [StructLayout(LayoutKind.Sequential)] struct Attr { public int Size; public ushort Vid; public ushort Pid; public ushort Ver; }
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attr a);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle h, byte[] b, int l);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [StructLayout(LayoutKind.Sequential)] struct Caps
    {
        public ushort Usage, UsagePage, InLen, OutLen, FeatLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 27)] public ushort[] Rest;
    }
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, ref Caps c);

    /// <summary>Device interface paths of all present HID collections.</summary>
    public static List<string> Paths()
    {
        var list = new List<string>();
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevsW(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);  // PRESENT | DEVICEINTERFACE
        try
        {
            var d = new IfData(); d.cbSize = Marshal.SizeOf(typeof(IfData));
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
            {
                int req;
                SetupDiGetDeviceInterfaceDetailW(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(req);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref d, buf, req, out req, IntPtr.Zero))
                        list.Add(Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4)));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    /// <summary>"pid ver | product | usagePage usage | in out feature | writable" for one path.</summary>
    public static string Describe(string path)
    {
        using (var h = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            if (h.IsInvalid) return "cannot open (error " + Marshal.GetLastWin32Error() + ")";
            var a = new Attr(); a.Size = Marshal.SizeOf(typeof(Attr));
            HidD_GetAttributes(h, ref a);
            var name = new byte[256];
            string product = HidD_GetProductString(h, name, name.Length) ? Encoding.Unicode.GetString(name).TrimEnd('\0') : "";
            var c = new Caps();
            IntPtr pp;
            if (HidD_GetPreparsedData(h, out pp)) { HidP_GetCaps(pp, ref c); HidD_FreePreparsedData(pp); }
            bool writable;
            using (var w = CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) writable = !w.IsInvalid;
            return string.Format("PID {0:X4} ver {1:X4} | \"{2}\" | page 0x{3:X4} usage 0x{4:X4} | in {5} out {6} feature {7} | {8}",
                a.Pid, a.Ver, product, c.UsagePage, c.Usage, c.InLen, c.OutLen, c.FeatLen, writable ? "writable" : "NOT writable");
        }
    }

    /// <summary>ClawAmbilight's RAM profile write (report 0x0F, header 0x3C, cmd 0x21, addresses 0x02/0x4A,
    /// 9 LEDs, static mode): all LEDs one color. Returns the Win32 error or 0.</summary>
    public static int Light(string path, byte r, byte g, byte b, int brightness)
    {
        var rgb = new byte[27];
        for (int i = 0; i < 9; i++) { rgb[i * 3] = r; rgb[i * 3 + 1] = g; rgb[i * 3 + 2] = b; }
        var blocks = new List<byte[]>();
        var b0 = new byte[64];
        b0[0] = 0x0F; b0[3] = 0x3C; b0[4] = 0x21; b0[5] = 0x01; b0[6] = 0x02; b0[7] = 0x4A;
        b0[8] = 0x20; b0[9] = 0x00; b0[10] = 0x01; b0[11] = 9; b0[12] = 0x03; b0[13] = (byte)brightness;
        Array.Copy(rgb, 0, b0, 14, 27);
        blocks.Add(b0);
        foreach (int off in new[] { 0x4A + 32, 0x4A + 59, 0x4A + 86 })
        {
            var x = new byte[64];
            x[0] = 0x0F; x[3] = 0x3C; x[4] = 0x21; x[5] = 0x01; x[6] = 0x02; x[7] = (byte)(off & 0xFF); x[8] = 27;
            Array.Copy(rgb, 0, x, 9, 27);
            blocks.Add(x);
        }
        using (var h = CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            if (h.IsInvalid) return Marshal.GetLastWin32Error();
            foreach (var blk in blocks)
            {
                int w;
                if (!WriteFile(h, blk, blk.Length, out w, IntPtr.Zero)) return Marshal.GetLastWin32Error();
            }
        }
        return 0;
    }
}
'@

$os = Get-CimInstance Win32_OperatingSystem
$cs = Get-CimInstance Win32_ComputerSystem
Say "ClawAmbilight LED probe $(Get-Date -Format s)"
Say "Device: $($cs.Manufacturer) $($cs.Model) | CPU: $((Get-CimInstance Win32_Processor).Name) | $($os.Caption) $($os.BuildNumber)"
Say "ClawAmbilight running: $([bool](Get-Process ClawAmbilight -ErrorAction SilentlyContinue)) | HidHide installed: $([bool](Get-Service HidHide -ErrorAction SilentlyContinue))"

Say ""
Say "== MSI devices (VID 0DB0) in Device Manager"
Get-CimInstance Win32_PnPEntity | Where-Object { $_.DeviceID -match 'VID_0DB0' } | Sort-Object DeviceID | ForEach-Object {
    Say ("  {0,-8} {1} | {2}" -f $_.Status, $_.DeviceID, $_.Name)
}

Say ""
Say "== HID collections in the registry (also hidden / unplugged ones)"
$hidKey = 'HKLM:\SYSTEM\CurrentControlSet\Enum\HID'
Get-ChildItem $hidKey -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match 'VID_0DB0' } | ForEach-Object { Say "  $($_.PSChildName)" }

Say ""
Say "== Present MSI HID interfaces (* = looks like the LED/vendor interface: vendor page, 64-byte output)"
$candidates = @()
foreach ($p in [ClawProbe]::Paths()) {
    if ($p -notmatch 'vid_0db0') { continue }
    $d = [ClawProbe]::Describe($p)
    $vendor = $d -match 'page 0xFF[0-9A-F]{2}' -and $d -match 'out 64 '
    $mark = if ($vendor) { '*' } else { ' ' }
    $known = if ($p -match 'pid_1902&mi_00' -and $p -match 'col02') { '  <- what ClawAmbilight uses on the Claw 8 AI+' } else { '' }
    $id = ($p -replace '^\\\\\?\\hid#', '' -replace '#\{.*$', '')
    Say " $mark $id"
    Say "     $d$known"
    if ($vendor) { $candidates += $p }
}
if ($candidates.Count -eq 0) { Say "  no vendor interface with a 64-byte output report found (hidden by HidHide or another layout)" }

if ($Test) {
    Say ""
    Say "== Light test (red, RAM only). Close ClawAmbilight first, or it repaints the LEDs at once."
    Get-Process ClawAmbilight -ErrorAction SilentlyContinue | ForEach-Object { Say "  ClawAmbilight is running: closing it for the test"; Stop-Process -Id $_.Id -Force }
    foreach ($p in $candidates) {
        $id = ($p -replace '^\\\\\?\\hid#', '' -replace '#\{.*$', '')
        $err = [ClawProbe]::Light($p, 255, 0, 0, 100)
        if ($err -ne 0) { Say "  $id : write failed, error $err"; continue }
        $ans = Read-Host "  $id : did the sticks turn RED? (y/n)"
        Say "  $id : write OK, user says '$ans'"
        [void][ClawProbe]::Light($p, 0, 0, 0, 0)
    }
}

$lines | Set-Content $out -Encoding UTF8
Write-Host ""
Write-Host "Saved: $out  (send this file)"
