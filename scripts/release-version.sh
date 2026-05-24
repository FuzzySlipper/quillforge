#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/release-version.sh [<version>] [--dry-run]

Examples:
  scripts/release-version.sh
  scripts/release-version.sh 4.4.0
  scripts/release-version.sh 4.4.1
  scripts/release-version.sh 4.4  # normalized to 4.4.0
  scripts/release-version.sh --dry-run

If no version is provided, the script bumps the minor version by 1 and resets
the patch component to 0. Desktop package versions are always written as
major.minor.patch because electron-builder requires full semver.
The script commits the QuillForge Desktop package version bump before tagging so
release builds actually contain the new version.
EOF
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
DESKTOP_PACKAGE_REL="src/QuillForge.Desktop/package.json"
DESKTOP_LOCK_REL="src/QuillForge.Desktop/package-lock.json"
DESKTOP_PACKAGE="$REPO_ROOT/$DESKTOP_PACKAGE_REL"
DESKTOP_LOCK="$REPO_ROOT/$DESKTOP_LOCK_REL"

if [[ ! -f "$DESKTOP_PACKAGE" ]]; then
  echo "Desktop package file not found: $DESKTOP_PACKAGE" >&2
  exit 1
fi

if ! git -C "$REPO_ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "This script must be run inside the QuillForge git repository." >&2
  exit 1
fi

dry_run=0
requested_version=""

for arg in "$@"; do
  case "$arg" in
    -h|--help)
      usage
      exit 0
      ;;
    --dry-run)
      dry_run=1
      ;;
    *)
      if [[ -n "$requested_version" ]]; then
        echo "Only one version argument is allowed." >&2
        usage >&2
        exit 1
      fi
      requested_version="$arg"
      ;;
  esac
done

read_current_version() {
  node -e "process.stdout.write(require(process.argv[1]).version)" "$DESKTOP_PACKAGE"
}

normalize_version() {
  local value="$1"
  value="${value#v}"
  if [[ "$value" =~ ^([0-9]+)\.([0-9]+)$ ]]; then
    value="${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.0"
  fi
  printf '%s\n' "$value"
}

validate_version() {
  local value="$1"
  if [[ ! "$value" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Invalid version '$value'. Use major.minor.patch." >&2
    exit 1
  fi
}

bump_minor_version() {
  local current="$1"
  IFS='.' read -r major minor _ <<< "$current"
  printf '%s.%s.0\n' "$major" "$((minor + 1))"
}

replace_version() {
  local target="$1"
  node - "$target" "$DESKTOP_PACKAGE" "$DESKTOP_LOCK" <<'NODE'
const fs = require('fs');

const [target, packagePath, lockPath] = process.argv.slice(2);

function writeJson(path, value) {
  fs.writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`);
}

const packageJson = JSON.parse(fs.readFileSync(packagePath, 'utf8'));
packageJson.version = target;
writeJson(packagePath, packageJson);

if (fs.existsSync(lockPath)) {
  const lockJson = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
  lockJson.version = target;
  if (lockJson.packages && lockJson.packages['']) {
    lockJson.packages[''].version = target;
  }
  writeJson(lockPath, lockJson);
}
NODE
}

current_version="$(read_current_version)"

if [[ -n "$requested_version" ]]; then
  target_version="$(normalize_version "$requested_version")"
  validate_version "$target_version"
else
  validate_version "$current_version"
  target_version="$(bump_minor_version "$current_version")"
fi

tag_name="v$target_version"

if git -C "$REPO_ROOT" rev-parse --verify --quiet "refs/tags/$tag_name" >/dev/null; then
  echo "Tag already exists locally: $tag_name" >&2
  exit 1
fi

changed_files=("$DESKTOP_PACKAGE_REL")
if [[ -f "$DESKTOP_LOCK" ]]; then
  changed_files+=("$DESKTOP_LOCK_REL")
fi

if (( dry_run )); then
  echo "Current version: $current_version"
  echo "Target version:  $target_version"
  if [[ "$target_version" != "$current_version" ]]; then
    echo "Would update ${changed_files[*]}"
    echo "Would commit: Bump QuillForge Desktop version to $target_version"
  else
    echo "Version is already $target_version; would tag current HEAD."
  fi
  echo "Would run: git tag $tag_name"
  echo "Would run: git push origin $tag_name"
  exit 0
fi

cd "$REPO_ROOT"

if [[ "$target_version" != "$current_version" ]]; then
  if [[ -n "$(git status --porcelain -- "${changed_files[@]}")" ]]; then
    echo "Release version files have uncommitted changes. Commit or stash them first." >&2
    git status --short -- "${changed_files[@]}" >&2
    exit 1
  fi

  replace_version "$target_version"
  git add "${changed_files[@]}"
  git commit -m "Bump QuillForge Desktop version to $target_version"
else
  echo "Version is already $target_version; tagging current HEAD."
fi

git tag "$tag_name"
git push origin "$tag_name"

echo "Released $tag_name"
echo "If you want the version bump commit on the remote branch too, push your branch after this."
