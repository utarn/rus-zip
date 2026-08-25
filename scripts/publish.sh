#!/usr/bin/env bash
# rus-zip automated publishing script for macOS (osx-arm64) and cross-platform RIDs
# Usage:
#   ./scripts/publish.sh                  # Publish for default RID (osx-arm64)
#   ./scripts/publish.sh osx-arm64        # Publish explicitly for osx-arm64
#   ./scripts/publish.sh --rid win-x64    # Publish for win-x64
#   ./scripts/publish.sh --help           # Show usage help

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

DEFAULT_RID="osx-arm64"
RID="$DEFAULT_RID"
CONFIGURATION="Release"
VERSION_FILE="$ROOT_DIR/VERSION"
APP_VERSION="1.0.0"
if [ -f "$VERSION_FILE" ]; then
    APP_VERSION="$(tr -d '[:space:]' < "$VERSION_FILE")"
fi

print_usage() {
    cat << 'EOF'
rus-zip publishing script

Usage:
  ./scripts/publish.sh [rid] [options]

Arguments:
  rid                     Target Runtime Identifier (default: osx-arm64)

Options:
  -r, --rid <RID>         Target Runtime Identifier (e.g. osx-arm64, osx-x64, linux-x64, win-x64)
  -c, --configuration <C> Build configuration (Debug, Release; default: Release)
  -h, --help              Show this help message

Examples:
  ./scripts/publish.sh
  ./scripts/publish.sh osx-arm64
  ./scripts/publish.sh --rid linux-x64
  ./scripts/publish.sh --rid win-x64 -c Release
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
    elif [ -d "$target" ]; then
        if du --apparent-size -sh "$target" >/dev/null 2>&1; then
            du --apparent-size -sh "$target" 2>/dev/null | awk '{print $1}'
        else
            du -sh "$target" 2>/dev/null | awk '{print $1}'
        fi
    else
        echo "N/A"
    fi
}

# Parse command-line arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --rid=*)
            RID="${1#*=}"
            shift
            ;;
        -r|--rid)
            if [[ $# -lt 2 ]]; then
                echo "Error: --rid requires a value." >&2
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
                echo "Error: --configuration requires a value." >&2
                exit 1
            fi
            CONFIGURATION="$2"
            shift 2
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
            RID="$1"
            shift
            ;;
    esac
done

check_dotnet

OUTPUT_DIR="$ROOT_DIR/dist/$RID"
echo "=================================================="
echo "Publishing rus-zip"
echo "  Target RID:       $RID"
echo "  Configuration:    $CONFIGURATION"
echo "  Output Directory: $OUTPUT_DIR"
echo "=================================================="

# Staging directories
STAGING_BASE="$(mktemp -d -t ruszip_publish_${RID}_XXXXXX 2>/dev/null || mktemp -d /tmp/ruszip_publish_${RID}_XXXXXX)"
TMP_CLI_DIR="$STAGING_BASE/publish_cli_${RID}"
TMP_DESKTOP_DIR="$STAGING_BASE/publish_desktop_${RID}"

cleanup() {
    if [ -d "$STAGING_BASE" ]; then
        rm -rf "$STAGING_BASE"
    fi
}
trap cleanup EXIT

# Clean and recreate target output directory
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# 1. Publish CLI (Self-contained, single file, Standard JIT)
echo ""
echo "--> Publishing RusZip CLI (Standard JIT)..."
dotnet publish "$ROOT_DIR/src/RusZip.Cli/RusZip.Cli.csproj" \
    -c "$CONFIGURATION" \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PublishReadyToRun=false \
    -o "$TMP_CLI_DIR"

# Copy CLI binary to dist/<rid>/
if [ -f "$TMP_CLI_DIR/RusZip.Cli" ]; then
    cp "$TMP_CLI_DIR/RusZip.Cli" "$OUTPUT_DIR/rus-zip"
    chmod +x "$OUTPUT_DIR/rus-zip"
elif [ -f "$TMP_CLI_DIR/rus-zip" ]; then
    cp "$TMP_CLI_DIR/rus-zip" "$OUTPUT_DIR/rus-zip"
    chmod +x "$OUTPUT_DIR/rus-zip"
elif [ -f "$TMP_CLI_DIR/RusZip.Cli.exe" ]; then
    cp "$TMP_CLI_DIR/RusZip.Cli.exe" "$OUTPUT_DIR/rus-zip.exe"
elif [ -f "$TMP_CLI_DIR/rus-zip.exe" ]; then
    cp "$TMP_CLI_DIR/rus-zip.exe" "$OUTPUT_DIR/rus-zip.exe"
else
    echo "Error: Published CLI executable not found in $TMP_CLI_DIR" >&2
    exit 1
fi

# 2. Publish Desktop (Self-contained, single file, ReadyToRun)
echo ""
echo "--> Publishing RusZip Desktop (ReadyToRun)..."
dotnet publish "$ROOT_DIR/src/RusZip.Desktop/RusZip.Desktop.csproj" \
    -c "$CONFIGURATION" \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PublishReadyToRun=true \
    -p:PublishReadyToRunShowWarnings=true \
    -o "$TMP_DESKTOP_DIR"

# Find published Desktop binary
DESKTOP_BIN=""
if [ -f "$TMP_DESKTOP_DIR/RusZip.Desktop" ]; then
    DESKTOP_BIN="$TMP_DESKTOP_DIR/RusZip.Desktop"
elif [ -f "$TMP_DESKTOP_DIR/RusZip" ]; then
    DESKTOP_BIN="$TMP_DESKTOP_DIR/RusZip"
elif [ -f "$TMP_DESKTOP_DIR/RusZip.Desktop.exe" ]; then
    DESKTOP_BIN="$TMP_DESKTOP_DIR/RusZip.Desktop.exe"
elif [ -f "$TMP_DESKTOP_DIR/RusZip.exe" ]; then
    DESKTOP_BIN="$TMP_DESKTOP_DIR/RusZip.exe"
fi

if [ -z "$DESKTOP_BIN" ]; then
    echo "Error: Published Desktop executable not found in $TMP_DESKTOP_DIR" >&2
    exit 1
fi

# If macOS RID (or default), create standard .app bundle
if [[ "$RID" == osx* ]]; then
    APP_DIR="$OUTPUT_DIR/RusZip.app"
    MACOS_DIR="$APP_DIR/Contents/MacOS"
    RESOURCES_DIR="$APP_DIR/Contents/Resources"

    mkdir -p "$MACOS_DIR"
    mkdir -p "$RESOURCES_DIR"

    cp "$DESKTOP_BIN" "$MACOS_DIR/RusZip"
    chmod +x "$MACOS_DIR/RusZip"

    if [ -f "$ROOT_DIR/src/RusZip.Desktop/Assets/rus-zip.icns" ]; then
        cp "$ROOT_DIR/src/RusZip.Desktop/Assets/rus-zip.icns" "$RESOURCES_DIR/RusZip.icns"
    fi

    cat << 'PLIST' > "$APP_DIR/Contents/Info.plist"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>RusZip</string>
    <key>CFBundleIconFile</key>
    <string>RusZip.icns</string>
    <key>CFBundleIdentifier</key>
    <string>com.ruszip.desktop</string>
    <key>CFBundleName</key>
    <string>RUS ZIP</string>
    <key>CFBundleDisplayName</key>
    <string>RUS ZIP</string>
    <key>CFBundleVersion</key>
    <string>$APP_VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$APP_VERSION</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>????</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
</dict>
</plist>
PLIST
else
    # Non-macOS Desktop binary placement
    if [[ "$RID" == win* ]]; then
        cp "$DESKTOP_BIN" "$OUTPUT_DIR/RusZip.Desktop.exe"
    else
        cp "$DESKTOP_BIN" "$OUTPUT_DIR/RusZip.Desktop"
        chmod +x "$OUTPUT_DIR/RusZip.Desktop"
    fi
fi

# Output summary
echo ""
echo "=================================================="
echo "Publish completed successfully!"
echo "=================================================="
echo "Output Directory: $OUTPUT_DIR"

if [ -f "$OUTPUT_DIR/rus-zip" ]; then
    echo "  - CLI Binary:         $OUTPUT_DIR/rus-zip ($(get_file_size "$OUTPUT_DIR/rus-zip"))"
elif [ -f "$OUTPUT_DIR/rus-zip.exe" ]; then
    echo "  - CLI Binary:         $OUTPUT_DIR/rus-zip.exe ($(get_file_size "$OUTPUT_DIR/rus-zip.exe"))"
fi

if [ -d "$OUTPUT_DIR/RusZip.app" ]; then
    echo "  - macOS App Bundle:   $OUTPUT_DIR/RusZip.app ($(get_file_size "$OUTPUT_DIR/RusZip.app"))"
    echo "    - Executable:       $OUTPUT_DIR/RusZip.app/Contents/MacOS/RusZip ($(get_file_size "$OUTPUT_DIR/RusZip.app/Contents/MacOS/RusZip"))"
    echo "    - Info.plist:       $OUTPUT_DIR/RusZip.app/Contents/Info.plist"
elif [ -f "$OUTPUT_DIR/RusZip.Desktop.exe" ]; then
    echo "  - Desktop Executable: $OUTPUT_DIR/RusZip.Desktop.exe ($(get_file_size "$OUTPUT_DIR/RusZip.Desktop.exe"))"
elif [ -f "$OUTPUT_DIR/RusZip.Desktop" ]; then
    echo "  - Desktop Executable: $OUTPUT_DIR/RusZip.Desktop ($(get_file_size "$OUTPUT_DIR/RusZip.Desktop"))"
fi
echo "=================================================="
