using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace B0r3dQR
{
    // The screens, in real pixels (b0r3d QR follows each screen's own scaling, so a 4K screen at
    // 150 % is 3840 pixels across here), and pictures of them. A picture only ever lives in memory.
    static class Screens
    {
        public sealed class Screen
        {
            public Int32Rect Bounds;   // real pixels, on the desktop that spans every screen
            public double Scale;       // 1.5 at 150 %
            public bool Main;
            public int Number;         // 1, 2, 3 from left to right
        }

        public static List<Screen> All()
        {
            var screens = new List<Screen>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, dc, rect, data) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (!GetMonitorInfo(monitor, ref info)) return true;
                uint dpi = 96;
                try
                {
                    if (GetDpiForMonitor(monitor, 0, out uint x, out uint y) == 0) dpi = x;
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                Rect32 r = info.Monitor;
                screens.Add(new Screen { Bounds = new Int32Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), Scale = dpi / 96.0, Main = (info.Flags & 1) != 0 });
                return true;
            }, IntPtr.Zero);
            screens.Sort((a, b) => a.Bounds.X != b.Bounds.X ? a.Bounds.X.CompareTo(b.Bounds.X) : a.Bounds.Y.CompareTo(b.Bounds.Y));
            for (int i = 0; i < screens.Count; i++) screens[i].Number = i + 1;
            return screens;
        }

        // A picture of part of the desktop in real pixels, see-through windows included.
        public static BitmapSource Capture(Int32Rect area)
        {
            if (area.Width <= 0 || area.Height <= 0) throw new ArgumentException("an empty part of the screen");
            IntPtr desktop = GetDC(IntPtr.Zero);
            IntPtr dc = CreateCompatibleDC(desktop);
            IntPtr bitmap = CreateCompatibleBitmap(desktop, area.Width, area.Height);
            try
            {
                IntPtr old = SelectObject(dc, bitmap);
                bool copied = BitBlt(dc, 0, 0, area.Width, area.Height, desktop, area.X, area.Y, SourceCopy | CaptureLayered);
                SelectObject(dc, old);  // a bitmap still chosen into a DC can't be read
                if (!copied) throw new InvalidOperationException("Windows didn't hand over a picture of the screen.");
                BitmapSource picture = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                picture.Freeze();
                return picture;
            }
            finally
            {
                DeleteObject(bitmap);
                DeleteDC(dc);
                ReleaseDC(IntPtr.Zero, desktop);
            }
        }

        // where the mouse pointer is, in real pixels
        public static Point Pointer()
        {
            return GetCursorPos(out Point32 p) ? new Point(p.X, p.Y) : new Point(0, 0);
        }

        // puts a window exactly over an area of the desktop, in real pixels, above other windows
        public static void Cover(Window window, Int32Rect area)
        {
            IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
            SetWindowPos(handle, TopMost, area.X, area.Y, area.Width, area.Height, NoActivate);
        }

        const uint SourceCopy = 0x00CC0020, CaptureLayered = 0x40000000, NoActivate = 0x0010;
        static readonly IntPtr TopMost = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        struct Rect32 { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct Point32 { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MonitorInfo
        {
            public int Size;
            public Rect32 Monitor;
            public Rect32 Work;
            public uint Flags;
        }

        delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out Point32 point);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr dc, IntPtr thing);

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr to, int x, int y, int width, int height, IntPtr from, int fromX, int fromY, uint how);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr thing);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr dc);
    }
}
