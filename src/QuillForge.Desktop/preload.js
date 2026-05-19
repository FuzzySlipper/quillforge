const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('quillforgeDesktop', {
  getStatus: () => ipcRenderer.invoke('shell:get-status'),
  restartBackend: () => ipcRenderer.invoke('shell:restart-backend'),
  setLanAccess: (enabled) => ipcRenderer.invoke('shell:set-lan-access', enabled),
  openWorkspace: () => ipcRenderer.invoke('shell:open-workspace'),
  openUrl: (url) => ipcRenderer.invoke('shell:open-url', url),
  onStatusUpdate: (callback) => {
    ipcRenderer.on('shell:status-update', (_event, data) => callback(data));
  },
});
