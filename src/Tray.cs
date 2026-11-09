using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Resources;

namespace B0r3dQR
{
    // Scan from anywhere while b0r3d QR is running: a cup icon in the taskbar corner and a global
    // hotkey (Ctrl+Shift+Q) that start Scan the screen without opening the main window first.
    // Nothing starts with Windows, and nothing is written outside the data folder.
    sealed class Tray : IDisposable
    {
        public const string HotkeyText = "Ctrl+Shift+Q";

        const uint NIM_ADD = 0x00, NIM_MODIFY = 0x01, NIM_DELETE = 0x02;
        const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04;
        const int TrayMessage = 0x8000 + 17;
        const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B, WM_HOTKEY = 0x0312;
        const int HotkeyId = 1;
        const uint MOD_CONTROL = 0x02, MOD_SHIFT = 0x04, MOD_NOREPEAT = 0x4000;
        const uint VK_Q = 0x51;

        readonly MainWindow window;
        public readonly ContextMenu Menu;
        public readonly MenuItem ScanItem, CameraItem, PasteItem, ShowItem, ExitItem;
        HwndSource source;
        IntPtr iconHandle;
        bool iconAdded, hotkeyRegistered;

        public bool Active { get; private set; }
        public bool IconShown => iconAdded;
        public bool HotkeyRegistered => hotkeyRegistered;

        public Tray(MainWindow window)
        {
            this.window = window;
            ScanItem = new MenuItem { Header = "Scan the _screen", InputGestureText = HotkeyText };
            ScanItem.Click += (s, e) => TriggerScan();
            CameraItem = new MenuItem { Header = "Scan _with the camera" };
            CameraItem.Click += (s, e) => TriggerCamera();
            PasteItem = new MenuItem { Header = "_Paste picture" };
            PasteItem.Click += (s, e) => TriggerPaste();
            ShowItem = new MenuItem { Header = "Show _b0r3d QR", FontWeight = FontWeights.SemiBold };
            ShowItem.Click += (s, e) => TriggerShow();
            ExitItem = new MenuItem { Header = "E_xit" };
            ExitItem.Click += (s, e) => TriggerExit();

            Menu = new ContextMenu();
            Menu.Items.Add(ScanItem);
            Menu.Items.Add(CameraItem);
            Menu.Items.Add(PasteItem);
            Menu.Items.Add(new Separator());
            Menu.Items.Add(ShowItem);
            Menu.Items.Add(ExitItem);
        }

        // turns the tray icon and Ctrl+Shift+Q on or off (`installOs`: true once the window is on screen)
        public void SetActive(bool on, bool installOs)
        {
            Active = on;
            if (!on)
            {
                RemoveOs();
                return;
            }
            if (!installOs) return;
            EnsureSource();
            if (iconHandle == IntPtr.Zero) iconHandle = MakeIcon(32);
            if (!hotkeyRegistered && source != null)
                hotkeyRegistered = RegisterHotKey(source.Handle, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_Q);
            if (source != null && iconHandle != IntPtr.Zero)
            {
                var data = new NotifyIconData
                {
                    Size = (uint)Marshal.SizeOf(typeof(NotifyIconData)),
                    Hwnd = source.Handle,
                    Id = 1,
                    Flags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                    CallbackMessage = TrayMessage,
                    Icon = iconHandle,
                    Tip = hotkeyRegistered ? "b0r3d QR · " + HotkeyText + " to scan the screen" : "b0r3d QR · click to scan the screen",
                };
                if (!iconAdded) iconAdded = Shell_NotifyIconW(NIM_ADD, ref data);
                else Shell_NotifyIconW(NIM_MODIFY, ref data);
            }
        }

        // registers only the hotkey on a message-only window, for testing RegisterHotKey without adding a tray icon
        internal bool TryRegisterHotkeyOnly()
        {
            EnsureSource();
            if (source == null) return false;
            if (!hotkeyRegistered)
                hotkeyRegistered = RegisterHotKey(source.Handle, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_Q);
            return hotkeyRegistered;
        }

        void EnsureSource()
        {
            if (source != null) return;
            var parameters = new HwndSourceParameters("b0r3dQR tray")
            {
                Width = 0,
                Height = 0,
                ParentWindow = new IntPtr(-3),   // HWND_MESSAGE: a message-only window, never shown
            };
            source = new HwndSource(parameters);
            source.AddHook(WndProc);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                TriggerScan();
            }
            else if (msg == TrayMessage)
            {
                int mouse = lParam.ToInt32() & 0xFFFF;
                if (mouse == WM_LBUTTONUP)
                {
                    handled = true;
                    TriggerScan();
                }
                else if (mouse == WM_RBUTTONUP || mouse == WM_CONTEXTMENU)
                {
                    handled = true;
                    Menu.IsOpen = true;
                }
            }
            return IntPtr.Zero;
        }

        public void TriggerScan() => window.ScanScreenFromAnywhere();

        public void TriggerCamera()
        {
            window.ShowFromTray();
            if (Commands.ScanCamera.CanExecute(null, window)) Commands.ScanCamera.Execute(null, window);
        }

        public void TriggerPaste()
        {
            window.ShowFromTray();
            if (Commands.PastePicture.CanExecute(null, window)) Commands.PastePicture.Execute(null, window);
        }

        public void TriggerShow() => window.ShowFromTray();

        public void TriggerExit() => window.Close();

        void RemoveOs()
        {
            Menu.IsOpen = false;
            if (hotkeyRegistered && source != null)
            {
                UnregisterHotKey(source.Handle, HotkeyId);
                hotkeyRegistered = false;
            }
            if (iconAdded && source != null)
            {
                var data = new NotifyIconData
                {
                    Size = (uint)Marshal.SizeOf(typeof(NotifyIconData)),
                    Hwnd = source.Handle,
                    Id = 1,
                };
                Shell_NotifyIconW(NIM_DELETE, ref data);
                iconAdded = false;
            }
        }

        public void Dispose()
        {
            Active = false;
            RemoveOs();
            if (source != null)
            {
                source.RemoveHook(WndProc);
                source.Dispose();
                source = null;
            }
            if (iconHandle != IntPtr.Zero)
            {
                DestroyIcon(iconHandle);
                iconHandle = IntPtr.Zero;
            }
        }

        // the cup icon from the exe's resources, as a Windows HICON at the size asked for
        internal static IntPtr MakeIcon(int wantedSize = 32)
        {
            StreamResourceInfo info = Application.GetResourceStream(new Uri("/b0r3dQR;component/assets/b0r3dQR.ico", UriKind.Relative));
            if (info == null) return IntPtr.Zero;
            byte[] ico;
            using (Stream stream = info.Stream)
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                ico = copy.ToArray();
            }
            if (ico.Length < 6) return IntPtr.Zero;
            int count = BitConverter.ToUInt16(ico, 4);
            int bestOffset = -1, bestLength = 0, bestDiff = int.MaxValue;
            for (int i = 0; i < count && 6 + 16 * (i + 1) <= ico.Length; i++)
            {
                int at = 6 + 16 * i;
                int size = ico[at] == 0 ? 256 : ico[at];
                int length = BitConverter.ToInt32(ico, at + 8);
                int offset = BitConverter.ToInt32(ico, at + 12);
                if (offset < 0 || length <= 0 || offset + length > ico.Length) continue;
                int diff = Math.Abs(size - wantedSize);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestOffset = offset;
                    bestLength = length;
                }
            }
            if (bestOffset < 0) return IntPtr.Zero;
            var frame = new byte[bestLength];
            Buffer.BlockCopy(ico, bestOffset, frame, 0, bestLength);
            return CreateIconFromResourceEx(frame, (uint)frame.Length, true, 0x00030000, wantedSize, wantedSize, 0);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NotifyIconData
        {
            public uint Size;
            public IntPtr Hwnd;
            public uint Id;
            public uint Flags;
            public uint CallbackMessage;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Tip;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        [DllImport("user32.dll")]
        static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr icon);
    }
}
