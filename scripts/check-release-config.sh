#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

fail() {
  echo "release config check failed: $*" >&2
  exit 1
}

pkg_version="$(node -e "process.stdout.write(require('./src/QuillForge.Desktop/package.json').version)")"
lock_version="$(node -e "process.stdout.write(require('./src/QuillForge.Desktop/package-lock.json').version)")"
lock_root_version="$(node -e "process.stdout.write(require('./src/QuillForge.Desktop/package-lock.json').packages[''].version)")"

[[ "$pkg_version" == "$lock_version" ]] || fail "Desktop package.json version ($pkg_version) does not match package-lock.json version ($lock_version)"
[[ "$pkg_version" == "$lock_root_version" ]] || fail "Desktop package.json version ($pkg_version) does not match package-lock root package version ($lock_root_version)"

node <<'NODE'
const fs = require('fs');

const workflow = fs.readFileSync('.github/workflows/release.yml', 'utf8');
const start = workflow.indexOf('      - name: Stage release assets');
const end = workflow.indexOf('      - name: Upload staged assets', start);
if (start === -1 || end === -1) {
  console.error('release config check failed: could not locate Stage release assets block');
  process.exit(1);
}
const stageBlock = workflow.slice(start, end);
const requiredUpdaterAssets = ["*.blockmap", "*.yml", "latest*"];
for (const pattern of requiredUpdaterAssets) {
  if (!stageBlock.includes(pattern)) {
    console.error(`release config check failed: electron-updater release path must stage ${pattern}`);
    process.exit(1);
  }
}
if (!stageBlock.includes("! -name 'builder-debug.yml'")) {
  console.error('release config check failed: Stage release assets block must exclude builder-debug.yml');
  process.exit(1);
}

const releaseScript = fs.readFileSync('scripts/release-version.sh', 'utf8');
if (!releaseScript.includes('src/QuillForge.Desktop/package.json')) {
  console.error('release config check failed: release-version.sh must update the Desktop package.json version source');
  process.exit(1);
}
if (releaseScript.includes('PROJECT_FILE_REL="src/QuillForge.Web/QuillForge.Web.csproj"')) {
  console.error('release config check failed: release-version.sh still targets the Web csproj version');
  process.exit(1);
}

const desktopPackage = JSON.parse(fs.readFileSync('src/QuillForge.Desktop/package.json', 'utf8'));
if (!desktopPackage.dependencies || !Object.prototype.hasOwnProperty.call(desktopPackage.dependencies, 'electron-updater')) {
  console.error('release config check failed: electron-updater dependency is required while updater metadata assets are published');
  process.exit(1);
}
const desktopMain = fs.readFileSync('src/QuillForge.Desktop/main.js', 'utf8');
if (!desktopMain.includes("require('electron-updater')") || !desktopMain.includes('autoUpdater.checkForUpdates')) {
  console.error('release config check failed: Desktop shell must keep electron-updater runtime wiring while updater metadata assets are published');
  process.exit(1);
}
const desktopPreload = fs.readFileSync('src/QuillForge.Desktop/preload.js', 'utf8');
if (!desktopPreload.includes('installUpdate') || !desktopPreload.includes('onUpdateStatus') || !desktopPreload.includes('onUpdateProgress')) {
  console.error('release config check failed: Desktop preload must expose updater IPC bridge methods while electron-updater is enabled');
  process.exit(1);
}
NODE

echo "release config check passed (desktop version $pkg_version)"
