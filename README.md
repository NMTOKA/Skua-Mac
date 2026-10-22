# Skua Mac

**Skua Mac** is the AQW bot client [VibeSkua](https://github.com/NinjaXz/VibeSkua), made to run natively on Apple Silicon Macs (M1 and newer). No virtual machine, no Windows, no coding.

> Skua Mac is a macOS port of **[VibeSkua](https://github.com/NinjaXz/VibeSkua) by NinjaXz**, which is built on **[Skua](https://github.com/auqw/skua)**. All the bot features come from those projects; this one makes them work on a Mac. See [Credits](#credits--acknowledgements).

## Install

1. **Download** the latest `Skua-Mac-….dmg` from the [Releases page](https://github.com/NMTOKA/Skua-Mac/releases/latest).
2. **Open the file** and drag **Skua Mac** onto the **Applications** folder.
3. **Install the [Artix Game Launcher](https://www.artix.com/downloads/artixlauncher/)** if you don't have it. It's free, and Skua Mac uses its Flash player.
4. **Open Skua Mac** from Applications. The first time, macOS will say it can't check the app because it isn't from the App Store. Click **Done**, open **System Settings → Privacy & Security**, scroll down, and click **Open Anyway** next to Skua Mac. You only do this once.
5. If Skua Mac offers to **install Rosetta**, click **Yes**. If macOS asks to let it **find devices on your local network**, click **Allow**. Both are one-time.
6. **Log in** in the game window under the Skua Mac bar, then use the menus on the bar (Scripts, Options, Tools…) to run the bot.

## Running more than one bot

Each Skua Mac window is its own bot with its own game window. To add another, open the **Skua Mac** menu at the top of the screen and choose **New Skua Window**, or press **⌘N**. New windows open slightly offset so they don't cover each other.

To control all your bots at once, use the **Army** menu in any Skua Mac window. It works on every Skua Mac window that's open:

- **Start scripts / Stop scripts on all windows**
- **Load a script on all windows...**: pick a script once and every window loads it
- **Log in / Log out all windows**: logins are spaced 2 seconds apart
- **Send all to map... / Send all to player...**
- **Lag Killer**, **Hide the game (headless)** and **Hide other players**: switch them on or off everywhere

The top line of the menu shows how many Skua Mac windows are running, and after each click it shows how many windows got it. The army hotkeys (Options > HotKeys) do the same things.

## If something goes wrong

- **The game window never appears.** Make sure the Artix Game Launcher is in your Applications folder and that you allowed Rosetta. Then quit and reopen Skua Mac.
- **"Skua Mac is damaged" or it won't open.** Do the Open Anyway step in Install step 4. If that option is missing, open the Terminal app and run `xattr -dr com.apple.quarantine "/Applications/Skua Mac.app"`, then try again.
- **A button shows "Something went wrong".** The bot keeps running. Details are saved in `crash.txt` inside the app; please report it on the Issues page.

> Botting is against AQW's rules. Use an alt account if you don't want to risk your main.

Want to build it yourself or see how it works? See [README-macOS.md](README-macOS.md).

## What was changed to make it work on Mac

**Running the game.** VibeSkua shows the game through Windows' ActiveX Flash control, which doesn't exist on macOS. Skua Mac runs the game in a small Electron window with PepperFlash, the same setup the official Artix Game Launcher uses on Mac (its Flash player is borrowed from your launcher install). A local bridge (`FlashUtil.Unix.cs` + `Skua.FlashHost`) connects the bot to the game and matches the Windows behaviour exactly, so scripts and the bot core don't need changes. Each game call takes about 0.4 ms.

**One window.** The Skua Mac bar shrinks to its title and menus, and the game window docks underneath it and moves, resizes and minimizes with it. Headless mode hides the game, hotkeys work from either window, Discord screenshots come from the game window, and a closed or crashed game window can be reopened. For multiple accounts, open the app again; each copy gets its own game profile, and the Army menu controls them all (each copy listens on a private local socket instead of the Windows window messages).

**Scripts.** Community scripts use Windows-only pieces, so two compatibility fixes were added. A small stand-in for Windows Forms (`Skua.Compat.WinForms`) lets `CoreBots.cs` compile. On macOS, CoreBots' "requires Windows 10" check is reworded before compiling so it no longer stops every script. Script files themselves are never edited.

**Crash fixes.** The file pickers (Load Script and others), Open VSCode, the themes folder button and the army hotkeys all called Windows-only code and crashed on Mac; each now has a Mac version. A frozen window caused by the bridge was fixed, and any button that still fails now shows an error instead of closing the app.

**Performance.** On the login screen the bot dropped from about 24% to 2% of a CPU core, and the game window from 19% to 7%. The fixes: a hidden loading bar that kept animating, Apple's Metal renderer instead of OpenGL, and fewer background status checks. CPU use is higher in game, but the same fixes apply.

**Packaging.** `./build-mac.sh` builds a self-contained, double-clickable `Skua Mac.app`. Every push is built on both Windows and macOS by GitHub Actions, so Mac changes can't break the Windows build. Windows behaviour is otherwise unchanged.



## Credits & Acknowledgements

* **[auqw/skua](https://github.com/auqw/skua)** — Original Skua framework and core botting API.
* **[ArrowDev](https://github.com/ArrowDev123/Skua) (BlackArrow on Discord)** — Early Avalonia UI port foundation.
* **[NinjaXz](https://github.com/NinjaXz/VibeSkua)** — VibeSkua, the client Skua Mac is ported from. Client overhaul: engineered the multi-client tabbed host, army controls & grid view, headless mode, autonomous script scheduler, repaired broken subsystems & crash loops, and modernized the desktop UI with Avalonia.
* **[NMTOKA](https://github.com/NMTOKA/Skua-Mac)** — Skua Mac: the macOS port (game window bridge, Mac fixes, packaging, Army menu).

---

### Copyright & Disclaimer

**Educational & Personal Use Only:** This project is a derivative of [auqw/skua](https://github.com/auqw/skua) and is provided "as-is" under the MIT License. I do not claim ownership of the original assets, game data, or the intellectual property of the game developers.
 
**Disclaimer:** Use of this software may violate the Terms of Service of the associated game. The author assumes no responsibility for any account actions, bans, or other consequences taken by game developers against users of this software. By using this tool, you acknowledge that you do so entirely at your own risk. If your PC decides to commit a toaster bath, that is not my problem.
