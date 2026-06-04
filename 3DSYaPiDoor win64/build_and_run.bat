@echo off
title 3DS YaPiDoor

echo.
echo  =============================================
echo     3DS -^> ViGEmBus Virtual PS$ controller 
echo  =============================================
echo.

cd /d "%~dp0"
echo  Working dir: %cd%
echo.

echo  [..] Checking .NET SDK...
dotnet --version
if %errorlevel% neq 0 (
    echo.
    echo  [ERR] dotnet not found!
    echo  Download .NET SDK from:
    echo  https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)
echo  [OK] .NET SDK found.
echo.

if not exist "%~dp0build" mkdir "%~dp0build"

echo  [..] Building...
cd /d "%~dp0\3DSYaPiDoor"

dotnet publish -c Release -r win-x64 --no-self-contained -o "%~dp0build"

if %errorlevel% neq 0 (
    echo.
    echo  [ERR] Build failed (see output above)
    echo.
    pause
    exit /b 1
)

echo.
echo  [OK] Done! Output: %~dp0build
echo.

cd /d "%~dp0"

if not exist "%~dp0build\ViGEmClient.dll" (
    echo  [!] WARNING: ViGEmClient.dll not found in build\
    echo.
)

pause
