using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;

namespace DolphinRoulette;

/// <summary>
/// Sends WM_KEYDOWN/WM_KEYUP messages directly to Dolphin's render window
/// so hotkeys work regardless of which window has focus.
/// </summary>
public static class DolphinHotkeys
{
    // Virtual key codes
    private const int VK_SHIFT = 0x10;
    private const int VK_F1    = 0x70; // F1–F8 are 0x70–0x77

    // WM_KEYDOWN / WM_KEYUP
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP   = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_RESTORE = 9;

    /// <summary>
    /// Finds the main Dolphin window by looking for a process named "Dolphin".
    /// </summary>
    public static IntPtr FindDolphinWindow()
    {
        var procs = Process.GetProcessesByName("Dolphin");
        if (procs.Length == 0) return IntPtr.Zero;
        return procs[0].MainWindowHandle;
    }

    /// <summary>
    /// Saves to the given slot (Shift+F{slot}).
    /// Brings Dolphin to foreground briefly to ensure the hotkey registers.
    /// </summary>
    public static void SaveState(int slot, Action<string> log)
    {
        SendHotkey(slot, save: true, log);
    }

    /// <summary>
    /// Loads from the given slot (F{slot}).
    /// </summary>
    public static void LoadState(int slot, Action<string> log)
    {
        SendHotkey(slot, save: false, log);
    }

    private static void SendHotkey(int slot, bool save, Action<string> log)
    {
        var hwnd = FindDolphinWindow();
        if (hwnd == IntPtr.Zero)
        {
            log("  [!] Could not find Dolphin window to send hotkey.");
            return;
        }

        // Bring Dolphin to foreground so key input is accepted
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
        Thread.Sleep(150); // let it come to front

        int vkF = VK_F1 + (slot - 1); // F1=0x70, F2=0x71, …

        if (save)
        {
            // Shift+F{slot} = Save State to Slot N
            SendKeys.SendWait($"+{{F{slot}}}");
            log($"  Sent Shift+F{slot} (save state slot {slot})");
        }
        else
        {
            // F{slot} = Load State from Slot N
            SendKeys.SendWait($"{{F{slot}}}");
            log($"  Sent F{slot} (load state slot {slot})");
        }
    }
}
