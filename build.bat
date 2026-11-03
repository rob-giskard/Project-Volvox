@echo off
rem Builds Volvox.exe (the game) and VolvoxRunner.exe (headless test runner) into .\dist
rem Requires the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [build] .NET SDK not found. Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
  goto :fail
)

echo [build] Publishing game...
dotnet publish src\Volvox.App\Volvox.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist
if errorlevel 1 goto :fail

echo [build] Publishing test runner...
dotnet publish src\Volvox.Runner\Volvox.Runner.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:DebugType=none -o dist
if errorlevel 1 goto :fail

copy /y volvox.config.json dist\volvox.config.json >nul

echo.
echo [build] Done:
echo    dist\Volvox.exe          the game
echo    dist\VolvoxRunner.exe    headless speed-mode tests (or use run-tests.bat)
echo    dist\volvox.config.json  tuning parameters, read by both
if /i not "%~1"=="nopause" pause
exit /b 0

:fail
echo.
echo [build] FAILED
if /i not "%~1"=="nopause" pause
exit /b 1
