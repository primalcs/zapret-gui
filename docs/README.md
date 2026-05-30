# Zapret — Documentation

High-level documentation for the [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) Windows distribution as installed at `c:\zapret`. This describes only the **top-level layout** (root files and immediate contents of `bin/`, `lists/`, `utils/`).

## What it is

Zapret is a Windows DPI (Deep Packet Inspection) bypass tool. It intercepts network traffic with **WinDivert**, then applies desynchronization tricks (`dpi-desync`) so ISP or firewall middleboxes fail to block or throttle selected connections — commonly used for Discord, YouTube, and other filtered services.

The distribution is controlled by batch scripts. The core engine is `bin\winws.exe`; everything else configures how that process is started, which domains/IPs to target, and whether it runs as a Windows service or a one-off process.

## How it works (flow)

```
┌─────────────────────────────────────────────────────────────────┐
│  User runs service.bat OR general*.bat                          │
└───────────────────────────┬─────────────────────────────────────┘
                            │
         ┌──────────────────┴──────────────────┐
         │                                     │
         ▼                                     ▼
┌─────────────────┐                 ┌─────────────────────┐
│  service.bat    │                 │  general*.bat       │
│  (admin menu)   │                 │  (strategy launch)  │
└────────┬────────┘                 └──────────┬──────────┘
         │                                     │
         │ Install/remove/status               │ Pre-flight calls:
         │ Toggle settings                     │  • status_zapret
         │ Update lists                        │  • check_updates
         │ Diagnostics                         │  • load_game_filter
         │                                     │  • load_user_lists
         │                                     │
         └──────────────────┬──────────────────┘
                            ▼
                 ┌─────────────────────┐
                 │  bin\winws.exe      │
                 │  + WinDivert driver │
                 │  + host/IP lists    │
                 │  + .bin payloads    │
                 └─────────────────────┘
```

### Two ways to run

| Mode | Entry point | Behavior |
|------|-------------|----------|
| **Standalone** | Double-click a `general*.bat` | Starts `winws.exe` minimized in the current session. Exits when the process is killed or the machine reboots (unless re-run). |
| **Service** | `service.bat` → Install Service | Parses a chosen `general*.bat`, registers a `zapret` Windows service that runs `winws.exe` with the same arguments at boot. |

Both modes use the same underlying `winws.exe` command line; only the lifecycle differs.

### Pre-flight steps (every strategy script)

Each `general*.bat` begins with the same bootstrap:

1. **`service.bat status_zapret`** — Ensures TCP timestamps are enabled; warns if the `zapret` service is already running (conflict with standalone mode).
2. **`service.bat check_updates`** — Optionally checks GitHub for a newer version (when `utils\check_updates.enabled` exists).
3. **`service.bat load_game_filter`** — Reads `utils\game_filter.enabled` and sets `%GameFilterTCP%` / `%GameFilterUDP%` port ranges.
4. **`service.bat load_user_lists`** — Creates default user list files under `lists\` if missing.

Then the script `cd`s into `bin\` and runs `winws.exe` with a long chain of `--filter-*` rules separated by `--new`.

---

## Top-level directory layout

```
c:\zapret\
├── service.bat              # Admin service manager & shared helpers
├── general.bat              # Default strategy
├── general (ALT).bat        # Alternative strategies (ALT … ALT11)
├── general (SIMPLE FAKE*).bat
├── general (FAKE TLS AUTO*).bat
├── bin\                     # Binaries, driver, packet templates
├── lists\                   # Domain and IP block lists
└── utils\                   # Feature flags and test tooling
```

---

## `bin\` — Binaries and payloads

| File | Role |
|------|------|
| `winws.exe` | Main DPI bypass process (Windows build of zapret/winws). |
| `WinDivert64.sys` | Kernel driver for packet capture/reinjection. |
| `WinDivert.dll` | User-mode WinDivert library. |
| `cygwin1.dll` | Runtime dependency for `winws.exe`. |
| `tls_clienthello_*.bin` | Pre-captured TLS ClientHello payloads injected during `fake` / `multisplit` desync. |
| `quic_initial_*.bin` | Pre-captured QUIC initial packets for UDP fake desync. |
| `stun.bin` | STUN packet template for L7 Discord/STUN filtering. |

`winws.exe` is always launched from this directory. Strategy scripts reference `%BIN%` for both the executable and the `.bin` template files.

---

## `lists\` — Traffic selection

| File | Purpose |
|------|---------|
| `list-general.txt` | Primary domain list — traffic to these hosts gets bypass rules. |
| `list-google.txt` | Google-specific domains (separate TCP/443 rule). |
| `list-exclude.txt` | Domains excluded from filtering. |
| `list-general-user.txt` | User additions (created by `load_user_lists` if absent). |
| `list-exclude-user.txt` | User exclusions. |
| `ipset-all.txt` | CIDR ranges for IP-based filtering (can be updated from GitHub). |
| `ipset-all.txt.backup` | Backup when IPSet filter is toggled off. |
| `ipset-exclude.txt` | IP ranges to skip. |
| `ipset-exclude-user.txt` | User IP exclusions. |

Lists are passed to `winws.exe` via `--hostlist`, `--hostlist-exclude`, and `--ipset` arguments. The **IPSet filter** toggle in `service.bat` swaps `ipset-all.txt` between a real list, an empty file (match any IP), or a placeholder `203.0.113.113/32` (match none).

---

## `utils\` — Flags and tooling

| File | Purpose |
|------|---------|
| `check_updates.enabled` | When present, strategy scripts silently check for updates on launch. |
| `game_filter.enabled` | Contains `all`, `tcp`, or `udp` — expands game port filtering to `1024-65535`. |
| `targets.txt` | Used by the PowerShell test script. |
| `test zapret.ps1` | Connectivity test suite (launched from service menu option 11). |

These are simple presence/content flag files — no subfolder structure.

---

## `service.bat` — Service manager

Version: **1.9.8c** (embedded as `LOCAL_VERSION`).

Requires **administrator** rights. On first launch without the `admin` argument it re-launches itself elevated via PowerShell.

### External subcommands

Other scripts call `service.bat` with arguments instead of opening the menu:

| Argument | Action |
|----------|--------|
| `status_zapret` | Enable TCP timestamps; soft-check if `zapret` service is already running. |
| `check_updates` | Fetch latest version from GitHub (respects `check_updates.enabled`). |
| `load_game_filter` | Set `%GameFilter%`, `%GameFilterTCP%`, `%GameFilterUDP%`. |
| `load_user_lists` | Ensure user list files exist under `lists\`. |

### Interactive menu

| # | Function |
|---|----------|
| 1 | **Install Service** — Pick a `general*.bat`, parse its `winws.exe` arguments, create/start `zapret` service, store strategy name in registry. |
| 2 | **Remove Services** — Stop/delete `zapret`, kill `winws.exe`, clean up WinDivert services. |
| 3 | **Check Status** — Report service state, WinDivert driver, and whether `winws.exe` is running. |
| 4 | **Game Filter** — Toggle/filter game ports via `utils\game_filter.enabled`. |
| 5 | **IPSet Filter** — Switch `ipset-all.txt` between loaded / none / any modes. |
| 6 | **Auto-Update Check** — Toggle `utils\check_updates.enabled`. |
| 7 | **Update IPSet List** — Download latest `ipset-all.txt` from GitHub. |
| 8 | **Update Hosts File** — Merge zapret hosts entries into `%SystemRoot%\System32\drivers\etc\hosts`. |
| 9 | **Check for Updates** — Compare `LOCAL_VERSION` with GitHub and open release page if newer. |
| 10 | **Run Diagnostics** — Check BFE, proxy, TCP timestamps, conflicting software (Adguard, Killer, Intel Connectivity, Check Point, etc.). |
| 11 | **Run Tests** — Launch `utils\test zapret.ps1`. |

When a service is installed, the chosen strategy filename is stored at:

`HKLM\System\CurrentControlSet\Services\zapret\zapret-discord-youtube`

The menu displays this as the current strategy.

---

## Strategy scripts (`general*.bat`)

All strategy scripts share the same skeleton and the same **WinDivert capture ports**:

- **TCP:** `80, 443, 2053, 2083, 2087, 2096, 8443` + optional game range
- **UDP:** `443, 19294-19344, 50000-50100` + optional game range

They differ in **how** traffic is desynchronized — the `--dpi-desync=*` options on each `--filter-*` rule.

### Rule categories (common across strategies)

Each script defines a stack of rules (each ending with `--new`):

1. **General UDP/443** — Hostlist-based QUIC fake desync for listed domains.
2. **Discord/STUN UDP** — L7 filter for Discord voice/STUN ports with protocol-specific fakes.
3. **Discord media TCP** — Ports 2053, 2083, 2087, 2096, 8443 for `discord.media`.
4. **Google TCP/443** — Uses `list-google.txt`, often with `--ip-id=zero`.
5. **General TCP 80/443** — Hostlist-based rules for main domain list.
6. **IPSet UDP/443 and TCP 80/443/8443** — IP-range-based catch-all rules.
7. **Game filter TCP/UDP** — Uses `%GameFilterTCP%` / `%GameFilterUDP%` when game filter is enabled.

### Strategy families

| Script | TCP desync approach | Notes |
|--------|---------------------|-------|
| `general.bat` | `multisplit` with seq overlap patterns | Default/recommended baseline. Uses `tls_clienthello_4pda_to.bin` for general traffic. |
| `general (ALT).bat` | `fake,fakedsplit` + `fooling=ts` | Timestamp fooling variant. |
| `general (ALT2).bat` | `fake,fakedsplit` (variant parameters) | Another fake/fakedsplit tuning. |
| `general (ALT3).bat` | `fake,fakedsplit` (variant parameters) | Further ALT tuning. |
| `general (ALT4).bat` | `fake,fakedsplit` (variant parameters) | Further ALT tuning. |
| `general (ALT5).bat` | `syndata,multidisorder` | **Not recommended** — broad L3 IPv4 catch, minimal host filtering. |
| `general (ALT6).bat` | `multisplit` (681 seq overlap everywhere) | Like general but uniform split pattern. |
| `general (ALT7).bat` | `multisplit` at `sniext+1` + `syndata` for ipset | Mixed split/syndata approach. |
| `general (ALT8).bat` | `fake` + `badseq` fooling | Bad sequence number increment trick. |
| `general (ALT9).bat` | `fake,fakedsplit` (variant) | Another fake family variant. |
| `general (ALT10).bat` | `fake,fakedsplit` (variant) | Another fake family variant. |
| `general (ALT11).bat` | `fake,fakedsplit` (variant) | Another fake family variant. |
| `general (SIMPLE FAKE).bat` | `fake` + `fooling=ts` | Simpler fake TLS injection without split. |
| `general (SIMPLE FAKE ALT).bat` | `fake` (variant) | SIMPLE FAKE alternative tuning. |
| `general (SIMPLE FAKE ALT2).bat` | `fake` (variant) | SIMPLE FAKE alternative tuning. |
| `general (FAKE TLS AUTO).bat` | `fake,multidisorder` + auto TLS (`fake-tls-mod=rnd,dupsid,sni=...`) | Generates randomized fake TLS rather than fixed `.bin` files for many rules. |
| `general (FAKE TLS AUTO ALT).bat` | Auto TLS (variant) | FAKE TLS AUTO alternative. |
| `general (FAKE TLS AUTO ALT2).bat` | Auto TLS (variant) | FAKE TLS AUTO alternative. |
| `general (FAKE TLS AUTO ALT3).bat` | Auto TLS (variant) | FAKE TLS AUTO alternative. |

If one strategy stops working (ISP changes filtering), try another family — e.g. switch from `multisplit` to `fake,fakedsplit` or `FAKE TLS AUTO`.

### Key `winws.exe` concepts

| Option | Meaning |
|--------|---------|
| `--wf-tcp` / `--wf-udp` | WinDivert capture ports (wire filter). |
| `--filter-tcp` / `--filter-udp` | Apply the following options to matching traffic. |
| `--hostlist` | Match domains from a text file. |
| `--ipset` | Match destination IPs from CIDR list. |
| `--dpi-desync` | Desync method: `fake`, `multisplit`, `fakedsplit`, `multidisorder`, `syndata`, etc. |
| `--dpi-desync-fake-tls` | Inject a captured TLS ClientHello (.bin or auto-generated). |
| `--dpi-desync-fake-quic` | Inject a captured QUIC initial packet. |
| `--dpi-desync-fooling` | Packet-level tricks: `ts` (timestamps), `badseq`, etc. |
| `--new` | Start a new filter rule block. |

---

## Typical usage

### Quick test (no service)

1. Run `service.bat` → Remove Services (if previously installed).
2. Double-click `general.bat` (or an ALT variant if the default fails).
3. Check Discord/YouTube connectivity.

### Persistent (service)

1. Run `service.bat` as admin.
2. Remove any existing service (option 2).
3. Install Service (option 1) and pick a strategy.
4. Optionally enable Game Filter (option 4) for online games.
5. Use Check Status (option 3) to verify.

### Updates

- **This GUI** (`zapret-gui`) downloads release ZIPs from GitHub and extracts to the configured zapret path.
- **Built-in checker** in `service.bat` compares version `1.9.8c` against GitHub and opens the release page.

---

## Validation (used by zapret-gui)

The GUI treats a folder as a valid zapret installation when both files exist:

- `service.bat`
- `general.bat`

See `ZapretBatRunner.IsZapretFolder()`.

---

## GUI invocation (zapret-gui)

All GUI actions run `service.bat` from the configured zapret folder via `ZapretBatRunner` (`cmd.exe /c`, working directory = zapret root). Implementation: `ZapretServiceCommands.cs`.

Menu-driven actions run **`service.bat admin`** in-process (GUI requires administrator). Stdin menu choices are sent when the matching prompt appears in stdout; the process is stopped once action-specific output is detected (before the menu loops).

### Non-interactive CLI (no admin menu)

Called as `service.bat <argument>` **without** `admin`. Used by `general*.bat` pre-flight and available to the GUI if needed.

| Argument | Behavior |
|----------|----------|
| `status_zapret` | Soft-check if `zapret` service is running; enable TCP timestamps. |
| `check_updates` | If `utils\check_updates.enabled` exists, spawn/check GitHub version (may re-invoke with `soft`). |
| `check_updates soft` | Run update check inline (no background spawn). |
| `load_game_filter` | Set `%GameFilter%` / `%GameFilterTCP%` / `%GameFilterUDP%` from `utils\game_filter.enabled`. |
| `load_user_lists` | Create default user list files under `lists\` if missing. |
| `admin` | Skip UAC re-launch; enter interactive menu (requires elevation). |

### Interactive menu (stdin via `service.bat admin`)

| Menu | Label | CLI shortcut | GUI stdin sequence | Notes |
|------|-------|--------------|-------------------|--------|
| 1 | Install Service | — | `1`, `{index}` | `{index}` = 1-based number from numbered `*.bat` list (excludes `service*`). |
| 2 | Remove Services | — | `2` | Stops/deletes `zapret`, kills `winws.exe`, cleans WinDivert. |
| 3 | Check Status | — | `3` | Strategy registry value, `zapret` / WinDivert service, `winws.exe`. |
| 7 | Update IPSet List | — | `7` | Downloads `lists\ipset-all.txt` from GitHub. |
| 8 | Update Hosts File | — | `8` | Downloads hosts snippet; may open Notepad for **manual** merge into `%SystemRoot%\System32\drivers\etc\hosts`. |
| 10 | Run Diagnostics | — | `10`, `N`, `Y`, `` | **N** = no conflicting-software removal; **Y** = clear Discord cache; last Enter = pause. |
| 11 | Run Tests | — | `11` | Starts `utils\test zapret.ps1` in a **separate** PowerShell window; little stdout in GUI. |

### GUI button → bat call

| UI control | `ZapretServiceCommands` | Invocation |
|------------|-------------------------|------------|
| Install service | `InstallServiceAsync(runner, strategyBatFileName)` | Menu **1** + install index for selected `*.bat` (excludes `service*`) |
| Remove service | `RemoveServicesAsync(runner)` | Menu **2** |
| Check status | `CheckStatusAsync(runner)` | Menu **3** |
| Update IPSet list | `UpdateIpSetListAsync(runner)` | Menu **7** |
| Update hosts file | `UpdateHostsFileAsync(runner)` | Menu **8** |
| Run diagnostics | `RunDiagnosticsAsync(runner)` | Menu **10** |
| Run connectivity tests | `RunTestsAsync(runner)` | Menu **11** (requires `utils\test zapret.ps1`) |

Service status is shown via registry (installed strategy) and the **Check status** button (menu option 3). No direct `sc.exe` or process checks in the GUI.

---

## Related links

- [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) — Source distribution and releases
- [ValdikSS/GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) — Related DPI bypass project (zapret is a fork/evolution)
