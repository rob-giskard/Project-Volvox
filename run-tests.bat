@echo off
rem Headless speed-mode test runs. Every simulation parameter is configurable.
rem
rem   run-tests.bat                                   20 runs, 12 sim hours each (defaults below)
rem   run-tests.bat --runs 100 --hours 24             more runs, longer
rem   run-tests.bat HydrationDrain=0.02 Thermotaxis=0 tweak any parameter (see --params)
rem   run-tests.bat --out results.csv                 save per-run results
rem   run-tests.bat --trace 7 --hours 2 > seed7.log   full event log of one seed
rem   run-tests.bat --params                          list all parameters and defaults
rem   run-tests.bat --help
rem
rem Parameters are read from volvox.config.json, then command-line overrides are applied.
setlocal
cd /d "%~dp0"

set RUNS=20
set HOURS=12

if exist dist\VolvoxRunner.exe (
  dist\VolvoxRunner.exe --runs %RUNS% --hours %HOURS% --config volvox.config.json %*
) else (
  where dotnet >nul 2>nul
  if errorlevel 1 (
    echo dist\VolvoxRunner.exe not found and no .NET SDK available. Run build.bat first.
    exit /b 1
  )
  dotnet run -c Release --project src\Volvox.Runner -- --runs %RUNS% --hours %HOURS% --config volvox.config.json %*
)
