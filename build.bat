@echo off
REM ------------------------------------------------------------------
REM  KaraokeForge - build helpers for Windows
REM    build.bat        -> build the app (Debug)
REM    build.bat publish -> single-file .exe for any Windows 10/11 PC
REM    build.bat run    -> build and start the app
REM ------------------------------------------------------------------
setlocal
cd /d "%~dp0"

where ffmpeg >nul 2>nul || (echo ERROR: FFmpeg is not on PATH. Install it with: winget install Gyan.FFmpeg -e & pause & exit /b 1)

if "%1"=="publish" goto publish
if "%1"=="run" goto run

echo Building KaraokeForge...
dotnet build -c Release -v minimal || (pause & exit /b 1)
echo.
echo Done. Start it with: dotnet run   (or run bin\Release\net8.0-windows\KaraokeForge.exe)
pause
exit /b 0

:publish
echo Publishing a single-file .exe...
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -v minimal || (pause & exit /b 1)
echo.
echo Your exe: bin\Release\net8.0-windows\win-x64\publish\KaraokeForge.exe
pause
exit /b 0

:run
dotnet run
exit /b 0
