using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR.Qr
{
    // A PDF file of one page with one picture on it, placed at an exact size in points (72 to an
    // inch). The picture keeps its pixels, compressed, with a soft mask for see-through parts, so
    // the code prints exactly as it was drawn. Written by hand: it's a few objects.
    static class PdfFile
    {
        public const double PointsPerInch = 72;

        // `place` is where the picture goes on the page, in points from the page's top-left corner
        public static byte[] Make(BitmapSource picture, double pageWidth, double pageHeight, Rect place, string title)
        {
            var bgra = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var pixels = new byte[(long)w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);
            var rgb = new byte[(long)w * h * 3];
            var alpha = new byte[(long)w * h];
            bool seeThrough = false;
            for (long i = 0, j = 0; i < (long)w * h; i++, j += 4)
            {
                byte a = pixels[j + 3];
                alpha[i] = a;
                if (a != 255) seeThrough = true;
                // Bgra32 isn't premultiplied: the colours are as they are
                rgb[i * 3] = pixels[j + 2];
                rgb[i * 3 + 1] = pixels[j + 1];
                rgb[i * 3 + 2] = pixels[j];
            }

            var pdf = new MemoryStream();
            var offsets = new List<long>();
            void Text(string s) { byte[] b = Encoding.ASCII.GetBytes(s); pdf.Write(b, 0, b.Length); }
            void Start(int number) { offsets.Add(pdf.Position); Text(number + " 0 obj\n"); }

            Text("%PDF-1.4\n%");
            pdf.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3 }, 0, 4);   // bytes over 127 mark the file as binary for programs that look
            Text("\n");
            Start(1);
            Text("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            Start(2);
            Text("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
            Start(3);
            Text("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(pageWidth) + " " + N(pageHeight) + "] /Resources << /XObject << /Code 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

            byte[] colours = Zlib(rgb);
            Start(4);
            Text("<< /Type /XObject /Subtype /Image /Width " + w + " /Height " + h + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode" +
                (seeThrough ? " /SMask 7 0 R" : "") + " /Length " + colours.Length + " >>\nstream\n");
            pdf.Write(colours, 0, colours.Length);
            Text("\nendstream\nendobj\n");

            // PDF counts up from the bottom of the page
            double bottom = pageHeight - place.Bottom;
            byte[] content = Encoding.ASCII.GetBytes("q " + N(place.Width) + " 0 0 " + N(place.Height) + " " + N(place.Left) + " " + N(bottom) + " cm /Code Do Q\n");
            Start(5);
            Text("<< /Length " + content.Length + " >>\nstream\n");
            pdf.Write(content, 0, content.Length);
            Text("endstream\nendobj\n");

            Start(6);
            Text("<< /Title " + PdfString(title) + " /Producer " + PdfString(Program.Name + " " + Program.Version) + " >>\nendobj\n");

            if (seeThrough)
            {
                byte[] mask = Zlib(alpha);
                Start(7);
                Text("<< /Type /XObject /Subtype /Image /Width " + w + " /Height " + h + " /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length " + mask.Length + " >>\nstream\n");
                pdf.Write(mask, 0, mask.Length);
                Text("\nendstream\nendobj\n");
            }

            long xref = pdf.Position;
            Text("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
            foreach (long o in offsets) Text(o.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
            Text("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R /Info 6 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            return pdf.ToArray();
        }

        static string N(double d) => Math.Round(d, 3).ToString("0.###", CultureInfo.InvariantCulture);

        // a PDF text string: plain ASCII as is, anything else as UTF-16 with its byte-order mark
        static string PdfString(string text)
        {
            bool ascii = true;
            foreach (char c in text) if (c < ' ' || c > '~') { ascii = false; break; }
            if (ascii) return "(" + text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")";
            var hex = new StringBuilder("<FEFF");
            foreach (char c in text) hex.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            return hex.Append('>').ToString();
        }

        // Flate as PDF wants it: zlib's two-byte header, the deflated bytes, then the Adler-32 sum
        static byte[] Zlib(byte[] data)
        {
            var output = new MemoryStream();
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true)) deflate.Write(data, 0, data.Length);
            uint a = 1, b = 0;
            for (int i = 0; i < data.Length; )
            {
                int end = Math.Min(data.Length, i + 5552);  // the most bytes before the sums can overflow
                for (; i < end; i++)
                {
                    a += data[i];
                    b += a;
                }
                a %= 65521;
                b %= 65521;
            }
            uint sum = (b << 16) | a;
            output.WriteByte((byte)(sum >> 24));
            output.WriteByte((byte)(sum >> 16));
            output.WriteByte((byte)(sum >> 8));
            output.WriteByte((byte)sum);
            return output.ToArray();
        }
    }
}
