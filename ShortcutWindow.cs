using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PromptTray
{
    internal sealed class ShortcutWindow : NativeWindow, IDisposable
    {
        public const string ShortcutText = "Ctrl+Alt+P";
        private const string WindowName = "PromptTray.ShortcutWindow";
        private const int OpenMessage = 0x8001;
        private const int HotkeyMessage = 0x0312;
        private const int ClipboardMessage = 0x031D;
        private const int HotkeyId = 1;
        private const uint CtrlAltNoRepeat = 0x4003;
        private readonly Action open;
        private readonly Action clipboardChanged;
        private bool listening;

        public bool Registered { get; private set; }

        public ShortcutWindow(Action open, Action clipboardChanged)
        {
            this.open = open;
            this.clipboardChanged = clipboardChanged;
            CreateHandle(new CreateParams { Caption = WindowName });
            Registered = RegisterHotKey(Handle, HotkeyId, CtrlAltNoRepeat, (uint)Keys.P);
        }

        public bool SetClipboardListening(bool enabled)
        {
            if (enabled == listening)
                return true;
            if (enabled && !AddClipboardFormatListener(Handle))
                return false;
            if (!enabled)
                RemoveClipboardFormatListener(Handle);
            listening = enabled;
            return true;
        }

        public static bool OpenExisting()
        {
            IntPtr window = FindWindow(null, WindowName);
            if (window == IntPtr.Zero)
                return false;
            uint process;
            GetWindowThreadProcessId(window, out process);
            if (process != 0)
                AllowSetForegroundWindow(process);
            return PostMessage(window, OpenMessage, IntPtr.Zero, IntPtr.Zero);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == ClipboardMessage)
            {
                if (listening)
                    clipboardChanged();
                return;
            }
            if (message.Msg == OpenMessage ||
                (message.Msg == HotkeyMessage && message.WParam.ToInt32() == HotkeyId))
            {
                open();
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            SetClipboardListening(false);
            if (Registered)
                UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }

        [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint process);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
