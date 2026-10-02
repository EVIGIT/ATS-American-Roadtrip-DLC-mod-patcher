@echo off
setlocal
cd /d "%~dp0"
echo Building ATS American Roadtrip Car Patcher...
dotnet build "%~dp0ATSRoadTripConverter.csproj" -c Release
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)
echo.
echo Build succeeded.
pause
