#!/usr/bin/env bash
# rus-zip runner script for macOS and Linux
# Usage:
#   ./run.sh desktop        # Run Avalonia Desktop App
#   ./run.sh gui            # Alias for desktop
#   ./run.sh cli [args...]  # Run RusZip CLI
#   ./run.sh test [args...] # Run all unit and integration tests
#   ./run.sh build [args...] # Build entire solution
#   ./run.sh --help         # Show usage help

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

print_usage() {
    cat << 'EOF'
rus-zip runner script for macOS and Linux

Usage:
  ./run.sh <command> [args...]

Commands:
  desktop, gui     Run the RusZip Avalonia Desktop application
  cli              Run the RusZip CLI tool (passes remaining args)
  test             Run all unit and integration tests
  build            Build the solution
  help, -h, --help Show this help message

Examples:
  ./run.sh desktop
  ./run.sh cli compress src/ backup.zrus --profile high
  ./run.sh cli list backup.zrus
  ./run.sh test
  ./run.sh build -c Release
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

if [ $# -eq 0 ]; then
    print_usage
    exit 0
fi

COMMAND="$1"
shift

case "$COMMAND" in
    desktop|gui)
        check_dotnet
        exec dotnet run --project "$SCRIPT_DIR/src/RusZip.Desktop" -- "$@"
        ;;
    cli)
        check_dotnet
        exec dotnet run --project "$SCRIPT_DIR/src/RusZip.Cli" -- "$@"
        ;;
    test)
        check_dotnet
        if [ $# -eq 0 ]; then
            exec dotnet test "$SCRIPT_DIR/RusZip.slnx"
        else
            exec dotnet test "$@"
        fi
        ;;
    build)
        check_dotnet
        if [ $# -eq 0 ]; then
            exec dotnet build "$SCRIPT_DIR/RusZip.slnx"
        else
            exec dotnet build "$@"
        fi
        ;;
    help|-h|--help)
        print_usage
        exit 0
        ;;
    *)
        echo "Error: Unknown command '$COMMAND'." >&2
        echo "" >&2
        print_usage >&2
        exit 1
        ;;
esac
