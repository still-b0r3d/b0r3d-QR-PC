using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace B0r3dQR.Barcodes
{
    // zint 2.16's drawing of a barcode as the website gets it: vector.c's zint_plot_vector, output.c's
    // quiet zones and svg.c's SVG, for the types and settings the website uses (BSD-3-Clause, see
    // THIRD-PARTY-NOTICES.txt), down to zint's float arithmetic and how it writes numbers. The SVG
    // comes out as the website's cleanZintSvg leaves it: from the <svg> tag on, without the
    // description, one unit per module.
    static class ZintSvg
    {
        sealed class Box
        {
            public float X, Y, W, H;
        }

        sealed class Words
        {
            public float X, Y, Size;
            public int Align;   // 0 middle, 1 start, 2 end
            public string Text;
        }

        // the drawing's SVG; `showText` puts the text under the bars, `quietZones` the margins zint has for the type
        public static string Draw(ZintSymbol symbol, bool showText, bool quietZones)
        {
            float largeBarHeight = LargeBarHeight(symbol);
            int upcean = UpceanFlag(symbol);
            int mainWidth = upcean == 13 || upcean == 12 ? 95 : upcean == 8 ? 68 : upcean == 6 ? 51 : symbol.Width;
            bool hideText = !showText || symbol.Text.Length == 0;

            QuietZones(symbol, hideText, quietZones, out float left, out float right, out float top, out float bottom);
            float xoffset = left, roffset = right, yoffset = top, boffset = bottom;
            if (symbol.Box)
            {
                xoffset = (float)(xoffset + symbol.BorderWidth);
                roffset = (float)(roffset + symbol.BorderWidth);
                yoffset = (float)(yoffset + symbol.BorderWidth);
                boffset = (float)(boffset + symbol.BorderWidth);
            }
            int xoffsetComp = (int)xoffset;
            float width = (float)((float)symbol.Width + (float)(xoffset + roffset));

            // text sizes (zint's are doubled, so these are its halves), and the gap under the bars
            int fontHeight;
            float textGap, guardDescent, digitAscender = 0, antialiasFudge;
            if (upcean != 0)
            {
                fontHeight = 10;
                digitAscender = (float)(fontHeight * 0.22f);
                antialiasFudge = (float)(fontHeight * 0.02f);
                textGap = (float)(1.0f - digitAscender);
                guardDescent = 5.0f;
            }
            else
            {
                fontHeight = 7;
                antialiasFudge = (float)(fontHeight * 0.02f);
                textGap = 1.0f;
                guardDescent = 0.0f;
            }
            float textOffset;
            if (hideText) textOffset = guardDescent;
            else
            {
                textOffset = (float)(fontHeight + (float)(textGap + antialiasFudge));
                if (upcean != 0 && textOffset < guardDescent) textOffset = guardDescent;
            }
            float height = (float)((float)(symbol.Height + textOffset) + (float)(yoffset + boffset));

            // the bars or modules, run by run along each row
            var boxes = new List<Box>();
            float y = yoffset;
            for (int r = 0; r < symbol.Rows.Count; r++)
            {
                float rowHeight = symbol.RowHeights[r] != 0 ? symbol.RowHeights[r] : largeBarHeight;
                int run;
                for (int i = 0; i < symbol.Width; i += run)
                {
                    bool fill = symbol.IsSet(r, i);
                    for (run = 1; i + run < symbol.Width && symbol.IsSet(r, i + run) == fill; run++) { }
                    if (fill) boxes.Add(new Box { X = (float)(i + xoffset), Y = y, W = run, H = rowHeight });
                }
                y = (float)(y + rowHeight);
            }

            // EAN and UPC guard bars reach down into the text
            if (upcean != 0)
            {
                int[] guards = upcean == 6 ? new[] { 0, 1, 14, 15, 16 }
                    : upcean == 8 ? new[] { 0, 1, 10, 11, 20, 21 }
                    : upcean == 12 ? new[] { 0, 1, 2, 3, 14, 15, 26, 27, 28, 29 }
                    : new[] { 0, 1, 14, 15, 28, 29 };
                foreach (int g in guards)
                    if (g < boxes.Count) boxes[g].H = (float)(boxes[g].H + guardDescent);
            }

            var words = new List<Words>();
            if (!hideText)
            {
                if (upcean != 0)
                {
                    float textY = (float)((float)((float)((float)(yoffset + symbol.Height) + fontHeight) + textGap) - antialiasFudge);
                    string t = symbol.Text;
                    if (upcean == 6)
                    {
                        words.Add(Say(t.Substring(0, 1), (float)(-(5.0f - 0.35f) + xoffsetComp), textY, 7, 2));
                        words.Add(Say(t.Substring(1, 6), (float)((24.0f + 0.5f) + xoffsetComp), textY, fontHeight, 0));
                        words.Add(Say(t.Substring(7, 1), (float)((float)((51.0f - 0.35f) + 2.0f) + xoffsetComp), textY, 7, 1));
                    }
                    else if (upcean == 8)
                    {
                        words.Add(Say(t.Substring(0, 4), (float)((17.0f + 0.5f) + xoffsetComp), textY, fontHeight, 0));
                        words.Add(Say(t.Substring(4, 4), (float)((50.0f - 0.5f) + xoffsetComp), textY, fontHeight, 0));
                    }
                    else if (upcean == 12)
                    {
                        words.Add(Say(t.Substring(0, 1), (float)(-(5.0f - 0.35f) + xoffsetComp), textY, 7, 2));
                        words.Add(Say(t.Substring(1, 5), (float)(28.0f + xoffsetComp), textY, fontHeight, 0));
                        words.Add(Say(t.Substring(6, 5), (float)(67.0f + xoffsetComp), textY, fontHeight, 0));
                        words.Add(Say(t.Substring(11, 1), (float)((float)((95.0f - 0.35f) + 4.0f) + xoffsetComp), textY, 7, 1));
                    }
                    else
                    {
                        words.Add(Say(t.Substring(0, 1), (float)(-(5.0f - 0.1f) + xoffsetComp), textY, fontHeight, 2));
                        words.Add(Say(t.Substring(1, 6), (float)((24.0f + 0.5f) + xoffsetComp), textY, fontHeight, 0));
                        words.Add(Say(t.Substring(7, 6), (float)((71.0f - 0.5f) + xoffsetComp), textY, fontHeight, 0));
                    }
                }
                else
                {
                    float textX = (float)(mainWidth / 2.0f + xoffsetComp);
                    float textY = (float)((float)((float)(yoffset + symbol.Height) + fontHeight) + textGap);
                    textY = (float)(textY - 1.32779717f);   // Arimo's descent at this size
                    if (symbol.Box) textY = (float)(textY + symbol.BorderWidth);
                    words.Add(Say(symbol.Text, textX, textY, fontHeight, 0));
                }
            }

            // ITF-14's box: top and bottom across the whole width, then the sides
            if (symbol.Box && symbol.BorderWidth > 0)
            {
                float border = symbol.BorderWidth;
                float bindBottom = (float)((float)(height - textOffset) - boffset);
                boxes.Add(new Box { X = 0, Y = (float)(yoffset - border), W = width, H = border });
                boxes.Add(new Box { X = 0, Y = bindBottom, W = width, H = border });
                float boxHeight = (float)((float)((float)(height - textOffset) - yoffset) - boffset);
                boxes.Add(new Box { X = 0, Y = yoffset, W = border, H = boxHeight });
                boxes.Add(new Box { X = (float)(width - border), Y = yoffset, W = border, H = boxHeight });
            }

            Merge(boxes);
            return Write(symbol, width, height, boxes, words);
        }

        static Words Say(string text, float x, float y, float size, int align) => new Words { Text = text, X = x, Y = y, Size = size, Align = align };

        // the height of rows without one of their own, the symbol's height set to match (zint_out_large_bar_height)
        static float LargeBarHeight(ZintSymbol symbol)
        {
            float fixedHeight = 0;
            int zeroes = 0;
            foreach (float h in symbol.RowHeights)
            {
                if (h != 0) fixedHeight = (float)(fixedHeight + h);
                else zeroes++;
            }
            if (zeroes == 0) return 0;
            float large = (float)((float)(symbol.Height - fixedHeight) / zeroes);
            symbol.Height = (float)((float)(large * zeroes) + fixedHeight);
            return large;
        }

        // which EAN/UPC layout the text takes (zint_out_process_upcean, without add-ons)
        static int UpceanFlag(ZintSymbol symbol)
        {
            switch (symbol.Symbology)
            {
                case Symbology.Ean13:
                case Symbology.Isbn: return 13;
                case Symbology.Ean8: return 8;
                case Symbology.UpcA: return 12;
                case Symbology.UpcE: return 6;
                default: return 0;
            }
        }

        static bool IsUpcean(ZintSymbol symbol) => UpceanFlag(symbol) != 0;

        // zint's quiet zones for each type (out_quiet_zones): EAN, UPC and ITF-14 always have theirs
        // (EAN and UPC keep room for their outside digits even without), the others only when asked
        static void QuietZones(ZintSymbol symbol, bool hideText, bool quietZones, out float left, out float right, out float top, out float bottom)
        {
            left = right = top = bottom = 0;
            switch (symbol.Symbology)
            {
                case Symbology.Itf14:
                    if (quietZones) left = right = 10;
                    return;
                case Symbology.Ean13:
                case Symbology.Isbn:
                    if (quietZones)
                    {
                        left = 11;
                        right = 7;
                    }
                    else if (!hideText) left = 11;
                    return;
                case Symbology.Ean8:
                    if (quietZones) left = right = 7;
                    return;
                case Symbology.UpcA:
                    if (quietZones || !hideText) left = right = 9;
                    return;
                case Symbology.UpcE:
                    if (quietZones || !hideText)
                    {
                        left = 9;
                        right = 7;
                    }
                    return;
            }
            if (!quietZones) return;
            switch (symbol.Symbology)
            {
                case Symbology.Code39:
                case Symbology.Code128:
                case Symbology.Gs1_128:
                    left = right = 10;
                    break;
                case Symbology.Pdf417:
                    left = right = top = bottom = 2;
                    break;
                case Symbology.DataMatrix:
                    left = right = top = bottom = 1;
                    break;
            }
        }

        // Boxes one under the other, alike in x and width, become one (vector_reduce_rectangles): each box
        // takes the next box that starts where it ends, again and again, in the order they were made
        static void Merge(List<Box> boxes)
        {
            var below = new Dictionary<(float, float, float), List<int>>();
            for (int i = 0; i < boxes.Count; i++)
            {
                var key = (boxes[i].X, boxes[i].W, boxes[i].Y);
                if (!below.TryGetValue(key, out List<int> list)) below[key] = list = new List<int>();
                list.Add(i);
            }
            var gone = new bool[boxes.Count];
            var kept = new List<Box>(boxes.Count);
            for (int i = 0; i < boxes.Count; i++)
            {
                if (gone[i]) continue;
                Box box = boxes[i];
                int after = i;
                while (below.TryGetValue((box.X, box.W, (float)(box.Y + box.H)), out List<int> list))
                {
                    int next = list.FindIndex(j => j > after && !gone[j]);
                    if (next < 0) break;
                    int j2 = list[next];
                    box.H = (float)(box.H + boxes[j2].H);
                    gone[j2] = true;
                    after = j2;
                }
                kept.Add(box);
            }
            boxes.Clear();
            boxes.AddRange(kept);
        }

        // svg.c's SVG, as the website keeps it (black on white; the website recolours it later)
        static string Write(ZintSymbol symbol, float width, float height, List<Box> boxes, List<Words> words)
        {
            int w = (int)Math.Ceiling(width), h = (int)Math.Ceiling(height);
            var svg = new StringBuilder();
            svg.Append("<svg width=\"").Append(w).Append("\" height=\"").Append(h).Append("\" version=\"1.1\" xmlns=\"http://www.w3.org/2000/svg\">\n");
            svg.Append(" <g id=\"barcode\" fill=\"#000000\">\n");
            svg.Append("  <rect x=\"0\" y=\"0\" width=\"").Append(w).Append("\" height=\"").Append(h).Append("\" fill=\"#FFFFFF\"/>\n");
            if (boxes.Count > 0)
            {
                svg.Append("  <path d=\"");
                foreach (Box b in boxes)
                {
                    svg.Append('M').Append(Number(b.X, 2)).Append(' ').Append(Number(b.Y, 2)).Append('h').Append(Number(b.W, 2))
                       .Append('v').Append(Number(b.H, 2)).Append("h-").Append(Number(b.W, 2)).Append('Z');
                }
                svg.Append("\"/>\n");
            }
            string family = IsUpcean(symbol) ? "OCRB, monospace" : "Arimo, Arial, sans-serif";
            foreach (Words t in words)
            {
                svg.Append("  <text x=\"").Append(Number(t.X, 2)).Append("\" y=\"").Append(Number(t.Y, 2)).Append("\" text-anchor=\"")
                   .Append(t.Align == 2 ? "end" : t.Align == 1 ? "start" : "middle").Append("\" font-family=\"").Append(family)
                   .Append("\" font-size=\"").Append(Number(t.Size, 1)).Append("\">\n   ").Append(Escape(t.Text)).Append("\n  </text>\n");
            }
            svg.Append(" </g>\n</svg>\n");
            return svg.ToString();
        }

        static string Escape(string text) =>
            text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");

        // A number as zint writes it: printf's "%.*f" of the float's exact value (a half going to the
        // even digit), with no trailing zeros or bare point
        public static string Number(float value, int decimals)
        {
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            int exponent = (bits >> 23) & 0xFF;
            long mantissa = bits & 0x7FFFFF;
            if (exponent == 0) exponent = 1;
            else mantissa |= 0x800000;
            exponent -= 150;   // the value is mantissa x 2^exponent
            BigInteger scaled = BigInteger.Pow(10, decimals) * mantissa;
            BigInteger whole;
            if (exponent >= 0) whole = scaled << exponent;
            else
            {
                BigInteger divisor = BigInteger.One << -exponent;
                whole = BigInteger.DivRem(scaled, divisor, out BigInteger rest);
                int half = (rest * 2).CompareTo(divisor);
                if (half > 0 || (half == 0 && !whole.IsEven)) whole += 1;
            }
            string digits = whole.ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
            string text = (bits < 0 ? "-" : "") + digits.Substring(0, digits.Length - decimals) + "." + digits.Substring(digits.Length - decimals);
            text = text.TrimEnd('0');
            return text.EndsWith(".", StringComparison.Ordinal) ? text.Substring(0, text.Length - 1) : text;
        }
    }
}
