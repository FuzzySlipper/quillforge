const { app, BrowserWindow, ipcMain, shell, nativeImage, dialog } = require('electron');
const { spawn } = require('child_process');
const http = require('http');
const path = require('path');
const fs = require('fs');
const net = require('net');

// ─── Constants ───────────────────────────────────────────────────────────────

const MAIN_WINDOW_LABEL = 'main';
const STATUS_EVENT = 'shell:status-update';
const READY_POLL_INTERVAL_MS = 500;
const READY_TIMEOUT_MS = 30_000;
const BACKEND_START_ATTEMPTS = 3;
const BACKEND_START_RETRY_MS = 250;
const SETTINGS_FILE_NAME = 'desktop-settings.json';
const MAX_DIAGNOSTIC_ENTRIES = 200;

// ─── Helpers ─────────────────────────────────────────────────────────────────

function loopbackUrlForPort(port) {
  return `http://127.0.0.1:${port}`;
}

function reservePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.listen(0, '127.0.0.1', () => {
      const port = server.address().port;
      server.close(() => resolve(port));
    });
    server.on('error', reject);
  });
}

function resolveWorkspacePath() {
  const explicit = process.env.QUILLFORGE_DESKTOP_CONTENT_ROOT;
  if (explicit && explicit.trim()) {
    return path.resolve(explicit.trim());
  }

  const docsDir = app.getPath('documents');
  const candidate = path.join(docsDir, 'QuillForge');
  try {
    fs.mkdirSync(candidate, { recursive: true });
    return candidate;
  } catch {
    return path.join(app.getPath('home'), 'Documents', 'QuillForge');
  }
}

function resolveBackendPath() {
  // In dev: env var, then look relative to the repo
  if (process.env.QUILLFORGE_BACKEND_PATH) {
    return process.env.QUILLFORGE_BACKEND_PATH;
  }

  // Dev-mode fallback: look for dotnet publish output
  // We try the current dir and walk up looking for .NET output
  const candidates = [
    path.join(__dirname, '..', '..', '..', 'publish'),
    path.join(__dirname, '..', '..', '..', 'src', 'QuillForge.Web', 'bin', 'Debug', 'net10.0'),
  ];

  for (const candidate of candidates) {
    const indexHtml = path.join(candidate, 'wwwroot', 'index.html');
    if (fs.existsSync(indexHtml)) {
      return candidate;
    }
  }

  // Production: relative to the Electron app resources
  const resourcePath = path.join(process.resourcesPath, 'backend-payload');
  if (fs.existsSync(path.join(resourcePath, 'wwwroot', 'index.html'))) {
    return resourcePath;
  }

  // Last resort: assume it's the asar dir for bundled dev
  return path.join(__dirname);
}

// ─── Settings ────────────────────────────────────────────────────────────────

function settingsPath() {
  return path.join(app.getPath('userData'), SETTINGS_FILE_NAME);
}

function loadSettings() {
  try {
    const data = fs.readFileSync(settingsPath(), 'utf8');
    const parsed = JSON.parse(data);
    return { bindMode: parsed.bindMode === 'lan' ? 'lan' : 'loopback' };
  } catch {
    return { bindMode: 'loopback' };
  }
}

function saveSettings(settings) {
  const dir = path.dirname(settingsPath());
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(settingsPath(), JSON.stringify({ bindMode: settings.bindMode }, null, 2) + '\n');
}

// ─── Diagnostic entries ──────────────────────────────────────────────────────

function pushDiagnostic(diagnostics, level, source, message) {
  if (!message || !message.trim()) return;
  diagnostics.push({ level, source, message: String(message) });
  if (diagnostics.length > MAX_DIAGNOSTIC_ENTRIES) {
    diagnostics.splice(0, diagnostics.length - MAX_DIAGNOSTIC_ENTRIES);
  }
}

// ─── Status ──────────────────────────────────────────────────────────────────

function buildStatus(phase, opts = {}) {
  const { message, backendUrl, workspacePath, port, bindMode, restartAvailable } = opts;
  const resolvedBindMode = bindMode || 'loopback';
  const loopbackUrl = port != null ? loopbackUrlForPort(port) : null;
  const diagnostics = opts.diagnostics || [];

  return {
    phase,
    message: message || null,
    backendUrl: backendUrl || null,
    workspacePath: workspacePath || '',
    port: port != null ? port : null,
    bindMode: resolvedBindMode,
    loopbackUrl,
    lanUrl: null,
    restartAvailable: restartAvailable !== false,
    diagnostics,
  };
}

// ─── State ──────────────────────────────────────────────────────────────────

let runtimeState = {
  generation: 0,
  childProcess: null,
  childKilled: false,
  childExitHandler: null,
  status: buildStatus('starting', {
    message: 'Preparing QuillForge desktop startup.',
    workspacePath: resolveWorkspacePath(),
  }),
  settings: loadSettings(),
  diagnostics: [],
  shuttingDown: false,
};

function emitStatus() {
  for (const win of BrowserWindow.getAllWindows()) {
    win.webContents.send(STATUS_EVENT, runtimeState.status);
  }
}

function setStatus(newStatus) {
  newStatus.diagnostics = [...runtimeState.diagnostics];
  runtimeState.status = newStatus;
  emitStatus();
}

// ─── Backend lifecycle ───────────────────────────────────────────────────────

async function startBackend() {
  if (runtimeState.shuttingDown) return;

  runtimeState.generation++;
  const generation = runtimeState.generation;
  const workspacePath = resolveWorkspacePath();
  const bindMode = runtimeState.settings.bindMode;

  // Kill any existing backend child
  killExistingChild();

  // Get the backend payload directory
  const backendDir = resolveBackendPath();

  const desktopInstanceId = `${process.pid}-${Date.now()}`;

  for (let attempt = 1; attempt <= BACKEND_START_ATTEMPTS; attempt++) {
    if (runtimeState.shuttingDown || generation !== runtimeState.generation) return;

    const port = await reservePort();
    const backendUrl = loopbackUrlForPort(port);

    const startupMsg = attempt === 1
      ? 'Launching the QuillForge backend...'
      : `Launching the QuillForge backend... Retrying backend launch on a new port (${attempt}/${BACKEND_START_ATTEMPTS}).`;

    pushDiagnostic(runtimeState.diagnostics, 'info', 'shell', startupMsg);

    setStatus(buildStatus('starting', {
      message: startupMsg,
      workspacePath,
      port,
      bindMode,
    }));

    const args = [
      '--desktop-mode',
      '--content-root', workspacePath,
      '--runtime-root', backendDir,
      '--bind-mode', bindMode,
      '--port', String(port),
      '--desktop-instance-id', desktopInstanceId,
      '--open-browser', 'false',
    ];

    const child = spawn(
      path.join(backendDir, 'QuillForge.Web'),
      args,
      { cwd: backendDir, stdio: ['ignore', 'pipe', 'pipe'] }
    );

    // Forward stdout/stderr to diagnostics
    child.stdout.on('data', (data) => {
      const lines = data.toString().split('\n').filter(l => l.trim());
      for (const line of lines) {
        const level = classifyLevel(line);
        pushDiagnostic(runtimeState.diagnostics, level, 'backend', line);
        updateStatusDiagnostics();
      }
    });

    child.stderr.on('data', (data) => {
      const lines = data.toString().split('\n').filter(l => l.trim());
      for (const line of lines) {
        const level = classifyLevel(line);
        pushDiagnostic(runtimeState.diagnostics, level, 'backend', line);
        updateStatusDiagnostics();
      }
    });

    child.on('error', (err) => {
      const msg = `Unable to launch the QuillForge backend sidecar: ${err.message}`;
      pushDiagnostic(runtimeState.diagnostics, 'error', 'shell', msg);
      if (generation === runtimeState.generation) {
        setStatus(buildStatus('failed', {
          message: msg,
          workspacePath,
          port,
          bindMode,
          diagnostics: runtimeState.diagnostics,
        }));
      }
    });

    let childExitedDuringStartup = false;
    child.on('exit', (code, signal) => {
      childExitedDuringStartup = true;
      if (generation === runtimeState.generation && !runtimeState.shuttingDown) {
        handleBackendExit(generation, workspacePath, port, bindMode, code, signal);
      }
    });

    runtimeState.childProcess = child;

    // Poll for readiness
    const ready = await pollForReadiness(backendUrl, child, generation);
    if (!ready) {
      killExistingChild();
      if (!childExitedDuringStartup && generation === runtimeState.generation) {
        if (attempt < BACKEND_START_ATTEMPTS) {
          await sleep(BACKEND_START_RETRY_MS);
          continue;
        }
        const msg = `Unable to launch the QuillForge backend sidecar.`;
        pushDiagnostic(runtimeState.diagnostics, 'error', 'shell', msg);
        setStatus(buildStatus('failed', {
          message: msg,
          workspacePath,
          port,
          bindMode,
          diagnostics: runtimeState.diagnostics,
        }));
        return;
      }
      return;
    }

    // ── Backend is ready ──
    const readyMsg = bindMode === 'lan'
      ? 'LAN/mobile access is enabled for this run.'
      : 'The local QuillForge backend is ready.';
    pushDiagnostic(runtimeState.diagnostics, 'info', 'shell', readyMsg);

    setStatus(buildStatus('ready', {
      message: readyMsg,
      backendUrl,
      workspacePath,
      port,
      bindMode,
    }));

    // Create the main window
    createMainWindow(backendUrl);

    // Watch for unexpected exit
    child.removeAllListeners('exit');
    child.on('exit', (code, signal) => {
      if (generation === runtimeState.generation && !runtimeState.shuttingDown) {
        handleBackendExit(generation, workspacePath, port, bindMode, code, signal);
      }
    });

    return;
  }
}

function killExistingChild() {
  if (runtimeState.childProcess) {
    runtimeState.childProcess.removeAllListeners('exit');
    runtimeState.childProcess.kill();
    runtimeState.childProcess = null;
  }
}

function handleBackendExit(generation, workspacePath, port, bindMode, code, signal) {
  if (runtimeState.shuttingDown) {
    pushDiagnostic(runtimeState.diagnostics, 'info', 'shell', 'Shutting down the QuillForge backend.');
    setStatus(buildStatus('stopped', {
      message: 'Shutting down the QuillForge backend.',
      workspacePath,
      bindMode,
      restartAvailable: false,
    }));
    return;
  }

  let message;
  if (code != null) {
    message = `The backend exited unexpectedly with code ${code}.`;
  } else if (signal != null) {
    message = `The backend stopped unexpectedly with signal ${signal}.`;
  } else {
    message = 'The backend stopped unexpectedly.';
  }

  pushDiagnostic(runtimeState.diagnostics, 'error', 'shell', message);
  runtimeState.childProcess = null;
  setStatus(buildStatus('exited', {
    message,
    workspacePath,
    port,
    bindMode,
  }));
}

function updateStatusDiagnostics() {
  runtimeState.status.diagnostics = [...runtimeState.diagnostics];
  emitStatus();
}

async function pollForReadiness(backendUrl, child, generation) {
  const readyUrl = `${backendUrl}/api/health/ready`;
  const deadline = Date.now() + READY_TIMEOUT_MS;

  while (Date.now() < deadline) {
    if (runtimeState.shuttingDown || generation !== runtimeState.generation) return false;

    // Check if child exited
    if (child.killed) {
      return false;
    }

    try {
      const ok = await httpGet(readyUrl);
      if (ok) return true;
    } catch {
      // Connection refused or timeout — still starting
    }

    await sleep(READY_POLL_INTERVAL_MS);
  }

  return false;
}

function httpGet(url) {
  return new Promise((resolve) => {
    const req = http.get(url, { timeout: 3000 }, (res) => {
      // Consume response data
      res.resume();
      resolve(res.statusCode === 200);
    });
    req.on('error', () => resolve(false));
    req.on('timeout', () => {
      req.destroy();
      resolve(false);
    });
  });
}

function classifyLevel(line) {
  const trimmed = line.trimStart();
  if (trimmed.startsWith('error:') || trimmed.startsWith('fail:')) return 'error';
  if (trimmed.startsWith('warn:')) return 'warning';
  return 'info';
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// ─── Window ──────────────────────────────────────────────────────────────────

let mainWindow = null;

function createMainWindow(backendUrl) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.loadURL(backendUrl);
    return;
  }

  mainWindow = new BrowserWindow({
    width: 1440,
    height: 960,
    minWidth: 1100,
    minHeight: 760,
    resizable: true,
    fullscreen: false,
    title: 'QuillForge',
    show: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  // Show shell overlay while loading
  mainWindow.loadFile(path.join(__dirname, 'renderer', 'index.html'));

  mainWindow.once('ready-to-show', () => {
    mainWindow.show();
  });

  // Once backend is ready, navigate to it
  if (backendUrl) {
    mainWindow.loadURL(backendUrl);
  }

  mainWindow.on('closed', () => {
    mainWindow = null;
  });
}

function ensureMainWindow() {
  if (!mainWindow || mainWindow.isDestroyed()) {
    createMainWindow(null);
  }
  return mainWindow;
}

function navigateToBackend(url) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.loadURL(url);
  }
}

// ─── IPC Handlers ────────────────────────────────────────────────────────────

ipcMain.handle('shell:get-status', () => {
  return runtimeState.status;
});

ipcMain.handle('shell:restart-backend', async () => {
  startBackend();
  return { success: true };
});

ipcMain.handle('shell:set-lan-access', async (_event, enabled) => {
  const bindMode = enabled ? 'lan' : 'loopback';
  runtimeState.settings.bindMode = bindMode;
  saveSettings(runtimeState.settings);

  const msg = enabled
    ? 'Restarting the QuillForge backend with LAN/mobile access enabled...'
    : 'Restarting the QuillForge backend in local-only mode...';

  pushDiagnostic(runtimeState.diagnostics, 'info', 'shell', msg);
  setStatus(buildStatus('starting', {
    message: msg,
    workspacePath: resolveWorkspacePath(),
    bindMode,
  }));

  startBackend();
  return { success: true };
});

ipcMain.handle('shell:open-workspace', async () => {
  const wsPath = runtimeState.status.workspacePath;
  if (wsPath) {
    shell.openPath(wsPath);
  }
  return { success: true };
});

ipcMain.handle('shell:open-url', async (_event, url) => {
  if (!url) return { success: false, error: 'URL is required' };

  try {
    const parsed = new URL(url);
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
      return { success: false, error: 'Only http:// and https:// URLs can be opened from the desktop shell.' };
    }
    if (!parsed.hostname) {
      return { success: false, error: `URL '${url}' is missing a host.` };
    }
    shell.openExternal(url);
    return { success: true };
  } catch (err) {
    return { success: false, error: `Invalid URL '${url}': ${err.message}` };
  }
});

// ─── App lifecycle ───────────────────────────────────────────────────────────

const gotSingleInstanceLock = app.requestSingleInstanceLock();
if (!gotSingleInstanceLock) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (mainWindow && !mainWindow.isDestroyed()) {
      if (mainWindow.isMinimized()) mainWindow.restore();
      mainWindow.show();
      mainWindow.focus();
    }
  });
}

app.whenReady().then(() => {
  // Set app name for user data path
  app.setName('QuillForge');

  // Load settings
  runtimeState.settings = loadSettings();

  runtimeState.status = buildStatus('starting', {
    message: 'Preparing QuillForge desktop startup.',
    workspacePath: resolveWorkspacePath(),
    bindMode: runtimeState.settings.bindMode,
  });

  // Create window with shell overlay
  createMainWindow(null);

  // Launch backend
  startBackend();
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit();
  }
});

app.on('will-quit', () => {
  runtimeState.shuttingDown = true;
  killExistingChild();
});

app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) {
    createMainWindow(null);
  }
});

// Prevent multiple instance via module caching — already handled by requestSingleInstanceLock
