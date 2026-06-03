# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
# Run locally
dotnet run

# Build distributable single-file exe
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# Output: bin\Release\net8.0-windows\win-x64\publish\DolphinRoulette.exe
```

No tests exist in this project.

## Architecture

WinForms app (.NET 8, Windows-only). Three logical layers:

**Data** — `Settings.cs`  
`AppSettings` and `GameEntry` are the only data types. Settings persist to `roulette_settings.json` next to the exe (not in AppData). `GameEntry.PlayCount` and `.Finished` are serialized there and survive restarts.

**Engine** — `RouletteEngine.cs`  
Runs the game-switching loop on a background `Task`. Uses a two-level cancellation scheme: an outer `CancellationToken` for Stop, and an inner linked token that `ForceSkip`/`MarkCurrentDone` cancel mid-turn without stopping the outer loop. State changes are surfaced via the `onStateChanged` callback (always invoked from the background thread — UI must `Invoke`).

**Double-buffering:** the engine keeps two Dolphin instances — `_current` (playing) and `_next` (a pre-booted buffer for the upcoming game). While the current game plays, `PrebootNext` launches the next game in a background instance that auto-pauses via Dolphin's *Pause on Focus Loss*. At swap, the current game is saved (while still foreground), the buffer is foregrounded + loaded, then the outgoing instance is killed. This hides Dolphin's boot cost behind playtime. The first game of a session and any rotation with `<2` unfinished games fall back to synchronous launch (no buffer). Requires specific Dolphin setup — see README.

**UI** — `MainForm.cs` + `SettingsForm.cs`  
`MainForm` owns a `System.Windows.Forms.Timer` for the countdown display (separate from the engine's `Task.Delay`). The game `ListBox` uses owner-draw to show play counts and highlight the current game. All engine callbacks cross-invoke to UI thread via `InvokeRequired` / `Invoke`.

**Hotkeys** — `DolphinHotkeys.cs`  
Takes a specific `Process` (so the right instance is targeted with two Dolphins live), brings its window to foreground via the `AttachThreadInput` trick, then uses `SendInput` with scancodes + hold time to send F-key hotkeys. Save = `Shift+F{slot}` (Shift staggered *before* the F-key, else Dolphin reads a bare F-key = Load), Load = `F{slot}`. `BringToForeground` re-asserts focus after a background pre-boot.

## Key behaviors to preserve

- `PickNext` weights by `1 / (playCount + 1)` and excludes the last-played game when >1 game available.
- `MarkCurrentDone` sets `Finished = true` and calls `ForceSkip` — the engine skips the save step for finished games.
- `KillProcess` tries `CloseMainWindow` first, kills after 800ms timeout (state is already saved by then). On Stop/finish, both `_current` and `_next` are killed in a `finally`.
- Adding/removing games is disabled while the engine is running.
