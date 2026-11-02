using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace B0r3dQR.Barcodes
{
    // GS1 data checked and shortened the way zint 2.16 does it before making GS1-128 and GS1 DataMatrix
    // (gs1.c's zint_gs1_verify, with AIs in round brackets and without the GS1 Syntax Engine, which the
    // website's zint doesn't have). zint refuses only what's below; its other checks (check digits,
    // dates...) are warnings, which the website never shows, so it checks check digits itself.
    static partial class Gs1
    {
        // The data with the brackets gone and a GS (FNC1) after each value whose AI's length isn't fixed
        // by GS1, or null with zint's message. `matrix`: for a 2D code (zint's message differs when the
        // data doesn't start with an AI; the website's own checks never let such data this far).
        public static string Reduce(string source, bool matrix, out string error)
        {
            error = null;
            foreach (char c in source)
            {
                if (c > 127) return Refuse(out error, "Extended ASCII characters are not supported by GS1");
                if (c == 0) return Refuse(out error, "NUL characters not permitted in GS1 mode");
                if (c < 32) return Refuse(out error, "Control characters are not supported by GS1");
                if (c == 127) return Refuse(out error, "DEL characters are not supported by GS1");
            }
            if (source.Length == 0 || source[0] != '(')
                return Refuse(out error, matrix ? "Data does not start with an AI or Digital Link URI" : "Data does not start with an AI");

            // the brackets balance, and each AI is 2 to 4 digits
            int level = 0, maxLevel = 0, aiLength = 0, longest = 0, shortest = 5, longestAt = 0, shortestAt = 0, nonDigitAt = -1;
            bool inAi = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '(')
                {
                    level++;
                    if (level > maxLevel) maxLevel = level;
                    inAi = true;
                }
                else if (c == ')' && level > 0)
                {
                    level--;
                    if (aiLength > longest) { longest = aiLength; longestAt = i - aiLength; }
                    if (aiLength < shortest) { shortest = aiLength; shortestAt = i - aiLength; }
                    aiLength = 0;
                    inAi = false;
                }
                else if (inAi)
                {
                    aiLength++;
                    if (c < '0' || c > '9') nonDigitAt = i - aiLength + 1;
                }
            }
            if (level != 0) return Refuse(out error, "Malformed AI in input (brackets don't match)");
            if (maxLevel > 1) return Refuse(out error, "Found nested brackets in input");
            if (longest > 4) return Refuse(out error, "Invalid AI at position " + longestAt + " in input (AI too long)");
            if (shortest <= 1) return Refuse(out error, "Invalid AI at position " + shortestAt + " in input (AI too short)");
            if (nonDigitAt >= 0) return Refuse(out error, "Invalid AI at position " + nonDigitAt + " in input (non-numeric characters in AI)");

            // each AI's data, where zint looks for it: after as many digits as the AI's number has
            var ais = new List<(int Ai, int Start, int Length)>();
            for (int i = 1; i < source.Length; i++)
            {
                if (source[i - 1] != '(') continue;
                int digits = source.IndexOf(')', i) - i;
                int ai = int.Parse(source.Substring(i, digits), CultureInfo.InvariantCulture);
                int start = i + (ai >= 1000 ? 5 : ai >= 100 ? 4 : 3);
                int length = 0;
                while (start + length < source.Length && source[start + length] != '(') length++;
                if (length == 0) return Refuse(out error, "Empty data field in input");
                ais.Add((ai, start, length));
                i += digits;
            }
            foreach (var (ai, _, length) in ais)
            {
                (int Min, int Max)? known = Known(ai);
                if (known == null) return Refuse(out error, "Invalid AI (" + ai.ToString("00", CultureInfo.InvariantCulture) + ")");
                if (length < known.Value.Min || length > known.Value.Max || !PartsWhole(ai, length))
                    return Refuse(out error, "Invalid data length for AI (" + ai.ToString("00", CultureInfo.InvariantCulture) + ")");
            }

            // the reduced data: brackets dropped, GS before an AI that follows one of unfixed length
            var reduced = new StringBuilder(source.Length);
            bool fixedLength = true;
            level = 0;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '(')
                {
                    level++;
                    if (!fixedLength) reduced.Append('\x1D');
                    if (i + 1 != source.Length)
                    {
                        int first = int.Parse(source.Substring(i + 1, 2), CultureInfo.InvariantCulture);
                        // GS1's "element strings with predefined length" (and AI 23, which zint keeps for old data)
                        fixedLength = first <= 4 || (first >= 11 && first <= 20) || first == 23 || (first >= 31 && first <= 36) || first == 41;
                    }
                }
                else if (c == ')' && level > 0) level--;
                else reduced.Append(c);
            }
            return reduced.ToString();
        }

        static string Refuse(out string error, string message)
        {
            error = message;
            return null;
        }

        // GS1 data as a reader gives it (AIs and values, a GS after each value of no fixed length) in round
        // brackets, as the website's reader writes it: "(01)09506000134352(10)ABC". Null when it doesn't
        // read as AIs zint knows.
        public static string Hri(string raw)
        {
            var hri = new StringBuilder();
            int i = 0;
            while (i < raw.Length)
            {
                if (raw[i] == '\x1D')
                {
                    i++;
                    continue;
                }
                int digits = 0;
                for (int n = 2; n <= 4 && i + n <= raw.Length && digits == 0; n++)
                {
                    string candidate = raw.Substring(i, n);
                    if (IsDigits(candidate) && Known(int.Parse(candidate, CultureInfo.InvariantCulture)) != null) digits = n;
                }
                if (digits == 0) return null;
                string ai = raw.Substring(i, digits);
                i += digits;
                int first = int.Parse(ai.Substring(0, 2), CultureInfo.InvariantCulture);
                int end;
                if (first <= 4 || (first >= 11 && first <= 20) || first == 23 || (first >= 31 && first <= 36) || first == 41)
                    end = Math.Min(raw.Length, i + Known(int.Parse(ai, CultureInfo.InvariantCulture)).Value.Max);
                else
                {
                    end = raw.IndexOf('\x1D', i);
                    if (end < 0) end = raw.Length;
                }
                hri.Append('(').Append(ai).Append(')').Append(raw, i, end - i);
                i = end;
            }
            return hri.Length > 0 ? hri.ToString() : null;
        }

        // GS1 data as ZXing.Net reads it, in round brackets (null when it isn't GS1 data zint knows), and
        // the same with only what the reader puts first taken off: "]C1" from its Code 128 reader told to
        // expect GS1, a GS from its Data Matrix one
        public static string FromScanned(string text) => Hri(Unprefixed(text));

        public static string Unprefixed(string text) => System.Text.RegularExpressions.Regex.Replace(text, "^(\\][A-Za-z][0-9])?\x1D*", "");

        // The AIs whose data has optional parts that zint's checks take whole or not at all (gs1_lint.h: a part cut
        // short fails the length): a date or a date range (7007), a date and maybe hours and minutes (7011), a date
        // and hour, maybe with minutes and then seconds (8008). Every other AI takes any length in its range.
        static bool PartsWhole(int ai, int length)
        {
            switch (ai)
            {
                case 7007: return length == 6 || length == 12;
                case 7011: return length == 6 || length == 10;
                case 8008: return length == 8 || length == 10 || length == 12;
                default: return true;
            }
        }

        static bool IsDigits(string text)
        {
            foreach (char c in text)
                if (c < '0' || c > '9') return false;
            return true;
        }

        // the shortest and longest data zint takes for an AI, or null when it doesn't know the AI
        static (int Min, int Max)? Known(int ai)
        {
            foreach (var (from, to, min, max) in KnownAis)
                if (ai >= from && ai <= to) return (min, max);
            return null;
        }
    }
}
