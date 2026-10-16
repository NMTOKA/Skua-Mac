# VibeSkua on macOS

Status: **experimental**. The client UI builds and launches on Apple Silicon. The game runs in a separate
Electron 8 + PepperFlash window (`Skua.FlashHost`), bridged to the client over a local WebSocket.

## Requirements

- **.NET 10 SDK** (`dotnet-install.sh --channel 10.0`, or the official installer)
- **Node.js** (to install the Electron host)
- **Rosetta 2**, because PepperFlash is Intel-only: `softwareupdate --install-rosetta`
- **Artix Game Launcher** in `/Applications`. Its `PepperFlashPlayer.plugin` is used at runtime and is not
  redistributed here. Override the path with `SKUA_PPAPI=/path/to/PepperFlashPlayer.plugin`.
- Java (optional, only for map item parsing via FFDec)

## Build and run

```bash
cd Skua.FlashHost && npm_config_arch=x64 npm install && cd ..
dotnet build Skua.App.Avalonia/Skua.App.Avalonia.csproj
cd Skua.App.Avalonia/bin/Debug/net10.0 && ./VibeSkua
```

If .NET is installed in your home folder, export `DOTNET_ROOT=$HOME/.dotnet` first.

## How it differs from Windows

| Area | Windows | macOS |
|---|---|---|
| Game | ActiveX Flash embedded in the window | Separate `Skua.FlashHost` window (Electron 8, Rosetta) |
| Multi-account | Tabbed host re-parents child windows (Win32) | One client per window; launch more instances instead |
| Global hotkeys | Win32 | Only while a Skua window has focus |
| Screenshots | GDI capture | Not implemented yet |
| Auto-update | Velopack | Not set up |

## Not yet verified

The Flash host and bridge haven't been tested end to end yet (they need Rosetta). Expect fixes after the
first real run.
