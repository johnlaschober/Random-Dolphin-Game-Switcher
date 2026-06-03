using System.Diagnostics;

namespace DolphinRoulette;

public enum RouletteState { Stopped, Running, Paused }

public class RouletteEngine
{
    private readonly AppSettings _settings;
    private readonly Random _rng = new();
    private readonly Action<string> _log;
    private readonly Action<GameEntry?, int, RouletteState> _onStateChanged;

    private CancellationTokenSource? _outerCts;  // cancelled by StopAsync only
    private CancellationTokenSource? _cts;        // cancelled by ForceSkip / Pause
    private Task? _runTask;
    private Process? _current;            // instance the user is playing now
    private Process? _next;               // pre-booted buffer for the next game
    private GameEntry? _nextGame;         // game _next was booted with
    private DateTime _nextLaunchUtc;      // when _next started booting
    private CancellationTokenSource? _prebootCts; // stops the keep-minimized watcher
    private string? _lastGameName;
    private GameEntry? _currentGame;

    private volatile bool _pauseRequested = false;
    private volatile bool _resumeRequested = false;
    private TaskCompletionSource? _resumeTcs;

    public RouletteState State { get; private set; } = RouletteState.Stopped;
    public GameEntry? CurrentGame => _currentGame;

    public RouletteEngine(
        AppSettings settings,
        Action<string> log,
        Action<GameEntry?, int, RouletteState> onStateChanged)
    {
        _settings = settings;
        _log = log;
        _onStateChanged = onStateChanged;
    }

    // ── Public controls ────────────────────────────────────────────────

    public void Start()
    {
        if (State != RouletteState.Stopped) return;
        _outerCts = new CancellationTokenSource();
        _cts = _outerCts;
        State = RouletteState.Running;
        _runTask = Task.Run(() => RunLoop(_outerCts.Token));
    }

    public async Task StopAsync()
    {
        if (State == RouletteState.Stopped) return;
        _outerCts?.Cancel();
        _resumeTcs?.TrySetResult(); // unblock if paused
        if (_runTask != null) await _runTask;
        State = RouletteState.Stopped;
        NotifyStateChanged(0);
    }

    public void Pause()
    {
        if (State != RouletteState.Running) return;
        _pauseRequested = true;
        _cts?.Cancel();
    }

    public void Resume()
    {
        if (State != RouletteState.Paused) return;
        _resumeRequested = true;
        _resumeTcs?.TrySetResult();
    }

    /// <summary>Forces an immediate switch to the next game (saves first).</summary>
    public void ForceSkip()
    {
        _cts?.Cancel();
        _resumeTcs?.TrySetResult(); // wake up if paused; resumeRequested=false → inner loop breaks
    }

    /// <summary>Marks the current game finished and removes it from future rotation.</summary>
    public void MarkCurrentDone()
    {
        if (_currentGame == null) return;
        _currentGame.Finished = true;
        _settings.Save();
        _log($"  '{_currentGame.Name}' marked as finished and removed from rotation.");
        ForceSkip();
    }

    // ── Core loop ──────────────────────────────────────────────────────

    private async Task RunLoop(CancellationToken outerToken)
    {
        try
        {
            // ── Prime ─────────────────────────────────────────────────
            // The first game has no pre-booted buffer, so it pays the full
            // boot cost. Every swap after this one is hidden behind playtime.
            var first = PickFromRotation();
            if (first == null) { _log("  All games are finished! Nothing left in rotation."); return; }

            _current = await BootGameAsync(first);
            if (_current == null) { _log("  [!] Failed to launch Dolphin. Check the path in Settings."); return; }

            BecomeCurrent(first, out int playSeconds);
            LoadCurrent();
            PrebootNext();

            // ── Main loop ─────────────────────────────────────────────
            while (!outerToken.IsCancellationRequested)
            {
                await PlayWaitAsync(playSeconds, outerToken);

                if (outerToken.IsCancellationRequested) break;

                // If the current game is the only one left in rotation, there is
                // nothing to switch to — keep playing the same running instance
                // (just re-roll the timer). No save/kill/relaunch churn.
                var remaining = Rotation();
                if (remaining.Count == 1 && remaining[0] == _currentGame)
                {
                    playSeconds = _rng.Next(_settings.MinPlaySeconds, _settings.MaxPlaySeconds + 1);
                    _log($"  Only '{_currentGame!.Name}' left — continuing without switching.");
                    NotifyStateChanged(playSeconds);
                    continue;
                }

                // Save the outgoing game while it is still foreground.
                if (_currentGame is { Finished: false })
                {
                    DolphinHotkeys.BringToForeground(_current!);
                    _log($"\n  Saving state for '{_currentGame.Name}'…");
                    DolphinHotkeys.SaveState(_current!, _settings.SavestateSlot, _log);
                    await Task.Delay(1000, CancellationToken.None); // let the savestate flush to disk
                }
                else if (_currentGame != null)
                {
                    _log($"  Skipping save — '{_currentGame.Name}' is finished.");
                }

                if (outerToken.IsCancellationRequested) break;

                var avail = Rotation();
                if (avail.Count == 0)
                {
                    _log("  All games are finished! Nothing left in rotation.");
                    break;
                }

                // ── Acquire the next game + a booted instance for it ───
                GameEntry nextGame;
                Process? nextProc;

                if (_next != null && _nextGame != null && !_next.HasExited && avail.Contains(_nextGame))
                {
                    // Buffer hit — make sure it has booted long enough to be playable.
                    nextGame = _nextGame;
                    nextProc = _next;
                    _next = null;
                    _nextGame = null;

                    var elapsed = DateTime.UtcNow - _nextLaunchUtc;
                    var grace = TimeSpan.FromMilliseconds(_settings.GracePeriodMs);
                    if (elapsed < grace)
                        await Task.Delay(grace - elapsed, CancellationToken.None);

                    nextProc.Refresh();
                    if (nextProc.HasExited || nextProc.MainWindowHandle == IntPtr.Zero)
                    {
                        _log("  [!] Pre-booted instance not ready — relaunching (buffer miss).");
                        KillProcess(ref nextProc);
                        nextProc = await BootGameAsync(nextGame);
                    }
                }
                else
                {
                    // No usable buffer (e.g. only one game left) — synchronous boot.
                    nextGame = PickNext(avail);
                    nextProc = await BootGameAsync(nextGame);
                }

                if (nextProc == null) { _log("  [!] Failed to launch Dolphin. Check the path in Settings."); break; }

                // ── Swap: load the next instance, then kill the outgoing one ──
                _prebootCts?.Cancel(); // stop fighting the buffer's focus — we want it foreground now
                var outgoing = _current;
                _current = nextProc;
                BecomeCurrent(nextGame, out playSeconds);
                LoadCurrent();
                KillProcess(ref outgoing);

                PrebootNext();
            }
        }
        finally
        {
            _prebootCts?.Cancel();
            KillProcess(ref _current);
            KillProcess(ref _next);
            _nextGame = null;
            State = RouletteState.Stopped;
            _currentGame = null;
            NotifyStateChanged(0);
        }
    }

    /// <summary>Inner wait for the current game — returns when it's time to
    /// switch away (timer elapsed, Skip, Done, or Stop). Handles pause/resume.</summary>
    private async Task PlayWaitAsync(int playSeconds, CancellationToken outerToken)
    {
        while (true)
        {
            using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
            _cts = innerCts;

            try { await Task.Delay(playSeconds * 1000, innerCts.Token); }
            catch (TaskCanceledException) { }

            if (!_pauseRequested || outerToken.IsCancellationRequested) return;

            // ── Paused ────────────────────────────────────────────────
            _pauseRequested = false;
            State = RouletteState.Paused;
            NotifyStateChanged(0);
            _log($"  Paused — Dolphin still running.");

            _resumeTcs = new TaskCompletionSource();
            try { await _resumeTcs.Task.WaitAsync(outerToken); }
            catch (OperationCanceledException) { return; }

            bool resuming = _resumeRequested;
            _resumeRequested = false;

            if (!resuming) { State = RouletteState.Running; return; } // Skip/Done

            // ── Resumed ───────────────────────────────────────────────
            playSeconds = _rng.Next(_settings.MinPlaySeconds, _settings.MaxPlaySeconds + 1);
            _log($"  ▶  Resumed '{_currentGame!.Name}' — new play time: {FormatTime(playSeconds)}");
            State = RouletteState.Running;
            NotifyStateChanged(playSeconds);
        }
    }

    // ── Turn helpers ────────────────────────────────────────────────────

    private List<GameEntry> Rotation() => _settings.Games.Where(g => !g.Finished).ToList();

    private GameEntry? PickFromRotation()
    {
        var avail = Rotation();
        return avail.Count == 0 ? null : PickNext(avail);
    }

    /// <summary>Launches a game and waits the grace period for it to boot.</summary>
    private async Task<Process?> BootGameAsync(GameEntry game)
    {
        _log($"  Launching Dolphin with: {game.Path}");
        var proc = LaunchDolphin(game.Path);
        if (proc == null) return null;
        await Task.Delay(_settings.GracePeriodMs, CancellationToken.None);
        return proc;
    }

    /// <summary>Marks a game (whose instance is already in _current) as the one
    /// being played: picks a playtime, logs the banner, notifies the UI.</summary>
    private void BecomeCurrent(GameEntry game, out int playSeconds)
    {
        _currentGame = game;
        _lastGameName = game.Name;
        State = RouletteState.Running;

        playSeconds = _rng.Next(_settings.MinPlaySeconds, _settings.MaxPlaySeconds + 1);
        _log($"\n{'═',50}".Replace(" ", "═"));
        _log($"  Selected : {game.Name}");
        _log($"  Play time: {FormatTime(playSeconds)}");
        NotifyStateChanged(playSeconds);
    }

    /// <summary>Foregrounds _current and loads its savestate.</summary>
    private void LoadCurrent()
    {
        DolphinHotkeys.MoveTopLeft(_current!); // the played game always lives top-left
        _log($"  Loading savestate slot {_settings.SavestateSlot}…");
        DolphinHotkeys.LoadState(_current!, _settings.SavestateSlot, _log);
        _currentGame!.PlayCount++;
        _settings.Save();
    }

    /// <summary>Pre-boots the next game in a second instance so the upcoming
    /// swap is instant. Skipped when fewer than two games remain (the buffer
    /// would have to be the same game, which would clash on its savestate).</summary>
    private void PrebootNext()
    {
        var avail = Rotation();
        if (avail.Count < 2) { _next = null; _nextGame = null; return; }

        var pick = PickNext(avail); // excludes _lastGameName (the current game)
        _log($"  Pre-booting next: {pick.Name}");
        var proc = LaunchDolphin(pick.Path, minimized: true);
        if (proc == null) { _next = null; _nextGame = null; return; }

        _next = proc;
        _nextGame = pick;
        _nextLaunchUtc = DateTime.UtcNow;

        // The buffer boots minimized so it never paints on-screen. The watcher
        // keeps it down + refocuses the active game if it grabs focus. The swap
        // restores it into the top-left.
        if (_current != null)
        {
            _prebootCts?.Cancel();
            _prebootCts = new CancellationTokenSource();
            DolphinHotkeys.KeepMinimized(proc, _current, _prebootCts.Token);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private GameEntry PickNext(List<GameEntry> available)
    {
        var eligible = available.Count > 1
            ? available.Where(g => g.Name != _lastGameName).ToList()
            : available;

        // Weight = 1 / (playCount + 1)
        double[] weights = eligible.Select(g => 1.0 / (g.PlayCount + 1)).ToArray();
        double total = weights.Sum();
        double roll = _rng.NextDouble() * total;

        double cumulative = 0;
        for (int i = 0; i < eligible.Count; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative) return eligible[i];
        }
        return eligible[^1];
    }

    private Process? LaunchDolphin(string isoPath, bool minimized = false)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _settings.DolphinPath,
                Arguments = $"--batch --exec \"{isoPath}\"",
                // Minimized launch is used for the pre-booted buffer so it never
                // paints on-screen (no white flash). Requires UseShellExecute so
                // the requested window state reaches the new process.
                UseShellExecute = minimized,
                WindowStyle = minimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal,
            };
            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            _log($"  [!] Exception launching Dolphin: {ex.Message}");
            return null;
        }
    }

    private void KillProcess(ref Process? proc)
    {
        if (proc == null) return;
        try
        {
            if (!proc.HasExited)
            {
                // State already saved to disk — no need to wait long for a
                // graceful close. Short window for Dolphin to flush, then kill.
                proc.CloseMainWindow();
                if (!proc.WaitForExit(800))
                    proc.Kill();
            }
        }
        catch { /* process may already be gone */ }
        proc = null;
    }

    private void NotifyStateChanged(int remainingSeconds) =>
        _onStateChanged(_currentGame, remainingSeconds, State);

    private static string FormatTime(int seconds)
    {
        int m = seconds / 60, s = seconds % 60;
        return m > 0 ? $"{m}m {s}s" : $"{s}s";
    }
}
