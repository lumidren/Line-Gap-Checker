@echo off
rem Builds and runs the geometry-engine unit tests. Needs no AutoCAD.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" goto :nocsc
if not exist bin mkdir bin

"%CSC%" /nologo /target:exe /platform:x64 /optimize+ /warn:4 /out:bin\GapEngineTests.exe src\GapEngine.cs tests\GapEngineTests.cs
if errorlevel 1 exit /b 1

bin\GapEngineTests.exe
exit /b %errorlevel%

:nocsc
echo csc.exe not found at %CSC%
echo Install or repair .NET Framework 4.8.
exit /b 1
