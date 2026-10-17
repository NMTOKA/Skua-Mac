# Skua Mac

**Skua Mac** is a macOS port of [VibeSkua](https://github.com/NinjaXz/VibeSkua) for Apple Silicon Macs. It runs as a native Mac app: no virtual machine, no Windows. Community scripts run unchanged, and a full script run has been tested start to finish.

Build and setup instructions are in [README-macOS.md](README-macOS.md). The original VibeSkua README follows below.

## What was changed to make it work on Mac

**Running the game.** VibeSkua shows the game through Windows' ActiveX Flash control, which doesn't exist on macOS. Skua Mac runs the game in a small Electron window with PepperFlash, the same setup the official Artix Game Launcher uses on Mac (its Flash player is borrowed from your launcher install). A local bridge (`FlashUtil.Unix.cs` + `Skua.FlashHost`) connects the bot to the game and matches the Windows behaviour exactly, so scripts and the bot core don't need changes. Each game call takes about 0.4 ms.

**One window.** The Skua Mac bar shrinks to its title and menus, and the game window docks underneath it and moves, resizes and minimizes with it. Headless mode hides the game, hotkeys work from either window, Discord screenshots come from the game window, and a closed or crashed game window can be reopened. For multiple accounts, open the app again; each copy gets its own game profile.

**Scripts.** Community scripts use Windows-only pieces, so two compatibility fixes were added. A small stand-in for Windows Forms (`Skua.Compat.WinForms`) lets `CoreBots.cs` compile. On macOS, CoreBots' "requires Windows 10" check is reworded before compiling so it no longer stops every script. Script files themselves are never edited.

**Crash fixes.** The file pickers (Load Script and others), Open VSCode, the themes folder button and the army hotkeys all called Windows-only code and crashed on Mac; each now has a Mac version. A frozen window caused by the bridge was fixed, and any button that still fails now shows an error instead of closing the app.

**Performance.** On the login screen the bot dropped from about 24% to 2% of a CPU core, and the game window from 19% to 7%. The fixes: a hidden loading bar that kept animating, Apple's Metal renderer instead of OpenGL, and fewer background status checks. CPU use is higher in game, but the same fixes apply.

**Packaging.** `./build-mac.sh` builds a self-contained, double-clickable `Skua Mac.app`. Every push is built on both Windows and macOS by GitHub Actions, so Mac changes can't break the Windows build. Windows behaviour is otherwise unchanged.

> Botting is against AQW's rules. Use an alt account if you don't want to risk your main.


## Credits & Acknowledgements

* **[auqw/skua](https://github.com/auqw/skua)** — Original Skua framework and core botting API.
* **[ArrowDev](https://github.com/ArrowDev123/Skua) (BlackArrow on Discord)** — Early Avalonia UI port foundation.
* **NinjaXz** — Client overhaul: engineered the multi-client tabbed host, army controls & grid view, headless mode, autonomous script scheduler, repaired broken subsystems & crash loops, and modernized the desktop UI with Avalonia.

---

### Copyright & Disclaimer

**Educational & Personal Use Only:** This project is a derivative of [auqw/skua](https://github.com/auqw/skua) and is provided "as-is" under the MIT License. I do not claim ownership of the original assets, game data, or the intellectual property of the game developers.
 
**Disclaimer:** Use of this software may violate the Terms of Service of the associated game. The author assumes no responsibility for any account actions, bans, or other consequences taken by game developers against users of this software. By using this tool, you acknowledge that you do so entirely at your own risk. If your PC decides to commit a toaster bath, that is not my problem.
