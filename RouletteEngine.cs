using System.Diagnostics;

namespace DolphinRoulette;

public enum RouletteState { Stopped, Running, Paused }

public class RouletteEngine
{
    private readonly AppSettings _settings;
    private readonly Random _rng = new();
    private readonly Action<string> _log;
    private readonly Action<GameEntry?, int, RouletteState> _onStateChanged;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private Process? _dolphinProcess;
    private string? _lastGameName;
    private GameEntry? _currentGame;

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
        if (State == RouletteState.Running) return;
        _cts = new CancellationTokenSource();
        State = RouletteState.Running;
        _runTask = Task.Run(() => RunLoop(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (State == RouletteState.Stopped) return;
        _cts?.Cancel();
        if (_runTask != null) await _runTask;
        State = RouletteState.Stopped;
        NotifyStateChanged(0);
    }

    /// <summary>Forces an immediate switch to the next game (saves first).</summary>
    public void ForceSkip() => _cts?.Cancel(); // RunLoop catches this and restarts

    /// <summary>Marks the current game finished and removes it from future rotation.</summary>
    public void MarkCurrentDone()
    {
        if (_currentGame == null) return;
        _currentGame.Finished = true;
        _settings.Save();
        _log($"  ✓ '{_currentGame.Name}' marked as finished and removed from rotation.");
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

            // Pick next game with inverse-frequency weighting, never repeating last
            var next = PickNext(available);
            _currentGame = next;
            _lastGameName = next.Name;

            int playSeconds = _rng.Next(_settings.MinPlaySeconds, _settings.MaxPlaySeconds + 1);
            _log($"\n{'═',50}".Replace(" ", "═"));
            _log($"  🎲  Selected : {next.Name}");
            _log($"  ⏱   Play time: {FormatTime(playSeconds)}");

            NotifyStateChanged(playSeconds);

            // Launch Dolphin
            _log($"  Launching Dolphin with: {next.Path}");
            _dolphinProcess = LaunchDolphin(next.Path);

            if (_dolphinProcess == null)
            {
                _log("  [!] Failed to launch Dolphin. Check the path in Settings.");
                State = RouletteState.Stopped;
                NotifyStateChanged(0);
                return;
            }

            // Wait for Dolphin to start, then load savestate
            await Task.Delay((_settings.GracePeriodSeconds + 1) * 1000, CancellationToken.None);
            _log($"  Loading savestate slot {_settings.SavestateSlot}…");
            DolphinHotkeys.LoadState(_settings.SavestateSlot, _log);

            // Update play count
            next.PlayCount++;
            _settings.Save();

            // Inner loop: use a local CTS so ForceSkip/MarkDone can cancel just this turn
            using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
            _cts = innerCts; // re-point so ForceSkip cancels the inner one

            try
            {
                await Task.Delay(playSeconds * 1000, innerCts.Token);
            }
            catch (TaskCanceledException) { /* expected on skip/done/stop */ }

            // Save state (unless game was marked done)
            if (!next.Finished)
            {
                _log($"\n  ⏳  Saving state for '{next.Name}'…");
                DolphinHotkeys.SaveState(_settings.SavestateSlot, _log);
                await Task.Delay(_settings.GracePeriodSeconds * 1000, CancellationToken.None);
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
