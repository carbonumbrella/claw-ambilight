# CHANGELOG

## Unreleased - taskbar cut by the work area, whole screen in fullscreen

- `ScreenArea`: while the foreground window covers the primary monitor (games, fullscreen video, F11) the
  whole screen feeds the LEDs; otherwise only the work area, so the taskbar is cut on whatever side it
  docks (auto-hide = nothing cut). Re-read every 0.5 s, changes are logged ("screen area: ...").

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
