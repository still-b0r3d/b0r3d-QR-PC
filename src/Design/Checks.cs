using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using B0r3dQR.Qr;

namespace B0r3dQR.Design
{
    // The website's checks on a design, worded the same: colours and quiet zone under the code, the
    // typed text under the data box (each with a one-click fix), and "What's in this code".
    static class Checks
    {
        // ---------- colours and the quiet zone ----------

        // a colour as the website mixes them: channels 0-255, unrounded
        struct Rgb
        {
            public double R, G, B;
            public static Rgb Of(Color c) => new Rgb { R = c.R, G = c.G, B = c.B };
        }

        static readonly Rgb White = new Rgb { R = 255, G = 255, B = 255 };

        public static List<string> Warnings(QrDesign d) =>
            Warnings(new[] { d.DotsColor, d.CornersSquareColor, d.CornersDotColor }, d.Background, d.Frame?.BackgroundColor, d.Frame == null ? d.Margin : (double?)null);

        // the same for any code's colours: its parts, its background, a frame's background, and its
        // quiet zone in modules (null with a frame, which gives the room)
        public static List<string> Warnings(string[] colours, string backgroundCss, string frameBackground, double? margin)
        {
            var warnings = new List<string>();
            // what's behind the code: its background, over the frame's (see-through parts of either
            // over white paper)
            Color? frame = frameBackground != null ? Css.Colour(frameBackground) : null;
            bool hasFrame = frame.HasValue && frame.Value.A > 0;
            Color? background = Css.Colour(backgroundCss);
            Rgb under;
            if (background.HasValue && background.Value.A > 0) under = Over(background.Value, hasFrame ? Over(frame.Value, White) : White);
            else if (hasFrame) under = Over(frame.Value, White);
            else
            {
                warnings.Add("Transparent background: put the code on a plain, light surface.");
                under = White;
            }
            var parts = new List<Rgb>();
            foreach (string css in colours)
            {
                Color? c = Css.Colour(css);
                if (c.HasValue && c.Value.A > 0) parts.Add(Over(c.Value, under));
            }
            if (parts.Count > 0)
            {
                double back = Luminance(under);
                if (parts.Any(p => Luminance(p) > back))
                    warnings.Add("Light code on a dark background: many phone cameras can't read inverted codes.");
                double worst = parts.Min(p => Contrast(Luminance(p), back));
                if (worst < 4)
                    warnings.Add("Low contrast (" + Math.Round(worst, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture) +
                                 ":1) between the code and its background. Dark on light with at least 4:1 scans best.");
            }
            if (margin < 4)
                warnings.Add("Only " + Math.Max(0, margin.Value).ToString("0.##", CultureInfo.InvariantCulture) + " modules of blank space around the code. Leave at least 4 (Quiet zone), or place it on a plain light area.");
            return warnings;
        }

        static Rgb Over(Color top, Rgb under)
        {
            double a = top.A / 255.0;
            return new Rgb { R = top.R * a + under.R * (1 - a), G = top.G * a + under.G * (1 - a), B = top.B * a + under.B * (1 - a) };
        }

        // WCAG's relative luminance and contrast ratio
        static double Luminance(Rgb c)
        {
            double Channel(double v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

        // ---------- the typed text ----------

        public sealed class DataCheck
        {
            public string Message, Button, Fixed;
        }

        public static List<DataCheck> DataChecks(string text)
        {
            var checks = new List<DataCheck>();
            string t = text ?? "";
            string trimmed = t.Trim();
            if (trimmed.Length > 0 && trimmed.Length != t.Length)
                checks.Add(new DataCheck { Message = "Starts or ends with spaces or line breaks, which are stored in the code too.", Button = "Trim", Fixed = trimmed });
            if (BareDomain(trimmed))
                checks.Add(new DataCheck { Message = "This looks like a web address without https://. Many phone cameras will show it as plain text instead of opening it.", Button = "Add https://", Fixed = "https://" + trimmed });
            List<string> tracking = TrackingNames(trimmed, out string clean);
            if (tracking.Count > 0)
                checks.Add(new DataCheck { Message = "This link has tracking parameters: " + string.Join(", ", tracking) + ".", Button = "Remove them", Fixed = clean });
            return checks;
        }

        // A web address without its https:// ("b0r3d.org/page"), as the website sees one.
        internal static bool BareDomain(string t)
        {
            if (t.Length == 0 || t.Any(char.IsWhiteSpace)) return false;
            Match scheme = Regex.Match(t, @"^[A-Za-z][A-Za-z0-9+.\-]*:");
            if (scheme.Success && !Regex.IsMatch(t.Substring(scheme.Length), @"^\d{1,5}([/?#]|$)")) return false;
            return Regex.IsMatch(t, @"^(www\.)?([a-z0-9]([a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,63}(:\d{1,5})?([/?#].*)?$", RegexOptions.IgnoreCase);
        }

        static readonly string[] TrackingList =
        {
            "fbclid", "gclid", "gclsrc", "dclid", "gbraid", "wbraid", "msclkid", "yclid", "twclid", "ttclid", "li_fat_id",
            "igshid", "igsh", "mc_cid", "mc_eid", "_hsenc", "_hsmi", "mkt_tok", "srsltid",
        };

        // The tracking parameters in a link (first appearance first) and the link without them.
        internal static List<string> TrackingNames(string text, out string clean)
        {
            clean = text;
            var names = new List<string>();
            Match m = Regex.Match(text ?? "", @"^(https?://)([^/?#\s]*)([^?#\s]*)(\?[^#\s]*)?(#\S*)?$", RegexOptions.IgnoreCase);
            if (!m.Success || !m.Groups[4].Success) return names;
            string authority = m.Groups[2].Value;
            string host = authority.Substring(authority.LastIndexOf('@') + 1);
            int colon = host.LastIndexOf(':');
            if (colon >= 0 && !host.EndsWith("]", StringComparison.Ordinal)) host = host.Substring(0, colon);
            host = host.ToLowerInvariant();
            bool siTracks = new[] { "youtube.com", "youtu.be", "spotify.com" }.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
            var kept = new List<string>();
            foreach (string pair in m.Groups[4].Value.Substring(1).Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string raw = eq >= 0 ? pair.Substring(0, eq) : pair, name;
                try { name = Uri.UnescapeDataString(raw.Replace('+', ' ')); }
                catch (UriFormatException) { name = raw; }
                string lower = name.ToLowerInvariant();
                if (lower.StartsWith("utm_", StringComparison.Ordinal) || TrackingList.Contains(lower) || (siTracks && lower == "si"))
                {
                    if (!names.Contains(name)) names.Add(name);
                }
                else kept.Add(pair);
            }
            if (names.Count > 0)
                clean = m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + (kept.Count > 0 ? "?" + string.Join("&", kept) : "") + m.Groups[5].Value;
            return names;
        }

        // ---------- what's in the code ----------

        public static string LevelName(QrLevel level)
        {
            switch (level)
            {
                case QrLevel.L: return "Low (7%)";
                case QrLevel.M: return "Medium (15%)";
                case QrLevel.Q: return "High (25%)";
                default: return "Highest (30%)";
            }
        }

        // the level the website suggests for this much text (null with none)
        public static QrLevel? Suggested(string text)
        {
            int n = (text ?? "").Length;
            if (n == 0) return null;
            return n <= 50 ? QrLevel.H : n <= 150 ? QrLevel.Q : n <= 500 ? QrLevel.M : QrLevel.L;
        }

        // the rows of "What's in this code": what's stored, the size, the level and the encoding
        public static List<KeyValuePair<string, string>> InTheCode(QrArt art)
        {
            QrCode code = art.Code;
            var rows = new List<KeyValuePair<string, string>>();
            int lineCount = Regex.Split(code.Text, "\r\n|\r|\n").Length;
            string stored = code.ByteCount.ToString("N0", CultureInfo.InvariantCulture) + (code.ByteCount == 1 ? " byte, " : " bytes, ") +
                code.CharacterCount.ToString("N0", CultureInfo.InvariantCulture) + (code.CharacterCount == 1 ? " character" : " characters") +
                (lineCount > 1 ? ", " + lineCount + " lines" : "");
            rows.Add(new KeyValuePair<string, string>("Stored", stored));
            rows.Add(new KeyValuePair<string, string>("Size", "Version " + code.Version + ", " + code.Size + " × " + code.Size + " modules"));
            rows.Add(new KeyValuePair<string, string>("Error correction", LevelName(code.Level) + (art.LevelRaised ? ", raised for the logo" : "")));
            string encoding = code.Utf8Marker ? "text, marked as UTF-8 (ECI 26)" :
                string.Join(" + ", code.Segments.Select(s => (s.Mode == QrMode.Numeric ? "numbers" : s.Mode == QrMode.Alphanumeric ? "capitals and digits" : "text") + " (" + CodePoints(s.Text) + ")"));
            rows.Add(new KeyValuePair<string, string>("Encoding", encoding));
            return rows;
        }

        public const string EverythingLine = "That's everything: no link to this site, no tracking. Scanning shows exactly this text.";

        static int CodePoints(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
                n++;
            }
            return n;
        }
    }
}
