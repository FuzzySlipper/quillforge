# Desktop Release Validation

Use this checklist before publishing or announcing a desktop release. It is intentionally concrete and a little repetitive so a sleepy maintainer can work through it without inventing steps on the fly.

## Release Artifacts

For the tagged release you expect to publish, confirm GitHub Actions produced the current first-wave desktop assets:

- `QuillForge-fedora-x86_64.rpm`
- `QuillForge-debian-amd64.deb`
- `QuillForge-macos-<arch>.app.zip`
- `QuillForge-macos-<arch>.dmg`
- `QuillForge-windows-x64-setup.exe`

Quick sanity checks:
- artifact filenames match the stable names documented in `README.md`
- the release notes do not still describe the old raw-backend tarball flow
- the artifact sizes are plausible for a bundled desktop app, not suspiciously tiny

## User-Facing Install Smoke Test

Run at least one real install path for the platforms you care about most.

### Fedora

1. Install the RPM with `sudo dnf install ./QuillForge-fedora-x86_64.rpm`.
2. Launch QuillForge from the app launcher.
3. Confirm the desktop shell appears instead of a terminal-plus-browser workflow.
4. Confirm the first run creates `~/Documents/QuillForge`.
5. Close and relaunch to confirm the installed app still opens the same workspace.

### Debian Or Ubuntu

1. Install the DEB with `sudo apt install ./QuillForge-debian-amd64.deb`.
2. Launch QuillForge from the desktop environment menu.
3. Confirm the shell opens and uses `~/Documents/QuillForge`.
4. Restart once to make sure the installed app still finds the same workspace.

### macOS

1. Download either the zipped `.app` bundle or the `.dmg`.
2. Move `QuillForge.app` into `Applications`.
3. Launch it once.
4. If Gatekeeper blocks it, use `System Settings -> Privacy & Security -> Open Anyway`.
5. Confirm the app launches after that override and creates `~/Documents/QuillForge`.
6. Relaunch from `Applications` to make sure the override path is no longer confusing the user.

### Windows

1. Run `QuillForge-windows-x64-setup.exe`.
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

- Fedora: run `sudo dnf install https://github.com/FuzzySlipper/quillforge/releases/latest/download/QuillForge-fedora-x86_64.rpm`
- Debian/Ubuntu: install a newer DEB over the old app
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

Before tagging a release, make sure the core desktop build commands still succeed locally in the environment you are using:

```bash
cd src/QuillForge.Desktop
npm install
npm run ui:build
cargo check --manifest-path src-tauri/Cargo.toml
npm run tauri:build -- --debug --bundles deb
```

If you are validating release automation rather than only local shell behavior, also inspect `.github/workflows/release.yml` and confirm the matrix still covers:
- Fedora-style RPM output
- Debian-style DEB output
- macOS `.app` plus `.dmg`
- Windows installer output

## Relationship To Synthetic Testing

This checklist is for release/install confidence. If you need a live feature smoke test of QuillForge behavior itself, use `docs/synthetic-testing.md` as the higher-level guide and add the desktop-specific checks from this document on top.
