using System.Runtime.InteropServices;
using System.Diagnostics;

namespace DolphinRoulette;

public static class DolphinHotkeys
{
    private const int VK_SHIFT  = 0x10;
    private const int VK_F1      = 0x70;
    private const int SW_RESTORE  = 9;
    private const int SW_MINIMIZE = 6;

    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER   = 0x0004;

    private const uint INPUT_KEYBOARD     = 1;
    private const uint KEYEVENTF_KEYUP     = 0x0002;
    private const uint KEYEVENTF_SCANCODE  = 0x0008;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint MAPVK_VK_TO_VSC     = 0;

    // ── Structs for SendInput ─────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT    mi;
        [FieldOffset(0)] public KEYBDINPUT    ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    // ── P/Invoke ──────────────────────────────────────────────────────

    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int cbSize);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern IntPtr SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>First visible top-level window owned by the given process id.
    /// Catches a freshly-spawned window before Process.MainWindowHandle reports.</summary>
    private static IntPtr FindWindowForPid(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint wpid);
            if (wpid == pid && IsWindowVisible(h)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    // ── Public API ────────────────────────────────────────────────────

    public static void SaveState(Process proc, int slot, Action<string> log) => SendHotkey(proc, slot, save: true,  log);
    public static void LoadState(Process proc, int slot, Action<string> log) => SendHotkey(proc, slot, save: false, log);

    /// <summary>Brings a specific Dolphin instance to the foreground (used to
    /// re-assert focus after pre-booting a background instance).</summary>
    public static void BringToForeground(Process proc)
    {
        IntPtr hwnd = MainWindow(proc);
        if (hwnd == IntPtr.Zero) return;

        uint callerThread = GetCurrentThreadId();
        uint dolphinThread = GetWindowThreadProcessId(hwnd, out _);
        bool attached = callerThread != dolphinThread &&
                        AttachThreadInput(callerThread, dolphinThread, true);
        ShowWindow(hwnd, SW_RESTORE);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        if (attached) AttachThreadInput(callerThread, dolphinThread, false);
    }

    /// <summary>Gives the foreground to <paramref name="hwnd"/> by attaching to
    /// the thread that *currently* owns the foreground (not hwnd's thread). This
    /// beats the OS foreground lock without ever attaching to — and thus jamming
    /// — hwnd's own UI thread, so it's safe to call against the active game.</summary>
    private static void StealForegroundTo(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        IntPtr fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, out _);
        uint caller = GetCurrentThreadId();
        bool attached = fgThread != caller && AttachThreadInput(caller, fgThread, true);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        if (attached) AttachThreadInput(caller, fgThread, false);
    }

    /// <summary>Restores an instance's window (it boots minimized) and moves it
    /// to the top-left corner where the actively-played game lives. Called when a
    /// game becomes the current one.</summary>
    public static void MoveTopLeft(Process proc)
    {
        IntPtr hwnd = MainWindow(proc);
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
    }

    /// <summary>Keeps a pre-booted buffer instance minimized while it boots, so it
    /// never paints on-screen (no white flash). If Dolphin restores/raises its
    /// window during boot, it's pushed back down; minimizing it hands focus back
    /// to the active game on its own, so we never force-foreground the active
    /// instance (doing that in a tight loop jams its UI thread and freezes
    /// emulation). Runs on a background thread for <paramref name="durationMs"/>
    /// or until cancelled; the swap then restores the buffer via
    /// <see cref="MoveTopLeft"/>. <paramref name="active"/> is currently unused
    /// but kept for symmetry / future use.</summary>
    public static void KeepMinimized(Process bg, Process active, CancellationToken token)
    {
        Task.Run(() =>
        {
            uint bgPid = (uint)bg.Id;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (bg.HasExited) return;

                    // Push any restored/visible buffer window back to minimized.
                    IntPtr h = FindWindowForPid(bgPid);
                    if (h != IntPtr.Zero && !IsIconic(h))
                        ShowWindow(h, SW_MINIMIZE);

                    // Only if the buffer actually holds the foreground, hand it
                    // back to the active game (else it sits unfocused → paused).
                    // Edge-triggered + attaches to the buffer's thread, so the
                    // active game's UI thread is never jammed.
                    IntPtr fg = GetForegroundWindow();
                    GetWindowThreadProcessId(fg, out uint fgPid);
                    if (fgPid == bgPid)
                        StealForegroundTo(MainWindow(active));
                }
                catch { /* process may be mid-teardown */ }
                Thread.Sleep(60);
            }
        }, token);
    }

    /// <summary>Returns the instance's main window handle, refreshing the
    /// process so a freshly-launched instance reports its window once shown.</summary>
    private static IntPtr MainWindow(Process proc)
    {
        try { proc.Refresh(); return proc.HasExited ? IntPtr.Zero : proc.MainWindowHandle; }
        catch { return IntPtr.Zero; }
    }

    // ── Implementation ────────────────────────────────────────────────

    private static void SendHotkey(Process proc, int slot, bool save, Action<string> log)
    {
        var mainHwnd = MainWindow(proc);
        if (mainHwnd == IntPtr.Zero) { log("  [!] Dolphin instance has no main window."); return; }

        // Save = Shift+F{slot}, Load = F{slot}. Dolphin's Win32 input polls key
        // state (held >=16ms, keyed off scancode), so a held SendInput chord is
        // what registers — the modifier must be down before the F-key arrives.
        int vkF = VK_F1 + (slot - 1);
        SendChordToWindow(mainHwnd, save ? VK_SHIFT : 0, vkF);
        if (GetForegroundWindow() != mainHwnd)
            log("  [!] Dolphin is NOT foreground — hotkey may have been dropped.");
        log($"  Sent {(save ? $"Shift+F{slot}" : $"F{slot}")}");
    }

    /// <summary>Foregrounds a window (beating the foreground lock via the
    /// AttachThreadInput trick) and sends a held modifier+key chord to it.
    /// modifierVk = 0 for no modifier.</summary>
    private static void SendChordToWindow(IntPtr mainHwnd, int modifierVk, int vk)
    {
        uint callerThread = GetCurrentThreadId();
        uint dolphinThread = GetWindowThreadProcessId(mainHwnd, out _);
        bool attached = callerThread != dolphinThread &&
                        AttachThreadInput(callerThread, dolphinThread, true);

        ShowWindow(mainHwnd, SW_RESTORE);
        BringWindowToTop(mainHwnd);
        SetForegroundWindow(mainHwnd);
        Thread.Sleep(200);

        if (modifierVk != 0) { SendInput(new[] { Key(modifierVk, true) }); Thread.Sleep(60); }
        SendInput(new[] { Key(vk, true) });
        Thread.Sleep(200);
        SendInput(new[] { Key(vk, false) }); // release key first
        if (modifierVk != 0) { Thread.Sleep(60); SendInput(new[] { Key(modifierVk, false) }); }

        if (attached) AttachThreadInput(callerThread, dolphinThread, false);
    }

    // ── SendInput helpers ─────────────────────────────────────────────

    private static void SendInput(INPUT[] inputs) =>
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    private static INPUT Key(int vk, bool down)
    {
        ushort scan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
        uint flags = KEYEVENTF_SCANCODE | (down ? 0u : KEYEVENTF_KEYUP);
        return new()
        {
            type = INPUT_KEYBOARD,
            u    = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = scan, dwFlags = flags } }
        };
    }
}
