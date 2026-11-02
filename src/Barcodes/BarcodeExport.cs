using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Design;
using B0r3dQR.Qr;
using ZXing;

namespace B0r3dQR.Barcodes
{
    // A barcode's settings as the website keeps them (its BarcodeSettings): the bars' colour, the
    // background or none, pixels per module, and how the barcode is made.
    sealed class BarcodeLook
    {
        public const int MinScale = 1, MaxScale = 20;

        public string Color = "#000000", Background = "#ffffff";
        public bool Transparent;
        public int Scale = 4;
        public bool ShowText = true, AllowRectangular, QuietZones = true;
        public int BarHeight = Barcode.DefaultBarHeight;

        public string Paper => Transparent ? "transparent" : Background;

        public BarcodeOptions Options => new BarcodeOptions { ShowText = ShowText, AllowRectangular = AllowRectangular, BarHeight = BarHeight, QuietZones = QuietZones };

        public BarcodeLook Clone() => (BarcodeLook)MemberwiseClone();
    }

    // a print size for a barcode, every module whole pixels (the website's barcodePrintGuidance)
    sealed class BarcodeGuidance
    {
        public int WidthPx, HeightPx, ModulePx;
        public double ModuleMm, ActualWidthMm;
        public bool TooSmall;
    }

    // A barcode's files as the website makes them (src/components/BarcodeCreate.vue): pictures at whole
    // pixels per module, SVG at its print size, PDF; the warnings and the scan check.
    static class BarcodeExport
    {
        public const double MinBarMm = 0.25;   // narrower bars are hard to print and scan
        public const int CheckScale = 4;       // pixels per module for the scan check

        public static BarcodeGuidance Guidance(PrintSize print, BarcodeSvg barcode)
        {
            int modulePx = Math.Max(1, (int)Math.Round((double)print.PixelWidth / barcode.Width, MidpointRounding.AwayFromZero));
            double moduleMm = modulePx / (double)print.Dpi * 25.4;
            return new BarcodeGuidance
            {
                WidthPx = modulePx * barcode.Width, HeightPx = modulePx * barcode.Height, ModulePx = modulePx,
                ModuleMm = moduleMm, ActualWidthMm = moduleMm * barcode.Width, TooSmall = moduleMm < MinBarMm,
            };
        }

        // pixels per module in files: the print size's when it applies, else Size
        public static int ModulePx(BarcodeSvg barcode, BarcodeLook look, PrintSize print) => print.Applies ? Guidance(print, barcode).ModulePx : look.Scale;

        static Color Paint(string css) => Css.Paint(css);

        // the PNG, JPG and clipboard picture: a JPG gets the background (white for none) under it
        public static BitmapSource Picture(BarcodeArt art, BarcodeLook look, PrintSize print, bool forJpg = false)
        {
            int modulePx = ModulePx(art.Barcode, look, print);
            Allowed(art.W * modulePx, art.H * modulePx);
            // a JPG has no see-through: on white then, as the website does for "transparent" (and so for a colour of no opacity)
            Color background = Paint(look.Background);
            Color? under = forJpg ? (look.Transparent || background.A == 0 ? Colors.White : background) : (Color?)null;
            return art.Render(modulePx, Paint(look.Color), look.Transparent ? (Color?)null : Paint(look.Background), under, print.Applies ? print.Dpi : 96);
        }

        static void Allowed(long width, long height)
        {
            if (width * height > QrExport.MostPixels)
                throw new QrException("That picture would be " + width.ToString("N0", CultureInfo.InvariantCulture) + " × " + height.ToString("N0", CultureInfo.InvariantCulture) +
                    " pixels, more than b0r3d QR draws at once (100 million). Use a smaller size or print size.");
        }

        // the SVG in colour; with a print size every module keeps its printed width, written in the unit ("40.5mm")
        public static string Svg(BarcodeSvg barcode, BarcodeLook look, PrintSize print)
        {
            string svg = barcode.Styled(look.Color, look.Paper, ModulePx(barcode, look, print));
            if (!print.Applies) return svg;
            double widthMm = Guidance(print, barcode).ModuleMm * barcode.Width;
            return PrintSized(svg, print.Unit == "in" ? widthMm / 25.4 : widthMm, print.Unit);
        }

        // the website's setSvgPrintSize: the width and height in the unit, the viewBox kept
        static string PrintSized(string svg, double width, string unit)
        {
            Match open = Regex.Match(svg, "<svg\\b[^>]*>");
            if (!open.Success) return svg;
            Match viewBox = Regex.Match(open.Value, "viewBox=\"\\s*[-0-9.]+[\\s,]+[-0-9.]+[\\s,]+([0-9.]+)[\\s,]+([0-9.]+)\\s*\"");
            if (!viewBox.Success) return svg;
            double height = width * double.Parse(viewBox.Groups[2].Value, CultureInfo.InvariantCulture) / double.Parse(viewBox.Groups[1].Value, CultureInfo.InvariantCulture);
            string Length(double v) => Js.NumberToString(double.Parse(Js.ToFixed(v, 3), CultureInfo.InvariantCulture)) + unit;
            string tag = new Regex("\\swidth=\"[^\"]*\"").Replace(open.Value, "", 1);
            tag = new Regex("\\sheight=\"[^\"]*\"").Replace(tag, "", 1);
            tag = "<svg width=\"" + Length(width) + "\" height=\"" + Length(height) + "\"" + tag.Substring("<svg".Length);
            return svg.Substring(0, open.Index) + tag + svg.Substring(open.Index + open.Length);
        }

        // The PDF: the PNG without loss at its size on paper. With a print size every module keeps its
        // printed width; without, the page is the PNG's size at 96 px to the inch, drawn at whole pixels per
        // module near 300 DPI (but no more than 4096 px wide) so the bars stay sharp.
        public static byte[] Pdf(BarcodeArt art, BarcodeLook look, PrintSize print, string title)
        {
            int modulePx = ModulePx(art.Barcode, look, print);
            double dpi;
            if (print.Applies) dpi = print.Dpi;
            else
            {
                int sharper = (int)Math.Ceiling(modulePx * 300 / 96.0);
                int widest = (int)Math.Floor(4096.0 / art.W);
                double pageInches = art.W * modulePx / 96.0;
                modulePx = Math.Max(modulePx, Math.Min(sharper, widest));
                dpi = art.W * modulePx / pageInches;
            }
            Allowed(art.W * modulePx, art.H * modulePx);
            BitmapSource picture = art.Render(modulePx, Paint(look.Color), look.Transparent ? (Color?)null : Paint(look.Background), null, dpi);
            double pageWidth = Math.Round(picture.PixelWidth / dpi * PdfFile.PointsPerInch, 2);
            double pageHeight = Math.Round(picture.PixelHeight / dpi * PdfFile.PointsPerInch, 2);
            return PdfFile.Make(picture, pageWidth, pageHeight, new Rect(0, 0, pageWidth, pageHeight), string.IsNullOrEmpty(title) ? "Barcode" : title);
        }

        // "0.48 mm" or "0.019 in" (the website's formatSmallLength)
        public static string SmallLength(double mm, string unit) => unit == "in" ? Js.ToFixed(mm / 25.4, 3) + " in" : Js.ToFixed(mm, 2) + " mm";

        // the print size's line, and the warning for bars too narrow (null when they aren't)
        public static string PrintLine(BarcodeSvg barcode, PrintSize print, out string warning)
        {
            BarcodeGuidance g = Guidance(print, barcode);
            warning = g.TooSmall ? "Bars under about " + SmallLength(MinBarMm, print.Unit) + " are hard to print and scan. Make it wider." : null;
            return g.WidthPx.ToString(CultureInfo.InvariantCulture) + " × " + g.HeightPx.ToString(CultureInfo.InvariantCulture) + " px, " + SmallLength(g.ActualWidthMm, print.Unit) +
                   " wide so every bar is whole pixels. The narrowest bar is " + SmallLength(g.ModuleMm, print.Unit) + ".";
        }

        // the website's warnings for a barcode: zint always gives it its quiet zones, so none about that
        public static List<string> Warnings(BarcodeLook look) => Checks.Warnings(new[] { look.Color }, look.Paper, null, 4);

        // modules of blank room round the code for the scan check's second try
        public const int CheckRoom = 10;

        // The scan check's picture: drawn as a PNG is, at 4 pixels per module, on white; with `room` modules of its
        // background round it for the second try (without quiet zones ZXing.Net's EAN/UPC and Data Matrix readers
        // need some, where the website's reader doesn't)
        public static BitmapSource CheckPicture(BarcodeArt art, BarcodeLook look, int room = 0)
        {
            Color? paper = look.Transparent ? (Color?)null : Paint(look.Background);
            BitmapSource picture = art.Render(CheckScale, Paint(look.Color), paper, Colors.White);
            if (room == 0) return picture;
            int pad = room * CheckScale, width = picture.PixelWidth + 2 * pad, height = picture.PixelHeight + 2 * pad;
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                if (paper != null) dc.DrawRectangle(new SolidColorBrush(paper.Value), null, new Rect(0, 0, width, height));
                dc.DrawImage(picture, new Rect(pad, pad, picture.PixelWidth, picture.PixelHeight));
            }
            var roomy = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            roomy.Render(visual);
            roomy.Freeze();
            return roomy;
        }

        // What a scanner reads from the picture's grey levels, in the form the type's expected text takes
        // (GS1 data in round brackets), or null. Safe on any thread.
        public static string Read(byte[] grays, int width, int height, BarcodeFormat format)
        {
            var reader = new BarcodeReaderGeneric { AutoRotate = false };
            reader.Options.PossibleFormats = new List<ZXing.BarcodeFormat> { format.ReadFormat };
            reader.Options.TryHarder = true;
            reader.Options.AssumeGS1 = format.Gs1;
            // light codes on dark too, as the website's reader tries them; only for the 2D codes it reads that way
            // (ZXing.Net would also read light barcodes the website's reader doesn't)
            reader.Options.TryInverted = format.ReadFormat == ZXing.BarcodeFormat.DATA_MATRIX || format.ReadFormat == ZXing.BarcodeFormat.AZTEC;
            Result result = reader.Decode(new RGBLuminanceSource(grays, width, height, RGBLuminanceSource.BitmapFormat.Gray8));
            if (result == null) return null;
            string text = result.Text;
            if (format.Gs1) text = Gs1.FromScanned(text) ?? Gs1.Unprefixed(text);
            return format.NormalizeRead(text);
        }
    }
}
