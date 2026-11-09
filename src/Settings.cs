using System;
using System.Globalization;
using System.IO;
using System.Text;
using B0r3dQR.Barcodes;
using B0r3dQR.Design;
using B0r3dQR.Qr;

namespace B0r3dQR
{
    // View → Look: like Windows' apps (the default), or always light or dark.
    enum Look { Windows, Light, Dark }

    // The settings, kept as key=value lines in b0r3dQR.ini in the data folder (a name of its own,
    // so a --data folder's other files are never touched). What you type isn't kept here, just how
    // the program looks and the code's options; the codes you save or copy go to Recent codes, in
    // a file of their own, while it's on (as on the website).
    sealed class Settings
    {
        public const string FileName = "b0r3dQR.ini";
        public const int MaxQuietZone = 20, MinPictureSize = 100;
        public const double MinWindow = 600, MaxWindow = 20000;
        public const double MaxLabelNudge = 10;

        public Look Look = Look.Windows;
        public QrLevel Level = QrLevel.Q;   // error correction (the design keeps these three now; read for an older ini)
        public int QuietZone = 4;           // modules of white around the code
        public int PictureSize = 1000;      // pixels across
        public double WindowWidth = 1000, WindowHeight = 680;   // device-independent pixels
        public bool Maximized;
        public PrintSize Print = new PrintSize();   // "Size for print", kept apart from designs as the website keeps it
        public bool WholePixels;            // saved pictures at whole pixels per module
        public bool FromOlder;              // the ini is from b0r3d QR 0.1 or 0.2 (no whole_pixels line): its level, quiet zone and picture size are the design's
        public bool RecentCodes = true;     // add codes saved, copied or dragged out to Recent codes (on, as on the website)
        public bool RecentNoteShown;        // the note about Recent codes has been shown once

        // the barcode settings and print size, kept apart from QR codes' as the website keeps them
        public BarcodeLook BarcodeLook = new BarcodeLook();
        public PrintSize BarcodePrint = new PrintSize();

        // Print → A label sheet (Avery): the sheet and how far to nudge it (in millimetres)
        public string LabelSheet = "5160";
        public double LabelNudgeX, LabelNudgeY;
        public bool ScanAnywhere = true;    // tray icon and Ctrl+Shift+Q while b0r3d QR is running

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
            bool wholePixelsLine = false;
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
                    case "print":
                        s.Print.On = value.Equals("on", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "print_width":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double width) && width > 0 && width <= 1000) s.Print.Width = width;
                        break;
                    case "print_unit":
                        if (value == "mm" || value == "in") s.Print.Unit = value;
                        break;
                    case "print_dpi":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dpi) && Array.IndexOf(PrintSize.Resolutions, dpi) >= 0) s.Print.Dpi = dpi;
                        break;
                    case "whole_pixels":
                        s.WholePixels = value.Equals("on", StringComparison.OrdinalIgnoreCase);
                        wholePixelsLine = true;
                        break;
                    case "recent_codes":
                        // whatever reads as off is off, since what it keeps is the text typed
                        s.RecentCodes = !Array.Exists(new[] { "off", "false", "no", "0" }, word => value.Equals(word, StringComparison.OrdinalIgnoreCase));
                        break;
                    case "recent_note":
                        s.RecentNoteShown = value.Equals("shown", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "barcode_colour":
                        if (IsColour(value)) s.BarcodeLook.Color = value;
                        break;
                    case "barcode_background":
                        if (IsColour(value)) s.BarcodeLook.Background = value;
                        break;
                    case "barcode_transparent":
                        s.BarcodeLook.Transparent = value.Equals("on", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "barcode_scale":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int scale)) s.BarcodeLook.Scale = Clamp(scale, BarcodeLook.MinScale, BarcodeLook.MaxScale);
                        break;
                    case "barcode_text":
                        s.BarcodeLook.ShowText = !value.Equals("off", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "barcode_rectangular":
                        s.BarcodeLook.AllowRectangular = value.Equals("on", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "barcode_quiet_zones":
                        s.BarcodeLook.QuietZones = !value.Equals("off", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "barcode_bar_height":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bars)) s.BarcodeLook.BarHeight = Clamp(bars, Barcode.MinBarHeight, Barcode.MaxBarHeight);
                        break;
                    case "barcode_print":
                        s.BarcodePrint.On = value.Equals("on", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "barcode_print_width":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double barcodeWidth) && barcodeWidth > 0 && barcodeWidth <= 1000) s.BarcodePrint.Width = barcodeWidth;
                        break;
                    case "barcode_print_unit":
                        if (value == "mm" || value == "in") s.BarcodePrint.Unit = value;
                        break;
                    case "barcode_print_dpi":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int barcodeDpi) && Array.IndexOf(PrintSize.Resolutions, barcodeDpi) >= 0) s.BarcodePrint.Dpi = barcodeDpi;
                        break;
                    case "label_sheet":
                        if (Printing.PrintSheet.IsLabelSheet(value)) s.LabelSheet = value.ToUpperInvariant();
                        break;
                    case "label_nudge_x":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double nudgeX) && !double.IsNaN(nudgeX) && !double.IsInfinity(nudgeX))
                            s.LabelNudgeX = Math.Max(-MaxLabelNudge, Math.Min(MaxLabelNudge, nudgeX));
                        break;
                    case "label_nudge_y":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double nudgeY) && !double.IsNaN(nudgeY) && !double.IsInfinity(nudgeY))
                            s.LabelNudgeY = Math.Max(-MaxLabelNudge, Math.Min(MaxLabelNudge, nudgeY));
                        break;
                    case "scan_anywhere":
                        s.ScanAnywhere = !Array.Exists(new[] { "off", "false", "no", "0" }, word => value.Equals(word, StringComparison.OrdinalIgnoreCase));
                        break;
                }
            }
            if (!wholePixelsLine && lines.Length > 0)
            {
                // 0.1 and 0.2 always saved pictures at whole pixels per module
                s.FromOlder = true;
                s.WholePixels = true;
            }
            return s;
        }

        // "1000x680", or "1000x680 maximized"
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
            text.Append("print=").Append(Print.On ? "on" : "off").Append("\r\n");
            text.Append("print_width=").Append(Print.Width.ToString("0.######", CultureInfo.InvariantCulture)).Append("\r\n");   // inches from millimetres stay the same length
            text.Append("print_unit=").Append(Print.Unit).Append("\r\n");
            text.Append("print_dpi=").Append(Print.Dpi.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("whole_pixels=").Append(WholePixels ? "on" : "off").Append("\r\n");
            text.Append("recent_codes=").Append(RecentCodes ? "on" : "off").Append("\r\n");
            if (RecentNoteShown) text.Append("recent_note=shown\r\n");
            text.Append("barcode_colour=").Append(BarcodeLook.Color).Append("\r\n");
            text.Append("barcode_background=").Append(BarcodeLook.Background).Append("\r\n");
            text.Append("barcode_transparent=").Append(BarcodeLook.Transparent ? "on" : "off").Append("\r\n");
            text.Append("barcode_scale=").Append(BarcodeLook.Scale.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("barcode_text=").Append(BarcodeLook.ShowText ? "on" : "off").Append("\r\n");
            text.Append("barcode_rectangular=").Append(BarcodeLook.AllowRectangular ? "on" : "off").Append("\r\n");
            text.Append("barcode_quiet_zones=").Append(BarcodeLook.QuietZones ? "on" : "off").Append("\r\n");
            text.Append("barcode_bar_height=").Append(BarcodeLook.BarHeight.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("barcode_print=").Append(BarcodePrint.On ? "on" : "off").Append("\r\n");
            text.Append("barcode_print_width=").Append(BarcodePrint.Width.ToString("0.######", CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("barcode_print_unit=").Append(BarcodePrint.Unit).Append("\r\n");
            text.Append("barcode_print_dpi=").Append(BarcodePrint.Dpi.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("label_sheet=").Append(LabelSheet).Append("\r\n");
            text.Append("label_nudge_x=").Append(LabelNudgeX.ToString("0.######", CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("label_nudge_y=").Append(LabelNudgeY.ToString("0.######", CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("scan_anywhere=").Append(ScanAnywhere ? "on" : "off").Append("\r\n");
            return text.ToString();
        }

        // a colour as the website's colour boxes give it: #rrggbb
        // a colour as the colour boxes write one (#rrggbb, #rrggbbaa, "transparent"), or any other the website takes
        static bool IsColour(string value) => Css.Colour(value) != null;

        // false when settings can't be kept (DataFolder.WhyNotKept says why)
        public bool Save() => DataFolder.TryWrite(FileName, ToText());

        public static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    }
}
