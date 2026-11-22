# Line Gap Checker

**An AutoCAD plugin that finds the small drawing mistakes your eye misses: lines that stop a few centimetres short, and lines that stick out a few millimetres too far. It colours them so you can fix them.**

![What the checker flags](docs/cases.svg)

## What it does

When you draw in AutoCAD, lines often *look* connected but aren't. You only notice when hatching fails, an area won't close, or a CNC/GIS export breaks. Line Gap Checker scans your LINE objects and colours every problem:

| Colour | Meaning | Caught when |
|---|---|---|
| 🔵 **Blue** | **Extend this line** | Its end stops short of another line, an open corner, or a gap in a straight run. Gaps up to **10 cm** are reported. |
| 🟣 **Magenta** | **Trim this line** | It sticks out past a crossing by **5 mm or less**, or the whole line is **5 mm or shorter** (stray bits, zero-length lines). |

Lines that connect properly at corners, T-junctions and crossings are left alone. Your original colours are saved, so one command puts everything back.

## How to use it

1. **Load it.** In AutoCAD, type `NETLOAD` and pick `LineGapChecker.dll`. The command line lists the commands.
2. **Check.** Type `GAPCHECK`, then press **Enter** to check every line, or select only the lines you want. Problem lines turn blue or magenta, and the command line shows a summary like this:
   ```
   GAPCHECK: 1240 line(s) checked in 85 ms. Units: millimetres (INSUNITS=4).
     To extend (blue):   3
     To trim (magenta):  2
     Findings:
       TRIM overshoot      0.0040 m  at 6000,-4
       EXTEND to line      0.0500 m  at 2000,50
   ```
   Each finding gives the problem type, its size in metres and where it is.
3. **Fix** the coloured lines with `EXTEND` and `TRIM`, then run `GAPCHECK` again until nothing is flagged. Re-running clears the old colours first, so results never pile up.
4. **Clean up.** Type `GAPRESET` to give every line its original colour back.

Undo works too: one `UNDO` after `GAPCHECK` reverses the whole check.

### Changing the limits (optional)

Type `GAPSET` to change the limits for the current AutoCAD session:

| Option | Default | What it controls |
|---|---|---|
| `Short` | 0.005 m (5 mm) | Largest overshoot to trim; lines this short or shorter are flagged |
| `Gap` | 0.10 m (10 cm) | Largest gap to report |
| `Tolerance` | 0.000001 m | How close two lines must be to count as touching |
| `Units` | 0 = automatic | Drawing units per metre. Normally read from `INSUNITS`. |

The plugin reads the drawing's units from `INSUNITS` (mm, cm, m, km, in, ft, yd, mi and more), so the limits are always in real metres. If the units aren't set, it assumes metres and prints a warning.

> Only **LINE** objects are checked. If your drawing uses polylines, `EXPLODE` them first. GAPCHECK tells you how many it skipped.

## Install / build

📘 **Full step-by-step guide: [INSTALL.md](INSTALL.md)**. It covers building, auto-loading on startup, security prompts, units, settings, updating, uninstalling and troubleshooting.

Quick version: build the DLL, then run `install.bat` once (with AutoCAD closed) so the plugin loads automatically on every start.

| AutoCAD version | Build |
|---|---|
| **2024 or older** (.NET Framework 4.8) | `build.bat "C:\Program Files\Autodesk\AutoCAD 2024"` produces `bin\LineGapChecker.dll` |
| **2025 or newer** (.NET 8) | `dotnet build LineGapChecker.csproj -c Release -p:AcadDir="C:\Program Files\Autodesk\AutoCAD 2025"` |

`build.bat` needs only the `csc.exe` that ships with Windows. It runs the unit tests first and builds the plugin only if they pass. The AutoCAD DLLs (`acdbmgd`, `accoremgd`, `acmgd`) are referenced, never copied.

---

## How it works (technical)

### Project layout

| File | Role |
|---|---|
| `src/GapEngine.cs` | Pure geometry engine. **No AutoCAD dependency**, so it can be unit-tested anywhere. |
| `src/GapCommands.cs` | AutoCAD layer: `GAPCHECK`, `GAPSET`, `GAPRESET`, and `IExtensionApplication` |
| `tests/GapEngineTests.cs` | Unit tests (a self-contained console runner, no NuGet) |
| `test.bat` / `build.bat` / `LineGapChecker.csproj` | Build scripts for both AutoCAD generations |
| `install.bat` / `uninstall.bat` / `bundle/PackageContents.xml` | Auto-load bundle: AutoCAD loads the plugin on every start |

The sources are written in **C# 5**, so the same files compile with the old Framework `csc.exe` and with the .NET 8 SDK.

### Detection rules

All geometry is 2D, and thresholds are converted from metres to drawing units.

- A **free end** is an endpoint that is not within the touch tolerance of any other line.
- **Extend.** A free end qualifies when, within the max gap:
  - **To line**: its outward ray hits another line.
  - **Corner**: its ray hits the *extension* of another line, near that line's own free end. Both lines stop short.
  - **Collinear**: a line in the same direction starts after a gap.

  Near-parallel lines (sin θ < 1e-4) are ignored except in the collinear case. The nearest hit wins.
- **Trim.**
  - **Short line**: total length is at most the short length, including zero-length lines.
  - **Overshoot**: a free end runs past a crossing, and the stub is longer than the touch tolerance and at most the short length.
- **Both**: a line needing both is coloured magenta. The summary counts these lines and tags their findings.
- **Tiny lines**: these still count as "touching" when finding free ends, but are never used as extension or crossing targets.

### Speed: a spatial grid, not O(n²)

Comparing every line with every other line takes 1.25 billion checks for 50,000 lines. Instead, the engine builds a **uniform hash grid**:

- Each segment is registered only in the cells it **actually passes through** (Amanatides–Woo grid traversal), not its whole bounding box. A long diagonal line costs a few cells, not thousands.
- The cell size is at least twice the max gap, so every query (touch test, ray, corner, overshoot) reads at most a 3×3 block of cells.
- The traversal takes exactly |Δx| + |Δy| steps, so floating-point rounding can never run past the end cell.
- Coordinates are shifted to a local origin first, so survey-sized coordinates (hundreds of km, in mm) keep full precision.

### Never losing your colours

Before recolouring a line, GAPCHECK writes its original colour into **XData** under the registered app `LINEGAPCHECK`. The record holds a format version, the colour method (ByLayer, ByBlock, ACI or true colour), the ACI index, the RGB value and, for colour-book colours, the book and colour name. `GAPRESET`, and the start of every `GAPCHECK`, rebuild the colour from that record and remove the XData.

The whole check runs in **one transaction**, so one `UNDO` reverses it. Lines are opened for read and upgraded to write only when they actually change. Lines on **locked layers** are still analysed but are not recoloured or restored, and the summary says how many were skipped.

### Measured results

From `test.bat` on a laptop:

| Test | Result |
|---|---|
| 21 unit tests: T-gap, open corner, collinear gap, 4 mm overshoot (trim), 20 mm overshoot (ignored), gap > max (ignored), tiny and zero-length lines, clean corner, T and cross, mm drawing, unit table, UTM-size coordinates and more | **all pass** |
| 50,244-line lattice with injected defects | **~50 ms**, exact expected counts |
| 50,000 random lines up to 80 m long | **~260 ms**. The same results come back when the drawing is rotated 90°, which changes every grid cell. |

### Verification status

The geometry engine is compiled and fully unit-tested. The AutoCAD command layer (`GapCommands.cs`) has been compiled against stand-ins that copy the AutoCAD .NET API signatures, but **has not yet been run inside AutoCAD**. Treat the first real-drawing run as the acceptance test:

1. Draw a T with a 50 mm gap, a 4 mm overshoot, a 3 mm line, a clean corner, and a green or true-colour line.
2. Run `GAPCHECK` and check the colours and the summary.
3. Run `UNDO` and check that the drawing is back to how it was.
4. Run `GAPCHECK` again, then `GAPRESET`. Check that the original colours are back.
5. Lock a layer with a flagged line and check that the summary reports it as skipped.

---

Built by **lumidren**.
