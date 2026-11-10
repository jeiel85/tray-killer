<div align="center">

<img src="docs/assets/banner.png" alt="Tray Killer" width="100%" />

<br/>

# Tray Killer

**Unwanted tray apps? Gone — and they stay gone.**<br/>
A tiny Windows tray guardian that kills chosen apps the instant they start and switches off every way they auto-start.

[![Release](https://img.shields.io/github/v/release/jeiel85/tray-killer?style=flat-square&color=ff2b5e&label=release)](https://github.com/jeiel85/tray-killer/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/jeiel85/tray-killer/total?style=flat-square&color=ff7a2b)](https://github.com/jeiel85/tray-killer/releases)
[![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011-0b0d12?style=flat-square&logo=windows11&logoColor=white)](#-install)
[![License](https://img.shields.io/github/license/jeiel85/tray-killer?style=flat-square&color=34d399)](LICENSE)

[**⬇ Download**](https://github.com/jeiel85/tray-killer/releases/latest/download/TrayKiller.exe) &nbsp;·&nbsp;
[**🌐 Website**](https://jeiel85.github.io/tray-killer/) &nbsp;·&nbsp;
[**한국어**](README.md)

</div>

---

## Why?

You install one program and it quietly drops a remote-support agent into your tray.
You disable it in Task Manager — it's back tomorrow. You right-click the icon — nothing happens.

**Tray Killer** hunts those apps down for good:

- Kills them **within about a second** of starting, and clears away any icon they managed to show.
- **Switches off auto-start** in the registry, Startup folders, Task Scheduler and services. It does it again if the app re-registers itself.
- Wipes the **ghost icon** a force-killed app leaves behind.
- Starts **before every other tray app** at logon.

<div align="center"><img src="docs/assets/screens/dashboard.png" alt="Dashboard" width="88%" /></div>

## Features

| | Feature | Details |
|:-:|---|---|
| ⚡ | **Real-time watch** | Kernel-backed `Win32_ProcessStartTrace` events when elevated (1 s WMI polling otherwise). |
| 🧹 | **Periodic sweeps** | Every 5 s – 5 min: catches anything missed and re-created startup entries. |
| 🚫 | **Auto-start blocking** | `Run`/`RunOnce` (HKCU, HKLM, WOW6432Node, Policies), Startup folders, Task Scheduler, Windows services. |
| ♻️ | **One-click restore** | Every change is backed up. Restored items are never touched again. |
| 👻 | **Ghost icon cleanup** | Sends `NIM_DELETE` on the target's behalf right before and after killing it. |
| 🙈 | **Hide tray icons** | Pushes target icons into the Windows 11 overflow area. |
| 🏁 | **Starts first** | Elevated Task Scheduler logon task, so it isn't held back by Explorer's startup delay. |
| 🔄 | **Self-updating** | Checks GitHub Releases every 6 h, verifies SHA-256, swaps itself and restarts. |
| 🛡️ | **Guard rails** | Core Windows processes (`explorer.exe`, `svchost.exe`, `csrss.exe`, …) can't be targeted, not even by wildcard. |
| 🌏 | **Korean / English UI** | Follows Windows, switchable in Settings. |

## Screenshots

| Targets | Activity |
|---|---|
| ![Targets](docs/assets/screens/targets.png) | ![Activity](docs/assets/screens/activity.png) |
| **Settings** | **Add an app** |
| ![Settings](docs/assets/screens/settings.png) | ![Add](docs/assets/screens/add.png) |

> Screenshots show the real, running app pointed at harmless demo apps staged with [`tools/stage-demo.ps1`](tools/stage-demo.ps1). The UI shown is Korean; English is built in.

## Install

1. [**Download TrayKiller.exe**](https://github.com/jeiel85/tray-killer/releases/latest/download/TrayKiller.exe) (~400 KB).
2. Run it and approve **one UAC prompt**. It installs to `C:\Program Files\TrayKiller` and registers itself in the Start menu and Apps list.
3. Click **Add app** and pick what you want gone.

Requires Windows 10 1903+ or Windows 11. It runs on .NET Framework 4.8, which ships with Windows, so there's nothing else to install.
Uninstall from *Settings → Uninstall* or *Windows Settings → Apps*. Everything it blocked is restored.

## Security design

Tray Killer runs elevated, so it is built so that **no unprivileged program can use it as a stepping stone**:

- The exe the elevated logon task runs lives in `Program Files` (admin-only). v1.0.x installed into the user profile; v1.1.0 migrates automatically.
- Settings and restore data, which drive privileged actions, live in an admin-only folder: `C:\ProgramData\TrayKiller\<user SID>`. A pre-created or link-replaced folder is not trusted.
- Every restore is re-validated against the locations Tray Killer blocks (Run keys, Startup folders, tray settings …).
- Nothing is moved in or out of user-writable folders while elevated. Per-user Startup items are switched off with the `StartupApproved` flag, as Task Manager does, and clean-up never follows junctions or symlinks.
- Least privilege: the command channel is restricted to the current user, other users' sessions are left alone, `SeDebugPrivilege` is only held for a single kill when needed, and URLs and folders open unelevated.
- Updates: only this repo's release download URLs are accepted, and both SHA-256 and file version must match.

Please report vulnerabilities privately, as described in [SECURITY.md](SECURITY.md).

## Build

Visual Studio 2022 or Build Tools with the *.NET desktop development* workload.

```powershell
.\build.ps1 -Version 1.0.0          # → dist\TrayKiller.exe + .sha256
.\tools\stage-demo.ps1 -SeedHistory # try it safely against fake tray apps (-Cleanup to remove)
```

Pushing a `v*.*.*` tag builds and publishes a release through GitHub Actions.

## License

[MIT](LICENSE) © 2026 jeiel85
