# zapret-gui

WPF GUI for [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) on Windows (requires administrator).

## Application updates (Updatum + GitHub Releases)

The GUI checks **[primalcs/zapret-gui](https://github.com/primalcs/zapret-gui)** releases via [Updatum](https://github.com/sn4k3/Updatum).

- On startup (can be disabled in `settings.json`: `CheckAppUpdatesOnStartup`)
- Manually: **Check app update** under the title
- Release asset name: `zapret-gui_win-x64_v{version}.exe` (Inno Setup installer)

Zapret bundle updates (zip from Flowseal) are separate — use **Check for update** / **Update** in Advanced.

## Build installer locally

1. Install [.NET SDK](https://dotnet.microsoft.com/download) and [Inno Setup 6](https://jrsoftware.org/isinfo.php).
2. Run (from repo root):

```cmd
scripts\build-release.cmd
```

If PowerShell blocks unsigned scripts, use the `.cmd` wrapper above, or:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

Optional: `scripts\build-release.cmd -Version 0.1.1 -SelfContained`

Output: `artifacts\installer\zapret-gui_win-x64_v{version}.exe`

## Publish a release

1. Bump `<Version>` in `zapret-gui.csproj`.
2. Commit and push tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

3. GitHub Actions (`.github/workflows/release.yml`) builds the installer and attaches it to the release.

Users install the `.exe`; updates run the new installer silently (`/VERYSILENT`) and restart the app.
