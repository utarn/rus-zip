# Design Spec: Modern UI Overhaul & Cross-Platform Scripts (macOS osx-arm64 & Windows win-x64)

## 1. Overview & Goals

This specification outlines the modernization of the `rus-zip` Avalonia 11 desktop user interface and the delivery of targeted cross-platform run, build, publish, and install scripts for **macOS (`osx-arm64`)** and **Windows (`win-x64`)**.

### Goals
1. **Modern Fluent 2 & Apple Human Interface Styling**:
   - Seamless extended client area window styling (`ExtendClientAreaToDecorationsHint`) supporting Mica/Acrylic blur backdrops.
   - macOS traffic-light margin auto-inset and Windows custom title draggable header.
   - Crisp SVG vector path icon library (`VectorIcons.axaml`), replacing raw emojis with high-DPI resolution-independent glyphs.
   - Dynamic **Dark / Light / System** theme switcher toggle.
2. **Enhanced Archive Browser & Interactivity**:
   - Breadcrumb / path navigation for nested folder structures.
   - Extension-aware file type icons (Code, Documents, Images, Archives, Binary).
   - Right-click context menus on entries (Extract Selected, Copy Path, View Properties).
   - Modernized compression preset cards and animated drag-and-drop empty state.
3. **Cross-Platform Script Suite (Targeting `osx-arm64` and `win-x64`)**:
   - `run.sh`: Unified runner on macOS/Linux for GUI, CLI, and Test execution.
   - `run.ps1` / `run.bat`: Unified runner on Windows PowerShell and Command Prompt.
   - `scripts/publish.sh`: Automated `osx-arm64` self-contained single-file publisher + macOS `.app` bundle generator with `Info.plist`.
   - `scripts/publish.ps1`: Automated `win-x64` self-contained single-file publisher.
   - `scripts/install.sh` / `scripts/install.ps1`: One-command CLI installers to user's system PATH.

### Non-Goals
- Adding complex third-party UI framework packages (e.g. SukiUI or FluentAvalonia) that bloat dependencies.
- Targeting 32-bit platforms or legacy .NET versions.

---

## 2. User Interface Modernization Architecture

### 2.1 Window Chrome & Layout Architecture
- **Window Configuration (`MainWindow.axaml`)**:
  - `ExtendClientAreaToDecorationsHint="True"`
  - `ExtendClientAreaTitleBarHeightHint="36"`
  - `ExtendClientAreaChromeHints="PreferSystemChrome"`
  - `TransparencyLevelHint="Mica, AcrylicBlur, Blur"`
  - `Background="Transparent"`
  - `Icon="avares://RusZip.Desktop/Assets/rus-zip.ico"`
- **Compact Icon-Only Toolbar & Overflow**:
  - Title-bar toolbar is a compact ~36px strip of icon-only buttons with tooltips (New Archive, Open Archive, Extract All, Add to Archive, Delete Selected, Close, Settings, Theme Switcher).
  - Trailing buttons collapse into an overflow (⋯) menu flyout in narrow windows (< 780px).
- **macOS vs Windows Title Bar Handling**:
  - Platform detection in `MainWindow.axaml.cs` (`OperatingSystem.IsMacOS()` vs `OperatingSystem.IsWindows()`):
    - On macOS: Left titlebar margin is offset by 76px to prevent overlapping standard macOS window traffic lights. The in-window application menu bar is hidden (`IsVisible = false`), using screen-top `NativeMenu` exclusively.
    - On Windows/Linux: Themed in-window menu bar renders below the title bar. Drag region allows window dragging while leaving interactive buttons clickable.
- **Theme Variant Management (`App.axaml.cs` & `MainWindowViewModel.cs`)**:
  - `MainWindowViewModel` exposes `CurrentTheme` property (`System`, `Dark`, `Light`) and `ToggleThemeCommand`.
  - `App.SetTheme(ThemeVariant)` dynamically changes `Application.Current.RequestedThemeVariant`.

### 2.2 Vector Icon Resource Library (`src/RusZip.Desktop/Styles/VectorIcons.axaml`)
A centralized Avalonia `ResourceDictionary` containing `StreamGeometry` definitions for all UI elements:
- `Icon.NewArchive`: Box / package SVG path.
- `Icon.OpenFolder`: Folder open SVG path.
- `Icon.Extract`: Download / unpack arrow SVG path.
- `Icon.Close`: Dismiss cross SVG path.
- `Icon.Search`: Magnifying glass SVG path.
- `Icon.Clear`: Circle-x SVG path.
- `Icon.ThemeLight`: Sun SVG path.
- `Icon.ThemeDark`: Moon SVG path.
- `Icon.Folder`: Standard directory SVG path.
- `Icon.FileCode`: File with code brackets SVG path.
- `Icon.FileDoc`: File with text lines SVG path.
- `Icon.FileImage`: Image picture frame SVG path.
- `Icon.FileArchive`: Archive zipped file SVG path.
- `Icon.FileGeneric`: Generic clean file SVG path.
- `Icon.ExpandAll` & `Icon.CollapseAll`: Tree expand/collapse SVG paths.

### 2.3 Archive Browser & Interactivity Overhaul (`ArchiveBrowserView.axaml`)
- **Breadcrumb Navigation**:
  - Clickable breadcrumb path bar indicating navigation position in the archive (e.g. `Archive > folder > subfolder`).
- **File Type Resolvers (`ArchiveItemViewModel.cs`)**:
  - Added `IconKey` property computing the appropriate `StreamGeometry` resource key based on entry extension:
    - Code: `.cs`, `.json`, `.xml`, `.yaml`, `.yml`, `.js`, `.ts`, `.py`, `.sh`, `.cpp`, `.h`, `.html`, `.css`
    - Document: `.txt`, `.md`, `.pdf`, `.doc`, `.docx`, `.rtf`, `.log`
    - Image: `.png`, `.jpg`, `.jpeg`, `.svg`, `.ico`, `.webp`, `.bmp`, `.gif`
    - Archive: `.zip`, `.zrus`, `.tar`, `.gz`, `.tgz`, `.7z`, `.rar`, `.bz2`
    - Generic: default fallback.
- **Context Menu (ProDataGrid / Row ContextMenu)**:
  - Context menu on archive items:
    - `Extract Item...` -> triggers single-item / subfolder extraction request.
    - `Copy Path` -> copies item relative path to system clipboard.
- **Elevated Empty State**:
  - Card with dashed border accent, vector package glyph, and quick action chips.

### 2.4 Compression Settings & Progress Overlay
- **Segmented Preset Selectors**:
  - Replaces raw buttons with modern segmented radio/pill buttons for `Fast (3)`, `Balanced (9)`, `High (15)`, and `Ultra (22)` with dynamic visual badges.
- **Progress Modal**:
  - Rounded floating modal card with real-time throughput meter (`MB/s`) and remaining duration estimate (`ETA`).

---

## 3. Cross-Platform Script Suite Architecture

Targeting **macOS (`osx-arm64`)** and **Windows (`win-x64`)**:

### 3.1 Root Run Scripts

#### `run.sh` (macOS / Linux)
```bash
#!/usr/bin/env bash
# rus-zip runner script for macOS and Linux
# Usage:
#   ./run.sh desktop        # Run Avalonia Desktop App
#   ./run.sh cli [args...]  # Run RusZip CLI
#   ./run.sh test           # Run all unit and integration tests
#   ./run.sh build          # Build entire solution
```
- Validates `dotnet` CLI is installed and .NET 10 SDK is available.
- Forwards arguments seamlessly to `dotnet run --project src/RusZip.Desktop` or `src/RusZip.Cli`.

#### `run.ps1` & `run.bat` (Windows PowerShell / CMD)
```powershell
# rus-zip runner script for Windows
# Usage:
#   .\run.ps1 desktop
#   .\run.ps1 cli [args...]
#   .\run.ps1 test
#   .\run.ps1 build
```
- PowerShell 5.1 & PowerShell 7+ compatible.
- `run.bat` allows double-click execution or quick Command Prompt invocations.

---

### 3.2 Automated Publishing Scripts (`scripts/`)

#### `scripts/publish.sh` (macOS `osx-arm64`)
- Publishes self-contained, single-file native binaries for **Apple Silicon (`osx-arm64`)**:
  - CLI binary: `dist/osx-arm64/rus-zip`
  - Desktop GUI App Bundle: `dist/osx-arm64/RusZip.app`
- Creates complete macOS bundle structure:
  ```
  dist/osx-arm64/RusZip.app/
  └── Contents/
      ├── Info.plist
      ├── MacOS/
      │   └── RusZip (executable)
      └── Resources/
  ```
- Generates `Info.plist` with:
  - `CFBundleExecutable`: `RusZip`
  - `CFBundleIconFile`: `RusZip.icns`
  - `CFBundleIdentifier`: `com.ruszip.desktop`
  - `CFBundleName`: `RUS ZIP`
  - `CFBundleDisplayName`: `RUS ZIP`
  - `CFBundleVersion`: `1.0.0`
  - `CFBundlePackageType`: `APPL`
  - `NSHighResolutionCapable`: `true`
  - `NSSupportsAutomaticGraphicsSwitching`: `true`
- Marks executables as executable (`chmod +x`).

#### `scripts/publish.ps1` (Windows `win-x64`)
- Publishes self-contained, single-file native binaries for **Windows x64 (`win-x64`)**:
  - CLI executable: `dist/win-x64/rus-zip.exe`
  - Desktop executable: `dist/win-x64/RusZip.Desktop.exe`
- Uses `PublishSingleFile=true`, `SelfContained=true`, and `IncludeNativeLibrariesForSelfExtract=true`.

---

### 3.3 Installers (`scripts/`)

#### `scripts/install.sh` (macOS `osx-arm64`)
- Publishes or copies `rus-zip` CLI binary to `$HOME/.local/bin` (or `/usr/local/bin` if writable).
- Ensures directory is present and outputs verification instruction.

#### `scripts/install.ps1` (Windows `win-x64`)
- Publishes or copies `rus-zip.exe` CLI to `$env:LOCALAPPDATA\Programs\rus-zip`.
- Automatically registers `$env:LOCALAPPDATA\Programs\rus-zip` in the Windows User `PATH` environment variable if not already present.

---

## 4. Testing & Verification Plan

1. **Unit & Desktop ViewModel Tests**:
   - Update and expand `tests/RusZip.Desktop.Tests` to verify:
     - Theme switching logic (`ThemeMode`, `ToggleThemeCommand`).
     - File icon resolution (`IconKey` based on file extensions).
     - Context menu command bindings.
   - Run `dotnet test` ensuring 100% of all tests in `RusZip.Core.Tests`, `RusZip.Cli.Tests`, and `RusZip.Desktop.Tests` pass.
2. **Script Verification**:
   - Verify `run.sh` with `./run.sh test`, `./run.sh build`, and `./run.sh cli --help`.
   - Verify `scripts/publish.sh` builds clean self-contained `osx-arm64` outputs and `.app` bundle structure.
   - Verify syntax and logic for `run.ps1`, `run.bat`, `scripts/publish.ps1`, and `scripts/install.ps1`.
