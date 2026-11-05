using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace B0r3dQR.Design
{
    // The CSS values design files carry: colours ("#c000c0", "rgb(0 0 0 / 50%)", "red",
    // "transparent") and lengths ("16px"), read the way the website's browser reads them.
    static class Css
    {
        public static bool IsTransparent(string colour) => string.Equals((colour ?? "").Trim(), "transparent", StringComparison.OrdinalIgnoreCase);

        // A CSS colour, or null when the browser wouldn't take it either.
        public static Color? Colour(string value)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0) return null;
            if (IsTransparent(v)) return Color.FromArgb(0, 0, 0, 0);
            if (v[0] == '#') return Hex(v.Substring(1));
            // rgb(...) and the like, what's inside on one line but for space around it (looked at without a
            // pattern for the inside: one that has to try every way to split spaces took minutes for a
            // few thousand of them)
            Match f = Regex.Match(v, @"^(rgba?|hsla?)\(", RegexOptions.IgnoreCase);
            if (f.Success && v.EndsWith(")", StringComparison.Ordinal))
            {
                string inside = v.Substring(f.Length, v.Length - f.Length - 1).Trim();
                if (inside.IndexOf('\n') < 0) return Function(f.Groups[1].Value.ToLowerInvariant(), inside);
            }
            if (!Regex.IsMatch(v, "^[A-Za-z]+$")) return null;
            if (v.Equals("rebeccapurple", StringComparison.OrdinalIgnoreCase)) return Color.FromRgb(0x66, 0x33, 0x99);
            try
            {
                // WPF knows the CSS (X11) colour names, but only the "gray" way of spelling the greys
                object named = ColorConverter.ConvertFromString(Regex.Replace(v, "grey", "gray", RegexOptions.IgnoreCase));
                return named is Color c ? c : (Color?)null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        static Color? Hex(string h)
        {
            if (!Regex.IsMatch(h, "^[0-9A-Fa-f]+$")) return null;
            int D(int i) => Convert.ToInt32(h.Substring(i, 1), 16);
            int P(int i) => Convert.ToInt32(h.Substring(i, 2), 16);
            switch (h.Length)
            {
                case 3: return Color.FromRgb((byte)(D(0) * 17), (byte)(D(1) * 17), (byte)(D(2) * 17));
                case 4: return Color.FromArgb((byte)(D(3) * 17), (byte)(D(0) * 17), (byte)(D(1) * 17), (byte)(D(2) * 17));
                case 6: return Color.FromRgb((byte)P(0), (byte)P(2), (byte)P(4));
                case 8: return Color.FromArgb((byte)P(6), (byte)P(0), (byte)P(2), (byte)P(4));
                default: return null;
            }
        }

        // rgb(1, 2, 3), rgba(1 2 3 / 50%), hsl(120deg 50% 50%)...
        static Color? Function(string name, string args)
        {
            string[] parts = Regex.Split(args.Trim(), @"\s*[,/]\s*|\s+");
            if (parts.Length < 3 || parts.Length > 4) return null;
            double alpha = 1;
            if (parts.Length == 4 && !Number(parts[3], 1, out alpha)) return null;
            if (name.StartsWith("rgb", StringComparison.Ordinal))
            {
                if (!Number(parts[0], 255, out double r) || !Number(parts[1], 255, out double g) || !Number(parts[2], 255, out double b)) return null;
                return Color.FromArgb(Byte(alpha * 255), Byte(r), Byte(g), Byte(b));
            }
            string hue = parts[0].ToLowerInvariant().Replace("deg", "");
            if (!double.TryParse(hue, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) ||
                !parts[1].EndsWith("%", StringComparison.Ordinal) || !parts[2].EndsWith("%", StringComparison.Ordinal) ||
                !Number(parts[1], 1, out double s) || !Number(parts[2], 1, out double l)) return null;
            h = ((h % 360) + 360) % 360 / 360;
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            double Channel(double t)
            {
                t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
                return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
            }
            return Color.FromArgb(Byte(alpha * 255), Byte(Channel(h + 1.0 / 3) * 255), Byte(Channel(h) * 255), Byte(Channel(h - 1.0 / 3) * 255));
        }

        // a number, or a percentage of `whole`
        static bool Number(string text, double whole, out double value)
        {
            bool percent = text.EndsWith("%", StringComparison.Ordinal);
            if (!double.TryParse(percent ? text.Substring(0, text.Length - 1) : text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            if (percent) value = value / 100 * whole;
            return true;
        }

        static byte Byte(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v)));

        // "#rrggbb", the way the website's colour pickers write colours
        public static string ToHex(Color c) => "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2");

        // a colour for drawing: one the browser can't read draws black in an SVG, as on the website
        public static Color Paint(string value) => Colour(value) ?? Colors.Black;

        // A CSS length the website's frame fields take, as a browser's CSS.supports("width", ...)
        // does: "0", a number with a unit ("16px", "1.5em", "1e1px"), a keyword ("auto") or a sum
        // ("calc(...)"). A negative one isn't a width.
        public static bool IsLength(string value)
        {
            string v = (value ?? "").Trim();
            if (Regex.IsMatch(v, @"^(auto|min-content|max-content|fit-content|inherit|initial|unset|revert|revert-layer)$", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(v, @"^(calc|min|max|clamp|var|env|fit-content)\(.*\)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)) return true;
            return Regex.IsMatch(v, @"^\+?(0+(\.0+)?|(\d+(\.\d+)?|\.\d+)(e[+-]?\d+)?(" + Units + "))$", RegexOptions.IgnoreCase);
        }

        const string Units = "px|em|rem|ex|rex|ch|rch|cap|rcap|ic|ric|lh|rlh|vw|vh|vi|vb|vmin|vmax|svw|svh|svi|svb|svmin|svmax|lvw|lvh|lvi|lvb|lvmin|lvmax|" +
                             "dvw|dvh|dvi|dvb|dvmin|dvmax|cqw|cqh|cqi|cqb|cqmin|cqmax|cm|mm|q|in|pt|pc|%";

        // What the website's export makes of a length field: its first run of digits, points and
        // minus signs, as px whatever the unit ("1.5em" is 1.5, "1e1px" 1), or 0 when that isn't a
        // number; an empty field gets the default.
        public static double Pixels(string value, double empty)
        {
            if (string.IsNullOrEmpty(value)) return empty;
            Match n = Regex.Match(value, @"[-\d.]+");
            return n.Success && double.TryParse(n.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double px) && px != 0 ? px : 0;
        }
    }
}
