@echo off
rem Double-cliquez ce fichier pour installer Mixdeck sur ce PC (aucun droit administrateur requis).
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\install.ps1" %*
set CODE=%ERRORLEVEL%
echo.
pause
exit /b %CODE%
