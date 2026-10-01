@echo off
setlocal
cd /d "%~dp0"
echo Cleaning stale Visual Studio / WinUI generated build artifacts...
for %%D in ("WarThunderChatTranslator\bin" "WarThunderChatTranslator\obj" "WarThunderChatTranslator.Tests\bin" "WarThunderChatTranslator.Tests\obj" ".vs") do (
  if exist %%D (
    echo Removing %%D
    rmdir /s /q %%D
  )
)
echo Done. Reopen the solution and use Build ^> Rebuild Solution.
pause
