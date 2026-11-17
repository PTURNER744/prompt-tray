using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PromptTray
{
    internal sealed class Foreground : IDisposable
    {
        internal struct Target
        {
            public IntPtr Window;
            public uint Process;
        }

        private readonly uint ownProcess;
        private readonly WinEvent callback;
        private readonly IntPtr hook;
        private Target previous;

        public Foreground()
        {
            using (var process = Process.GetCurrentProcess())
                ownProcess = (uint)process.Id;
            callback = ForegroundChanged;
            hook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0);
            Remember(GetForegroundWindow());
        }

        public Target Capture()
        {
            Remember(GetForegroundWindow());
            return previous;
        }

        private void ForegroundChanged(IntPtr handle, uint type, IntPtr window,
            int objectId, int childId, uint thread, uint time)
        {
            Remember(window);
        }

        private void Remember(IntPtr window)
        {
            uint process;
            if (window == IntPtr.Zero || window == GetShellWindow() ||
                GetWindowThreadProcessId(window, out process) == 0 || process == ownProcess)
                return;

            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            string windowClass = name.ToString();
            if (windowClass == "Shell_TrayWnd" || windowClass == "Shell_SecondaryTrayWnd" ||
                windowClass == "NotifyIconOverflowWindow" || windowClass == "TopLevelWindowForOverflowXamlIsland" ||
                windowClass == "WorkerW")
                return;

            previous = new Target { Window = window, Process = process };
        }

        public static bool Activate(Target target)
        {
            uint process;
            return target.Window != IntPtr.Zero &&
                GetWindowThreadProcessId(target.Window, out process) != 0 &&
                process == target.Process && !IsIconic(target.Window) &&
                (GetForegroundWindow() == target.Window || SetForegroundWindow(target.Window));
        }

        public static bool IsActive(Target target)
        {
            return GetForegroundWindow() == target.Window;
        }

        public static bool ModifiersDown()
        {
            foreach (int key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x56 })
                if ((GetAsyncKeyState(key) & 0x8000) != 0)
                    return true;
            return false;
        }

        public static bool Paste(Target target, uint clipboardVersion)
        {
            if (!IsActive(target) || GetClipboardSequenceNumber() != clipboardVersion)
                return false;

            var inputs = new[] { Key(0x11, 0), Key(0x56, 0), Key(0x56, 2), Key(0x11, 2) };
            int size = Marshal.SizeOf(typeof(Input));
            uint sent = SendInput((uint)inputs.Length, inputs, size);
            if (sent > 0 && sent < inputs.Length)
                SendInput(2, new[] { Key(0x56, 2), Key(0x11, 2) }, size);
            return sent == inputs.Length;
        }

        private static Input Key(ushort code, uint flags)
        {
            return new Input
            {
                Type = 1,
                Data = new InputData { Keyboard = new KeyboardInput { Key = code, Flags = flags } }
            };
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero)
                UnhookWinEvent(hook);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input { public uint Type; public InputData Data; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputData
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort Key, Scan;
            public uint Flags, Time;
            public UIntPtr Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int X, Y;
            public uint Data, Flags, Time;
            public UIntPtr Extra;
        }

        private delegate void WinEvent(IntPtr hook, uint type, IntPtr window, int objectId,
            int childId, uint thread, uint time);

        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max,
            IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    }
}
