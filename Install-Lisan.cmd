@echo off
setlocal
cd /d "%~dp0"
title Lisan installer
echo.
echo Double-click installer — Python, model, build, Dalamud Dev Plugin.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Lisan.ps1" %*
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo Installer failed. See errors above.
) else (
  echo Installer finished.
)
pause
exit /b %ERR%
