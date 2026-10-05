# Changelog

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
