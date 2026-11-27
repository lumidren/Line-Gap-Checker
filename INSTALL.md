# Installing and configuring Line Gap Checker

This guide covers how to install the plugin so AutoCAD loads it on every start, and how to configure, update and remove it. Building from source is at the end. You only need it to change the code.

## Before you start

- **Full AutoCAD 2019 or newer on 64-bit Windows.** AutoCAD **LT does not support plugins**, so this tool cannot run in LT.
- **Close AutoCAD** before you install, update or remove the plugin. AutoCAD locks the plugin while it runs.
- You don't need admin rights, Visual Studio or a .NET SDK.

## Step 1: Install

### Option A: one line in PowerShell (recommended)

1. Open **PowerShell**: Start menu → type `powershell` → Enter.
2. Paste this line and press Enter:
   ```powershell
   irm https://raw.githubusercontent.com/lumidren/Line-Gap-Checker/main/install.ps1 | iex
   ```
3. It downloads `LineGapChecker.zip` from the latest GitHub release and installs it. You should see:
   ```
   Line Gap Checker 1.2.1 installed to C:\Users\<you>\AppData\Roaming\Autodesk\ApplicationPlugins\LineGapChecker.bundle
   Builds: net10, net48, net8 (AutoCAD picks the right one for its version)
   ```

`irm` downloads the script and `iex` runs it. To read the script before you run it, open [install.ps1](install.ps1).

### Option B: download the zip

1. Download **`LineGapChecker.zip`** from the [latest release](https://github.com/lumidren/Line-Gap-Checker/releases/latest).
2. Unzip it anywhere, for example with right-click → **Extract All**.
3. Double-click **`install.bat`** in the unzipped folder.

### What gets installed

Both options copy one folder, `LineGapChecker.bundle`, into `%APPDATA%\Autodesk\ApplicationPlugins`. AutoCAD checks that folder at startup and loads every bundle in it. The bundle holds three builds, and AutoCAD picks the one that matches its version:

| Your AutoCAD | Runtime | Folder in the bundle |
|---|---|---|
| 2019 – 2024 | .NET Framework 4.8 | `Contents\net48` |
| 2025 – 2026 | .NET 8 | `Contents\net8` |
| 2027 and newer | .NET 10 | `Contents\net10` |

Windows marks downloaded files as blocked, and AutoCAD refuses to load a blocked DLL. Both installers clear that mark for you.

## Step 2: Start AutoCAD

Start AutoCAD. The command line shows:

```
LineGapChecker loaded. Commands:
  GAPCHECK  Check lines, arcs and polylines: blue = needs extending, magenta = needs trimming
  GAPSET    Set thresholds for this session
  GAPRESET  Restore the original colours of objects marked by GAPCHECK
```

The commands work in every drawing, and the plugin loads by itself every time AutoCAD starts.

### If AutoCAD shows a security warning

AutoCAD warns before it loads code from a folder it doesn't trust (the `SECURELOAD` setting). Either:
- choose **Always Load** in the prompt, or
- add the plugin folder to the trusted list: type `OPTIONS` → **Files** tab → **Trusted Locations** → **Add**, and enter `%APPDATA%\Autodesk\ApplicationPlugins\LineGapChecker.bundle\Contents\...` (the `\...` at the end includes the subfolders).

### Trying it without installing

1. Download the DLL for your AutoCAD from the [latest release](https://github.com/lumidren/Line-Gap-Checker/releases/latest). Type `ABOUT` in AutoCAD if you're not sure of your version.

   | Your AutoCAD | Download |
   |---|---|
   | 2019 – 2024 | `LineGapChecker-AutoCAD-2019-2024.dll` |
   | 2025 – 2026 | `LineGapChecker-AutoCAD-2025-2026.dll` |
   | 2027 and newer | `LineGapChecker-AutoCAD-2027.dll` |

   The same builds are also in the zip, under `LineGapChecker.bundle\Contents\net48`, `net8` and `net10`.
2. Right-click the DLL → **Properties** → tick **Unblock** → **OK**. AutoCAD refuses DLLs that Windows has marked as downloaded.
3. In AutoCAD, type `NETLOAD` and pick the DLL. The command list appears on the command line.

This lasts until you close AutoCAD. After a restart, run `NETLOAD` again.

## Step 3: Check that it works

A two-minute test in a new drawing:

1. Type `INSUNITS`, then `4` to set the drawing to millimetres.
2. Draw three lines with `LINE`, typing the points:
   - a floor: `0,0` to `1000,0`
   - a wall that stops 50 mm short of the floor: `300,50` to `300,600`
   - a wall that runs 4 mm past the floor: `700,-4` to `700,600`
3. Type `GAPCHECK` and press **Enter** twice.
4. The first wall turns **blue** (`EXTEND to line 0.0500 m`) and the second turns **magenta** (`TRIM overshoot 0.0040 m`). The floor stays as it is.
5. Type `GAPRESET`. Both walls get their original colour back.

How to use the plugin day to day is in the README, under [How to use it](README.md#how-to-use-it).

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

Close AutoCAD, then run the same PowerShell line from Step 1 again, or the new zip's `install.bat`. Either one replaces the old version.

## Uninstalling

1. In every drawing where objects are still coloured, run `GAPRESET`.
2. Close AutoCAD.
3. Double-click `uninstall.bat` in the unzipped release folder, or run this in PowerShell:
   ```powershell
   & ([scriptblock]::Create((irm https://raw.githubusercontent.com/lumidren/Line-Gap-Checker/main/install.ps1))) -Uninstall
   ```

If you only used `NETLOAD`, there's nothing to uninstall. The plugin is gone after an AutoCAD restart.

## Building from source

You only need this to change the code. The release zip is built by CI ([build.yml](.github/workflows/build.yml)) from the same steps.

**All three builds**, with the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). No AutoCAD is needed, because the API comes from Autodesk's official [`AutoCAD.NET`](https://www.nuget.org/packages/AutoCAD.NET) NuGet packages:

```powershell
dotnet build LineGapChecker.csproj -c Release
```

This creates `bin\x64\Release\net48\`, `net8.0-windows\` and `net10.0-windows\`, each with a `LineGapChecker.dll`. Add `-f net8.0-windows` to build just one.

**AutoCAD 2019 – 2024 only, without any SDK.** This uses the `csc.exe` that ships with Windows and the DLLs of your local AutoCAD:

```powershell
.\build.bat "C:\Program Files\Autodesk\AutoCAD 2024"
```

It runs the engine unit tests first, then creates `bin\LineGapChecker.dll`. To run only the tests, use `.\test.bat`.

Then, with AutoCAD closed, run `.\install.bat` to install whatever you built.

## Troubleshooting

| Problem | Cause and fix |
|---|---|
| `Unknown command "GAPCHECK"` | The plugin isn't loaded. Check that the installer ran while AutoCAD was closed and printed "installed to". Then restart AutoCAD. |
| The PowerShell line says "running scripts is disabled" | Run it in a new PowerShell window exactly as written: `irm ... \| iex` does not need a script file. Or use Option B. |
| `NETLOAD` fails with "Cannot load assembly" | The DLL is blocked: Properties → **Unblock**. Or it's the wrong build for your version (see the table in Step 1). |
| The installer says AutoCAD is running | Close every AutoCAD window, and check Task Manager for a leftover `acad.exe`. |
| `build.bat` says the AutoCAD DLLs were not found | Wrong folder. Pass the folder that contains `acdbmgd.dll`. |
| Nothing is flagged, but there are gaps | The gaps may be larger than the `Gap` limit, or the units may be wrong. Splines, ellipses and spline-fit polylines are not checked: the summary says how many objects were skipped. |
| A whole polyline turned blue or magenta | A polyline is one object, so the whole thing is coloured. The coordinates in the summary show which end needs fixing. |
| Far too many lines are flagged | The units are probably wrong. Check `UNITS` → Insertion scale. |
| "flagged object(s) are on locked layers" | Locked objects are checked but not recoloured. Unlock the layer and run `GAPCHECK` again. |
| `install.bat` says "Copy failed" | AutoCAD is open and holding the DLL. Close it and run the script again. |

> **Status:** the geometry engine is unit-tested. CI compiles the plugin against Autodesk's API for AutoCAD 2019, 2025 and 2027, and tests both installers on the release zip. The plugin has not yet been run inside AutoCAD itself. Please report anything that doesn't work as described.
