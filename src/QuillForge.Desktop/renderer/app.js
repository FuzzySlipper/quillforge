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

  const ALL_STATES = [stateStarting, stateReady, stateFailed, stateExited];

  function showState(el) {
    ALL_STATES.forEach(s => s.classList.remove('active'));
    el.classList.add('active');
  }

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
        break;

      case 'exited':
        showState(stateExited);
        exitedMsgEl.textContent = status.message || 'The backend has stopped.';
        break;

      case 'stopped':
        showState(stateExited);
        exitedMsgEl.textContent = status.message || 'The backend has stopped.';
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
