@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Collect-DcsDebugBundle.ps1"
set "HZPL_COLLECTOR_EXIT=%ERRORLEVEL%"
echo.
if not "%HZPL_COLLECTOR_EXIT%"=="0" echo The collector did not finish successfully.
pause
exit /b %HZPL_COLLECTOR_EXIT%

