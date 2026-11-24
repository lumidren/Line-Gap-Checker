# Line Gap Checker installer for AutoCAD 2019 and newer (64-bit Windows).
#
# One line, in PowerShell (no admin rights needed):
#   irm https://raw.githubusercontent.com/lumidren/Line-Gap-Checker/main/install.ps1 | iex
#
# Saved as a file it takes options:
#   .\install.ps1 -Uninstall                      remove the plugin
#   .\install.ps1 -ZipPath .\LineGapChecker.zip   install a release zip you already have
#   .\install.ps1 -Target D:\Plugins              use another ApplicationPlugins folder
#
# It downloads the latest release zip from GitHub, copies LineGapChecker.bundle into
# %APPDATA%\Autodesk\ApplicationPlugins (AutoCAD loads every bundle there on start-up)
# and removes the Windows "downloaded file" block so AutoCAD accepts the DLL.

param(
    [string]$ZipPath,
    [string]$Target,
    [switch]$Uninstall
)

function Install-LineGapChecker {
    param([string]$ZipPath, [string]$Target, [switch]$Uninstall)

    $ErrorActionPreference = 'Stop'
    $url = 'https://github.com/lumidren/Line-Gap-Checker/releases/latest/download/LineGapChecker.zip'
    if (-not $Target) { $Target = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins' }
    $bundle = Join-Path $Target 'LineGapChecker.bundle'

    if (Get-Process -Name acad -ErrorAction SilentlyContinue) {
        Write-Host 'AutoCAD is running. Close it first, then run the installer again.' -ForegroundColor Yellow
        return
    }

    if ($Uninstall) {
        if (Test-Path -LiteralPath $bundle) {
            Remove-Item -LiteralPath $bundle -Recurse -Force
            Write-Host "Line Gap Checker removed from $Target" -ForegroundColor Green
        } else {
            Write-Host "Line Gap Checker is not installed in $Target"
        }
        return
    }

    $work = Join-Path ([IO.Path]::GetTempPath()) ('LineGapChecker-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        if ($ZipPath) {
            $zip = (Resolve-Path -LiteralPath $ZipPath).Path
        } else {
            # Windows PowerShell 5.1 may default to TLS 1.0, which GitHub refuses.
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            $zip = Join-Path $work 'LineGapChecker.zip'
            Write-Host "Downloading $url"
            $old = $ProgressPreference
            $ProgressPreference = 'SilentlyContinue'   # the progress bar makes 5.1 downloads very slow
            try { Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing }
            finally { $ProgressPreference = $old }
        }

        $unzipped = Join-Path $work 'unzipped'
        Expand-Archive -LiteralPath $zip -DestinationPath $unzipped
        $manifest = Get-ChildItem -LiteralPath $unzipped -Recurse -Filter PackageContents.xml |
            Where-Object { $_.Directory.Name -eq 'LineGapChecker.bundle' } | Select-Object -First 1
        if (-not $manifest) { throw "The zip has no LineGapChecker.bundle folder: $zip" }

        if (-not (Test-Path -LiteralPath $Target)) { New-Item -ItemType Directory -Path $Target | Out-Null }
        if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Recurse -Force }
        Copy-Item -LiteralPath $manifest.Directory.FullName -Destination $bundle -Recurse
        Get-ChildItem -LiteralPath $bundle -Recurse -File | Unblock-File

        $builds = (Get-ChildItem -LiteralPath (Join-Path $bundle 'Contents') -Directory | ForEach-Object Name) -join ', '
        $version = ([xml](Get-Content -LiteralPath (Join-Path $bundle 'PackageContents.xml') -Raw)).ApplicationPackage.AppVersion
        Write-Host ''
        Write-Host "Line Gap Checker $version installed to $bundle" -ForegroundColor Green
        Write-Host "Builds: $builds (AutoCAD picks the right one for its version)"
        Write-Host 'Start AutoCAD and type GAPCHECK. If it asks whether to load LineGapChecker.dll, choose "Always Load".'
    }
    catch {
        Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Install-LineGapChecker -ZipPath $ZipPath -Target $Target -Uninstall:$Uninstall
