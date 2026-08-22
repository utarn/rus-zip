#!/usr/bin/env bash
# rus-zip one-command CLI PATH installer for macOS and Linux
# Usage:
#   ./scripts/install.sh                  # Install to $HOME/.local/bin (or /usr/local/bin if root)
#   ./scripts/install.sh --dir /usr/local/bin
#   ./scripts/install.sh --rid osx-arm64
#   ./scripts/install.sh --uninstall      # Remove rus-zip and its backup
#   ./scripts/install.sh --help
#
# NOTE: Full Windows/macOS runtime verification of these installers is out of
# scope for the #54 audit (no Windows/macOS test machines were available). The
# Linux logic paths are exercised; PowerShell/macOS specifics are parse-checked
# only.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# Detect OS and architecture
detect_rid() {
    local os
    local arch
    os="$(uname -s 2>/dev/null || echo "Unknown")"
    arch="$(uname -m 2>/dev/null || echo "Unknown")"

    case "$os" in
        Darwin)
            case "$arch" in
                arm64|aarch64) echo "osx-arm64" ;;
                x86_64|amd64)  echo "osx-x64" ;;
                *)              echo "osx-arm64" ;;
            esac
            ;;
        Linux)
            case "$arch" in
                x86_64|amd64)   echo "linux-x64" ;;
                aarch64|arm64)  echo "linux-arm64" ;;
                armv7l|armhf)   echo "linux-arm" ;;
                *)              echo "linux-x64" ;;
            esac
            ;;
        *)
            echo "linux-x64"
            ;;
    esac
}

RID="$(detect_rid)"
CONFIGURATION="Release"
FORCE_BUILD=false
CUSTOM_INSTALL_DIR=""
DO_UNINSTALL=false

# Determine default install directory
if [ "${EUID:-$(id -u)}" -eq 0 ]; then
    DEFAULT_INSTALL_DIR="/usr/local/bin"
else
    DEFAULT_INSTALL_DIR="${HOME}/.local/bin"
fi

print_usage() {
    cat << 'EOF'
rus-zip CLI Installer for macOS and Linux

Usage:
  ./scripts/install.sh [options]

Options:
  -d, --dir <DIR>         Target install directory (default: $HOME/.local/bin, or /usr/local/bin if root)
  -p, --prefix <DIR>      Alias for --dir
  -r, --rid <RID>         Runtime Identifier (auto-detected: osx-arm64, osx-x64, linux-x64, linux-arm64)
  -c, --configuration <C> Build configuration when building from source (default: Release)
  -b, --build             Force rebuilding CLI from source even if pre-built binary exists in dist/
  -u, --uninstall         Remove rus-zip and its backup from the install directory
  -h, --help              Show this help message

Examples:
  ./scripts/install.sh
  ./scripts/install.sh --dir /usr/local/bin
  ./scripts/install.sh --rid osx-arm64 --build
  ./scripts/install.sh --uninstall
EOF
}

check_dotnet() {
    if ! command -v dotnet >/dev/null 2>&1; then
        echo "Error: 'dotnet' CLI not found. Please install the .NET 10 SDK." >&2
        exit 1
    fi

    if ! dotnet --list-sdks 2>/dev/null | grep -qE '^10\.'; then
        echo "Error: .NET 10 SDK is required but not found in 'dotnet --list-sdks'." >&2
        echo "Installed SDKs:" >&2
        dotnet --list-sdks >&2 || true
        exit 1
    fi
}

get_file_size() {
    local target="$1"
    if [ -f "$target" ]; then
        ls -lh "$target" 2>/dev/null | awk '{print $5}'
    else
        echo "N/A"
    fi
}

# Parse command line options
while [[ $# -gt 0 ]]; do
    case "$1" in
        --dir=*)
            CUSTOM_INSTALL_DIR="${1#*=}"
            shift
            ;;
        -d|--dir|--prefix|-p)
            if [[ $# -lt 2 ]]; then
                echo "Error: $1 requires a directory argument." >&2
                exit 1
            fi
            CUSTOM_INSTALL_DIR="$2"
            shift 2
            ;;
        --prefix=*)
            CUSTOM_INSTALL_DIR="${1#*=}"
            shift
            ;;
        --rid=*)
            RID="${1#*=}"
            shift
            ;;
        -r|--rid)
            if [[ $# -lt 2 ]]; then
                echo "Error: $1 requires a RID argument." >&2
                exit 1
            fi
            RID="$2"
            shift 2
            ;;
        --configuration=*)
            CONFIGURATION="${1#*=}"
            shift
            ;;
        -c|--configuration)
            if [[ $# -lt 2 ]]; then
                echo "Error: $1 requires a configuration argument." >&2
                exit 1
            fi
            CONFIGURATION="$2"
            shift 2
            ;;
        -b|--build|--rebuild)
            FORCE_BUILD=true
            shift
            ;;
        -u|--uninstall|uninstall)
            DO_UNINSTALL=true
            shift
            ;;
        -h|--help|help)
            print_usage
            exit 0
            ;;
        -*)
            echo "Error: Unknown option '$1'." >&2
            print_usage >&2
            exit 1
            ;;
        *)
            if [ -z "$CUSTOM_INSTALL_DIR" ]; then
                CUSTOM_INSTALL_DIR="$1"
            else
                echo "Error: Unexpected argument '$1'." >&2
                print_usage >&2
                exit 1
            fi
            shift
            ;;
    esac
done

INSTALL_DIR="${CUSTOM_INSTALL_DIR:-$DEFAULT_INSTALL_DIR}"

# Best-effort read of a binary's version string (rus-zip --version -> e.g. "1.0.0").
# Empty result when the binary is absent or not runnable.
get_binary_version() {
    local binary="$1"
    if [ -x "$binary" ]; then
        local out
        out="$("$binary" --version 2>/dev/null | head -n 1)" || true
        echo "$out"
    else
        echo ""
    fi
}

# Remove the installed binary and its single backup. Leaves the install directory
# behind if it still contains other files; removes it only when it becomes empty.
uninstall_ruszip() {
    local installed="$INSTALL_DIR/rus-zip"
    local backup="$INSTALL_DIR/rus-zip.bak"

    if [ -f "$installed" ]; then
        rm -f "$installed"
        echo "[✓] Removed $installed"
    else
        echo "[✓] No installed binary at $installed"
    fi

    if [ -f "$backup" ]; then
        rm -f "$backup"
        echo "[✓] Removed backup $backup"
    else
        echo "[✓] No backup at $backup"
    fi

    if [ -d "$INSTALL_DIR" ] && rmdir "$INSTALL_DIR" 2>/dev/null; then
        echo "[✓] Removed now-empty directory $INSTALL_DIR"
    fi

    echo ""
    echo "rus-zip has been uninstalled."
    echo "If you added '$INSTALL_DIR' to your PATH manually, remove that entry yourself."
    exit 0
}

if [ "$DO_UNINSTALL" = true ]; then
    echo "Uninstalling rus-zip from $INSTALL_DIR"
    uninstall_ruszip
fi

echo "=================================================="
echo "Installing rus-zip CLI"
echo "  Target RID:        $RID"
echo "  Install Directory: $INSTALL_DIR"
echo "=================================================="

BINARY_SOURCE=""
TMP_BUILD_DIR=""

cleanup() {
    if [ -n "$TMP_BUILD_DIR" ] && [ -d "$TMP_BUILD_DIR" ]; then
        rm -rf "$TMP_BUILD_DIR"
    fi
}
trap cleanup EXIT

# 1. Locate pre-built binary or publish from source
if [ "$FORCE_BUILD" = false ]; then
    if [ -f "$ROOT_DIR/dist/$RID/rus-zip" ]; then
        BINARY_SOURCE="$ROOT_DIR/dist/$RID/rus-zip"
        echo "[+] Found pre-built binary: $BINARY_SOURCE"
    elif [ -f "$ROOT_DIR/dist/rus-zip" ]; then
        BINARY_SOURCE="$ROOT_DIR/dist/rus-zip"
        echo "[+] Found pre-built binary: $BINARY_SOURCE"
    fi
fi

if [ -z "$BINARY_SOURCE" ]; then
    echo "[*] Building self-contained CLI binary from source ($RID, $CONFIGURATION)..."
    check_dotnet

    TMP_BUILD_DIR="$(mktemp -d -t ruszip_install_${RID}_XXXXXX 2>/dev/null || mktemp -d /tmp/ruszip_install_${RID}_XXXXXX)"

    dotnet publish "$ROOT_DIR/src/RusZip.Cli/RusZip.Cli.csproj" \
        -c "$CONFIGURATION" \
        -r "$RID" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "$TMP_BUILD_DIR"

    if [ -f "$TMP_BUILD_DIR/RusZip.Cli" ]; then
        BINARY_SOURCE="$TMP_BUILD_DIR/RusZip.Cli"
    elif [ -f "$TMP_BUILD_DIR/rus-zip" ]; then
        BINARY_SOURCE="$TMP_BUILD_DIR/rus-zip"
    else
        echo "Error: Published binary not found in $TMP_BUILD_DIR" >&2
        exit 1
    fi
fi

# 2. Version check, backup, and idempotency
mkdir -p "$INSTALL_DIR"

INSTALLED_BINARY="$INSTALL_DIR/rus-zip"
NEW_BINARY="$BINARY_SOURCE"

# The staged/source binary may not have the executable bit set yet; make it
# runnable so we can query its version (dist/ artifacts are disposable build output).
if [ ! -x "$NEW_BINARY" ]; then
    chmod +x "$NEW_BINARY" 2>/dev/null || true
fi

OLD_VERSION="$(get_binary_version "$INSTALLED_BINARY")"
NEW_VERSION="$(get_binary_version "$NEW_BINARY")"

# Idempotency: same version already installed is a no-op success.
if [ -f "$INSTALLED_BINARY" ] && [ -n "$OLD_VERSION" ] && [ "$OLD_VERSION" = "$NEW_VERSION" ]; then
    echo "[✓] rus-zip $OLD_VERSION is already installed at $INSTALLED_BINARY. Nothing to do."
    exit 0
fi

echo "[*] Installing binary to $INSTALLED_BINARY..."
if [ -f "$INSTALLED_BINARY" ]; then
    echo "  Existing version:   ${OLD_VERSION:-<unknown>}"
    echo "  Installing version: ${NEW_VERSION:-<unknown>}"
    # Keep exactly one timestamped backup of the previous binary.
    if [ -f "$INSTALL_DIR/rus-zip.bak" ]; then
        rm -f "$INSTALL_DIR/rus-zip.bak"
    fi
    cp "$INSTALLED_BINARY" "$INSTALL_DIR/rus-zip.bak"
    echo "[+] Backed up previous binary to $INSTALL_DIR/rus-zip.bak ($(date -u +%Y-%m-%dT%H:%M:%SZ))"
else
    echo "  Installing version: ${NEW_VERSION:-<unknown>}"
fi

cp "$NEW_BINARY" "$INSTALLED_BINARY"
chmod +x "$INSTALLED_BINARY"

BINARY_SIZE="$(get_file_size "$INSTALLED_BINARY")"

# 3. Check PATH presence
IS_IN_PATH=false
IFS=':' read -ra PATH_PARTS <<< "$PATH"
for p in "${PATH_PARTS[@]}"; do
    if [ "$p" = "$INSTALL_DIR" ]; then
        IS_IN_PATH=true
        break
    fi
done

echo ""
echo "=================================================="
echo "rus-zip CLI successfully installed!"
echo "=================================================="
echo "  Location: $INSTALL_DIR/rus-zip ($BINARY_SIZE)"
echo ""

if [ "$IS_IN_PATH" = true ]; then
    echo "[✓] '$INSTALL_DIR' is already in your PATH."
else
    echo "[!] Warning: '$INSTALL_DIR' is not currently in your system PATH."
    echo ""
    echo "To add it to your PATH, run one of the following commands based on your shell:"
    echo ""
    USER_SHELL="$(basename "${SHELL:-bash}")"
    case "$USER_SHELL" in
        zsh)
            echo "  # For Zsh (macOS default):"
            echo "  echo 'export PATH=\"$INSTALL_DIR:\$PATH\"' >> ~/.zshrc"
            echo "  source ~/.zshrc"
            ;;
        fish)
            echo "  # For Fish:"
            echo "  fish_add_path $INSTALL_DIR"
            ;;
        bash|*)
            echo "  # For Bash:"
            echo "  echo 'export PATH=\"$INSTALL_DIR:\$PATH\"' >> ~/.bashrc"
            echo "  source ~/.bashrc"
            ;;
    esac
    echo ""
    echo "  # Or for current shell session only:"
    echo "  export PATH=\"$INSTALL_DIR:\$PATH\""
    echo ""
fi

# 4. Verification and sample commands
echo "Quick Start Commands:"
echo "  rus-zip compress <source> <archive.zrus> --profile high   # Create a .zrus archive"
echo "  rus-zip extract <archive.zrus> -o <destination>           # Extract archive"
echo "  rus-zip list <archive.zrus>                               # List archive contents"
echo "  rus-zip --help                                            # Show all CLI commands & options"
echo "=================================================="
