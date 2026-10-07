# Changelog

## 0.5.0 - backup health, verify, bundles and exports

Nothing here sends anything new to a robot except the clock check, which reads a header the web
server already sends with the probe's existing request.

- **Backup health** on the Fleet tab and in the site report: OK, *last attempt failed* (amber - the
  last good backup is in date but the attempts since have not completed), stale, or never. The stale
  limit is now a site setting (**Stale after**, 7 days by default).
- **Scheduled backups retry**: a robot whose scheduled backup failed or came back partial is tried
  again five minutes later (**Retries** in site settings, 1 by default). Backups started by hand are
  not retried. Every attempt keeps its own folder and event-log line.
- **Verify backups** (Fleet tab): re-reads backups from disk and checks every file against the
  SHA-256 in its manifest. Damaged backups are listed first and marked *do not restore*;
  interrupted and manifest-less folders are listed too.
- **Prune old backups** (Fleet tab), off until **Keep** is set in site settings: keeps the newest
  *Keep* complete backups per robot and everything after them, says what would go, and only on Yes
  sends it to the Recycle Bin. Nothing is pruned automatically.
- **Network** view on the Fleet tab: hostname, IP addresses, subnet mask, router and MAC from each
  robot's newest backup, with an address the backup never mentions shown in amber. Found by variable
  name - to be confirmed against real controllers. CSV export with the source of every value.
- **Controller clock**: the probe compares the controller's clock with this PC's and warns when it
  is more than two minutes off - alarm times and history from that robot would not line up.
- **Site bundle**: **Site > Export site bundle** writes the whole site to one zip - settings, robot
  list, event log, trends and, if asked, every backup. **Import site file or bundle** takes either
  and always makes a new site; robots are checked as if typed in, entry names in the zip are
  checked like file names from a controller, and existing backup folders are never overwritten.
- **CSV exports** for alarms, search hits, where-used and the event log (the record of everything
  RobControl has sent to a controller at the site - for plant IT).
- **Fixed:** the Search, Alarms and Fleet tabs from 0.4.0 used `File` and `IOException` without
  `using System.IO`, which a WPF project does not get implicitly, so the 0.4.0 app would not build.

## 0.4.0 - fleet insight from backups

Everything here reads the backups already on disk - nothing new is sent to a robot.

- **Search** tab: a program name, `R[45]`, `DO[120]` or any text across every robot's newest backup
  (or the whole history), with the lines around each hit. Registers and ports are matched however a
  line writes them. For a program or register, **which programs call or use it**; and **Programs
  nothing calls**, per robot.
- **Alarms** tab: alarm history from the alarm logs in every backup, merged and de-duplicated - most
  frequent alarms, per-robot counts and mean time between alarms, occurrences of one alarm, and
  battery, collision and mastering alarms marked as needing a job.
- **Fleet** tab: model, software, F-number and backup age of every robot (stale in red after 7
  days), CSV export, and **watched setting changes** between backups - tool and user frames,
  payload, mastering, reference positions, joint limits, DCS, software version.
- **Site report** (Fleet tab, or Site > Site report): one printable page for the visit - backup
  status, alarms needing a job, frequent alarms, setting changes and site notes.
- **Fixed: the downloaded exe did nothing on another PC.** The single-file build left WPF's and
  SQLite's native DLLs beside the exe instead of inside it, and the release ships the exe alone, so it
  exited without a window or a message. They are now inside the exe, and the build refuses to publish
  if anything is left beside it.

## 0.3.1 - help inside the app

- **Help > Read me** (F1) shows how to use RobControl, and **Help > What's new** shows this
  changelog - both compiled into the exe, so a portable copy has its help too, and it always
  matches the version running. Links open in the browser; **Open on GitHub** shows the newest copy.
- The README is rewritten for the person using the app: getting started, sites, trends, where
  things are kept, the command line. The developer notes follow it.

## 0.3.0 - sites

- **A site per plant.** Each site has its own robot list, event log, trends, archive folder, schedule,
  and the FTP login new robots start with. Switch between them from the **Site** menu; the open
  site is named in the title bar and the status bar. Two plants can both have an R1-01 at the same
  address without ever meeting.
- Each site is a folder under `%LOCALAPPDATA%\RobControl\sites\` holding a hand-editable
  `site.json` and its own database. **Site > Open site folder** goes there.
- **Export site file / Import site file**: a site's settings and robot list in one
  `.robcontrol-site.json`, to carry to another laptop or hand to a colleague. Importing always makes
  a new site - never a merge - and an archive folder that is not on this PC falls back to the
  default. The file contains FTP passwords where robots have them.
- `RobControl.exe --site "Plant 3"` opens that site.
- **Upgrading from 0.2.0:** the existing robot list, event log and trends move into a first site
  called "My site", keeping the archive folder, schedule and settings already chosen - so every
  existing backup is still in its robot's history. Rename it from Site > Site settings.

## 0.2.0 - trending, and many robots at once

- **Trending** of registers, I/O and system variables. Type signals as on the pendant -
  `R[1-10], DI[1..8], GO[1], $TIMER[1].$TIMER_VAL` - and they are added to every selected robot.
  **Read now** reads them once; **Start recording** polls every robot at once (each on its own loop,
  one request at a time per controller, never faster than every 2 s) and stores only changes plus a
  value a minute. A chart overlays every plotted signal, with on/off I/O in lanes underneath, over
  5 minutes to 7 days; **Export CSV** writes what is plotted.
- Registers come from one fetch of `NUMREG.VA`, I/O from one fetch of `IOSTATE.DG`, system
  variables from KCL `SHOW VAR` (one each). A robot with KCL locked still trends its registers and I/O.
- **Multi-select** in the robot list (Ctrl/Shift-click): Probe, Back up and the whole Trends tab act on
  every selected robot.
- A robot that stops answering is backed off up to a minute, and comes back on its own.
- Samples older than 30 days are pruned (`trendRetentionDays` in `robcontrol.json`; per site in `site.json` from 0.3.0). Starting and
  stopping a recording is in the event log for good.
- `robotsim --animate` makes registers, I/O and a timer move, for trying all of this without a robot.

## 0.1.0 - first build, no robot yet

The foundation, written and tested against a simulated controller. Nothing here has met a real
FANUC controller yet - the first capture with `robcontrol-probe` is what confirms (or corrects) the
formats it assumes.

- **Backup over FTP**, read-only by construction: the FTP client cannot send anything but log in,
  list and download. One robot or the fleet, a few at a time, into plain dated folders with a
  `manifest.json` (SHA-256 of every file) and the FTP transcript. Anything cut short is marked
  `_INCOMPLETE`; one stubborn file is retried on a fresh session, then recorded and skipped.
- **Probe**: what the controller is (generation, software version, application, arm, F-number) and
  which ways in are open - FTP, diagnostic files, KCL - with the pendant menu path when one is locked.
- **Compare** any two backups: changed, added and removed files, and line diffs of the ASCII
  listings. Live-data `.DG` files are hidden by default.
- **KCL reads** behind a classifier that only lets SHOW, DIRECTORY, TYPE and HELP through. Safety,
  DCS and mastering variables can never be written, even when writes arrive.
- **Event log**: append-only at the database, every probe and backup recorded.
- **Scheduled fleet backups** while the app is open.
- **Diagnose network** opens NetControl on the robot's address.
- `robotsim` - a fake controller for development - and `robcontrol-probe`, which records a real
  controller read-only into a profile robotsim can replay.
