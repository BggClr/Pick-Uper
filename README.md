# Browser Pick-Uper
## Configure your browsers to work as your wish

A .NET 10 app that registers itself as a Windows default browser but doesn't actually
render anything: it reads a JSON config from your home folder and forwards the clicked
link to whichever browser (and profile) your rules point to.

```
click a link    →  Windows launches pick-uper.exe "https://acme.slack.com/..."
                →  ~/.pick-uper.json: first matching rule → browser "work"
                →  chrome.exe --profile-directory="Profile 1" https://acme.slack.com/...
```

## Quick start

On a Windows machine, from the repo root:

```powershell
.\scripts\install.ps1 -Build
```

All of the install logic (copying to `%LOCALAPPDATA%\Programs\pick-uper`, a Start Menu
shortcut, a starter `~\.pick-uper.json`, registering in the registry) lives right in the
app, in the `pick-uper --install` command — `install.ps1` just optionally builds the exe
and calls that command. No administrator rights needed, everything is written under
`HKCU`. It finishes by opening Settings → Apps → Default apps.

There you need to pick **Pick-Uper** for `HTTP`, `HTTPS`, `.htm` and `.html` — Windows
10/11 fundamentally does not let an app make itself the default; only the user can do
that by hand.

If you already have a built exe (say, from `scripts/build.sh` on a Mac) — copy
`pick-uper.exe` to the Windows machine anywhere and just run:

```powershell
.\pick-uper.exe --install
```

It copies itself into `%LOCALAPPDATA%\Programs\pick-uper`, registers, and prints status;
`--no-settings` skips the final step of opening Settings.

Uninstall: `.\scripts\uninstall.ps1` (pick another browser in Settings first) — also a
thin wrapper, the actual work is in `pick-uper --uninstall` (unregisters, removes the
shortcut and installed files; `-RemoveConfig` / `--remove-config` also deletes
`~\.pick-uper.json`).

### Building manually

From Mac/Linux (cross-compiled thanks to `EnableWindowsTargeting`) or from Windows:

```bash
./scripts/build.sh                # win-x64, Release, ReadyToRun
./scripts/build.sh -r win-arm64   # for ARM laptops
./scripts/build.sh -c             # smaller (~11 MB), but no ReadyToRun
./scripts/build.sh -a             # Native AOT — Windows-only, see below
./scripts/build.sh -o dist        # custom output directory
```

The direct `dotnet` equivalent:

```powershell
dotnet publish src\PickUper\PickUper.csproj -c Release -r win-x64 -o artifacts\publish\win-x64
```

The result is a single self-contained `pick-uper.exe` (~20 MB, trimmed + ReadyToRun, no
.NET runtime needed on the target machine) — every dependency is already inside the exe.

The size trades off in favor of speed: the process starts on every link click, so the
code is precompiled and the file isn't compressed. If size matters more (~11 MB), use
`-c` on `build.sh`, or add
`-p:PublishReadyToRun=false -p:EnableCompressionInSingleFile=true` by hand.

### Native AOT

`./scripts/build.sh -a` publishes with Native AOT instead of ReadyToRun — startup drops
to ~5ms instead of ~40ms, since there's no JIT and no runtime to spin up at all, just a
native binary.

This **must run on an actual Windows machine** (PowerShell, WSL or Git Bash all work, as
long as the .NET SDK and the Visual Studio Build Tools / MSVC linker are installed) —
Native AOT needs `link.exe` to produce the final binary, and that can't be cross-compiled
from macOS or Linux. Running `-a` from a non-Windows host fails fast with `dotnet`'s own
"Cross-OS native compilation is not supported" error.

The direct `dotnet` equivalent:

```powershell
dotnet publish src\PickUper\PickUper.csproj -c Release -r win-x64 -p:PublishAot=true -p:PublishReadyToRun=false
```

## Config

Looked up in this order, first one found wins:

1. `%PICKUPER_CONFIG%` (if the variable is set — only that one)
2. `%USERPROFILE%\.pick-uper.json`
3. `%USERPROFILE%\pick-uper.json`
4. `%USERPROFILE%\.config\pick-uper\config.json`

The file supports `//` comments and trailing commas. Full example —
[`pick-uper.sample.json`](pick-uper.sample.json).

`pick-uper --init-config` (also run by `--install`) doesn't just write that sample
verbatim: it scans the machine for known browsers (`App Paths` in the registry, the
usual `Program Files`/`LocalAppData` install locations, `PATH`) and seeds `browsers`
with the ones it actually finds, at their real paths — Edge preferred as
`defaultBrowser` if present, otherwise whichever was found first. `rules` starts empty;
add your own referencing the generated browser keys. Only falls back to the static
sample above if no known browser is found at all.

```jsonc
{
  "defaultBrowser": "edge",

  "browsers": {
    "edge":   { "path": "msedge.exe" },
    "chrome": { "path": "chrome.exe" },
    "work":   { "path": "chrome.exe", "args": ["--profile-directory=Profile 1"] },
    "private":{ "path": "firefox.exe", "args": ["-private-window", "{url}"] }
  },

  "rules": [
    { "matches": ["*.slack.com", "*.atlassian.net"], "browser": "work" },
    { "match": "*.github.com", "browser": "chrome" },
    { "match": "regex:^https://([^/]*\\.)?corp\\.example\\.com/", "browser": "work" }
  ],

  "logging": { "enabled": true }
}
```

### `browsers`

| Field  | What it does |
|--------|--------------|
| `path` | Full path to the exe, or just a name (`chrome.exe`, `chrome`, `edge`, `firefox`, `brave`, `vivaldi`, `opera`, `yandex`, `zen`…). A name is looked up in the registry's `App Paths`, in `Program Files`/`LocalAppData`, and in `PATH`. |
| `args` | Launch arguments. If `{url}` appears among them, the URL is substituted there; otherwise it's appended as the last argument. |
| `name` | Optional human-readable name, shown by `--status` and in logs. |

Different profiles of the same browser are just different keys with the same `path` and
different `args` (`--profile-directory=...` for Chrome/Edge, `-P name` for Firefox).

### `rules`

Rules are checked top to bottom, first match wins. Nothing matched — the URL goes to
`defaultBrowser`. A rule can have a single `match` or a list of `matches` (matches if any
pattern matches), plus its own `args`, which override the browser's `args`, and a
`comment`, which is ignored.

Pattern syntax (all case-insensitive):

| Pattern | Matches |
|---------|---------|
| `github.com` | exactly this host |
| `*.github.com` | the domain itself **and** any subdomains (`github.com`, `gist.github.com`) |
| `github.com/anthropics/*` | host plus a path prefix (`/anthropics` and everything under it) |
| `youtube.com/watch*` | a path with a query string (`/watch?v=…`) |
| `localhost:*/*` | a host with an explicit port (bare `localhost` without a port needs a separate `localhost/*`) |
| `https://*.corp.local/*` | a pattern with a scheme is matched against the whole URL |
| `regex:^https://.*\.corp\.` | a regular .NET regex against the whole URL |

`*` matches any number of characters, `?` matches exactly one.

### `logging`

`{ "enabled": true }` turns on verbose logging to
`%LOCALAPPDATA%\PickUper\pick-uper.log` (customizable via `"path"`). Errors are always
written there regardless of the flag: the app has no window to complain in when Explorer
launched it.

## Commands

Double-clicking `pick-uper.exe` (or running it with no arguments at all) opens a numbered
menu of the same commands instead of just printing help and closing — pick one, and the
window waits for a keypress before it closes so the output doesn't just flash by.

```
pick-uper <url>            open the URL in the browser your rules select
pick-uper --install        install to %LOCALAPPDATA%\Programs\pick-uper, shortcut, config, registration
pick-uper --uninstall      unregister, remove the shortcut and installed files
pick-uper --check <url>    show which browser a URL would open, without opening it
pick-uper --status         registration, config, browsers detected on this machine
pick-uper --register       register in the registry (HKCU)
pick-uper --unregister     remove the registration
pick-uper --set-default    open "Default apps"
pick-uper --init-config    write a starter ~\.pick-uper.json
pick-uper --config <path>  use a different config file
pick-uper --no-settings    with --install, skip opening "Default apps"
pick-uper --remove-config  with --uninstall, also delete ~\.pick-uper.json
pick-uper --version
```

`--check` is the main tool for debugging rules:

```
> pick-uper --check https://acme.slack.com/messages
config : C:\Users\me\.pick-uper.json
url    : https://acme.slack.com/messages
host   : acme.slack.com
match  : rule #1 ('*.slack.com') -> work
exe    : C:\Program Files\Google\Chrome\Application\chrome.exe  [App Paths]
command: ...\chrome.exe --profile-directory="Profile 1" https://acme.slack.com/messages
```

The app is built as a `WinExe`, so no console flashes when you click a link; for
commands it attaches to the parent process's console when there is one (`cmd`,
PowerShell), or allocates a fresh console window otherwise (double-clicked from
Explorer).

## What registration does

Everything under `HKEY_CURRENT_USER`, no administrator needed:

- `Software\Clients\StartMenuInternet\PickUper` — the "browser" card: name, icon, launch
  command and `Capabilities` with `URLAssociations` (`http`, `https`) and
  `FileAssociations` (`.htm`, `.html`, `.shtml`, `.xht`, `.xhtml`);
- `Software\RegisteredApplications\PickUper` — a link to those `Capabilities`, which is
  how Windows shows the app in the "Default apps" list;
- `Software\Classes\PickUperURL` — the ProgID with `shell\open\command` =
  `"pick-uper.exe" "%1"`;
- `App Paths\pick-uper.exe` — so the exe can be found by name.

The registry points at a specific path, so don't move the exe after registering — or
just run `--register` again.

## Resilience

A default browser isn't allowed to "fail to open a link", so:

- a broken or missing config doesn't block the click — the link goes to whatever browser
  is found on the system, and the reason is logged;
- a rule pointing at a browser that doesn't exist is skipped rather than breaking
  parsing;
- if the exe from the config isn't found or fails to start, there's a second attempt via
  an auto-detected browser;
- a rule pointing back at pick-uper itself is dropped — otherwise it would recurse
  forever.

To check a config for these kinds of issues: `pick-uper --status` (the `warning:`
section).

## Layout

```
src/PickUper.Core/     config parsing, URL matching, browser selection (net10.0, cross-platform)
src/PickUper/          CLI, registry, process launching (net10.0-windows, WinExe)
tests/                 xunit tests for the core — run on any OS
scripts/               build.sh (build), install.ps1 / uninstall.ps1
```

```bash
dotnet test
```
