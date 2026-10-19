# Installation

## Requirements

- Pathfinder: Wrath of the Righteous on **Windows** (Steam).
- A **screen reader** (NVDA, JAWS, …) or Windows SAPI voices.
- Optionally **Git** — only if you want the pull-and-deploy install path below; the installer
  doesn't need it.
- That's it — this uses the game's **native mod system**, so there is **no Unity Mod Manager** to
  install.

There are two ways to install — use whichever suits you. **Close the game first** either way, and
the setup wizard runs automatically on the first launch. Your Wrath Access settings are stored
separately, so updating never resets them.

## Without Git (installer)

1. Download the installer:
   **[WrathAccessInstaller.exe](https://github.com/bradjrenshaw/wotr-access/releases/latest/download/WrathAccessInstaller.exe)**
   and run it from anywhere — there is nothing to install. Windows SmartScreen may warn about an
   unrecognized app the first time; choose **More info**, then **Run anyway**.
2. Check the game directory is right (it auto-detects your Steam install; use **Browse** if not),
   then press **Install** and pick a version. The newest release is selected; the
   **dev channel (latest from the main branch)** at the end of the list installs the very latest
   build instead.
3. Press **Launch game**, or start the game as usual.

To **update**, run the installer again: it says when an update is available, and the button reads
**Update**. The installer keeps itself up to date too. **Uninstall** removes everything it
installed. The installer is [Loadstone](https://github.com/bradjrenshaw/loadstone), an accessible
mod manager, set up for Wrath Access alone.

## With Git

1. Clone the repo:
   ```
   git clone https://github.com/bradjrenshaw/wotr-access
   ```
2. In PowerShell, from the repo folder, run `.\deploy.ps1`.
3. Start the game. To **update** later: `git pull`, then `.\deploy.ps1` again.

`deploy.ps1` finds your install automatically (Steam libraries); if it can't, pass the folder
explicitly:

```
.\deploy.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure"
```

## First launch

Launch the game normally. If the game has been running for a while and nothing is happening, press
Enter on the keyboard and it should load you into the main menu. The first time you launch with the
mod installed, a **setup wizard** walks you through speech rate, movement, and your preferred
exploration features (sonar, wall tones, and so on). You can re-run it any time from the mod menu
(Ctrl+M).
