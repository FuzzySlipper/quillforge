#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
exec npx --yes electron src/QuillForge.Desktop
