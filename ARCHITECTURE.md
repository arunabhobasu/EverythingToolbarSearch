# Architecture Overview

## What the Application Does

**Everything Quick Search** replaces the Windows Search bar with a fast, full-featured overlay powered by the [Everything](https://www.voidtools.com) search engine.  When a user opens Windows Search, this app intercepts the key-presses, shows its own WPF overlay next to the taskbar, and delegates all file-system queries to the Everything service via IPC.

---

## Technology Stack

| Layer | Technology |
|---|---|
| UI | WPF (.NET 8), [WPF-UI 4.2.0](https://github.com/lepoco/wpfui) (Fluent Design) |
| Language | C# 12 / .NET 8 (`net8.0-windows10.0.26100.0`) |
| Search engine | [Everything 1.4.1+](https://www.voidtools.com) via `Everything64.dll` P/Invoke |
| Global hotkeys | [MouseKeyHook 5.7.1](https://github.com/gmamaladze/globalmousekeyhook) |
| Thumbnails | `Microsoft-WindowsAPICodePack-Shell` + `Svg 3.4.7` |
| Persistence | Windows Registry (`HKCU\SOFTWARE\EverythingQuickSearch`) |
| Autorun | Windows Registry (`HKCU\...\Run`) |

**Minimum OS requirement:** Windows 10 Build 19041 (20H1).

---

## Directory Structure

```
EverythingQuickSearch/
├── App.xaml / App.xaml.cs          # Application entry point; suppresses noisy data-binding traces
├── MainWindow.xaml / .cs           # Primary UI & orchestration (≈1,970 lines)
├── SettingsWindow.xaml / .cs       # Background-style settings dialog
├── AssemblyInfo.cs                 # Assembly metadata
├── app.manifest                    # Windows app manifest (DPI awareness, UAC)
│
├── Core/
│   ├── FileItem.cs                 # WPF-bindable model for a single search result
│   ├── SearchCategory.cs           # Category enum + Everything query-prefix builder
│   └── Settings.cs                 # INotifyPropertyChanged settings backed by Registry
│
├── Everything/
│   ├── Everything.cs               # Low-level P/Invoke declarations (sync API, reserved for future use)
│   └── EverythingService.cs        # Async IPC wrapper; dispatches results via Windows messages
│
├── Converters/
│   └── BooleanToVisibilityConverter.cs
│
├── Util/
│   ├── EverythingInstaller.cs      # Detect, install, and start the Everything service
│   ├── RegistryHelper.cs           # Read/write HKCU registry keys; autorun management
│   └── ThumbnailGenerator.cs       # Async shell thumbnail & SVG rendering
│
├── Properties/
│   ├── Lang.resx                   # Localisation strings
│   └── Lang.Designer.cs            # Auto-generated strongly-typed resource accessor
│
├── Icon/
│   ├── icon.ico
│   └── icon2.ico
│
├── Installer/
│   └── Everything-Setup.exe        # Bundled Everything 1.4.1.1032 x64 silent installer (NSIS, /S flag)
│
└── Everything64.dll                # Bundled Everything SDK (copied to output dir)
```

---

## Key Components

### `EverythingInstaller` (Util/EverythingInstaller.cs)

Static helper that manages the Everything service lifecycle:
- **`IsEverythingRunning()`** — checks for a live `Everything` process.
- **`IsEverythingInstalled()`** — checks the registry (`HKLM\SOFTWARE\voidtools\Everything`) and common install paths.
- **`InstallEverythingAsync(installerPath)`** — runs `Everything-Setup.exe /S` with UAC elevation and awaits completion.
- **`StartEverythingServiceAsync()`** — launches the installed `Everything.exe -startup` and polls until the process is live.
- **`EnsureEverythingReadyAsync(installerPath)`** — single entry-point that detects, installs, and starts Everything in sequence.

---

### `MainWindow` (MainWindow.xaml.cs)

The central class — it wires everything together.

**Responsibilities:**
- Registers a global keyboard hook (via `MouseKeyHook`) to detect Windows Search activation.
- Monitors foreground-window changes (`SetWinEventHook`) to discover the Windows Search host window (`SearchHost.exe`) position and size.
- Positions and shows/hides the overlay to align with the search bar.
- Drives paginated file and application search via `EverythingService`.
- Manages the category-filter bar (ALT+1–8) and the regex toggle (ALT+R).
- Loads UWP app entries from `HKCU\...\PackageRepository` for app search (cached; refreshed at most every 5 minutes).
- Generates thumbnails asynchronously, bounded by `_thumbnailSemaphore` (concurrency = CPU count − 1).
- Synchronises the WPF theme with the Windows system theme at runtime.
- Unregisters the `WinEventHook` and disposes `EverythingService` on window close.

**Pagination:** results are fetched in pages of 30 (`PageSize`). Scroll-to-bottom triggers `LoadNextFilePageAsync` / `LoadNextAppPageAsync` for infinite scroll.

**Keyboard shortcuts** handled in `FluentWindow_PreviewKeyDown`:

| Key | Action |
|---|---|
| ALT + 1–8 | Switch category |
| ALT + 0 | "All" category |
| ALT + W / K, ↑ | Move selection up |
| ALT + S / J, ↓ | Move selection down |
| ALT + A / H, ← | Move selection left (app list → file list navigation) |
| ALT + D / L, → | Move selection right (file list → app list navigation) |
| ALT + R | Toggle regex |
| ENTER | Open selected item |
| ESC | Close overlay |

---

### `EverythingService` (Everything/EverythingService.cs)

Async wrapper around the synchronous Everything SDK.

- Uses a `HwndSource` hook on the main window to receive `WM_*` reply messages from Everything.
- Each call to `SearchAsync` is given a unique reply-ID so concurrent queries can be demultiplexed correctly.
- A `SemaphoreSlim(1,1)` serialises concurrent `SearchAsync` callers to prevent interleaved global state writes to the Everything SDK.
- Results are materialised into `List<FileItem>` (pre-sized to the result count) and delivered via `TaskCompletionSource`.
- Path buffer is sized to 32767 characters (Everything long-path limit).
- `CancellationToken` support: in-flight queries are cleaned up on cancellation.
- Properly disposes `HwndSource` and the semaphore on `Dispose()`.

---

### `SearchCategory` / `FileTypes` (Core/SearchCategory.cs)

- `Category` enum: Image, Document, Audio, Video, Executable, Compressed, File, Folder, All.
- `SearchCategory.GetExtensions(category)` returns the Everything query prefix (e.g. `"ext:jpg;png;... "`).
- `FileTypes` holds the static extension arrays per category.

---

### `Settings` (Core/Settings.cs)

- `INotifyPropertyChanged` class; each setter persists its new value to the registry via `RegistryHelper`.
- Currently exposes one setting: `TransparentBackground`.

---

### `ThumbnailGenerator` (Util/ThumbnailGenerator.cs)

- Runs on a background thread, concurrency bounded by `_thumbnailSemaphore` in `MainWindow`.
- Delegates to `ShellObject.Thumbnail` (Windows Shell API) for most file types.
- Has a dedicated SVG rendering path via `Svg.SvgDocument`.
- Falls back to the system "unknown document" stock icon on failure.

---

### `RegistryHelper` (Util/RegistryHelper.cs)

- Thin wrapper around `Microsoft.Win32.Registry`.
- Stores app settings under `HKCU\SOFTWARE\EverythingQuickSearch`.
- Manages the autorun entry under `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`.

---

## Data Flow: Search Request

```
User types in Windows Search
        │
        ▼
GlobalHookKeyPress (MouseKeyHook)
        │ forwards key to EQS overlay
        ▼
MainWindow.SearchBarTextBox_TextChanged
        │ cancels previous CancellationTokenSource
        │ builds query: categoryPrefix + userText
        ▼
EverythingService.SearchAsync
        │ acquires SemaphoreSlim(1,1)
        │ calls Everything64.dll (async/IPC, bWait=false)
        ▼
WndProc receives WM reply
        │ ProcessResults → List<FileItem> (pre-sized)
        ▼
MainWindow populates FileItems / AppItems
        │ ObservableCollection bound to ListViews
        ▼
ThumbnailGenerator (background threads, _thumbnailSemaphore bounded)
        │ loads thumbnails lazily
        ▼
UI updates via INotifyPropertyChanged
```

---

## Build & Run

**Prerequisites:**
- .NET 8 SDK
- Windows 10 Build 19041+
- Everything Search is **bundled** and installed automatically on first run — no manual setup needed

```bash
dotnet build EverythingQuickSearch/EverythingQuickSearch.csproj
dotnet run   --project EverythingQuickSearch/EverythingQuickSearch.csproj
```

`Everything64.dll` and `Installer/Everything-Setup.exe` are bundled in the project and copied to the output directory automatically.
On first launch the app checks whether Everything is installed and running; if not, a WPF-UI dialog prompts the user to install it silently (UAC elevation is requested automatically).

Release builds use `<Optimize>true</Optimize>` for smaller, faster output.

---

## Known Limitations / TODOs

| # | Area | Description | Status |
|---|---|---|---|
| A | `SearchBarTextBox_TextChanged` | No search debounce — fires on every keystroke; add ~150 ms debounce to reduce Everything IPC calls on fast typing | Open |
| B | `ThumbnailGenerator` | No thumbnail memory cache — identical thumbnails are re-loaded across searches; add `ConcurrentDictionary<string, BitmapSource>` cache | Open |
| C | `App.xaml.cs` | No single-instance enforcement — launching the app twice creates two overlays | Open |
| D | Localisation | New install/start messages are hardcoded English strings; other strings use `Lang.resx` | Open |
| E | UI | No right-click context menu on result items (Open, Copy path, Properties, etc.) | Open |
| F | `Settings` | Only `TransparentBackground` is user-configurable; expose `PageSize`, default sort, regex default, window opacity | Open |
| G | Search | No search history / recent items shown when overlay opens with an empty query | Open |
| H | Accessibility | Icon and filter buttons lack `AutomationProperties.Name`; screen readers cannot identify them | Open |
| I | `EverythingInstaller` | The 10-second Everything IPC poll timeout is a hardcoded magic number; should be a named constant | Open |
| J | Testing | No unit tests exist; `SearchCategory`, `RegistryHelper`, `EverythingInstaller`, and `Settings` are all pure logic amenable to unit testing | Open |
| K | `EverythingService` | No Everything version check after connecting; should warn if version &lt; 1.4.1 | Open |
| L | `MainWindow` | `_setSortAscending` field is declared but never wired to the sort direction passed to `SearchAsync` | Open |

