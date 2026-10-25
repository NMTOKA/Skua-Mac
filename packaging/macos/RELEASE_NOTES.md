## Skua Mac

A native macOS version of the AQW bot client [VibeSkua](https://github.com/NinjaXz/VibeSkua) by NinjaXz
(built on [Skua](https://github.com/auqw/skua)), for Apple Silicon Macs.

### New in this version

Fixes for the new Ruffle game engine (the one Skua Mac uses on macOS 28, or when you choose it):

- **Logging in works.** The login request reached AQW empty, so the game showed "What? Try again."
- **The bot sees your inventory and class again**, and with them the bank, drops, quests and shops. Ruffle writes
  numbers slightly differently from Flash, and the bot couldn't read item IDs.
- Ruffle problems are now written to `~/Library/Logs/Skua Mac/game-window.log` for bug reports.

Nothing changes if you use the Flash engine (the default up to macOS 27).

To try Ruffle on macOS 27, open Terminal and run `echo ruffle > ~/Library/Application\ Support/VibeSkua/game-engine`,
then reopen Skua Mac. `echo flash > ~/Library/Application\ Support/VibeSkua/game-engine` switches back.

**Install:** download the `.dmg` below, open it, and drag **Skua Mac** into Applications.
You also need the free [Artix Game Launcher](https://www.artix.com/downloads/artixlauncher/) installed.

**First launch:** macOS will block it once because it isn't from the App Store. Open
System Settings > Privacy & Security, scroll down, and click **Open Anyway** next to Skua Mac.
If Skua Mac offers to install Rosetta, click Yes.

**More bots:** Skua Mac menu > **New Skua Window** (⌘N). Each window is its own bot; use the **Army** menu to
control them all.

Botting is against AQW's rules. Use an alt account if you don't want to risk your main.
