@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0DownloadSpeechModel.ps1"
if errorlevel 1 (
  echo.
  echo Model download failed.
  pause
  exit /b 1
)
echo.
echo Model download completed.
pause
