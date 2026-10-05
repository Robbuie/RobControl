# Changelog

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
- Samples older than 30 days are pruned (`trendRetentionDays` in `robcontrol.json`). Starting and
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
