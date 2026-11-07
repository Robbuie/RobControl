# excerpt-r30ib-v830-repairtool

**Trimmed excerpts of real pages**, not a full capture and not a robotsim profile (no
`profile.json`). Read from an R-30iB, V8.30 RepairTool+ controller's web server in a browser on
6 November 2026, while working out why the Live tab showed no I/O.

- Each file is the page as the web server sends it: an HTML wrapper (title, a script block, a header
  table) with the diagnostic file inside one `<pre>`. The wrapper is shortened; the `<pre>` content
  is the controller's own text, cut down to a few lines of each kind.
- The F-number and robot number are replaced with zeros.
- `IOSTATE.DG` on that controller is ~130 KB: 2032 `DIN`, 2032 `DOUT`, 1024 `FLG` (two to a line),
  plus `GIN`, `GOUT`, `UI`, `UO`, `SI`, `SO`, `RI`, `RO`. No safe I/O on this one.

Replace with a full `robcontrol-probe` capture when one is taken.
