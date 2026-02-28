using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace MixOverlays.Services
{
    /// <summary>
    /// Hook clavier bas-niveau (WH_KEYBOARD_LL).
    /// Capture Ctrl+X même quand League of Legends est en plein écran exclusif,
    /// contrairement à RegisterHotKey qui est bloqué par les jeux fullscreen.
    /// </summary>
    public sealed class GlobalHotkeyService : IDisposable
    {
        // ─── Win32 ────────────────────────────────────────────────────────────
        private const int  WH_KEYBOARD_LL = 13;
        private const int  WM_KEYDOWN     = 0x0100;
        private const int  WM_SYSKEYDOWN  = 0x0104;
        private const uint VK_X           = 0x58;
        private const uint VK_CONTROL     = 0x11;
        private const uint VK_LCONTROL    = 0xA2;
        private const uint VK_RCONTROL    = 0xA3;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
                                                       IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
                                                     IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(uint vKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint   vkCode;
            public uint   scanCode;
            public uint   flags;
            public uint   time;
            public IntPtr dwExtraInfo;
        }

        // ─── État ─────────────────────────────────────────────────────────────
        private IntPtr              _hookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc _proc;          // gardé en vie pour éviter le GC
        private bool                _disposed;

        /// <summary>Déclenché sur le thread UI quand Ctrl+X est pressé.</summary>
        public event EventHandler? CtrlXPressed;

        // ─── Constructeur ─────────────────────────────────────────────────────
        public GlobalHotkeyService()
        {
            _proc = HookCallback;
            Install();
        }

        private void Install()
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule  = curProcess.MainModule!;
            _hookHandle = SetWindowsHookEx(
                WH_KEYBOARD_LL,
                _proc,
                GetModuleHandle(curModule.ModuleName!),
                0);

            if (_hookHandle == IntPtr.Zero)
                Debug.WriteLine("[GlobalHotkey] Échec SetWindowsHookEx: " +
                                Marshal.GetLastWin32Error());
            else
                Debug.WriteLine("[GlobalHotkey] Hook bas-niveau installé.");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                if (kb.vkCode == VK_X)
                {
                    // Vérifie que Ctrl est enfoncé (GetAsyncKeyState fonctionne cross-process)
                    bool ctrlDown = (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0
                                 || (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0
                                 || (GetAsyncKeyState(VK_CONTROL)  & 0x8000) != 0;

                    if (ctrlDown)
                    {
                        Debug.WriteLine("[GlobalHotkey] Ctrl+X détecté !");
                        // Remonter sur le thread UI
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                            () => CtrlXPressed?.Invoke(this, EventArgs.Empty));
                    }
                }
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // ─── Dispose ──────────────────────────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                Debug.WriteLine("[GlobalHotkey] Hook retiré.");
            }
        }
    }
}
