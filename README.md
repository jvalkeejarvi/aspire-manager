# aspire-manager

A [lazygit](https://github.com/jesseduffield/lazygit)-style terminal UI for .NET Aspire. Watch your
AppHost's resources, tail their logs, and restart or rebuild them without leaving the terminal.

```
╭┤[1] AppHost├───────────────────────╮╭┤[0] Logs: orders-api├─────────────────╮
│ Shop.AppHost   [connected]         ││ 17:12:04 info Now listening on :7246  │
│ …/src/host/Shop.AppHost.csproj     ││ 17:12:04 info Application started     │
╰────────────────────────────────────╯│ 17:12:09 warn Slow query (412ms)      │
╭┤[2] Resources├─────────────────────╮│ 17:12:11 info 200 GET /api/orders     │
│▾ AzureStorageResource (1)          ││                                       │
│    R H storage                     ││                                       │
│▾ Container (2)                     ││                                       │
│    R H redis                       ││                                       │
│    R H sql                         ││                                       │
│▾ Project (3)                       ││                                       │
│    R H orders-api                  ││                                       │
│    R U catalog-api                 ││                                       │
│    R H webui                       ││                                       │
│▾ Executable (1)                    ││                                       │
│    F - frontend-installer          ││                                       │
╰────────────────────────────────────╯╰───────────────────────────────────────╯
 1/2/0 panes   j/k move   / search   enter logs   r/s/b   c cmds   ? help   q quit
```

Each resource row is `state health name`. `g` cycles how the pane arranges itself: grouped under
foldable type headings, one flat list with each row's type after its name, or names alone. The state
initial is coloured — green running, grey stopped,
red failed — and the health initial beside it (`H`, `U`, `D`, or `-` for no health check) is coloured
separately, because a running resource can still be unhealthy.

## Requirements

- **.NET 10 SDK**
- **Aspire CLI** on your `PATH` (`aspire --version`) — built against 13.5.3
- macOS or Linux (developed on macOS; suspend and terminal handling are Unix-only)

There is nothing to configure to get started, and nothing is installed into your AppHost: the tool drives
the `aspire` CLI, so it sees exactly what `aspire describe` and `aspire logs` see.

## Build and run

```bash
dotnet run --project src/AspireManager.Tui            # attaches to the running AppHost
dotnet run --project src/AspireManager.Tui path/to/AppHost.csproj
```

With no argument it attaches to the only running AppHost, and asks which one if several are up.

A native binary, if you would rather have one on your `PATH`:

```bash
dotnet publish src/AspireManager.Tui -r osx-arm64 -c Release
cp src/AspireManager.Tui/bin/Release/net10.0/osx-arm64/publish/aspire-manager ~/bin/
```

That is a self-contained ~20MB executable — copy the one file, nothing else is needed beside it. Use
`-r linux-x64` on Linux; cross-compiling from macOS needs a cross-linker and is not set up here.

## Installing it as a dotnet tool

`install-tool.ps1` packs the working tree and installs it as a global dotnet tool, into whichever directory
the SDK uses on this OS (`~/.dotnet/tools`, or `%USERPROFILE%\.dotnet\tools` on Windows). It is PowerShell
so it runs the same everywhere, and it has a shebang so `./install-tool.ps1` works on macOS and Linux.

```bash
./install-tool.ps1              # install the current working tree
./install-tool.ps1 -Native      # build a native binary for this machine instead
./install-tool.ps1 -Uninstall   # remove it
```

Each run stamps a unique `0.0.0-local.<timestamp>` version. That is not cosmetic: NuGet caches packages by
id and version, so reusing one risks installing the previous build out of `~/.nuget/packages` rather than
what you just compiled.

`-Native` packs a RID-specific tool containing a real native binary — about 90ms to first render against
300ms for the portable one, and no .NET runtime needed to run it. Native AOT cannot cross-compile, so it
only ever targets the machine that builds it; without the flag you get the portable IL tool, which installs
anywhere with a .NET 10 runtime.

To publish to a feed, pack and push as usual:

```bash
dotnet pack src/AspireManager.Tui -c Release -o ./nupkg
dotnet nuget push ./nupkg/aspire-manager.<version>.nupkg -s <feed> -k <key>
```

## Keys

Press `?` for the keys that apply where you are; the list is generated from the bindings themselves, so it
cannot fall out of date. `/` narrows it, as it does in every list here.

### Resources

| Key | |
|---|---|
| `j` `k`, `^d` `^u` | move, page |
| `enter` | show a resource's logs; fold a group heading |
| `r` `s` `b` | restart, stop, rebuild |
| `c` | every command the AppHost offers for this resource |
| `o` `O` | open its first URL, or pick one |
| `e` `E` | open its logs in your editor — buffered, or the full history |
| `g` | cycle grouping: groups, type after name, names only |
| `-` `=` | fold or unfold every group |
| `/`, `esc` | filter by name — and by type where `g` shows it — then clear the filter |

### Logs

| Key | |
|---|---|
| `j` `k`, `^d` `^u` | move, page |
| `/`, `n` `N` | search, next and previous match |
| `e` `E` | open in your editor at the selected line, or the full history |
| `esc` | clear the search, then leave the pane |

### Anywhere

| Key | |
|---|---|
| `1` `2` `0`, `tab` | focus AppHost, resources, logs |
| `^r` | switch to another running AppHost |
| `^z` | suspend to the shell (`fg` to return) |
| `?` | keys for the current pane |
| `q` | quit |

Commands are read from the AppHost, never hardcoded, so a resource offers exactly what the dashboard
offers it. `start`, `stop`, `restart` and `rebuild` run on a single keypress; anything else — including
commands an integration added, such as `delete-azure-resources` — asks you to type the resource name
first. Commands needing arguments are listed but not offered, since this does not build forms.

## Configuration

Optional, at `~/.aspire-manager.json`. Comments and trailing commas are allowed.

```json
{
  "editor": {
    "command":       "emacsclient -n +{line} {file}",
    "commandNoLine": "emacsclient -n {file}"
  }
}
```

`{file}` and `{line}` are substituted; the string is split on whitespace **first**, so a path containing
spaces stays a single argument. No shell is involved, so pipes and variables do not work.

The editor is never waited on — pass whatever flag yours needs to return immediately (`-n` for
`emacsclient`, and simply omit `--wait` for VS Code):

```json
{ "editor": { "command": "code --goto {file}:{line}", "commandNoLine": "code {file}" } }
```

There is deliberately no default. Without this section `e` says so rather than guessing at `$EDITOR` and
possibly blocking the whole UI.

### Groups at startup

Which type groups start folded, and how the pane is arranged:

```json
{
  "groups": {
    "mode": "grouped",
    "default": "collapsed",
    "except": ["Project", "AzureCosmosDB*"]
  }
}
```

`mode` is `grouped`, `typeSuffix` or `plain` — where `g` starts, not where it has to stay. `default` is
`collapsed` or `expanded`, and `except` lists the types that go the other way, matched case-insensitively
with a trailing `*` for a prefix. A value that cannot be read is reported in the status line and falls back
to `grouped` / `expanded`; a name matching no type is silent, since types come and go with what is running.

This is a standing rule, not a one-time seed: a resource type first seen mid-session, or every type after
`^r` switches AppHost, starts the way the config says. Folding by hand overrides it for that type until you
quit — including `-` and `=`, which speak only for the groups on screen when you press them.

`e` writes what the log pane holds, so the line you are on is the line the editor opens. `E` fetches the
AppHost's entire history instead and opens at the top. Both go to
`$TMPDIR/aspire-manager/<apphost>/<resource>.log`, one file per resource, overwritten each time so your
editor reuses its buffer.

## How it works

Everything comes from the `aspire` CLI:

| | |
|---|---|
| `aspire ps` | which AppHosts are running |
| `aspire describe --follow` | resource states, health and available commands |
| `aspire logs --follow` | every resource's logs, one process for all of them |
| `aspire resource <name> <cmd>` | runs a command |

Lines the resource wrote to stderr are marked as such by the CLI and shown in red.

Two long-lived streams feed thread-safe stores; the UI polls them. Both reconnect with a doubling backoff
when the AppHost goes away, and their child processes are killed as a tree on exit, so nothing is left
following your AppHost afterwards. Logs are kept in a bounded ring per resource, which a busy
AppHost fills quickly — reattaching to a large one replayed close to 8000 lines in the first ten seconds.

## Development

```bash
dotnet build
dotnet test                                        # no AppHost required
cd testapphost/AspireManager.TestAppHost && aspire run
```

`testapphost/` is a throwaway AppHost of two sleeping executables — no Docker, up in about 20 seconds — so
the UI can be driven without a real application. One of its resources carries a custom no-op command, to
exercise the confirmation path without pointing anything destructive at a real environment.

`src/AspireManager.Core` holds everything testable without a terminal: CLI output parsing, the resource and
log stores, search, and the command policy. `src/AspireManager.Tui` is the Terminal.Gui layer on top.
`CLAUDE.md` records the decisions and the Terminal.Gui behaviours worth knowing before changing it.
