using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR.Qr
{
    // Drawing the shapes: on screen, into PNG files, and as SVG.
    static class QrPicture
    {
        // the largest picture that can be asked for, in pixels across
        public const int MaxPixels = 4000;

        // Whole pixels per module for a picture about `wanted` pixels across (at least 1), so every
        // module is a sharp square of the same size; the picture comes out within half a module.
        public static int PixelsPerModule(int wanted, int modules)
        {
            return Math.Max(1, (int)Math.Round((double)wanted / modules, MidpointRounding.AwayFromZero));
        }

        // black modules on white, `pixelsPerModule` pixels each
        public static BitmapSource Render(QrShapes shapes, int pixelsPerModule)
        {
            int pixels = shapes.Width * pixelsPerModule;
            var visual = new DrawingVisual();
            RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);  // whole pixels: no grey edges
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pixels, pixels));
                dc.DrawGeometry(Brushes.Black, null, shapes.ToGeometry(pixelsPerModule));
            }
            var picture = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
            picture.Render(visual);
            picture.Freeze();
            return picture;
        }

        public static byte[] Png(BitmapSource picture)
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(picture));
            using (var bytes = new MemoryStream())
            {
                png.Save(bytes);
                return bytes.ToArray();
            }
        }

        // An SVG file: the squares in modules (the viewBox), shown `pixels` across.
        public static string Svg(QrShapes shapes, int pixels)
        {
            string w = shapes.Width.ToString(CultureInfo.InvariantCulture);
            string p = pixels.ToString(CultureInfo.InvariantCulture);
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"" + p + "\" height=\"" + p + "\" viewBox=\"0 0 " + w + " " + w + "\" shape-rendering=\"crispEdges\">\n" +
                "<rect width=\"" + w + "\" height=\"" + w + "\" fill=\"#ffffff\"/>\n" +
                "<path fill=\"#000000\" d=\"" + shapes.ToSvgPath() + "\"/>\n" +
                "</svg>\n";
        }
    }
}
