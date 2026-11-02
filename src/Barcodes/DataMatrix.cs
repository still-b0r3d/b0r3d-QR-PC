// Data Matrix (ECC 200) encoder. It follows zint 2.16.0's backend/dmatrix.c (Copyright (C) 2009-2025 Robin
// Stuart and the zint contributors, BSD-3-Clause; the licence is in THIRD-PARTY-NOTICES.txt), with the
// tables of dmatrix.h and the parts of backend/reedsol.c that it relies on (library.c's and eci.c's parts
// are in Barcode.cs and Eci.cs).

using System;
using System.Globalization;
using System.Text;

namespace B0r3dQR.Barcodes
{
    // Turns text or GS1 data into a Data Matrix code's modules exactly as qr.b0r3d.org does, so a code
    // made on the website and one made here match module for module (the tests carry codes made by
    // the website itself). The website has zint (inside zxing-wasm) make the code with:
    // - the smallest symbol size that holds the data, rectangular and DMRE sizes included unless the
    //   code has to be square;
    // - zint's default encodation, the minimal one (ASCII, C40, Text, X12, EDIFACT and Base 256 mixed
    //   by the cheapest path through the data, not the ISO look-ahead that FAST_MODE would give);
    // - ISO interleaving of the Reed-Solomon blocks of the 144x144 size;
    // - text as UTF-8, which zint turns into ISO 8859-1 when it can; when it can't, into the first of a
    //   list of code pages that holds all of it, announced with an ECI, or else sends the UTF-8 itself
    //   (ECI 26);
    // - GS1 data with FNC1 (never GS) between fields.
    // zint splits input into segments by ECI; the website never gives it more than one, so there is
    // always exactly one segment and it is the last.
    static class DataMatrix
    {
        const int DmAscii = 1, DmC40 = 2, DmText = 3, DmX12 = 4, DmEdifact = 5, DmBase256 = 6;
        const int NumModes = 6;
        const int MaxSegmentLength = 3116;   // two digits to a codeword in the 1558 of the largest symbol
        const int MaxCodewords = 1558;
        const int RsBlockSpace = 256;
        const int TargetSize = 2200;         // the most codewords zint's buffer holds: 1558 of data and 620 of check

        // The rows of modules (dark = true), no quiet zone, or null with the website's message when zint refuses.
        // For text, `data` is the text as typed. For GS1, it's the GS1 data already checked and reduced the way
        // zint_gs1_verify (gs1.c) leaves it: AIs and values, with GS (0x1D) between a value and the next AI
        // unless that AI's length is predefined.
        // The checks zint makes first, whatever the kind of code (library.c: at most 17,400 bytes, real UTF-8
        // for text, one leading byte order mark dropped), are the caller's, so none is made here.
        public static bool[][] Encode(string data, bool gs1, bool squareOnly, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(data))
            {
                // zxing-cpp's own check, before zint is asked
                error = "Can not create a barcode from NULL or empty data";
                return null;
            }
            int eci = 0;
            byte[] source = gs1 ? Encoding.UTF8.GetBytes(data) : Eci.ToCharacterSet(data, out eci);
            return EncodeBytes(source, eci, gs1, squareOnly, out error);
        }

        // ---------- tables (dmatrix.h) ----------

        // The C40 shift to use per ASCII character (Table C.1)
        static readonly int[] C40Shift =
        {
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            0, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 2, 2, 2,
            2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 2, 2,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3
        };

        // The C40 value of each ASCII character (Table C.1)
        static readonly int[] C40Value =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
            16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31,
            3, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
            4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15, 16, 17, 18, 19, 20,
            21, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28,
            29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 22, 23, 24, 25, 26,
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
            16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31
        };

        // The Text shift to use per ASCII character (Table C.2)
        static readonly int[] TextShift =
        {
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            0, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 2, 2, 2,
            2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2,
            3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 3, 3, 3, 3
        };

        // The Text value of each ASCII character (Table C.2)
        static readonly int[] TextValue =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
            16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31,
            3, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
            4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 15, 16, 17, 18, 19, 20,
            21, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
            16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 22, 23, 24, 25, 26,
            0, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28,
            29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 27, 28, 29, 30, 31
        };

        // The 48 symbol sizes in order of data codewords (Table 7), with the data codewords of each; the
        // tables after this one list more about the same sizes, in the same order. A size is chosen by
        // walking down the list, so of two sizes that hold the same, the first is used and the second
        // (8x18, 8x48, 12x36, 8x80, 20x36, 8x120 and 16x64) never is.
        static readonly int[] MatrixBytes =
        {
            3, 5, 5, 8, 10, 12, 16, 18, 18, 22, 22, 24,
            30, 32, 32, 36, 38, 43, 44, 44, 49, 49, 56, 62,
            62, 63, 64, 70, 72, 80, 84, 86, 90, 108, 114, 118,
            144, 174, 204, 280, 368, 456, 576, 696, 816, 1050, 1304, 1558
        };

        // Rows of the symbol (zint calls these H)
        static readonly int[] MatrixH =
        {
            10, 12, 8, 14, 8, 16, 12, 18, 8, 20, 12, 8,
            22, 16, 8, 24, 8, 12, 26, 20, 16, 8, 20, 32,
            16, 8, 12, 26, 22, 24, 20, 36, 26, 24, 40, 26,
            44, 48, 52, 64, 72, 80, 88, 96, 104, 120, 132, 144
        };

        // Columns of the symbol (W)
        static readonly int[] MatrixW =
        {
            10, 12, 18, 14, 32, 16, 26, 18, 48, 20, 36, 64,
            22, 36, 80, 24, 96, 64, 26, 36, 48, 120, 44, 32,
            64, 144, 88, 40, 48, 48, 64, 36, 48, 64, 40, 64,
            44, 48, 52, 64, 72, 80, 88, 96, 104, 120, 132, 144
        };

        // Rows of a data region with its finder pattern (Table 7, data region height + 2)
        static readonly int[] MatrixFH =
        {
            10, 12, 8, 14, 8, 16, 12, 18, 8, 20, 12, 8,
            22, 16, 8, 24, 8, 12, 26, 20, 16, 8, 20, 16,
            16, 8, 12, 26, 22, 24, 20, 18, 26, 24, 20, 26,
            22, 24, 26, 16, 18, 20, 22, 24, 26, 20, 22, 24
        };

        // Columns of a data region with its finder pattern (Table 7, data region width + 2)
        static readonly int[] MatrixFW =
        {
            10, 12, 18, 14, 16, 16, 26, 18, 24, 20, 18, 16,
            22, 18, 20, 24, 24, 16, 26, 18, 24, 20, 22, 16,
            16, 24, 22, 20, 24, 24, 16, 18, 24, 16, 20, 16,
            22, 24, 26, 16, 18, 20, 22, 24, 26, 20, 22, 24
        };

        // Data codewords per Reed-Solomon block
        static readonly int[] MatrixDataBlock =
        {
            3, 5, 5, 8, 10, 12, 16, 18, 18, 22, 22, 24,
            30, 32, 32, 36, 38, 43, 44, 44, 49, 49, 56, 62,
            62, 63, 64, 70, 72, 80, 84, 86, 90, 108, 114, 118,
            144, 174, 102, 140, 92, 114, 144, 174, 136, 175, 163, 156
        };

        // Check codewords per Reed-Solomon block
        static readonly int[] MatrixRsBlock =
        {
            5, 7, 7, 10, 11, 12, 14, 14, 15, 18, 18, 18,
            20, 24, 22, 24, 28, 27, 28, 28, 28, 32, 34, 36,
            36, 36, 36, 38, 38, 41, 42, 42, 42, 46, 48, 50,
            56, 68, 42, 56, 36, 48, 56, 68, 56, 68, 62, 62
        };

        // ---------- placement (Annex F) ----------

        static void PlacementBit(int[] array, int nr, int nc, int r, int c, int p, int b)
        {
            if (r < 0)
            {
                r += nr;
                c += 4 - ((nr + 4) % 8);
            }
            if (c < 0)
            {
                c += nc;
                r += 4 - ((nc + 4) % 8);
            }
            // necessary for DMRE (ISO/IEC 21471:2020 Annex E)
            if (r >= nr) r -= nr;
            array[r * nc + c] = (p << 3) + b;
        }

        static void PlacementBlock(int[] array, int nr, int nc, int r, int c, int p)
        {
            PlacementBit(array, nr, nc, r - 2, c - 2, p, 7);
            PlacementBit(array, nr, nc, r - 2, c - 1, p, 6);
            PlacementBit(array, nr, nc, r - 1, c - 2, p, 5);
            PlacementBit(array, nr, nc, r - 1, c - 1, p, 4);
            PlacementBit(array, nr, nc, r - 1, c - 0, p, 3);
            PlacementBit(array, nr, nc, r - 0, c - 2, p, 2);
            PlacementBit(array, nr, nc, r - 0, c - 1, p, 1);
            PlacementBit(array, nr, nc, r - 0, c - 0, p, 0);
        }

        static void PlacementCornerA(int[] array, int nr, int nc, int p)
        {
            PlacementBit(array, nr, nc, nr - 1, 0, p, 7);
            PlacementBit(array, nr, nc, nr - 1, 1, p, 6);
            PlacementBit(array, nr, nc, nr - 1, 2, p, 5);
            PlacementBit(array, nr, nc, 0, nc - 2, p, 4);
            PlacementBit(array, nr, nc, 0, nc - 1, p, 3);
            PlacementBit(array, nr, nc, 1, nc - 1, p, 2);
            PlacementBit(array, nr, nc, 2, nc - 1, p, 1);
            PlacementBit(array, nr, nc, 3, nc - 1, p, 0);
        }

        static void PlacementCornerB(int[] array, int nr, int nc, int p)
        {
            PlacementBit(array, nr, nc, nr - 3, 0, p, 7);
            PlacementBit(array, nr, nc, nr - 2, 0, p, 6);
            PlacementBit(array, nr, nc, nr - 1, 0, p, 5);
            PlacementBit(array, nr, nc, 0, nc - 4, p, 4);
            PlacementBit(array, nr, nc, 0, nc - 3, p, 3);
            PlacementBit(array, nr, nc, 0, nc - 2, p, 2);
            PlacementBit(array, nr, nc, 0, nc - 1, p, 1);
            PlacementBit(array, nr, nc, 1, nc - 1, p, 0);
        }

        static void PlacementCornerC(int[] array, int nr, int nc, int p)
        {
            PlacementBit(array, nr, nc, nr - 3, 0, p, 7);
            PlacementBit(array, nr, nc, nr - 2, 0, p, 6);
            PlacementBit(array, nr, nc, nr - 1, 0, p, 5);
            PlacementBit(array, nr, nc, 0, nc - 2, p, 4);
            PlacementBit(array, nr, nc, 0, nc - 1, p, 3);
            PlacementBit(array, nr, nc, 1, nc - 1, p, 2);
            PlacementBit(array, nr, nc, 2, nc - 1, p, 1);
            PlacementBit(array, nr, nc, 3, nc - 1, p, 0);
        }

        static void PlacementCornerD(int[] array, int nr, int nc, int p)
        {
            PlacementBit(array, nr, nc, nr - 1, 0, p, 7);
            PlacementBit(array, nr, nc, nr - 1, nc - 1, p, 6);
            PlacementBit(array, nr, nc, 0, nc - 3, p, 5);
            PlacementBit(array, nr, nc, 0, nc - 2, p, 4);
            PlacementBit(array, nr, nc, 0, nc - 1, p, 3);
            PlacementBit(array, nr, nc, 1, nc - 3, p, 2);
            PlacementBit(array, nr, nc, 1, nc - 2, p, 1);
            PlacementBit(array, nr, nc, 1, nc - 1, p, 0);
        }

        // Annex F: where each bit of each codeword goes in the data area (the number is codeword * 8 + bit)
        static void Placement(int[] array, int nr, int nc)
        {
            int r = 4, c = 0, p = 1;
            do
            {
                // check corner
                if (r == nr && c == 0) PlacementCornerA(array, nr, nc, p++);
                if (r == nr - 2 && c == 0 && nc % 4 != 0) PlacementCornerB(array, nr, nc, p++);
                if (r == nr - 2 && c == 0 && nc % 8 == 4) PlacementCornerC(array, nr, nc, p++);
                if (r == nr + 4 && c == 2 && nc % 8 == 0) PlacementCornerD(array, nr, nc, p++);
                // up/right
                do
                {
                    if (r < nr && c >= 0 && array[r * nc + c] == 0) PlacementBlock(array, nr, nc, r, c, p++);
                    r -= 2;
                    c += 2;
                } while (r >= 0 && c < nc);
                r++;
                c += 3;
                // down/left
                do
                {
                    if (r >= 0 && c < nc && array[r * nc + c] == 0) PlacementBlock(array, nr, nc, r, c, p++);
                    r += 2;
                    c -= 2;
                } while (r < nr && c >= 0);
                r += 3;
                c++;
            } while (r < nr || c < nc);
            // unfilled corner
            if (array[nr * nc - 1] == 0) array[nr * nc - 1] = array[nr * nc - nc - 2] = 1;
        }

        // ---------- error correction (dm_ecc, reedsol.c) ----------

        // GF(256) with the polynomial 0x12D: the powers of 2 (the list twice round, to skip a modulo) and their logs
        static readonly byte[] GfAlog = MakeAlog();
        static readonly byte[] GfLogt = MakeLogt(GfAlog);

        static byte[] MakeAlog()
        {
            var alog = new byte[510];
            int p = 1;
            for (int v = 0; v < 255; v++)
            {
                alog[v] = (byte)p;
                alog[255 + v] = (byte)p;
                p <<= 1;
                if ((p & 0x100) != 0) p ^= 0x12D;
            }
            return alog;
        }

        static byte[] MakeLogt(byte[] alog)
        {
            var logt = new byte[256];
            for (int v = 0; v < 255; v++) logt[alog[v]] = (byte)v;
            return logt;
        }

        // The generator polynomial of nsym check bytes, with roots 2^1 to 2^nsym (zint_rs_init_code)
        static byte[] RsPolynomial(int nsym)
        {
            var rspoly = new byte[nsym + 1];
            int index = 1;
            rspoly[0] = 1;
            for (int i = 1; i <= nsym; i++)
            {
                rspoly[i] = 1;
                for (int k = i - 1; k > 0; k--)
                {
                    if (rspoly[k] != 0) rspoly[k] = GfAlog[GfLogt[rspoly[k]] + index];   // multiply the coefficient by 2^index
                    rspoly[k] ^= rspoly[k - 1];                                          // add the coefficient of x^(k-1) * x
                }
                rspoly[0] = GfAlog[GfLogt[rspoly[0]] + index];
                index++;
            }
            return rspoly;
        }

        // The nsym check bytes of the data (zint_rs_encode)
        static void RsEncode(byte[] rspoly, int nsym, int dataLength, byte[] data, byte[] res)
        {
            Array.Clear(res, 0, nsym);
            for (int i = 0; i < dataLength; i++)
            {
                int m = res[nsym - 1] ^ data[i];
                if (m != 0)
                {
                    int logM = GfLogt[m];
                    for (int k = nsym - 1; k > 0; k--)
                        res[k] = (byte)(res[k - 1] ^ (rspoly[k] != 0 ? GfAlog[logM + GfLogt[rspoly[k]]] : 0));
                    res[0] = rspoly[0] != 0 ? GfAlog[logM + GfLogt[rspoly[0]]] : (byte)0;
                }
                else
                {
                    for (int k = nsym - 1; k > 0; k--) res[k] = res[k - 1];
                    res[0] = 0;
                }
            }
            // the check bytes come out last first
            for (int i = 0; i < nsym / 2; i++)
            {
                byte tmp = res[i];
                res[i] = res[nsym - 1 - i];
                res[nsym - 1 - i] = tmp;
            }
        }

        // Calculates the check bytes after the data and interleaves the blocks. The 144x144 size has ten
        // blocks, the last two a byte shorter, which the interleaving takes in its stride (zint's
        // "skew" for the old 144x144 format isn't used: the website asks for DM_ISO_144).
        static void Ecc(byte[] binary, int bytes, int dataBlock, int rsBlock)
        {
            int blocks = (bytes + 2) / dataBlock;
            int rsBlocks = rsBlock * blocks;
            byte[] rspoly = RsPolynomial(rsBlock);
            var buf = new byte[RsBlockSpace];
            var ecc = new byte[RsBlockSpace];
            for (int b = 0; b < blocks; b++)
            {
                int p = 0;
                for (int n = b; n < bytes; n += blocks) buf[p++] = binary[n];
                RsEncode(rspoly, rsBlock, p, buf, ecc);
                p = 0;
                for (int n = b; n < rsBlocks; n += blocks, p++) binary[bytes + n] = ecc[p];
            }
        }

        // ---------- the kinds of character ----------

        static bool IsDigit(int c) => c >= '0' && c <= '9';

        static bool IsUpper(int c) => c >= 'A' && c <= 'Z';

        static bool IsLower(int c) => c >= 'a' && c <= 'z';

        // Is basic (non-shifted) C40?
        static bool IsC40(int c)
        {
            if (c <= '9') return c >= '0' || c == ' ';
            return IsUpper(c);
        }

        // Is basic (non-shifted) TEXT?
        static bool IsTextSet(int c)
        {
            if (c <= '9') return c >= '0' || c == ' ';
            return IsLower(c);
        }

        // Is basic (non-shifted) C40/TEXT?
        static bool IsC40Text(int mode, int c) => mode == DmC40 ? IsC40(c) : IsTextSet(c);

        // Is it valid in the X12 set?
        static bool IsX12(int c) => IsC40(c) || c == 13 || c == '*' || c == '>';

        // Is it valid in the EDIFACT set?
        static bool IsEdifact(int c) => c >= ' ' && c <= '^';

        // ---------- the symbol size ----------

        // The index in the tables of the smallest symbol holding `minimum` codewords (the squares only if
        // `square`). Beyond 1304 there is only 144x144; beyond 1558 it is the first one, which then won't do.
        static int GetSymbolSize(bool square, int minimum)
        {
            if (minimum > 1304) return minimum <= 1558 ? MatrixBytes.Length - 1 : 0;
            int i = minimum >= 62 ? 23 : 0;
            while (minimum > MatrixBytes[i]) i++;
            if (square)
            {
                // skip rectangular symbols
                while (MatrixH[i] != MatrixW[i]) i++;
            }
            return i;
        }

        // Number of codewords remaining in a particular version (may be negative)
        static int CodewordsRemaining(bool square, int tp, int processP)
        {
            int symbolSize = GetSymbolSize(square, tp + processP);   // allow for the remaining data characters
            return MatrixBytes[symbolSize] - tp;
        }

        // Number of C40/TEXT elements needed to encode `input`
        static int C40TextCount(int mode, bool gs1, int input)
        {
            if (gs1 && input == 0x1D) return 2;
            int cnt = 1;
            if ((input & 0x80) != 0)
            {
                cnt += 2;
                input -= 128;
            }
            if ((mode == DmC40 && C40Shift[input] != 0) || (mode == DmText && TextShift[input] != 0)) cnt += 1;
            return cnt;
        }

        // ---------- the minimal encodation (Alex Geller's Dijkstra-based algorithm) ----------

        // One run of characters in one mode, ending at a vertex (the place between two characters)
        struct Edge
        {
            public int Mode;       // 0 while there is no edge here yet
            public int EndMode;    // dm_getEndMode()
            public int From;       // position in the data
            public int Len;
            public int Size;       // codewords so far
            public int Bytes;      // the Base 256 byte count so far
            public int Previous;   // index in the edges of the edge before this one, 0 for none
        }

        // Determine if next 1 to 4 chars are at the end of the data and can be encoded as 1 or 2 ASCII
        // codewords
        static int LastAscii(byte[] source, int length, int from)
        {
            if (length - from > 4 || from >= length) return 0;
            if (length - from == 1) return (source[from] & 0x80) != 0 ? 0 : 1;
            if (length - from == 2)
            {
                if ((source[from] & 0x80) != 0 || (source[from + 1] & 0x80) != 0) return 0;
                if (IsDigit(source[from]) && IsDigit(source[from + 1])) return 1;
                return 2;
            }
            if (length - from == 3)
            {
                if (IsDigit(source[from]) && IsDigit(source[from + 1]) && (source[from + 2] & 0x80) == 0) return 2;
                if (IsDigit(source[from + 1]) && IsDigit(source[from + 2]) && (source[from] & 0x80) == 0) return 2;
                return 0;
            }
            if (IsDigit(source[from]) && IsDigit(source[from + 1]) && IsDigit(source[from + 2]) && IsDigit(source[from + 3])) return 2;
            return 0;
        }

        // Treat EDIFACT edges specially, returning DM_ASCII mode if not full (i.e. encoding < 4 chars), or if
        // full and at the end of the data where 1 or 2 ASCII chars can be encoded
        static int GetEndMode(bool square, byte[] source, int length, int mode, int from, int len, int size)
        {
            if (mode == DmEdifact)
            {
                if (len < 4) return DmAscii;
                int lastAscii = LastAscii(source, length, from + len);
                if (lastAscii != 0)   // at the end with remaining chars ASCII-encodable in 1 or 2 codewords
                {
                    int symbolsLeft = CodewordsRemaining(square, size + lastAscii, 0);
                    // if no codewords left and 1 or 2 ASCII-encodables or 1 codeword left and 1 ASCII-encodable
                    if (symbolsLeft <= 2 - lastAscii) return DmAscii;
                }
            }
            return mode;
        }

        // Return number of C40/TEXT codewords needed to encode characters in full batches of 3 (or less if at
        // the end). The number of characters encoded is returned in `len`.
        static int GetNumberOfC40Words(byte[] source, int length, int from, int mode, out int len)
        {
            int thirdsCount = 0;
            for (int i = from; i < length; i++)
            {
                int ci = source[i];
                if (IsC40Text(mode, ci)) thirdsCount++;                                 // native
                else if ((ci & 0x80) == 0) thirdsCount += 2;                            // shift
                else if (IsC40Text(mode, ci & 0x7F)) thirdsCount += 3;                  // shift, upper shift
                else thirdsCount += 4;                                                  // shift, upper shift, shift

                int remainder = thirdsCount % 3;
                if (remainder == 0 || (remainder == 2 && i + 1 == length))
                {
                    len = i - from + 1;
                    return (thirdsCount + 2) / 3 * 2;
                }
            }
            len = 0;
            return 0;
        }

        // Initialize a new edge. Returns endMode
        static int NewEdge(bool square, byte[] source, int length, Edge[] edges, int mode, int from, int len,
            int previous, out Edge edge, int cwds)
        {
            int previousMode;
            int size;

            edge = new Edge { Mode = mode, EndMode = mode, From = from, Len = len, Bytes = 0 };
            if (previous != 0)
            {
                previousMode = edges[previous].EndMode;
                edge.Previous = previous;
                size = edges[previous].Size;
            }
            else
            {
                previousMode = DmAscii;
                edge.Previous = 0;
                size = 0;
            }

            switch (mode)
            {
                case DmAscii:
                    size++;
                    if ((source[from] & 0x80) != 0) size++;
                    if (previousMode != DmAscii && previousMode != DmBase256) size++;   // unlatch to ASCII
                    break;

                case DmBase256:
                    size++;
                    if (previousMode != DmBase256)
                    {
                        size += 2;   // byte count + latch to BASE256
                        if (previousMode != DmAscii) size++;   // unlatch to ASCII
                        edge.Bytes = 1;
                    }
                    else
                    {
                        edge.Bytes = 1 + edges[previous].Bytes;
                        if (edge.Bytes == 250) size++;   // extra byte count
                    }
                    break;

                case DmC40:
                case DmText:
                    size += cwds;
                    if (previousMode != mode)
                    {
                        size++;   // latch to this mode
                        if (previousMode != DmAscii && previousMode != DmBase256) size++;   // unlatch to ASCII
                    }
                    if (from + len + 2 >= length)   // if less than batch of 3 away from the end
                    {
                        int lastAscii = LastAscii(source, length, from + len);
                        int symbolsLeft = CodewordsRemaining(square, size + lastAscii, 0);
                        if (symbolsLeft > 0) size++;   // we need an extra unlatch at the end
                    }
                    break;

                case DmX12:
                    size += 2;
                    if (previousMode != DmX12)
                    {
                        size++;   // latch to this mode
                        if (previousMode != DmAscii && previousMode != DmBase256) size++;   // unlatch to ASCII
                    }
                    if (from + len + 2 >= length)   // if less than batch of 3 away from the end
                    {
                        int lastAscii = LastAscii(source, length, from + len);
                        if (lastAscii == 2)   // only 1 ASCII-encodable allowed at the end for X12, unlike C40/TEXT
                        {
                            size++;   // we need an extra unlatch at the end
                        }
                        else
                        {
                            int symbolsLeft = CodewordsRemaining(square, size + lastAscii, 0);
                            if (symbolsLeft > 0) size++;   // we need an extra unlatch at the end
                        }
                    }
                    break;

                case DmEdifact:
                    size += 3;
                    if (previousMode != DmEdifact)
                    {
                        size++;   // latch to this mode
                        if (previousMode != DmAscii && previousMode != DmBase256) size++;   // unlatch to ASCII
                    }
                    edge.EndMode = GetEndMode(square, source, length, mode, from, len, size);
                    break;
            }
            edge.Size = size;

            return edge.EndMode;
        }

        // Add an edge for a mode at a vertex if no existing edge or if more optimal than existing edge
        static void AddEdge(bool square, byte[] source, int length, Edge[] edges, int mode, int from, int len,
            int previous, int cwds)
        {
            Edge edge;
            int endMode = NewEdge(square, source, length, edges, mode, from, len, previous, out edge, cwds);
            int vertexIndex = from + len;
            int vij = vertexIndex * NumModes + endMode - 1;

            if (edges[vij].Mode == 0 || edges[vij].Size > edge.Size) edges[vij] = edge;
        }

        // Add edges for the various modes at a vertex
        static void AddEdges(bool square, byte[] source, int length, Edge[] edges, int from, int previous, bool gs1)
        {
            // Not possible to unlatch a full EDF edge to something else
            if (previous == 0 || edges[previous].EndMode != DmEdifact)
            {
                if (IsDigit(source[from]) && from + 1 < length && IsDigit(source[from + 1]))
                {
                    AddEdge(square, source, length, edges, DmAscii, from, 2, previous, 0);
                    // If ASCII vertex, don't bother adding other edges as this will be optimal
                    if (previous != 0 && edges[previous].Mode == DmAscii) return;
                }
                else
                {
                    AddEdge(square, source, length, edges, DmAscii, from, 1, previous, 0);
                }

                for (int mode = DmC40; mode <= DmText; mode++)
                {
                    int len;
                    int cwds = GetNumberOfC40Words(source, length, from, mode, out len);
                    if (cwds != 0) AddEdge(square, source, length, edges, mode, from, len, previous, cwds);
                }

                if (from + 2 < length && IsX12(source[from]) && IsX12(source[from + 1]) && IsX12(source[from + 2]))
                    AddEdge(square, source, length, edges, DmX12, from, 3, previous, 0);

                if (!gs1 || source[from] != 0x1D) AddEdge(square, source, length, edges, DmBase256, from, 1, previous, 0);
            }

            if (IsEdifact(source[from]))
            {
                // We create 3 EDF edges, 2, 3 or 4 characters length. The 4-char normally doesn't have a latch to
                // ASCII unless it is 2 characters away from the end of the input.
                for (int i = 1, pos = from + i; i < 4 && pos < length && IsEdifact(source[pos]); i++, pos++)
                    AddEdge(square, source, length, edges, DmEdifact, from, i + 1, previous, 0);
            }
        }

        // Calculate optimized encoding modes: the mode of each character
        static byte[] DefineModes(bool square, byte[] source, int length, bool gs1)
        {
            var edges = new Edge[(length + 1) * NumModes];
            AddEdges(square, source, length, edges, 0, 0, gs1);

            for (int i = 1; i < length; i++)
            {
                int vi = i * NumModes;
                for (int j = 0; j < NumModes; j++)
                {
                    if (edges[vi + j].Mode != 0) AddEdges(square, source, length, edges, i, vi + j, gs1);
                }
            }

            int last = length * NumModes;
            int minimalJ = -1;
            int minimalSize = int.MaxValue;
            for (int j = 0; j < NumModes; j++)
            {
                if (edges[last + j].Mode != 0 && edges[last + j].Size < minimalSize)
                {
                    minimalSize = edges[last + j].Size;
                    minimalJ = j;
                }
            }

            var modes = new byte[length];
            int edge = last + minimalJ;
            int modeEnd = length;
            int modeLen = 0;
            while (true)
            {
                int currentMode = edges[edge].Mode;
                modeLen += edges[edge].Len;
                int previous = edges[edge].Previous;
                if (previous == 0 || edges[previous].Mode != currentMode)
                {
                    for (int i = modeEnd - modeLen; i < modeEnd; i++) modes[i] = (byte)currentMode;
                    modeEnd -= modeLen;
                    modeLen = 0;
                }
                if (previous == 0) break;
                edge = previous;
            }
            return modes;
        }

        // ---------- encoding the data ----------

        // The codewords made so far and the part-made ones zint keeps in its process buffer
        sealed class Encodation
        {
            const string X12NonAlphanumChars = "\r*> ";

            readonly bool square;
            readonly bool gs1;
            readonly int[] buffer = new int[8];   // elements waiting for a whole triplet or quadruplet
            int bufferLen;
            int b256Start;
            int currentMode = DmAscii;
            int sp;

            public readonly byte[] Target = new byte[TargetSize];
            public int Tp;

            public Encodation(bool square, bool gs1)
            {
                this.square = square;
                this.gs1 = gs1;
            }

            // Copy C40/TEXT/X12 triplets from the buffer to the target, leaving the elements (< 3) that aren't a
            // whole one
            void CtxBufferXfer()
            {
                int e = bufferLen / 3 * 3;
                for (int i = 0; i < e; i += 3)
                {
                    int iv = 1600 * buffer[i] + 40 * buffer[i + 1] + buffer[i + 2] + 1;
                    Target[Tp++] = (byte)(iv >> 8);
                    Target[Tp++] = (byte)(iv & 0xFF);
                }
                bufferLen -= e;
                if (bufferLen != 0) Array.Copy(buffer, e, buffer, 0, bufferLen);
            }

            // Copy EDIFACT quadruplets from the buffer to the target, leaving the elements (< 4) that aren't a
            // whole one, or if `empty` writing those as well
            void EdiBufferXfer(bool empty)
            {
                int e = bufferLen / 4 * 4;
                for (int i = 0; i < e; i += 4)
                {
                    Target[Tp++] = (byte)(buffer[i] << 2 | (buffer[i + 1] & 0x30) >> 4);
                    Target[Tp++] = (byte)((buffer[i + 1] & 0x0F) << 4 | (buffer[i + 2] & 0x3C) >> 2);
                    Target[Tp++] = (byte)((buffer[i + 2] & 0x03) << 6 | buffer[i + 3]);
                }
                bufferLen -= e;
                if (bufferLen != 0)
                {
                    Array.Copy(buffer, e, buffer, 0, bufferLen);
                    if (empty)
                    {
                        if (bufferLen == 3)
                        {
                            Target[Tp++] = (byte)(buffer[0] << 2 | (buffer[1] & 0x30) >> 4);
                            Target[Tp++] = (byte)((buffer[1] & 0x0F) << 4 | (buffer[2] & 0x3C) >> 2);
                            Target[Tp++] = (byte)((buffer[2] & 0x03) << 6);
                        }
                        else if (bufferLen == 2)
                        {
                            Target[Tp++] = (byte)(buffer[0] << 2 | (buffer[1] & 0x30) >> 4);
                            Target[Tp++] = (byte)((buffer[1] & 0x0F) << 4);
                        }
                        else
                        {
                            Target[Tp++] = (byte)(buffer[0] << 2);
                        }
                        bufferLen = 0;
                    }
                }
            }

            // Update Base 256 field length
            void UpdateB256FieldLength()
            {
                int b256Count = Tp - (b256Start + 1);
                if (b256Count <= 249)
                {
                    Target[b256Start] = (byte)b256Count;
                }
                else
                {
                    // insert extra codeword
                    Array.Copy(Target, b256Start + 1, Target, b256Start + 2, b256Count);
                    Target[b256Start] = (byte)(249 + b256Count / 250);
                    Target[b256Start + 1] = (byte)(b256Count % 250);
                    Tp++;
                }
            }

            // B.2.1 255-state randomising algorithm
            void RandomiseB256()
            {
                for (int i = b256Start; i < Tp; i++)
                {
                    int prn = (149 * (i + 1)) % 255 + 1;
                    Target[i] = (byte)((Target[i] + prn) & 0xFF);
                }
            }

            // Switch from ASCII or Base 256 to another mode
            void SwitchMode(int nextMode)
            {
                switch (nextMode)
                {
                    case DmC40:
                        Target[Tp++] = 230;
                        break;
                    case DmText:
                        Target[Tp++] = 239;
                        break;
                    case DmX12:
                        Target[Tp++] = 238;
                        break;
                    case DmEdifact:
                        Target[Tp++] = 240;
                        break;
                    case DmBase256:
                        Target[Tp++] = 231;
                        b256Start = Tp;
                        Target[Tp++] = 0;   // byte count holder (may be expanded to 2 codewords)
                        break;
                }
            }

            // Do the default minimal encodation. Returns zint's message when the data needs too many codewords.
            string MinimalEncode(byte[] source, int length)
            {
                byte[] modes = DefineModes(square, source, length, gs1);

                while (sp < length)
                {
                    if (modes[sp] != currentMode)
                    {
                        switch (currentMode)
                        {
                            case DmC40:
                            case DmText:
                            case DmX12:
                                bufferLen = 0;   // throw away buffer if any
                                Target[Tp++] = 254;   // unlatch
                                break;
                            case DmEdifact:
                                {
                                    int lastAscii = LastAscii(source, length, sp);
                                    if (lastAscii == 0)
                                    {
                                        buffer[bufferLen++] = 31;   // unlatch
                                    }
                                    else
                                    {
                                        int symbolsLeft = CodewordsRemaining(square, Tp + lastAscii, bufferLen);
                                        if (symbolsLeft > 2 - lastAscii) buffer[bufferLen++] = 31;   // unlatch
                                    }
                                    EdiBufferXfer(true);
                                    break;
                                }
                            case DmBase256:
                                UpdateB256FieldLength();
                                RandomiseB256();
                                break;
                        }
                        SwitchMode(modes[sp]);
                    }

                    currentMode = modes[sp];

                    if (currentMode == DmAscii)
                    {
                        if (sp + 1 < length && IsDigit(source[sp]) && IsDigit(source[sp + 1]))
                        {
                            Target[Tp++] = (byte)(10 * (source[sp] - '0') + (source[sp + 1] - '0') + 130);
                            sp += 2;
                        }
                        else
                        {
                            if ((source[sp] & 0x80) != 0)
                            {
                                Target[Tp++] = 235;   // FNC4
                                Target[Tp++] = (byte)((source[sp] - 128) + 1);
                            }
                            else if (gs1 && source[sp] == 0x1D)
                            {
                                Target[Tp++] = 232;   // FNC1
                            }
                            else
                            {
                                Target[Tp++] = (byte)(source[sp] + 1);
                            }
                            sp++;
                        }
                    }
                    else if (currentMode == DmC40 || currentMode == DmText)
                    {
                        int[] ctShift = currentMode == DmC40 ? C40Shift : TextShift;
                        int[] ctValue = currentMode == DmC40 ? C40Value : TextValue;
                        int shiftSet, value;

                        if ((source[sp] & 0x80) != 0)
                        {
                            buffer[bufferLen++] = 1;
                            buffer[bufferLen++] = 30;   // upper shift
                            shiftSet = ctShift[source[sp] - 128];
                            value = ctValue[source[sp] - 128];
                        }
                        else if (gs1 && source[sp] == 0x1D)
                        {
                            shiftSet = 2;
                            value = 27;   // FNC1
                        }
                        else
                        {
                            shiftSet = ctShift[source[sp]];
                            value = ctValue[source[sp]];
                        }

                        if (shiftSet != 0) buffer[bufferLen++] = shiftSet - 1;
                        buffer[bufferLen++] = value;

                        if (bufferLen >= 3) CtxBufferXfer();
                        sp++;
                    }
                    else if (currentMode == DmX12)
                    {
                        int value;
                        if (IsDigit(source[sp])) value = (source[sp] - '0') + 4;
                        else if (IsUpper(source[sp])) value = (source[sp] - 'A') + 14;
                        else value = X12NonAlphanumChars.IndexOf((char)source[sp]);

                        buffer[bufferLen++] = value;

                        if (bufferLen >= 3) CtxBufferXfer();
                        sp++;
                    }
                    else if (currentMode == DmEdifact)
                    {
                        int value = source[sp];
                        if (value >= 64) value -= 64;   // '@'

                        buffer[bufferLen++] = value;
                        sp++;

                        if (bufferLen >= 4) EdiBufferXfer(false);
                    }
                    else if (currentMode == DmBase256)
                    {
                        Target[Tp++] = source[sp++];
                    }

                    if (Tp > MaxCodewords) return "Input too long, requires too many codewords (maximum 1558)";
                }

                return null;
            }

            // Encodes data using ASCII, C40, Text, X12, EDIFACT or Base 256 modes as appropriate
            // Supports encoding FNC1 in supporting systems
            public string EncodeSegment(byte[] source, int length, int eci)
            {
                if (eci > 0)
                {
                    // Encode ECI numbers according to Table 6
                    Target[Tp++] = 241;   // ECI character
                    if (eci <= 126)
                    {
                        Target[Tp++] = (byte)(eci + 1);
                    }
                    else if (eci <= 16382)
                    {
                        Target[Tp++] = (byte)((eci - 127) / 254 + 128);
                        Target[Tp++] = (byte)((eci - 127) % 254 + 1);
                    }
                    else
                    {
                        Target[Tp++] = (byte)((eci - 16383) / 64516 + 192);
                        Target[Tp++] = (byte)(((eci - 16383) / 254) % 254 + 1);
                        Target[Tp++] = (byte)((eci - 16383) % 254 + 1);
                    }
                }

                string error = MinimalEncode(source, length);
                if (error != null) return error;

                int symbolsLeft = CodewordsRemaining(square, Tp, bufferLen);

                if (currentMode == DmC40 || currentMode == DmText)
                {
                    // NOTE: zint follows the spec exactly here, only using Shift 1 padded triplets when 2 symbol
                    // chars remain (BWIPP does the same, TEC-IT doesn't)
                    if (bufferLen == 0)
                    {
                        if (symbolsLeft > 0) Target[Tp++] = 254;   // unlatch
                    }
                    else if (bufferLen == 2 && symbolsLeft == 2)
                    {
                        // 5.2.5.2 (b)
                        buffer[bufferLen++] = 0;   // shift 1
                        CtxBufferXfer();
                    }
                    else if (bufferLen == 1 && symbolsLeft <= 2 && IsC40Text(currentMode, source[length - 1]))
                    {
                        // 5.2.5.2 (c)/(d)
                        if (symbolsLeft > 1) Target[Tp++] = 254;   // unlatch and encode remaining data in ASCII
                        Target[Tp++] = (byte)(source[length - 1] + 1);
                    }
                    else
                    {
                        int total = 0;
                        // Backtrack to last complete triplet (same technique as BWIPP)
                        while (sp > 0 && bufferLen % 3 != 0)
                        {
                            sp--;
                            int cnt = C40TextCount(currentMode, gs1, source[sp]);
                            total += cnt;
                            bufferLen -= cnt;
                        }
                        Tp -= total / 3 * 2;

                        Target[Tp++] = 254;   // unlatch
                        for (; sp < length; sp++)
                        {
                            if (sp + 1 < length && IsDigit(source[sp]) && IsDigit(source[sp + 1]))
                            {
                                Target[Tp++] = (byte)(10 * (source[sp] - '0') + (source[sp + 1] - '0') + 130);
                                sp++;
                            }
                            else if ((source[sp] & 0x80) != 0)
                            {
                                Target[Tp++] = 235;   // FNC4
                                Target[Tp++] = (byte)((source[sp] - 128) + 1);
                            }
                            else if (gs1 && source[sp] == 0x1D)
                            {
                                Target[Tp++] = 232;   // FNC1
                            }
                            else
                            {
                                Target[Tp++] = (byte)(source[sp] + 1);
                            }
                        }
                    }
                }
                else if (currentMode == DmX12)
                {
                    if (symbolsLeft == 1 && bufferLen == 1)
                    {
                        // unlatch not required!
                        Target[Tp++] = (byte)(source[length - 1] + 1);
                    }
                    else
                    {
                        if (symbolsLeft > 0) Target[Tp++] = 254;   // unlatch

                        if (bufferLen == 1)
                        {
                            Target[Tp++] = (byte)(source[length - 1] + 1);
                        }
                        else if (bufferLen == 2)
                        {
                            Target[Tp++] = (byte)(source[length - 2] + 1);
                            Target[Tp++] = (byte)(source[length - 1] + 1);
                        }
                    }
                }
                else if (currentMode == DmEdifact)
                {
                    if (symbolsLeft <= 2 && bufferLen <= symbolsLeft)   // unlatch not required!
                    {
                        if (bufferLen == 1)
                        {
                            Target[Tp++] = (byte)(source[length - 1] + 1);
                        }
                        else if (bufferLen == 2)
                        {
                            Target[Tp++] = (byte)(source[length - 2] + 1);
                            Target[Tp++] = (byte)(source[length - 1] + 1);
                        }
                    }
                    else
                    {
                        // append EDIFACT unlatch value (31) and empty buffer
                        if (bufferLen <= 3) buffer[bufferLen++] = 31;
                        EdiBufferXfer(true);
                    }
                }
                else if (currentMode == DmBase256)
                {
                    if (symbolsLeft > 0) UpdateB256FieldLength();
                    RandomiseB256();
                }

                return null;
            }
        }

        // ---------- the symbol (dm_encode_segs, dm_ecc200) ----------

        // Add pad bits
        static void AddTail(byte[] target, int tp, int tailLength)
        {
            target[tp++] = 129;   // pad
            for (int i = 1; i < tailLength; i++)
            {
                // B.1.1 253-state randomising algorithm
                int prn = (149 * (tp + 1)) % 253 + 1;
                int temp = 129 + prn;
                target[tp++] = (byte)(temp <= 254 ? temp : temp - 254);
            }
        }

        // The code for the bytes of one segment (the data in the ECI's character set, or for GS1 the reduced
        // data, with an ECI of 0 for no ECI)
        static bool[][] EncodeBytes(byte[] source, int eci, bool gs1, bool square, out string error)
        {
            error = null;
            int length = source.Length;
            if (length > MaxSegmentLength)
            {
                error = "Input length " + length.ToString(CultureInfo.InvariantCulture) + " too long (maximum 3116)";
                return null;
            }

            var encodation = new Encodation(square, gs1);
            byte[] binary = encodation.Target;
            if (gs1) binary[encodation.Tp++] = 232;   // FNC1

            // Check for Macro05/Macro06: "[)>[RS]05[GS]...[RS][EOT]" -> CW 236, "[)>[RS]06[GS]...[RS][EOT]" -> CW 237
            if (encodation.Tp == 0 && length >= 9
                && source[0] == '[' && source[1] == ')' && source[2] == '>' && source[3] == 0x1E
                && source[4] == '0' && (source[5] == '5' || source[5] == '6') && source[6] == 0x1D
                && source[length - 1] == 0x04 && source[length - 2] == 0x1E)
            {
                binary[encodation.Tp++] = (byte)(source[5] == '5' ? 236 : 237);
                // remove the macro characters from the input
                var inner = new byte[length - 9];
                Array.Copy(source, 7, inner, 0, inner.Length);
                source = inner;
                length = inner.Length;
            }

            // A macro header and trailer with nothing between them leave nothing to encode. zint itself reads
            // past the end of its data there, so there is no result of its to match.
            if (length > 0)
            {
                error = encodation.EncodeSegment(source, length, eci);
                if (error != null) return null;
            }
            int binlen = encodation.Tp;

            int symbolSize = GetSymbolSize(square, binlen);
            if (binlen > MatrixBytes[symbolSize])
            {
                error = "Input too long, requires " + binlen.ToString(CultureInfo.InvariantCulture) + " codewords (maximum 1558)";
                return null;
            }

            int h = MatrixH[symbolSize];
            int w = MatrixW[symbolSize];
            int fh = MatrixFH[symbolSize];
            int fw = MatrixFW[symbolSize];
            int bytes = MatrixBytes[symbolSize];
            int dataBlock = MatrixDataBlock[symbolSize];
            int rsBlock = MatrixRsBlock[symbolSize];

            int tailLength = bytes - binlen;
            if (tailLength != 0) AddTail(binary, binlen, tailLength);

            Ecc(binary, bytes, dataBlock, rsBlock);

            // placement
            int nc = w - 2 * (w / fw);
            int nr = h - 2 * (h / fh);
            var places = new int[nc * nr];
            Placement(places, nr, nc);

            var rows = new bool[h][];
            for (int y = 0; y < h; y++) rows[y] = new bool[w];
            for (int y = 0; y < h; y += fh)
            {
                for (int x = 0; x < w; x++) rows[(h - y) - 1][x] = true;
                for (int x = 0; x < w; x += 2) rows[y][x] = true;
            }
            for (int x = 0; x < w; x += fw)
            {
                for (int y = 0; y < h; y++) rows[(h - y) - 1][x] = true;
                for (int y = 0; y < h; y += 2) rows[(h - y) - 1][x + fw - 1] = true;
            }
            for (int y = 0; y < nr; y++)
            {
                for (int x = 0; x < nc; x++)
                {
                    int v = places[(nr - y - 1) * nc + x];
                    if (v == 1 || (v > 7 && (binary[(v >> 3) - 1] & (1 << (v & 7))) != 0))
                        rows[h - (1 + y + 2 * (y / (fh - 2))) - 1][1 + x + 2 * (x / (fw - 2))] = true;
                }
            }
            return rows;
        }
    }
}
