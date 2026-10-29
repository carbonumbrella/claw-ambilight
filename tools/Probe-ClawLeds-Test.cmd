@echo off
rem Probe + light test: each candidate interface is lit red (controller RAM only) and you answer y/n.
rem ClawAmbilight is closed for the test; start it again afterwards.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Probe-ClawLeds.ps1" -Test
pause
