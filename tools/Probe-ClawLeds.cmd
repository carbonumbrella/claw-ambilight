@echo off
rem Read-only probe of the MSI Claw controller interfaces. Writes Desktop\claw-leds-probe.txt.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Probe-ClawLeds.ps1"
pause
