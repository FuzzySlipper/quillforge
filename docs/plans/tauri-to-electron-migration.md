# Tauri → Electron Desktop Migration Plan

> **Status:** Planning
> **Epic:** Migrate desktop wrapper from Tauri v2 to Electron, unifying QuillForge with sibling products already on Electron via the shared `den-bridge` submodule.
> **Motivation:** Linux webview behavior varies unpredictably across distros with Tauri's native webview. Electron provides a consistent Chromium shell everywhere. All sibling products already use Electron + den-bridge; standardizing reduces maintenance and unlocks shared improvements.

## Architecture

The architecture stays the same — only the shell changes:

```
Before (Tauri):
  Tauri Rust shell → sidecar: QuillForge.Web (.NET) → HTTP → React SPA

After (Electron):
  Electron JS shell → child_process: QuillForge.Web (.NET) → HTTP → React SPA
                                                                 ↕
                                          Den.Bridge WebSocket (for shell-bound IPC)
```

**What stays:**
- `QuillForge.Web` — unchanged .NET backend, launched as a sidecar with same CLI args
- `QuillForge.Web/Client/` — unchanged React SPA, loaded in Electron's BrowserWindow
- Backend launch contract — existing `--desktop-mode`, `--port`, `--bind-mode`, `--content-root`, `--desktop-instance-id`
- Health endpoints — `GET /api/health/ready` polling for readiness
- Workspace resolution — `~/Documents/QuillForge` default

**What changes:**
- `src/QuillForge.Desktop/` — replaced with Electron project
- Build chain — `npm run electron:build` instead of `npm run tauri:build`, no Rust/Cargo
- IPC — Electron `contextBridge`/`ipcRenderer` for shell-owned actions instead of Tauri IPC commands. Den.Bridge WebSocket available for richer protocol later.
- Release artifacts — Electron Builder produces `.AppImage`, `.dmg`, `.exe` installers
- CI/CD — `.github/workflows/release.yml` updated to Electron

## Tasks (Phased)

### Phase 1: Foundation

**Task 1: Add den-bridge submodule**
- `git submodule add https://github.com/FuzzySlipper/den-bridge.git lib/den-bridge`
- `dotnet build` still passes

**Task 2: Reference Den.Bridge in QuillForge.Web**
- Add `<ProjectReference Include="../../lib/den-bridge/src/Den.Bridge/Den.Bridge.csproj" />` to `QuillForge.Web.csproj`
- `dotnet build` passes

### Phase 2: Electron Shell

**Task 3: Create Electron project scaffold**
- New `src/QuillForge.Electron/` directory
- `package.json` with electron + electron-builder deps
- `main.js`: backend sidecar launch, readiness polling (500ms/30s), BrowserWindow, single-instance, cleanup
- `preload.js`: `contextBridge` exposing IPC channels for status, restart, LAN toggle, workspace open
- `renderer/`: shell status overlay (starting/ready/failed/exited states)
- Verify: `npm start` launches backend + app in Electron window

**Task 4: Build the shell UI overlay**
- Small HTML/CSS/JS overlay at `renderer/index.html`
- States: Starting (spinner), Ready (auto-hide), Failed (retry + workspace), Exited (restart)
- Matches QuillForge dark theme

**Task 5: Port Tauri IPC commands to Electron IPC**

| Tauri Command | Electron IPC | Logic |
|---|---|---|
| `get_shell_status` | `ipc:get-status` | Returns sidecar state |
| `restart_backend` | `ipc:restart-backend` | Kill and re-spawn |
| `set_lan_access_enabled` | `ipc:set-lan-access` | Update settings, restart |
| `open_workspace` | `ipc:open-workspace` | `shell.openPath` |
| `open_external_url` | `ipc:open-url` | `shell.openExternal` |

Settings: `app.getPath('userData')/desktop-settings.json` (same schema as Tauri)

### Phase 3: Strip Tauri

**Task 6: Remove Tauri project**
- Delete `src/QuillForge.Desktop/` entirely
- Remove all Rust/Cargo/Tauri files, icon assets, Tauri-specific npm scripts

**Task 7: Update build pipeline**
- Rename Electron dir to `QuillForge.Desktop` (or keep as `QuillForge.Electron`)
- Update `scripts/rundesktop.sh`
- Remove `scripts/tauri-build.mjs`, `scripts/prepare-sidecar.mjs`, `scripts/stage-release-assets.mjs`
- Update `docs/desktop-release-validation.md`

**Task 8: Update CI/CD**
- `.github/workflows/release.yml`: Replace Tauri matrix with Electron Builder
- Outputs: `.AppImage` (Linux), `.dmg` (macOS), `.exe` (Windows)

### Phase 4: Cleanup & Docs

**Task 9: Update documentation**
- `README.md` — swap Tauri prerequisites for Electron
- `docs/desktop-release-validation.md` — update artifact names, install commands
- `docs/desktop-host-sidecar-contract.md` — note Electron is the shell (contract is shell-agnostic)

**Task 10: Final integration test**
- Full Electron build
- Launch on Linux, verify backend + SPA + LAN toggle
- `dotnet test QuillForge.slnx` — all tests pass
- Tag and push

## Deleted (Tauri)

```
src/QuillForge.Desktop/
├── src-tauri/
│   ├── Cargo.toml       # 29 lines
│   ├── Cargo.lock
│   ├── build.rs
│   ├── tauri.conf.json  # 46 lines
│   ├── src/lib.rs       # ~1227 lines Rust lifecycle code
│   └── icons/
├── scripts/tauri-build.mjs, prepare-sidecar.mjs, stage-release-assets.mjs
├── package.json, package-lock.json
├── README.md
└── src/main.ts
```

## Created (Electron)

```
src/QuillForge.Desktop/
├── package.json
├── main.js              # ~300 lines
├── preload.js           # ~80 lines
├── renderer/
│   ├── index.html
│   ├── styles.css
│   └── app.js
└── build/
    └── entitlements.mac.plist
```

## Risks

| Risk | Mitigation |
|---|---|
| Electron bundle ~150MB vs Tauri ~30MB | Acceptable; users expect this |
| Linux distro packaging | .AppImage works everywhere |
| Den.Bridge TS packages not ready | Use direct Electron IPC initially; add WebSocket later |
| No sibling Electron skeleton to copy | Build from scratch; it's a ~300-line main process |

## Open Questions

1. Keep `src/QuillForge.Desktop` name or use `src/QuillForge.Electron` during transition?
2. Do sibling projects have an Electron skeleton you'd like mirrored?
3. Den.Bridge WebSocket — replace HTTP health polling or sit alongside it?
