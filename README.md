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

**Known limit.** The Flash player is Intel-only and runs through Rosetta, and macOS has warned it won't open on macOS 28. A native replacement based on [Ruffle](https://ruffle.rs) has reached the AQW login screen in testing; gameplay is not yet tested.

> Botting is against AQW's rules. Use an alt account if you don't want to risk your main.

---

# VibeSkua
> **Note:** This project is **Vibe Coded**—built through AI-assisted development, and pure momentum.

A feature-rich, high-performance rehaul of [auqw/skua](https://github.com/auqw/skua) (v1.4.3.0), building upon the early Avalonia port by [ArrowDev](https://github.com/ArrowDev123/Skua) (BlackArrow on Discord). Upgraded with native multi-client army orchestration, headless performance, and core stability fixes.

---

## The Avalonia UI Rehaul (What's New in 2.0)
VibeSkua has officially transitioned away from legacy WPF to a ground-up modern desktop architecture:

* 🎨 **Modern Fluent Avalonia Shell:** Completely redesigned from scratch using **Avalonia 11**. Features a sleek, modern dark glassmorphism theme, crisp typography, responsive layout scaling, and smooth animations.
* ⚡ **Unified Multi-Client Tabbed Host:** Native multi-client workspace (`TabbedHostWindow`) with dynamic scrollable tabs, instant 1-click tab creation, and seamless instance management without overlapping taskbar clutter.
* 🌐 **Global Army Controls:** Integrated top-bar Army Control hub (`Start All`, `Stop All`, `Load Script to All`, `Army Scheduler`, `Jump All to Map/Player`, and synchronized `Lag Killer / Headless / Hide Players` toggles).
* ⊞ **Adaptive Multi-Instance Grid View:** View and monitor entire armies simultaneously in a clean, auto-fitting single-window grid layout.

---

## Skua Architecture Comparison
The following overview compares the systems and core features between the legacy WPF `auqw/skua` client and VibeSkua.

### Quality of Life & Features
| Feature | Original (Legacy WPF) | VibeSkua (Avalonia) |
| :--- | :--- | :--- |
| **UI Framework & Design** | Legacy WPF framework. | **Rebuilt in Avalonia UI.** Features sleek Fluent dark styling, smooth animations, and hardware acceleration. |
| **Account Tabs & Multi-Client** | Required running individual separate windows which clutter the screen and taskbar. | Native `TabbedHostWindow` host with dynamic SWF patching and horizontal tab bar. |
| **Army Control & Navigation** | Required managing each client independently. | Features a Script Scheduler to broadcast schedules, "Load Script to All", strict login validation, separate Map and Cell input boxes for exact room placement (`teleport across rooms inside your current map without reloading`), exact spawn/pad targeting (`e.g. Boss, Left`), a dedicated `"Jump All to Player..."` option (`instant 1-click /goto commands across all tabs`), and a robust IPC system across tabs. |
| **Smart Grid View** | Required managing dozens of overlapping individual windows. | Consolidates all active accounts into a clean, clutter-free grid inside a single window to monitor a full army at once. |
| **Headless Mode** | Full-screen rendering; high resource demand per instance. | Introduced a 1x1 hidden pixel viewport, forcing Flash to bypass geometry/blitting and significantly reducing resource consumption. |
| **Discord Integration** | Lacked native capability. | `DiscordWebhookService` integrated natively. Features rich visual embed cards (`Script Started`, `Farming Session Concluded`, `Scheduler Paused`), automatic rate-limiting (`HTTP 429`) retry loops, a threaded queue structure to prevent dropped packets, and `CachedUsername` preservation so webhooks and script status alerts always display your character's real username after disconnections. |
| **Script Scheduling** | Required manual initialization and supervision with static script options. | Added autonomous script queuing with independent option profiles, custom display names, save/load playlist states, `SilentConfig` unattended execution (`popup modal windows automatically suppressed so overnight farming never stalls`), smart auto-relogin collision protection (`resumption recognition prevents skipped items when reconnecting during playlists`), plain-English error unwrapping, and automatic paused state monitoring (`Scheduler Paused`). |
| **Script Repository & Sorting** | Basic navigation; crash-prone when downloading all scripts; cache sizing hides scripts. | Upgraded `ScriptRepoViewModel` with search-as-you-type filtering, category filters, and ascending/descending sorting. Fixed semaphore lifecycle crashes during "Download All" and repaired cache sizing so community scripts are never hidden. |
| **Pause Functionality** | Could only fully Stop scripts, entirely losing current progression. | Built a native `Pause` feature that safely freezes the execution thread in place, letting you interact with menus and resume later. |
| **Streamer Mode** | Basic privacy capabilities. | Actively scrubs character names, guild tags, room numbers, and disables chat via background asynchronous Flash injection. |
| **Auto-Relogin Resilience** | Basic relogin handling prone to freezing during network timeouts. | Redesigned with asynchronous task scheduling, dynamic alternative server selection, and fallback socket injection. Upgraded with staggered multi-account batch launches (`500ms–900ms stagger paired with retry jitter to prevent server rate-limiting`), an active `"Stuck on Login"` monitor (`automatic UI reset and rescue if authentication hangs above 5 seconds`), and synchronized character data buffering (`waiting for server list readiness before joining to completely eliminate "Character Data Could Not Be Loaded" errors`). |
| **Reorganized Navigation & UI** | Default top menu navigation. | Reorganized left-to-right based on workflow priority (`Scripts`, `Options`, `Tools & Helpers`, `Combat`, `Bank`, `Diagnostics`, `Plugins`). Features instant batch-loading for the Daily Tracker window, categorized daily vs. weekly ultra boss lockouts, and a unified developer diagnostics workspace (`Logs, Console, Spammer, Logger, Interceptor`). |
| **Loadouts Manager** | Non-existent or fully manual. | Fully automates item equipping and dynamic (Forge) enhancements natively via C#. Features a smart banking algorithm, missing gear alerts, and state restoration. |
| **Custom Scripts Loader** | Only supported official repository scripts. | Natively load individual local scripts or custom script directories directly into the UI, complete with safe file deletion prompts and ghost entry cleanup. |
| **Custom Hotkeys** | Relied on static, hardcoded keyboard shortcuts. | Replaced static keybinds with a dynamic cross-window `IHotKeyService` supporting global army toggles, multi-client broadcast triggers, and user-customizable bindings. |

---

### Performance & Engine Optimizations

* **Combat Cooldown Deadlock & Race Elimination:** Reworked Global Cooldown (`GCD`) index checks in `AdvancedSkillCommand.cs` by verifying skill readiness (`isOK` and Flash `canUseSkill`) before advancing rotation indices, completely eliminating endless Auto-Attack loops (`Only using autoattack`). Enforced a 3.5-second bounded safety ceiling in `ScriptSkill.cs` (`Wait.ForTrue`) so background threads break cleanly and trigger `OnTargetReset()` during monster death or UI lockups without deadlocking or freezing your character.
* **ActionScript 3 (SWF) Garbage Collection & Animation Protection:** Throttled Flash display tree inspection modules (`DisableFX` and `HidePlayers`) down to synchronized 2 FPS checks, cutting string allocations and Flash garbage collection overhead by over 93% to prevent overnight out-of-memory crashes. Disabled destructive animation clipping (`OptimizePlayers`) so room and character poses keep playing smoothly during map transitions, and added strict null safety checks in `RemoteRegistry.as` (`destroy()` and `ext_destroy()`) and `Player.as` (`isKicked()`) to eliminate Flash `Error #1009` crashes.
* **C# / .NET Core Concurrency & Memory Upgrades:** Added automatic Large Object Heap cache clearing for dynamic XML game queries (`getGameObject`), fast multi-file `#include` dependency preprocessing, accurate `ScriptWait` loop math (`dividing timeout arguments by 100 instead of 1000`), and thread-safe COM dispatching across UI boundaries while keeping background script threads thread-safe.
* **SWF Memory Caching:** Implemented `PreloadSwf()` in `FlashUtil.cs` to cache files directly in RAM, accelerating instance launches. Enforced `WMode="direct"` for hardware-accelerated rendering.
* **Optimized Memory Management:** Mitigated memory leaks in the Avalonia client by properly managing subscriber lifecycles on `StrongReferenceMessenger` and using WeakReference patterns, preventing slowdowns over multi-day sessions.
* **Fluid Asynchronous Operations:** Replaced blocking `Thread.Sleep` calls with non-blocking `await Task.Delay` across automated hunting loops, freeing up thread-pool resources and reducing UI micro-stutters.
* **Clean Task Lifecycles & UI Decoupling:** Overhauled background processes (like `DailyTracker` and `SingleInstanceWatcher`) to respect cancellation tokens and fully decouple heavy game-state checks from the main rendering thread.
* **Network Proxy Optimization:** Refactored `CaptureProxy.cs` to utilize `Encoding.UTF8.GetBytes()` for packet conversion, minimizing latency during high-traffic sessions.
* **GitHub Script Caching Engine:** Engineered `ScriptDates.json` to store metadata and track SHA hashes. Intelligent API querying conserves rate limits and provides graceful UI fallbacks on connection failure.
* **Background Connection Stability:** Repositions inactive clients off-screen and uses an Avalonia `DispatcherTimer` to ping the `isLoggedIn` COM interface every 500ms, preventing OS-level socket throttling.
* **Active Memory Management:** Introduced `MemoryUtils.cs` to periodically trim the application’s working set, ensuring RAM stability during long, multi-day farming sessions.
* **Asynchronous Flash Injection:** Built a background loop to actively override ActionScript 3 variables (e.g., `world.strMapName`) every 500ms to maintain privacy in Streamer Mode.
* **Velopack Deployment Architecture:** Fully migrated deployment infrastructure to Velopack. Enables 1-click silent installs, automatic desktop shortcuts, zero-install portable zip generation, and built-in background auto-updating via GitHub Releases API.
* **And Many More:** Dozens of underlying architectural, thread-safety, and runtime stability enhancements across the entire framework.

---

## Installation & Downloads

Pre-built binaries are available on the [**Releases**](https://github.com/NinjaXz/VibeSkua/releases) page:

* **`VibeSkua-win-Setup.exe` (Recommended):** 1-click Windows installer. Installs cleanly to `%LocalAppData%\VibeSkua`, provisions Desktop and Start Menu shortcuts, and enables seamless background auto-updating.
* **`VibeSkua-win-Portable.zip`:** Zero-install portable package. Extract anywhere and launch `VibeSkua.Manager.exe` or `VibeSkua.exe`.

---

## Building the Project

### Automated Release Packaging (1-Click)
Run the release packager in the root folder to compile the solution and produce full Velopack installer artifacts:

```cmd
Publish-Release.bat
```
Output binaries and installers are generated into the `Build\Releases\` folder.

### Fast Local Dev Build
To build and stage binaries into `Build\` for immediate local testing:

```cmd
BuildProject.bat
```

### Manual (CLI)
```bash
dotnet build Skua.sln -c Release -p:WarningLevel=0 --nologo
```

---

## Credits & Acknowledgements

* **[auqw/skua](https://github.com/auqw/skua)** — Original Skua framework and core botting API.
* **[ArrowDev](https://github.com/ArrowDev123/Skua) (BlackArrow on Discord)** — Early Avalonia UI port foundation.
* **NinjaXz** — Client overhaul: engineered the multi-client tabbed host, army controls & grid view, headless mode, autonomous script scheduler, repaired broken subsystems & crash loops, and modernized the desktop UI with Avalonia.

---

### Copyright & Disclaimer

**Educational & Personal Use Only:** This project is a derivative of [auqw/skua](https://github.com/auqw/skua) and is provided "as-is" under the MIT License. I do not claim ownership of the original assets, game data, or the intellectual property of the game developers.
 
**Disclaimer:** Use of this software may violate the Terms of Service of the associated game. The author assumes no responsibility for any account actions, bans, or other consequences taken by game developers against users of this software. By using this tool, you acknowledge that you do so entirely at your own risk. If your PC decides to commit a toaster bath, that is not my problem.
