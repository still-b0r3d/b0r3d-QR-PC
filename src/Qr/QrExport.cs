using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Design;

namespace B0r3dQR.Qr
{
    // Print size: the whole exported picture's width on paper, at a resolution (the website's
    // "Size for print", which it keeps apart from designs, as b0r3d QR keeps it in its settings).
    sealed class PrintSize
    {
        public bool On;
        public double Width = 40;            // in Unit
        public string Unit = "mm";           // mm or in
        public int Dpi = 300;                // 150, 300 or 600 offered

        public static readonly int[] Resolutions = { 150, 300, 600 };

        public double Millimetres => Unit == "in" ? Width * 25.4 : Width;
        public double Inches => Millimetres / 25.4;

        public int PixelWidth => (int)Math.Round(Millimetres / 25.4 * Dpi, MidpointRounding.AwayFromZero);

        // why the website wouldn't use it (it then uses the screen size), or null
        public string Problem
        {
            get
            {
                if (!(Millimetres > 0) || Millimetres > 1000) return "Enter a width above 0 and up to 1000 mm (39 in).";
                if (Dpi < 72 || Dpi > 2400) return "Enter a resolution between 72 and 2400 DPI.";
                if (PixelWidth > 10000) return "That would be over 10,000 pixels wide. Use a smaller width or resolution.";
                return null;
            }
        }

        public bool Applies => On && Problem == null;

        public PrintSize Clone() => (PrintSize)MemberwiseClone();
    }

    // A design's files, sized the way the website sizes them.
    static class QrExport
    {
        public sealed class Size
        {
            public double Scale;      // pixels per px of the design
            public int Width, Height; // pixels
            public double Dpi = 96;
        }

        // the PNG, JPG and clipboard size: the print size when it applies, else the website's screen size
        public static Size PictureSize(QrArt art, PrintSize print, bool wholePixels)
        {
            if (print != null && print.Applies)
            {
                int w = print.PixelWidth;
                return new Size { Scale = w / art.W, Width = w, Height = (int)Math.Round(w * art.H / art.W, MidpointRounding.AwayFromZero), Dpi = print.Dpi };
            }
            QrDesign d = art.Design;
            if (d.Frame == null)
            {
                double side = Math.Max(d.Width, d.Height);
                if (wholePixels)
                {
                    double modules = art.Code.Size + 2 * Math.Max(0, d.Margin);
                    side = Math.Max(1, Math.Round(side / modules, MidpointRounding.AwayFromZero)) * modules;
                }
                double scale = side / art.S;
                return new Size { Scale = scale, Width = (int)Math.Floor(art.W * scale + 1e-9), Height = (int)Math.Floor(art.H * scale + 1e-9) };
            }
            // with a frame the website sizes the file from its 200 px preview of it, grown to the width
            QrFrame f = d.Frame;
            double p = Css.Pixels(f.Padding, 0), b = Css.Pixels(f.BorderWidth, 0);
            double column = f.Beside ? f.CaptionWidth : 200;
            int lines = QrArt.PreviewLines(string.IsNullOrEmpty(f.Text) ? QrFrame.DefaultText : f.Text, f.FontFamily, column);
            double pw, ph;
            if (f.Beside)
            {
                pw = 200 + 16 + column + 2 * p + 2 * b;
                ph = Math.Max(200, 28 * lines) + 2 * p + 2 * b;
            }
            else
            {
                pw = 200 + 2 * p + 2 * b;
                ph = 200 + 16 + 28 * lines + 2 * p + 2 * b;
            }
            double grow = d.Width / 200;
            double k = Math.Max(pw * grow / art.W, ph * grow / art.H);
            return new Size { Scale = k, Width = (int)Math.Floor(art.W * k + 1e-9), Height = (int)Math.Floor(art.H * k + 1e-9) };
        }

        // the most pixels b0r3d QR draws in one picture (a 10,000 px square print size), so a huge one
        // is refused with a reason instead of running out of memory
        public const long MostPixels = 100000000;

        static void Allowed(int width, int height)
        {
            if ((long)width * height > MostPixels)
                throw new QrException("That picture would be " + width.ToString("N0", CultureInfo.InvariantCulture) + " × " + height.ToString("N0", CultureInfo.InvariantCulture) +
                    " pixels, more than b0r3d QR draws at once (100 million). Use a smaller picture size, print size or caption.");
        }

        public static BitmapSource Picture(QrArt art, PrintSize print, bool wholePixels, bool forJpg = false)
        {
            Size size = PictureSize(art, print, wholePixels);
            Allowed(size.Width, size.Height);
            // A JPG has no see-through: as the website's, white paper then the background go under the whole
            // picture, and the drawing paints the background in the code's square again, so a partly see-through
            // one is twice as strong there as round a rounded corner (#00000080: 63 and 127). The website's
            // barcode JPG paints it once; the two stay as they are on both sides until both change.
            Color? under = null;
            if (forJpg) under = art.Design.Transparent ? Colors.White : Css.Paint(art.Design.Background);
            return art.Render(size.Scale, size.Width, size.Height, under, size.Dpi);
        }

        public static byte[] Png(BitmapSource picture)
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(picture));
            return Encode(png);
        }

        public static byte[] Jpg(BitmapSource picture, int quality = 92)   // 92: what browsers use when the website asks for none
        {
            var jpg = new JpegBitmapEncoder { QualityLevel = quality };
            jpg.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(picture, PixelFormats.Bgr24, null, 0)));
            return Encode(jpg);
        }

        static byte[] Encode(BitmapEncoder encoder)
        {
            using (var bytes = new MemoryStream())
            {
                encoder.Save(bytes);
                return bytes.ToArray();
            }
        }

        // the SVG, with a print size as physical lengths ("40mm"), the drawing's own size kept in its viewBox
        public static string Svg(QrArt art, PrintSize print)
        {
            if (print == null || !print.Applies) return art.ToSvg();
            string Length(double v) => v.ToString("0.###", CultureInfo.InvariantCulture) + print.Unit;
            return art.ToSvg(Length(print.Width), Length(print.Width * art.H / art.W));
        }

        // The PDF: the drawing's shapes (its caption as the letters' outlines, a raster picture kept
        // without loss), its page exactly the picture's size on paper (the print size, or the screen
        // size at 96 px to the inch).
        public static byte[] Pdf(QrArt art, PrintSize print, bool wholePixels, string title)
        {
            Size size = PictureSize(art, print, wholePixels);
            Allowed(size.Width, size.Height);
            double inches = print != null && print.Applies ? print.Inches : size.Width / 96.0;
            double pageWidth = Math.Round(inches * PdfFile.PointsPerInch, 2);
            double pageHeight = Math.Round(pageWidth * art.H / art.W, 2);
            return PdfFile.Make(art.ToDrawing(), new Rect(0, 0, art.W, art.H), pageWidth, pageHeight, new Rect(0, 0, pageWidth, pageHeight), string.IsNullOrEmpty(title) ? "QR code" : title);
        }

        // ---------- text ----------

        public enum TextStyle { HalfBlocks, FullBlocks, Ascii }

        // The code as text, the website's three ways, always with 4 modules of quiet zone, at the
        // size chosen and the level chosen (a logo can't be in text, so it isn't raised), except with
        // the UTF-8 marker, where the website uses the picture's own code, raised level and all.
        public static string Text(QrDesign d, TextStyle style)
        {
            QrCode code = QrCode.Make(d.Data, d.Utf8Marker ? d.LevelUsed : d.Level, d.Version, d.Utf8Marker);
            const int quiet = 4;
            int n = code.Size + 2 * quiet;
            bool Dark(int r, int c) => r >= quiet && c >= quiet && r < n - quiet && c < n - quiet && code[r - quiet, c - quiet];
            var s = new StringBuilder();
            if (style == TextStyle.HalfBlocks)
            {
                for (int r = 0; r < n; r += 2)
                {
                    if (r > 0) s.Append('\n');
                    for (int c = 0; c < n; c++)
                    {
                        bool top = Dark(r, c), bottom = r + 1 < n && Dark(r + 1, c);
                        s.Append(top && bottom ? '█' : top ? '▀' : bottom ? '▄' : ' ');
                    }
                }
            }
            else
            {
                for (int r = 0; r < n; r++)
                {
                    if (r > 0) s.Append('\n');
                    for (int c = 0; c < n; c++)
                        s.Append(style == TextStyle.FullBlocks ? (Dark(r, c) ? "██" : "  ") : (Dark(r, c) ? "  " : "##"));
                }
            }
            return s.ToString();
        }

        public static string Markdown(string text) => "```\n" + text + "\n```\n";

        // ---------- print size wording ----------

        // "472 × 472 px. Each module is 1.00 mm; scans from up to about 29 cm." and, for modules
        // under 0.4 mm, the website's warning (null when they're big enough)
        public static string PrintLine(QrArt art, PrintSize print, out string warning)
        {
            warning = null;
            if (print == null || !print.Applies) return null;
            int w = print.PixelWidth, h = (int)Math.Round(w * art.H / art.W, MidpointRounding.AwayFromZero);
            double moduleMm = print.Millimetres * art.S / art.W / (art.Code.Size + 2 * Math.Max(0, art.Design.Margin));
            double symbolMm = moduleMm * art.Code.Size, distanceMm = symbolMm * 10;
            bool inches = print.Unit == "in";
            string module = inches ? (moduleMm / 25.4).ToString("0.000", CultureInfo.InvariantCulture) + " in" : moduleMm.ToString("0.00", CultureInfo.InvariantCulture) + " mm";
            // rounded as the website rounds, halves up
            string distance = inches ? Math.Round(distanceMm / 25.4, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + " in"
                                     : Math.Round(distanceMm / 10, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + " cm";
            if (moduleMm < 0.4)
                warning = "Modules under about " + (inches ? "0.016 in" : "0.40 mm") + " are hard to print and scan. Make it bigger, or store less data or use lower error correction.";
            return w.ToString("N0", CultureInfo.InvariantCulture) + " × " + h.ToString("N0", CultureInfo.InvariantCulture) + " px. Each module is " + module + "; scans from up to about " + distance + ".";
        }
    }
}
