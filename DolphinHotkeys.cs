using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;

namespace DolphinRoulette;

public static class DolphinHotkeys
{
    private const int VK_SHIFT  = 0x10;
    private const int VK_F1     = 0x70;
    private const int SW_RESTORE = 9;

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP   = 0x0101;
    private const uint WM_COMMAND = 0x0111;

    private const uint MF_BYPOSITION = 0x0400;

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
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern IntPtr SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr hMenu, int nPos);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(IntPtr hMenu, uint uIDItem, [Out] StringBuilder lpString, int nMaxCount, uint uFlag);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    // ── Public API ────────────────────────────────────────────────────

    public static IntPtr FindDolphinWindow()
    {
        var procs = Process.GetProcessesByName("Dolphin");
        if (procs.Length == 0) return IntPtr.Zero;
        return procs[0].MainWindowHandle;
    }

    public static void SaveState(int slot, Action<string> log) => SendHotkey(slot, save: true,  log);
    public static void LoadState(int slot, Action<string> log) => SendHotkey(slot, save: false, log);

    // ── Implementation ────────────────────────────────────────────────

    private static void SendHotkey(int slot, bool save, Action<string> log)
    {
        var procs = Process.GetProcessesByName("Dolphin");
        if (procs.Length == 0) { log("  [!] Could not find Dolphin process."); return; }

        var mainHwnd = procs[0].MainWindowHandle;
        if (mainHwnd == IntPtr.Zero) { log("  [!] Dolphin has no main window."); return; }

        // Bring Dolphin to foreground. SetForegroundWindow from a background
        // thread is blocked by the OS foreground lock, so attach our input
        // thread to Dolphin's — that lets the call succeed — and stay attached
        // through the SendInput send so focus can't slip away mid-hotkey.
        uint callerThread = GetCurrentThreadId();
        uint dolphinThread = GetWindowThreadProcessId(mainHwnd, out _);
        bool attached = callerThread != dolphinThread &&
                        AttachThreadInput(callerThread, dolphinThread, true);

        ShowWindow(mainHwnd, SW_RESTORE);
        BringWindowToTop(mainHwnd);
        SetForegroundWindow(mainHwnd);
        Thread.Sleep(200);

        // Verify Dolphin actually owns the foreground — if not, the hotkey
        // would land in the wrong window and Dolphin (focus-gated by default)
        // would ignore it. This is the usual cause of "inputs not recognized".
        IntPtr fg = GetForegroundWindow();
        bool foreground = fg == mainHwnd;
        if (!foreground)
            log("  [!] Dolphin is NOT foreground — hotkey may be dropped. " +
                "Enable Dolphin: Config ▸ Interface ▸ 'Keep window on top', or " +
                "Controllers ▸ Hotkeys ▸ Background Input.");

        // ── Approach 1: WM_COMMAND via menu item ID ───────────────────
        // Bypasses keyboard input entirely — triggers the QAction directly.
        uint cmdId = FindStateMenuCommandId(mainHwnd, slot, save, log);
        if (cmdId != 0)
        {
            PostMessage(mainHwnd, WM_COMMAND, (IntPtr)cmdId, IntPtr.Zero);
            log($"  {(save ? "Save" : "Load")} state slot {slot} via WM_COMMAND id={cmdId}");
            if (attached) AttachThreadInput(callerThread, dolphinThread, false);
            return;
        }

        // ── Approach 2: SendInput with scancodes + hold time ──────────
        // Dolphin's Win32 input polls key state; key must be held >=16ms and
        // many builds key off the scancode, not the virtual-key.
        log($"  [!] No menu item found — falling back to SendInput hold");
        int vkF = VK_F1 + (slot - 1);
        if (save)
        {
            // Shift must be polled as already-held before F-key arrives,
            // else Dolphin reads a bare F-key = Load State. Stagger them.
            SendInput(new[] { Key(VK_SHIFT, true) });
            Thread.Sleep(60);
            SendInput(new[] { Key(vkF, true) });
            Thread.Sleep(200);
            SendInput(new[] { Key(vkF, false) });   // release F first
            Thread.Sleep(60);
            SendInput(new[] { Key(VK_SHIFT, false) }); // then release Shift
            log($"  Sent Shift+F{slot} via SendInput hold");
        }
        else
        {
            SendInput(new[] { Key(vkF, true) });
            Thread.Sleep(200);
            SendInput(new[] { Key(vkF, false) });
            log($"  Sent F{slot} via SendInput hold");
        }

        if (attached) AttachThreadInput(callerThread, dolphinThread, false);
    }

    // ── Menu traversal ────────────────────────────────────────────────

    private static uint FindStateMenuCommandId(IntPtr hwnd, int slot, bool save, Action<string> log)
    {
        IntPtr menuBar = GetMenu(hwnd);
        if (menuBar == IntPtr.Zero) return 0;

        // Walk top-level menus looking for "Emulation"
        int topCount = GetMenuItemCount(menuBar);
        for (int i = 0; i < topCount; i++)
        {
            var sb = new StringBuilder(256);
            GetMenuString(menuBar, (uint)i, sb, 256, MF_BYPOSITION);
            string topName = CleanMenuText(sb.ToString());
            if (!topName.Contains("emulation", StringComparison.OrdinalIgnoreCase)) continue;

            IntPtr emulMenu = GetSubMenu(menuBar, i);
            if (emulMenu == IntPtr.Zero) continue;

            // Walk Emulation items for Save State / Load State
            string stateKind = save ? "save" : "load";
            int emulCount = GetMenuItemCount(emulMenu);
            for (int j = 0; j < emulCount; j++)
            {
                var sb2 = new StringBuilder(256);
                GetMenuString(emulMenu, (uint)j, sb2, 256, MF_BYPOSITION);
                string emulName = CleanMenuText(sb2.ToString());
                if (!emulName.Contains(stateKind, StringComparison.OrdinalIgnoreCase) ||
                    !emulName.Contains("state", StringComparison.OrdinalIgnoreCase)) continue;

                // Found "Save State" or "Load State" — may be a submenu or direct item
                IntPtr stateSub = GetSubMenu(emulMenu, j);
                if (stateSub != IntPtr.Zero)
                {
                    uint id = FindSlotInMenu(stateSub, slot);
                    if (id != 0) return id;
                }
                else
                {
                    // Direct item — check if it matches the slot shortcut (F1..F8 / Shift+F1..F8)
                    string shortcut = save ? $"Shift+F{slot}" : $"F{slot}";
                    if (sb2.ToString().Contains(shortcut, StringComparison.OrdinalIgnoreCase))
                        return GetMenuItemID(emulMenu, j);
                }
            }
        }
        return 0;
    }

    private static uint FindSlotInMenu(IntPtr menu, int slot)
    {
        int count = GetMenuItemCount(menu);
        for (int i = 0; i < count; i++)
        {
            var sb = new StringBuilder(256);
            GetMenuString(menu, (uint)i, sb, 256, MF_BYPOSITION);
            string text = sb.ToString();
            // Match either "Slot N" or the shortcut "FN" / "Shift+FN"
            bool matchesSlot     = text.Contains($"Slot {slot}", StringComparison.OrdinalIgnoreCase);
            bool matchesShortcut = text.Contains($"F{slot}", StringComparison.OrdinalIgnoreCase);
            if (matchesSlot || matchesShortcut)
                return GetMenuItemID(menu, i);
        }
        return 0;
    }

    private static string CleanMenuText(string raw) =>
        raw.Split('\t')[0].Replace("&", "").Trim();

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
