# CHANGELOG

## 0.9.5 (2026-10-30) - Claw A8 support

- The LEDs are also found on the controller's Xbox 360 (XInput) interface, VID 0DB0 PID 1901 MI_01 (vendor
  page 0xFFA0, 64-byte reports). That is how the Claw A8 shows up; the same command lights its sticks
  (tested with tools\Probe-ClawLeds.ps1 on an A8). PID 1902 MI_00 COL02 is still tried first.
- The device is looked up again on every reopen, and every registry instance is tried until one opens, so a
  controller mode switch (1901 <-> 1902) or a stale unplugged instance no longer leaves the LEDs dark.
- tools\Probe-ClawLeds: finds the LED interface on untested models (read-only, optional red light test).

## 0.9.4 (2026-10-29) - autostart works in Xbox mode

- Autostart is now a per-user logon task "ClawAmbilight" instead of the Run key: Windows' Xbox full screen
  experience starts Run-key apps only on the first exit to the desktop, so the LEDs stayed dark after booting
  into Xbox mode. Registering a logon task for oneself needs no admin rights. An old Run entry is migrated on
  start; the Run key stays as a fallback if the task cannot be created.
- `--set-autostart on|off` for the installer; a second launch with `--tray` no longer opens the window.

## 0.9.3 (2026-10-29) - settings window no longer eats CPU and RAM

- The open settings window cost ~23% of a core even with the lights off: the preview repainted 10x/s
  (new brushes every tick) and each LED had a blurred DropShadowEffect, which this AllowsTransparency
  window renders in software for the whole window. Now: repaint only changed LEDs, radial-gradient glow
  instead of the blur, status text set only on change. Window open: 26% -> 6.8% with the lights on
  (3% is the capture itself), 0.2% with the lights off.
- Closing the window destroys it (recreated on show) and trims the working set: RAM after closing
  ~300 MB -> ~50 MB. Display on/off notifications moved to a hidden HwndSource owned by App.

## 0.9.2 (2026-10-27) - taskbar cut, whole screen in fullscreen, edge weighting

- `ScreenArea`: while the foreground window covers the primary monitor (games, fullscreen video, F11) the
  whole screen feeds the LEDs; otherwise only the work area, so the taskbar is cut on whatever side it
  docks (auto-hide = nothing cut). Re-read every 0.5 s, changes are logged ("screen area: ...").
- Edge weighting like a TV ambilight: rows near the outer screen edge (top for the upper LEDs, bottom for
  the lower ones) count up to 4x more than rows at the middle line, in both screen modes.

## 0.9.1 (2026-10-27) - "Яркий" mode reworked, whole screen captured

- Old vibrant (ported from the Python version) weighted pixels by chroma^2.5: one small HUD icon recolored a
  whole quadrant, colors kept the screen's dim brightness, gray scenes flipped to the mean at a hard
  threshold, and the saturation slider was ignored.
- New: dominant hue (24 chroma-weighted hue bins, best bin + neighbors), lifted to near full LED brightness
  (0.45..1 by the zone's brightness), saturation slider applied, smooth blend toward neutral when under 25%
  of the pixels are colored.
- Zones cover the whole screen height in every screen mode (the bottom ~30% used to be skipped, so content
  at the bottom did not light the LEDs).

## 0.9.0 (2026-10-27) - first public release (beta)

- Published as 0.9.0 pre-release, not 1.x: so far tested on one Claw 8 AI+ (controller firmware
  2.30+) only; portrait rotation and the runtime download path of the installer are untested.
- Screen-reactive backlight for the MSI Claw stick rings and ABXY. Replaces a Python prototype
  that used 31% of a core (GDI full-screen copy per frame): DXGI Desktop Duplication with the
  downscale done on the GPU (GenerateMips, ~60x37 readback), about 1.5% of one core, idle on a
  static screen, works in fullscreen games.
- Modes: Мягкий (chroma-weighted ambient + saturation boost), Яркий (most saturated accent),
  Один цвет with separate left / right+ABXY colors, a full HSV color picker and a Joy-Con preset
  (#0AB9E6 / #FF3C28).
- Brightness follows the display backlight (slider = cap) or is manual. On-battery policy:
  normal / eco (10 FPS) / off. LEDs go dark on lock, display off and exit.
- WPF settings window (theme follows Windows, two columns so it fits 175% scaling on 1920x1200),
  tray icon, autostart, settings.json watched live for scripts and voice control.
- Inno Setup per-user installer; framework-dependent exe (1.8 MB) with an automatic .NET 10
  Desktop Runtime download only when it is missing (self-contained would be 70 MB).
- Do not set InvariantGlobalization: WPF TextBox measuring throws CultureNotFoundException.
