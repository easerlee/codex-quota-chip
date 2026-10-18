<div align="center">

# Codex Quota Chip

A small Codex quota strip that lives on the Windows taskbar: one glance for what is left, right-click for every account.

**English** | [简体中文](README.md) ・ [Changelog](CHANGELOG.md)

![Codex Quota Chip](docs/icon.png)

</div>

## What it does

**The two things it is for:**

1. **The logged-in account's quota, live** - a strip parked on the taskbar: how much of the 5-hour and the
   weekly window is left, and when each refills, at a glance;
2. **Every account's quota, in one place** - the right-click panel lists the last values read for each
   account this machine has used, and marks which one is logged in now.

**Everything else**:

- **Refill notifications**: a Windows notification when a quota window refills;
- **Reset credits**: how many manual resets an account has left, as ticket icons (with each one's expiry);
- **One instance**: lives on the taskbar, no window, no Alt+Tab entry, never takes focus;
- **Read-only, no credentials stored**.

## What it looks like

![Plate on](docs/chip.png) &nbsp;&nbsp;&nbsp;&nbsp; ![Plate off](docs/chip-light.png)

A small block on the taskbar, three lines: the 5-hour bar, the weekly bar, and "5H reset clock / weekly
countdown + status light". The bars change colour with what is left (green / amber / red), and the small dot
at the bottom right is the status light (fine / reading or the data has gone stale / nothing read yet / the
read failed).

The strip draws a dark rounded plate behind itself by default (left), or can leave it off and sit straight on
the taskbar (right); the palette follows the taskbar's light/dark setting.

The mouse: **left click refreshes, right click opens the panel**.

The right-click panel:

![The right-click panel](docs/panel.png)

It shows each account's quota: the name (the logged-in one is marked `【当前】`), the tickets for how many
manual resets it has left, and the two rows of numbers. The dot in front of a name says whether that
account's data is still being refreshed - green while it is live, grey once it has stopped (an account that
has been signed out, or the stretch when a token was revoked). Each account's clock and countdown are
rendered from the time the server gave; a window that has just refilled, or whose reset time has passed,
reads `已重置`, with its bar drawn full green.

## How the numbers are read

The app starts the command line that ships with the local Codex:

```
codex.exe app-server --listen stdio://
```

then calls `account/rateLimits/read` over JSON-RPC and tells the windows apart by `windowDurationMins`
(about 300 minutes → 5H, about 10080 → week) rather than assuming which of `primary`/`secondary` is which.
`rateLimitResetCredits` in the same answer is how many reset credits the account has left; the panel's
tickets come from it. Each read starts a process and ends it (about 1.5 seconds per read).

- **Read-only**: it never writes to or changes any Codex file; `auth.json` is only read, to find out which
  account is logged in.
- **No credentials stored**: an account record holds an id, an email, percentages and times. No tokens.
- The state files live in `%LOCALAPPDATA%\CodexQuotaChip\`.

### How a refill is recognised

- **The account being read**: not by the local clock - when a window refills the server pushes `resetsAt`
  forward by nearly a whole window, so "the reset time jumped forward by at least 70% of the window" is what
  counts as a refill; that stays right across restarts, sleep and the app not running at all, and the same
  refill is never announced twice.
- **An account that is not being read**: nothing is consuming its quota, so once the reset time in its last
  reading goes by, the window is full again - announced once, with the small print saying it was worked out
  from that last reading.

Notifications go through system toasts (they land in the Action Center and honour Focus Assist / do not
disturb): the app registers an app id for itself and makes the process answer to the same one - an unpackaged
program that skips the second half gets its toast taken and then quietly dropped. After sending, it checks
the system's own history and falls back to a notification-area balloon if the toast did not arrive.

## Installing

A portable package, no administrator rights needed. After unzipping, in that folder:

```powershell
pwsh -File .\install.ps1            # installs to %LOCALAPPDATA%\Programs\CodexQuotaChip
pwsh -File .\install.ps1 -Autostart # and writes the autostart entry
```

`CodexQuotaChip.exe` in the zip can also just be double-clicked (self-contained, no .NET needed). Autostart
can be toggled from the panel too; `uninstall.ps1` removes the autostart entry and the install folder
(`-Purge` takes the state files with it).

There is an npm package as well - it is only a wrapper: on install it downloads the zip from the matching
Release and refuses to unpack it unless the sha256 matches:

```sh
npm install -g codex-quota-chip
codex-quota-chip --check
```

Requirements: Windows 10/11 x64, and a Windows desktop Codex that is installed and signed in (the app looks
for `codex.exe` under `%LOCALAPPDATA%\OpenAI\Codex\bin\*\` or on `PATH`).

## Settings file

`%LOCALAPPDATA%\CodexQuotaChip\settings.json`; every key and its default is in
[`settings.example.json`](settings.example.json), and what the panel changes is written here too.
`CODEX_QUOTA_CHIP_HOME` or `--home` moves the state folder elsewhere.

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
pwsh -File .\tools\make-icon.ps1                             # regenerate app.ico and docs\icon.png
pwsh -File .\build-publish.ps1 -Version 0.2.13               # zip under publish\
```

The icon is not a binary dropped in the repository: `tools/make-icon.ps1` draws it - a dark rounded square
with the two quota bars - at 16/24/32/48/64/128/256, and the README's header image `docs/icon.png` comes out
of the same script.

The self-tests cover what has a rule worth stating: parsing and window classification, turning a reading
into text, account ordering, where the strip parks, bar geometry, how the strip's width is measured, the
full bar a refilled window gets, the freshness behind the link light, a refill being reported only once,
the click-outside rule, repairing the settings file, re-resolving codex.exe when it disappears, and the
account record file.

## Known limits

- `codex app-server` is not a promised-stable interface, so a desktop Codex update can change method or field
  names; when a read fails the reason goes to the log
  (`%LOCALAPPDATA%\CodexQuotaChip\logs\app.log`), the strip keeps the last good reading and the status light
  turns amber or red.
- Only the **logged-in account** is read. An account that has been signed out keeps the numbers from the last
  time it was read (its dot goes grey), and its countdown is drawn locally from the stored reset time - once
  that passes it reads `已重置` and says so once. That is an inference, not another read of the server.
  Keeping it refreshing would mean storing credentials for every account, which this app deliberately does
  not do.
- The numbers are for everyday reference, not for billing.

## Licence

MIT, see `LICENSE`. This is not an OpenAI project and is not affiliated with OpenAI.
