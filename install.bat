@echo off
rem ---------------------------------------------------------------------------
rem  Installs LineGapChecker so AutoCAD loads it automatically on every start.
rem
rem  From the release zip: unzip it and double-click install.bat. Done.
rem  From source: build first (dotnet build -c Release, or build.bat), then run this.
rem
rem  Usage:  install.bat               install for the current Windows user
rem          install.bat "D:\Plugins"  install into another ApplicationPlugins folder
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=%APPDATA%\Autodesk\ApplicationPlugins"
set "BUNDLE=%TARGET%\LineGapChecker.bundle"

tasklist /fi "imagename eq acad.exe" 2>nul | find /i "acad.exe" >nul
if not errorlevel 1 goto :acadrunning

rem --- Release zip: a ready-made LineGapChecker.bundle sits next to this script.
if exist "LineGapChecker.bundle\PackageContents.xml" goto :fromrelease

rem --- Source checkout: collect whatever builds exist.
set "NET48=bin\x64\Release\net48\LineGapChecker.dll"
if not exist "%NET48%" set "NET48=bin\LineGapChecker.dll"
set "NET8=bin\x64\Release\net8.0-windows\LineGapChecker.dll"
set "NET10=bin\x64\Release\net10.0-windows\LineGapChecker.dll"
if not exist "%NET48%" if not exist "%NET8%" if not exist "%NET10%" goto :notbuilt

if not exist "%BUNDLE%\Contents" mkdir "%BUNDLE%\Contents"
copy /y "bundle\PackageContents.xml" "%BUNDLE%\PackageContents.xml" >nul || goto :copyfailed
call :copybuild "%NET48%" net48 "AutoCAD 2019-2024" || goto :copyfailed
call :copybuild "%NET8%" net8 "AutoCAD 2025-2026" || goto :copyfailed
call :copybuild "%NET10%" net10 "AutoCAD 2027+" || goto :copyfailed
goto :installed

:fromrelease
if exist "%BUNDLE%" rmdir /s /q "%BUNDLE%"
if exist "%BUNDLE%" goto :copyfailed
xcopy "LineGapChecker.bundle" "%BUNDLE%\" /e /i /q /y >nul || goto :copyfailed
echo Installed the builds for AutoCAD 2019-2024, 2025-2026 and 2027+.

:installed
rem Files from a downloaded zip carry a "blocked" mark and AutoCAD refuses to load them.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%BUNDLE%' -Recurse -File | Unblock-File" >nul 2>&1

echo.
echo Installed to: %BUNDLE%
echo Start AutoCAD and type GAPCHECK.
echo If AutoCAD asks whether to load LineGapChecker.dll, choose "Always Load".
exit /b 0

:copybuild
if not exist "%~1" exit /b 0
if not exist "%BUNDLE%\Contents\%~2" mkdir "%BUNDLE%\Contents\%~2"
copy /y "%~1" "%BUNDLE%\Contents\%~2\LineGapChecker.dll" >nul || exit /b 1
echo Installed the %~3 build.
exit /b 0

:acadrunning
echo AutoCAD is running. Close it first: it locks the plugin while open.
exit /b 1

:notbuilt
echo No built DLL found. Either download the ready-made zip from
echo   https://github.com/lumidren/Line-Gap-Checker/releases/latest
echo or build it first:
echo   dotnet build LineGapChecker.csproj -c Release
echo   build.bat "C:\Program Files\Autodesk\AutoCAD 2024"   (AutoCAD 2024 and older, no SDK needed)
exit /b 1

:copyfailed
echo Copy failed. Close AutoCAD (it locks the loaded DLL) and run install.bat again.
exit /b 1
