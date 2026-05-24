/* ─── QuillForge shell overlay UI controller ─────────────────────────────── */

(function () {
  'use strict';

  // DOM refs
  const stateStarting = document.getElementById('state-starting');
  const stateReady    = document.getElementById('state-ready');
  const stateFailed   = document.getElementById('state-failed');
  const stateExited   = document.getElementById('state-exited');

  const startupMsgEl  = document.getElementById('startup-message');
  const errorMsgEl    = document.getElementById('error-message');
  const exitedMsgEl   = document.getElementById('exited-message');

  const btnRetry         = document.getElementById('btn-retry');
  const btnOpenWorkspace = document.getElementById('btn-open-workspace');
  const btnRestart       = document.getElementById('btn-restart');

  // Diagnostics DOM refs
  const failedDiagnosticsContainer = document.getElementById('failed-diagnostics');
  const failedDiagnosticsToggle    = document.getElementById('btn-toggle-failed-diagnostics');
  const failedDiagnosticsBody      = failedDiagnosticsContainer?.querySelector('.diagnostics-body');
  const failedDiagnosticsLog       = document.getElementById('failed-diagnostics-log');
  const failedDiagnosticsCount     = failedDiagnosticsContainer?.querySelector('.diagnostics-count');
  const failedDiagnosticsCopy      = document.getElementById('btn-copy-failed-diagnostics');

  const exitedDiagnosticsContainer = document.getElementById('exited-diagnostics');
  const exitedDiagnosticsToggle    = document.getElementById('btn-toggle-exited-diagnostics');
  const exitedDiagnosticsBody      = exitedDiagnosticsContainer?.querySelector('.diagnostics-body');
  const exitedDiagnosticsLog       = document.getElementById('exited-diagnostics-log');
  const exitedDiagnosticsCount     = exitedDiagnosticsContainer?.querySelector('.diagnostics-count');
  const exitedDiagnosticsCopy      = document.getElementById('btn-copy-exited-diagnostics');

  const ALL_STATES = [stateStarting, stateReady, stateFailed, stateExited];

  function showState(el) {
    ALL_STATES.forEach(s => s.classList.remove('active'));
    el.classList.add('active');
  }

  // ─── Diagnostics helpers ────────────────────────────────────────────────

  function renderDiagnostics(container, logEl, countEl, entries) {
    if (!container || !logEl) return;

    if (!entries || entries.length === 0) {
      container.classList.add('hidden');
      return;
    }

    container.classList.remove('hidden');
    if (countEl) {
      countEl.textContent = `${entries.length} entr${entries.length === 1 ? 'y' : 'ies'}`;
    }

    logEl.innerHTML = '';
    for (const entry of entries) {
      const div = document.createElement('div');
      div.className = 'entry';

      const sourceSpan = document.createElement('span');
      sourceSpan.className = 'source';
      sourceSpan.textContent = entry.source || 'unknown';

      const levelSpan = document.createElement('span');
      levelSpan.className = `level-${entry.level || 'info'}`;
      levelSpan.textContent = entry.level || 'info';

      const messageSpan = document.createElement('span');
      messageSpan.className = 'message';
      messageSpan.textContent = entry.message || '';

      div.appendChild(sourceSpan);
      div.appendChild(levelSpan);
      div.appendChild(document.createTextNode(' '));
      div.appendChild(messageSpan);
      logEl.appendChild(div);
    }
  }

  function diagnosticsToText(entries) {
    if (!entries || entries.length === 0) return '';
    return entries.map(e => `[${e.source || 'unknown'}] [${e.level || 'info'}] ${e.message || ''}`).join('\n');
  }

  function setupDiagnosticsToggle(toggleBtn, bodyEl, copyBtn, getEntries) {
    if (!toggleBtn || !bodyEl) return;

    toggleBtn.addEventListener('click', () => {
      const expanded = bodyEl.classList.toggle('expanded');
      toggleBtn.textContent = expanded ? 'Hide diagnostics' : 'Show diagnostics';
    });

    if (copyBtn) {
      copyBtn.addEventListener('click', async () => {
        const text = diagnosticsToText(getEntries());
        try {
          await navigator.clipboard.writeText(text);
          const original = copyBtn.textContent;
          copyBtn.textContent = 'Copied!';
          setTimeout(() => { copyBtn.textContent = original; }, 1200);
        } catch {
          // Fallback for environments without clipboard API
          const ta = document.createElement('textarea');
          ta.value = text;
          document.body.appendChild(ta);
          ta.select();
          document.execCommand('copy');
          document.body.removeChild(ta);
          const original = copyBtn.textContent;
          copyBtn.textContent = 'Copied!';
          setTimeout(() => { copyBtn.textContent = original; }, 1200);
        }
      });
    }
  }

  let lastFailedDiagnostics = [];
  let lastExitedDiagnostics = [];

  setupDiagnosticsToggle(
    failedDiagnosticsToggle,
    failedDiagnosticsBody,
    failedDiagnosticsCopy,
    () => lastFailedDiagnostics,
  );

  setupDiagnosticsToggle(
    exitedDiagnosticsToggle,
    exitedDiagnosticsBody,
    exitedDiagnosticsCopy,
    () => lastExitedDiagnostics,
  );

  // ─── Status update handler ──────────────────────────────────────────────

  function onStatusUpdate(status) {
    switch (status.phase) {
      case 'starting':
        showState(stateStarting);
        startupMsgEl.textContent = status.message || 'Starting QuillForge...';
        break;

      case 'ready':
        showState(stateReady);
        // Auto-hide after a brief display (1.5 seconds), then navigate
        setTimeout(() => {
          if (status.backendUrl) {
            window.location.href = status.backendUrl;
          }
        }, 1500);
        break;

      case 'failed':
        showState(stateFailed);
        errorMsgEl.textContent = status.message || 'Something went wrong.';
        lastFailedDiagnostics = status.diagnostics || [];
        renderDiagnostics(
          failedDiagnosticsContainer,
          failedDiagnosticsLog,
          failedDiagnosticsCount,
          lastFailedDiagnostics,
        );
        break;

      case 'exited':
        showState(stateExited);
        exitedMsgEl.textContent = status.message || 'The backend has stopped.';
        lastExitedDiagnostics = status.diagnostics || [];
        renderDiagnostics(
          exitedDiagnosticsContainer,
          exitedDiagnosticsLog,
          exitedDiagnosticsCount,
          lastExitedDiagnostics,
        );
        break;

      case 'stopped':
        showState(stateExited);
        exitedMsgEl.textContent = status.message || 'The backend has stopped.';
        lastExitedDiagnostics = status.diagnostics || [];
        renderDiagnostics(
          exitedDiagnosticsContainer,
          exitedDiagnosticsLog,
          exitedDiagnosticsCount,
          lastExitedDiagnostics,
        );
        break;

      default:
        // Unknown phase — show starting as fallback
        showState(stateStarting);
        startupMsgEl.textContent = 'Connecting...';
    }
  }

  // ─── Wire up button handlers ────────────────────────────────────────────

  btnRetry.addEventListener('click', () => {
    window.quillforgeDesktop.restartBackend().catch(console.error);
  });

  btnRestart.addEventListener('click', () => {
    window.quillforgeDesktop.restartBackend().catch(console.error);
  });

  btnOpenWorkspace.addEventListener('click', () => {
    window.quillforgeDesktop.openWorkspace().catch(console.error);
  });

  // ─── Listen for status events from main process ─────────────────────────

  if (window.quillforgeDesktop) {
    window.quillforgeDesktop.onStatusUpdate(onStatusUpdate);
  }

  // ─── Initial state is already "starting" via the HTML ───────────────────
  // Query for current status on load
  if (window.quillforgeDesktop) {
    window.quillforgeDesktop.getStatus().then(onStatusUpdate).catch(console.error);
  }
})();
