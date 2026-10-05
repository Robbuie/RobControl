# RobControl

![RobControl](assets/icon.png)

A Windows tool for backing up FANUC robots, keeping the history, and seeing what changed - and,
step by step, real read access to what is inside the controller.

It talks to live production robots. **Read [the safety rules](CLAUDE.md#safety---this-touches-live-production-robots)
before changing anything that sends a packet.**

## What it does today

| | |
|---|---|
| **Back up** | FTP copy of `md:` (optionally `fr:`) for one robot or the whole fleet, a few at a time. Plain dated folders you can open in Explorer, a `manifest.json` with the SHA-256 of every file, and the FTP transcript. Anything cut short is marked `_INCOMPLETE` - never a folder that looks like a good backup. |
| **Probe** | What the controller is - generation, software version, application, arm, F-number - and which ways in are open: FTP, diagnostic files, KCL. When something is locked it says where on the pendant to change it, and does not change it itself. |
| **Compare** | Any two backups of a robot: which files changed, were added or removed, and the line diff of the ASCII listings (`.LS`, `.VA`). |
| **Schedule** | Fleet backups every 4/8/12/24 hours while the app is open. |
| **Trend** | Registers, I/O and system variables, typed as on the pendant (`R[1-10], DI[1..8], $TIMER[1].$TIMER_VAL`). Read once, or record several robots at once and overlay them on one chart. Only changes are stored; CSV export. |
| **Many at once** | Ctrl/Shift-click robots in the list and Probe, Back up and Trends act on all of them. |
| **Event log** | Every probe and backup, append-only at the database. |
| **Diagnose network** | Opens [NetControl](https://github.com/Robbuie/NetControl) on a robot that does not answer. |

**Read-only by construction.** The FTP client can only log in, list and download. KCL goes through
a classifier that only lets `SHOW`, `DIRECTORY`, `TYPE` and `HELP` through. Nothing writes to,
moves, starts or deletes anything on a controller.

## Trying it without a robot

```powershell
dotnet run --project src/RobControl.RobotSim -- tests/Fixtures/synthetic-r30ibplus-v940-spottool --animate
# FTP 127.0.0.1:2121, HTTP 127.0.0.1:8080
```

Then in RobControl add a robot at `127.0.0.1` with FTP port `2121` and web port `8080`, and probe
it or back it up. With `--animate`, `R[1-4]`, `DI[1]`, `DO[1]`, `GI[1]` and `$TIMER[1].$TIMER_VAL`
move, so the Trends tab has something to draw. Start a second simulator on other ports to try
several robots at once. The synthetic profile is hand-written; see `tests/Fixtures/README.md`.

## First contact with a real robot

```powershell
dotnet run --project tools/RobControl.Probe -- 10.20.1.54
```

Read-only. Logs in (anonymous unless `--user` / `--password`), lists `md:`, copies a handful of
representative files, fetches the diagnostic pages and asks KCL two `SHOW` questions - then writes
everything it saw into a `capture-...` folder in the shape `robotsim` serves. That folder is how
RobControl learns what your controllers really say: read it through for anything site-confidential,
then commit it under `tests/Fixtures/`.

## Building

```powershell
dotnet build
dotnet test
dotnet run --project src/RobControl.App

pwsh tools/publish.ps1              # -> artifacts/RobControl-<version>/RobControl.exe
pwsh tools/publish.ps1 -Installer   # and the setup exe
```

Releases are a tag push - see [RELEASING.md](RELEASING.md).

## Layout

`src/RobControl.Core` is the engine and **must not reference any UI assembly**. `src/RobControl.App`
is the WPF front end, sharing NetControl's appearance system. `src/RobControl.RobotSim` is the fake
controller every test runs against. `tools/RobControl.Probe` is the capture tool. [CLAUDE.md](CLAUDE.md)
is the full picture.
