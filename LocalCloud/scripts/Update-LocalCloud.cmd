@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-LocalCloud.ps1" %*
set "LOCALCLOUD_UPDATE_EXIT=%ERRORLEVEL%"
cd /d "%TEMP%"
if not "%LOCALCLOUD_UPDATE_EXIT%"=="0" pause
exit /b %LOCALCLOUD_UPDATE_EXIT%
