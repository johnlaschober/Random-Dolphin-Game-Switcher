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

**UI** — `MainForm.cs` + `SettingsForm.cs`  
`MainForm` owns a `System.Windows.Forms.Timer` for the countdown display (separate from the engine's `Task.Delay`). The game `ListBox` uses owner-draw to show play counts and highlight the current game. All engine callbacks cross-invoke to UI thread via `InvokeRequired` / `Invoke`.

**Hotkeys** — `DolphinHotkeys.cs`  
Finds the Dolphin process by name, brings it to foreground, then uses `SendKeys.SendWait` to send F-key hotkeys. Save = `Shift+F{slot}`, Load = `F{slot}`. There is a `GracePeriodSeconds` delay before/after hotkeys to let Dolphin register them.

## Key behaviors to preserve

- `PickNext` weights by `1 / (playCount + 1)` and excludes the last-played game when >1 game available.
- `MarkCurrentDone` sets `Finished = true` and calls `ForceSkip` — the engine skips the save step for finished games.
- `KillDolphin` tries `CloseMainWindow` first, kills after 3s timeout.
- Adding/removing games is disabled while the engine is running.
