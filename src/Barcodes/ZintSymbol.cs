using System.Collections.Generic;

namespace B0r3dQR.Barcodes
{
    // The kinds of barcode zint draws differently
    enum Symbology { Ean13, Ean8, UpcA, UpcE, Isbn, Code128, Gs1_128, Itf14, Code39, DataMatrix, Aztec, Pdf417 }

    // A barcode as zint 2.16 holds it once it's made (the parts of its struct zint_symbol the website's
    // drawings use): rows of modules, each row's height (0: as tall as the bars), the whole height, the
    // text under the bars, and ITF-14's box. The barcodes follow zint's rules so they come out as the
    // website's do; zint's licence (BSD-3-Clause) is in THIRD-PARTY-NOTICES.txt.
    sealed class ZintSymbol
    {
        public readonly Symbology Symbology;
        public readonly List<bool[]> Rows = new List<bool[]>();
        public readonly List<float> RowHeights = new List<float>();
        public int Width;
        public float Height;
        public string Text = "";
        public int BorderWidth;
        public bool Box;

        public ZintSymbol(Symbology symbology) => Symbology = symbology;

        public bool IsSet(int row, int column) => column < Rows[row].Length && Rows[row][column];

        // a row from the widths of its bars and spaces in turn, starting with a bar (zint's z_expand)
        public void Expand(string widths)
        {
            var row = new List<bool>();
            bool bar = true;
            foreach (char width in widths)
            {
                for (int i = 0; i < width - '0'; i++) row.Add(bar);
                bar = !bar;
            }
            Add(row.ToArray(), 0);
        }

        public void Add(bool[] row, float height)
        {
            Rows.Add(row);
            RowHeights.Add(height);
            if (row.Length > Width) Width = row.Length;
        }

        // zint's z_set_height with no height asked for: the rows without one share the default, or each
        // is the least height when there's no default
        public void SetHeight(float leastRowHeight, float defaultHeight)
        {
            float fixedHeight = 0;
            int zeroes = 0;
            foreach (float height in RowHeights)
            {
                if (height != 0) fixedHeight = (float)(fixedHeight + height);
                else zeroes++;
            }
            if (zeroes == 0)
            {
                Height = fixedHeight;
                return;
            }
            float rowHeight = defaultHeight != 0 ? (float)(defaultHeight / zeroes) : leastRowHeight;
            if (rowHeight < 0.5f) rowHeight = 0.5f;
            Height = (float)((float)(rowHeight * zeroes) + fixedHeight);
        }

        // zint's GS1 check digit for the digits given (zint_gs1_check_digit)
        public static char CheckDigit(string digits)
        {
            int sum = 0, factor = (digits.Length & 1) != 0 ? 3 : 1;
            foreach (char c in digits)
            {
                sum += factor * (c - '0');
                factor ^= 2;
            }
            return (char)('0' + (10 - sum % 10) % 10);
        }
    }
}
