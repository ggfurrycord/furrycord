<div align="center">

# Furrycord

**A custom Discord client built for people who actually care about how Discord runs.**

[![License](https://img.shields.io/github/license/furrycord/furrycord?color=a855f7)](./LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-3b82f6.svg?logo=windows&logoColor=white)](https://github.com/furrycord/furrycord)

</div>

---

Furrycord is a fork of [Nightcord](https://github.com/20ch/nightcord), which itself builds on [Equicord](https://github.com/Equicord/Equicord) and [Vencord](https://github.com/Vendicated/Vencord). Plugin support, custom styling, better audio, and a lighter client — rebranded as **Furrycord**.

---

## What's in it

* **Faster startup** — loads quicker and sits lighter on CPU and RAM
* **Plugin support** — compatible with the Vencord/Equicord plugin ecosystem
* **Better audio** — hardware-optimized voice modules
* **Custom styling** — smoother UI and quality-of-life tweaks

---

## Quick start (Windows — already built)

If you've already built the project, run:

```
release\win-unpacked\Furrycord.exe
```

Your settings and plugins are stored in `%APPDATA%\Furrycord`.

---

## Build from source (Windows)

**Requirements:**

* [Git](https://git-scm.com/download)
* [Node.js 18+](https://nodejs.org/)
* [pnpm](https://pnpm.io/installation) — `npm install -g pnpm`

```powershell
git clone https://github.com/furrycord/furrycord.git
cd furrycord
pnpm install -r
pnpm run package:dir
```

The built app will be at `release\win-unpacked\Furrycord.exe`.

### Development mode

```powershell
pnpm install -r
pnpm start
```

---

## Inject into existing Discord (recommended)

The Furrycord installer is a wizard-style app inspired by [BetterDiscord's installer](https://github.com/BetterDiscord/Installer):

1. **Welcome** — agree to terms
2. **Choose action** — Install, Repair, or Uninstall
3. **Select Discord** — Stable, PTB, Canary, or Dev (latest version per channel)

**Build the installer** (requires [.NET 8 SDK](https://dotnet.microsoft.com/download)):

```powershell
pnpm run buildStandalone
.\build-installer.ps1
```

Run `release\installer\Furrycord-Installer.exe`

**What install does:**
- Injects Furrycord into your Discord client
- Copies the **Furrycord icon** to `%LOCALAPPDATA%\Furrycord\furrycord.ico`
- Creates a desktop shortcut **Furrycord (Stable/PTB/Canary)** with the custom icon
- **Auto-re-applies** when Discord updates (Stable, PTB, Canary, Dev) — no manual repair needed

**Repair** — use if injection breaks after an update; cleans rival mods and re-injects.

**Alternative (legacy):**

```powershell
.\furrycord-install.ps1
```

---

## macOS

```bash
git clone https://github.com/furrycord/furrycord.git
cd furrycord
pnpm install -r
pnpm add -D react react-dom
pnpm approve-builds
pnpm run package:dir
```

Built app: `release/arm64/`
