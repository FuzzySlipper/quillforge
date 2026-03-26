#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="$(dirname "$SCRIPT_DIR")"

# Find the executable
if [[ -f "$APP_DIR/QuillForge.Web" ]]; then
    EXEC="$APP_DIR/QuillForge.Web"
elif [[ -f "$SCRIPT_DIR/QuillForge.Web" ]]; then
    EXEC="$SCRIPT_DIR/QuillForge.Web"
else
    echo "QuillForge executable not found. Expected in $APP_DIR or $SCRIPT_DIR"
    exit 1
fi

echo "Starting QuillForge..."
echo "Content directory: ${CONTENT_ROOT:-$APP_DIR/build}"

exec "$EXEC" --ContentRoot "${CONTENT_ROOT:-$APP_DIR/build}" "$@"
