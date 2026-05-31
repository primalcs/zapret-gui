# zapret-gui

[Русский](README_RU.md)

WPF GUI for managing [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) on Windows.

The app automates service installation, list updates, and diagnostics — without manually running `service.bat` and picking menu options in the console.

## Features

- **Do everything** — one button: check for zapret updates, try strategies, and install the first working service
- **Turn Off** — stop and remove the zapret service
- **Service** — manual install/remove, status check, strategy selection (`general*.bat`)
- **Lists** — update `ipset-all.txt` and the system `hosts` file
- **Diagnostics** — environment checks and connectivity tests (PowerShell)
- **Zapret updates** — download the latest ZIP from [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube)
- **App updates** — via [Updatum](https://github.com/sn4k3/Updatum) and [GitHub Releases](https://github.com/primalcs/zapret-gui/releases)
- **UI** — English and Russian

## Requirements

- Windows 10/11 (x64)
- **Administrator** rights (required at startup)
- [.NET 11 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/11.0) — unless you use a self-contained build
- A [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) folder (configure the path in settings, e.g. `C:\zapret`)

## Installation

1. Download the latest installer from [Releases](https://github.com/primalcs/zapret-gui/releases):  
   `zapret-gui_win-x64_v{version}.exe`
2. Run the installer as administrator.
3. If your zapret folder is not the default, set the path under **Advanced**.

## Quick start

1. Download and extract [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) (e.g. to `C:\zapret`).
2. Run **zapret-gui** as administrator.
3. Under **Advanced**, set the path to your zapret folder.
4. Click **Do everything** — the app updates zapret if needed and picks a working strategy.
5. On success, a green banner appears at the top; use **Turn Off** to disable.

If auto-selection fails, open the **Service** tab and try another strategy manually.

## Settings

`settings.json` is created next to the application executable:

```json
{
  "ZapretPath": "C:\\zapret",
  "InstalledVersion": "0.0.0",
  "Language": "en",
  "CheckAppUpdatesOnStartup": true,
  "SkippedAppUpdateVersion": ""
}
```

| Field | Description |
|-------|-------------|
| `ZapretPath` | Path to the zapret folder (`service.bat`, `general.bat`) |
| `Language` | `en` or `ru` |
| `CheckAppUpdatesOnStartup` | Check for zapret-gui updates on startup |
| `SkippedAppUpdateVersion` | App version the user chose to skip |

## Two kinds of updates

| What | Source | In the GUI |
|------|--------|------------|
| **zapret-gui** (this app) | [primalcs/zapret-gui](https://github.com/primalcs/zapret-gui) | **Check app update** under the title, or on startup |
| **zapret-discord-youtube** (bypass engine) | [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) | **Check for update** / **Update** under **Advanced** |

App updates download the installer and run it silently (`/VERYSILENT`), then restart the GUI.

## Building from source

### Run for development

```powershell
dotnet run
```

### Build the installer locally

1. Install [.NET SDK 11](https://dotnet.microsoft.com/download) and [Inno Setup 6](https://jrsoftware.org/isinfo.php).
2. From the repo root:

```cmd
scripts\build-release.cmd
```

If PowerShell blocks unsigned scripts:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

Optional: `scripts\build-release.cmd -Version 0.1.1 -SelfContained`

Output: `artifacts\installer\zapret-gui_win-x64_v{version}.exe`

### Publish a release

1. Bump `<Version>` in `zapret-gui.csproj`.
2. Create and push a tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

3. GitHub Actions ([`.github/workflows/release.yml`](.github/workflows/release.yml)) builds the installer and attaches it to the release.

## Zapret documentation

For folder layout, strategies, and `service.bat` invocation details, see [`docs/README.md`](docs/README.md).

## Related projects

- [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) — zapret Windows distribution
- [ValdikSS/GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) — related DPI bypass project
- [sn4k3/Updatum](https://github.com/sn4k3/Updatum) — auto-update library
