@echo off
setlocal
cd /d "%~dp0"
echo Starting ATS American Roadtrip Car Patcher with Hot Reload...
dotnet watch run --project "%~dp0ATSRoadTripConverter.csproj" -c Debug
if errorlevel 1 (
    echo.
    echo Watch session ended with an error.
    pause
)