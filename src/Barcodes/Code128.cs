using System.Collections.Generic;
using System.Text;

namespace B0r3dQR.Barcodes
{
    // Code 128 and GS1-128 as zint 2.16 makes them (code128.c; BSD-3-Clause, see THIRD-PARTY-NOTICES.txt):
    // the fewest symbol characters over Code Sets A, B and C and their extended (FNC4) forms, found the
    // way zint finds them, with its ties settled the same way, so the bars match the website's.
    static class Code128
    {
        const int MaxSymbolChars = 102;
        const int MaxInput = 256;

        // Code Set states: A, B and C, and A and B for the extended range (FNC4)
        const int A0 = 1, B0 = 2, A1 = 3, B1 = 4, C0 = 5, C1 = 6, States = 7;

        static bool IsAB0(int set) => set <= B0;
        static bool IsC(int set) => set >= C0;
        static bool IsA(int set) => (set & 1) != 0;

        // the codewords that latch from one state to another, and what starting in each costs
        static readonly int[][][] Latch =
        {
            new int[States][],
            new[] { null, new int[0], new[] { 100 }, new[] { 101, 101 }, new[] { 100, 100, 100 }, new[] { 99 }, new[] { 101, 101, 99 } },
            new[] { null, new[] { 101 }, new int[0], new[] { 101, 101, 101 }, new[] { 100, 100 }, new[] { 99 }, new[] { 100, 100, 99 } },
            new[] { null, new[] { 101, 101 }, new[] { 100, 100, 100 }, new int[0], new[] { 100 }, new[] { 101, 101, 99 }, new[] { 99 } },
            new[] { null, new[] { 101, 101, 101 }, new[] { 100, 100 }, new[] { 101 }, new int[0], new[] { 100, 100, 99 }, new[] { 99 } },
            new[] { null, new[] { 101 }, new[] { 100 }, new[] { 101, 101, 101 }, new[] { 100, 100, 100 }, new int[0], null },
            new[] { null, new[] { 101, 101, 101 }, new[] { 100, 100, 100 }, new[] { 101 }, new[] { 100 }, null, new int[0] },
        };

        static readonly int[][] Start =
        {
            new[] { 0, 1, 1, 3, 3, 1, 64 },   // plain
            new[] { 0, 2, 2, 4, 4, 2, 64 },   // GS1: FNC1 after the start
        };

        static readonly int[][][] StartCodewords =
        {
            new[] { null, new[] { 103 }, new[] { 104 }, new[] { 103, 101, 101 }, new[] { 104, 100, 100 }, new[] { 105 }, null },
            new[] { null, new[] { 103, 102 }, new[] { 104, 102 }, new[] { 103, 102, 101, 101 }, new[] { 104, 102, 100, 100 }, new[] { 105, 102 }, null },
        };

        static int LatchLength(int from, int to) => Latch[from][to] == null ? 64 : Latch[from][to].Length;

        // Code 128 from text whose characters are all Latin-1 (zint has turned it into those bytes)
        public static ZintSymbol Make(string text, out string error)
        {
            if (text.Length > MaxInput) return Refuse(out error, "Input length " + text.Length + " too long (maximum " + MaxInput + ")");
            bool haveA = false, haveB = false, haveC = false, haveExtended = false, digit = false;
            foreach (char c in text)
            {
                int mask = c & 0x60;
                haveExtended |= c >= 0x80;
                haveA |= mask == 0;
                haveB |= mask == 0x60;
                bool wasDigit = digit;
                digit = c >= '0' && c <= '9';
                haveC |= wasDigit && digit;
            }
            List<int> values = Values(text, false, Priority(haveA, haveB, haveC, haveExtended), out int count);
            if (values == null) return Refuse(out error, "Input too long, requires " + count + " symbol characters (maximum " + MaxSymbolChars + ")");
            ZintSymbol symbol = Expand(Symbology.Code128, values);
            // the text under the bars: control characters and the C1 range as spaces
            var hrt = new StringBuilder(text.Length);
            foreach (char c in text) hrt.Append(c < 32 || c == 127 || (c >= 0x80 && c < 0xA0) ? ' ' : c);
            symbol.Text = hrt.ToString();
            error = null;
            return symbol;
        }

        // GS1-128 from GS1 data in brackets, its AIs checked by Gs1.Reduce
        public static ZintSymbol Gs1(string data, out string error)
        {
            // GS1 data reaches zint as the UTF-8 it was given, so its length is counted in those bytes
            int length = Barcode.Utf8Length(data);
            if (length > MaxInput) return Refuse(out error, "Input length " + length + " too long (maximum " + MaxInput + ")");
            string reduced = Barcodes.Gs1.Reduce(data, false, out error);
            if (reduced == null) return null;
            List<int> values = Values(reduced, true, Priority(false, true, true, false), out int count);
            if (values == null) return Refuse(out error, "Input too long, requires " + count + " symbol characters (maximum " + MaxSymbolChars + ")");
            ZintSymbol symbol = Expand(Symbology.Gs1_128, values);
            symbol.Text = data;
            return symbol;
        }

        // the states to try, in zint's order (which settles its ties)
        static int[] Priority(bool haveA, bool haveB, bool haveC, bool haveExtended)
        {
            var priority = new List<int>();
            if (haveC) priority.Add(C0);
            if (haveB || !haveA) priority.Add(B0);
            if (haveA) priority.Add(A0);
            if (haveExtended)
            {
                if (haveC) priority.Add(C1);
                if (haveB || !haveA) priority.Add(B1);
                if (haveA) priority.Add(A1);
            }
            return priority.ToArray();
        }

        // The symbol characters (start included, check character and stop not), or null when there'd
        // be more than 102 (`count` says how many). In GS1 data every GS is an FNC1.
        static List<int> Values(string source, bool gs1, int[] priority, out int count)
        {
            var search = new Search { Source = source, Gs1 = gs1, Priority = priority, Costs = new int[source.Length, States], Modes = new int[source.Length, States] };
            count = search.Cost(0, 0);
            if (count > MaxSymbolChars) return null;

            var values = new List<int>(count);
            int set = 0;
            int start = gs1 ? 1 : 0;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                bool fnc1 = gs1 && c == '\x1D';
                int mode = search.Modes[i, set];
                int prior = set;
                set = mode & 0x0F;
                if (set != prior) values.AddRange(prior == 0 ? StartCodewords[start][set] : Latch[prior][set]);
                if (mode >= 0x30) values.AddRange(new[] { 100 + (IsA(set) ? 1 : 0), 98 });   // FNC4, SHIFT
                else if (mode >= 0x20) values.Add(100 + (IsA(set) ? 1 : 0));                  // FNC4
                else if (mode >= 0x10) values.Add(98);                                        // SHIFT
                if (fnc1) values.Add(102);
                else if (IsC(set)) values.Add((c - '0') * 10 + source[++i] - '0');
                else values.Add((c & 0x7F) + ((c & 0x60) == 0 ? 96 : 0) - 32);
            }
            return values;
        }

        // zint's c128_cost: the fewest symbol characters for the rest of the text from each point and
        // state, remembered as it goes (divide and conquer with memoization)
        sealed class Search
        {
            public string Source;
            public bool Gs1;
            public int[] Priority;
            public int[,] Costs, Modes;

            public int Cost(int i, int prior)
            {
                char c = Source[i];
                bool fnc1 = Gs1 && c == '\x1D';
                char next = i + 1 < Source.Length ? Source[i + 1] : '\0';
                bool canC = fnc1 || (c >= '0' && c <= '9' && next >= '0' && next <= '9');
                int best = 999999, bestMode = 0;
                foreach (int set in Priority)
                {
                    int step = IsC(set) ? 2 - (fnc1 ? 1 : 0) : 1;
                    if (IsC(set) && !canC) continue;
                    int mode = IsC(set) ? prior : set;
                    int cost = IsC(set) || fnc1 ? 1 : CostAB(set, c, ref mode);
                    if (prior != set)
                    {
                        cost += prior == 0 ? Start[Gs1 ? 1 : 0][set] : LatchLength(prior, set);
                        if (IsC(set)) mode = set;
                    }
                    if (i + step < Source.Length) cost += Costs[i + step, set] != 0 ? Costs[i + step, set] : Cost(i + step, set);
                    if (cost < best)
                    {
                        best = cost;
                        bestMode = mode;
                    }
                }
                Costs[i, prior] = best;
                Modes[i, prior] = bestMode;
                return best;
            }

            // a character in A or B: a SHIFT for one from the other set, an FNC4 for one from the other range
            static int CostAB(int set, char c, ref int mode)
            {
                int mask = c & 0x60, cost = 1;
                bool a = IsA(set);
                if ((a && mask == 0x60) || (!a && mask == 0))
                {
                    cost++;
                    mode |= 0x10;
                }
                if (IsAB0(set) == (c >= 0x80))
                {
                    cost++;
                    mode |= 0x20;
                }
                return cost;
            }
        }

        // the bars: the symbol characters, the check character and the stop
        static ZintSymbol Expand(Symbology symbology, List<int> values)
        {
            var widths = new StringBuilder();
            int sum = values[0];
            widths.Append(Table[values[0]]);
            for (int i = 1; i < values.Count; i++)
            {
                widths.Append(Table[values[i]]);
                sum += values[i] * i;
            }
            widths.Append(Table[sum % 103]).Append("2331112");
            var symbol = new ZintSymbol(symbology);
            symbol.Expand(widths.ToString());
            symbol.SetHeight(0, 50);
            return symbol;
        }

        static ZintSymbol Refuse(out string error, string message)
        {
            error = message;
            return null;
        }

        // ISO/IEC 15417 Table 1
        static readonly string[] Table =
        {
            "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
            "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
            "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
            "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
            "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
            "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
            "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
            "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
            "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
            "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
            "114131", "311141", "411131", "211412", "211214", "211232",
        };
    }
}
