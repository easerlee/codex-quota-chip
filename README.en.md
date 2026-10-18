<div align="center">

# Codex Quota Chip

A small Codex quota strip that lives on the Windows taskbar: one glance for what is left, right-click for every account.

**English** | [简体中文](README.md) ・ [Changelog](CHANGELOG.md)

</div>

A small tool for the Windows taskbar. It parks next to the notification area (or at the taskbar's far
end - see below), shows how much of the **5-hour window** and the **weekly window** is left for the
account that is logged in now and when each one refills, and opens a panel on right-click listing the
last values read for every account this machine has used.

It is an **independent implementation**: the numbers come from a JSON-RPC client of its own talking to
the local Codex command line, and the UI is drawn from scratch. It is not a fork of any similar tool.

## What it looks like

```
┌──────────────────────────────┐   ← dark plate, can be switched off in settings
│ 5H  ▬▬▬▬▬▬▬▬▬▬▬▬▬▬           │      colour follows what is left: green / amber / red
│ WK  ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬    │
│ 09:16 / 6d 18h            ●  │   ← 5H reset clock / weekly countdown + status light
└──────────────────────────────┘
```

- **The width is measured, not fixed**: the card is as wide as the longest line its bottom row can
  ever show (`23:59 / 13d 23h`, `已重置 / 已重置`, `--:-- / reset --` and friends), and every pixel
  the rows do not need goes to the bars. The height is the taskbar's, split into three equal rows.
- **Dark plate** (on by default): the panel's own colour `#F0141619`, 8px corners, 3px in from the
  sides and 1px from the top and bottom - 46px tall on a 48px taskbar. Its switch lives in the
  settings layer.
- **Colours**: with the plate off, the text and bars follow the taskbar's light/dark setting; with it
  on, the whole strip uses the dark set. Near-black text on a near-black plate is invisible, so the
  two have to change together.
- **Status light** (the dot at the bottom right): green = fine, amber = reading, or the data has gone
  stale, grey = nothing read yet, red = the read failed. "Stale" means no successful read for two
  refresh intervals (`Freshness`, the same rule the panel uses).
- **Left click** refreshes on the spot. **Right click** opens the panel. **Hover** shows
  `左键刷新.右键设置` (left click refreshes, right click opens settings).
- Hides itself while a fullscreen app is in front, and comes back afterwards.
- **A Windows notification when a quota window refills** (it lands in the Action Center; the 5-hour and
  the weekly window are judged separately). Switch it off in the settings layer if you would rather not.
- One instance only; never appears in the taskbar list or in Alt+Tab.

The panel has two levels: the card on the left, and - once 设置 › is clicked - a **second layer to its
right** (it never displaces the card; one click opens it, the next closes it):

```
● easerlee 【当前】        🎫     │  刷新间隔        [1][5][15]
  5H   20%   09:16       │  显示位置           [左][右]
  WK   72%   6d 18h      │  黑色底图             ●──
─────────────────────── │  开机自动启动         ●──
刷新当前账号数据            │  额度重置时通知       ●──
设置 ›                   │
─────────────────────── │
退出                     │
```

- **The dot in front of an account is the link, not the login**: green while the app is still hearing
  from that account, grey once its reading has stopped (a revoked token shows up here). Which account
  is logged in is what `【当前】` - right after the name - is for.
- Accounts are ordered "the logged-in one first, then whoever frees up soonest"; windows that already
  reset, or that have not started counting, count as usable now and sort ahead of the ones still
  running; a record with no reset time sorts last.
- `已重置` means the window is full but has not started counting, or its reset time has passed - the
  percentage and reset time the server last gave are not trustworthy then (the 5-hour window starts at
  **first use**), so no old numbers are shown. **Its bar is drawn full green**, because a window that
  just refilled is full.
- **The gold ticket next to an account name** is how many **manual quota resets** that account has left
  (the server's `availableCount`), right-aligned to the same edge as the account's other numbers; extra
  tickets stack leftwards, and hovering one shows the expiry of the nearest. Tickets the server has
  already redeemed, or that have expired, are not drawn.
- The rows in the settings layer are sliding controls: the track is the bar's own track colour, the
  selection is the bar's green. Changing one **does not close the panel** - the control itself is the
  feedback.
- **显示位置**: `right` (the default) sits left of the notification area; `left` goes to the taskbar's
  far end - which is empty when Windows 11 centres the taskbar, so `left` suits that case. With a
  left-aligned taskbar use `right`, or the strip covers the Start button.
- Clicking anywhere outside the panel - either layer - closes it: another app, the taskbar, the desktop.

## How the numbers are read

The app starts the command line that ships with the local Codex:

```
codex.exe app-server --listen stdio://
```

then calls `account/rateLimits/read` over JSON-RPC and tells the windows apart by `windowDurationMins`
(about 300 minutes → 5H, about 10080 → week) rather than assuming which of `primary`/`secondary` is
which. `rateLimitResetCredits`, a sibling of `rateLimits` in the same answer, is how many reset credits
the account has left (`availableCount`, and each credit's `status`/`expiresAt`); the panel's tickets come
from it. Each read starts a process and ends it (a read takes about 1.5 seconds).

- **Read-only**: it never writes to or changes any Codex file; `auth.json` is only read, to find out
  which account is logged in.
- **No credentials stored**: an account record holds an id, an email, percentages and times. No tokens.
- The app's own state lives in `%LOCALAPPDATA%\CodexQuotaChip\` (move it with the environment variable
  `CODEX_QUOTA_CHIP_HOME` or `--home`).

## Installing

A portable package, no administrator rights needed:

```powershell
# after unzipping, in that folder
pwsh -File .\install.ps1            # installs to %LOCALAPPDATA%\Programs\CodexQuotaChip
pwsh -File .\install.ps1 -Autostart # and writes the autostart entry
```

`CodexQuotaChip.exe` in the zip can also just be double-clicked (self-contained, no .NET needed).
Autostart can be toggled from the panel too; `uninstall.ps1` removes the autostart entry and the install
folder (`-Purge` takes the state files with it).

There is an npm package as well - it is only a wrapper: on install it downloads the zip from the matching
Release and refuses to unpack it unless the sha256 matches:

```sh
npm install -g codex-quota-chip
codex-quota-chip --check
```

Requirements: Windows 10/11 x64, and a Windows desktop Codex that is installed and signed in (the app
looks for `codex.exe` under `%LOCALAPPDATA%\OpenAI\Codex\bin\*\` or on `PATH`).

## Settings

`%LOCALAPPDATA%\CodexQuotaChip\settings.json` (see `settings.example.json`):

| key | default | meaning |
|---|---|---|
| `refresh_seconds` | 60 | how often to read, minimum 30 |
| `display_side` | `right` | `right` = left of the tray, `left` = the taskbar's far end |
| `warn_below` | 30 | below this much left, turn amber |
| `critical_below` | 15 | below this much left, turn red |
| `notify_on_reset` | true | notify when a quota window refills |
| `plate` | true | draw the dark rounded plate behind the strip |

Everything the panel offers is stored here, and editing the file by hand is fine - a change takes effect
at the next start or the next time the panel is opened.

### How a refill is recognised

Not by the local clock, and with no extra state kept: when a window refills the server pushes `resetsAt`
**forward by nearly a whole window** (about five hours for the 5-hour window, about seven days for the
weekly one), so "the reset time jumped forward by at least 70% of the window" is what counts as a
refill. That stays right across restarts, sleep and the app not running at all, and the same refill is
never announced twice. A window with **no** reset time in the previous record is one that has just
started being used: not a refill, no notification.

**Why the threshold has to be that high**: the server slides the end of the weekly window forward a
little as you use it (minutes at a time). 0.2.2 started out calling any forward move a refill, so every
single read qualified - a false notification every two or three minutes. 0.2.3 judges by window length
instead, and no amount of drift reaches 70%.

Notifications go through system toasts (they land in the Action Center and honour Focus Assist /
do not disturb). On its first run the app registers an app id for itself under
`HKCU\Software\Classes\AppUserModelId\easerlee.CodexQuotaChip` - no Start Menu shortcut and no installer
needed. If the system has no toast interface it falls back to a notification-area balloon.

## Command line

```
CodexQuotaChip.exe --check      # state folder, codex path, where it would park (and the real width), no UI
CodexQuotaChip.exe --once       # read once and print JSON, no UI
CodexQuotaChip.exe --notify-test # send a test notification now (no need to wait for a refill)
  --codex-home <path>           # a different Codex home (default %CODEX_HOME% or ~/.codex)
  --codex-exe <path>            # an explicit codex.exe
  --refresh-seconds <n>         # override the read interval
  --home <path>                 # a different state folder
```

## Building from source

Needs the .NET 8 SDK:

```powershell
dotnet build .\src\CodexQuotaChip\CodexQuotaChip.csproj -c Release
dotnet run   --project .\tests\CodexQuotaChip.Tests          # self-tests
pwsh -File .\tools\make-icon.ps1                             # regenerate app.ico
pwsh -File .\build-publish.ps1 -Version 0.2.11               # zip under publish\
```

The icon is not a binary dropped in the repository: `tools/make-icon.ps1` draws it - a dark rounded
square with the two quota bars - at 16/24/32/48/64/128/256, so a new look is a script change and a rerun.

The self-tests cover what has a rule worth stating: parsing and window classification, turning a reading
into text, account ordering, where the strip parks, bar geometry, how the strip's width is measured, the
full bar a refilled window gets, the freshness behind the link light, the click-outside rule, repairing
the settings file, re-resolving codex.exe when it disappears, and the account record file.

## Known limits

- `codex app-server` is not a promised-stable interface, so a desktop Codex update can change method or
  field names. When a read fails the reason goes to the log
  (`%LOCALAPPDATA%\CodexQuotaChip\logs\app.log`), the strip keeps the last good reading, and the status
  light turns amber or red.
- Only the **logged-in account** is read. An account that has been signed out keeps the numbers from the
  last time it was read, and its dot goes grey to say that this link has stopped; its countdown is drawn
  locally from the stored reset time, so it reads `已重置` once that time passes - a local inference, not
  another read of the server. Keeping it refreshing would mean storing credentials for every account,
  which this app deliberately does not do.
- The numbers are for everyday reference, not for billing.

## Licence

MIT, see `LICENSE`. This is not an OpenAI project and is not affiliated with OpenAI.
