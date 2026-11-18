using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskPause
{
    internal static class Native
    {
        internal delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
        [DllImport("user32", EntryPoint = "MapVirtualKeyW")] internal static extern uint MapVirtualKey(uint key, uint mapType);
        [DllImport("user32", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int kind, Hook callback, IntPtr module, uint thread);
        [DllImport("user32")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr msg, IntPtr data);
        [DllImport("kernel32", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32")] internal static extern IntPtr GetForegroundWindow();
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [DllImport("user32")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint mods, uint key);
        [DllImport("user32")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32")] internal static extern bool LockWorkStation();
        [DllImport("wtsapi32", SetLastError = true)] internal static extern bool WTSRegisterSessionNotification(IntPtr window, int flags);
        [DllImport("wtsapi32")] internal static extern bool WTSUnRegisterSessionNotification(IntPtr window);
        [DllImport("user32")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32")] internal static extern bool SetProcessDPIAware();
    }

    // Called on the UI message-loop thread. Only physical PIN digits go in the short-lived buffer.
    internal sealed class PinEntry
    {
        private readonly Action<byte[]> submit;
        private readonly byte[] digits;
        private readonly bool[] held = new bool[256];
        private int length, lastDigit;
        private int submitOnRelease = -1;
        private int incorrectAttempts;
        internal bool Verifying { get; set; }
        internal bool Confirmed { get; set; }

        internal PinEntry(int pinLength, Action<byte[]> submit)
        {
            if (pinLength < Preferences.MinPinLength || pinLength > Preferences.MaxPinLength)
                throw new ArgumentOutOfRangeException("pinLength");
            digits = new byte[pinLength];
            this.submit = submit;
        }

        internal static bool IsInjectedKey(int flags) { return (flags & 0x10) != 0; }
        internal static bool IsInjectedMouse(int flags) { return (flags & 1) != 0; }

        internal void Key(int key, bool up, int now)
        {
            if (key < 0 || key >= held.Length) return;
            if (up)
            {
                held[key] = false;
                if (key == submitOnRelease)
                {
                    byte[] candidate = (byte[])digits.Clone();
                    ClearDigits();
                    Verifying = true;
                    submit(candidate);
                }
                return;
            }
            if (Verifying || Confirmed) return;
            // A security chord can move subsequent releases to another desktop.
            // Discard an interrupted entry; the chord itself never unlocks the guard.
            if (key == (int)Keys.Escape || key == (int)Keys.Delete || key == (int)Keys.ControlKey || key == (int)Keys.Menu ||
                (key >= (int)Keys.LControlKey && key <= (int)Keys.RMenu))
            {
                Reset();
                return;
            }
            if (held[key]) return; // Ignore auto-repeat, including the last digit.
            if (unchecked((uint)(now - lastDigit)) > 10000) ClearDigits();
            held[key] = true;
            lastDigit = now;
            if (submitOnRelease >= 0) return;
            if (key == (int)Keys.Back)
            {
                if (length > 0) digits[--length] = 0;
                return;
            }
            int digit;
            if (key >= 0x30 && key <= 0x39) digit = key - 0x30;
            else if (key >= 0x60 && key <= 0x69) digit = key - 0x60;
            else return;
            digits[length++] = (byte)(48 + digit);
            if (length == digits.Length) submitOnRelease = key;
        }

        private void ClearDigits() { Array.Clear(digits, 0, digits.Length); length = 0; submitOnRelease = -1; }
        // One PinEntry lives for one arming. Clearing partial digits leaves completed attempts counted.
        internal bool RecordIncorrectPin(bool lockAfterWrongPins)
        {
            if (!lockAfterWrongPins || ++incorrectAttempts < 5) return false;
            incorrectAttempts = 0;
            return true;
        }

        internal void Reset()
        {
            ClearDigits();
            Array.Clear(held, 0, held.Length);
            Confirmed = Verifying = false;
        }
    }

    internal sealed class InputGuard : IDisposable
    {
        private readonly Native.Hook keyboardProc, mouseProc;
        private readonly PinEntry pin;
        // Zero for desktop-wide protection. The compatibility check only guards its own window.
        private readonly IntPtr testWindow;
        private IntPtr keyboard, mouse;
        internal int InjectedKeys, InjectedMouse;

        internal InputGuard(PinEntry pin, IntPtr testWindow)
        {
            this.pin = pin;
            this.testWindow = testWindow;
            keyboardProc = OnKeyboard;
            mouseProc = OnMouse;
        }

        internal void Start()
        {
            IntPtr module = Native.GetModuleHandle(null);
            keyboard = Native.SetWindowsHookEx(13, keyboardProc, module, 0);
            if (keyboard == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            mouse = Native.SetWindowsHookEx(14, mouseProc, module, 0);
            if (mouse == IntPtr.Zero)
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error());
                Dispose();
                throw error;
            }
        }

        private IntPtr OnKeyboard(int code, IntPtr msg, IntPtr data)
        {
            if (code >= 0 && (testWindow == IntPtr.Zero || Native.GetForegroundWindow() == testWindow))
            {
                int flags = Marshal.ReadInt32(data, 8);
                if (!PinEntry.IsInjectedKey(flags))
                {
                    if (pin != null)
                        pin.Key(Marshal.ReadInt32(data), (flags & 0x80) != 0, Environment.TickCount);
                    return new IntPtr(1);
                }
                if (testWindow != IntPtr.Zero) InjectedKeys++;
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, msg, data);
        }

        private bool InTestArea(IntPtr data)
        {
            if (testWindow == IntPtr.Zero) return true;
            var point = new Native.Point { X = Marshal.ReadInt32(data), Y = Marshal.ReadInt32(data, 4) };
            return Native.GetForegroundWindow() == testWindow &&
                Native.GetAncestor(Native.WindowFromPoint(point), 2) == testWindow;
        }

        private IntPtr OnMouse(int code, IntPtr msg, IntPtr data)
        {
            if (code >= 0 && InTestArea(data))
            {
                if (!PinEntry.IsInjectedMouse(Marshal.ReadInt32(data, 12)))
                    return new IntPtr(1);
                if (testWindow != IntPtr.Zero) InjectedMouse++;
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, msg, data);
        }

        public void Dispose()
        {
            if (mouse != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouse); mouse = IntPtr.Zero; }
            if (keyboard != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyboard); keyboard = IntPtr.Zero; }
            if (pin != null) pin.Reset();
        }
    }
}
