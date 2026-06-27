# wstat — Windows Activity & Firefox Screen-Time Tracker

Lightweight, local-first Windows desktop app that tracks active screen time, monitors foreground applications, and captures Firefox tab URLs via a browser extension.

---

## Table of Contents

- [Quick Start](#quick-start)
- [User Guide](#user-guide)
  - [Running the App](#running-the-app)
  - [System Tray](#system-tray)
  - [Dashboard](#dashboard)
  - [Firefox Extension Setup](#firefox-extension-setup)
- [Developer Guide](#developer-guide)
  - [Prerequisites](#prerequisites)
  - [Building](#building)
  - [Project Architecture](#project-architecture)
  - [Extending the App](#extending-the-app)
- [Troubleshooting](#troubleshooting)

---

## Quick Start

1. **Build the app**
   ```powershell
   cd wstat
   dotnet build
   ```

2. **Run the desktop app**
   ```powershell
   dotnet run --project src\Wstat.Desktop
   ```
   Or launch `src\Wstat.Desktop\bin\Debug\net9.0-windows\Wstat.Desktop.exe`.

3. **Install the Firefox extension** (see [Firefox Extension Setup](#firefox-extension-setup))

Tracking starts immediately. The app minimizes to the system tray when you close the window.

---

## User Guide

### Running the App

```powershell
# From the repo root:
dotnet run --project src\Wstat.Desktop

# Or run the compiled exe directly:
.\src\Wstat.Desktop\bin\Debug\net9.0-windows\Wstat.Desktop.exe
```

On first launch, the app creates `%LOCALAPPDATA%\wstat\wstat.db` automatically.

### System Tray

- **Close window** → minimizes to tray (app keeps running)
- **Double-click tray icon** → restores the main window
- **Right-click tray icon** → menu with "Show Window" and "Quit"
- **Quit** → fully stops tracking and exits

### Dashboard

The main window has two tabs:

#### Applications tab
- Lists all foreground apps tracked today (or filtered period)
- Shows total time spent per application
- Columns: Application icon, Application name, Time spent
- Icons are extracted from each executable via the Windows API

#### Websites tab
- Lists URLs tracked via the Firefox extension
- Each URL shows its most recent page title
- Columns: URL, Page title, Visit count, Time spent

#### Date filters
Buttons at the top: **Today**, **Yesterday**, **Last 7 Days**, **Last 30 Days**

### Firefox Extension Setup

1. Open Firefox and navigate to `about:debugging`
2. Click **This Firefox** (left sidebar)
3. Click **Load Temporary Add-on…**
4. Browse to and select `src\Wstat.FirefoxExtension\manifest.json`
5. The extension loads immediately; no restart needed

The extension sends tab URL/title data to the desktop app on:
- Tab switch (`tabs.onActivated`)
- Page navigation / load complete (`tabs.onUpdated` with `status === "complete"`)
- Firefox window regaining focus (`windows.onFocusChanged`)

The extension silently tolerates the desktop app being closed. It retries on the next event.

> **Note:** Temporary add-ons are removed when Firefox closes. For permanent installation, you'll need to sign the extension via [addons.mozilla.org](https://addons.mozilla.org).

---

## Developer Guide

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (verify with `dotnet --version`)
- Windows 10 or later (Win32 API dependency)
- Firefox (for the browser extension feature)

### Building

```powershell
# Clean build
dotnet build

# Build in Release mode
dotnet build -c Release

# Run directly
dotnet run --project src\Wstat.Desktop

# Publish as self-contained exe
dotnet publish src\Wstat.Desktop -c Release -r win-x64 --self-contained true
```

### Project Architecture

```
src/
├── Wstat.Desktop/                  # .NET 9 WPF desktop application
│   ├── Native/                     # Win32 P/Invoke declarations
│   ├── Models/                     # Data models (ActivityRecord, DTOs)
│   ├── Services/                   # Core business logic
│   │   ├── DatabaseService.cs      # SQLite connection + queries
│   │   ├── WindowTrackerService.cs # 2s polling + idle detection
│   │   └── LocalHttpServer.cs      # HTTP listener for extension IPC
│   ├── ViewModels/                 # MVVM view models
│   ├── MainWindow.xaml             # Dashboard UI
│   └── App.xaml.cs                 # Startup, tray icon, lifecycle
└── Wstat.FirefoxExtension/         # Firefox MV3 extension
    ├── manifest.json
    └── background.js
```

#### Key design decisions

- **Polling, not hooks**: Uses a 2-second timer (`System.Timers.Timer`) rather than a Win32 event hook (`SetWinEventHook`). Simpler, lower risk, and CPU usage remains negligible (<1%).
- **Write-on-change**: Database writes only on window change or idle transition. Active records are updated in-place with running duration.
- **HTTP not WebSocket**: Simpler for a unidirectional data flow (extension → desktop app). No connection management overhead.
- **Manual DI**: No DI container — services are created and wired in `App.xaml.cs`. Keeps the project small and easy to understand.
- **WinForms NotifyIcon**: System tray uses `System.Windows.Forms.NotifyIcon` (not a third-party library) for reliable tray integration on all Windows versions.
- **App icons**: Icons are extracted via `Icon.ExtractAssociatedIcon()` and cached per process path. Displayed as 16×16 images in the Applications DataGrid.
- **DB schema evolution**: New columns are added via `ALTER TABLE` wrapped in a try/catch, so existing databases are migrated forward without data loss.

### Extending the App

#### Adding a new data source
1. Create a new service class in `Services/`
2. If it produces data to log, have it call `DatabaseService` methods
3. Wire it up in `App.xaml.cs`

#### Adding a dashboard chart
1. Add the chart NuGet package (e.g., `LiveChartsCore.SkiaSharpView.Wpf`)
2. Create a new property in `DashboardViewModel`
3. Bind it in `MainWindow.xaml`

#### Adding persistent settings
1. Create a `SettingsService` that reads/writes a JSON file or a settings table in SQLite
2. Inject it into services that need configuration

#### Adding startup-on-boot
On Windows, add a scheduled task or registry `Run` key:

```powershell
# Example: HKCU run key
$exePath = Resolve-Path "src\Wstat.Desktop\bin\Release\net9.0-windows\Wstat.Desktop.exe"
New-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "wstat" -Value $exePath.Path -PropertyType String -Force
```

### Testing database directly

The SQLite database is stored at:
```
%LOCALAPPDATA%\wstat\wstat.db
```
 
You can inspect it with any SQLite browser or `dotnet sqlite`:

```powershell
# Install the CLI tool
dotnet tool install --global dotnet-sqlite

# Query it
dotnet sqlite "$env:LOCALAPPDATA\wstat\wstat.db" "SELECT * FROM ActivityLog ORDER BY StartTime DESC LIMIT 10;"
```

---

## Troubleshooting

| Problem | Likely Cause | Fix |
|---------|-------------|-----|
| App won't start | Missing .NET runtime | Install .NET 9 runtime from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/9.0) |
| No data in dashboard | App just started | Interact with windows; data appears on next state change |
| Firefox URLs not tracked | Extension not loaded | Load it temporarily via `about:debugging` |
| Extension can't connect | Desktop app not running | Start the desktop app first; extension handles this gracefully |
| Port 12345 in use | Another app on that port | Kill the other process or change the port in `LocalHttpServer.cs` and `background.js` |
| DB file location | Default path | `%LOCALAPPDATA%\wstat\wstat.db` |
| Trace log | Debugging extension data | `%LOCALAPPDATA%\wstat\trace.log` — shows every received/skipped tab event |

### Checking the trace log

If websites aren't appearing, check the trace log for diagnostic info:

```powershell
type "$env:LOCALAPPDATA\wstat\trace.log"
```

The log shows every tab event the server receives, whether it was stored or skipped, and why. Example output:
```
2026-06-26T16:45:00.1234567 STORED: Example Site (https://example.com)
2026-06-26T16:45:02.4567890 SKIPPED: curr=firefox.exe, idle=False, url=https://other.com
```

### Cleaning old databases

If the schema has changed, delete the old database to recreate it:
```powershell
Remove-Item "$env:LOCALAPPDATA\wstat\wstat.db"
```

The app recreates it automatically on next launch with the latest schema.

## .gitignore

A `.gitignore` is provided at the project root. It excludes:
- `bin/`, `obj/` — .NET build output
- `.vs/` — Visual Studio user settings
- `*.user`, `*.suo` — per-user IDE files
- `packages/`, `*.nupkg` — NuGet artifacts
- `.DS_Store`, `Thumbs.db` — OS metadata files
