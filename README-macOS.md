# Skua Mac: building and technical notes

Status: **experimental, working**. The client runs natively on Apple Silicon. The game runs in a separate
window (`Skua.FlashHost`: Electron 8 + PepperFlash) that talks to the client over a local WebSocket.
Tested on macOS 27: the game loads to the login screen, and calls and events flow both ways.

> **macOS 28 warning:** macOS 27 shows *"This version of Electron includes a component that will not open in
> macOS 28."* PepperFlash and Electron 8 are Intel-only and run through Rosetta. The Ruffle game host (below)
> is the replacement; it is the default from macOS 28 on.

## Game engine: Flash (Electron) or Ruffle

| | Flash (`Skua.FlashHost`) | Ruffle (`Skua.RuffleHost`, beta) |
|---|---|---|
| What runs the game | Electron 8 + PepperFlash, x64 under Rosetta | [Ruffle](https://ruffle.rs) 0.7.1 (Flash emulator, WebAssembly) in a native arm64 WKWebView |
| Needs | Rosetta, Artix Game Launcher | nothing extra |
| macOS 28 | won't open | works |

Choose with `SKUA_GAME_ENGINE=ruffle` or `SKUA_GAME_ENGINE=flash`, or put the word in a file (no environment
variable needed for the DMG app):

```bash
echo ruffle > ~/Library/Application\ Support/VibeSkua/game-engine
```

Without a choice, Skua Mac uses Flash up to macOS 27 and Ruffle from macOS 28 on. The bot sees no difference: both
hosts speak the same bridge protocol.

How the Ruffle host works: `SkuaRuffleHost` (`RuffleHost.swift`) is a small AppKit app with one WKWebView. It serves
`host.html`, Ruffle and skua.swf from `skua://game/`, a fixed origin, so the game's saved settings (Ruffle keeps them
in the page's local storage) survive restarts, and fetches `game.aq.com` for the page (`skua://game/proxy/...`),
because Artix's servers send no CORS headers. Ruffle can't open TCP sockets in a web view, so it connects to the game
server through `ws://127.0.0.1:<port>/sock/<host>/<port>`, which the client relays to the real server
(`FlashUtil.Ruffle.cs`, Artix hosts only). The game keeps running at full speed while the window is hidden or covered.

Known differences under Ruffle:
- Logging in and gameplay are not tested yet. Please report what breaks.
- The game's own "Save" username/password boxes don't stick: Ruffle allows those secure saves only for SWFs
  served over https. Skua's account manager is unaffected.
- `ExternalInterface.call('pre-load')` is caught through `load.valueOf` in `host.html` (Ruffle runs the call as
  `(pre - load)(...)`, Flash as `pre - load(...)`).
- `getGameObject` on a whole MovieClip returns null. Under Flash it throws; neither engine can serialize one.
- Fonts the game asks the system for (Calibri, Noto Sans, ...) fall back to Ruffle's built-in font.
- `SKUA_RUFFLE_INSPECT=1` makes the page inspectable in Safari's Develop menu.

## Requirements

- **.NET 10 SDK**. The official installer, or `dotnet-install.sh --channel 10.0` into `~/.dotnet`
  (then `export DOTNET_ROOT=$HOME/.dotnet`).
- **Node.js**, to install the Electron host.
- **Rosetta 2**: `softwareupdate --install-rosetta --agree-to-license`
- **Artix Game Launcher** in `/Applications`. Its `PepperFlashPlayer.plugin` is used at runtime and is not
  redistributed here. Override the path with `SKUA_PPAPI=/path/to/PepperFlashPlayer.plugin`.
- Java (optional, only for map item parsing via FFDec).

## Build the app

```bash
./build-mac.sh          # Build/macOS/Skua Mac.app
./build-mac.sh --dmg    # also the downloadable Skua-Mac-<version>.dmg
```

Users normally download the DMG from GitHub Releases instead. The **Release Skua Mac** workflow builds it on a
GitHub Apple Silicon runner: run it by hand for a test artifact, or push a `mac-v<version>` tag to publish a release.

This produces `Build/macOS/Skua Mac.app` (self-contained, about 320 MB, ad-hoc signed). Double-click it, or
drag it to `/Applications`. The client shows as **Skua Mac** and shrinks to its title and menu bar, and the game appears directly underneath,
docked to it: move or resize the Skua Mac bar and the game follows. Log in in the game. Set `SKUA_UNDOCKED=1` to
get a separate, freely movable game window instead.
On first launch macOS may ask to let Skua Mac find devices on the local network. The client and the game
window talk over a local connection.

## Development build

```bash
cd Skua.FlashHost && npm_config_arch=x64 npm install && cd ..
Skua.RuffleHost/build.sh    # Ruffle game host into Skua.RuffleHost/build (needs Xcode command line tools)
dotnet build Skua.App.Avalonia/Skua.App.Avalonia.csproj
cd Skua.App.Avalonia/bin/Debug/net10.0 && ./VibeSkua
```

## How it differs from Windows

| Area | Windows | macOS |
|---|---|---|
| Game | ActiveX Flash embedded in the client window | Game window docked under the client's menu bar (Electron 8 under Rosetta) |
| Multi-account | Tabbed host window with army controls | Skua Mac menu > New Skua Window (⌘N): each window is its own bot with its own game profile. The **Army** menu in any window controls all of them |
| Headless mode | 1x1 viewport | Hides the game window; the game keeps running |
| Hotkeys | Global (Win32) | Work while the client **or** its game window has focus |
| Screenshots (Discord) | GDI capture | Captured from the game window |
| Game window closed/crashed | n/a | Client asks whether to reopen it |
| Sound alerts | Tone beeps | System alert sound |
| Auto-update | Velopack | Not set up |

## Performance diagnostics

- `SKUA_BRIDGE_STATS=1`: prints game calls per second, by function, to stderr every 10 s.
- `SKUA_FPS_OVERLAY=1` (frame rate) or `SKUA_FPS_OVERLAY=dirty` (redrawn regions): Avalonia render overlays.
- `SKUA_RENDER=gl`: use Avalonia's OpenGL renderer instead of Metal (Metal is the default and much cheaper).

## Army control

The Windows tab host drives its clients with `WM_SKUA_*` window messages. On macOS every Skua Mac window is its own
process, so each one listens on a Unix domain socket, `~/Library/Application Support/VibeSkua/army/<pid>.sock`
(folder readable only by you). The sender lists the sockets, skips and removes ones whose process has quit, and
sends each a JSON line `{"Msg":1474,"W":99,"L":1,"Data":null}` using the same `WM_SKUA_*` numbers; the reply is one
number (for example 1/0 for "logged in?"). Code: `Skua.Core/AppStartup/ArmyChannel.cs` (transport),
`Skua.App.Avalonia/Views/MainWindow.Army.cs` (what each action does, shared with the Windows window procedure) and
`MainWindow.Unix.cs` (the Army menu). The army hotkeys use the same channel.

## Where things live

- Game profiles (Flash settings, saved logins): `~/Library/Application Support/skua-flashhost/instances/<n>`.
  Instance 1 is reused by the first running client, instance 2 by the second, and so on.
- Bridge code: `Skua.App.Avalonia/Flash/FlashUtil.Unix.cs` (C#) and `Skua.FlashHost/host.html` (JS); Ruffle:
  `Skua.App.Avalonia/Flash/FlashUtil.Ruffle.cs`, `Skua.RuffleHost/host.html` and `RuffleHost.swift`.

## Not yet verified

- Hotkeys pressed while the game window (rather than the Skua Mac bar) has focus.
- Army **Log in all windows**, **Send all to map/player** with logged-in accounts (start/stop scripts, load
  script and the option switches were tested across two windows).
