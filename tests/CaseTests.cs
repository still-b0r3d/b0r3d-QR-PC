using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using B0r3dQR.Design;

namespace B0r3dQR.Tests
{
    // toUpperCase and toLowerCase against a browser's own (ChromiumCase.cs, from headless Edge).
    static partial class TestMain
    {
        // every code point's capital and small letter as Chromium gives them, the capital sigma's rule
        // for every code point before and after one, and strings of sigmas and of mixed letters
        static void CaseLikeChromium()
        {
            Dictionary<int, string> upper = Changes(ChromiumUpperOnes, ChromiumUpperLonger.Select(p => (p.Code, p.Upper)));
            Dictionary<int, string> lower = Changes(ChromiumLowerOnes, ChromiumLowerLonger.Select(p => (p.Code, p.Lower)));
            string sigma = ((char)0x3A3).ToString(), alpha = ((char)0x391).ToString();
            const char final = (char)0x3C2;
            var wrong = new List<string>();
            int netUpper = 0, netLower = 0, part = 0, partAt = 0;
            for (int c = 0; c <= 0x10FFFF; c++)
            {
                string s = c < 0x10000 ? ((char)c).ToString() : char.ConvertFromUtf32(c);
                string up = upper.TryGetValue(c, out string u) ? u : s, low = lower.TryGetValue(c, out string l) ? l : s;
                string ours = Js.ToUpperCase(s);
                if (ours != up) wrong.Add("toUpperCase(" + Codes(s) + ") is " + Codes(ours) + ", Chromium's " + Codes(up));
                ours = Js.ToLowerCase(s);
                if (ours != low) wrong.Add("toLowerCase(" + Codes(s) + ") is " + Codes(ours) + ", Chromium's " + Codes(low));
                if (s.ToUpperInvariant() != up) netUpper++;
                if (s.ToLowerInvariant() != low) netLower++;

                // its part in the sigma rule: before a sigma at the end, between a capital and a sigma, then
                // after a sigma with a capital on each side
                while (partAt < ChromiumSigmaParts.Length && ChromiumSigmaParts[partAt] <= c)
                {
                    part = ChromiumSigmaParts[partAt + 1];
                    partAt += 2;
                }
                bool before = Js.ToLowerCase(s + sigma)[low.Length] == final;
                bool between = Js.ToLowerCase(alpha + s + sigma)[1 + low.Length] == final;
                bool after = Js.ToLowerCase(alpha + sigma + s + alpha)[1] == final;
                int found = before && between && !after ? 1 : !before && between && !after ? 2 : !before && !between && after ? 0 : 9;
                if (found != part) wrong.Add("U+" + c.ToString("X4") + " plays part " + found + " in the sigma rule, in Chromium " + part);
            }
            foreach (var t in ChromiumCaseStrings)
            {
                string up = Js.ToUpperCase(t.Text), low = Js.ToLowerCase(t.Text);
                if (up != t.Upper) wrong.Add("toUpperCase(" + Codes(t.Text) + ") is " + Codes(up) + ", Chromium's " + Codes(t.Upper));
                if (low != t.Lower) wrong.Add("toLowerCase(" + Codes(t.Text) + ") is " + Codes(low) + ", Chromium's " + Codes(t.Lower));
            }
            Check(Js.ToUpperCase(null) == "" && Js.ToLowerCase("") == "", "nothing stays nothing");
            Check(wrong.Count == 0, wrong.Count + " differ from Chromium: " + string.Join(" | ", wrong.Take(8)));
            Console.WriteLine("      (.NET's ToUpperInvariant gives something else for " + netUpper.ToString("N0", CultureInfo.InvariantCulture) + " code points, ToLowerInvariant for " +
                              netLower.ToString("N0", CultureInfo.InvariantCulture) + ")");
        }

        // the code points a conversion changes, each with what it gives
        static Dictionary<int, string> Changes(int[] ones, IEnumerable<(int Code, string To)> longer)
        {
            var changes = new Dictionary<int, string>();
            for (int i = 0; i < ones.Length; i += 2) changes[ones[i]] = char.ConvertFromUtf32(ones[i + 1]);
            foreach (var (code, to) in longer) changes[code] = to;
            return changes;
        }

        // a string's code points in hex, for a failure message
        static string Codes(string s)
        {
            var codes = new List<string>();
            for (int i = 0; i < s.Length; i++)
            {
                bool pair = char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
                codes.Add("U+" + (pair ? char.ConvertToUtf32(s[i], s[i + 1]) : s[i]).ToString("X4"));
                if (pair) i++;
            }
            return string.Join(" ", codes);
        }
    }
}
