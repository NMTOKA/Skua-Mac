# VibeSkua on macOS

Status: **experimental, working**. The client runs natively on Apple Silicon. The game runs in a separate
window (`Skua.FlashHost`: Electron 8 + PepperFlash) that talks to the client over a local WebSocket.
Tested on macOS 27: the game loads to the login screen, and calls and events flow both ways.

> **macOS 28 warning:** macOS 27 shows *"This version of Electron includes a component that will not open in
> macOS 28."* PepperFlash and Electron 8 are Intel-only and run through Rosetta. Expect this game host to
> stop working on the next major macOS release unless a native backend (e.g. Ruffle) replaces it.

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
./build-mac.sh
```

This produces `Build/macOS/VibeSkua.app` (self-contained, about 320 MB, ad-hoc signed). Double-click it, or
drag it to `/Applications`. VibeSkua shrinks to its title and menu bar, and the game appears directly underneath,
docked to it: move or resize the VibeSkua bar and the game follows. Log in in the game. Set `SKUA_UNDOCKED=1` to
get a separate, freely movable game window instead.
On first launch macOS may ask to let VibeSkua find devices on the local network. The client and the game
window talk over a local connection.

## Development build

```bash
cd Skua.FlashHost && npm_config_arch=x64 npm install && cd ..
dotnet build Skua.App.Avalonia/Skua.App.Avalonia.csproj
cd Skua.App.Avalonia/bin/Debug/net10.0 && ./VibeSkua
```

## How it differs from Windows

| Area | Windows | macOS |
|---|---|---|
| Game | ActiveX Flash embedded in the client window | Game window docked under the client's menu bar (Electron 8 under Rosetta) |
| Multi-account | Tabbed host window | Run VibeSkua again: each instance gets its own game window and profile |
| Headless mode | 1x1 viewport | Hides the game window; the game keeps running |
| Hotkeys | Global (Win32) | Work while the client **or** its game window has focus |
| Screenshots (Discord) | GDI capture | Captured from the game window |
| Game window closed/crashed | n/a | Client asks whether to reopen it |
| Sound alerts | Tone beeps | System alert sound |
| Auto-update | Velopack | Not set up |

## Where things live

- Game profiles (Flash settings, saved logins): `~/Library/Application Support/skua-flashhost/instances/<n>`.
  Instance 1 is reused by the first running client, instance 2 by the second, and so on.
- Bridge code: `Skua.App.Avalonia/Flash/FlashUtil.Unix.cs` (C#) and `Skua.FlashHost/host.html` (JS).

## Not yet verified

- Running a full bot script while logged in
- Hotkeys pressed inside the game window (forwarding is implemented but untested)
