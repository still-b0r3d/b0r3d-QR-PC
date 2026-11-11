using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace B0r3dQR
{
    // One b0r3d QR for each data folder. Started again while it runs (from the same folder, or with the
    // same --data), b0r3d QR brings the one running forward and ends, instead of a second copy with a
    // second tray icon, Ctrl+Shift+Q taken twice and both writing b0r3dQR.ini. Copies with data folders
    // of their own (two portable copies in their own folders) still run side by side.
    static class SingleCopy
    {
        static Mutex held;

        // the title of the running copy's hidden window (Tray), by which a copy started again finds it
        public static string WindowTitle { get; private set; } = "b0r3dQR";

        // "b0r3dQR 1A2B3C4D5E6F7081": the name the claim and the hidden window go by, the same for a
        // data folder however its path is written (capitals, a slash at the end)
        internal static string Name(string dataFolder)
        {
            string full;
            try
            {
                full = Path.GetFullPath(dataFolder).TrimEnd('\\');
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException || e is System.Security.SecurityException)
            {
                full = dataFolder;
            }
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(full.ToUpperInvariant()));
                var name = new StringBuilder("b0r3dQR ");
                for (int i = 0; i < 8; i++) name.Append(hash[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                return name.ToString();
            }
        }

        // true for the first copy for this data folder, which keeps the claim until Release
        public static bool Claim(string dataFolder)
        {
            WindowTitle = Name(dataFolder);
            var mutex = new Mutex(true, @"Local\" + WindowTitle, out bool first);
            if (first) held = mutex;
            else mutex.Dispose();
            return first;
        }

        public static void Release()
        {
            if (held == null) return;
            held.ReleaseMutex();
            held.Dispose();
            held = null;
        }

        // Tells the running copy to show itself, letting it come to the front (a program the person just
        // started may hand the front over). It may still be starting, so its window is looked for a while.
        public static bool BringForward(int waitMs = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                IntPtr window = FindWindowW(null, WindowTitle);
                if (window != IntPtr.Zero)
                {
                    if (GetWindowThreadProcessId(window, out uint process) != 0) AllowSetForegroundWindow(process);
                    return PostMessageW(window, (uint)Tray.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                }
                if (watch.ElapsedMilliseconds >= waitMs) return false;
                Thread.Sleep(100);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowW(string className, string windowName);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllowSetForegroundWindow(uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    }
}
