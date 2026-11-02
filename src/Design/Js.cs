using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace B0r3dQR.Design
{
    // The bits of JavaScript the website's data templates lean on, done the way a browser does
    // them, so the text they write and read is the website's to the byte: what counts as a space
    // (trim, and \s in its patterns), encodeURIComponent and decodeURIComponent, URLSearchParams,
    // parseFloat and toFixed(2). .NET's own versions differ in small ways (Uri.EscapeDataString
    // escapes ! * ' ( ), Trim takes U+0085 but not U+FEFF, Math.Round works on the rounded value).
    static class Js
    {
        // JavaScript's white space and line ends, for patterns (its \s)
        public const string Space = @"[\t\n\v\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]";

        public static bool IsSpace(char c) =>
            c == '\t' || c == '\n' || c == '\v' || c == '\f' || c == '\r' || c == ' ' || c == 0x00A0 || c == 0x1680 ||
            (c >= 0x2000 && c <= 0x200A) || c == 0x2028 || c == 0x2029 || c == 0x202F || c == 0x205F || c == 0x3000 || c == 0xFEFF;

        public static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            int start = 0, end = s.Length;
            while (start < end && IsSpace(s[start])) start++;
            while (end > start && IsSpace(s[end - 1])) end--;
            return s.Substring(start, end - start);
        }

        public static string TrimStart(string s)
        {
            int start = 0;
            while (start < s.Length && IsSpace(s[start])) start++;
            return s.Substring(start);
        }

        // encodeURIComponent: UTF-8, with A-Z a-z 0-9 - _ . ! ~ * ' ( ) left as they are. (A browser
        // refuses half an emoji; here it becomes the replacement character.)
        public static string EncodeUriComponent(string s)
        {
            var result = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(s ?? ""))
            {
                char c = (char)b;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || "-_.!~*'()".IndexOf(c) >= 0) result.Append(c);
                else result.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            return result.ToString();
        }

        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        // decodeURIComponent, or null where a browser would throw (a stray %, broken UTF-8)
        public static string DecodeUriComponent(string s)
        {
            var result = new StringBuilder();
            var run = new List<byte>();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '%')
                {
                    if (!Flush(run, result)) return null;
                    result.Append(s[i]);
                    continue;
                }
                if (i + 2 >= s.Length || !IsHex(s[i + 1]) || !IsHex(s[i + 2])) return null;
                run.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
                i += 2;
            }
            return Flush(run, result) ? result.ToString() : null;
        }

        static bool Flush(List<byte> run, StringBuilder into)
        {
            if (run.Count == 0) return true;
            try
            {
                into.Append(StrictUtf8.GetString(run.ToArray()));
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            finally
            {
                run.Clear();
            }
        }

        static bool IsHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        // new URLSearchParams(query).get(name): the first value of that name ('+' is a space, %xx
        // are UTF-8 bytes, a broken % stays as it is), or null when there's none
        public static string SearchParam(string query, string name)
        {
            string q = query ?? "";
            if (q.StartsWith("?", StringComparison.Ordinal)) q = q.Substring(1);
            foreach (string pair in q.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string key = FormDecode(eq < 0 ? pair : pair.Substring(0, eq));
                if (key == name) return eq < 0 ? "" : FormDecode(pair.Substring(eq + 1));
            }
            return null;
        }

        static string FormDecode(string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s.Replace('+', ' '));
            var decoded = new List<byte>(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == '%' && i + 2 < bytes.Length && IsHex((char)bytes[i + 1]) && IsHex((char)bytes[i + 2]))
                {
                    decoded.Add(Convert.ToByte(((char)bytes[i + 1]).ToString() + (char)bytes[i + 2], 16));
                    i += 2;
                }
                else decoded.Add(bytes[i]);
            }
            return Encoding.UTF8.GetString(decoded.ToArray());
        }

        // parseFloat: the number at the start (after any space), else NaN ("12abc" is 12)
        public static double ParseFloat(string s)
        {
            Match m = Regex.Match(TrimStart(s ?? ""), @"^[+-]?(Infinity|([0-9]+(\.[0-9]*)?|\.[0-9]+)([eE][+-]?[0-9]+)?)");
            if (!m.Success) return double.NaN;
            string number = m.Value;
            if (number.EndsWith("Infinity", StringComparison.Ordinal)) return number.StartsWith("-", StringComparison.Ordinal) ? double.NegativeInfinity : double.PositiveInfinity;
            try
            {
                return double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return number.StartsWith("-", StringComparison.Ordinal) ? double.NegativeInfinity : double.PositiveInfinity;
            }
        }

        // What a browser's number field takes ("-12.5", ".5", "1e3"; not "5.", "+5" or "12,5")
        public static bool IsNumberFieldValue(string s) => Regex.IsMatch(s ?? "", @"^-?([0-9]+(\.[0-9]+)?|\.[0-9]+)([eE][+-]?[0-9]+)?\z");

        // String(x) for a number: the shortest digits that read back the same, written the way
        // JavaScript writes numbers (0.00001, 1e-7, 1e+21)
        public static string NumberToString(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsInfinity(v)) return v > 0 ? "Infinity" : "-Infinity";
            if (v == 0) return "0";
            // the first of 15, 16 and 17 digits that reads back the same ("R" on .NET Framework
            // goes from 15 straight to 17)
            double a = Math.Abs(v);
            string r = a.ToString("G15", CultureInfo.InvariantCulture);
            if (double.Parse(r, CultureInfo.InvariantCulture) != a) r = a.ToString("G16", CultureInfo.InvariantCulture);
            if (double.Parse(r, CultureInfo.InvariantCulture) != a) r = a.ToString("G17", CultureInfo.InvariantCulture);
            int e = r.IndexOfAny(new[] { 'E', 'e' });
            int exponent = e < 0 ? 0 : int.Parse(r.Substring(e + 1), CultureInfo.InvariantCulture);
            string mantissa = e < 0 ? r : r.Substring(0, e);
            int point = mantissa.IndexOf('.');
            string digits = mantissa.Replace(".", "");
            int n = (point < 0 ? mantissa.Length : point) + exponent;   // the value is 0.digits x 10^n
            int lead = 0;
            while (lead < digits.Length - 1 && digits[lead] == '0') { lead++; n--; }
            digits = digits.Substring(lead).TrimEnd('0');
            if (digits.Length == 0) return "0";
            int k = digits.Length;
            string s;
            if (k <= n && n <= 21) s = digits + new string('0', n - k);
            else if (0 < n && n <= 21) s = digits.Substring(0, n) + "." + digits.Substring(n);
            else if (-6 < n && n <= 0) s = "0." + new string('0', -n) + digits;
            else s = digits.Substring(0, 1) + (k > 1 ? "." + digits.Substring(1) : "") + "e" + (n - 1 >= 0 ? "+" : "-") + Math.Abs(n - 1).ToString(CultureInfo.InvariantCulture);
            return (v < 0 ? "-" : "") + s;
        }

        // (x).toFixed(2) for x from 0 up: the exact value of the number rounded to cents, halves up
        // (1.005 is really 1.00499... so it gives 1.00, as in a browser)
        public static string ToFixed2(double x) => ToFixed(x, 2);

        // (x).toFixed(digits) for x from 0 up, the same way
        public static string ToFixed(double x, int digits)
        {
            if (double.IsInfinity(x)) return x > 0 ? "Infinity" : "-Infinity";
            if (Math.Abs(x) >= 1e21) return x.ToString("R", CultureInfo.InvariantCulture).Replace("E+", "e+");   // a browser writes these as "1e+21"
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(x));
            int exponent = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (exponent == 0) exponent = 1;
            else mantissa |= 1L << 52;
            exponent -= 1075;
            BigInteger scaled = new BigInteger(mantissa) * BigInteger.Pow(10, digits);
            if (exponent >= 0) scaled <<= exponent;
            else
            {
                BigInteger whole = BigInteger.One << -exponent;
                scaled = (2 * scaled + whole) / (2 * whole);
            }
            string text = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
            return (x < 0 ? "-" : "") + (digits == 0 ? text : text.Substring(0, text.Length - digits) + "." + text.Substring(text.Length - digits));
        }
    }
}
