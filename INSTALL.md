# Installing and configuring Line Gap Checker

This guide covers how to build the plugin, load it into AutoCAD (once, or automatically on every start), configure it, update it and remove it.

## Before you start

- **Full AutoCAD on 64-bit Windows.** AutoCAD **LT does not support plugins**, so this tool cannot run in LT.
- Know your AutoCAD version, because it decides which build you need:

| Your AutoCAD | Build type | What you need on the PC |
|---|---|---|
| 2019 – 2024 | .NET Framework 4.8 | Nothing extra: `build.bat` uses the compiler that ships with Windows |
| 2025 and newer | .NET 8 | The [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |

- Find your **AutoCAD install folder**. It is usually `C:\Program Files\Autodesk\AutoCAD 2024`. If you're not sure, right-click the AutoCAD shortcut and choose **Open file location**.

## Step 1: Build the DLL

Open a terminal in this folder and run the command for your version.

**AutoCAD 2019 – 2024:**

```powershell
.\build.bat "C:\Program Files\Autodesk\AutoCAD 2024"
```

This runs the unit tests first, then creates `bin\LineGapChecker.dll`.

**AutoCAD 2025 and newer:**

```powershell
dotnet build LineGapChecker.csproj -c Release -p:AcadDir="C:\Program Files\Autodesk\AutoCAD 2025"
```

This creates `bin\x64\Release\net8.0-windows\LineGapChecker.dll`.

> If someone sends you an already-built `LineGapChecker.dll`, skip this step. Right-click the file → **Properties** → tick **Unblock** → **OK**. Windows blocks DLLs that arrive by download, email or zip, and AutoCAD will refuse to load a blocked file.

## Step 2: Load it into AutoCAD

### Option A: Load once (for a quick test)

1. In AutoCAD, type `NETLOAD` and press Enter.
2. Pick `LineGapChecker.dll`.
3. The command line shows:
   ```
   LineGapChecker loaded. Commands:
     GAPCHECK  Check lines, arcs and polylines: blue = needs extending, magenta = needs trimming
     GAPSET    Set thresholds for this session
     GAPRESET  Restore the original colours of objects marked by GAPCHECK
   ```

You have to repeat this each time you restart AutoCAD.

### Option B: Load automatically on every start (recommended)

1. **Close AutoCAD.**
2. Run:
   ```powershell
   .\install.bat
   ```
   This copies the plugin into `%APPDATA%\Autodesk\ApplicationPlugins\LineGapChecker.bundle`. AutoCAD checks that folder at startup. The bundle holds both builds, and AutoCAD picks the right one for its version.
3. Start AutoCAD. The command list appears on the command line, and the commands work in every drawing.

### If AutoCAD shows a security warning

AutoCAD warns before loading code from a folder it doesn't trust (the `SECURELOAD` setting). Either:
- choose **Always Load** in the prompt, or
- add the plugin folder to the trusted list: type `OPTIONS` → **Files** tab → **Trusted Locations** → **Add**. Then pick the folder that holds the DLL (for Option B, `%APPDATA%\Autodesk\ApplicationPlugins\LineGapChecker.bundle\Contents`).

## Step 3: Check that it works

1. Open a drawing that contains lines, arcs or polylines, or draw a quick test: one long horizontal line, and a vertical line that stops a little short of it.
2. Type `GAPCHECK` and press **Enter** to check everything.
3. Objects that stop short turn **blue**, and objects that stick out a little turn **magenta**. The command line prints a summary.
4. Type `GAPRESET` to put the colours back.

## Configuration

### Drawing units: important

The limits are in real-world metres. The plugin reads the drawing's units from the `INSUNITS` setting to convert them. To check or set it, type `UNITS` and look at **Insertion scale**. It must match what one drawing unit really means (Millimeters, Meters, and so on).

If the units are wrong, the results are wrong. For example, a drawing in millimetres marked as metres would make every limit 1000 times too big. If `INSUNITS` is not set, the plugin assumes metres and prints a warning.

### Limits (`GAPSET`)

Type `GAPSET`, pick an option, enter a new value, then press **Enter** to finish.

| Option | Default | Meaning |
|---|---|---|
| `Short` | 0.005 m (5 mm) | Overshoots up to this length are flagged for trimming, and so are whole lines this short |
| `Gap` | 0.10 m (10 cm) | Gaps up to this size are flagged for extending. Bigger gaps are treated as intentional. |
| `Tolerance` | 0.000001 m | Objects closer than this count as touching |
| `Units` | 0 | Drawing units per metre. `0` = automatic from `INSUNITS`. Set it, e.g. `1000` for millimetres, only if `INSUNITS` can't be trusted. |
| `Defaults` | | Puts all of the above back to the defaults |

`GAPSET` values last until AutoCAD is closed. After a restart, the defaults apply again.

## Updating to a new version

1. Close AutoCAD. A loaded DLL is locked and can't be replaced.
2. Get the new code and build again (Step 1).
3. Run `install.bat` again, or `NETLOAD` the new DLL.

## Uninstalling

1. In every drawing where objects are still coloured, run `GAPRESET`.
2. Close AutoCAD.
3. Run:
   ```powershell
   .\uninstall.bat
   ```

If you only used `NETLOAD`, there's nothing to uninstall. The plugin is gone after an AutoCAD restart.

## Troubleshooting

| Problem | Cause and fix |
|---|---|
| `Unknown command "GAPCHECK"` | The plugin isn't loaded. Run `NETLOAD`, or check that `install.bat` ran while AutoCAD was closed. |
| `NETLOAD` fails with "Cannot load assembly" | The DLL is blocked: Properties → **Unblock**. Or it's the wrong build for your version, since 2025+ needs the .NET 8 build. |
| `build.bat` says the AutoCAD DLLs were not found | Wrong folder. Pass the folder that contains `acdbmgd.dll`. |
| Nothing is flagged, but there are gaps | The gaps may be larger than the `Gap` limit, or the units may be wrong. Splines, ellipses and spline-fit polylines are not checked: the summary says how many objects were skipped. |
| A whole polyline turned blue or magenta | A polyline is one object, so the whole thing is coloured. The coordinates in the summary show which end needs fixing. |
| Far too many lines are flagged | The units are probably wrong. Check `UNITS` → Insertion scale. |
| "flagged object(s) are on locked layers" | Locked objects are checked but not recoloured. Unlock the layer and run `GAPCHECK` again. |
| `install.bat` says "Copy failed" | AutoCAD is open and holding the DLL. Close it and run the script again. |

> **Status:** the geometry engine and the build and install scripts are tested. Loading inside AutoCAD has not been tested yet, because no AutoCAD was available while this was built. Please report anything that doesn't work as described.
