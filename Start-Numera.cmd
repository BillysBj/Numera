@echo off
REM Numera lokal starten (Doppelklick). Startet Docker-Infra + API + Worker + Web.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-numera.ps1"
if errorlevel 1 pause
