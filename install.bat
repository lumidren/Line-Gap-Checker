@echo off
rem ---------------------------------------------------------------------------
rem  Installs LineGapChecker so AutoCAD loads it automatically on every start.
rem  Run build.bat (AutoCAD 2024 and older) and/or the dotnet build (2025+) first.
rem
rem  Usage:  install.bat               install for the current Windows user
rem          install.bat "D:\Plugins"  install into another ApplicationPlugins folder
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=%APPDATA%\Autodesk\ApplicationPlugins"
set "BUNDLE=%TARGET%\LineGapChecker.bundle"

set "NET48=bin\LineGapChecker.dll"
set "NET8=bin\x64\Release\net8.0-windows\LineGapChecker.dll"
if not exist "%NET48%" if not exist "%NET8%" goto :notbuilt

if not exist "%BUNDLE%\Contents" mkdir "%BUNDLE%\Contents"
copy /y "bundle\PackageContents.xml" "%BUNDLE%\PackageContents.xml" >nul || goto :copyfailed

if exist "%NET48%" (
    if not exist "%BUNDLE%\Contents\net48" mkdir "%BUNDLE%\Contents\net48"
    copy /y "%NET48%" "%BUNDLE%\Contents\net48\LineGapChecker.dll" >nul || goto :copyfailed
    echo Installed the AutoCAD 2019-2024 build.
)
if exist "%NET8%" (
    if not exist "%BUNDLE%\Contents\net8" mkdir "%BUNDLE%\Contents\net8"
    copy /y "%NET8%" "%BUNDLE%\Contents\net8\LineGapChecker.dll" >nul || goto :copyfailed
    echo Installed the AutoCAD 2025+ build.
)

echo.
echo Installed to: %BUNDLE%
echo Restart AutoCAD. If it shows a security prompt for LineGapChecker.dll, choose "Always Load".
exit /b 0

:notbuilt
echo No built DLL found. Build it first:
echo   AutoCAD 2024 and older:  build.bat "C:\Program Files\Autodesk\AutoCAD 2024"
echo   AutoCAD 2025 and newer:  dotnet build LineGapChecker.csproj -c Release -p:AcadDir="C:\Program Files\Autodesk\AutoCAD 2025"
exit /b 1

:copyfailed
echo Copy failed. Close AutoCAD (it locks the loaded DLL) and run install.bat again.
exit /b 1
