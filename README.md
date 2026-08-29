# RUS ZIP

**A fast, cross-platform archive tool powered by Tar+Zstandard (`.zrus`).**

RUS ZIP is a compression suite for **Windows**, **macOS**, and **Linux**, built on .NET 10. It ships two tools built on one engine:

- **`rus-zip` CLI** — a single self-contained executable for terminals and scripts, with machine-readable `--json` output.
- **RUS ZIP Desktop** — a graphical app (Avalonia) with an archive browser, compression wizard, and dark/light themes.

No external tools (no `zstd`, `7z`, `unrar` binaries) need to be installed — everything runs in-process.

## Formats

| Direction | Formats |
| --- | --- |
| Compress & Decompress | `.zrus` (Tar+Zstd, the native format), `.tar.zstd`, `.tzstd`, `.zst`, `.zip` |
| Decompress Only | `.rar`, `.7z`, `.gz`, `.tar.gz` |

`.zrus` combines a Tar container (multi-file trees, POSIX permissions, timestamps) with Zstandard compression at levels 1–22, via friendly profiles: `fast` (3), `balanced` (9), `high` (15), `ultra` (22).

Extraction of every archive is guarded against zip-slip, decompression bombs (default 64 GB / 1,000,000 entry caps), and accidental overwrites.

---

## Install

### Windows — CLI + Desktop, on your `%PATH%`

**1. Run the installer (recommended).** Download and run:

```
rus-zip-1.0.2-setup.exe
```

It installs **both** the CLI (`rus-zip.exe`) and the Desktop app (`RusZip.Desktop.exe`), creates Start Menu entries, and — with *"Add rus-zip to my PATH (recommended)"* checked (the default) — adds the install directory (`%LOCALAPPDATA%\Programs\rus-zip`) to your user `%PATH%`.

Open a **new** terminal and verify:

```powershell
rus-zip --version
```

**2. Or install via WinGet** (portable layout; does not modify `%PATH%` — you invoke the executables by full path, or add the winget link folder to `%PATH%` yourself):

```powershell
winget install rus.zip
```

**3. Or use the portable bundle:** download `rus-zip-win-x64.zip`, extract anywhere, and add that folder to `%PATH%` if you want `rus-zip` globally:

```powershell
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";C:\path\to\rus-zip", "User")
```

### macOS — Homebrew (CLI and Desktop are installed differently)

Add the tap once, then trust it (recent Homebrew versions refuse to load third-party taps until trusted — you'll see `Refusing to load formula ... from untrusted tap` otherwise):

```bash
brew tap utarn/rus-zip
brew trust utarn/rus-zip
```

The package name is **`rus-zip`** (hyphenated — not `ruszip`).

**CLI** — installed by the Homebrew *formula* into `$(brew --prefix)/bin`:

```bash
brew install rus-zip        # or: brew install utarn/rus-zip/rus-zip
rus-zip --version
```

> If you get `Refusing to load formula utarn/rus-zip/rus-zip from untrusted tap`, run `brew trust utarn/rus-zip` and retry.

**Desktop** — installed by the Homebrew *cask* into `/Applications` (this is a GUI `.app`, so it is a cask, not a formula):

```bash
brew install --cask rus-zip # or: brew install --cask utarn/rus-zip/rus-zip
open -a "RUS ZIP"
```

> Same for the cask: `brew trust utarn/rus-zip` if Homebrew refuses the untrusted tap.

Upgrading later: `brew upgrade rus-zip` and `brew upgrade --cask rus-zip`.

**Direct download (no Homebrew):** grab `RusZip-mac-arm64.zip` (Apple Silicon) or `RusZip-mac-x64.zip` (Intel), unzip, and drag `RusZip.app` to `/Applications`. Builds are ad-hoc signed unless notarized, so on first launch right-click the app → **Open**, or run:

> `xattr -dr com.apple.quarantine "/Applications/RUS ZIP.app"`

A drag-to-Applications **`.dmg`** is also attached to some [releases](https://github.com/utarn/rus-zip/releases) when available; the `.zip` above works exactly the same — just unzip and drag.

### Linux — CLI only

Grab the single self-contained executable — no runtime, no dependencies:

```bash
curl -fsSL -o rus-zip https://github.com/utarn/rus-zip/releases/download/v1.0.2/rus-zip-cli-linux-x64
chmod +x rus-zip
sudo mv rus-zip /usr/local/bin/   # or ~/.local/bin
rus-zip --version
```

Verify integrity against `SHA256SUMS.txt` in the [release](https://github.com/utarn/rus-zip/releases/tag/v1.0.2):

```bash
sha256sum rus-zip   # compare with rus-zip-cli-linux-x64 in SHA256SUMS.txt
```

---

## All downloads

Every asset is a self-contained binary (single executable, `.exe` installer, or `.app` bundle) — no .NET runtime required. Checksums: [`SHA256SUMS.txt`](https://github.com/utarn/rus-zip/releases/download/v1.0.2/SHA256SUMS.txt).

| Platform | Asset | Contains |
| --- | --- | --- |
| Windows x64 | `rus-zip-1.0.2-setup.exe` | Installer: CLI + Desktop, Start Menu, PATH opt-in |
| Windows x64 | `rus-zip-win-x64.zip` | Portable: `rus-zip.exe` + `RusZip.Desktop.exe` |
| Windows x64 | `rus-zip-win-x64.exe` | CLI only, single file |
| Windows x64 | `RusZip.Desktop-win-x64.exe` | Desktop only, single file |
| macOS Apple Silicon | `rus-zip-cli-osx-arm64.tar.gz` | CLI only |
| macOS Intel | `rus-zip-cli-osx-x64.tar.gz` | CLI only |
| macOS Apple Silicon | `RusZip-mac-arm64.zip` | Desktop `.app` (drag to Applications) |
| macOS Intel | `RusZip-mac-x64.zip` | Desktop `.app` (drag to Applications) |
| Linux x64 | `rus-zip-cli-linux-x64` | CLI only, single file |
| Linux x64 | `rus-zip-linux-x64.tar.gz` | CLI + Desktop + icon (portable) |

---

## CLI quick start

```bash
rus-zip compress ./docs backup.zrus --profile high   # compress a folder
rus-zip list backup.zrus                             # list contents
rus-zip list backup.zrus --json                      # machine-readable
rus-zip extract backup.zrus -o ./restored            # extract
rus-zip --help
```

## Desktop app

- **Archive browser** — hierarchical view with size rollups, breadcrumbs, per-item and full-archive extraction.
- **Compression wizard** — pick sources, format (`.zrus` / `.zip`), profile, and level.
- **Themes** — System / Dark / Light.

## Also on

[WinGet](https://winget.run/pkg/rus/zip) (`winget install rus.zip`) · Flatpak (`flatpak install flathub com.ruszip.desktop`) · Snap (`snap install rus-zip`)

## License

[MIT](LICENSE)
