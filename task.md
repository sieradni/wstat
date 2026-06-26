# wstat — Windows Activity & Firefox Screen-Time Tracker

## Status: MVP Complete ✓

All three milestones are implemented:

| Milestone | Status |
|-----------|--------|
| **M1: Win32 Tracker & Database** | ✅ Done |
| **M2: Firefox Extension & API Listener** | ✅ Done |
| **M3: UI & System Tray Integration** | ✅ Done |

## Project Structure

```
wstat/
├── Wstat.sln
├── task.md
├── README.md
├── src/
│   ├── Wstat.Desktop/                    # .NET 9 WPF app
│   │   ├── Native/
│   │   │   └── Win32Api.cs               # P/Invoke: GetForegroundWindow, etc.
│   │   ├── Models/
│   │   │   └── ActivityRecord.cs          # DB models + summary DTOs
│   │   ├── Services/
│   │   │   ├── DatabaseService.cs         # SQLite CRUD + queries
│   │   │   ├── WindowTrackerService.cs    # 2s polling + idle detection
│   │   │   └── LocalHttpServer.cs         # HTTP listener on :12345
│   │   ├── ViewModels/
│   │   │   ├── DashboardViewModel.cs      # MVVM dashboard logic
│   │   │   └── RelayCommand.cs            # ICommand helper
│   │   ├── MainWindow.xaml / .cs          # Dashboard UI
│   │   └── App.xaml / .cs                 # Startup + tray icon
│   └── Wstat.FirefoxExtension/           # Firefox MV3 extension
│       ├── manifest.json
│       ├── background.js
│       └── icons/
```

## Implementation Notes

### Win32 API Usage (`Native/Win32Api.cs`)
- `GetForegroundWindow` → active window handle
- `GetWindowText` → window title
- `GetWindowThreadProcessId` + `QueryFullProcessImageName` → full exe path
- `GetLastInputInfo` → idle detection (5-min threshold)

### Data Flow
1. `WindowTrackerService` polls every 2s on background thread
2. On window change or idle transition, writes to SQLite via `DatabaseService`
3. `LocalHttpServer` listens on `127.0.0.1:12345` for Firefox tab data
4. Firefox extension POSTs `{url, title}` on tab change
5. Tab data is attached to the current active record when Firefox is foreground

### Database Schema (`ActivityLog`)
| Column | Type | Notes |
|--------|------|-------|
| Id | INTEGER PK | Auto-increment |
| AppName | TEXT | e.g. `firefox.exe` |
| WindowTitle | TEXT | Captured or extension title |
| BrowserUrl | TEXT | Nullable, from extension |
| StartTime | TEXT | ISO 8601 |
| EndTime | TEXT | Nullable (null = still active) |
| DurationSeconds | INTEGER | Updated every tick while active |

## Build

```powershell
dotnet build
```

Output: `src\Wstat.Desktop\bin\Debug\net9.0-windows\Wstat.Desktop.exe`

## Remaining / Future Work

- Startup-on-boot registration (scheduled task or run registry key)
- Settings persistence (config file or DB)
- Charts / graphs in dashboard
- Export data to CSV
- Installer (WiX or MSIX)
- Per-app URL blacklist/whitelist
- Graceful handling of daylight saving / sleep-resume edge cases
