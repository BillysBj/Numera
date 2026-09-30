@echo off
REM Numera lokal stoppen (Doppelklick).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\stop-numera.ps1"
timeout /t 3 >nul
