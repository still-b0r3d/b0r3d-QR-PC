using System;
using System.Globalization;
using System.IO;
using System.Text;
using B0r3dQR.Qr;

namespace B0r3dQR
{
    // View → Look: like Windows' apps (the default), or always light or dark.
    enum Look { Windows, Light, Dark }

    // The settings, kept as key=value lines in b0r3dQR.ini in the data folder (a name of its own,
    // so a --data folder's other files are never touched). What you type isn't kept, just how the
    // program looks and the code's options (the website doesn't keep the text either).
    sealed class Settings
    {
        public const string FileName = "b0r3dQR.ini";
        public const int MaxQuietZone = 20, MinPictureSize = 100;
        public const double MinWindow = 600, MaxWindow = 20000;

        public Look Look = Look.Windows;
        public QrLevel Level = QrLevel.Q;   // error correction
        public int QuietZone = 4;           // modules of white around the code
        public int PictureSize = 1000;      // pixels across asked for; it saves at whole pixels per module
        public double WindowWidth = 1000, WindowHeight = 660;   // device-independent pixels
        public bool Maximized;

        public static Settings Load() => Load(System.IO.Path.Combine(DataFolder.Path, FileName));

        // missing or garbled lines keep their defaults
        internal static Settings Load(string path)
        {
            var s = new Settings();
            string[] lines;
            try
            {
                if (!File.Exists(path)) return s;
                lines = File.ReadAllLines(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                return s;
            }
            foreach (string line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();
                bool named = value.Length > 0 && char.IsLetter(value[0]);  // "dark" or "H", never a number
                switch (key)
                {
                    case "look":
                        if (named && Enum.TryParse(value, true, out Look look) && Enum.IsDefined(typeof(Look), look)) s.Look = look;
                        break;
                    case "level":
                        if (named && Enum.TryParse(value, true, out QrLevel level) && Enum.IsDefined(typeof(QrLevel), level)) s.Level = level;
                        break;
                    case "quiet_zone":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quiet)) s.QuietZone = Clamp(quiet, 0, MaxQuietZone);
                        break;
                    case "picture_size":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size)) s.PictureSize = Clamp(size, MinPictureSize, QrPicture.MaxPixels);
                        break;
                    case "window":
                        ReadWindow(s, value);
                        break;
                }
            }
            return s;
        }

        // "1000x660", or "1000x660 maximized"
        static void ReadWindow(Settings s, string value)
        {
            string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            string[] wh = parts[0].Split('x');
            if (wh.Length != 2) return;
            if (!double.TryParse(wh[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double w) ||
                !double.TryParse(wh[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double h) ||
                double.IsNaN(w) || double.IsNaN(h)) return;
            s.WindowWidth = Math.Max(MinWindow, Math.Min(MaxWindow, w));
            s.WindowHeight = Math.Max(MinWindow * 0.75, Math.Min(MaxWindow, h));
            s.Maximized = parts.Length > 1 && parts[1].Equals("maximized", StringComparison.OrdinalIgnoreCase);
        }

        public string ToText()
        {
            var text = new StringBuilder();
            text.Append("look=").Append(Look.ToString().ToLowerInvariant()).Append("\r\n");
            text.Append("level=").Append(Level).Append("\r\n");
            text.Append("quiet_zone=").Append(QuietZone.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("picture_size=").Append(PictureSize.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("window=").Append(Math.Round(WindowWidth).ToString(CultureInfo.InvariantCulture)).Append('x')
                .Append(Math.Round(WindowHeight).ToString(CultureInfo.InvariantCulture)).Append(Maximized ? " maximized" : "").Append("\r\n");
            return text.ToString();
        }

        // false when settings can't be kept (DataFolder.WhyNotKept says why)
        public bool Save() => DataFolder.TryWrite(FileName, ToText());

        public static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    }
}
