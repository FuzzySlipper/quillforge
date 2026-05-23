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
const forbidden = ["*.blockmap", "*.yml", "latest*"];
for (const pattern of forbidden) {
  if (stageBlock.includes(pattern)) {
    console.error(`release config check failed: Stage release assets block still includes ${pattern}`);
    process.exit(1);
  }
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
if (desktopPackage.dependencies && Object.prototype.hasOwnProperty.call(desktopPackage.dependencies, 'electron-updater')) {
  console.error('release config check failed: electron-updater requires latest*.yml metadata assets that should not be published');
  process.exit(1);
}
const desktopMain = fs.readFileSync('src/QuillForge.Desktop/main.js', 'utf8');
if (desktopMain.includes('electron-updater') || desktopMain.includes('autoUpdater')) {
  console.error('release config check failed: Desktop shell still references electron-updater metadata flow');
  process.exit(1);
}
NODE

echo "release config check passed (desktop version $pkg_version)"
