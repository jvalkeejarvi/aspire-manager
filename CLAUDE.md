# Aspire Manager

A lazygit-style TUI over the `aspire` CLI. See `README.md` for what it does and which keys do it; this file
is only what you need to know before changing it.

## Layout

```
src/AspireManager.Core   CLI output parsing, resource/log stores, search, policy — no terminal types
src/AspireManager.Tui    Terminal.Gui layer
tests/                   xunit.v3 on Microsoft.Testing.Platform; run args go after `--`
testapphost/             two sleep executables, no Docker, ~20s to start — outside tests/ on purpose,
                         since tests/Directory.Build.props enables the MTP runner an AppHost must not inherit
```

Anything decidable without a terminal belongs in Core, where it can be tested. NuGet versions live in
`Directory.Packages.props`. `dotnet build` never pays the AOT cost; only `publish -r <rid>` compiles natively.

## The aspire CLI

Its JSON comes in two shapes and this has caused two bugs: **`--follow` streams NDJSON, without it you get one
pretty-printed document** (`{"resources":[…]}`, `{"logs":[…]}`). Parse the right one.

- Always pass `--apphost`. With several running, the CLI picks one silently.
- `describe` reports `name` with a random suffix and `displayName` without. **`displayName` is the join key**
  for `logs` and `resource <name> <cmd>`. It is not guaranteed unique — replicas share one, and logs carry
  nothing finer, so `ResourceStore` reports the ambiguity rather than pretending to resolve it.
- Every reconnect replays the whole log history, so `LogStore` drops exact repeats.
- Commands come from the AppHost; nothing is hardcoded. `CommandPolicy` is an **allowlist** —
  `start/stop/restart/rebuild` fire on one keypress, everything else needs typed confirmation, because
  nothing in the metadata marks a command destructive and a denylist would miss whatever an integration adds.

## Values from Aspire

**Never bind them to an enum.** States, health and command states are strings interpreted with fallbacks, so
a value we have not seen cannot throw; `UnknownValueTests` pins where each falls. The records declare those
strings non-nullable but the deserialiser fills in null for anything the CLI omits — a resource caught
mid-restart has no `state`, which once crashed the app. `AspireJson` normalises at the boundary: unusable
rows dropped, the rest filled in, so no caller null-checks.

## Terminal.Gui 2.4.17

Chosen over Spectre.Console, which has no focus or pane model. Use `Application.Create()`; the static
`Application.Run`/`Instance` surface is obsolete and most tutorials still show it. Traps, all of which cost
real debugging:

- **Keys go on `app.Keyboard.KeyDown`**, not a view. A focused `ListView` eats bare letters for its
  type-to-search, so view-level handlers never see them. Ctrl combinations carry **no rune** — read the
  letter from `KeyCode` with `CtrlMask` masked off.
- **No cross-thread marshalling exists.** The CLI streams write into the locked Core stores; the window polls
  them on a timer.
- **`Border` is not a `View`** and has no scheme. Panel colour goes on the frame, and every child must be
  pinned back to the default or the whole panel takes it. Capture that default *once* — re-reading a frame
  you have already painted makes the new colour the baseline. A `Scheme` built from one `Attribute` derives
  Focus by swapping fore/background, which renders a focused title as a solid inverted bar.
- **`ListView.SelectedItem` is `int?`**, starts null, does not stick before layout, and setting it on an
  empty list throws. Clear it when a pane empties or a stale index paints a band over nothing.
  `ShellModel.NextIndex` owns the arithmetic. A fresh list opens scrolled to the *bottom*.
- **Nested `app.Run` does not unwind.** `RequestStop` sets a flag a nested loop re-checks only on the next
  keypress. In-session dialogs are therefore overlay views in one loop; only the startup picker, which runs
  before the main window exists, gets its own.
- **The terminal must be restored by hand.** `TerminalState` owns entering and leaving; the teardown must
  pop the kitty keyboard protocol (`CSI < u`, pushed as `CSI > 31 u`) or the shell fills with `5u`-style key
  reports. Derive it from what the driver actually emits, not from what it seems like it should.

## Shared structures

- **Every key is one `Binding`** in `ManagerWindow.BuildBindings`: label, description, panes, matcher,
  action, optional `Available`. `OnKeyDown` dispatches from it and `?` renders from it, so a new key
  documents itself. First match wins, so narrower bindings come first. `Panes.All` means global.
- **Every pick-from-a-list dialog is `ListOverlay`** — palette, AppHost switcher, URL picker, help, startup
  picker. It owns sizing, padding, filtering with `/`, and the cancel/`j`/`k`/Enter keys.
- Unix-only paths (`SIGHUP`, `SIGSTOP`, `/bin/stty`, VT teardown) are guarded; Ctrl-Z reports itself
  unavailable on Windows via `Available`, which removes it from the help too.

## Conventions

Comment style is inherited from the monorepo this started in. **Every comment has to earn its line** — the
test is whether a competent reader with this code in front of them would already know it. Earning it: a
constraint the code cannot show, a framework quirk, an ordering or idempotency requirement, a domain rule
the types do not carry, a deliberate corner cut and its ceiling. Not earning it: restating the code,
repeating a signature, section banners, commented-out code, defending the design against an objection
nobody raised. One line by default, two or three when a constraint needs them; prefer fixing the code over
explaining it.

Tests build their payloads in code — no JSON fixture files. Captured CLI output goes inline as a literal.
