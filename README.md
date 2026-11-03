# Project-Volvox

Observational artificial-life simulator. One organism that knows nothing, four needs, a graph-paper world with hills, marsh and thicket in the way, and a log of how it figures things out — or doesn't.

Design, developer review and MVP spec: [docs/DESIGN.md](docs/DESIGN.md)

## Build

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```
build.bat
```

Produces in `dist\`:

| File | What |
|---|---|
| `Volvox.exe` | the game (single self-contained exe) |
| `VolvoxRunner.exe` | headless speed-mode test runner |
| `volvox.config.json` | every tuning parameter, read by both |

## Play

`dist\Volvox.exe` (optionally `--seed 42`).

- **Speed:** PAUSE / 1x / 10x / 100x — keys `space`, `1`, `2`, `3`
- **Map:** wheel zoom, drag pan, double-click fit. `B` beliefs (fog over unseen ground + where it remembers things), `E` remembered effort, `T` trail
- **Log:** search, filter by category, sort by time / lifetime / event / interaction / category, click a row to locate it on the map
- **NEW / RESTART** a seed; **AUTO-NEW** starts the next seed 3 s after a death

## Test

```
run-tests.bat                                   20 runs × 12 sim hours
run-tests.bat --runs 100 --hours 24
run-tests.bat HydrationDrain=0.02 Thermotaxis=0 tweak any parameter
run-tests.bat --out results.csv                 per-run CSV
run-tests.bat --trace 7 --hours 2 > seed7.log   full event log of one seed
run-tests.bat --params                          list every parameter
```

## Layout

```
src/Volvox.Sim      simulation engine, no UI (world, organism, mind, log)
src/Volvox.App      WinForms desktop app
src/Volvox.Runner   headless console runner
```
