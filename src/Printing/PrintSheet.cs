using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace B0r3dQR.Printing
{
    // a code to print: its shapes over their own area (the whole picture, blank margin and all: shapes alone would
    // be stretched over it), its size on paper (in WPF's units, 96 to the inch) and the caption under it
    sealed class PrintCode
    {
        public Drawing Drawing;
        public Rect Canvas;
        public Size Size;
        public string Caption = "";
    }

    // how the codes go on the paper
    sealed class PrintOptions
    {
        public Size Page = PrintSheet.A4;      // the paper, as it's turned
        public bool Sheet;                     // a grid of codes; else each code alone, in the middle of its page
        public bool Captions = true;           // sheets: a caption under each code
        public bool CutMarks = true;           // sheets: marks at each code's corners, to cut along
    }

    // The pages to print: each code at its exact size (as its PDF is), alone or on sheets in a grid.
    static class PrintSheet
    {
        public static double Mm(double mm) => mm / 25.4 * 96;

        public static readonly Size A4 = new Size(Mm(210), Mm(297));
        public static readonly Size Letter = new Size(8.5 * 96, 11 * 96);

        public static readonly double Margin = Mm(10);       // left clear on every side: printers can't print to the edge
        static readonly double Gap = Mm(8);                  // between codes on a sheet, room for the cut marks
        static readonly double CaptionGap = Mm(1.5), CaptionSize = 11;
        static readonly double MarkFrom = Mm(1), MarkLength = Mm(2.5);

        // 10 mm, or less on small paper (labels)
        static double MarginFor(Size page) => Math.Min(Margin, Math.Min(page.Width, page.Height) * 0.08);

        // the room a caption takes under a code, or 0 when the paper has no room for one
        static double CaptionHeight(bool captions, double usableHeight)
        {
            double height = CaptionGap + Math.Ceiling(CaptionSize * 1.4);
            return captions && height < usableHeight / 2 ? height : 0;
        }

        // the codes laid out on pages; `shrunk` when a code was bigger than the paper and was made smaller to fit
        public static FixedDocument Pages(IList<PrintCode> codes, PrintOptions options, out bool shrunk)
        {
            shrunk = false;
            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = options.Page;
            if (codes.Count == 0) return document;
            double margin = MarginFor(options.Page);
            double usableWidth = Math.Max(1, options.Page.Width - 2 * margin), usableHeight = Math.Max(1, options.Page.Height - 2 * margin);
            double captionHeight = CaptionHeight(options.Sheet && options.Captions && codes.Any(c => !string.IsNullOrEmpty(c.Caption)), usableHeight);
            bool captions = captionHeight > 0;

            if (!options.Sheet)
            {
                foreach (PrintCode code in codes)
                {
                    double k = Math.Min(1, Math.Min(usableWidth / code.Size.Width, usableHeight / code.Size.Height));
                    shrunk |= k < 1;
                    var size = new Size(code.Size.Width * k, code.Size.Height * k);
                    FixedPage page = NewPage(options.Page);
                    Place(page, code, new Rect((options.Page.Width - size.Width) / 2, (options.Page.Height - size.Height) / 2, size.Width, size.Height));
                    Add(document, page);
                }
                return document;
            }

            // a sheet: every code in a cell as big as the biggest, so the grid lines up
            double cellWidth = codes.Max(c => c.Size.Width), cellHeight = codes.Max(c => c.Size.Height);
            double scale = Math.Min(1, Math.Min(usableWidth / cellWidth, (usableHeight - captionHeight) / cellHeight));
            if (scale < 1)
            {
                shrunk = true;
                cellWidth *= scale;
                cellHeight *= scale;
            }
            double boxHeight = cellHeight + captionHeight;
            int columns = Math.Max(1, (int)Math.Floor((usableWidth + Gap) / (cellWidth + Gap)));
            int rows = Math.Max(1, (int)Math.Floor((usableHeight + Gap) / (boxHeight + Gap)));
            double gridWidth = columns * cellWidth + (columns - 1) * Gap, gridHeight = rows * boxHeight + (rows - 1) * Gap;
            double left = (options.Page.Width - gridWidth) / 2, top = margin + (usableHeight - gridHeight) / 2;
            for (int first = 0; first < codes.Count; first += columns * rows)
            {
                FixedPage page = NewPage(options.Page);
                var marks = new GeometryGroup();
                for (int i = first; i < Math.Min(codes.Count, first + columns * rows); i++)
                {
                    int column = (i - first) % columns, row = (i - first) / columns;
                    var cell = new Rect(left + column * (cellWidth + Gap), top + row * (boxHeight + Gap), cellWidth, boxHeight);
                    PrintCode code = codes[i];
                    var size = new Size(code.Size.Width * scale, code.Size.Height * scale);
                    Place(page, code, new Rect(cell.X + (cellWidth - size.Width) / 2, cell.Y + (cellHeight - size.Height) / 2, size.Width, size.Height));
                    if (captions && !string.IsNullOrEmpty(code.Caption))
                    {
                        // (a batch row's file name or a code's text: capped, as all outside text shown is, LongText)
                        var caption = new TextBlock
                        {
                            Text = LongText.Cap(code.Caption), FontFamily = new FontFamily("Segoe UI"), FontSize = CaptionSize, Foreground = Brushes.Black,
                            Width = cell.Width, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                        };
                        FixedPage.SetLeft(caption, cell.X);
                        FixedPage.SetTop(caption, cell.Y + cellHeight + CaptionGap);
                        page.Children.Add(caption);
                    }
                    if (options.CutMarks) Marks(marks, cell);
                }
                if (options.CutMarks)
                    page.Children.Add(new Path { Data = marks, Stroke = Brushes.Black, StrokeThickness = 0.5, SnapsToDevicePixels = true });
                Add(document, page);
            }
            return document;
        }

        static FixedPage NewPage(Size paper) => new FixedPage { Width = paper.Width, Height = paper.Height, Background = Brushes.White };

        // the code's whole area (a see-through rectangle over it, and nothing outside it) fitted to `where`
        static void Place(FixedPage page, PrintCode code, Rect where)
        {
            var framed = new DrawingGroup { ClipGeometry = new RectangleGeometry(code.Canvas) };
            framed.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(code.Canvas)));
            framed.Children.Add(code.Drawing);
            framed.Freeze();
            var image = new Image { Source = new DrawingImage(framed), Width = where.Width, Height = where.Height, Stretch = Stretch.Fill };
            FixedPage.SetLeft(image, where.X);
            FixedPage.SetTop(image, where.Y);
            page.Children.Add(image);
        }

        static void Add(FixedDocument document, FixedPage page)
        {
            var content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(page);
            document.Pages.Add(content);
        }

        // short lines pointing out from each corner of the box, a little way off it
        static void Marks(GeometryGroup marks, Rect box)
        {
            foreach (Point corner in new[] { box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight })
            {
                double sx = corner.X == box.Left ? -1 : 1, sy = corner.Y == box.Top ? -1 : 1;
                marks.Children.Add(new LineGeometry(new Point(corner.X + sx * MarkFrom, corner.Y), new Point(corner.X + sx * (MarkFrom + MarkLength), corner.Y)));
                marks.Children.Add(new LineGeometry(new Point(corner.X, corner.Y + sy * MarkFrom), new Point(corner.X, corner.Y + sy * (MarkFrom + MarkLength))));
            }
        }

        // how many codes of this size go on a sheet (the same sums as Pages)
        public static int PerPage(Size code, PrintOptions options)
        {
            double margin = MarginFor(options.Page);
            double usableWidth = Math.Max(1, options.Page.Width - 2 * margin), usableHeight = Math.Max(1, options.Page.Height - 2 * margin);
            double captionHeight = CaptionHeight(options.Captions, usableHeight);
            double scale = Math.Min(1, Math.Min(usableWidth / code.Width, (usableHeight - captionHeight) / code.Height));
            int columns = Math.Max(1, (int)Math.Floor((usableWidth + Gap) / (code.Width * scale + Gap)));
            int rows = Math.Max(1, (int)Math.Floor((usableHeight + Gap) / (code.Height * scale + captionHeight + Gap)));
            return columns * rows;
        }

        // a QR code's size on paper: its picture's, at the print size's resolution (96 to the inch without one)
        public static Size QrSize(Qr.QrArt art, Qr.PrintSize print, bool wholePixels)
        {
            Qr.QrExport.Size picture = Qr.QrExport.PictureSize(art, print, wholePixels);
            return new Size(picture.Width * 96.0 / picture.Dpi, picture.Height * 96.0 / picture.Dpi);
        }

        // a barcode's: whole pixels a module at the print size's resolution, as its files are
        public static Size BarcodeSize(Barcodes.BarcodeArt art, Barcodes.BarcodeLook look, Qr.PrintSize print)
        {
            int modulePx = Barcodes.BarcodeExport.ModulePx(art.Barcode, look, print);
            double dpi = print.Applies ? print.Dpi : 96;
            return new Size(art.W * modulePx * 96.0 / dpi, art.H * modulePx * 96.0 / dpi);
        }

        // what decides the codes' size, in words
        public static string SizeNote(Size size, Qr.PrintSize print, bool several) => print.Applies
            ? (several ? "Each code prints at its own size, " : "It prints at ") + Describe(size, print.Unit) + (several ? " for the first" : "") + ": the print size set on the Make tab."
            : (several ? "Each code prints at its picture's size, " : "It prints at its picture's size, ") + Describe(size, "mm") + (several ? " for the first" : "") +
              " (96 pixels to the inch). For an exact size, set a print size on the Make tab.";

        // "40 × 40 mm" (or inches where the print size is in inches)
        public static string Describe(Size size, string unit) => unit == "in"
            ? (size.Width / 96).ToString("0.##", CultureInfo.CurrentCulture) + " × " + (size.Height / 96).ToString("0.##", CultureInfo.CurrentCulture) + " in"
            : (size.Width / 96 * 25.4).ToString("0.#", CultureInfo.CurrentCulture) + " × " + (size.Height / 96 * 25.4).ToString("0.#", CultureInfo.CurrentCulture) + " mm";
    }
}
