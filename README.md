# RobControl

![RobControl](assets/icon.png)

A Windows tool for controls engineers who look after FANUC robots: back them up, keep the history,
see what changed, and read what is inside the controller - registers, I/O, system variables - and
trend it over time. It works across R-30iA, R-30iB and R-30iB Plus controllers.

**Read-only by construction.** RobControl copies files off a controller and reads its state. It
never writes to, moves, starts or deletes anything on a robot, and never changes a controller's
settings to make itself work - when something is locked, it says where on the pendant to change it.

## What it does today

| | |
|---|---|
| **Back up** | FTP copy of `md:` (optionally `fr:`) for one robot or the whole fleet, a few at a time. Plain dated folders you can open in Explorer, a `manifest.json` with the SHA-256 of every file, and the FTP transcript. Anything cut short is marked `_INCOMPLETE` - never a folder that looks like a good backup. |
| **Probe** | What the controller is - generation, software version, application, arm, F-number - and which ways in are open: FTP, diagnostic files, KCL - and whether its clock agrees with this PC's. When something is locked it says where on the pendant to change it, and does not change it itself. |
| **Compare** | Any two backups of a robot: which files changed, were added or removed, and the line diff of the ASCII listings (`.LS`, `.VA`). |
| **Schedule** | Fleet backups every 4/8/12/24 hours while the app is open. A robot whose scheduled backup fails or comes back partial is tried again a few minutes later. |
| **Verify** | Re-checks backups on disk against the SHA-256 recorded when each was taken, so a file edited, truncated or damaged by a bad copy is caught before anyone restores from it. |
| **Trend** | Registers, I/O and system variables, typed as on the pendant (`R[1-10], DI[1..8], $TIMER[1].$TIMER_VAL`). Read once, or record several robots at once and overlay them on one chart. Only changes are stored; CSV export. |
| **Search** | Across every robot's newest backup (or the whole history): a program name, `R[45]`, `DO[120]` or any text. Shows which programs call a program or use a register, and which programs nothing calls. |
| **Alarms** | Alarm history from the alarm logs in the backups, merged across backups: most frequent alarms, alarms per robot, mean time between alarms, and battery, collision and mastering alarms called out. |
| **Fleet** | Every robot's model, software and serial, how its backups stand (OK, failing, stale, never), changes to tool and user frames, payload, mastering and other watched settings, and its network settings as the backup records them. CSV export and a one-page site report. |
| **Sites** | One per plant: its own robot list, archive folder, schedule, event log and trends. Switch from the Site menu; export a site to a file - or the whole site, history and backups, to one zip - to carry it to another laptop. |
| **Many at once** | Ctrl/Shift-click robots in the list and Probe, Back up and Trends act on all of them. |
| **Event log** | Every probe, backup, KCL read and trend session, append-only at the database. Export it as CSV for plant IT. |
| **CSV everywhere** | Alarms, search hits, where-used, the robot list, network settings, trends and the event log all export to CSV for Excel. |
| **Diagnose network** | Opens [NetControl](https://github.com/Robbuie/NetControl) on a robot that does not answer. |

## Getting started

1. **Pick or name your site.** The first time, RobControl opens a site called *My site*. Rename it
   to the plant from **Site > Site settings**, or make one per plant with **Site > New site**.
2. **Add a robot** - its name, as the plant calls it, and the controller's IPv4 address. A controller
   with no FTP users set up accepts `anonymous` with no password, which is the default.
3. **Probe** it. RobControl says what the controller is (model, software version, application) and
   which ways in are open: FTP, the web server's diagnostic files, KCL.
4. **Back up.** The files from `md:` land in the site's archive folder. Select several robots with
   Ctrl- or Shift-click to back them up together, or use **Back up all**.
5. **Compare** two backups of a robot to see which programs and variables changed.

## Sites

A site is one plant. Each site has its own:

- robot list, event log and trend data - two plants can both have an R1-01 at the same address
  without ever being confused;
- archive folder for backups (by default `Documents\RobControl Backups\<site name>`);
- backup schedule, how many robots are backed up at once, and whether `fr:` is copied too;
- FTP login that new robots start with - handy when a plant uses one login on every controller;
- notes, for whatever is worth knowing next visit.

The open site is named in the title bar and the status bar. Switch from **Site > Switch to**.
Switching waits for a running backup or probe, and asks first if trends are recording.

**Site settings** also hold how the site treats its backups:

- **Stale after** - days before a robot's last complete backup is flagged (7 by default).
- **Retries** - how many times a *scheduled* backup tries again, five minutes later
  (`retryDelayMinutes` in `site.json`), a robot that failed or came back partial (1 by default; 0 is off). Backups started by hand are not retried -
  you are there to see the result. Every attempt is its own folder and its own event-log line, so a
  retry that worked does not hide the failure before it.
- **Keep** - complete backups per robot that **Prune old backups** keeps (0, the default, is off).

**Taking a site to another PC** - two ways:

- **Site > Export site file** writes the settings and robot list to one small
  `.robcontrol-site.json`. Backups are not in it.
- **Site > Export site bundle** writes the whole site to one `.robcontrol-bundle.zip`: settings,
  robot list, event log, last probes and trends - and, if you say Yes when asked, every backup in
  the archive. For moving to another laptop, or carrying a plant's history out of a site with no
  network. Inside, the backups are in the same `<robot>\<date_time>` layout, so the zip can be
  opened in Explorer and one backup copied out by hand.

On the other PC, **Site > Import site file or bundle** takes either and always makes a **new**
site - never a merge; a second import of the same file becomes "Plant 3 (2)". Robots are checked as
if typed into the robot dialog. A bundle's backups go into a new archive folder named after the new
site, and its event log and trends come only when its database matches its robot list - if
somebody has edited one and not the other, the robots come from the site file and the history
stays out, and the import says so. Both files contain FTP passwords where robots have them - keep
them like a list of logins.

## Trends

Type signals as on the pendant - `R[1-10], DI[1..8], GO[1], $TIMER[1].$TIMER_VAL` - and they are
added to every selected robot. **Read now** reads them once; **Start recording** polls each robot
on its own, one request at a time and never faster than every 2 seconds, and stores only changes
plus one value a minute. The chart overlays every plotted signal; **Export CSV** writes what is on
it. A robot that stops answering is backed off and picked up again when it comes back.

Registers and I/O need only the controller's web server. System variables need KCL unlocked under
**Setup > Host Comm > HTTP** on the pendant; RobControl says so when it is locked.

## Search

The **Search** tab looks through the newest complete backup of every robot in the site - nothing is
read from the robots, so it works away from the plant too.

- Type a **program name** (`WELD_A`), a **register or port** (`R[45]`, `PR[3]`, `DO[120]`) or any
  text, and press Enter. A register is found however the line writes it - `R[45:Weld count]`,
  `R[ 45]` - and `R[45]` does not match `R[450]` or `PR[45]`.
- For a program name or a register, the lower list shows **which programs call it or use it**, on
  which robots and lines.
- **Programs nothing calls** lists, per robot, programs no other program CALLs or RUNs. They may
  still be started by PNS, RSR, a style table, a macro or the PLC, so treat it as a list to check.
- Choose programs only, programs and variables, or all text files; tick **Every backup** to search
  the history and see when a line first appeared.

## Alarms

**Refresh** on the **Alarms** tab reads the alarm log (`ERRALL.LS`) in every backup of the site.
Each controller keeps only its most recent alarms, so merging the backups gives a longer history
than the controller has. Pick a period to see:

- the most frequent alarms first - where the downtime is - with the robots that had them;
- alarms per robot, the most frequent code on each, and the mean time between alarms;
- every occurrence of the selected alarm.

Battery (`SRVO-065`, `SRVO-062`), collision (`SRVO-050`, `SRVO-053`) and mastering (`SRVO-038`,
`SRVO-075`) alarms are marked **Needs a job**; tick the box to see only those. Times are each
controller's own clock - **Probe** says when a controller's clock is more than two minutes off this
PC's. **Export CSV** writes every alarm in the period and filter shown, one per line.

On the **Search** tab, **Export hits** and **Export programs** do the same for search results and
the where-used list.

## Fleet and the site report

**Refresh** on the **Fleet** tab lists every robot: controller, model, software, F-number, its last
complete backup and how old it is, and a **Backups** column:

| | |
|---|---|
| **OK** | A complete backup within the site's *Stale after* days, and the latest attempt worked. |
| **Last attempt failed** (amber) | The last good backup is still in date, but the attempts since have not completed - the robot is going stale and nobody has noticed yet. |
| **Stale** (red) | The last complete backup is older than *Stale after*. |
| **Never backed up** (red) | No complete backup at all. |

**Export CSV** saves the list for a spreadsheet. The lower half has three views:

- **Setting changes** - between consecutive complete backups of each robot, changes to tool frames,
  user frames, payload, mastering data, reference positions, joint limits, DCS and the software
  version, with the line before and after. These are the changes that quietly change what a robot
  does; the Changes tab has the full diff.
- **Network** - each robot's hostname, IP addresses, subnet mask, router and MAC as its newest
  backup records them, beside the address in the robot list. An address the backup never mentions
  is shown in amber: a swapped controller, a re-addressed port, or a robot list that is out of date.
  These are found by matching variable names in the listings and only shown when the value looks
  like an address; until RobControl has been checked against real controllers, a blank here means
  "not found", not "not set". **Export CSV** includes the variable and file each value came from.
- **Backup check** - fills when you press **Verify backups**: each robot's newest backup (or, ticked,
  every backup) re-read from disk and every file checked against the SHA-256 in its manifest.
  Missing or changed files mark the backup **DAMAGED - do not restore from it**, damaged ones listed
  first; interrupted and manifest-less folders are listed too, so nothing is silently skipped. It
  reads the disk only.

**Prune old backups** (off until *Keep* is set in **Site > Site settings**) works out which backups
are past the limit - per robot, it keeps the newest *Keep* complete backups and everything taken
after the oldest of them - says how many and how much space, and only on **Yes** sends them to the
**Recycle Bin**. Robots with fewer complete backups lose nothing, folders without a manifest are
never touched, and nothing is ever pruned automatically.

**Site report** (also **Site > Site report**) writes one page for the visit - backup status and health,
alarms needing a job, the most frequent alarms, watched setting changes and the site notes - and
opens it in the browser, to print or save as PDF. It is made from the backups alone.

## Where things are kept

| | |
|---|---|
| **Backups** | The site's archive folder: `<robot>\<date_time>\MD\...`, with a `manifest.json` (SHA-256 of every file) and the FTP transcript in each. Readable in Explorer without RobControl. A backup cut short ends in `_INCOMPLETE`. |
| **Sites** | `%LOCALAPPDATA%\RobControl\sites\` - a folder per site with its `site.json` and its database. **Site > Open site folder** goes there. |
| **Site reports, CSV exports and bundles** | Wherever you save them; RobControl suggests a name with the site and the date. |
| **Event log** | In the site's database; the **Event log** tab shows it and **Export CSV** there writes it all - every probe, backup, KCL read and trend session, and what came back. What to hand plant IT or a FANUC engineer who asks what this laptop has been doing on their network. |
| **Diagnostic log** | `%LOCALAPPDATA%\RobControl\logs` - **Help > Open the diagnostic log folder**. Send this when something misbehaves. |
| **Update check** | One request at startup to GitHub for the latest release. Turn it off with `"checkForUpdates": false` in `%LOCALAPPDATA%\RobControl\settings.json`. |

## Command line

| | |
|---|---|
| `RobControl.exe --site "Plant 3"` | Opens that site instead of the one used last. |
| `RobControl.exe --robot 10.20.1.54` | Selects that robot in the open site - how NetControl's *Open in RobControl* lands. |

## Help

**Help > Read me** shows this page; **Help > What's new** shows the changes in each version.
**Help > Check for updates** looks for a newer release. The source and every release are at
[github.com/Robbuie/RobControl](https://github.com/Robbuie/RobControl).

<!-- end of in-app readme -->
<!-- Everything above is shown in the app under Help > Read me. Keep it for users, and current. -->

## For developers

It talks to live production robots. **Read [the safety rules](CLAUDE.md#safety---this-touches-live-production-robots)
before changing anything that sends a packet.** [CLAUDE.md](CLAUDE.md) is the full picture. The
part of this file above here is what the app shows under **Help > Read me**: keep it written for the
person using the app, and update it with every change that affects what they see.

### Trying it without a robot

```powershell
dotnet run --project src/RobControl.RobotSim -- tests/Fixtures/synthetic-r30ibplus-v940-spottool --animate
# FTP 127.0.0.1:2121, HTTP 127.0.0.1:8080
```

Then in RobControl add a robot at `127.0.0.1` with FTP port `2121` and web port `8080`, and probe
it or back it up. With `--animate`, `R[1-4]`, `DI[1]`, `DO[1]`, `GI[1]` and `$TIMER[1].$TIMER_VAL`
move, so the Trends tab has something to draw. Start a second simulator on other ports to try
several robots at once. The synthetic profile is hand-written; see `tests/Fixtures/README.md`.

### First contact with a real robot

```powershell
dotnet run --project tools/RobControl.Probe -- 10.20.1.54
```

Read-only. Logs in (anonymous unless `--user` / `--password`), lists `md:`, copies a handful of
representative files, fetches the diagnostic pages and asks KCL two `SHOW` questions - then writes
everything it saw into a `capture-...` folder in the shape `robotsim` serves. That folder is how
RobControl learns what your controllers really say: read it through for anything site-confidential,
then commit it under `tests/Fixtures/`.

### Building

```powershell
dotnet build
dotnet test
dotnet run --project src/RobControl.App

pwsh tools/publish.ps1              # -> artifacts/RobControl-<version>/RobControl.exe
pwsh tools/publish.ps1 -Installer   # and the setup exe
```

Releases are a tag push - see [RELEASING.md](RELEASING.md).

### Layout

`src/RobControl.Core` is the engine and **must not reference any UI assembly**. `src/RobControl.App`
is the WPF front end, sharing NetControl's appearance system. `src/RobControl.RobotSim` is the fake
controller every test runs against. `tools/RobControl.Probe` is the capture tool. [CLAUDE.md](CLAUDE.md)
is the full picture.
