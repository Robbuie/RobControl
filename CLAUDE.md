# CLAUDE.md

Guidance for working in this repository.

## What this is

**RobControl** - a Windows desktop tool for controls engineers who look after FANUC robots. It
backs robots up, keeps the history, says what changed, and gives real read access (and, later,
carefully gated write access) to what is inside the controller: system variables, registers, I/O,
alarms, program state - and trends them over time.

It talks to live robots on production lines. Read the Safety section before writing anything that
sends a packet to a controller.

It is a sibling of **NetControl** (`~/Projects/BootP.DHCP`, repo `Robbuie/NetControl`), and is
built the same way on purpose - same stack, same layering rule, same appearance system, same
release pipeline. When in doubt about a convention, NetControl's CLAUDE.md is the reference and
this file says where RobControl differs.

## The fleet it is for

- Mixed generations: **R-30iA, R-30iB, R-30iB Plus** (roughly V7.x, V8.x, V9.x software).
- Most of the current work is **V9.40 SpotTool+**. That is the first target; other versions and
  applications (PaintTool, HandlingTool) must work but are allowed to light up fewer features.
- **Most robots have the KAREL option. Few are known to have HMI Device (SNPX) or RMI.**
- **No Roboguide, no ktrans.** We cannot compile `.kl` to `.pc`, so RobControl does not depend on
  any KAREL program of its own. It may *call* KAREL programs already loaded on a robot. Writing
  our own helper KAREL is a later, optional phase if Roboguide ever appears.

## How RobControl gets into a controller

Every way in is a **transport** in `RobControl.Core/Transports/`, behind an interface, so the
features above them do not care which one answered and the tests can fake any of them.

| Transport | Used for | Needs on the robot | Phase |
|---|---|---|---|
| **FTP** (TCP/21) | Backups. Reading from `MD:` makes the controller generate fresh ASCII listings (`.LS`, `.VA`, `.DG`) alongside the binary files | Ethernet + FTP server (standard) | 1 |
| **HTTP - diagnostic files** (`/MD/...`) | Live state without a session: summary, alarm log, I/O state, current position, program state | Web server (standard) | 1 |
| **HTTP - KCL** (`/KCL/<command>`) | `SHOW VAR`, `SHOW PROGRAM` and friends - the "real access" | KCL resource unlocked under Setup > Host Comm > HTTP | 3 |
| **HTTP - KAREL** (`/KAREL/<prog>?...`) | Calling KAREL programs that are already on the robot | KAREL option, resource unlocked | later |
| **SNPX / HMI Device** (TCP/60008) | Fast register/I/O/alarm polling for trends | HMI Device option | later, optional |
| **RMI** (TCP/16001, R-30iB Plus) | JSON register/I/O access | RMI option | later, optional |

**Everything about URL names, file names and response formats in this table is from the field and
from FANUC documentation, not yet watched against our own controllers.** Phase 0 exists to confirm
it. When a detail is confirmed, move it into *Controller gotchas* with the version it was seen on.

### Capabilities are probed, never assumed

On first contact RobControl reads what the controller is - generation, software version,
application (SpotTool / PaintTool / HandlingTool), and which options and HTTP resources are
available - and stores it per robot. Features check that record and say *why* they are greyed
out ("KCL is locked on this controller: Setup > Host Comm > HTTP > KCL"). **RobControl never
changes a controller's security or Host Comm settings itself.** It tells the person what to change.

## An FTP backup is not an image backup

Worth being clear about, because people use the word "backup" for both:

- **FTP file backup** (what RobControl does): the files from `MD:` (and optionally `FR:`) while
  the controller runs normally. Programs, system variable files, I/O config, ASCII listings for
  diffing. Restorable file-by-file at a controlled start. Does **not** restore a dead controller
  by itself.
- **Image backup**: taken from the boot monitor over TFTP (or to a memory card/USB). Restores the
  whole controller. That is the path NetControl's TFTP backup tab watches and diagnoses.

RobControl shows both side by side per robot: last good file backup (its own) and, where it can be
read from the plant's TFTP folder, last image backup - and hands off to NetControl when an image
backup has gone stale or failed.

## Relationship to NetControl

Two apps, handing off to each other. No shared project references across repos yet.

- RobControl's **Diagnose network** (on any robot that fails to answer, or a stale image backup)
  launches NetControl pointed at that robot's address or at the TFTP server.
- NetControl gets a matching **Open in RobControl**.
- The handoff is a command line: `NetControl.exe --diagnose <ip>` and
  `RobControl.exe --robot <ip>`. Each app finds the other through its per-user install location
  (`%LOCALAPPDATA%\Programs\<App>`); if it is not installed, say so and link the release page.
- If real code ends up duplicated (the updater, the appearance system, `UnicastTarget`), the
  answer is a shared library later - not a cross-repo project reference now.

## Stack

Same as NetControl, deliberately:

- **.NET 10 (LTS), C# latest, `net10.0`**, Windows only. `RobControl.App` targets
  `net10.0-windows` because WPF requires it; everything else - including `RobControl.Tests`, which
  has no view-model tests yet - stays on plain `net10.0`. When view-model tests arrive the test
  project moves to `net10.0-windows`, as NetControl's did.
- **WPF + MVVM via CommunityToolkit.Mvvm - without its source generators.** View models are written
  longhand (`SetProperty`, explicit `RelayCommand`s). Slightly more typing, and in exchange they
  compile and can be checked anywhere the SDK is, not only where the generator runs.
- **SQLite via Microsoft.Data.Sqlite** (not `.Core`) for the robot list, last probe per robot, and
  the append-only event log - and later trend data. **Not for backups**: see the archive section.
  `SQLitePCLRaw.bundle_e_sqlite3` is pinned the same way NetControl pins it.
- **xUnit** for tests.
- **Inno Setup** installer, per-user, no admin. Self-update from GitHub releases with checksum,
  same as NetControl - copy its `Diagnostics/` updater rather than reinventing it.
- **FTP: our own small client in `Core/Transports/Ftp/`** over `TcpClient` - passive mode, `TYPE I`,
  `LIST`/`NLST`, `RETR`, nothing else. A FANUC FTP server needs a small subset, we want to control
  exactly what is sent, and it keeps the dependency list empty. `FtpWebRequest` is obsolete and
  hides the replies we need for diagnosis. If the subset proves inadequate, FluentFTP (MIT) is the
  fallback and needs justifying here first.
- **HTTP: `HttpClient`.** No dependency.

Keep dependencies few. Every package added is a package someone has to justify to plant IT.

## Layout

```
RobControl.sln
Directory.Build.props / .targets   copied from NetControl, Product = RobControl
.github/workflows/                 verify.yml on push, release.yml on a v* tag
installer/RobControl.iss           Inno Setup, per-user. Its AppId GUID must never change.
src/RobControl.Core/               engine - MUST NOT reference any UI assembly
    Net/                           UnicastTarget (copied from NetControl), Ipv4Subnet
    Robots/                        Robot - what the person typed
    Transports/Ftp/                FtpClient (read verbs only), reply reader, PASV, transcript
    Transports/Http/               ControllerWebClient - GET /MD/ files, KCL through the classifier
    Kcl/                           KclClassifier (read / write / never), page text extraction
    Controllers/                   ControllerIdentity + parser, ShowVarParser, CapabilityProbe
    Backup/                        BackupRunner, FleetBackup, archive layout, manifest, name safety
    History/                       LineDiff (Myers), BackupComparer, TextSniffer
    Events/                        IEventSink - how Core writes the record without knowing SQLite
    Persistence/                   FleetStore: robots, last probe, append-only Event table
    Diagnostics/                   TraceLog (copied from NetControl)
src/RobControl.App/                WPF front end - net10.0-windows
    Appearance/                    copied value for value from NetControl; default accent amber
    Composition/                   AppHost, AppPaths, dispatcher, UserSettings, NetControlHandoff
    Diagnostics/                   build stamp and the updater, copied from NetControl
    ViewModels/                    all UI logic, free of WPF types
    Views/                         MainWindow, RobotWindow, AppearanceWindow, UpdateWindow
src/RobControl.RobotSim/           fake controller (FTP + HTTP) serving a profile folder; records
                                   every command and refuses - and lists - anything not a read
tools/RobControl.Probe/            Phase 0 capture tool: one real robot, read-only, into a profile
tools/icon.py                      redraws the icon (committed output)
tests/RobControl.Tests/            xUnit, against robotsim on loopback
tests/Fixtures/                    controller profiles - synthetic now, real captures to come
```

`RobControl.RobotSim` is not polish. It is what lets the whole thing be written and regression-tested
at a desk instead of against a running line. **Every response captured from a real controller in
Phase 0 goes into `tests/Fixtures/<generation>-<version>-<app>/`**, and the parsers are tested
against those, not against what we think a controller says.

## Backups - the archive

- Plain files on disk, readable without the app:
  `<archive root>/<robot name>/<yyyy-MM-dd_HHmmss>/<device>/<file>`.
  An engineer with Explorer and no RobControl must still be able to find and use a backup.
- **Each backup folder carries its own `manifest.json`** - robot, start/end, outcome, every file
  with size and SHA-256, failures and skipped names, the controller identity at the time, and the
  build that took it - plus `ftp-transcript.txt`. The app's history is built by reading manifests,
  not a database, so a folder copied to another laptop or a share brings its history with it (and
  SQLite on a network share is a known way to corrupt a database).
- **A backup is complete or it is marked incomplete.** Files land in `<stamp>.partial`; only once the
  manifest is written is it renamed - to `<stamp>` if every listed file arrived, `<stamp>_INCOMPLETE`
  otherwise. A crash leaves a `.partial` folder the history shows as unreadable.
- **A name from the controller is untrusted input headed for the disk.** `ArchiveNames.IsSafeFileName`
  either accepts a listed name as-is or the file is skipped and recorded - never "cleaned up" into a
  name that does not match the robot.
- Diffs are computed on text files, decided by content (`TextSniffer`) with the extension as a hint.
  Binary files are compared by hash only. `.DG` files are live snapshots and are flagged so the
  comparison can hide them.
- No git dependency: git is not guaranteed on a plant laptop. Retention (pruning old backups) is a
  later setting; nothing is ever deleted automatically today.

## Safety - this touches live production robots

These are hard rules, not preferences. Changing one is a change to this section first, with the
reason written down, not a feature request.

- **Read-only is the default, and the whole of Phases 0-4.** No transport has a write method until
  the write phase, and when it does, it lives behind the write gate below.
- **Never anything that can move the robot or actuate tooling.** No motion commands, no program
  start/resume, no UOP or SOP signals, and **no output writes at all** - on a spot robot a DO can
  close a gun or a clamp. This is not covered by the write gate; it is simply not built.
- **KCL commands are classified before they are sent** (`Core/Kcl/`): *read* (SHOW ...) is allowed;
  *write* (SET VAR, SET REG and the like) goes through the write gate; *never* (RUN, CONTINUE,
  ABORT, PAUSE, HOLD, RESET, DELETE, CLEAR, LOAD, SET PORT, anything touching DCS / `$DCSS_*`,
  controlled/cold start, format) is refused with nothing sent. **Anything not recognised is
  *never*.** The free-text KCL console goes through the same classifier.
- **The write gate (later phase):** armed per robot, per session, by the person, after a
  confirmation naming the robot and address. Every write reads the old value first, writes, reads
  back, and logs old/new/readback. A readback that disagrees is reported as a failure, not a success.
  Safety and DCS variables are on a deny-list the gate cannot unlock.
- **Never put, rename or delete a file on a controller.** Backup is `RETR` only. Restore is a
  separate, later, explicit flow - never a side effect of anything else.
- **One connection per robot at a time, gentle by default.** Controller FTP and web servers are
  small. Backups across the fleet run with a low concurrency limit; trend polling has a minimum
  interval per robot. Faster is opt-in and labelled.
- **Every probe goes to one address the person named or listed.** No broadcast, multicast or subnet
  sweep from RobControl - discovery is NetControl's job. Check with a `UnicastTarget` equivalent
  before the first packet.
- **Never change a controller's settings to make RobControl work.** Locked KCL, a password on FTP,
  a disabled resource - report it and say where to change it.
- **Every operation that transmits gets an event row** - backups, KCL reads, file fetches, polls
  (polls summarised per session, not per sample): who, which robot, what was asked, what came back.

## Controller gotchas

Confirmed facts only, each with the controller generation and software version it was seen on.
Empty until Phase 0 has been in front of a robot.

### Assumed, not yet confirmed - Phase 0 checks each of these

The code is written to tolerate being wrong about every one of them, and the probe tool records
exactly what a real controller does instead.

- FTP accepts `anonymous` with an empty password unless Host Comm has FTP users set up.
  (`FtpCredentials.Default`; the user remembers the manual giving a default login - confirm which.)
- `CWD md:` selects the device; `NLST` lists bare names (the sim can also prefix them with `md:` -
  `SimProfile.NlstWithDevicePrefix` - and `ArchiveNames.StripDevice` copes either way).
- Passive mode works. Active mode is not built; `PassiveEndpoint` says so if PASV is refused.
- Diagnostic files are at `/MD/<name>`; `SUMMARY.DG` names the cabinet, version, application,
  arm and F-number somewhere in it. `ControllerIdentityParser` searches rather than parses.
- KCL is at `/KCL/<command>` with spaces as `%20` and `$`, `[`, `]` literal; the output is inside
  `<XMP>` (or `<PRE>`); a locked resource answers 401 or 403. KCL errors arrive inside a 200.
- `SHOW VAR` puts the value after the last `=` on the line.

## C# specifics

NetControl's CLAUDE.md, *C# specifics that have already bitten us*, applies here in full. The ones
that will bite first in this repo:

- `UseWPF` drops `System.IO` from implicit usings; WPF fails silently under `InvariantGlobalization`
  (App project must override it back to false).
- `Dispatcher.BeginInvoke`, never `Invoke`, from a Core event handler.
- Build SQLite commands with `connection.CreateCommand()`. Timestamps as round-trip ("O") UTC text.
- `Directory.Build.props` is imported before the project body - conditions on project flags go in
  `.targets`. No `--` inside XML comments.
- `Span<T>` locals are illegal in `async` methods and iterators.

## Conventions

- Nullable on, warnings as errors in `src/`.
- `RobControl.Core` throws typed exceptions; the UI catches and presents.
- Error messages name the likely cause and the next action. "FTP failed" is useless;
  "R2-14 (10.20.1.54) refused the FTP login - the controller may require a password: Setup > Host
  Comm > FTP" is the product.
- Comments explain *why*, especially controller quirks.
- One class per file in `src/`.
- Text encodings: controller ASCII files are not guaranteed UTF-8. Read as bytes, decode explicitly.

## Licensing

All rights reserved, same licence text as NetControl. Built from FANUC's published manuals and
observed controller behaviour.

- Do not decompile or reverse-engineer FANUC software (Roboguide, PCDK, iPendant, WinOLPC).
- Do not ship any FANUC binary or KAREL program with RobControl.

## Roadmap

**Phase 0 - spikes against one real robot (read-only).** A V9.40 SpotTool robot first, then one of
each other generation. Console spikes, no app:
1. FTP: log in, `LIST` of `MD:`, `RETR` one `.LS` and one `.VA`. Record exact replies.
2. HTTP: fetch the summary/version diagnostic file and the alarm log. Record exact responses.
3. KCL over HTTP: one `SHOW VAR` - and what the controller says when KCL is locked.
Every response saved as a fixture. Findings go into *Controller gotchas*.

**Phase 1 - backup.** Robot list, capability probe, on-demand backup of one robot, then the fleet
with a concurrency limit, scheduled backups while the app is running (tray), archive + index,
status grid of last good backup per robot.

**Phase 2 - history.** Diff any two backups, golden reference per robot, drift report. Spot: weld
schedules and gun data diffed as first-class items.

**Phase 3 - inspect.** KCL console (classified), system variable / register browser (read),
alarm history, live I/O view (read), diagnostics bundle for FANUC support.

**Phase 4 - trend.** Chosen variables and registers polled into SQLite, graphs, thresholds,
maintenance watches (battery, cycle time, alarm rates, weld counts / tip dress).

**Phase 5 - write gate.** As defined in Safety. Only after Phases 1-4 have run on real robots.

**Later / optional.** SNPX and RMI transports where the options exist; our own KAREL helpers if
Roboguide appears; restore assistant.

## Current state

**0.1.0 - written and tested against the simulator; never yet in front of a real controller.**

- Core, RobotSim and the probe tool build clean under warnings-as-errors, and the suite - 133 cases
  plus the SQLite ones - passes against `robotsim` on loopback. It was written in a session with no
  NuGet access, so `FleetStore` was compiled against a stand-in for Microsoft.Data.Sqlite (its
  schema and triggers were checked with Python's sqlite3), and xUnit was a stand-in runner. **The
  first CI run is the real check for those two.**
- The WPF app (`RobControl.App`) has **never been compiled**: no WPF targeting pack was available.
  Its view models were compile-checked against a stand-in for CommunityToolkit.Mvvm; the XAML and
  code-behind were not. Expect the first `verify` run to find something in the XAML.
- The tests found one real bug on the way: `SET VAR $DCSS_CPC[1].$ENABLE` was classified as a plain
  write, because the program-prefix strip cut at the array index's `]`. Fixed, and the cases stay.

### Pick up here

1. **Push and let `verify` compile the app.** Fix whatever the XAML compiler finds.
2. **Phase 0 with a real robot**: `robcontrol-probe <address>` on a V9.40 SpotTool robot, read
   the capture, commit it under `tests/Fixtures/`, and turn every assumption above that it confirms
   or breaks into a line under *Controller gotchas*.
3. Then the rest of Phase 1: tray icon so scheduled backups survive the window being closed, image
   backup freshness from the plant TFTP folder, and `--diagnose` in NetControl.
