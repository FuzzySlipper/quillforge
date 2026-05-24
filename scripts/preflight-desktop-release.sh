#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

echo "== QuillForge desktop release preflight =="

echo "-- release configuration"
scripts/check-release-config.sh

echo "-- restore .NET solution"
dotnet restore QuillForge.slnx

echo "-- build .NET solution"
dotnet build QuillForge.slnx --no-restore -c Release -p:AllowMissingPrunePackageData=true

echo "-- test .NET solution"
dotnet test QuillForge.slnx --no-build -c Release -p:AllowMissingPrunePackageData=true

echo "-- install web frontend dependencies"
npm ci --prefix src/QuillForge.Web/Client

if [[ "${RUN_FRONTEND_LINT:-0}" == "1" ]]; then
  echo "-- lint web frontend"
  npm run lint --prefix src/QuillForge.Web/Client
else
  echo "-- lint web frontend (skipped; set RUN_FRONTEND_LINT=1 to include current lint baseline)"
fi

echo "-- build web frontend"
npm run build --prefix src/QuillForge.Web/Client
touch src/QuillForge.Web/wwwroot/assets/.build-marker

echo "-- install desktop dependencies"
npm ci --prefix src/QuillForge.Desktop

echo "desktop release preflight passed"
