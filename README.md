# Dolphin Roulette

A WinForms app that randomly cycles through a list of games in Dolphin,
automatically saving and loading savestates between switches.

---

## Setup

1. Open the app and click **Settings**
2. Point **Dolphin.exe path** at your Dolphin installation
3. Set **Savestate slot** — pick whichever slot you'll use (default: 1)
4. Set your **Min/Max play time** in seconds
5. Open a game in Dolphin and adjust the window size...
6. Optionally pre-setup some save states for each game ahead of time in savestate slot 1.

---

## Required Dolphin setup (one time)

This app **double-buffers** Dolphin: while you play one game, it pre-boots the
next game in a second hidden Dolphin instance so switching is near-instant. That
requires the following Dolphin configuration — set these once:

**Options → Configuration → Interface**
- **Confirm on Stop** — **OFF**.
- **Pause on Focus Loss** — **ON**.

**Options → Configuration → GameCube**
- **Memory card** → set both slots to **Memory Card**.

**Options → Hotkey Settings → Save and Load State**
- **Save State Slot 1** → set to **Shift + F1**.
- **Load State Slot 1** → set to **F1**.
- If you use a different **Savestate slot** in the app, set Save/Load for that slot the same way (Save = Shift + F*N*, Load = F*N*).

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

## Notes

- Two Dolphin instances are live at once: the one you're playing (top-left) and a
  **minimized**, paused, pre-booted buffer for the next game. The buffer boots
  minimized so it never flashes on-screen; at the switch it restores into the
  top-left and becomes the game you play. The app foregrounds whichever instance
  it's sending hotkeys to. Avoid clicking during a switch.
- A second Dolphin instance launches **minimized** in the taskbar while you play —
  that's the buffer booting, not a glitch.
- Savestate hotkeys (F1 / Shift+F1) are sent via `SendInput`, which requires the
  target instance to be foreground.
- Grace period (default 4500 ms) is how long a freshly-launched instance is given
  to boot before its savestate is loaded. Increase it on slower machines in
  Settings if switches load too early.
- The **first** game of a session pays the full boot cost (no buffer exists yet);
  every switch after that is hidden behind playtime.


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
