using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using B0r3dQR.Design;

namespace B0r3dQR.Barcodes
{
    // the website's options for a barcode
    sealed class BarcodeOptions
    {
        public bool ShowText = true;           // the text under the bars of a linear code
        public bool AllowRectangular;          // Data Matrix: rectangles too (narrower; fewer scanners read them)
        public int BarHeight = Barcode.DefaultBarHeight;   // linear codes, in modules
        public bool QuietZones = true;         // the blank margins (always on for ITF-14)
    }

    // A barcode as the website keeps it: zint's SVG at one unit per module, and its size in modules,
    // quiet zones and text included (its src/lib/barcode/svg.ts).
    sealed class BarcodeSvg
    {
        public string Svg;
        public int Width, Height;

        // the size from the <svg> tag (the website's cleanZintSvg; ZintSvg already leaves it tidied)
        public static BarcodeSvg From(string svg)
        {
            string open = Regex.Match(svg, "<svg\\b[^>]*>").Value;
            return new BarcodeSvg
            {
                Svg = svg,
                Width = int.Parse(Regex.Match(open, "\\bwidth=\"([0-9.]+)\"").Groups[1].Value, CultureInfo.InvariantCulture),
                Height = int.Parse(Regex.Match(open, "\\bheight=\"([0-9.]+)\"").Groups[1].Value, CultureInfo.InvariantCulture),
            };
        }

        static readonly Regex OneRect = new Regex("^M([0-9.]+) ([0-9.]+)h([0-9.]+)v([0-9.]+)h-[0-9.]+Z\\z");

        // The bars `barHeight` modules tall instead of zint's 50, as the website does it: whatever reaches the
        // bottom of the bars grows or shrinks by the difference (the bars, EAN/UPC's longer guard bars,
        // ITF-14's sides); whatever lies below moves (ITF-14's bottom bar, the text). A drawing that isn't all
        // rectangles stays as it is.
        public BarcodeSvg WithBarHeight(int barHeight)
        {
            int delta = barHeight - Barcode.DefaultBarHeight;
            if (delta == 0) return this;
            Match path = Regex.Match(Svg, "(<path\\b[^>]*\\bd=\")([^\"]+)(\")");
            if (!path.Success) return this;
            var boxes = new List<double[]>();
            string d = path.Groups[2].Value;
            for (int start = 0; start < d.Length;)
            {
                int end = d.IndexOf('M', start + 1);
                if (end < 0) end = d.Length;
                Match rect = OneRect.Match(d.Substring(start, end - start));
                if (!rect.Success) return this;
                boxes.Add(new[] { Parse(rect.Groups[1].Value), Parse(rect.Groups[2].Value), Parse(rect.Groups[3].Value), Parse(rect.Groups[4].Value) });
                start = end;
            }

            // the bottom of the bars: where most of them end (the first such place on a tie, as a stable sort leaves it)
            var ends = new List<KeyValuePair<double, int>>();
            foreach (double[] b in boxes)
            {
                int at = ends.FindIndex(e => e.Key == b[1] + b[3]);
                if (at < 0) ends.Add(new KeyValuePair<double, int>(b[1] + b[3], 1));
                else ends[at] = new KeyValuePair<double, int>(ends[at].Key, ends[at].Value + 1);
            }
            double bottom = ends[0].Key;
            int most = ends[0].Value;
            foreach (var e in ends)
                if (e.Value > most)
                {
                    most = e.Value;
                    bottom = e.Key;
                }
            foreach (double[] b in boxes)
                if (b[1] < bottom && b[1] + b[3] >= bottom && b[3] + delta <= 0) return this;

            var moved = new StringBuilder();
            foreach (double[] b in boxes)
            {
                double x = b[0], y = b[1], w = b[2], h = b[3];
                if (y >= bottom) y += delta;
                else if (y + h >= bottom) h += delta;
                moved.Append('M').Append(Js.NumberToString(x)).Append(' ').Append(Js.NumberToString(y)).Append('h').Append(Js.NumberToString(w))
                     .Append('v').Append(Js.NumberToString(h)).Append("h-").Append(Js.NumberToString(w)).Append('Z');
            }
            int height = Height + delta;
            string svg = Svg.Remove(path.Index, path.Length).Insert(path.Index, path.Groups[1].Value + moved + path.Groups[3].Value);
            svg = new Regex("<svg\\b[^>]*>").Replace(svg, open => new Regex("\\bheight=\"[0-9.]+\"").Replace(open.Value, "height=\"" + height + "\"", 1), 1);
            svg = new Regex("(<rect\\b[^>]*\\bheight=\")[0-9.]+(\")").Replace(svg, m => m.Groups[1].Value + height + m.Groups[2].Value, 1);
            svg = Regex.Replace(svg, "(<text\\b[^>]*\\by=\")([0-9.]+)(\")", m =>
            {
                double y = Parse(m.Groups[2].Value);
                if (y < bottom) return m.Value;
                double moved2 = y + delta;
                string fixedPoint = moved2 < 0 ? "-" + Js.ToFixed2(-moved2) : Js.ToFixed2(moved2);   // +(y + delta).toFixed(2)
                return m.Groups[1].Value + Js.NumberToString(Parse(fixedPoint)) + m.Groups[3].Value;
            });
            return new BarcodeSvg { Svg = svg, Width = Width, Height = height };
        }

        static double Parse(string number) => double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);

        // The SVG in colour at `scale` pixels per module (the website's styleBarcodeSvg); its viewBox stays in
        // modules, so a print size only changes its width and height. "transparent" for no background.
        public string Styled(string color, string background, double scale)
        {
            bool transparent = background == "transparent";
            string svg = new Regex("<svg\\b[^>]*>").Replace(Svg, m =>
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 " + Width + " " + Height + "\" width=\"" + Js.NumberToString(Width * scale) +
                "\" height=\"" + Js.NumberToString(Height * scale) + "\" shape-rendering=\"crispEdges\">", 1);
            svg = new Regex("(<g id=\"barcode\" fill=\")[^\"]*\"").Replace(svg, m => m.Groups[1].Value + Attribute(color) + "\"", 1);
            svg = new Regex("<rect x=\"0\" y=\"0\" width=\"[0-9.]+\" height=\"[0-9.]+\" fill=\"[^\"]*\"/>\\s*").Replace(svg, m =>
                transparent ? "" : new Regex("fill=\"[^\"]*\"").Replace(m.Value, "fill=\"" + Attribute(background) + "\"", 1), 1);
            return svg;
        }

        static string Attribute(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    // Making a barcode as the website's src/lib/barcode/create.ts makes it: the type's own checks first,
    // then zint's (its rules followed by the encoders here), then zint's drawing, given its bar height.
    static class Barcode
    {
        public const int DefaultBarHeight = 50, MinBarHeight = 20, MaxBarHeight = 150;

        // the barcode, or null with the message the website shows
        public static BarcodeSvg Make(string text, BarcodeType type, BarcodeOptions options, out string error)
        {
            BarcodeFormat format = BarcodeFormats.Get(type);
            string prepared = format.Prepare(text ?? "");
            error = format.Check(prepared);
            if (error != null) return null;
            ZintSymbol symbol = Encode(type, format.EncodeText != null ? format.EncodeText(prepared) : prepared, options.AllowRectangular, out error);
            if (symbol == null)
            {
                error = Friendly(error);
                return null;
            }
            BarcodeSvg barcode = BarcodeSvg.From(ZintSvg.Draw(symbol, format.Linear && options.ShowText, options.QuietZones || format.KeepsQuietZones));
            return format.Linear ? barcode.WithBarHeight(options.BarHeight) : barcode;
        }

        // a code's modules as zint holds them, each row as many times as it's high, or null with the message
        // (the tests hold the 2D ones up to the website's)
        internal static List<bool[]> Modules(string text, BarcodeType type, bool allowRectangular, out string error)
        {
            BarcodeFormat format = BarcodeFormats.Get(type);
            string prepared = format.Prepare(text ?? "");
            error = format.Check(prepared);
            if (error != null) return null;
            ZintSymbol symbol = Encode(type, format.EncodeText != null ? format.EncodeText(prepared) : prepared, allowRectangular, out error);
            if (symbol == null)
            {
                error = Friendly(error);
                return null;
            }
            // a row of no height of its own (PDF417's) is as high as zint makes those: what's left of the
            // symbol's height, shared out
            float fixedHeight = 0;
            int unfixed = 0;
            foreach (float height in symbol.RowHeights)
                if (height > 0) fixedHeight += height;
                else unfixed++;
            float shared = unfixed > 0 ? (symbol.Height - fixedHeight) / unfixed : 0;
            var rows = new List<bool[]>();
            for (int r = 0; r < symbol.Rows.Count; r++)
                for (int k = 0; k < (int)(symbol.RowHeights[r] > 0 ? symbol.RowHeights[r] : shared); k++) rows.Add(symbol.Rows[r]);
            return rows;
        }

        // zint 2.16 with the website's settings (zxing-cpp's CreateBarcode): text in UTF-8, GS1 data in round
        // brackets, Data Matrix square unless rectangles are allowed (the larger DMRE ones included)
        static ZintSymbol Encode(BarcodeType type, string input, bool allowRectangular, out string error)
        {
            // zint's own first checks (library.c), for every type: no more than 17,400 bytes; then, for all but
            // GS1 data, the text must be real UTF-8 (zxing-wasm passes a lone surrogate on as three bytes that
            // aren't), and a byte-order mark at the start is dropped unless it's all there is
            if (Utf8Length(input) > MaxInputBytes)
            {
                error = "Input too long";
                return null;
            }
            if (type != BarcodeType.Gs1DataMatrix && type != BarcodeType.Gs1_128)
            {
                if (HasLoneSurrogate(input))
                {
                    error = "Invalid UTF-8 in input";
                    return null;
                }
                if (input.Length > 1 && input[0] == (char)0xFEFF) input = input.Substring(1);
            }
            switch (type)
            {
                case BarcodeType.DataMatrix:
                    return Matrix(Symbology.DataMatrix, DataMatrix.Encode(input, false, !allowRectangular, out error), 1);
                case BarcodeType.Gs1DataMatrix:
                {
                    string reduced = Gs1.Reduce(input, true, out error);
                    return reduced == null ? null : Matrix(Symbology.DataMatrix, DataMatrix.Encode(reduced, true, !allowRectangular, out error), 1);
                }
                case BarcodeType.Aztec:
                    return Matrix(Symbology.Aztec, Aztec.Encode(input, out error), 1);
                case BarcodeType.Pdf417:
                    return Matrix(Symbology.Pdf417, Pdf417.Encode(input, out error), 0);
                case BarcodeType.Gs1_128:
                    return Code128.Gs1(input, out error);
            }
            // the others take ISO/IEC 8859-1 only, which for zint has no C1 controls (U+0080 to U+009F)
            foreach (char c in input)
                if (c > 0xFF || c >= 0x80 && c < 0xA0)
                {
                    error = "Invalid character in input (ISO/IEC 8859-1 only)";
                    return null;
                }
            switch (type)
            {
                case BarcodeType.Ean13: return Linear.Ean13(input, Symbology.Ean13, out error);
                case BarcodeType.Ean8: return Linear.Ean8(input, out error);
                case BarcodeType.UpcA: return Linear.UpcA(input, out error);
                case BarcodeType.UpcE: return Linear.UpcE(input, out error);
                case BarcodeType.Isbn: return Linear.Isbn(input, out error);
                case BarcodeType.Itf14: return Linear.Itf14(input, out error);
                case BarcodeType.Code39: return Linear.Code39(input, out error);
                default: return Code128.Make(input, out error);
            }
        }

        const int MaxInputBytes = 17400;    // zint's ZINT_MAX_DATA_LEN

        // the text's length in UTF-8 as zxing-wasm counts it for zint (Emscripten's lengthBytesUTF8): any
        // surrogate is taken for the first of a pair, four bytes with the unit after it, even a lone one
        internal static int Utf8Length(string text)
        {
            int bytes = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < 0x80) bytes += 1;
                else if (c < 0x800) bytes += 2;
                else if (char.IsSurrogate(c))
                {
                    bytes += 4;
                    i++;
                }
                else bytes += 3;
            }
            return bytes;
        }

        static bool HasLoneSurrogate(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                else if (char.IsSurrogate(text[i])) return true;
            }
            return false;
        }

        // a 2D code's rows as zint holds them: Data Matrix and Aztec rows one module high, PDF417's three
        static ZintSymbol Matrix(Symbology symbology, bool[][] rows, float rowHeight)
        {
            if (rows == null) return null;
            var symbol = new ZintSymbol(symbology);
            foreach (bool[] row in rows) symbol.Add(row, rowHeight);
            symbol.SetHeight(3, 0);
            return symbol;
        }

        // zint's message as the website shows it: "Error 275: Invalid ... (retval: 7)" without the number parts
        static string Friendly(string message)
        {
            string text = Regex.Replace(Regex.Replace(message ?? "", "^Error [0-9]+:\\s*", ""), "\\s*\\(retval: [0-9]+\\)\\s*\\z", "").Trim();
            return text.Length > 0 ? Js.ToUpperCase(text.Substring(0, 1)) + text.Substring(1) : "This text cannot be encoded.";
        }
    }
}
