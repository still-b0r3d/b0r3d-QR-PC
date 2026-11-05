using System;
using System.Globalization;
using System.Windows.Media;

namespace B0r3dQR.Svg
{
    // Reads the numbers in SVG attributes the way Chromium's parser does: a sign, digits, a point
    // only with a digit after it, an exponent (but "em" and "ex" are units), nothing that doesn't
    // fit a float; spaces and at most one comma between numbers.
    sealed class Numbers
    {
        readonly string s;
        int at;

        public Numbers(string text)
        {
            s = text ?? "";
        }

        public bool AtEnd => at >= s.Length;
        public char Next => at < s.Length ? s[at] : '\0';
        public int At { get => at; set => at = value; }
        public string Text => s;

        public static bool Space(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';
        static bool Digit(char c) => c >= '0' && c <= '9';
        public static bool NumberStart(char c) => Digit(c) || c == '.' || c == '+' || c == '-';

        public void SkipSpaces()
        {
            while (at < s.Length && Space(s[at])) at++;
        }

        // spaces, and a comma with spaces after it if there's one
        public void SkipSeparator()
        {
            SkipSpaces();
            if (at < s.Length && s[at] == ',')
            {
                at++;
                SkipSpaces();
            }
        }

        public bool Take(char c)
        {
            if (at >= s.Length || s[at] != c) return false;
            at++;
            return true;
        }

        // a number at the cursor (no spaces before it), or false with the cursor where it was; with `clamp`, one past
        // a float's range is as big as a float goes (CSS's way in Chromium: font-size="1e400" is the biggest size),
        // where SVG's own numbers refuse it
        public bool Number(out double value, bool clamp = false)
        {
            value = 0;
            int i = at;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            if (i >= s.Length || !Digit(s[i]) && s[i] != '.') return false;
            while (i < s.Length && Digit(s[i])) i++;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                if (i >= s.Length || !Digit(s[i])) return false;
                while (i < s.Length && Digit(s[i])) i++;
            }
            if (i + 1 < s.Length && (s[i] == 'e' || s[i] == 'E') && s[i + 1] != 'x' && s[i + 1] != 'm')
            {
                int j = i + 1;
                if (s[j] == '+' || s[j] == '-') j++;
                if (j >= s.Length || !Digit(s[j])) return false;
                while (j < s.Length && Digit(s[j])) j++;
                i = j;
            }
            // (the text is a number by now, so .NET Framework refuses it only when it's past a double's range)
            if (!double.TryParse(s.Substring(at, i - at), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || Math.Abs(value) > float.MaxValue)
            {
                if (!clamp) return false;
                value = s[at] == '-' ? -float.MaxValue : float.MaxValue;
            }
            at = i;
            return true;
        }

        // a number, then spaces and a comma if they're there
        public bool NumberThenSeparator(out double value)
        {
            if (!Number(out value)) return false;
            SkipSeparator();
            return true;
        }

        // an arc's flag: 0 or 1, on its own (it needs nothing after it before the next number)
        public bool Flag(out bool value)
        {
            value = false;
            if (at >= s.Length || s[at] != '0' && s[at] != '1') return false;
            value = s[at++] == '1';
            SkipSeparator();
            return true;
        }

        // a run of letters (a unit, a function's name)
        public string Word()
        {
            int start = at;
            while (at < s.Length && (char.IsLetter(s[at]) || s[at] == '%')) at++;
            return s.Substring(start, at - start);
        }

        // A list of numbers separated by spaces or commas (a viewBox, points, a dash array); null when
        // something in it isn't a number. With `upToError`, the numbers before a problem instead.
        public static double[] List(string text, bool upToError = false)
        {
            var n = new Numbers(text);
            var found = new System.Collections.Generic.List<double>();
            n.SkipSpaces();
            while (!n.AtEnd)
            {
                if (!n.NumberThenSeparator(out double v)) return upToError ? found.ToArray() : null;
                found.Add(v);
            }
            return found.ToArray();
        }
    }

    // A length as written: a number and its unit ("" for user units).
    struct Length
    {
        public double Value;
        public string Unit;   // "", px, %, em, ex, rem, ch, pt, pc, mm, cm, in, q

        public Length(double value, string unit = "")
        {
            Value = value;
            Unit = unit;
        }

        public bool IsPercent => Unit == "%";
        public bool IsRelative => Unit == "%" || Unit == "em" || Unit == "ex" || Unit == "rem" || Unit == "ch";

        // The whole text as one length, spaces around it allowed; false when it isn't one. With `clamp`, a number past
        // a float's range is as big as a float goes, as CSS has it (see Numbers.Number).
        public static bool TryParse(string text, out Length length, bool clamp = false)
        {
            length = default;
            if (text == null) return false;
            var n = new Numbers(text);
            n.SkipSpaces();
            if (!n.Number(out double v, clamp)) return false;
            string unit = n.Word().ToLowerInvariant();
            n.SkipSpaces();
            if (!n.AtEnd) return false;
            switch (unit)
            {
                case "":
                case "px":
                case "%":
                case "em":
                case "ex":
                case "rem":
                case "ch":
                case "pt":
                case "pc":
                case "mm":
                case "cm":
                case "in":
                case "q":
                    length = new Length(v, unit == "px" ? "" : unit);
                    return true;
                default:
                    return false;
            }
        }

        // In px: `whole` is what 100% is, `font` the font's size in px, `xHeight` its x-height in px
        // (0: half the size).
        public double Px(double whole, double font, double xHeight = 0)
        {
            switch (Unit)
            {
                case "%": return Value / 100 * whole;
                case "em": return Value * font;
                case "ex": return Value * (xHeight > 0 ? xHeight : font * 0.5);
                case "ch": return Value * font * 0.5;
                case "rem": return Value * 16;
                case "pt": return Value * 96 / 72;
                case "pc": return Value * 16;
                case "mm": return Value * 96 / 25.4;
                case "cm": return Value * 96 / 2.54;
                case "in": return Value * 96;
                case "q": return Value * 96 / 101.6;
                default: return Value;
            }
        }
    }

    // SVG's transform attribute: matrix(), translate(), scale(), rotate(), skewX() and skewY(), in
    // a list; a list with any mistake in it counts as none, as in Chromium.
    static class Transforms
    {
        public static Matrix? Parse(string text)
        {
            if (text == null) return null;
            var n = new Numbers(text);
            Matrix total = Matrix.Identity;
            n.SkipSpaces();
            while (!n.AtEnd)
            {
                string name = n.Word();
                n.SkipSpaces();
                if (!n.Take('(')) return null;
                var args = new double[6];
                int count = 0;
                n.SkipSpaces();
                while (count < 6 && Numbers.NumberStart(n.Next))
                {
                    if (!n.NumberThenSeparator(out args[count])) return null;
                    count++;
                }
                n.SkipSpaces();
                if (!n.Take(')')) return null;
                Matrix m;
                switch (name)
                {
                    case "matrix" when count == 6:
                        m = new Matrix(args[0], args[1], args[2], args[3], args[4], args[5]);
                        break;
                    case "translate" when count == 1 || count == 2:
                        m = new Matrix(1, 0, 0, 1, args[0], count == 2 ? args[1] : 0);
                        break;
                    case "scale" when count == 1 || count == 2:
                        m = new Matrix(args[0], 0, 0, count == 2 ? args[1] : args[0], 0, 0);
                        break;
                    case "rotate" when count == 1 || count == 3:
                        m = Matrix.Identity;
                        m.RotateAt(args[0], count == 3 ? args[1] : 0, count == 3 ? args[2] : 0);
                        break;
                    case "skewX" when count == 1:
                        m = new Matrix(1, 0, Math.Tan(args[0] * Math.PI / 180), 1, 0, 0);
                        break;
                    case "skewY" when count == 1:
                        m = new Matrix(1, Math.Tan(args[0] * Math.PI / 180), 0, 1, 0, 0);
                        break;
                    default:
                        return null;
                }
                total = m * total;   // each one acts inside the ones before it
                n.SkipSeparator();
            }
            return total;
        }

        // CSS's transform property (in a style): the same functions with units (deg, rad, grad, turn,
        // px) and commas, plus translateX/Y, scaleX/Y and skew(); "none" is none
        public static Matrix? ParseCss(string text)
        {
            if (text == null) return null;
            string t = text.Trim();
            if (t.Equals("none", StringComparison.OrdinalIgnoreCase)) return Matrix.Identity;
            var n = new Numbers(t);
            Matrix total = Matrix.Identity;
            n.SkipSpaces();
            while (!n.AtEnd)
            {
                string name = n.Word().ToLowerInvariant();
                if (!n.Take('(')) return null;
                var values = new double[6];
                var units = new string[6];
                int count = 0;
                n.SkipSpaces();
                while (count < 6 && Numbers.NumberStart(n.Next))
                {
                    if (!n.Number(out values[count])) return null;
                    units[count] = n.Word().ToLowerInvariant();
                    count++;
                    n.SkipSeparator();
                }
                if (!n.Take(')')) return null;
                double Angle(int i)
                {
                    switch (units[i])
                    {
                        case "deg": return values[i];
                        case "rad": return values[i] * 180 / Math.PI;
                        case "grad": return values[i] * 0.9;
                        case "turn": return values[i] * 360;
                        case "": return values[i] == 0 ? 0 : double.NaN;
                        default: return double.NaN;
                    }
                }
                double Px(int i) => units[i] == "" || units[i] == "px" ? values[i] : double.NaN;
                Matrix m = Matrix.Identity;
                switch (name)
                {
                    case "matrix" when count == 6: m = new Matrix(values[0], values[1], values[2], values[3], values[4], values[5]); break;
                    case "translate" when count == 1 || count == 2: m = new Matrix(1, 0, 0, 1, Px(0), count == 2 ? Px(1) : 0); break;
                    case "translatex" when count == 1: m = new Matrix(1, 0, 0, 1, Px(0), 0); break;
                    case "translatey" when count == 1: m = new Matrix(1, 0, 0, 1, 0, Px(0)); break;
                    case "scale" when count == 1 || count == 2: m = new Matrix(values[0], 0, 0, count == 2 ? values[1] : values[0], 0, 0); break;
                    case "scalex" when count == 1: m = new Matrix(values[0], 0, 0, 1, 0, 0); break;
                    case "scaley" when count == 1: m = new Matrix(1, 0, 0, values[0], 0, 0); break;
                    case "rotate" when count == 1: m.Rotate(Angle(0)); break;
                    case "skew" when count == 1 || count == 2: m = new Matrix(1, count == 2 ? Math.Tan(Angle(1) * Math.PI / 180) : 0, Math.Tan(Angle(0) * Math.PI / 180), 1, 0, 0); break;
                    case "skewx" when count == 1: m = new Matrix(1, 0, Math.Tan(Angle(0) * Math.PI / 180), 1, 0, 0); break;
                    case "skewy" when count == 1: m = new Matrix(1, Math.Tan(Angle(0) * Math.PI / 180), 0, 1, 0, 0); break;
                    default: return null;
                }
                if (double.IsNaN(m.M11 + m.M12 + m.M21 + m.M22 + m.OffsetX + m.OffsetY)) return null;
                total = m * total;
                n.SkipSpaces();
            }
            return total;
        }
    }
}
