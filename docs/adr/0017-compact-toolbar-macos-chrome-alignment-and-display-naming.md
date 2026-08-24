# 0017: Compact Toolbar, macOS Chrome Alignment, and Display Naming

## Status

Accepted (Supersedes toolbar, menu-row, and naming portions of [ADR 0013](0013-desktop-menu-navigation-and-interface-architecture.md))

## Context

Following the desktop navigation and window chrome modernization, several inconsistencies and opportunities for UI refinement emerged:
1. **Title-Bar Toolbar Footprint**: The previous toolbar used icon-plus-text buttons spanning a 46px strip. This consumed excessive vertical height that rightfully belongs to the archive content pane.
2. **macOS Duplicate Menu Row**: On macOS, both the native screen-top menu bar (`NativeMenu`) and the in-window themed menu bar rendered simultaneously. This duplicated all six top-level menus and violated Apple Human Interface Guidelines, diverging from ADR 0013's stated intent (macOS → NativeMenu only).
3. **Display Name Fragmentation**: The application display name was fragmented across "RusZip", "rus-zip", and "RusZip.Desktop" across window titles, dialogs, menus, bundle plists, and OS registration strings.
4. **App Icon & Attribution Gap**: The desktop application lacked a committed platform app icon (showing generic blank tiles in docks and taskbars) and lacked third-party open-source attribution for embedded Material Design Icons glyphs.

## Decision

1. **Compact Icon-Only Toolbar with Overflow**:
   - Shrink the title-bar toolbar to a compact ~36px strip (`ExtendClientAreaTitleBarHeightHint="36"`).
   - Render all eight primary and utility actions as icon-only buttons with descriptive tooltips:
     - `New Archive` (`Ctrl+N`)
     - `Open Archive` (`Ctrl+O`)
     - `Extract All` (`Ctrl+E`)
     - `Add to Archive` (`Ctrl+Shift+A`)
     - `Delete Selected` (`Delete`)
     - `Close Archive` (`Ctrl+W`)
     - `Settings` (`Ctrl+,`)
     - `Theme Switcher` (tooltip reports active mode: `System`, `Dark`, or `Light`; clicking cycles modes)
   - When the window width is narrow (< 780px), collapse trailing actions into an overflow (⋯) menu flyout presenting the same full action list without wrapping or horizontal scrolling.
   - Explicitly reject tabbed ribbons or multi-tier toolbar layouts to maintain focus and maximize vertical data density.

2. **macOS Chrome & Menu-Row Alignment**:
   - On macOS, hide the in-window application menu bar entirely (`IsVisible = false` via platform chrome detection), relying exclusively on macOS's screen-top `NativeMenu`.
   - On Windows and Linux, continue rendering the themed in-window menu row below the title bar.
   - Maintain full accelerator and command routing across all platforms.

3. **Unified "RUS ZIP" Display Naming Policy**:
   - Centralize display branding in `AppBranding` (`DisplayName = "RUS ZIP"`).
   - Standardize main window title to `"RUS ZIP - Compression Suite"`.
   - Standardize all dialogs and quick-extract window titles to end with `"- RUS ZIP"`.
   - Standardize About menu entries to `"About RUS ZIP"`.
   - Set macOS bundle metadata to `CFBundleName = "RUS ZIP"` and `CFBundleDisplayName = "RUS ZIP"`.
   - Standardize file association friendly names (Windows ProgIDs and Linux desktop entries) to use `"RUS ZIP"`.
   - Preserve technical identifiers (assembly names `RusZip.*`, bundle ID `com.ruszip.desktop`, CLI binary `rus-zip`, domain glossary) unchanged.

4. **App Icon and Third-Party Notices**:
   - Standardize on a white Material archive-family glyph centered on an indigo rounded tile.
   - Ship macOS icon set (16→1024px, including 2× variants), `.icns`, and Windows `.ico` (16–256px) in `src/RusZip.Desktop/Assets/`.
   - Wire application icons into `RusZip.Desktop.csproj` (`<ApplicationIcon>`) and macOS publish bundles (`CFBundleIconFile`).
   - Introduce root-level `THIRD-PARTY-NOTICES.md` crediting Google Material Design Icons under Apache License 2.0.

## Consequences

- **Positive**:
  - Reclaims ~10px of vertical space for the archive data grid across all platforms.
  - Eliminates duplicate menu rows on macOS while preserving native screen-top menus.
  - Establishes a consistent, professional brand identity ("RUS ZIP") across all user-facing surfaces.
  - Provides crisp dock/taskbar icons across macOS and Windows.
  - Closes open-source license attribution requirements.
- **Negative / Tradeoffs**:
  - Icon-only buttons rely on tooltips for discoverability (mitigated by established universal archive icons and keyboard accelerators).
