Line Gap Checker for AutoCAD 2019 and newer (64-bit Windows, full AutoCAD, not LT)
https://github.com/lumidren/Line-Gap-Checker

INSTALL (loads on every AutoCAD start)
  1. Close AutoCAD.
  2. Double-click install.bat.
  3. Start AutoCAD. If it asks whether to load LineGapChecker.dll,
     choose "Always Load".
  The three builds (AutoCAD 2019-2024, 2025-2026 and 2027+) are all
  installed, and AutoCAD loads the one that matches its version.

TRY WITHOUT INSTALLING
  In AutoCAD type NETLOAD and pick the DLL for your version:
    AutoCAD 2019-2024   LineGapChecker.bundle\Contents\net48\LineGapChecker.dll
    AutoCAD 2025-2026   LineGapChecker.bundle\Contents\net8\LineGapChecker.dll
    AutoCAD 2027+       LineGapChecker.bundle\Contents\net10\LineGapChecker.dll
  If NETLOAD refuses it: right-click the DLL > Properties > tick Unblock > OK.

USE
  1. Type UNITS and check that Insertion scale matches the drawing
     (Millimeters, Meters, ...). Limits are in real metres.
  2. Type GAPCHECK, press Enter to check everything (or select objects first).
     A green circle appears on every spot to fix, like the red circles HATCH
     shows around a gap:
       an end that stops short  -> EXTEND it (gaps up to 10 cm)
       an end that sticks out   -> TRIM it (overshoots up to 5 mm)
       a tiny stray object      -> ERASE it
     The command line lists each finding with its size and position.
  3. Fix, then run GAPCHECK again until no circles are left.
  4. Type GAPCLEAR to remove the circles.
  The circles are only drawn on screen: nothing in the drawing is changed
  and they are never saved. GAPSET changes the limits for this session.

REMOVE
  Close AutoCAD, then double-click uninstall.bat.
