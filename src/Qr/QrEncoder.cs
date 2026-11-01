using System;
using System.Collections.Generic;
using System.Text;
using ZXing.Common;
using ZXing.Common.ReedSolomon;
using ZXing.QrCode.Internal;
using ZxingVersion = ZXing.QrCode.Internal.Version;

namespace B0r3dQR.Qr
{
    // One part of a code's data in one of QR's modes: digits, capitals and digits, or bytes.
    sealed class QrSegment
    {
        public readonly QrMode Mode;
        public readonly string Text;

        public QrSegment(QrMode mode, string text)
        {
            Mode = mode;
            Text = text;
        }
    }

    enum QrMode { Numeric, Alphanumeric, Byte }

    // Turns text into a QR code's modules exactly as qr.b0r3d.org does, so a design moved between
    // the website and the PC comes out module for module the same (its tests carry codes made by
    // the website itself):
    // - Plain ASCII is split into digit, capital-letter and byte parts, whichever adds up to the
    //   fewest bits for the version's size group, ties settled the website's way; any other
    //   character makes the whole text one part of UTF-8 bytes.
    // - The version is the smallest that holds it, from the one asked for, if any.
    // - Of the eight masks, the one the website's encoder (qrcode-generator) scores lowest: its own
    //   scoring, done before the format and version information are drawn in. ISO's scoring, which
    //   ZXing uses, sometimes picks another, equally valid, mask.
    // Codes with the UTF-8 marker the website has zint make (in zxing-wasm), so for them b0r3d QR
    // follows zint's rules instead (Marked, below; its tests carry codes zint made for the website).
    // ZXing.Net supplies the standard parts: the version tables, Reed-Solomon and placing the
    // modules for a chosen mask.
    static class QrEncoder
    {
        // the bits for each mode's character count in versions 1-9, 10-26 and 27-40
        static readonly int[,] CountBits = { { 10, 12, 14 }, { 9, 11, 13 }, { 8, 16, 16 } };
        // sixths of a bit a character costs in each mode, so 10 bits per 3 digits and 11 per 2 capitals stay whole
        static readonly int[] CharSixths = { 20, 33, 48 };
        const string Alphanumerics = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
        // the order the website tries the modes in, which settles ties
        static readonly QrMode[] Order = { QrMode.Byte, QrMode.Alphanumeric, QrMode.Numeric };

        public sealed class Result
        {
            public bool[] Dark;    // row by row
            public int Version, Size;
            public List<QrSegment> Segments;
            public int Mask;
        }

        static int Group(int version) => version <= 9 ? 0 : version <= 26 ? 1 : 2;

        static bool CanHold(QrMode mode, char c) =>
            mode == QrMode.Byte || (mode == QrMode.Numeric ? c >= '0' && c <= '9' : Alphanumerics.IndexOf(c) >= 0);

        // ---------- the parts ----------

        // The cheapest split of plain ASCII into parts for a size group: costs in sixths of a bit,
        // each part's rounded up to whole bits when the next part starts. On a tie the open part
        // carries on, a new part prefers to follow a byte part, then capitals, then digits, and
        // the text prefers to end in bytes, then capitals, then digits.
        internal static List<QrSegment> Split(string text, int group)
        {
            int n = text.Length;
            var segments = new List<QrSegment>();
            if (n == 0) return segments;
            int Head(QrMode m) => (4 + CountBits[(int)m, group]) * 6;
            const long Never = long.MaxValue / 4;
            // open[m]: the least it costs to have written the characters so far with mode m's part open
            var open = new long[3];
            foreach (QrMode m in Order) open[(int)m] = Head(m);
            var cameFrom = new QrMode[n, 3];   // the mode each character was written in, on each open mode's best path
            for (int i = 0; i < n; i++)
            {
                var kept = new long[3];
                var next = new long[3];
                foreach (QrMode m in Order)
                {
                    kept[(int)m] = CanHold(m, text[i]) ? open[(int)m] + CharSixths[(int)m] : Never;
                    next[(int)m] = kept[(int)m];
                    cameFrom[i, (int)m] = m;
                }
                foreach (QrMode to in Order)
                    foreach (QrMode from in Order)
                    {
                        if (kept[(int)from] >= Never) continue;
                        long switched = (kept[(int)from] + 5) / 6 * 6 + Head(to);
                        if (switched < next[(int)to])
                        {
                            next[(int)to] = switched;
                            cameFrom[i, (int)to] = from;
                        }
                    }
                open = next;
            }
            QrMode state = Order[0];
            foreach (QrMode m in Order)
                if (open[(int)m] < open[(int)state]) state = m;
            var modes = new QrMode[n];
            for (int i = n - 1; i >= 0; i--)
            {
                modes[i] = cameFrom[i, (int)state];
                state = modes[i];
            }
            int start = 0;
            for (int i = 1; i <= n; i++)
            {
                if (i < n && modes[i] == modes[start]) continue;
                segments.Add(new QrSegment(modes[start], text.Substring(start, i - start)));
                start = i;
            }
            return segments;
        }

        static bool PlainAscii(string text)
        {
            foreach (char c in text) if (c > 0x7F) return false;
            return true;
        }

        // ---------- the version ----------

        static int DataCodewords(int version, ErrorCorrectionLevel level)
        {
            int total = 0;
            foreach (ZxingVersion.ECB block in ZxingVersion.getVersionForNumber(version).getECBlocksForLevel(level).getECBlocks()) total += block.Count * block.DataCodewords;
            return total;
        }

        static int Length(QrSegment s) => s.Mode == QrMode.Byte ? Encoding.UTF8.GetByteCount(s.Text) : s.Text.Length;

        static int DataBits(QrSegment s)
        {
            int length = Length(s);
            switch (s.Mode)
            {
                case QrMode.Numeric: return length / 3 * 10 + (length % 3 == 2 ? 7 : length % 3 == 1 ? 4 : 0);
                case QrMode.Alphanumeric: return length / 2 * 11 + (length % 2) * 6;
                default: return length * 8;
            }
        }

        // the bits of the parts in a version (mode, count and data; no terminator)
        static int Bits(List<QrSegment> segments, int version)
        {
            int bits = 0;
            foreach (QrSegment s in segments) bits += 4 + CountBits[(int)s.Mode, Group(version)] + DataBits(s);
            return bits;
        }

        static bool Fits(List<QrSegment> segments, int version, ErrorCorrectionLevel level) =>
            Bits(segments, version) <= DataCodewords(version, level) * 8;

        // The website's search: the version asked for, if it holds the text; otherwise, size group by
        // size group, the smallest version that holds that group's split, kept when it lands in the
        // group (or the last). -1 when nothing holds it.
        static int ChooseVersion(string text, ErrorCorrectionLevel level, int least, out List<QrSegment> chosen)
        {
            bool ascii = PlainAscii(text);
            List<QrSegment> For(int group) => ascii ? Split(text, group) : new List<QrSegment> { new QrSegment(QrMode.Byte, text) };
            chosen = null;
            if (least > 0)
            {
                List<QrSegment> asked = For(Group(least));
                if (Fits(asked, least, level))
                {
                    chosen = asked;
                    return least;
                }
            }
            for (int group = Group(Math.Max(least, 1)); group <= 2; group++)
            {
                List<QrSegment> segments = For(group);
                int version = 1;
                while (version < 40 && !Fits(segments, version, level)) version++;
                if (!Fits(segments, version, level)) return -1;   // too much even for version 40
                if (Group(version) <= group || group == 2)
                {
                    chosen = segments;
                    return version;
                }
            }
            return -1;
        }

        // ---------- the bits ----------

        static BitArray DataStream(List<QrSegment> segments, int version, ErrorCorrectionLevel level, bool eci)
        {
            var bits = new BitArray();
            if (eci)
            {
                bits.appendBits(0x7, 4);   // ECI
                bits.appendBits(26, 8);    // UTF-8
            }
            foreach (QrSegment s in segments)
            {
                bits.appendBits(s.Mode == QrMode.Numeric ? 1 : s.Mode == QrMode.Alphanumeric ? 2 : 4, 4);
                bits.appendBits(Length(s), CountBits[(int)s.Mode, Group(version)]);
                switch (s.Mode)
                {
                    case QrMode.Numeric:
                        for (int i = 0; i < s.Text.Length; i += 3)
                        {
                            int digits = Math.Min(3, s.Text.Length - i);
                            bits.appendBits(int.Parse(s.Text.Substring(i, digits), System.Globalization.CultureInfo.InvariantCulture), digits == 3 ? 10 : digits == 2 ? 7 : 4);
                        }
                        break;
                    case QrMode.Alphanumeric:
                        for (int i = 0; i < s.Text.Length; i += 2)
                        {
                            int first = Alphanumerics.IndexOf(s.Text[i]);
                            if (i + 1 < s.Text.Length) bits.appendBits(first * 45 + Alphanumerics.IndexOf(s.Text[i + 1]), 11);
                            else bits.appendBits(first, 6);
                        }
                        break;
                    default:
                        foreach (byte b in Encoding.UTF8.GetBytes(s.Text)) bits.appendBits(b, 8);
                        break;
                }
            }
            int capacity = DataCodewords(version, level) * 8;
            if (bits.Size + 4 <= capacity) bits.appendBits(0, 4);   // the terminator, when all of it fits
            while (bits.Size % 8 != 0) bits.appendBit(false);
            for (int pad = 0; bits.Size < capacity; pad++) bits.appendBits(pad % 2 == 0 ? 0xEC : 0x11, 8);
            return bits;
        }

        // the data split into the version's blocks, each with its Reed-Solomon bytes, interleaved
        static BitArray Interleave(BitArray data, int version, ErrorCorrectionLevel level)
        {
            ZxingVersion.ECBlocks blocks = ZxingVersion.getVersionForNumber(version).getECBlocksForLevel(level);
            int ecPerBlock = blocks.ECCodewordsPerBlock;
            var bytes = new byte[data.SizeInBytes];
            data.toBytes(0, bytes, 0, bytes.Length);
            var dataBlocks = new List<int[]>();
            var ecBlocks = new List<int[]>();
            var encoder = new ReedSolomonEncoder(GenericGF.QR_CODE_FIELD_256);
            int at = 0;
            foreach (ZxingVersion.ECB group in blocks.getECBlocks())
                for (int k = 0; k < group.Count; k++)
                {
                    var block = new int[group.DataCodewords + ecPerBlock];
                    for (int i = 0; i < group.DataCodewords; i++) block[i] = bytes[at++];
                    encoder.encode(block, ecPerBlock);
                    var d = new int[group.DataCodewords];
                    var e = new int[ecPerBlock];
                    Array.Copy(block, 0, d, 0, d.Length);
                    Array.Copy(block, d.Length, e, 0, ecPerBlock);
                    dataBlocks.Add(d);
                    ecBlocks.Add(e);
                }
            var result = new BitArray();
            int longest = 0;
            foreach (int[] d in dataBlocks) longest = Math.Max(longest, d.Length);
            for (int i = 0; i < longest; i++)
                foreach (int[] d in dataBlocks)
                    if (i < d.Length) result.appendBits(d[i], 8);
            for (int i = 0; i < ecPerBlock; i++)
                foreach (int[] e in ecBlocks) result.appendBits(e[i], 8);
            return result;
        }

        // ---------- the modules ----------

        public static Result Encode(string text, ErrorCorrectionLevel level, int leastVersion, bool eci)
        {
            if (eci) return Marked(text, level, leastVersion);
            int version = ChooseVersion(text, level, leastVersion, out List<QrSegment> segments);
            if (version < 0) return null;
            BitArray final = Interleave(DataStream(segments, version, level, false), version, level);
            ZxingVersion v = ZxingVersion.getVersionForNumber(version);
            int size = v.DimensionForVersion;
            int best = 0;
            double lowest = 0;
            for (int mask = 0; mask < 8; mask++)
            {
                var trial = new ByteMatrix(size, size);
                MatrixUtil.buildMatrix(final, level, v, mask, trial);
                Blank(trial, version);
                double score = Score(trial);
                if (mask == 0 || score < lowest)
                {
                    lowest = score;
                    best = mask;
                }
            }
            return Finished(final, level, v, best, segments);
        }

        static Result Finished(BitArray final, ErrorCorrectionLevel level, ZxingVersion v, int mask, List<QrSegment> segments)
        {
            int size = v.DimensionForVersion;
            var matrix = new ByteMatrix(size, size);
            MatrixUtil.buildMatrix(final, level, v, mask, matrix);
            var dark = new bool[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    dark[y * size + x] = matrix[x, y] == 1;
            return new Result { Dark = dark, Version = v.VersionNumber, Size = size, Segments = segments, Mask = mask };
        }

        // The website's encoder scores each mask with the format information, the version
        // information and the always-dark module left light.
        static void Blank(ByteMatrix m, int version)
        {
            int n = m.Width;
            for (int i = 0; i <= 8; i++)
            {
                if (i != 6)
                {
                    m[i, 8] = 0;   // row 8, the first copy's columns
                    m[8, i] = 0;   // column 8, the first copy's rows
                }
            }
            for (int i = 0; i < 8; i++) m[n - 1 - i, 8] = 0;   // row 8, the second copy
            for (int i = 0; i < 7; i++) m[8, n - 1 - i] = 0;   // column 8, the second copy
            m[8, n - 8] = 0;                                    // the dark module
            if (version >= 7)
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 3; j++)
                    {
                        m[n - 11 + j, i] = 0;
                        m[i, n - 11 + j] = 0;
                    }
        }

        // qrcode-generator's mask score: crowded modules, 2x2 blocks of one colour, finder-like
        // runs, and how far the dark share is from half
        static double Score(ByteMatrix m)
        {
            int n = m.Width;
            int points = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool dark = m[x, y] == 1;
                    int same = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (y + dy < 0 || y + dy >= n) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (x + dx < 0 || x + dx >= n || (dx == 0 && dy == 0)) continue;
                            if ((m[x + dx, y + dy] == 1) == dark) same++;
                        }
                    }
                    if (same > 5) points += 3 + same - 5;
                }
            for (int y = 0; y < n - 1; y++)
                for (int x = 0; x < n - 1; x++)
                {
                    int count = m[x, y] + m[x + 1, y] + m[x, y + 1] + m[x + 1, y + 1];
                    if (count == 0 || count == 4) points += 3;
                }
            bool D(int x, int y) => m[x, y] == 1;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n - 6; x++)
                    if (D(x, y) && !D(x + 1, y) && D(x + 2, y) && D(x + 3, y) && D(x + 4, y) && !D(x + 5, y) && D(x + 6, y)) points += 40;
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n - 6; y++)
                    if (D(x, y) && !D(x, y + 1) && D(x, y + 2) && D(x, y + 3) && D(x, y + 4) && !D(x, y + 5) && D(x, y + 6)) points += 40;
            int darkCount = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (m[x, y] == 1) darkCount++;
            double ratio = Math.Abs(100.0 * darkCount / n / n - 50) / 5;
            return points + ratio * 10;
        }

        // ---------- codes with the UTF-8 marker, made by zint's rules ----------

        // zint's order of the modes, which settles its ties
        static readonly QrMode[] ZintOrder = { QrMode.Numeric, QrMode.Alphanumeric, QrMode.Byte };

        // A code with the UTF-8 marker, as zint makes it for the website:
        // - the text's UTF-8 bytes split into digit, capital-letter and byte parts by zint's costs
        //   (ZintModes);
        // - the smallest version that holds version 40's split, then smaller ones while their own
        //   split still fits; a version asked for is used when it's at least that big (when it's
        //   smaller, zint refuses and the website asks again for any size);
        // - the mask ISO 18004 scores lowest on the finished code, format and version information
        //   included, as zint counts it (IsoScore); ties go to the lower mask.
        static Result Marked(string text, ErrorCorrectionLevel level, int least)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            if (data.Length == 0) return null;
            QrMode[] modes = ZintModes(data, 40);
            int bits = ZintBits(data, modes, 40);
            if (bits > DataCodewords(40, level) * 8) return null;
            int version = 1;
            while (DataCodewords(version, level) * 8 < bits) version++;
            if (version != 40)
            {
                QrMode[] own = ZintModes(data, version);
                int ownBits = ZintBits(data, own, version);
                if (ownBits <= bits)   // zint keeps version 40's split if its own came out longer
                {
                    modes = own;
                    bits = ownBits;
                }
            }
            while (version > 1)
            {
                QrMode[] smaller = ZintModes(data, version - 1);
                int smallerBits = ZintBits(data, smaller, version - 1);
                if (DataCodewords(version - 1, level) * 8 < smallerBits) break;
                version--;
                modes = smaller;
            }
            if (least > version)
            {
                version = least;
                modes = ZintModes(data, least);
            }

            var segments = new List<QrSegment>();
            for (int start = 0, i = 1; i <= data.Length; i++)
            {
                if (i < data.Length && modes[i] == modes[start]) continue;
                // a part only ever starts or ends beside a digit or capital, so it holds whole characters
                segments.Add(new QrSegment(modes[start], Encoding.UTF8.GetString(data, start, i - start)));
                start = i;
            }
            BitArray final = Interleave(DataStream(segments, version, level, true), version, level);
            ZxingVersion v = ZxingVersion.getVersionForNumber(version);
            int size = v.DimensionForVersion;
            int best = 0, lowest = 0;
            for (int mask = 0; mask < 8; mask++)
            {
                var trial = new ByteMatrix(size, size);
                MatrixUtil.buildMatrix(final, level, v, mask, trial);
                int score = IsoScore(trial);
                if (mask == 0 || score < lowest)
                {
                    lowest = score;
                    best = mask;
                }
            }
            return Finished(final, level, v, best, segments);
        }

        static bool IsDigit(byte b) => b >= '0' && b <= '9';

        static bool IsCapital(byte b) => b < 0x80 && Alphanumerics.IndexOf((char)b) >= 0;

        // zint's split of the bytes into modes for a version's size group, cheapest first, in sixths
        // of a bit: digits in threes from the start of a run (20 each in a three, 21 in a pair, 24
        // alone), capitals in pairs (33 each, 36 alone or for the very last byte), bytes 48, and a
        // new part its header, with no rounding to whole bits. A tie keeps the open part; the text
        // prefers to end in digits, then capitals, then bytes.
        internal static QrMode[] ZintModes(byte[] data, int version)
        {
            int n = data.Length, g = Group(version);
            int Head(QrMode m) => (4 + CountBits[(int)m, g]) * 6;
            var open = new long[3];
            foreach (QrMode m in ZintOrder) open[(int)m] = Head(m);
            var cameFrom = new QrMode[n, 3];
            int digitsEnd = 0, digitCost = 0, capitalsEnd = 0, capitalCost = 0;
            for (int i = 0; i < n; i++)
            {
                var next = new long[3];
                var possible = new bool[3];
                bool last = i + 1 == n;

                if (i < digitsEnd) possible[(int)QrMode.Numeric] = true;
                else
                {
                    int count = 0;
                    while (count < 3 && i + count < n && IsDigit(data[i + count])) count++;
                    digitsEnd = count == 0 ? 0 : i + count;
                    digitCost = count == 1 ? 24 : count == 2 ? 21 : 20;
                    possible[(int)QrMode.Numeric] = count > 0;
                }
                if (i < capitalsEnd)
                {
                    capitalCost = last ? 36 : 33;
                    possible[(int)QrMode.Alphanumeric] = true;
                }
                else if (!IsCapital(data[i])) capitalsEnd = 0;
                else
                {
                    bool pair = !last && IsCapital(data[i + 1]);
                    capitalsEnd = pair ? i + 2 : i + 1;
                    capitalCost = pair ? 33 : 36;
                    possible[(int)QrMode.Alphanumeric] = true;
                }
                possible[(int)QrMode.Byte] = true;

                if (possible[(int)QrMode.Numeric]) next[(int)QrMode.Numeric] = open[(int)QrMode.Numeric] + digitCost;
                if (possible[(int)QrMode.Alphanumeric]) next[(int)QrMode.Alphanumeric] = open[(int)QrMode.Alphanumeric] + capitalCost;
                next[(int)QrMode.Byte] = open[(int)QrMode.Byte] + 48;
                for (int m = 0; m < 3; m++) cameFrom[i, m] = (QrMode)m;
                foreach (QrMode to in ZintOrder)
                    foreach (QrMode from in ZintOrder)
                    {
                        if (to == from || !possible[(int)from]) continue;
                        long switched = next[(int)from] + Head(to);
                        if (!possible[(int)to] || switched < next[(int)to])
                        {
                            next[(int)to] = switched;
                            cameFrom[i, (int)to] = from;
                            possible[(int)to] = true;
                        }
                    }
                open = next;
            }
            QrMode state = ZintOrder[0];
            foreach (QrMode m in ZintOrder)
                if (open[(int)m] < open[(int)state]) state = m;
            var modes = new QrMode[n];
            for (int i = n - 1; i >= 0; i--)
            {
                modes[i] = cameFrom[i, (int)state];
                state = modes[i];
            }
            return modes;
        }

        // the bits of the marker and the parts in a version (no terminator)
        static int ZintBits(byte[] data, QrMode[] modes, int version)
        {
            int bits = 12, g = Group(version);
            for (int start = 0, i = 1; i <= data.Length; i++)
            {
                if (i < data.Length && modes[i] == modes[start]) continue;
                int length = i - start;
                bits += 4 + CountBits[(int)modes[start], g];
                switch (modes[start])
                {
                    case QrMode.Numeric: bits += length / 3 * 10 + (length % 3 == 2 ? 7 : length % 3 == 1 ? 4 : 0); break;
                    case QrMode.Alphanumeric: bits += length / 2 * 11 + (length % 2) * 6; break;
                    default: bits += length * 8; break;
                }
                start = i;
            }
            return bits;
        }

        // ISO 18004's mask score, as zint counts it on the finished code: runs of five or more of one
        // colour along the rows and columns (3, and 1 more for each module past five), 2x2 blocks of
        // one colour (3 each), dark-light-dark-dark-dark-light-dark with four light modules (or the
        // edge) before or after it (40 each; the search goes on 4 modules further on), and 10 for
        // each whole 5 % the dark modules are off half
        internal static int IsoScore(ByteMatrix m)
        {
            int n = m.Width, points = 0, dark = 0;
            for (int line = 0; line < n; line++)
                for (int across = 0; across < 2; across++)
                {
                    int run = 0, colour = 2;
                    for (int i = 0; i < n; i++)
                    {
                        int here = across == 0 ? m[i, line] : m[line, i];
                        if (across == 0 && here == 1) dark++;
                        if (here == colour) run++;
                        else
                        {
                            if (run >= 5) points += run - 2;
                            run = 1;
                            colour = here;
                        }
                    }
                    if (run >= 5) points += run - 2;
                }
            for (int x = 0; x < n - 1; x++)
                for (int y = 0; y < n - 1; y++)
                {
                    int k = m[x, y];
                    if (k == m[x + 1, y] && k == m[x, y + 1] && k == m[x + 1, y + 1]) points += 3;
                }
            for (int line = 0; line < n; line++)
                for (int across = 0; across < 2; across++)
                {
                    bool D(int i) => (across == 0 ? m[i, line] : m[line, i]) == 1;
                    for (int i = 0; i <= n - 7; i++)
                    {
                        if (!(D(i) && !D(i + 1) && D(i + 2) && D(i + 3) && D(i + 4) && !D(i + 5) && D(i + 6))) continue;
                        if (Light(D, i - 1, -1, n) || Light(D, i + 7, 1, n)) points += 40;
                        i += 3;
                    }
                }
            points += 10 * (10 * Math.Abs(2 * dark - n * n) / (n * n));
            return points;
        }

        // four light modules from `from` going `step`, the edge counting as light
        static bool Light(Func<int, bool> dark, int from, int step, int n)
        {
            for (int k = 0, i = from; k < 4; k++, i += step)
            {
                if (i < 0 || i >= n) return true;
                if (dark(i)) return false;
            }
            return true;
        }
    }
}
