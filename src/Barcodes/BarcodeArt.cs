using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR.Barcodes
{
    // A barcode drawn from its SVG the way a browser draws the website's: the bars sharp on whole
    // pixels (the website draws them "crispEdges"), the text in the fonts a browser on Windows falls
    // back to, OCR-B to Consolas and Arimo to Arial.
    sealed class BarcodeArt
    {
        sealed class Words
        {
            public double X, Y, Size;
            public string Anchor, Family, Text;
        }

        public readonly BarcodeSvg Barcode;
        readonly List<Rect> bars = new List<Rect>();
        readonly List<Words> words = new List<Words>();

        public BarcodeArt(BarcodeSvg barcode)
        {
            Barcode = barcode;
            Match path = Regex.Match(barcode.Svg, "<path\\b[^>]*\\bd=\"([^\"]+)\"");
            if (path.Success)
                foreach (Match m in Regex.Matches(path.Groups[1].Value, "M([0-9.]+) ([0-9.]+)h([0-9.]+)v([0-9.]+)h-[0-9.]+Z"))
                    bars.Add(new Rect(Number(m.Groups[1].Value), Number(m.Groups[2].Value), Number(m.Groups[3].Value), Number(m.Groups[4].Value)));
            foreach (Match m in Regex.Matches(barcode.Svg, "<text x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" text-anchor=\"([a-z]+)\" font-family=\"([^\"]*)\" font-size=\"([0-9.]+)\">\\n   ([^\\n]*)\\n  </text>"))
            {
                words.Add(new Words
                {
                    X = Number(m.Groups[1].Value), Y = Number(m.Groups[2].Value), Anchor = m.Groups[3].Value,
                    Family = m.Groups[4].Value, Size = Number(m.Groups[5].Value), Text = Unescape(m.Groups[6].Value),
                });
            }
        }

        // the five characters zint's SVG writes as entities
        static string Unescape(string text) =>
            text.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");

        public int W => Barcode.Width;
        public int H => Barcode.Height;

        static double Number(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        // the font a browser on Windows would use for the SVG's font-family
        static FontFamily Font(string family) =>
            new FontFamily(family.StartsWith("OCRB", StringComparison.Ordinal) ? "OCRB, OCR-B, Consolas, Courier New" : "Arimo, Arial, Segoe UI");

        // `scale` pixels per module, the bars in `ink` on `paper` (null: see-through), on `under` if given
        // (a JPG's white, say)
        public BitmapSource Render(double scale, Color ink, Color? paper, Color? under = null, double dpi = 96)
        {
            int width = Math.Max(1, (int)Math.Round(W * scale)), height = Math.Max(1, (int)Math.Round(H * scale));
            var visual = new DrawingVisual();
            RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
            using (DrawingContext dc = visual.RenderOpen())
            {
                var whole = new Rect(0, 0, width, height);
                if (under.HasValue) dc.DrawRectangle(new SolidColorBrush(under.Value), null, whole);
                if (paper.HasValue) dc.DrawRectangle(new SolidColorBrush(paper.Value), null, whole);
                var brush = new SolidColorBrush(ink);
                brush.Freeze();
                foreach (Rect bar in bars)
                {
                    double x0 = Math.Round(bar.X * scale), y0 = Math.Round(bar.Y * scale);
                    double x1 = Math.Round((bar.X + bar.Width) * scale), y1 = Math.Round((bar.Y + bar.Height) * scale);
                    if (x1 > x0 && y1 > y0) dc.DrawRectangle(brush, null, new Rect(x0, y0, x1 - x0, y1 - y0));
                }
                foreach (Words w in words)
                {
                    var text = new FormattedText(w.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Font(w.Family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                                                 w.Size * scale, brush, 1.0);
                    double across = text.WidthIncludingTrailingWhitespace;
                    double x = w.X * scale - (w.Anchor == "middle" ? across / 2 : w.Anchor == "end" ? across : 0);
                    dc.DrawText(text, new Point(x, w.Y * scale - text.Baseline));
                }
            }
            var picture = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            picture.Render(visual);
            picture.Freeze();
            if (Math.Abs(dpi - 96) < 0.001) return picture;
            // the same pixels, marked with the print resolution
            var pixels = new byte[width * height * 4];
            picture.CopyPixels(pixels, width * 4, 0);
            BitmapSource marked = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Pbgra32, null, pixels, width * 4);
            marked.Freeze();
            return marked;
        }
    }
}
