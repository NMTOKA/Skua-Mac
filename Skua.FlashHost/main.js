// VibeSkua macOS game host.
//
// Launched by MacFlashUtil (Skua.App.Avalonia/Flash/FlashUtil.Unix.cs) with:
//   --port=<n>       WebSocket port the .NET client listens on (127.0.0.1)
//   --swf=<path>     absolute path to skua.swf
//   --ppapi=<path>   optional PepperFlashPlayer.plugin path (defaults to the Artix Game Launcher's copy)
//   --title=<text>   optional window title
//   --docked         frameless window positioned by the client under its toolbar (no Dock icon)
//
// PepperFlash is x86_64-only, so this runs as Electron 8 (x64) under Rosetta 2.

const { app, BrowserWindow, Menu, ipcMain } = require('electron');
const fs = require('fs');
const os = require('os');
const path = require('path');
const url = require('url');

const DEFAULT_PPAPI = '/Applications/Artix Game Launcher.app/Contents/Resources/plugins/PepperFlashPlayer.plugin';
const PPAPI_VERSION = '32.0.0.344';

function arg(name, fallback) {
  const prefix = `--${name}=`;
  const hit = process.argv.find(a => a.startsWith(prefix));
  return hit ? hit.substring(prefix.length) : fallback;
}

const port = parseInt(arg('port', '0'), 10);
const swfPath = path.resolve(arg('swf', path.join(__dirname, 'skua.swf')));
const ppapiPath = arg('ppapi', DEFAULT_PPAPI);
const title = arg('title', 'VibeSkua - Game');
const docked = process.argv.includes('--docked');

if (!port) {
  console.error('flashhost: missing --port');
  app.exit(2);
}
if (!fs.existsSync(ppapiPath)) {
  console.error(`flashhost: PepperFlash not found at ${ppapiPath}. Install the Artix Game Launcher or pass --ppapi.`);
  app.exit(3);
}
if (!fs.existsSync(swfPath)) {
  console.error(`flashhost: skua.swf not found at ${swfPath}`);
  app.exit(4);
}

// One Chromium profile per running client (a profile is locked by the host using it). Slots are reused
// across launches so the game's saved settings stick: the first slot whose owner process is gone wins.
function claimProfileSlot() {
  const root = path.join(app.getPath('appData'), 'skua-flashhost', 'instances');
  for (let slot = 1; ; slot++) {
    const dir = path.join(root, String(slot));
    const pidFile = path.join(dir, 'host.pid');
    try {
      const owner = parseInt(fs.readFileSync(pidFile, 'utf8'), 10);
      if (owner && owner !== process.pid) {
        process.kill(owner, 0); // throws if the process is gone
        continue;
      }
    } catch (e) { /* missing file or dead owner: free */ }
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(pidFile, String(process.pid));
    return dir;
  }
}
app.setPath('userData', claimProfileSlot());

app.commandLine.appendSwitch('ppapi-flash-path', ppapiPath);
app.commandLine.appendSwitch('ppapi-flash-version', PPAPI_VERSION);
app.commandLine.appendSwitch('ignore-gpu-blacklist');

// Give skua.swf (and the generated page) the local-trusted sandbox, the same way
// FlashTrustManager does on Windows, so it can load game.aq.com content and use ExternalInterface.
function writeTrustFiles(paths) {
  const roots = [
    path.join(app.getPath('userData'), 'Pepper Data', 'Shockwave Flash', 'WritableRoot', '#Security', 'FlashPlayerTrust'),
    path.join(os.homedir(), 'Library', 'Preferences', 'Macromedia', 'Flash Player', '#Security', 'FlashPlayerTrust'),
  ];
  for (const root of roots) {
    try {
      fs.mkdirSync(root, { recursive: true });
      fs.writeFileSync(path.join(root, 'VibeSkua.cfg'), paths.join('\n') + '\n');
    } catch (e) {
      console.error(`flashhost: could not write trust file in ${root}: ${e.message}`);
    }
  }
}

function writeHostPage() {
  const dir = path.join(app.getPath('userData'), 'host');
  fs.mkdirSync(dir, { recursive: true });
  const template = fs.readFileSync(path.join(__dirname, 'host.html'), 'utf8');
  const page = template
    .replace(/__SWF_URL__/g, url.pathToFileURL(swfPath).href)
    .replace(/__PORT__/g, String(port));
  const pagePath = path.join(dir, 'host.html');
  fs.writeFileSync(pagePath, page);
  return { dir, pagePath };
}

app.on('ready', () => {
  const { dir, pagePath } = writeHostPage();
  writeTrustFiles([path.dirname(swfPath), dir]);

  Menu.setApplicationMenu(null);
  // Docked: the client window is the only one with a title bar and Dock icon; this window sits under it.
  if (docked && app.dock) app.dock.hide();
  const win = new BrowserWindow({
    width: 958,
    height: 550,
    title,
    backgroundColor: '#000000',
    useContentSize: true,
    frame: !docked,
    resizable: !docked,
    fullscreenable: !docked,
    webPreferences: {
      plugins: true,
      nodeIntegration: false,
      contextIsolation: false,
      backgroundThrottling: false,
      preload: path.join(__dirname, 'preload.js'),
    },
  });
  registerControlHandlers(win);
  forwardHotkeys(win);
  win.on('page-title-updated', e => e.preventDefault());
  win.webContents.on('console-message', (_e, _level, message) => console.log(`[page] ${message}`));
  win.webContents.on('plugin-crashed', (_e, name, version) => {
    console.error(`flashhost: plugin crashed: ${name} ${version}`);
    app.exit(5);
  });
  win.loadFile(pagePath);
});

// Control calls from .NET (see FlashUtil.Unix.cs), routed through host.html.
function registerControlHandlers(win) {
  let hiddenByClient = false;
  ipcMain.handle('__show', () => { hiddenByClient = false; win.show(); return 'true'; });
  ipcMain.handle('__hide', () => { hiddenByClient = true; win.hide(); return 'true'; });
  // x, y, width, height in screen points (top-left origin).
  ipcMain.handle('__dock', (_e, x, y, width, height) => {
    win.setBounds({ x: Math.round(x), y: Math.round(y), width: Math.round(width), height: Math.round(height) });
    return 'true';
  });
  // Bring the window above other apps' windows without taking focus (the client was just activated).
  ipcMain.handle('__raise', () => { if (!hiddenByClient) win.showInactive(); return 'true'; });
  ipcMain.handle('__focus', () => { win.show(); app.focus({ steal: true }); win.focus(); return 'true'; });
  ipcMain.handle('__setTitle', (_e, text) => { win.setTitle(String(text)); return 'true'; });
  ipcMain.handle('__screenshot', async () => {
    const image = await win.webContents.capturePage();
    return image.toPNG().toString('base64');
  });
}

// Flash swallows keyboard input inside the plugin, so read keys at the browser level and pass them on
// for VibeSkua's hotkeys. The key still reaches the game.
function forwardHotkeys(win) {
  win.webContents.on('before-input-event', (_e, input) => {
    if (input.type !== 'keyDown' || input.isAutoRepeat || !input.code) return;
    const mods = (input.control || input.meta ? 'c' : '') + (input.shift ? 's' : '') + (input.alt ? 'a' : '');
    win.webContents.send('skua-key', `${input.code}|${mods}`);
  });
}

app.on('window-all-closed', () => app.quit());
