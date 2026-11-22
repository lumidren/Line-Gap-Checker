@echo off
rem Removes the LineGapChecker auto-load bundle. Close AutoCAD first.
rem Usage:  uninstall.bat  [ApplicationPlugins folder, default: current user's]
setlocal
set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=%APPDATA%\Autodesk\ApplicationPlugins"
set "BUNDLE=%TARGET%\LineGapChecker.bundle"

if not exist "%BUNDLE%" (
    echo LineGapChecker is not installed in "%TARGET%".
    exit /b 0
)
rmdir /s /q "%BUNDLE%"
if exist "%BUNDLE%" (
    echo Could not remove "%BUNDLE%". Close AutoCAD and try again.
    exit /b 1
)
echo LineGapChecker removed. Restart AutoCAD.
exit /b 0
