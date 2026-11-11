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

        public enum Claimed
        {
            First,     // the first copy for this data folder: it keeps the claim until Release
            Running,   // a copy for this data folder is running
            Unknown,   // Windows couldn't say: this copy runs on its own (never a reason not to start)
        }

        // what this copy is for its data folder; never throws
        public static Claimed Claim(string dataFolder)
        {
            WindowTitle = Name(dataFolder);
            try
            {
                var mutex = new Mutex(true, @"Local\" + WindowTitle, out bool first);
                if (first)
                {
                    held = mutex;
                    return Claimed.First;
                }
                mutex.Dispose();
                return Claimed.Running;
            }
            catch (UnauthorizedAccessException)
            {
                // made by a copy running with more rights than this one (started as administrator): it's there,
                // but this one may not open it
                return Claimed.Running;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return Claimed.Unknown;
            }
        }

        // what a copy started again says when the one running didn't come to the front
        public const string NotBroughtForward = "b0r3d QR is already running, but it didn't come to the front (it may be busy or closing, " +
                                                "or running as administrator). Look for it on the taskbar or in the tray, or close it and start b0r3d QR again.";

        public static void Release()
        {
            if (held == null) return;
            held.ReleaseMutex();
            held.Dispose();
            held = null;
        }

        // Tells the running copy to show itself, letting it come to the front (a program the person just
        // started may hand the front over), and waits `answerMs` for it to answer: false when it didn't
        // (busy, closing, or running as administrator, whose windows don't take messages from this one).
        // It may still be starting, so its window is looked for `waitMs` first.
        public static bool BringForward(int waitMs = 5000, int answerMs = 3000)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                IntPtr window = FindWindowW(null, WindowTitle);
                if (window != IntPtr.Zero)
                {
                    if (GetWindowThreadProcessId(window, out uint process) != 0) AllowSetForegroundWindow(process);
                    const uint SMTO_ABORTIFHUNG = 0x0002;
                    return SendMessageTimeoutW(window, (uint)Tray.ShowMessage, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, (uint)answerMs, out _) != IntPtr.Zero;
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
        static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
    }
}
