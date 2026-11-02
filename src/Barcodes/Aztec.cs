// This follows zint 2.16.0's backend/aztec.c (Copyright (C) 2009-2025 Robin Stuart and the zint
// contributors; BSD-3-Clause, the licence is in THIRD-PARTY-NOTICES.txt), with the parts of reedsol.c it
// leans on (library.c's and eci.c's are in Barcode.cs and Eci.cs).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace B0r3dQR.Barcodes
{
    // Aztec Code as zint 2.16.0 makes it for qr.b0r3d.org (zxing-wasm calls zint with its defaults), so a
    // code comes out module for module the same here as on the website. In zint's order:
    // - library.c and eci.c (Eci.cs): the text goes in as ISO/IEC 8859-1 bytes; if it won't fit that, as
    //   the bytes of the first single-byte character set that holds all of it, with that set's ECI, or
    //   else as UTF-8 (ECI 26);
    // - aztec.c: the mix of text modes and binary shifts, the smallest compact or full-size symbol at
    //   zint's default error correction (level 2, about 23%), bit stuffing, Reed-Solomon check words,
    //   the mode message and the spiral of modules.
    // What the website never asks for is left out: GS1 data, Reader Initialisation, Structured Append,
    // a chosen size or error correction level, and Aztec Runes. zint's warnings (a symbol short of the
    // standard's 5% check words, or one that needed an ECI) don't change a module, so they aren't kept.
    static class Aztec
    {
        const int MaxCapacity = 19968;     // ISO/IEC 24778:2008 5.3 Table 1: the most bits a symbol holds
        const int BinCapacity = 19932;     // MaxCapacity less 3 codewords of 12 bits
        const int MapPosnMax = 20039;      // the highest position in the full-size map
        const int EccLevel = 2;            // zint's default: about 23% check words
        const char InitialMode = 'U';      // a message starts in Upper mode

        // The rows of modules (dark = true), no quiet zone, or null with the website's message when zint refuses.
        // zint's checks for every kind of code (17,400 bytes at most, real UTF-8, a byte order mark at the
        // start dropped) are Barcode.Encode's, done before this.
        public static bool[][] Encode(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text))
            {
                error = "Can not create a barcode from NULL or empty data";   // zxing-cpp's, before zint is asked
                return null;
            }
            byte[] source = Eci.ToCharacterSet(text, out int eci);
            return Symbol(source, eci, out error);
        }

        // ---------- aztec.c ----------

        // zint_aztec: the modules for bytes in the character set the ECI names (0: ISO/IEC 8859-1).
        static bool[][] Symbol(byte[] source, int eci, out string error)
        {
            error = null;
            var message = new BitBuffer(BinCapacity);
            if (!TextProcess(source, eci, message))
            {
                error = "Input too long, requires too many codewords (maximum " + BinCapacity / 12 + ")";   // 502
                return null;
            }
            int dataLength = message.Length;

            // Decide what size symbol to use: the smallest that fits the data. Bit stuffing can lengthen the
            // data past the size chosen, which means choosing again.
            var adjusted = new byte[MaxCapacity + 64];
            int dataMaxsize = 0, adjustmentSize = 0;
            int layers, codewordSize, adjustedLength;
            bool compact;
            do
            {
                compact = false;
                layers = 0;
                for (int i = 4; i > 0; i--)
                {
                    if (dataLength + adjustmentSize <= CompactDataSizes[EccLevel - 1, i - 1])
                    {
                        layers = i;
                        compact = true;
                        dataMaxsize = CompactDataSizes[EccLevel - 1, i - 1];
                    }
                }
                if (!compact)
                {
                    for (int i = 32; i > 0; i--)
                    {
                        if (dataLength + adjustmentSize <= DataSizes[EccLevel - 1, i - 1])
                        {
                            layers = i;
                            dataMaxsize = DataSizes[EccLevel - 1, i - 1];
                        }
                    }
                }

                if (layers == 0)   // couldn't find a symbol which fits the data
                {
                    int mostCodewords = DataSizes[EccLevel - 1, 31] / 12;
                    if (adjustmentSize == 0)
                        error = "Input too long for ECC level " + EccLevel + ", requires too many codewords (maximum " + mostCodewords + ")";   // 707
                    else
                        error = "Input too long for ECC level " + EccLevel + ", requires " + (dataLength + adjustmentSize + 11) / 12 + " codewords (maximum " + mostCodewords + ")";   // 504
                    return null;
                }

                codewordSize = CodewordSize(layers);

                adjustedLength = BitrunStuff(message.Data, dataLength, codewordSize, adjustmentSize != 0 ? dataMaxsize : BinCapacity, adjusted);
                if (adjustedLength == 0)
                {
                    error = "Input too long for ECC level " + EccLevel + ", requires too many codewords (maximum " + (adjustmentSize != 0 ? dataMaxsize : BinCapacity) / codewordSize + ")";   // 705
                    return null;
                }
                adjustmentSize = adjustedLength - dataLength;

                // Add padding
                int remainder = adjustedLength % codewordSize;
                int padbits = codewordSize - remainder;
                if (padbits == codewordSize) padbits = 0;
                adjustedLength = AddPadding(padbits, codewordSize, adjusted, adjustedLength);
            } while (adjustedLength > dataMaxsize);

            int dataBlocks = adjustedLength / codewordSize;
            int eccBlocks;
            if (compact)
            {
                eccBlocks = CompactSizes[layers - 1] - dataBlocks;
                if (layers == 4) eccBlocks += 12;   // can use spare blocks for ECC (76 available - 64 max data blocks)
            }
            else
            {
                eccBlocks = Sizes[layers - 1] - dataBlocks;
            }

            // Split into codewords and calculate Reed-Solomon error correction codes
            var dataPart = new int[dataBlocks];
            for (int i = 0; i < dataBlocks; i++)
                for (int p = 0; p < codewordSize; p++)
                    if (adjusted[i * codewordSize + p] == 1) dataPart[i] |= 1 << (codewordSize - (p + 1));

            int[] eccPart;
            switch (codewordSize)
            {
                case 6: eccPart = ReedSolomon(dataPart, dataBlocks, eccBlocks, 0x43, 6); break;
                case 8: eccPart = ReedSolomon(dataPart, dataBlocks, eccBlocks, 0x12D, 8); break;
                case 10: eccPart = ReedSolomon(dataPart, dataBlocks, eccBlocks, 0x409, 10); break;
                default: eccPart = ReedSolomon(dataPart, dataBlocks, eccBlocks, 0x1069, 12); break;
            }
            for (int i = 0; i < eccBlocks; i++) adjustedLength = Append(eccPart[i], codewordSize, adjusted, adjustedLength);

            // Invert the data so that actual data is on the outside and Reed-Solomon on the inside
            var bitPattern = new byte[MapPosnMax + 1];
            int totalBits = (dataBlocks + eccBlocks) * codewordSize;
            for (int i = 0; i < totalBits; i++) bitPattern[i] = adjusted[totalBits - i - 1];

            // Now add the symbol descriptor (the mode message)
            var descriptor = new byte[40];
            if (compact)
            {
                // The first 2 bits represent the number of layers minus 1
                descriptor[0] = Bit(((layers - 1) & 0x02) != 0);
                descriptor[1] = Bit(((layers - 1) & 0x01) != 0);

                // The next 6 bits represent the number of data blocks minus 1
                descriptor[2] = Bit(((dataBlocks - 1) & 0x20) != 0);
                for (int i = 3; i < 8; i++) descriptor[i] = Bit(((dataBlocks - 1) & (0x10 >> (i - 3))) != 0);
            }
            else
            {
                // The first 5 bits represent the number of layers minus 1
                for (int i = 0; i < 5; i++) descriptor[i] = Bit(((layers - 1) & (0x10 >> i)) != 0);

                // The next 11 bits represent the number of data blocks minus 1
                descriptor[5] = Bit(((dataBlocks - 1) & 0x400) != 0);
                for (int i = 6; i < 16; i++) descriptor[i] = Bit(((dataBlocks - 1) & (0x200 >> (i - 6))) != 0);
            }

            // Split into 4-bit codewords and add Reed-Solomon error correction over GF(16) (section 7.2.3)
            var descData = new int[4];
            for (int i = 0; i < 4; i++)
                descData[i] = (descriptor[i * 4] << 3) | (descriptor[i * 4 + 1] << 2) | (descriptor[i * 4 + 2] << 1) | descriptor[i * 4 + 3];
            int descEccWords = compact ? 5 : 6;
            int[] descEcc = ReedSolomon(descData, compact ? 2 : 4, descEccWords, 0x13, 4);
            int descEccStart = compact ? 8 : 16;
            for (int i = 0; i < descEccWords; i++)
            {
                descriptor[descEccStart + i * 4] = Bit((descEcc[i] & 0x08) != 0);
                descriptor[descEccStart + i * 4 + 1] = Bit((descEcc[i] & 0x04) != 0);
                descriptor[descEccStart + i * 4 + 2] = Bit((descEcc[i] & 0x02) != 0);
                descriptor[descEccStart + i * 4 + 3] = Bit((descEcc[i] & 0x01) != 0);
            }

            // Merge descriptor with the rest of the symbol
            Array.Copy(descriptor, 0, bitPattern, (compact ? 2000 : 20000) - 2, 40);

            // Plot all of the data into the symbol in pre-defined spiral pattern
            short[] map;
            int mapWidth, offset;
            if (compact)
            {
                map = CompactMap;
                mapWidth = 27;
                offset = CompactOffset[layers - 1];
            }
            else
            {
                map = PopulateMap(layers);
                mapWidth = 151;
                offset = Offset[layers - 1];
            }
            int dim = mapWidth - 2 * offset;
            var rows = new bool[dim][];
            for (int y = offset; y < mapWidth - offset; y++)
            {
                var row = new bool[dim];
                for (int x = offset; x < mapWidth - offset; x++)
                {
                    int m = map[y * mapWidth + x];
                    row[x - offset] = m == 1 || (m >= 2 && bitPattern[m - 2] == 1);
                }
                rows[y - offset] = row;
            }
            return rows;
        }

        // aztec_text_process: works out the encodation mode of each byte and writes the message bits.
        // False when they won't fit in the biggest symbol. zint does this in one long function; the
        // passes are methods here, in the same order.
        static bool TextProcess(byte[] source, int eci, BitBuffer bits)
        {
            int length = source.Length;

            // A shortcut for text that can't fit whatever it holds: the message takes 2.5 bits a byte at the
            // very least (a CR LF, ". ", ", " or ": " is 5 bits for 2 bytes, anything else 4 bits or more
            // for 1). This keeps the mode search below, which slows down on long runs of spaces, short.
            if (length > BinCapacity * 2 / 5) return false;

            var encodeMode = new char[length + 1];
            for (int i = 0; i < length; i++)
                encodeMode[i] = source[i] > 0x7F ? 'B' : Modes[source[i]];

            CombinePairs(source, encodeMode);

            var reducedSource = new byte[length + 1];
            var reducedEncodeMode = new char[length + 1];
            int reducedLength = Reduce(source, encodeMode, reducedSource, reducedEncodeMode);

            ResolveModes(reducedSource, reducedEncodeMode, reducedLength);
            ChooseShifts(reducedEncodeMode, reducedLength);

            if (eci != 0)
            {
                bits.Add(0, 5);   // P/S
                bits.Add(0, 5);   // FLG(n)
                string digits = eci.ToString(CultureInfo.InvariantCulture);
                bits.Add(digits.Length, 3);   // FLG(1) to FLG(6): how many digits follow
                foreach (char digit in digits) bits.Add(2 + (digit - '0'), 4);
            }

            return WriteMessage(reducedSource, reducedEncodeMode, reducedLength, bits);
        }

        // Deal first with letter combinations which can be combined to one codeword.
        // Combinations are (CR LF) (. SP) (, SP) (: SP) in Punct mode.
        static void CombinePairs(byte[] source, char[] encodeMode)
        {
            int length = source.Length;
            char currentMode = InitialMode;
            for (int i = 0; i + 1 < length; i++)
            {
                // Combination (CR LF) should always be in Punct mode
                if (source[i] == 13 && source[i + 1] == 10)
                {
                    encodeMode[i] = 'P';
                    encodeMode[i + 1] = 'P';
                }
                // Combination (: SP) should always be in Punct mode
                else if (source[i] == ':' && source[i + 1] == ' ')
                {
                    encodeMode[i + 1] = 'P';
                }
                // Combinations (. SP) and (, SP) sometimes use fewer bits in Digit mode
                else if ((source[i] == '.' || source[i] == ',') && source[i + 1] == ' ' && encodeMode[i] == 'X')
                {
                    int count = CountDoubles(source, i, length);
                    char nextMode = NextMode(encodeMode, length, i);

                    if (currentMode == 'U')
                    {
                        if (nextMode == 'D' && count <= 5) Fill(encodeMode, i, 2 * count, 'D');
                    }
                    else if (currentMode == 'L')
                    {
                        if (nextMode == 'D' && count <= 4) Fill(encodeMode, i, 2 * count, 'D');
                    }
                    else if (currentMode == 'M')
                    {
                        if (nextMode == 'D' && count == 1)
                        {
                            encodeMode[i] = 'D';
                            encodeMode[i + 1] = 'D';
                        }
                    }
                    else if (currentMode == 'D')
                    {
                        if (nextMode != 'D' && count <= 4) Fill(encodeMode, i, 2 * count, 'D');
                        else if (nextMode == 'D' && count <= 7) Fill(encodeMode, i, 2 * count, 'D');
                    }

                    // Default is Punct mode
                    if (encodeMode[i] == 'X')
                    {
                        encodeMode[i] = 'P';
                        encodeMode[i + 1] = 'P';
                    }
                }

                if (encodeMode[i] != 'X' && encodeMode[i] != 'B') currentMode = encodeMode[i];
            }
        }

        // Reduce two letter combinations to one codeword marked as [abcd] in Punct mode; returns the new length
        static int Reduce(byte[] source, char[] encodeMode, byte[] reducedSource, char[] reducedEncodeMode)
        {
            int length = source.Length;
            int i = 0, j = 0;
            while (i < length)
            {
                reducedEncodeMode[j] = encodeMode[i];
                if (i + 1 < length)
                {
                    if (source[i] == 13 && source[i + 1] == 10)   // CR LF
                    {
                        reducedSource[j] = (byte)'a';
                        i += 2;
                    }
                    else if (source[i] == '.' && source[i + 1] == ' ' && encodeMode[i] == 'P')
                    {
                        reducedSource[j] = (byte)'b';
                        i += 2;
                    }
                    else if (source[i] == ',' && source[i + 1] == ' ' && encodeMode[i] == 'P')
                    {
                        reducedSource[j] = (byte)'c';
                        i += 2;
                    }
                    else if (source[i] == ':' && source[i + 1] == ' ')
                    {
                        reducedSource[j] = (byte)'d';
                        i += 2;
                    }
                    else
                    {
                        reducedSource[j] = source[i++];
                    }
                }
                else
                {
                    reducedSource[j] = source[i++];
                }
                j++;
            }
            return j;
        }

        // Settle the mode of the characters that can go in more than one: CR, full stop, comma and space
        static void ResolveModes(byte[] reducedSource, char[] reducedEncodeMode, int reducedLength)
        {
            char currentMode = InitialMode;
            for (int i = 0; i < reducedLength; i++)
            {
                // Resolve Carriage Return (CR) which can be Punct or Mixed mode
                if (reducedSource[i] == 13)
                {
                    int count = CountChar(reducedSource, i, reducedLength, 13);
                    char nextMode = NextMode(reducedEncodeMode, reducedLength, i);

                    if (currentMode == 'U' && (nextMode == 'U' || nextMode == 'B') && count == 1)
                    {
                        reducedEncodeMode[i] = 'P';
                    }
                    else if (currentMode == 'L' && (nextMode == 'L' || nextMode == 'B') && count == 1)
                    {
                        reducedEncodeMode[i] = 'P';
                    }
                    else if (currentMode == 'P' || nextMode == 'P')
                    {
                        reducedEncodeMode[i] = 'P';
                    }

                    if (currentMode == 'D')
                    {
                        if ((nextMode == 'E' || nextMode == 'U' || nextMode == 'D' || nextMode == 'B') && count <= 2)
                            Fill(reducedEncodeMode, i, count, 'P');
                        else if (nextMode == 'L' && count == 1)
                            reducedEncodeMode[i] = 'P';
                    }

                    // Default is Mixed mode
                    if (reducedEncodeMode[i] == 'X') reducedEncodeMode[i] = 'M';
                }
                // Resolve full stop and comma which can be in Punct or Digit mode
                else if (reducedSource[i] == '.' || reducedSource[i] == ',')
                {
                    int count = CountDotComma(reducedSource, i, reducedLength);
                    char nextMode = NextMode(reducedEncodeMode, reducedLength, i);

                    if (currentMode == 'U')
                    {
                        if ((nextMode == 'U' || nextMode == 'L' || nextMode == 'M' || nextMode == 'B') && count == 1)
                            reducedEncodeMode[i] = 'P';
                    }
                    else if (currentMode == 'L')
                    {
                        if (nextMode == 'L' && count <= 2) Fill(reducedEncodeMode, i, count, 'P');
                        else if ((nextMode == 'M' || nextMode == 'B') && count == 1) reducedEncodeMode[i] = 'P';
                    }
                    else if (currentMode == 'M')
                    {
                        if ((nextMode == 'E' || nextMode == 'U' || nextMode == 'L' || nextMode == 'M') && count <= 4)
                            Fill(reducedEncodeMode, i, count, 'P');
                        else if (nextMode == 'B' && count <= 2)
                            Fill(reducedEncodeMode, i, count, 'P');
                    }
                    else if (currentMode == 'P' && nextMode != 'D' && count <= 9)
                    {
                        Fill(reducedEncodeMode, i, count, 'P');
                    }

                    // Default is Digit mode
                    if (reducedEncodeMode[i] == 'X') reducedEncodeMode[i] = 'D';
                }
                // Resolve Space (SP) which can be any mode except Punct
                else if (reducedSource[i] == ' ')
                {
                    int count = CountChar(reducedSource, i, reducedLength, (byte)' ');
                    char nextMode = NextMode(reducedEncodeMode, reducedLength, i);

                    if (currentMode == 'U')
                    {
                        if (nextMode == 'E' && count <= 5)
                            Fill(reducedEncodeMode, i, count, 'U');
                        else if ((nextMode == 'U' || nextMode == 'L' || nextMode == 'M' || nextMode == 'P' || nextMode == 'B') && count <= 9)
                            Fill(reducedEncodeMode, i, count, 'U');
                    }
                    else if (currentMode == 'L')
                    {
                        if (nextMode == 'E' && count <= 5)
                            Fill(reducedEncodeMode, i, count, 'L');
                        else if (nextMode == 'U' && count == 1)
                            reducedEncodeMode[i] = 'L';
                        else if (nextMode == 'L' && count <= 14)
                            Fill(reducedEncodeMode, i, count, 'L');
                        else if ((nextMode == 'M' || nextMode == 'P' || nextMode == 'B') && count <= 9)
                            Fill(reducedEncodeMode, i, count, 'L');
                    }
                    else if (currentMode == 'M')
                    {
                        if ((nextMode == 'E' || nextMode == 'U') && count <= 9)
                            Fill(reducedEncodeMode, i, count, 'M');
                        else if ((nextMode == 'L' || nextMode == 'B') && count <= 14)
                            Fill(reducedEncodeMode, i, count, 'M');
                        else if ((nextMode == 'M' || nextMode == 'P') && count <= 19)
                            Fill(reducedEncodeMode, i, count, 'M');
                    }
                    else if (currentMode == 'P')
                    {
                        if (nextMode == 'E' && count <= 5)
                            Fill(reducedEncodeMode, i, count, 'U');
                        else if ((nextMode == 'U' || nextMode == 'L' || nextMode == 'M' || nextMode == 'P' || nextMode == 'B') && count <= 9)
                            Fill(reducedEncodeMode, i, count, 'U');
                    }

                    // Default is Digit mode
                    if (reducedEncodeMode[i] == 'X') reducedEncodeMode[i] = 'D';
                }

                if (reducedEncodeMode[i] != 'B') currentMode = reducedEncodeMode[i];
            }
        }

        // Decide when to use P/S instead of P/L and U/S instead of U/L: a short stay in Punct or Upper mode
        // is marked 'p' or 'u', for a single shifted character
        static void ChooseShifts(char[] reducedEncodeMode, int reducedLength)
        {
            char currentMode = InitialMode;
            for (int i = 0; i < reducedLength; i++)
            {
                if (reducedEncodeMode[i] != currentMode)
                {
                    int count = RunLength(reducedEncodeMode, reducedLength, i);
                    char nextMode = NextMode(reducedEncodeMode, reducedLength, i);

                    if (reducedEncodeMode[i] == 'P')
                    {
                        if (currentMode == 'U' && count <= 2)
                            Fill(reducedEncodeMode, i, count, 'p');
                        else if (currentMode == 'L' && nextMode != 'U' && count <= 2)
                            Fill(reducedEncodeMode, i, count, 'p');
                        else if (currentMode == 'L' && nextMode == 'U' && count == 1)
                            reducedEncodeMode[i] = 'p';
                        else if (currentMode == 'M' && nextMode != 'M' && count == 1)
                            reducedEncodeMode[i] = 'p';
                        else if (currentMode == 'M' && nextMode == 'M' && count <= 2)
                            Fill(reducedEncodeMode, i, count, 'p');
                        else if (currentMode == 'D' && nextMode != 'D' && count <= 3)
                            Fill(reducedEncodeMode, i, count, 'p');
                        else if (currentMode == 'D' && nextMode == 'D' && count <= 6)
                            Fill(reducedEncodeMode, i, count, 'p');
                    }
                    else if (reducedEncodeMode[i] == 'U')
                    {
                        if (currentMode == 'L' && (nextMode == 'L' || nextMode == 'M') && count <= 2)
                            Fill(reducedEncodeMode, i, count, 'u');
                        else if (currentMode == 'L' && (nextMode == 'E' || nextMode == 'D' || nextMode == 'B' || nextMode == 'P') && count == 1)
                            reducedEncodeMode[i] = 'u';
                        else if (currentMode == 'D' && nextMode == 'D' && count == 1)
                            reducedEncodeMode[i] = 'u';
                        else if (currentMode == 'D' && nextMode == 'P' && count <= 2)
                            Fill(reducedEncodeMode, i, count, 'u');
                    }
                }

                if (reducedEncodeMode[i] != 'p' && reducedEncodeMode[i] != 'u' && reducedEncodeMode[i] != 'B')
                    currentMode = reducedEncodeMode[i];
            }
        }

        // The last pass: the latch or shift codes between modes and the code of each character. False when
        // the bits won't fit.
        static bool WriteMessage(byte[] reducedSource, char[] reducedEncodeMode, int reducedLength, BitBuffer bits)
        {
            char currentMode = InitialMode;
            for (int i = 0; i < reducedLength; i++)
            {
                if (reducedEncodeMode[i] != currentMode)
                {
                    // Change mode
                    if (currentMode == 'U')
                    {
                        switch (reducedEncodeMode[i])
                        {
                            case 'L': bits.Add(28, 5); break;                    // L/L
                            case 'M': bits.Add(29, 5); break;                    // M/L
                            case 'P': bits.Add(29, 5); bits.Add(30, 5); break;   // M/L, P/L
                            case 'p': bits.Add(0, 5); break;                     // P/S
                            case 'D': bits.Add(30, 5); break;                    // D/L
                            case 'B': bits.Add(31, 5); break;                    // B/S
                        }
                    }
                    else if (currentMode == 'L')
                    {
                        switch (reducedEncodeMode[i])
                        {
                            case 'U': bits.Add(30, 5); bits.Add(14, 4); break;   // D/L, U/L
                            case 'u': bits.Add(28, 5); break;                    // U/S
                            case 'M': bits.Add(29, 5); break;                    // M/L
                            case 'P': bits.Add(29, 5); bits.Add(30, 5); break;   // M/L, P/L
                            case 'p': bits.Add(0, 5); break;                     // P/S
                            case 'D': bits.Add(30, 5); break;                    // D/L
                            case 'B': bits.Add(31, 5); break;                    // B/S
                        }
                    }
                    else if (currentMode == 'M')
                    {
                        switch (reducedEncodeMode[i])
                        {
                            case 'U': bits.Add(29, 5); break;                    // U/L
                            case 'L': bits.Add(28, 5); break;                    // L/L
                            case 'P': bits.Add(30, 5); break;                    // P/L
                            case 'p': bits.Add(0, 5); break;                     // P/S
                            case 'D': bits.Add(29, 5); bits.Add(30, 5); break;   // U/L, D/L
                            case 'B': bits.Add(31, 5); break;                    // B/S
                        }
                    }
                    else if (currentMode == 'P')
                    {
                        switch (reducedEncodeMode[i])
                        {
                            case 'U': bits.Add(31, 5); break;                    // U/L
                            case 'L': bits.Add(31, 5); bits.Add(28, 5); break;   // U/L, L/L
                            case 'M': bits.Add(31, 5); bits.Add(29, 5); break;   // U/L, M/L
                            case 'D': bits.Add(31, 5); bits.Add(30, 5); break;   // U/L, D/L
                            case 'B':
                                bits.Add(31, 5);                                 // U/L
                                currentMode = 'U';
                                bits.Add(31, 5);                                 // B/S
                                break;
                        }
                    }
                    else if (currentMode == 'D')
                    {
                        switch (reducedEncodeMode[i])
                        {
                            case 'U': bits.Add(14, 4); break;                                    // U/L
                            case 'u': bits.Add(15, 4); break;                                    // U/S
                            case 'L': bits.Add(14, 4); bits.Add(28, 5); break;                   // U/L, L/L
                            case 'M': bits.Add(14, 4); bits.Add(29, 5); break;                   // U/L, M/L
                            case 'P': bits.Add(14, 4); bits.Add(29, 5); bits.Add(30, 5); break;  // U/L, M/L, P/L
                            case 'p': bits.Add(0, 4); break;                                     // P/S
                            case 'B':
                                bits.Add(14, 4);                                                 // U/L
                                currentMode = 'U';
                                bits.Add(31, 5);                                                 // B/S
                                break;
                        }
                    }

                    // Byte mode - process full block here
                    if (reducedEncodeMode[i] == 'B')
                    {
                        int bigBatch = 0;
                        int count = RunLength(reducedEncodeMode, reducedLength, i);

                        if (count > 2047 + 2078) return false;   // can't be more than 19968 / 8 = 2496

                        if (count > 2047)   // max 11-bit number
                        {
                            bigBatch = count > 2078 ? 2078 : count;
                            // Put 00000 followed by 11-bit number of bytes less 31
                            bits.Add(bigBatch - 31, 16);
                            for (int j = 0; j < bigBatch; j++) bits.Add(reducedSource[i++], 8);
                            count -= bigBatch;
                        }
                        if (count > 0)
                        {
                            if (bigBatch > 0) bits.Add(31, 5);   // B/S
                            if (count > 31)
                                bits.Add(count - 31, 16);        // 00000 followed by 11-bit number of bytes less 31
                            else
                                bits.Add(count, 5);              // 5-bit number of bytes
                            for (int j = 0; j < count; j++) bits.Add(reducedSource[i++], 8);
                        }
                        i--;
                        continue;
                    }

                    if (reducedEncodeMode[i] != 'u' && reducedEncodeMode[i] != 'p') currentMode = reducedEncodeMode[i];
                }

                if (reducedEncodeMode[i] == 'U' || reducedEncodeMode[i] == 'u' || reducedEncodeMode[i] == 'L')
                {
                    if (reducedSource[i] == ' ') bits.Add(1, 5);                 // SP
                    else bits.Add(SymbolChar[reducedSource[i]], 5);
                }
                else if (reducedEncodeMode[i] == 'M')
                {
                    if (reducedSource[i] == ' ') bits.Add(1, 5);                 // SP
                    else if (reducedSource[i] == 13) bits.Add(14, 5);            // CR
                    else bits.Add(SymbolChar[reducedSource[i]], 5);
                }
                else if (reducedEncodeMode[i] == 'P' || reducedEncodeMode[i] == 'p')
                {
                    if (reducedSource[i] == 13) bits.Add(1, 5);                  // CR
                    else if (reducedSource[i] == 'a') bits.Add(2, 5);            // CR LF
                    else if (reducedSource[i] == 'b') bits.Add(3, 5);            // . SP
                    else if (reducedSource[i] == 'c') bits.Add(4, 5);            // , SP
                    else if (reducedSource[i] == 'd') bits.Add(5, 5);            // : SP
                    else if (reducedSource[i] == ',') bits.Add(17, 5);           // Comma
                    else if (reducedSource[i] == '.') bits.Add(19, 5);           // Full stop
                    else bits.Add(SymbolChar[reducedSource[i]], 5);
                }
                else if (reducedEncodeMode[i] == 'D')
                {
                    if (reducedSource[i] == ' ') bits.Add(1, 4);                 // SP
                    else if (reducedSource[i] == ',') bits.Add(12, 4);           // Comma
                    else if (reducedSource[i] == '.') bits.Add(13, 4);           // Full stop
                    else bits.Add(SymbolChar[reducedSource[i]], 4);
                }
            }

            return !bits.Overflow;
        }

        // Count number of consecutive (. SP) or (, SP) Punct mode doubles for comparison against Digit mode encoding
        static int CountDoubles(byte[] source, int position, int length)
        {
            int i = position;
            while (i + 1 < length && (source[i] == '.' || source[i] == ',') && source[i + 1] == ' ') i += 2;
            return (i - position) >> 1;
        }

        // Count number of consecutive full stops or commas (can be encoded in Punct or Digit mode)
        static int CountDotComma(byte[] source, int position, int length)
        {
            int i = position;
            while (i < length && (source[i] == '.' || source[i] == ',')) i++;
            return i - position;
        }

        // Count number of consecutive `chr`s
        static int CountChar(byte[] source, int position, int length, byte chr)
        {
            int i = position;
            while (i < length && source[i] == chr) i++;
            return i - position;
        }

        // How many modes in a row, from `start`, are the same as the one there
        static int RunLength(char[] encodeMode, int length, int start)
        {
            int count = 0;
            while (start + count < length && encodeMode[start + count] == encodeMode[start]) count++;
            return count;
        }

        // Return mode following current, or 'E' if none
        static char NextMode(char[] encodeMode, int length, int i)
        {
            char currentMode = encodeMode[i];
            do
            {
                i++;
            } while (i < length && encodeMode[i] == currentMode);
            return i >= length ? 'E' : encodeMode[i];
        }

        static void Fill(char[] array, int start, int count, char value)
        {
            for (int i = 0; i < count; i++) array[start + i] = value;
        }

        static byte Bit(bool set) => (byte)(set ? 1 : 0);

        // z_bin_append_posn: `value` as `length` bits (1s and 0s, high bit first) from `position`; the next position
        static int Append(int value, int length, byte[] bits, int position)
        {
            for (int i = 0; i < length; i++) bits[position + i] = (byte)((value >> (length - 1 - i)) & 1);
            return position + length;
        }

        // The message bits, one to a byte, and zint's limit on how many there can be (az_bin_append_posn).
        // Once an addition goes over the limit nothing more is kept and Overflow says so.
        sealed class BitBuffer
        {
            public readonly byte[] Data;
            public int Length;
            public bool Overflow;
            readonly int capacity;

            public BitBuffer(int capacity)
            {
                this.capacity = capacity;
                Data = new byte[capacity];
            }

            public void Add(int value, int length)
            {
                if (Overflow) return;
                if (Length + length > capacity)
                {
                    Overflow = true;
                    return;
                }
                Length = Append(value, length, Data, Length);
            }
        }

        // Prevent data from obscuring reference grid
        static int AvoidReferenceGrid(int output)
        {
            if (output > 10) output += (output - 11) / 15 + 1;
            return output;
        }

        // Calculate the position of the bits in the grid (non-compact)
        static short[] PopulateMap(int layers)
        {
            var map = new short[151 * 151];
            int offset = Offset[layers - 1];
            int endOffset = 151 - offset;
            int x, y;

            for (int layer = 0; layer < layers; layer++)
            {
                int start = (112 * layer) + (16 * layer * layer) + 2;
                int length = 28 + (layer * 4) + (layer + 1) * 4;
                int av0, av1;
                int n = start, end;
                // Top
                x = 64 - (layer * 2);
                y = 63 - (layer * 2);
                av0 = AvoidReferenceGrid(y) * 151;
                av1 = AvoidReferenceGrid(y - 1) * 151;
                end = start + length;
                while (n < end)
                {
                    int avxi = AvoidReferenceGrid(x++);
                    map[av0 + avxi] = (short)n++;
                    map[av1 + avxi] = (short)n++;
                }
                // Right
                x = 78 + (layer * 2);
                y = 64 - (layer * 2);
                av0 = AvoidReferenceGrid(x);
                av1 = AvoidReferenceGrid(x + 1);
                end += length;
                while (n < end)
                {
                    int avyi = AvoidReferenceGrid(y++) * 151;
                    map[avyi + av0] = (short)n++;
                    map[avyi + av1] = (short)n++;
                }
                // Bottom
                x = 77 + (layer * 2);
                y = 78 + (layer * 2);
                av0 = AvoidReferenceGrid(y) * 151;
                av1 = AvoidReferenceGrid(y + 1) * 151;
                end += length;
                while (n < end)
                {
                    int avxi = AvoidReferenceGrid(x--);
                    map[av0 + avxi] = (short)n++;
                    map[av1 + avxi] = (short)n++;
                }
                // Left
                x = 63 - (layer * 2);
                y = 77 + (layer * 2);
                av0 = AvoidReferenceGrid(x);
                av1 = AvoidReferenceGrid(x - 1);
                end += length;
                while (n < end)
                {
                    int avyi = AvoidReferenceGrid(y--) * 151;
                    map[avyi + av0] = (short)n++;
                    map[avyi + av1] = (short)n++;
                }
            }

            // Copy "Core Symbol" (finder, descriptor, orientation)
            for (y = 0; y < 15; y++)
                for (x = 0; x < 15; x++)
                    map[(y + 68) * 151 + 68 + x] = MapCore[y * 15 + x];

            // Reference grid guide bars
            for (y = offset <= 11 ? 11 : MapGridYOffsets[(offset - 11) / 16]; y < endOffset; y += 16)
            {
                for (x = offset; x < endOffset; x++)
                {
                    map[(x * 151) + y] = (short)(x & 1);
                    map[(y * 151) + x] = (short)(x & 1);
                }
            }

            return map;
        }

        // Helper to insert dummy '0' or '1's into runs of same bits. See ISO/IEC 24778:2008 7.3.1.2.
        // Returns the new length, or 0 when it grows past `dataMaxsize`.
        static int BitrunStuff(byte[] binary, int dataLength, int codewordSize, int dataMaxsize, byte[] adjusted)
        {
            int j = 0, count = 0;

            for (int i = 0; i < dataLength; i++)
            {
                if ((j + 1) % codewordSize == 0)
                {
                    // Last bit of codeword
                    // 7.3.1.2 "whenever the first B-1 bits ... are all 0s, then a dummy 1 is inserted..."
                    // "Similarly a message codeword that starts with B-1 1s has a dummy 0 inserted..."
                    if (count == 0 || count == codewordSize - 1)
                    {
                        // Codeword of B-1 '0's or B-1 '1's
                        if (j > dataMaxsize) return 0;
                        adjusted[j++] = (byte)(count == 0 ? 1 : 0);
                        count = binary[i] == 1 ? 1 : 0;
                    }
                    else
                    {
                        count = 0;
                    }
                }
                else if (binary[i] == 1)   // Skip B so only counting B-1
                {
                    count++;
                }
                if (j > dataMaxsize) return 0;
                adjusted[j++] = binary[i];
            }

            return j;
        }

        // Helper to add padding, accounting for bitrun stuffing
        static int AddPadding(int padbits, int codewordSize, byte[] adjusted, int adjustedLength)
        {
            int count = 0;

            for (int i = 0; i < padbits; i++) adjusted[adjustedLength++] = 1;

            for (int i = adjustedLength - codewordSize; i < adjustedLength; i++) count += adjusted[i];
            if (count == codewordSize) adjusted[adjustedLength - 1] = 0;

            return adjustedLength;
        }

        // Determine codeword bitlength - Table 3
        static int CodewordSize(int layers)
        {
            if (layers <= 2) return 6;
            if (layers <= 8) return 8;
            if (layers <= 22) return 10;
            return 12;
        }

        // ---------- reedsol.c ----------

        // The Reed-Solomon check words for `data` over GF(2^bits) with the given primitive polynomial,
        // the generator's roots being 2^1, 2^2 and on (zint's rs_init_gf, rs_init_code and rs_uint_encode).
        static int[] ReedSolomon(int[] data, int dataLength, int checkLength, int primePoly, int bits)
        {
            int logmod = (1 << bits) - 1;
            var logt = new int[logmod + 1];
            var alog = new int[logmod * 2];
            for (int p = 1, v = 0; v < logmod; v++)
            {
                alog[v] = p;
                alog[logmod + v] = p;   // doubled up, avoids a mod
                logt[p] = v;
                p <<= 1;
                if ((p & (logmod + 1)) != 0) p ^= primePoly;
            }

            // The generator polynomial
            var rspoly = new int[checkLength + 1];
            rspoly[0] = 1;
            for (int i = 1, index = 1; i <= checkLength; i++, index++)
            {
                rspoly[i] = 1;
                for (int k = i - 1; k > 0; k--)
                {
                    if (rspoly[k] != 0) rspoly[k] = alog[logt[rspoly[k]] + index];   // multiply coeff by 2**index
                    rspoly[k] ^= rspoly[k - 1];                                      // add coeff of x**(k-1) * x
                }
                rspoly[0] = alog[logt[rspoly[0]] + index];
            }
            var logRspoly = new int[checkLength + 1];
            for (int i = 0; i <= checkLength; i++) logRspoly[i] = logt[rspoly[i]];

            var res = new int[checkLength];
            for (int i = 0; i < dataLength; i++)
            {
                int m = res[checkLength - 1] ^ data[i];
                if (m != 0)
                {
                    int logM = logt[m];
                    for (int k = checkLength - 1; k > 0; k--)
                        res[k] = rspoly[k] != 0 ? res[k - 1] ^ alog[logM + logRspoly[k]] : res[k - 1];
                    res[0] = alog[logM + logRspoly[0]];
                }
                else
                {
                    for (int k = checkLength - 1; k > 0; k--) res[k] = res[k - 1];
                    res[0] = 0;
                }
            }
            // Reverse the result
            for (int i = 0; i < checkLength / 2; i++)
            {
                int tmp = res[i];
                res[i] = res[checkLength - 1 - i];
                res[checkLength - 1 - i] = tmp;
            }
            return res;
        }

        // ---------- aztec.h ----------

        // The mode each ASCII character is encoded in (AztecModes): U upper, L lower, M mixed, P punct, D digit,
        // B binary, and X for the ones that depend on what is around them (CR, space, comma and full stop)
        const string Modes =
            "BMMMMMMMMMMMMXBBBBBBBBBBBBBMMMMM" +
            "XPPPPPPPPPPPXPXPDDDDDDDDDDPPPPPP" +
            "MUUUUUUUUUUUUUUUUUUUUUUUUUUPMPMM" +
            "MLLLLLLLLLLLLLLLLLLLLLLLLLLPMPMM";

        // From Table 2: the codeword of each ASCII character in the mode that holds it (AztecSymbolChar)
        static readonly byte[] SymbolChar =
        {
             0,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13,  0, 14, 15,
            16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 15, 16, 17, 18, 19,
             1,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15, 16,  0, 18,  0, 20,
             2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 21, 22, 23, 24, 25, 26,
            20,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15, 16,
            17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 27, 21, 28, 22, 23,
            24,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15, 16,
            17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 29, 25, 30, 26, 27,
        };

        // Codewords per symbol: full-size by layers, then compact (AztecSizes, AztecCompactSizes)
        static readonly int[] Sizes =
        {
              21,   48,   60,   88,  120,  156,  196,  240,  230,  272,  316,  364,  416,  470,  528,  588,
             652,  720,  790,  864,  940, 1020,  920,  992, 1066, 1144, 1224, 1306, 1392, 1480, 1570, 1664,
        };

        static readonly int[] CompactSizes =
        {
              17,   40,   51,   64,
        };

        // Data bits per symbol maximum at 10%, 23%, 36% and 50% error correction: full-size, then compact
        // (AztecDataSizes, AztecCompactDataSizes)
        static readonly int[,] DataSizes =
        {
            {
                   96,   246,   408,   616,   840,  1104,  1392,  1704,  2040,  2420,  2820,  3250,  3720,  4200,  4730,  5270,
                 5840,  6450,  7080,  7750,  8430,  9150,  9900, 10680, 11484, 12324, 13188, 14076, 15000, 15948, 16920, 17940
            },
            {
                   84,   204,   352,   520,   720,   944,  1184,  1456,  1750,  2070,  2410,  2780,  3180,  3590,  4040,  4500,
                 5000,  5520,  6060,  6630,  7210,  7830,  8472,  9132,  9816, 10536, 11280, 12036, 12828, 13644, 14472, 15348
            },
            {
                   66,   168,   288,   432,   592,   776,   984,  1208,  1450,  1720,  2000,  2300,  2640,  2980,  3350,  3740,
                 4150,  4580,  5030,  5500,  5990,  6500,  7032,  7584,  8160,  8760,  9372,  9996, 10656, 11340, 12024, 12744
            },
            {
                   48,   126,   216,   328,   456,   600,   760,   936,  1120,  1330,  1550,  1790,  2050,  2320,  2610,  2910,
                 3230,  3570,  3920,  4290,  4670,  5070,  5484,  5916,  6360,  6828,  7308,  7800,  8316,  8844,  9384,  9948
            },
        };

        static readonly int[,] CompactDataSizes =
        {
            {    78,   198,   336,   512 },
            {    66,   168,   288,   440 },
            {    48,   138,   232,   360 },
            {    36,   102,   176,   280 },
        };

        // Where the symbol starts in the 151 x 151 map, by layers (AztecOffset), and in the 27 x 27 compact map
        static readonly int[] Offset =
        {
            66, 64, 62, 60, 57, 55, 53, 51, 49, 47, 45, 42, 40, 38, 36, 34,
            32, 30, 28, 25, 23, 21, 19, 17, 15, 13, 10,  8,  6,  4,  2,  0,
        };

        static readonly int[] CompactOffset =
        {
             6,  4,  2,  0,
        };

        // Rows of the reference grid in the full-size map (AztecMapGridYOffsets)
        static readonly int[] MapGridYOffsets =
        {
             27,  43,  59,  75,  91, 107, 123, 139,
        };

        // The 27 x 27 compact map (AztecCompactMap): 0 light, 1 dark, 2 and up the position of a data or check
        // bit, 2000 and up a bit of the mode message
        static readonly short[] CompactMap =
        {
             609,  608,  411,  413,  415,  417,  419,  421,  423,  425,  427,  429,  431,  433,  435,  437,  439,  441,  443,  445,  447,  449,  451,  453,  455,  457,  459,   // 0
             607,  606,  410,  412,  414,  416,  418,  420,  422,  424,  426,  428,  430,  432,  434,  436,  438,  440,  442,  444,  446,  448,  450,  452,  454,  456,  458,   // 1
             605,  604,  409,  408,  243,  245,  247,  249,  251,  253,  255,  257,  259,  261,  263,  265,  267,  269,  271,  273,  275,  277,  279,  281,  283,  460,  461,   // 2
             603,  602,  407,  406,  242,  244,  246,  248,  250,  252,  254,  256,  258,  260,  262,  264,  266,  268,  270,  272,  274,  276,  278,  280,  282,  462,  463,   // 3
             601,  600,  405,  404,  241,  240,  107,  109,  111,  113,  115,  117,  119,  121,  123,  125,  127,  129,  131,  133,  135,  137,  139,  284,  285,  464,  465,   // 4
             599,  598,  403,  402,  239,  238,  106,  108,  110,  112,  114,  116,  118,  120,  122,  124,  126,  128,  130,  132,  134,  136,  138,  286,  287,  466,  467,   // 5
             597,  596,  401,  400,  237,  236,  105,  104,    3,    5,    7,    9,   11,   13,   15,   17,   19,   21,   23,   25,   27,  140,  141,  288,  289,  468,  469,   // 6
             595,  594,  399,  398,  235,  234,  103,  102,    2,    4,    6,    8,   10,   12,   14,   16,   18,   20,   22,   24,   26,  142,  143,  290,  291,  470,  471,   // 7
             593,  592,  397,  396,  233,  232,  101,  100,    1,    1, 2000, 2001, 2002, 2003, 2004, 2005, 2006,    0,    1,   28,   29,  144,  145,  292,  293,  472,  473,   // 8
             591,  590,  395,  394,  231,  230,   99,   98,    1,    1,    1,    1,    1,    1,    1,    1,    1,    1,    1,   30,   31,  146,  147,  294,  295,  474,  475,   // 9
             589,  588,  393,  392,  229,  228,   97,   96, 2027,    1,    0,    0,    0,    0,    0,    0,    0,    1, 2007,   32,   33,  148,  149,  296,  297,  476,  477,   // 10
             587,  586,  391,  390,  227,  226,   95,   94, 2026,    1,    0,    1,    1,    1,    1,    1,    0,    1, 2008,   34,   35,  150,  151,  298,  299,  478,  479,   // 11
             585,  584,  389,  388,  225,  224,   93,   92, 2025,    1,    0,    1,    0,    0,    0,    1,    0,    1, 2009,   36,   37,  152,  153,  300,  301,  480,  481,   // 12
             583,  582,  387,  386,  223,  222,   91,   90, 2024,    1,    0,    1,    0,    1,    0,    1,    0,    1, 2010,   38,   39,  154,  155,  302,  303,  482,  483,   // 13
             581,  580,  385,  384,  221,  220,   89,   88, 2023,    1,    0,    1,    0,    0,    0,    1,    0,    1, 2011,   40,   41,  156,  157,  304,  305,  484,  485,   // 14
             579,  578,  383,  382,  219,  218,   87,   86, 2022,    1,    0,    1,    1,    1,    1,    1,    0,    1, 2012,   42,   43,  158,  159,  306,  307,  486,  487,   // 15
             577,  576,  381,  380,  217,  216,   85,   84, 2021,    1,    0,    0,    0,    0,    0,    0,    0,    1, 2013,   44,   45,  160,  161,  308,  309,  488,  489,   // 16
             575,  574,  379,  378,  215,  214,   83,   82,    0,    1,    1,    1,    1,    1,    1,    1,    1,    1,    1,   46,   47,  162,  163,  310,  311,  490,  491,   // 17
             573,  572,  377,  376,  213,  212,   81,   80,    0,    0, 2020, 2019, 2018, 2017, 2016, 2015, 2014,    0,    0,   48,   49,  164,  165,  312,  313,  492,  493,   // 18
             571,  570,  375,  374,  211,  210,   78,   76,   74,   72,   70,   68,   66,   64,   62,   60,   58,   56,   54,   50,   51,  166,  167,  314,  315,  494,  495,   // 19
             569,  568,  373,  372,  209,  208,   79,   77,   75,   73,   71,   69,   67,   65,   63,   61,   59,   57,   55,   52,   53,  168,  169,  316,  317,  496,  497,   // 20
             567,  566,  371,  370,  206,  204,  202,  200,  198,  196,  194,  192,  190,  188,  186,  184,  182,  180,  178,  176,  174,  170,  171,  318,  319,  498,  499,   // 21
             565,  564,  369,  368,  207,  205,  203,  201,  199,  197,  195,  193,  191,  189,  187,  185,  183,  181,  179,  177,  175,  172,  173,  320,  321,  500,  501,   // 22
             563,  562,  366,  364,  362,  360,  358,  356,  354,  352,  350,  348,  346,  344,  342,  340,  338,  336,  334,  332,  330,  328,  326,  322,  323,  502,  503,   // 23
             561,  560,  367,  365,  363,  361,  359,  357,  355,  353,  351,  349,  347,  345,  343,  341,  339,  337,  335,  333,  331,  329,  327,  324,  325,  504,  505,   // 24
             558,  556,  554,  552,  550,  548,  546,  544,  542,  540,  538,  536,  534,  532,  530,  528,  526,  524,  522,  520,  518,  516,  514,  512,  510,  506,  507,   // 25
             559,  557,  555,  553,  551,  549,  547,  545,  543,  541,  539,  537,  535,  533,  531,  529,  527,  525,  523,  521,  519,  517,  515,  513,  511,  508,  509,   // 26
        };

        // The 15 x 15 core of a full-size symbol (AztecMapCore): finder, mode message, orientation
        static readonly short[] MapCore =
        {
                1,     1, 20000, 20001, 20002, 20003, 20004,     0, 20005, 20006, 20007, 20008, 20009,     0,     1,
                1,     1,     1,     1,     1,     1,     1,     1,     1,     1,     1,     1,     1,     1,     1,
            20039,     1,     0,     0,     0,     0,     0,     0,     0,     0,     0,     0,     0,     1, 20010,
            20038,     1,     0,     1,     1,     1,     1,     1,     1,     1,     1,     1,     0,     1, 20011,
            20037,     1,     0,     1,     0,     0,     0,     0,     0,     0,     0,     1,     0,     1, 20012,
            20036,     1,     0,     1,     0,     1,     1,     1,     1,     1,     0,     1,     0,     1, 20013,
            20035,     1,     0,     1,     0,     1,     0,     0,     0,     1,     0,     1,     0,     1, 20014,
                0,     1,     0,     1,     0,     1,     0,     1,     0,     1,     0,     1,     0,     1,     0,
            20034,     1,     0,     1,     0,     1,     0,     0,     0,     1,     0,     1,     0,     1, 20015,
            20033,     1,     0,     1,     0,     1,     1,     1,     1,     1,     0,     1,     0,     1, 20016,
            20032,     1,     0,     1,     0,     0,     0,     0,     0,     0,     0,     1,     0,     1, 20017,
            20031,     1,     0,     1,     1,     1,     1,     1,     1,     1,     1,     1,     0,     1, 20018,
            20030,     1,     0,     0,     0,     0,     0,     0,     0,     0,     0,     0,     0,     1, 20019,
                0,     1,     1,     1,     1,     1,     1,     1,     1,     1,     1,     1,     1,     1,     1,
                0,     0, 20029, 20028, 20027, 20026, 20025,     0, 20024, 20023, 20022, 20021, 20020,     0,     0,
        };
    }
}
