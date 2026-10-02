@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

rem Extracts only the vehicle/car definition files from ATS game archives
rem (def.scs and dlc_*.scs) plus any mods dragged onto this file, and zips
rem them into RoadTrip-Defs.zip, which is small enough to upload.

set "ATS=C:\Program Files (x86)\Steam\steamapps\common\American Truck Simulator"
if not exist "%ATS%\def.scs" (
    echo Could not find def.scs in:
    echo     !ATS!
    set /p "ATS=Paste your American Truck Simulator install folder: "
)
set "ATS=!ATS:"=!"
if not exist "!ATS!\def.scs" (
    echo def.scs was not found in "!ATS!".
    pause
    exit /b 1
)

set "OUT=%~dp0RoadTrip-Defs"
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

echo Building extractor...
dotnet build "%~dp0Cli\ATSRoadTripConverter.Cli.csproj" -c Release -v quiet -nologo
if errorlevel 1 (
    echo Build failed. Is the .NET 8 SDK installed?
    pause
    exit /b 1
)
set "TOOL=dotnet "%~dp0Cli\bin\Release\net8.0\ats-roadtrip-convert.dll""

for %%F in ("!ATS!\def.scs" "!ATS!\dlc_*.scs") do (
    if exist "%%~F" (
        echo Extracting definitions from %%~nxF ...
        %TOOL% --extract "%%~F" --output "%OUT%\game\%%~nF" --defs-only > "%OUT%\game_%%~nF.log" 2>&1
    )
)

for %%M in (%*) do (
    echo Extracting definitions from mod %%~nxM ...
    %TOOL% --extract "%%~M" --output "%OUT%\mods\%%~nM" --defs-only > "%OUT%\mod_%%~nM.log" 2>&1
)

if exist "%~dp0RoadTrip-Defs.zip" del "%~dp0RoadTrip-Defs.zip"
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%~dp0RoadTrip-Defs.zip' -CompressionLevel Optimal"

echo.
echo Done. Upload this file:
echo     %~dp0RoadTrip-Defs.zip
pause
