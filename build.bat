@echo off
rem ---------------------------------------------------------------------------
rem  Builds LineGapChecker.dll for AutoCAD 2024 and older (.NET Framework 4.8, x64)
rem  with the csc.exe that ships with Windows. No Visual Studio needed.
rem
rem  Usage:  build.bat ["C:\Program Files\Autodesk\AutoCAD 2024"]
rem  The AutoCAD folder can also be set in the ACAD_DIR environment variable.
rem
rem  AutoCAD 2025 and newer run on .NET 8 or .NET 10: use LineGapChecker.csproj instead
rem  (see README.md).
rem ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

set "ACAD=%~1"
if "%ACAD%"=="" set "ACAD=%ACAD_DIR%"
if "%ACAD%"=="" set "ACAD=C:\Program Files\Autodesk\AutoCAD 2024"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" goto :nocsc

rem 1. Geometry engine unit tests (no AutoCAD needed).
call "%~dp0test.bat"
if errorlevel 1 goto :testsfailed

rem 2. The plugin. accoremgd.dll is needed as well as acdbmgd/acmgd: since AutoCAD 2013
rem    Document, Editor and CommandMethod live there. csc never copies referenced DLLs
rem    into the output, which is the same as Copy Local = false.
if not exist "%ACAD%\acdbmgd.dll" goto :noacad
if not exist "%ACAD%\accoremgd.dll" goto :noacad
if not exist "%ACAD%\acmgd.dll" goto :noacad
if not exist bin mkdir bin

echo.
echo Building bin\LineGapChecker.dll against "%ACAD%"
"%CSC%" /nologo /target:library /platform:x64 /optimize+ /warn:4 ^
  /out:bin\LineGapChecker.dll ^
  /reference:"%ACAD%\acdbmgd.dll" ^
  /reference:"%ACAD%\accoremgd.dll" ^
  /reference:"%ACAD%\acmgd.dll" ^
  src\GapEngine.cs src\GapCommands.cs
if errorlevel 1 goto :buildfailed

echo.
echo Done: %~dp0bin\LineGapChecker.dll
echo In AutoCAD run NETLOAD and pick that file.
exit /b 0

:nocsc
echo csc.exe not found at "%CSC%". Install or repair .NET Framework 4.8.
exit /b 1

:testsfailed
echo Unit tests failed: plugin not built.
exit /b 1

:noacad
echo AutoCAD managed DLLs (acdbmgd.dll, accoremgd.dll, acmgd.dll) not found in:
echo   "%ACAD%"
echo Pass the AutoCAD install folder:  build.bat "C:\Program Files\Autodesk\AutoCAD 2024"
exit /b 1

:buildfailed
echo Plugin build failed.
exit /b 1
