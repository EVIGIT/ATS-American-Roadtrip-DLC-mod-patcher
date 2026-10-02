@echo off
setlocal
cd /d "%~dp0"
echo Building ATS American Roadtrip Car Patcher...
dotnet build "%~dp0ATSRoadTripConverter.csproj" -c Debug
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)
echo.
echo Starting converter...
dotnet run --project "%~dp0ATSRoadTripConverter.csproj" --no-build -c Debug
if errorlevel 1 (
    echo.
    echo Converter exited with an error.
    pause
)
