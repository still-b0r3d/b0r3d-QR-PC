using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace B0r3dQR.Qr
{
    // One of the 18 caption fonts qr.b0r3d.org ships with itself (WebFontFaces.cs lists them), built into the exe:
    // its faces, one TrueType file for each subset of the font, in the order its 400.css declares them.
    sealed class WebFont
    {
        public readonly string Name, Package;
        public readonly WebFontFace[] Faces;

        public WebFont(string name, string package, WebFontFace[] faces)
        {
            Name = name;
            Package = package;
            Faces = faces;
            foreach (WebFontFace face in faces) face.Font = this;
        }
    }

    // A font a caption takes characters from: one of a web font's @font-faces, or a font on this PC.
    abstract class CaptionFace
    {
        readonly object gate = new object();
        IDictionary<int, ushort> glyphs;
        Typeface typeface;

        public abstract FontFamily Family { get; }

        // the font as WPF draws it, made once (the batch and print threads share it)
        public Typeface Typeface
        {
            get
            {
                lock (gate) return typeface ?? (typeface = new Typeface(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal));
            }
        }

        // whether the browser gets the character itself from this font
        public abstract bool Takes(int c);

        // whether the font's file has a glyph for the character (looked up one at a time: listing the map's keys
        // takes WPF a sixth of a second a font)
        public bool Has(int c)
        {
            lock (gate)
            {
                if (glyphs == null)
                    glyphs = Typeface.TryGetGlyphTypeface(out GlyphTypeface face) ? face.CharacterToGlyphMap : new Dictionary<int, ushort>();
                return glyphs.ContainsKey(c);
            }
        }

        // How the font draws one text element (a character with its marks), the way HarfBuzz, the browser's text
        // shaper, gets it from a font (from a web font's face, only the characters in its range): a lone character as
        // it is, else split into its letter and accent where the font has both (the latin faces carry U+0304, U+0308
        // and U+0329 for that, so "ā" comes from them as "a" and a macron, kerned with the letters around it); the
        // characters of a longer element split first, else as they are; then a letter joined again with the accent
        // after it where the font has the joined letter (a mark after one that didn't join isn't tried). A character
        // HarfBuzz hides when the font hasn't got it (a joiner, a variation selector, a tag) is drawn as it is and
        // never sends the element on, but U+FE0F after an emoji asks for its colour picture, which only a font that
        // has U+FE0F draws. The characters to draw, or null when one is left without a glyph: then the whole element
        // comes from the next font the browser tries. Half of a surrogate pair or a noncharacter has no glyph in any
        // font, so its element goes on to the last font, as it is. With `boxes`, a character left without a glyph
        // stays as it is, for the font to draw as its empty box (CaptionFont.Pick says when).
        public string Spell(string element, bool boxes = false)
        {
            List<int> points = Unicode.Points(element);
            if (!points.TrueForAll(Unicode.IsCharacter)) return null;
            bool alone = points.Count == 1, emoji = Unicode.EmojiBase(points[0]);
            var drawn = new List<(int Point, bool Glyph)>();
            foreach (int c in points)
            {
                if (alone && Takes(c)) drawn.Add((c, true));
                else if (!Split(c, alone, drawn)) drawn.Add((c, Takes(c) || (Unicode.Hidden(c) && !(emoji && c == Unicode.EmojiStyle))));
            }
            int starter = 0;
            for (int i = 1; i < drawn.Count; i++)
            {
                if (!Unicode.IsMark(drawn[i].Point))
                {
                    starter = i;
                    continue;
                }
                if (starter == i - 1 && Unicode.Joins(drawn[starter].Point, drawn[i].Point, out int joined) && Takes(joined))
                {
                    drawn[starter] = (joined, true);
                    drawn.RemoveAt(i--);
                }
            }
            var spelt = new StringBuilder(element.Length + 2);
            foreach (var (point, glyph) in drawn)
            {
                if (!glyph && !boxes) return null;
                spelt.Append(char.ConvertFromUtf32(point));
            }
            return spelt.ToString();
        }

        // HarfBuzz's split of a character: its letter and last accent when the font has the accent, the letter as it
        // is (first, for a lone character) or split again
        bool Split(int ab, bool shortest, List<(int Point, bool Glyph)> drawn)
        {
            if (!Unicode.Splits(ab, out int a, out int b) || (b != 0 && !Takes(b))) return false;
            bool hasLetter = Takes(a);
            if ((shortest && hasLetter) || !Split(a, shortest, drawn))
            {
                if (!hasLetter) return false;
                drawn.Add((a, true));
            }
            if (b != 0) drawn.Add((b, true));
            return true;
        }
    }

    // One @font-face of a web font: a file inside the exe, and the characters the website draws with it (its
    // unicode-range).
    sealed class WebFontFace : CaptionFace
    {
        public readonly string Subset, File, Inside;   // Inside: the family's name in the file, as Windows gives it
        public WebFont Font { get; internal set; }
        readonly int[] from, to;
        readonly object gate = new object();
        FontFamily family;

        public WebFontFace(string subset, string file, string inside, string range)
        {
            Subset = subset;
            File = file;
            Inside = inside;
            string[] parts = range.Split(',');   // "U+0000-00FF,U+0131,..."
            from = new int[parts.Length];
            to = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string[] ends = parts[i].Trim().Substring(2).Split('-');
                from[i] = int.Parse(ends[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                to[i] = int.Parse(ends[ends.Length - 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
        }

        public bool InRange(int c)
        {
            for (int i = 0; i < from.Length; i++)
                if (c >= from[i] && c <= to[i]) return true;
            return false;
        }

        // the characters of its range, in order
        public IEnumerable<int> Characters
        {
            get
            {
                for (int i = 0; i < from.Length; i++)
                    for (int c = from[i]; c <= to[i]; c++) yield return c;
            }
        }

        // The face as WPF draws it, read from the exe's own resources. Windows names a font by its STAT table too
        // (Merriweather's files are "Merriweather 18pt" to it); a Windows that doesn't knows the file by its plain
        // name, the second choice.
        public override FontFamily Family
        {
            get
            {
                lock (gate)
                {
                    if (family == null)
                    {
                        string at = "./fonts/" + File + "#";
                        family = new FontFamily(WebFonts.Inside, at + Inside + (Inside == Font.Name ? "" : ", " + at + Font.Name));
                    }
                    return family;
                }
            }
        }

        // in its range, and in its file (a range can name characters the file hasn't got)
        public override bool Takes(int c) => InRange(c) && Has(c);
    }

    // a font on this PC, or the one a generic family is on Windows: it gives every character it has
    sealed class PcFace : CaptionFace
    {
        static readonly Dictionary<string, PcFace> named = new Dictionary<string, PcFace>(StringComparer.OrdinalIgnoreCase);
        readonly FontFamily family;

        PcFace(string name)
        {
            family = new FontFamily(name);
        }

        public static PcFace Named(string name)
        {
            lock (named)
            {
                if (!named.TryGetValue(name, out PcFace face)) named[name] = face = new PcFace(name);
                return face;
            }
        }

        public override FontFamily Family => family;
        public override bool Takes(int c) => Has(c);
    }

    // Unicode's canonical decompositions and compositions, one step at a time as HarfBuzz takes them, from .NET's
    // normalisation; and how a text's characters hold together, as Unicode's grapheme clusters, by Unicode 17's
    // categories (UnicodeTables.cs), never .NET's older ones
    static partial class Unicode
    {
        public const int Zwj = 0x200D, EmojiStyle = 0xFE0F, TextStyle = 0xFE0E;   // the zero-width joiner; variation selectors 16 and 15

        // A character's decomposition into a letter and one accent (b: 0 for a character that stands for another,
        // like the ångström sign for Å), or false. The accent is the last one in the decomposed form, and the letter
        // what the rest makes together: the order Unicode builds its composed characters in. Half of a surrogate pair
        // and a noncharacter have none (.NET refuses to normalise them).
        public static bool Splits(int ab, out int a, out int b)
        {
            a = b = 0;
            if (!IsCharacter(ab)) return false;
            string s = char.ConvertFromUtf32(ab);
            string apart = s.Normalize(NormalizationForm.FormD);
            if (apart == s) return false;
            string joined = s.Normalize(NormalizationForm.FormC);
            if (joined != s && Points(joined).Count == 1)
            {
                a = char.ConvertToUtf32(joined, 0);
                return true;
            }
            List<int> points = Points(joined != s ? joined : apart);
            b = points[points.Count - 1];
            var rest = new StringBuilder();
            for (int i = 0; i < points.Count - 1; i++) rest.Append(char.ConvertFromUtf32(points[i]));
            List<int> letter = Points(rest.ToString().Normalize(NormalizationForm.FormC));
            if (letter.Count != 1) return false;
            a = letter[0];
            return true;
        }

        // the character a letter and an accent make together, if any
        public static bool Joins(int a, int b, out int joined)
        {
            joined = 0;
            if (!IsCharacter(a) || !IsCharacter(b)) return false;
            string letter = char.ConvertFromUtf32(a);
            string together = (letter + char.ConvertFromUtf32(b)).Normalize(NormalizationForm.FormC);
            if (letter.Normalize(NormalizationForm.FormC) != letter || Points(together).Count != 1) return false;
            joined = char.ConvertToUtf32(together, 0);
            return true;
        }

        // a mark (Mn, Mc or Me); none comes before U+0300
        public static bool IsMark(int c) => c >= 0x300 && In(Marks, c);

        // a spacing mark (Mc); none comes before U+0903
        static bool IsSpacingMark(int c) => c >= 0x903 && In(SpacingMarks, c);

        // a code point Unicode hasn't given a character yet (none comes before U+0378)
        static bool IsUnassigned(int c) => c >= 0x378 && In(Unassigned, c);

        // Whether a code point is a character a font can draw: not half of a surrogate pair (half an emoji) nor one
        // of the 66 noncharacters (U+FDD0 to U+FDEF, and U+FFFE and U+FFFF in every plane), which text may hold (a
        // design file's caption, a paste) but which mean nothing, and which .NET won't normalise.
        public static bool IsCharacter(int c) => c >= 0 && c <= 0x10FFFF && (c < 0xD800 || c > 0xDFFF) && (c < 0xFDD0 || c > 0xFDEF) && (c & 0xFFFE) != 0xFFFE;

        public static List<int> Points(string s)
        {
            var points = new List<int>(s.Length);
            for (int i = 0; i < s.Length; i++)
                points.Add(char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s[i], s[++i]) : s[i]);
            return points;
        }

        // The text's characters as they hold together, Unicode's extended grapheme clusters (UAX #29) as a caption
        // meets them, which Chromium's text shaper (HarfBuzz) keeps together too when it picks a font: marks, U+200C,
        // variation selectors and skin tones on the character before them, emoji joined by a ZWJ (U+200D) into one,
        // a flag's two letters, a flag's tags after its black flag; nothing on a control character or half of a
        // surrogate pair. (Hangul's syllables, Indic conjuncts and the prepended marks, which HarfBuzz doesn't hold
        // together either, are left apart.) A character keeps at most `mostMarks` of the characters on it in a row
        // (Joining); the rest are left out (CaptionFont.MostMarks says why).
        public static List<string> Clusters(string text, int mostMarks)
        {
            var clusters = new List<string>();
            var cluster = new StringBuilder();
            int last = -1, marks = 0;   // marks: the characters on the letter so far (Joining)
            bool pictograph = false, joiner = false, flag = false;   // a pictograph and its marks so far; then a ZWJ; a flag's first letter
            foreach (int c in Points(text))
            {
                if (!Joining(c)) marks = 0;
                else if (last >= 0 && !IsControl(last) && ++marks > mostMarks)
                {
                    last = c;
                    continue;
                }
                bool clings = Clings(c);
                bool join = last >= 0 && !IsControl(last) && !IsControl(c) && (clings || (joiner && IsPictograph(c)) || (flag && IsRegional(c)));
                if (!join && cluster.Length > 0)
                {
                    clusters.Add(cluster.ToString());
                    cluster.Clear();
                }
                last = c;
                if (clings)
                {
                    joiner = pictograph && c == Zwj;
                    pictograph = pictograph && c != Zwj && !IsSpacingMark(c);
                }
                else
                {
                    pictograph = IsPictograph(c);
                    joiner = false;
                }
                flag = !join && IsRegional(c);
                if (c > 0xFFFF) cluster.Append(char.ConvertFromUtf32(c));
                else cluster.Append((char)c);   // half of a surrogate pair too, as the one char it was
            }
            if (cluster.Length > 0) clusters.Add(cluster.ToString());
            return clusters;
        }

        // what holds on to the character before it: a mark, U+200C, a variation selector, a skin tone or a tag
        // (Unicode's Extend), the ZWJ, or a spacing mark
        static bool Clings(int c) =>
            c >= 0x300 && (c == Zwj || c == 0x200C || (c >= 0x1F3FB && c <= 0x1F3FF) || (c >= 0xE0020 && c <= 0xE007F) || c == 0xFF9E || c == 0xFF9F || IsMark(c));

        // What counts as one of the characters on a letter, which a letter keeps at most so many of in a row: what
        // clings to it, and a code point Unicode hasn't given a character yet. That one is a character of its own to
        // Windows and Chromium today (so it's a cluster of its own for the fonts), but a later Windows may know it
        // as a mark, as Windows 11 knows U+1AC1, which .NET's older Unicode calls unassigned.
        static bool Joining(int c) => Clings(c) || IsUnassigned(c);

        // what nothing holds on to and that holds on to nothing: control and format characters (but the joiners and
        // tags), line and paragraph separators, and half of a surrogate pair
        static bool IsControl(int c) =>
            c < 0x20 || (c >= 0x7F && c <= 0x9F) || c == 0x2028 || c == 0x2029 || (c >= 0xD800 && c <= 0xDFFF) ||
            (c >= 0xAD && c != Zwj && c != 0x200C && (c < 0xE0020 || c > 0xE007F) && In(Formats, c));

        static bool IsRegional(int c) => c >= 0x1F1E6 && c <= 0x1F1FF;

        // Unicode's Extended_Pictographic (emoji-data.txt, as Unicode 17 has it): the characters a ZWJ joins
        static readonly int[] Pictographs =
        {
            0xA9, 0xA9, 0xAE, 0xAE, 0x203C, 0x203C, 0x2049, 0x2049, 0x2122, 0x2122, 0x2139, 0x2139, 0x2194, 0x2199, 0x21A9, 0x21AA, 0x231A, 0x231B,
            0x2328, 0x2328, 0x23CF, 0x23CF, 0x23E9, 0x23F3, 0x23F8, 0x23FA, 0x24C2, 0x24C2, 0x25AA, 0x25AB, 0x25B6, 0x25B6, 0x25C0, 0x25C0, 0x25FB, 0x25FE,
            0x2600, 0x2604, 0x260E, 0x260E, 0x2611, 0x2611, 0x2614, 0x2615, 0x2618, 0x2618, 0x261D, 0x261D, 0x2620, 0x2620, 0x2622, 0x2623, 0x2626, 0x2626,
            0x262A, 0x262A, 0x262E, 0x262F, 0x2638, 0x263A, 0x2640, 0x2640, 0x2642, 0x2642, 0x2648, 0x2653, 0x265F, 0x2660, 0x2663, 0x2663, 0x2665, 0x2666,
            0x2668, 0x2668, 0x267B, 0x267B, 0x267E, 0x267F, 0x2692, 0x2697, 0x2699, 0x2699, 0x269B, 0x269C, 0x26A0, 0x26A1, 0x26A7, 0x26A7, 0x26AA, 0x26AB,
            0x26B0, 0x26B1, 0x26BD, 0x26BE, 0x26C4, 0x26C5, 0x26C8, 0x26C8, 0x26CE, 0x26CF, 0x26D1, 0x26D1, 0x26D3, 0x26D4, 0x26E9, 0x26EA, 0x26F0, 0x26F5,
            0x26F7, 0x26FA, 0x26FD, 0x26FD, 0x2702, 0x2702, 0x2705, 0x2705, 0x2708, 0x270D, 0x270F, 0x270F, 0x2712, 0x2712, 0x2714, 0x2714, 0x2716, 0x2716,
            0x271D, 0x271D, 0x2721, 0x2721, 0x2728, 0x2728, 0x2733, 0x2734, 0x2744, 0x2744, 0x2747, 0x2747, 0x274C, 0x274C, 0x274E, 0x274E, 0x2753, 0x2755,
            0x2757, 0x2757, 0x2763, 0x2764, 0x2795, 0x2797, 0x27A1, 0x27A1, 0x27B0, 0x27B0, 0x27BF, 0x27BF, 0x2934, 0x2935, 0x2B05, 0x2B07, 0x2B1B, 0x2B1C,
            0x2B50, 0x2B50, 0x2B55, 0x2B55, 0x3030, 0x3030, 0x303D, 0x303D, 0x3297, 0x3297, 0x3299, 0x3299, 0x1F004, 0x1F004, 0x1F02C, 0x1F02F, 0x1F094, 0x1F09F,
            0x1F0AF, 0x1F0B0, 0x1F0C0, 0x1F0C0, 0x1F0CF, 0x1F0D0, 0x1F0F6, 0x1F0FF, 0x1F170, 0x1F171, 0x1F17E, 0x1F17F, 0x1F18E, 0x1F18E, 0x1F191, 0x1F19A,
            0x1F1AE, 0x1F1E5, 0x1F201, 0x1F20F, 0x1F21A, 0x1F21A, 0x1F22F, 0x1F22F, 0x1F232, 0x1F23A, 0x1F23C, 0x1F23F, 0x1F249, 0x1F25F, 0x1F266, 0x1F321,
            0x1F324, 0x1F393, 0x1F396, 0x1F397, 0x1F399, 0x1F39B, 0x1F39E, 0x1F3F0, 0x1F3F3, 0x1F3F5, 0x1F3F7, 0x1F3FA, 0x1F400, 0x1F4FD, 0x1F4FF, 0x1F53D,
            0x1F549, 0x1F54E, 0x1F550, 0x1F567, 0x1F56F, 0x1F570, 0x1F573, 0x1F57A, 0x1F587, 0x1F587, 0x1F58A, 0x1F58D, 0x1F590, 0x1F590, 0x1F595, 0x1F596,
            0x1F5A4, 0x1F5A5, 0x1F5A8, 0x1F5A8, 0x1F5B1, 0x1F5B2, 0x1F5BC, 0x1F5BC, 0x1F5C2, 0x1F5C4, 0x1F5D1, 0x1F5D3, 0x1F5DC, 0x1F5DE, 0x1F5E1, 0x1F5E1,
            0x1F5E3, 0x1F5E3, 0x1F5E8, 0x1F5E8, 0x1F5EF, 0x1F5EF, 0x1F5F3, 0x1F5F3, 0x1F5FA, 0x1F64F, 0x1F680, 0x1F6C5, 0x1F6CB, 0x1F6D2, 0x1F6D5, 0x1F6E5,
            0x1F6E9, 0x1F6E9, 0x1F6EB, 0x1F6F0, 0x1F6F3, 0x1F6FF, 0x1F7DA, 0x1F7FF, 0x1F80C, 0x1F80F, 0x1F848, 0x1F84F, 0x1F85A, 0x1F85F, 0x1F888, 0x1F88F,
            0x1F8AE, 0x1F8AF, 0x1F8BC, 0x1F8BF, 0x1F8C2, 0x1F8CF, 0x1F8D9, 0x1F8FF, 0x1F90C, 0x1F93A, 0x1F93C, 0x1F945, 0x1F947, 0x1F9FF, 0x1FA58, 0x1FA5F,
            0x1FA6E, 0x1FAFF, 0x1FC00, 0x1FFFD,
        };

        // Unicode's Emoji_Presentation and Emoji_Modifier_Base (emoji-data.txt, as Unicode 17 has them): the characters
        // Chromium draws as emoji pictures on their own, without U+FE0F. It counts a hand or a person that takes a skin
        // tone as one too where Unicode's default is text (☝ ✌ ✍ ⛹ 🏋 🏌 🕴 🕵 🖐), but not a flag's letter or a skin
        // tone on its own, which it draws as text.
        static readonly int[] Pictures =
        {
            0x231A, 0x231B, 0x23E9, 0x23EC, 0x23F0, 0x23F0, 0x23F3, 0x23F3, 0x25FD, 0x25FE, 0x2614, 0x2615, 0x261D, 0x261D, 0x2648, 0x2653, 0x267F, 0x267F,
            0x2693, 0x2693, 0x26A1, 0x26A1, 0x26AA, 0x26AB, 0x26BD, 0x26BE, 0x26C4, 0x26C5, 0x26CE, 0x26CE, 0x26D4, 0x26D4, 0x26EA, 0x26EA, 0x26F2, 0x26F3,
            0x26F5, 0x26F5, 0x26F9, 0x26FA, 0x26FD, 0x26FD, 0x2705, 0x2705, 0x270A, 0x270D, 0x2728, 0x2728, 0x274C, 0x274C, 0x274E, 0x274E, 0x2753, 0x2755,
            0x2757, 0x2757, 0x2795, 0x2797, 0x27B0, 0x27B0, 0x27BF, 0x27BF, 0x2B1B, 0x2B1C, 0x2B50, 0x2B50, 0x2B55, 0x2B55, 0x1F004, 0x1F004, 0x1F0CF, 0x1F0CF,
            0x1F18E, 0x1F18E, 0x1F191, 0x1F19A, 0x1F201, 0x1F201, 0x1F21A, 0x1F21A, 0x1F22F, 0x1F22F, 0x1F232, 0x1F236, 0x1F238, 0x1F23A, 0x1F250, 0x1F251, 0x1F300, 0x1F320,
            0x1F32D, 0x1F335, 0x1F337, 0x1F37C, 0x1F37E, 0x1F393, 0x1F3A0, 0x1F3CC, 0x1F3CF, 0x1F3D3, 0x1F3E0, 0x1F3F0, 0x1F3F4, 0x1F3F4, 0x1F3F8, 0x1F3FA, 0x1F400, 0x1F43E,
            0x1F440, 0x1F440, 0x1F442, 0x1F4FC, 0x1F4FF, 0x1F53D, 0x1F54B, 0x1F54E, 0x1F550, 0x1F567, 0x1F574, 0x1F575, 0x1F57A, 0x1F57A, 0x1F590, 0x1F590, 0x1F595, 0x1F596,
            0x1F5A4, 0x1F5A4, 0x1F5FB, 0x1F64F, 0x1F680, 0x1F6C5, 0x1F6CC, 0x1F6CC, 0x1F6D0, 0x1F6D2, 0x1F6D5, 0x1F6D8, 0x1F6DC, 0x1F6DF, 0x1F6EB, 0x1F6EC, 0x1F6F4, 0x1F6FC,
            0x1F7E0, 0x1F7EB, 0x1F7F0, 0x1F7F0, 0x1F90C, 0x1F93A, 0x1F93C, 0x1F945, 0x1F947, 0x1F9FF, 0x1FA70, 0x1FA7C, 0x1FA80, 0x1FA8A, 0x1FA8E, 0x1FAC6, 0x1FAC8, 0x1FAC8,
            0x1FACD, 0x1FADC, 0x1FADF, 0x1FAEA, 0x1FAEF, 0x1FAF8,
        };

        public static bool IsPictograph(int c) => In(Pictographs, c);

        static bool In(int[] ranges, int c)
        {
            int low = 0, high = ranges.Length / 2 - 1;
            while (low <= high)
            {
                int middle = (low + high) / 2;
                if (c < ranges[2 * middle]) high = middle - 1;
                else if (c > ranges[2 * middle + 1]) low = middle + 1;
                else return true;
            }
            return false;
        }

        // a character U+FE0F asks to be drawn as an emoji's colour picture: a pictograph, or a keycap's digit, # or *
        public static bool EmojiBase(int c) => IsPictograph(c) || c == '#' || c == '*' || (c >= '0' && c <= '9');

        // Whether a cluster is an emoji that asks for its colour picture, as Chromium's emoji presentation has it: a
        // pictograph or keycap base with U+FE0F, a skin tone, a ZWJ or a flag's tags; a flag's two letters; or, without
        // U+FE0E, a character shown as a picture on its own (Pictures).
        public static bool AsksForEmoji(string cluster)
        {
            List<int> points = Points(cluster);
            if (points.Count == 0) return false;
            if (IsRegional(points[0])) return points.Count > 1 && IsRegional(points[1]);
            return (EmojiBase(points[0]) && points.Exists(c => c == EmojiStyle || c == Zwj || (c >= 0x1F3FB && c <= 0x1F3FF) || (c >= 0xE0020 && c <= 0xE007F))) ||
                   (In(Pictures, points[0]) && !points.Contains(TextStyle));
        }

        // Whether a cluster is an emoji Chromium draws as text, in a font Windows gives it rather than as the emoji
        // font's picture: a pictograph with U+FE0E or with nothing asking for its picture, a skin tone on its own, or
        // a keycap without U+FE0F (a digit, # or * and U+20E3).
        public static bool AsText(string cluster)
        {
            List<int> points = Points(cluster);
            if (points.Count == 0 || AsksForEmoji(cluster)) return false;
            int c = points[0];
            return IsPictograph(c) || (c >= 0x1F3FB && c <= 0x1F3FF) || (EmojiBase(c) && points.Count > 1 && points[1] == 0x20E3);
        }

        // The characters HarfBuzz hides (draws as nothing) when a font hasn't got them: Unicode's default ignorables
        // (joiners, variation selectors, tags, direction marks, the soft hyphen...) but the Hangul fillers and the
        // shorthand format controls, which it draws as fonts do.
        public static bool Hidden(int c) =>
            c == 0xAD || c == 0x34F || c == 0x61C || (c >= 0x17B4 && c <= 0x17B5) || (c >= 0x180B && c <= 0x180F) || (c >= 0x200B && c <= 0x200F) ||
            (c >= 0x202A && c <= 0x202E) || (c >= 0x2060 && c <= 0x206F) || (c >= 0xFE00 && c <= 0xFE0F) || c == 0xFEFF || (c >= 0xFFF0 && c <= 0xFFF8) ||
            (c >= 0x1D173 && c <= 0x1D17A) || (c >= 0xE0000 && c <= 0xE0FFF);

        // WPF's text layout ends the whole program — no exception, nothing to catch it (exit code
        // 0x80000003) — when a line holds a letter with about 720 marks, joiners or variation selectors on
        // it, or several letters with 600 or so each; a text box or block that shows such text does the
        // same. A caption is already drawn through Clusters for this reason; every other text that came
        // from outside the program (a scanned code, an opened file, a paste, a drop, a template's field) is
        // passed through here before it's shown, so none of it can end the program. It keeps at most
        // `mostMarks` of the characters on a letter in a row and leaves the rest out, by the very same rule
        // as a caption (Clusters, the one idea of what's on a letter: Joining). The string itself comes
        // back, nothing allocated, when no run is over the limit.
        public static string Capped(string text, int mostMarks)
        {
            if (text == null || !OverTheLimit(text, mostMarks)) return text;
            var kept = new StringBuilder(text.Length);
            foreach (string cluster in Clusters(text, mostMarks)) kept.Append(cluster);
            return kept.ToString();
        }

        // Whether Clusters would leave any character out: a letter with more than `mostMarks` characters
        // on it in a row (Joining; the first after a control character or at the start, which has nothing
        // to be on, isn't counted), counted character by character, allocating nothing (Clusters itself
        // builds the shorter text when it would). An SVG picture's text over its own limit is refused.
        public static bool OverTheLimit(string text, int mostMarks) => OverTheLimit(text, 0, text.Length, mostMarks);

        // The same count over part of a text, from `start` up to `end`, which begins where a letter does (as Around gives
        // it): a text box checks the letters around a change, not all of its text.
        public static bool OverTheLimit(string text, int start, int end, int mostMarks)
        {
            int last = -1, marks = 0;
            for (int i = start; i < end; i++)
            {
                int c = char.IsSurrogatePair(text, i) ? char.ConvertToUtf32(text, i) : text[i];
                if (c > 0xFFFF) i++;
                if (!Joining(c)) marks = 0;
                else if (last >= 0 && !IsControl(last) && ++marks > mostMarks) return true;
                last = c;
            }
            return false;
        }

        // The part of a text whose letters a change from `from` to `to` can put more on: back over what's on the letter
        // before it to that letter (or the start), and on over what's after it, which the change's last letter takes on.
        // Every letter outside it keeps what it had, so in a text within the limit only this part can go over it.
        public static void Around(string text, int from, int to, out int start, out int end)
        {
            start = Math.Max(0, Math.Min(from, text.Length));
            end = Math.Max(start, Math.Min(to, text.Length));
            if (start > 0 && start < text.Length && char.IsSurrogatePair(text[start - 1], text[start])) start--;   // never half an emoji
            if (end > 0 && end < text.Length && char.IsSurrogatePair(text[end - 1], text[end])) end++;
            while (start > 0)
            {
                bool pair = start > 1 && char.IsSurrogatePair(text[start - 2], text[start - 1]);
                int c = pair ? char.ConvertToUtf32(text[start - 2], text[start - 1]) : text[start - 1];
                start -= pair ? 2 : 1;
                if (!Joining(c)) break;   // the letter it's all on
            }
            while (end < text.Length)
            {
                int c = char.IsSurrogatePair(text, end) ? char.ConvertToUtf32(text, end) : text[end];
                if (!Joining(c)) break;
                end += c > 0xFFFF ? 2 : 1;
            }
        }

        // The letters from `start` up to `end` (as Around gives it) with exactly `mostMarks` of the characters on them in a
        // row, as a cap leaves a letter it cut short: each as the index of the character they're on (or of the first of
        // them, with nothing before them to be on).
        public static void AtTheLimit(string text, int start, int end, int mostMarks, List<int> letters)
        {
            int last = -1, marks = 0, letter = start;
            for (int i = start; i < end; i++)
            {
                int at = i;
                int c = char.IsSurrogatePair(text, i) ? char.ConvertToUtf32(text, i) : text[i];
                if (c > 0xFFFF) i++;
                if (!Joining(c))
                {
                    if (marks == mostMarks) letters.Add(letter);
                    marks = 0;
                    letter = at;
                }
                else if (last >= 0 && !IsControl(last)) marks++;
                last = c;
            }
            if (marks == mostMarks) letters.Add(letter);
        }

        // What can go into a text in place of its part from `at` to `to` (a paste or a drop) without putting
        // more than `mostMarks` of the characters on a letter in a row: `added` itself, the same string, when it fits; else
        // with what would go over left out, its letters carrying on from what's on the letter before it, and leaving room for
        // what's after it that its last letter takes on. The text around it is within the limit, as a text box's is.
        public static string Insertable(string text, int at, int to, string added, int mostMarks)
        {
            if (string.IsNullOrEmpty(added)) return added;
            bool joins = false;
            for (int i = 0; i < added.Length && !joins; i++)
            {
                int c = char.IsSurrogatePair(added, i) ? char.ConvertToUtf32(added, i) : added[i];
                if (c > 0xFFFF) i++;
                joins = Joining(c);
            }
            if (!joins) return added;   // letters, spaces and lines alone never put more on a letter
            Around(text, at, at, out int start, out _);
            Around(text, to, to, out _, out int end);
            string before = text.Substring(start, Math.Max(0, at - start)), after = text.Substring(Math.Min(to, text.Length), Math.Max(0, end - to));
            if (!OverTheLimit(before + added + after, mostMarks)) return added;
            // a letter keeps its first marks: those before it are kept, so what's left out is the added text's
            string capped = Capped(before + added, mostMarks);
            if (!capped.StartsWith(before, StringComparison.Ordinal)) return added;   // over the limit already: the text box's guard sees to it
            string kept = capped.Substring(before.Length);
            while (kept.Length > 0 && OverTheLimit(before + kept + after, mostMarks))
            {
                bool pair = kept.Length > 1 && char.IsSurrogatePair(kept[kept.Length - 2], kept[kept.Length - 1]);
                if (!Joining(pair ? char.ConvertToUtf32(kept[kept.Length - 2], kept[kept.Length - 1]) : kept[kept.Length - 1])) break;   // its last letter has none to give up
                kept = kept.Substring(0, kept.Length - (pair ? 2 : 1));
            }
            return kept;
        }
    }

    static partial class WebFonts
    {
        // The exe's own resources, where the fonts are, in the form that names the assembly (so the tests exe finds
        // them too). An application pack address only ever reads from inside the running program.
        const string Resources = "pack://application:,,,/b0r3dQR;component/";
        static Uri inside;

        internal static Uri Inside
        {
            get
            {
                if (inside == null)
                {
                    _ = Application.Current;   // sets up WPF's application first, which teaches .NET the pack address
                    inside = new Uri(Resources);
                }
                return inside;
            }
        }

        // the website's web font of that name (CSS names ignore case), or null
        public static WebFont Named(string family)
        {
            foreach (WebFont font in All)
                if (string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase)) return font;
            return null;
        }
    }

    // A caption's font, picked from a CSS family list the way the website's page picks it. Each name in turn is one of
    // the website's web fonts (from inside the exe, so before an installed font of the same name, as a page's
    // @font-face comes first), a font installed on this PC, or a generic family as Chromium has them on Windows; a
    // character comes from the first of them that draws it, a web font's last @font-face first, as the browser
    // checks them, then from Times New Roman, Chromium's standard font, which it tries after the list. A list with
    // none of them is drawn in Arial, as before (Chromium would use Times New Roman); what none draws is left to
    // Windows (an emoji shown as a picture to Segoe UI Emoji first, and one drawn as text to Segoe UI Symbol, as
    // Chromium does).
    sealed class CaptionFont
    {
        // the website's page font (src/style.css), the default caption's in its preview. The page doesn't load its
        // Inter web font for itself (only a caption in Inter does), so it's Inter only when installed, else Segoe UI.
        internal const string PageFont = "Inter, system-ui, Avenir, Helvetica, Arial, sans-serif";
        const string StandardFont = "Times New Roman", EmojiFont = "Segoe UI Emoji", SymbolFont = "Segoe UI Symbol";

        // WPF ends the whole program (exit code 0x80000003, without a word) when it lays out a letter with about 720
        // marks on it (half as many of those past U+FFFF, which take two chars each), or several letters with 600 or
        // so each; a text box showing the text does the same. A caption keeps a letter's first 100 (of what Unicode 17
        // counts as on a letter: Unicode.Joining) and leaves the rest out: far more than any script needs (Unicode's
        // Stream-Safe Text Format allows 30 in a row), and heaps of marks look the same past that.
        internal const int MostMarks = 100;

        public readonly IReadOnlyList<CaptionFace> Faces;   // in the order the browser tries them

        CaptionFont(List<CaptionFace> faces)
        {
            Faces = faces;
        }

        internal static CaptionFont Page => For(PageFont, false);

        public static CaptionFont For(string css, bool webFonts = true)
        {
            var faces = new List<CaptionFace>();
            foreach (string part in (css ?? "").Split(','))
            {
                string name = part.Trim().Trim('\'', '"').Trim();
                if (name.Length == 0) continue;
                WebFont web = webFonts ? WebFonts.Named(name) : null;
                if (web != null)
                {
                    for (int f = web.Faces.Length - 1; f >= 0; f--) faces.Add(web.Faces[f]);
                    continue;
                }
                // a font on this PC by its name, never a name WPF would read as a place to load a font from (anything
                // before a '#') or a path: the name is a design file's or a CSV's text
                string family = Generic(name) ?? (name.IndexOfAny(new[] { '#', '/', '\\' }) < 0 && QrArt.Installed(name) ? name : null);
                if (family != null && !faces.Contains(PcFace.Named(family))) faces.Add(PcFace.Named(family));
            }
            if (!(faces.Count > 0 && faces[faces.Count - 1] is PcFace)) faces.Add(PcFace.Named("Arial"));
            if (!faces.Contains(PcFace.Named(StandardFont))) faces.Add(PcFace.Named(StandardFont));
            return new CaptionFont(faces);
        }

        // CSS's generic families as Chromium has them on Windows, or null
        internal static string Generic(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "sans-serif": return "Arial";
                case "serif": return "Times New Roman";
                case "monospace": return "Consolas";
                case "cursive": return "Comic Sans MS";
                case "system-ui": return "Segoe UI";
                default: return null;
            }
        }

        // the text in this font at `size` px, each character (a letter with its marks) from the font that draws it
        public FormattedText Text(string text, double size, Brush ink, CultureInfo culture)
        {
            List<(string Element, CaptionFace Face, string Spelt)> picked = Pick(text);
            var drawn = new StringBuilder(text.Length + 4);
            var runs = new List<(int At, CaptionFace Face)>();
            foreach (var (_, face, spelt) in picked)
            {
                if (runs.Count == 0 || runs[runs.Count - 1].Face.Family != face.Family) runs.Add((drawn.Length, face));
                drawn.Append(spelt);
            }
            var formatted = new FormattedText(drawn.ToString(), culture, FlowDirection.LeftToRight, (runs.Count > 0 ? runs[0].Face : Faces[0]).Typeface, size, ink, 1.0);
            for (int i = 1; i < runs.Count; i++)
                formatted.SetFontTypeface(runs[i].Face.Typeface, runs[i].At, (i + 1 < runs.Count ? runs[i + 1].At : drawn.Length) - runs[i].At);
            return formatted;
        }

        // Each character of the text (a letter with its marks, an emoji sequence: Unicode.Clusters), the font that
        // draws it and the characters it draws it with (CaptionFace.Spell). As Chromium does, each font in turn gets
        // what the ones before it couldn't draw, but a web font's face only when its range has one of those
        // characters: so a lone "ǖ" never reaches Bebas Neue's latin face (which could draw it as "ü" and a macron)
        // and comes from Arial; and Segoe UI Emoji in the list is passed over for an emoji with U+FE0E, as Chromium
        // passes over a colour font for it. What none draws goes to the last, for Windows to find a font for, as the
        // browser asks Windows; but an emoji shown as a picture (a ZWJ sequence, a skin tone, a flag, a keycap, an
        // emoji with U+FE0F, or a character that is a picture on its own, like ⭐) comes from Segoe UI Emoji when it has
        // it, the font Chromium turns to for those before Windows; and an emoji drawn as text (with U+FE0E, a pictograph
        // that isn't a picture on its own, like ❤, a skin tone alone, a keycap without U+FE0F) from Segoe UI Symbol, the
        // font Windows gives Chromium for those (WPF's own choice is often Segoe UI Emoji's picture), else from Segoe UI
        // Emoji when only it has it, as Windows does. And a letter with a mark no font of the list has (U+1AC1, a mark
        // no font on Windows has yet), where the last font has the letter, so Windows finds no better one either:
        // Chromium draws it in the first font that has the letter, the mark as that font's empty box, and so does this.
        internal List<(string Element, CaptionFace Face, string Spelt)> Pick(string text)
        {
            var picked = new List<(string Element, CaptionFace Face, string Spelt)>();
            foreach (string cluster in Unicode.Clusters(text, MostMarks)) picked.Add((cluster, null, null));
            var waiting = new List<int>();
            for (int i = 0; i < picked.Count; i++) waiting.Add(i);
            bool Textual(string element) => element.IndexOf((char)Unicode.TextStyle) >= 0 && Unicode.AsText(element);
            foreach (CaptionFace face in Faces)
            {
                if (waiting.Count == 0) break;
                if (face is WebFontFace web && !waiting.Exists(i => Unicode.Points(picked[i].Element).Exists(web.InRange))) continue;
                bool colour = face == PcFace.Named(EmojiFont);
                waiting.RemoveAll(i =>
                {
                    string spelt = colour && Textual(picked[i].Element) ? null : face.Spell(picked[i].Element);
                    if (spelt != null) picked[i] = (picked[i].Element, face, spelt);
                    return spelt != null;
                });
            }
            CaptionFace emoji = waiting.Count > 0 && QrArt.Installed(EmojiFont) ? PcFace.Named(EmojiFont) : null;
            CaptionFace symbols = waiting.Count > 0 && QrArt.Installed(SymbolFont) ? PcFace.Named(SymbolFont) : null;
            foreach (int i in waiting)
            {
                string element = picked[i].Element;
                CaptionFace from = null;
                string spelt = null;
                bool asks = Unicode.AsksForEmoji(element), asText = !asks && Unicode.AsText(element);
                foreach (CaptionFace face in asks ? new[] { emoji } : asText ? new[] { symbols, emoji } : new CaptionFace[0])
                {
                    spelt = face?.Spell(element);
                    from = face;
                    if (spelt != null) break;
                }
                int letter = Unicode.Points(element)[0];
                if (spelt == null && !asks && !asText && Unicode.IsCharacter(letter) && Faces[Faces.Count - 1].Takes(letter))
                {
                    from = Faces[Faces.Count - 1];
                    for (int f = Faces.Count - 2; f >= 0; f--)
                        if (Faces[f].Takes(letter)) from = Faces[f];
                    spelt = from.Spell(element, true);
                }
                picked[i] = spelt != null ? (element, from, spelt) : (element, Faces[Faces.Count - 1], element);
            }
            return picked;
        }
    }
}
