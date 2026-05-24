# Desktop Release Validation

Use this checklist before publishing or announcing a desktop release. It is intentionally concrete and a little repetitive so a sleepy maintainer can work through it without inventing steps on the fly.

## Release Artifacts

For the tagged release you expect to publish, confirm GitHub Actions produced the current first-wave desktop assets:

- `QuillForge-Linux-x86_64-<version>.AppImage`
- `QuillForge-Linux-x86_64-<version>.rpm`
- `QuillForge-macOS-x64-<version>.dmg`
- `QuillForge-macOS-x64-<version>.zip`
- `QuillForge-macOS-arm64-<version>.dmg`
- `QuillForge-macOS-arm64-<version>.zip`
- `QuillForge-Windows-x64-<version>.exe`
- updater metadata: `latest.yml`, platform-specific `*.yml`, and `*.blockmap`

Quick sanity checks:
- artifact filenames match the stable names documented in `README.md`
- the release notes do not still describe the old raw-backend tarball flow
- the artifact sizes are plausible for a bundled desktop app, not suspiciously tiny

## User-Facing Install Smoke Test

Run at least one real install path for the platforms you care about most.

### Linux

1. Download `QuillForge-Linux-x86_64-<version>.AppImage`.
2. Make it executable with `chmod +x ./QuillForge-Linux-x86_64-<version>.AppImage`.
3. Run it with `./QuillForge-Linux-x86_64-<version>.AppImage`.
4. Confirm the desktop shell appears instead of a terminal-plus-browser workflow.
5. Confirm the first run creates `~/Documents/QuillForge`.
6. Close and relaunch to confirm the app still opens the same workspace.

### macOS

1. Download either the zipped `.app` bundle or the `.dmg`.
2. Move `QuillForge.app` into `Applications`.
3. Launch it once.
4. If Gatekeeper blocks it, use `System Settings -> Privacy & Security -> Open Anyway`.
5. Confirm the app launches after that override and creates `~/Documents/QuillForge`.
6. Relaunch from `Applications` to make sure the override path is no longer confusing the user.

### Windows

1. Run `QuillForge-Windows-x64-<version>.exe`.
2. Launch QuillForge from Start.
3. Confirm the desktop shell opens and uses the Windows Documents-based QuillForge workspace.
4. Close and relaunch once to make sure the installed shortcut still works.

## Workspace And Update Validation

These checks are important because QuillForge treats the visible workspace as the real user data.

### First-Run Workspace

On each platform you test:
- verify the app creates the expected visible workspace location instead of hiding user content next to binaries
- verify a starter `config.yaml` and content folders appear under that workspace
- verify `Open Workspace` reveals the same directory the shell reports

### Migration From Older Portable Layouts

If you have an older portable build available:
- place an old sibling `user/` directory next to the published app
- make sure `Documents/QuillForge` is empty before first launch
- launch the desktop app
- verify the old workspace is copied into `Documents/QuillForge`
- verify the original sibling `user/` directory is left in place

### Manual Update Flow

Validate the platform-appropriate update story without touching the workspace:

- Linux: download the latest AppImage, make it executable, and run it
- macOS: replace the old `QuillForge.app` in `Applications` with the newer one
- Windows: run the newer installer over the existing install

After each update check:
- QuillForge still opens successfully
- the existing `Documents/QuillForge` workspace is preserved
- existing sessions/content still appear where expected

## Desktop Behavior Validation

Run these checks from an actual desktop build, not only from `dotnet run`.

### Local-Only Default

1. Launch QuillForge.
2. Confirm the shell reports local-only binding by default.
3. Confirm the embedded app loads normally.
4. Confirm the shell shows a loopback URL for the local machine.

### Optional LAN/Mobile Access

1. Use the desktop shell toggle to enable LAN/mobile access.
2. Confirm the backend restarts successfully.
3. Confirm the shell shows a phone/tablet URL using a private LAN address.
4. Open that URL from another device on the same trusted network if available.
5. Disable LAN/mobile access again and confirm the shell returns to local-only mode.

### Failure And Recovery

At minimum, confirm:
- the shell shows a startup/error surface if the backend cannot launch
- `Restart Backend` works from the shell UI
- `Open Workspace` still works while the backend is stopped

## Maintainer Build Checks

GitHub Actions should be responsible for cross-platform packaging and release asset publication. Run the exhaustive quality checks locally before bumping/tagging so the tag workflow can stay small and mostly package/publish.

Before tagging a release, run the local preflight script from the repository root:

```bash
scripts/preflight-desktop-release.sh
```

That script verifies release config, builds/tests the .NET solution, builds the web frontend, and installs desktop dependencies. The current frontend lint baseline has unrelated warnings/errors, so lint is opt-in until that baseline is cleaned up:

```bash
RUN_FRONTEND_LINT=1 scripts/preflight-desktop-release.sh
```

If you need to isolate the desktop bundling step locally afterward, run:

```bash
cd src/QuillForge.Desktop
npm ci
npx electron-builder --linux --publish never
```

Tag-triggered GitHub Actions intentionally skips the full test suite. It only validates tag/version/release config before building installers on hosted Linux/macOS/Windows runners and uploading release assets. If the release upload step is rerun, it must reuse the existing release and clobber/retry individual assets rather than deleting and recreating the release.

If you are validating release automation rather than only local shell behavior, also inspect `.github/workflows/release.yml` and confirm the matrix still covers:
- Linux AppImage output
- macOS `.app` plus `.dmg`
- Windows installer output

## Relationship To Synthetic Testing

This checklist is for release/install confidence. If you need a live feature smoke test of QuillForge behavior itself, use `docs/synthetic-testing.md` as the higher-level guide and add the desktop-specific checks from this document on top.
