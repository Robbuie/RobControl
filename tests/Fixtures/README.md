# Test fixtures

Each folder is a **controller profile**: what `robotsim` serves and what the tests run against.

```
<profile>/profile.json   banner, login rules, which HTTP resources are locked
<profile>/kcl.json       KCL command -> the HTML page the controller answers with
<profile>/MD/ ...        files on md:  (FR/, MC/ for other devices)
```

## synthetic-*

**Written by hand.** They look like a controller, they are not one. Every format in them - the
summary file, the KCL page wrapping, the FTP banner - is a guess from documentation and field
reports, and is exactly what Phase 0 exists to replace.

## Captures from real controllers

`tools/RobControl.Probe` writes a profile folder from a real robot, read-only. Name it
`<generation>-<version>-<application>` (e.g. `r30ibplus-v940p23-spottoolplus`), check it for
anything site-confidential (program names and comments can be), and commit it here. Then:

1. Add it to the theory data in `CapturedProfileTests` so every parser runs against it.
2. Anything it gets wrong becomes a fix and a line in CLAUDE.md's *Controller gotchas*, with the
   version it was seen on.
