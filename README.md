# Dolphin Roulette

A WinForms app that randomly cycles through a list of games in Dolphin,
automatically saving and loading savestates between switches.

---

## Setup

1. Open the app and click **Settings**
2. Point **Dolphin.exe path** at your Dolphin installation
3. Set **Savestate slot** — pick whichever slot you'll use (default: 1)
4. Set your **Min/Max play time** in seconds
5. Before starting the roulette, open each game in Dolphin and
   **manually save a state to the configured slot** (Shift+F1 for slot 1)

---

## Usage

| Button | What it does |
|--------|-------------|
| **+ Add ISO** | Browse for ISO/RVZ/GCZ files to add to the rotation |
| **− Remove** | Remove selected game from the list entirely |
| **▶ Start** | Begin the roulette |
| **■ Stop** | Save current game state and stop |
| **⏭ Skip** | Save current state and jump to the next game now |
| **Completed** | Mark current game as finished — removes it from rotation (no save) |

---

## How the fairness weighting works

Games are picked using **inverse-frequency weighting**:
a game played 0 times is twice as likely to be picked as one played 1 time, etc.
The same game will never play twice in a row.
Play counts survive restarts (stored in `roulette_settings.json` next to the exe).

---

## Recommended Dolphin settings

In Dolphin: **Options → Configuration → Interface**, disable:

- **Confirm on Stop** — prevents a dialog blocking the auto-switch
- **Pause on Focus Loss** — prevents Dolphin pausing when the app briefly takes focus to send hotkeys

---

## Notes

- Savestate hotkeys (F1 / Shift+F1) are sent via `SendInput` which briefly brings
  Dolphin to the foreground. Avoid clicking during the grace period.
- Dolphin grace period (default 500 ms) is the wait after sending a hotkey before
  killing/switching. Increase it on slower machines in Settings.


## Building

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) (to build)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8) (to run — must be installed on the target machine)

### Build

```
dotnet publish -c Release -r win-x64
```

The output exe will be in:
```
bin\Release\net8.0-windows\win-x64\publish\DolphinRoulette.exe
```

### Run locally during development

```
dotnet run
```

---
