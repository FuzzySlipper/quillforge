# QuillForge Desktop Shell

This project hosts the first Tauri-based desktop shell for QuillForge. The shell keeps startup, backend supervision, and desktop-owned actions in the Tauri process while continuing to render the existing QuillForge web UI from the local .NET backend.

## Prerequisites

- Node.js and npm
- Rust toolchain
- .NET 10 SDK

## Development

From the repo root:

```bash
cd src/QuillForge.Desktop
npm install
npm run tauri:dev
```

What that does:

1. publishes `QuillForge.Web` as a self-contained single-file sidecar for the current host target
2. places the sidecar binary in `src-tauri/binaries/`
3. starts the local shell UI dev server
4. launches the Tauri desktop app

## Build

```bash
cd src/QuillForge.Desktop
npm install
npm run tauri:build
```

The shell expects the backend sidecar to be built through `npm run prepare:sidecar` first; the `tauri:dev` and `tauri:build` scripts already do that. For release packaging, the GitHub workflow builds stable desktop assets for Fedora RPM, Debian DEB, macOS zipped `.app` bundles plus DMGs, and the Windows NSIS installer.

## Current Behavior

- starts the backend sidecar with the desktop launch contract added in task `#692`
- passes an explicit desktop workspace path rooted at `Documents/QuillForge`
- waits for `/api/health/ready` before exposing the web UI
- keeps a shell-owned startup/error surface if the backend fails to launch or exits unexpectedly
- exposes desktop-owned actions to open the workspace and restart the backend

## Notes

- the shell currently defaults to loopback-only backend binding
- later tasks will add explicit LAN/mobile controls beyond the current shell
- release packaging now prioritizes Fedora RPM, Debian DEB, macOS `.app`/DMG, and the Windows installer with stable asset names for manual downloads or helper scripts
- Linux AppImage packaging is still a follow-up path; local Linux builds can stay on `.deb` unless you explicitly request additional bundles
