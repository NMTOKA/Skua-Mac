## Skua Mac

A native macOS version of the AQW bot client [VibeSkua](https://github.com/NinjaXz/VibeSkua) by NinjaXz
(built on [Skua](https://github.com/auqw/skua)), for Apple Silicon Macs.

### New in this version

- **Ready for macOS 28.** Skua Mac now includes a second game engine, [Ruffle](https://ruffle.rs), that runs the game
  natively on Apple Silicon with no Rosetta. It switches on by itself on macOS 28, where the old Flash game window
  can't open. On earlier macOS you can try it now (beta): open Terminal and run
  `echo ruffle > ~/Library/Application\ Support/VibeSkua/game-engine`, then reopen Skua Mac.
- **Update notice.** Skua Mac now tells you when a new version is out, and **Skua Mac menu > Check for Updates...**
  checks any time. Updating keeps your settings and scripts.
- **Army menu shows every window**: each bot's character and whether it's logged in and running a script, so you
  can spot one that stopped.
- **Skua Mac menu** now has About Skua Mac and New Skua Window (⌘N), which was missing before.
- **Streamer Mode** also re-hides your name after you respawn or change gear.
- If the game window closes while loading, Skua Mac now reopens it by itself. Problems with the game window are
  logged to `~/Library/Logs/Skua Mac/game-window.log` to attach to bug reports.

**Install:** download the `.dmg` below, open it, and drag **Skua Mac** into Applications.
You also need the free [Artix Game Launcher](https://www.artix.com/downloads/artixlauncher/) installed.

**First launch:** macOS will block it once because it isn't from the App Store. Open
System Settings > Privacy & Security, scroll down, and click **Open Anyway** next to Skua Mac.
If Skua Mac offers to install Rosetta, click Yes.

**More bots:** Skua Mac menu > **New Skua Window** (⌘N). Each window is its own bot; use the **Army** menu to
control them all.

Botting is against AQW's rules. Use an alt account if you don't want to risk your main.
