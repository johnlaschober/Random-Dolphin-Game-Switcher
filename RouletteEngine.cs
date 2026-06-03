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
    private Process? _dolphinProcess;
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
        while (!outerToken.IsCancellationRequested)
        {
            var available = _settings.Games
                .Where(g => !g.Finished)
                .ToList();

            if (available.Count == 0)
            {
                _log("  All games are finished! Nothing left in rotation.");
                State = RouletteState.Stopped;
                NotifyStateChanged(0);
                return;
            }

            var next = PickNext(available);
            _currentGame = next;
            _lastGameName = next.Name;

            int playSeconds = _rng.Next(_settings.MinPlaySeconds, _settings.MaxPlaySeconds + 1);
            _log($"\n{'═',50}".Replace(" ", "═"));
            _log($"  Selected : {next.Name}");
            _log($"  Play time: {FormatTime(playSeconds)}");

            NotifyStateChanged(playSeconds);

            _log($"  Launching Dolphin with: {next.Path}");
            _dolphinProcess = LaunchDolphin(next.Path);

            if (_dolphinProcess == null)
            {
                _log("  [!] Failed to launch Dolphin. Check the path in Settings.");
                State = RouletteState.Stopped;
                NotifyStateChanged(0);
                return;
            }

            await Task.Delay(_settings.GracePeriodMs, CancellationToken.None);
            _log($"  Loading savestate slot {_settings.SavestateSlot}…");
            DolphinHotkeys.LoadState(_settings.SavestateSlot, _log);

            next.PlayCount++;
            _settings.Save();

            // Inner wait loop — handles pause/resume for current game
            while (true)
            {
                using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
                _cts = innerCts;

                try { await Task.Delay(playSeconds * 1000, innerCts.Token); }
                catch (TaskCanceledException) { }

                if (!_pauseRequested || outerToken.IsCancellationRequested) break;

                // ── Paused ────────────────────────────────────────────
                _pauseRequested = false;
                State = RouletteState.Paused;
                NotifyStateChanged(0);
                _log($"  Paused — Dolphin still running.");

                _resumeTcs = new TaskCompletionSource();
                try { await _resumeTcs.Task.WaitAsync(outerToken); }
                catch (OperationCanceledException) { break; }

                bool resuming = _resumeRequested;
                _resumeRequested = false;

                if (!resuming)
                {
                    State = RouletteState.Running; // woken by Skip/Done, not Resume
                    break;
                }

                // ── Resumed ───────────────────────────────────────────
                playSeconds = _rng.Next(_settings.MinPlaySeconds, _settings.MaxPlaySeconds + 1);
                _log($"  ▶  Resumed '{next.Name}' — new play time: {FormatTime(playSeconds)}");
                State = RouletteState.Running;
                NotifyStateChanged(playSeconds);
            }

            if (!next.Finished)
            {
                _log($"\n  Saving state for '{next.Name}'…");
                DolphinHotkeys.SaveState(_settings.SavestateSlot, _log);
                await Task.Delay(500, CancellationToken.None);
            }
            else
            {
                _log($"  Skipping save — '{next.Name}' is finished.");
            }

            KillDolphin();

            if (outerToken.IsCancellationRequested) break;
        }

        State = RouletteState.Stopped;
        _currentGame = null;
        NotifyStateChanged(0);
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

    private Process? LaunchDolphin(string isoPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _settings.DolphinPath,
                Arguments = $"--batch --exec \"{isoPath}\"",
                UseShellExecute = false,
            };
            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            _log($"  [!] Exception launching Dolphin: {ex.Message}");
            return null;
        }
    }

    private void KillDolphin()
    {
        if (_dolphinProcess == null) return;
        try
        {
            if (!_dolphinProcess.HasExited)
            {
                _dolphinProcess.CloseMainWindow();
                if (!_dolphinProcess.WaitForExit(3000))
                    _dolphinProcess.Kill();
            }
        }
        catch { /* process may already be gone */ }
        _dolphinProcess = null;
    }

    private void NotifyStateChanged(int remainingSeconds) =>
        _onStateChanged(_currentGame, remainingSeconds, State);

    private static string FormatTime(int seconds)
    {
        int m = seconds / 60, s = seconds % 60;
        return m > 0 ? $"{m}m {s}s" : $"{s}s";
    }
}
