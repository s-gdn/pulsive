@echo off
cd /d "%~dp0"
if exist "dist-clean\Pulsive.exe" (
    start "" "dist-clean\Pulsive.exe"
    exit /b
)
if exist "dist\Pulsive.exe" (
    start "" "dist\Pulsive.exe"
    exit /b
)
if exist "bin\Release\net8.0-windows10.0.19041.0\Pulsive.exe" (
    start "" "bin\Release\net8.0-windows10.0.19041.0\Pulsive.exe"
    exit /b
)
echo Build pulsive first using Build.ps1.
pause
