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
├── MainWindow.xaml / .cs           # Primary UI & orchestration (≈1,950 lines)
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
│   ├── Everything.cs               # Low-level P/Invoke declarations (sync API)
│   └── EverythingService.cs        # Async IPC wrapper; dispatches results via Windows messages
│
├── Converters/
│   └── BooleanToVisibilityConverter.cs
│
├── Util/
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
└── Everything64.dll                # Bundled Everything SDK (copied to output dir)
```

---

## Key Components

### `MainWindow` (MainWindow.xaml.cs)

The central class — it wires everything together.

**Responsibilities:**
- Registers a global keyboard hook (via `MouseKeyHook`) to detect Windows Search activation.
- Monitors foreground-window changes (`SetWinEventHook`) to discover the Windows Search host window (`SearchHost.exe`) position and size.
- Positions and shows/hides the overlay to align with the search bar.
- Drives paginated file and application search via `EverythingService`.
- Manages the category-filter bar (ALT+1–8) and the regex toggle (ALT+R).
- Loads UWP app entries from `HKCU\...\PackageRepository` for app search.
- Generates thumbnails asynchronously with a semaphore-bounded concurrency limit.
- Synchronises the WPF theme with the Windows system theme at runtime.

**Pagination:** results are fetched in pages of 30 (`PageSize`). Scroll-to-bottom triggers `LoadNextFilePageAsync` / `LoadNextAppPageAsync` for infinite scroll.

**Keyboard shortcuts** handled in `FluentWindow_PreviewKeyDown`:

| Key | Action |
|---|---|
| ALT + 1–8 | Switch category |
| ALT + 0 | "All" category |
| ALT + W / K, ↑ | Move selection up |
| ALT + S / J, ↓ | Move selection down |
| ALT + R | Toggle regex |
| ENTER | Open selected item |
| ESC | Close overlay |

---

### `EverythingService` (Everything/EverythingService.cs)

Async wrapper around the synchronous Everything SDK.

- Uses a `HwndSource` hook on the main window to receive `WM_*` reply messages from Everything.
- Each call to `SearchAsync` is given a unique reply-ID so concurrent queries can be demultiplexed correctly.
- Results are materialised into `List<FileItem>` and delivered via `TaskCompletionSource`.

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

- Runs on a background thread (bounded by `_thumbnailSemaphore` in `MainWindow`).
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
        │ calls Everything64.dll (async/IPC)
        ▼
WndProc receives WM reply
        │ ProcessResults → List<FileItem>
        ▼
MainWindow populates FileItems / AppItems
        │ ObservableCollection bound to ListViews
        ▼
ThumbnailGenerator (background threads)
        │ loads thumbnails lazily
        ▼
UI updates via INotifyPropertyChanged
```

---

## Known Limitations / TODOs

| Location | Issue |
|---|---|
| `MainWindow.xaml.cs` ≈ line 448 | `TODO: When EQS is window too small` – category buttons may be hidden |
| `MainWindow.xaml.cs` ≈ line 1650 | `TODO: add left/right` – left/right arrow key navigation not implemented |
| `MainWindow.xaml.cs` | Class is ~1,950 lines; candidates for extraction include: thumbnail loading, UWP-app loading, theme management, and keyboard-shortcut handling |
| General | No unit tests; manual testing only |
| `EverythingService` | Concurrent `SearchAsync` calls share a single Everything query state – callers must serialise or cancel before calling again |

---

## Build & Run

**Prerequisites:**
- .NET 8 SDK
- Windows 10 Build 19041+
- [Everything 1.4.1+](https://www.voidtools.com/downloads/) running as a service

```bash
dotnet build EverythingQuickSearch/EverythingQuickSearch.csproj
dotnet run   --project EverythingQuickSearch/EverythingQuickSearch.csproj
```

`Everything64.dll` is bundled in the project and copied to the output directory automatically.
