using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace B0r3dQR.Qr
{
    // Dark modules side by side in one row, counted in modules from the picture's top-left corner.
    readonly struct Run
    {
        public readonly int X, Y, Length;

        public Run(int x, int y, int length)
        {
            X = x;
            Y = y;
            Length = length;
        }
    }

    // The code as shapes: the one list the preview, the PNG and the SVG are all drawn from, so what
    // you see is what you save. Measured in modules, with the quiet zone (the blank modules around
    // the code) included. For now every dark module is a square, and squares side by side in a row
    // make one rectangle.
    sealed class QrShapes
    {
        public readonly QrCode Code;
        public readonly int QuietZone;
        public readonly int Width;   // modules across, quiet zone included; the picture is square
        public readonly IReadOnlyList<Run> Runs;

        QrShapes(QrCode code, int quietZone, List<Run> runs)
        {
            Code = code;
            QuietZone = quietZone;
            Width = code.Size + 2 * quietZone;
            Runs = runs;
        }

        public static QrShapes For(QrCode code, int quietZone)
        {
            var runs = new List<Run>();
            for (int row = 0; row < code.Size; row++)
            {
                int col = 0;
                while (col < code.Size)
                {
                    if (!code[row, col]) { col++; continue; }
                    int start = col;
                    while (col < code.Size && code[row, col]) col++;
                    runs.Add(new Run(start + quietZone, row + quietZone, col - start));
                }
            }
            return new QrShapes(code, quietZone, runs);
        }

        // the dark shapes, `moduleSize` units to a module
        public Geometry ToGeometry(double moduleSize)
        {
            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (StreamGeometryContext c = geometry.Open())
            {
                foreach (Run r in Runs)
                {
                    double x = r.X * moduleSize, y = r.Y * moduleSize, w = r.Length * moduleSize;
                    c.BeginFigure(new Point(x, y), true, true);
                    c.PolyLineTo(new[] { new Point(x + w, y), new Point(x + w, y + moduleSize), new Point(x, y + moduleSize) }, false, false);
                }
            }
            geometry.Freeze();
            return geometry;
        }

        // the same shapes as SVG path data, in modules: "M4 4h7v1h-7z" for each run
        public string ToSvgPath()
        {
            var d = new StringBuilder(Runs.Count * 16);
            foreach (Run r in Runs)
            {
                string length = Number(r.Length);
                d.Append('M').Append(Number(r.X)).Append(' ').Append(Number(r.Y))
                 .Append('h').Append(length).Append("v1h-").Append(length).Append('z');
            }
            return d.ToString();
        }

        static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
