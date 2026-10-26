@echo off
rem Builds publish\fd\ClawAmbilight.exe and publish\installer\ClawAmbilight-Setup-<version>.exe.
rem Needs the .NET 10 SDK (dotnet on PATH, or set DOTNET=path\to\dotnet.exe) and Inno Setup 6
rem (found in its default per-user or machine location, or set ISCC=path\to\ISCC.exe).
setlocal
cd /d "%~dp0"

if not defined DOTNET set "DOTNET=dotnet"
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"

set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
"%DOTNET%" publish src -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish\fd || exit /b 1

if not defined ISCC (
  echo Inno Setup 6 not found: only publish\fd\ClawAmbilight.exe was built.
  exit /b 0
)
"%ISCC%" /Q installer\ClawAmbilight.iss || exit /b 1
echo Done: publish\installer
