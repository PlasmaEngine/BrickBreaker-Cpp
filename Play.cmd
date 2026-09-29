@echo off
powershell.exe -NoProfile -File "%~dp0Build.ps1" -Run
if errorlevel 1 pause
