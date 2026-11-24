Line Gap Checker for AutoCAD 2019 and newer (64-bit Windows, full AutoCAD, not LT)
https://github.com/lumidren/Line-Gap-Checker

INSTALL
  1. Close AutoCAD.
  2. Double-click install.bat.
  3. Start AutoCAD and type GAPCHECK.
     If AutoCAD asks whether to load LineGapChecker.dll, choose "Always Load".

USE
  GAPCHECK   blue = needs extending, magenta = needs trimming
  GAPSET     change the limits (default: gaps up to 10 cm, overshoots up to 5 mm)
  GAPRESET   put the original colours back

REMOVE
  Run GAPRESET in any drawing that is still coloured, close AutoCAD,
  then double-click uninstall.bat.

The zip holds three builds: AutoCAD 2019-2024, 2025-2026 and 2027+.
AutoCAD loads the one that matches its version.
