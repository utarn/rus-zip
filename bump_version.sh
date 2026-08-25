#!/usr/bin/env bash
# rus-zip version bumping script
# Usage:
#   ./bump_version.sh patch          # 1.0.0 -> 1.0.1
#   ./bump_version.sh minor          # 1.0.0 -> 1.1.0
#   ./bump_version.sh major          # 1.0.0 -> 2.0.0
#   ./bump_version.sh 1.2.3          # Explicit version: 1.2.3

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$SCRIPT_DIR"
VERSION_FILE="$ROOT_DIR/VERSION"
PROPS_FILE="$ROOT_DIR/Directory.Build.props"

if [ ! -f "$VERSION_FILE" ]; then
    echo "1.0.0" > "$VERSION_FILE"
fi

CURRENT_VERSION="$(tr -d '[:space:]' < "$VERSION_FILE")"
BUMP_TYPE="${1:-patch}"

IFS='.' read -r MAJOR MINOR PATCH EXTRA <<< "$CURRENT_VERSION"
MAJOR="${MAJOR:-1}"
MINOR="${MINOR:-0}"
PATCH="${PATCH:-0}"

# Strip any pre-release suffix from PATCH for incrementing
PATCH_NUM="${PATCH%%-*}"

case "$BUMP_TYPE" in
    major)
        NEW_MAJOR=$((MAJOR + 1))
        NEW_VERSION="${NEW_MAJOR}.0.0"
        ;;
    minor)
        NEW_MINOR=$((MINOR + 1))
        NEW_VERSION="${MAJOR}.${NEW_MINOR}.0"
        ;;
    patch)
        NEW_PATCH=$((PATCH_NUM + 1))
        NEW_VERSION="${MAJOR}.${MINOR}.${NEW_PATCH}"
        ;;
    [0-9]*.[0-9]*.[0-9]*)
        NEW_VERSION="$BUMP_TYPE"
        ;;
    *)
        echo "Error: Invalid argument '$BUMP_TYPE'." >&2
        echo "Usage: ./bump_version.sh [major|minor|patch|<version_string>]" >&2
        exit 1
        ;;
esac

echo "=================================================="
echo "Bumping rus-zip version: $CURRENT_VERSION -> $NEW_VERSION"
echo "=================================================="

# 1. Update VERSION file
echo "$NEW_VERSION" > "$VERSION_FILE"

# 2. Update Directory.Build.props
if [ -f "$PROPS_FILE" ]; then
    if grep -q "<VersionPrefix>" "$PROPS_FILE"; then
        sed -i.bak "s|<VersionPrefix>.*</VersionPrefix>|<VersionPrefix>$NEW_VERSION</VersionPrefix>|g" "$PROPS_FILE"
        rm -f "${PROPS_FILE}.bak"
    fi
fi

echo "Version successfully updated to $NEW_VERSION across project configuration."
