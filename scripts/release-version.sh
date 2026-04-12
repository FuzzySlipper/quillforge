#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/release-version.sh [<version>] [--dry-run]

Examples:
  scripts/release-version.sh
  scripts/release-version.sh 4.4
  scripts/release-version.sh 4.4.1
  scripts/release-version.sh --dry-run

If no version is provided, the script bumps the minor version by 1.
The script commits the QuillForge.Web version bump before tagging so the
release build actually contains the new version.
EOF
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
PROJECT_FILE_REL="src/QuillForge.Web/QuillForge.Web.csproj"
PROJECT_FILE="$REPO_ROOT/$PROJECT_FILE_REL"

if [[ ! -f "$PROJECT_FILE" ]]; then
  echo "Project file not found: $PROJECT_FILE" >&2
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
  local version
  version="$(sed -n 's:.*<Version>\([^<][^<]*\)</Version>.*:\1:p' "$PROJECT_FILE" | head -n 1)"
  if [[ -z "$version" ]]; then
    echo "Could not find <Version> in $PROJECT_FILE_REL" >&2
    exit 1
  fi
  printf '%s\n' "$version"
}

normalize_version() {
  local value="$1"
  value="${value#v}"
  printf '%s\n' "$value"
}

validate_version() {
  local value="$1"
  if [[ ! "$value" =~ ^[0-9]+(\.[0-9]+){1,2}$ ]]; then
    echo "Invalid version '$value'. Use major.minor or major.minor.patch." >&2
    exit 1
  fi
}

bump_minor_version() {
  local current="$1"
  IFS='.' read -r major minor _ <<< "$current"
  printf '%s.%s\n' "$major" "$((minor + 1))"
}

replace_version() {
  local target="$1"
  perl -0pi -e "s{<Version>[^<]+</Version>}{<Version>$target</Version>}" "$PROJECT_FILE"
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

if (( dry_run )); then
  echo "Current version: $current_version"
  echo "Target version:  $target_version"
  if [[ "$target_version" != "$current_version" ]]; then
    echo "Would update $PROJECT_FILE_REL"
    echo "Would commit: Bump QuillForge.Web version to $target_version"
  else
    echo "Version is already $target_version; would tag current HEAD."
  fi
  echo "Would run: git tag $tag_name"
  echo "Would run: git push origin $tag_name"
  exit 0
fi

cd "$REPO_ROOT"

if [[ "$target_version" != "$current_version" ]]; then
  if [[ -n "$(git status --porcelain -- "$PROJECT_FILE_REL")" ]]; then
    echo "$PROJECT_FILE_REL has uncommitted changes. Commit or stash them first." >&2
    exit 1
  fi

  replace_version "$target_version"
  git add "$PROJECT_FILE_REL"
  git commit -m "Bump QuillForge.Web version to $target_version"
else
  echo "Version is already $target_version; tagging current HEAD."
fi

git tag "$tag_name"
git push origin "$tag_name"

echo "Released $tag_name"
echo "If you want the version bump commit on the remote branch too, push your branch after this."
