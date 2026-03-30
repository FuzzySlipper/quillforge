#!/bin/bash
set -e

RELEASE_DIR="/mnt/den-data/dev/quillforge/release"
PUBLISH_DIR="/tmp/quillforge-publish"

echo "Building and publishing..."
dotnet publish src/QuillForge.Web/QuillForge.Web.csproj \
  -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$PUBLISH_DIR/"

echo "Deploying to $RELEASE_DIR..."
# Sync the binary and web assets, but never touch build/ (user data)
rsync -a --delete \
  --exclude 'build/' \
  --exclude 'appsettings.json' \
  --exclude 'appsettings.Development.json' \
  "$PUBLISH_DIR/" "$RELEASE_DIR/"

echo "Done. The server will auto-restart within a few seconds."
