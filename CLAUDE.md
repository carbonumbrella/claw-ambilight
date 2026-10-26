# Project: ClawAmbilight

Screen-reactive controller backlight for the MSI Claw (C# .NET 10 WPF). Public repo:
https://github.com/carbonumbrella/claw-ambilight. Records: `CHANGELOG.md` (decisions), README for users.

## Rules

- Build with `build.cmd` (on this machine set `DOTNET=C:\AI\dotnet\dotnet.exe`, the per-user SDK).
  Bump `<Version>` in `src/ClawAmbilight.csproj` for every release; the installer takes its version
  from the exe.
- Check UI changes without popping windows: `publish\fd\ClawAmbilight.exe --render <png> [ambient|static|picker]`
  draws the settings window (or the color picker popup) offscreen. Keep `docs/*.png` current.
- The window must fit 175% scaling on 1920x1200 (work area ~1097x638 DIP).
- Release: commit, tag `vX.Y.Z`, `gh release create` with `publish\installer\ClawAmbilight-Setup-*.exe`.
  Push only when the user asks.
- The installed copy lives in `%LOCALAPPDATA%\Programs\ClawAmbilight`, settings in
  `%APPDATA%\ClawAmbilight\settings.json` (the Gemma voice agent in `C:\AI\openvino-npu\gemma_agent.py`
  edits this file). Stop it with `--quit`, never by killing (LEDs would stay lit).
