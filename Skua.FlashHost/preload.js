// Exposes the host's control channel to host.html. contextIsolation is off (Electron 8 + plugins),
// so this simply sets a global the page script can use.
const { ipcRenderer } = require('electron');

window.skuaHost = {
  // Control calls from .NET ("__show", "__hide", "__setTitle", "__screenshot") handled by main.js.
  invoke: (fn, args) => ipcRenderer.invoke(fn, ...(args || [])),
  // Key presses captured in the main process (they never reach the DOM while Flash has focus).
  onKey: (handler) => ipcRenderer.on('skua-key', (_e, spec) => handler(spec)),
};
