const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('quillforgeDesktop', {
  // Status / backend
  getStatus: () => ipcRenderer.invoke('shell:get-status'),
  restartBackend: () => ipcRenderer.invoke('shell:restart-backend'),
  setLanAccess: (enabled) => ipcRenderer.invoke('shell:set-lan-access', enabled),
  openWorkspace: () => ipcRenderer.invoke('shell:open-workspace'),
  openUrl: (url) => ipcRenderer.invoke('shell:open-url', url),
  onStatusUpdate: (callback) => {
    ipcRenderer.on('shell:status-update', (_event, data) => callback(data));
  },

  // Updates
  installUpdate: () => ipcRenderer.invoke('shell:install-update'),
  onUpdateStatus: (callback) => {
    ipcRenderer.on('shell:update-status', (_event, data) => callback(data));
  },
  onUpdateProgress: (callback) => {
    ipcRenderer.on('shell:update-progress', (_event, data) => callback(data));
  },
});
