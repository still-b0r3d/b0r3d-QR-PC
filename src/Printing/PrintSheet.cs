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

    // an Avery label sheet: its paper, grid and exact measurements in WPF's units (96 to the inch)
    sealed class LabelSheetPreset
    {
        public readonly string Code;
        public readonly string Name;
        public readonly Size Page;
        public readonly int Columns, Rows;
        public readonly double LabelWidth, LabelHeight;
        public readonly double Left, Top, PitchX, PitchY;

        public LabelSheetPreset(string code, string name, Size page, int columns, int rows, double labelWidth, double labelHeight, double left, double top, double pitchX, double pitchY)
        {
            Code = code;
            Name = name;
            Page = page;
            Columns = columns;
            Rows = rows;
            LabelWidth = labelWidth;
            LabelHeight = labelHeight;
            Left = left;
            Top = top;
            PitchX = pitchX;
            PitchY = pitchY;
        }

        public int PerSheet => Columns * Rows;
        public override string ToString() => Name;
    }

    // how the codes go on the paper
    sealed class PrintOptions
    {
        public Size Page = PrintSheet.A4;      // the paper, as it's turned
        public bool Sheet;                     // a grid of codes; else each code alone, in the middle of its page
        public LabelSheetPreset Labels;        // an Avery label sheet: one code on every label (null: plain paper)
        public double NudgeX, NudgeY;          // how far to shift every code on a label sheet (WPF units: positive is right and down)
        public bool Captions = true;           // sheets and labels: a caption under each code
        public bool CutMarks = true;           // sheets: marks at each code's corners, to cut along
    }

    // The pages to print: each code at its exact size (as its PDF is), alone, on sheets in a grid, or on Avery label sheets.
    static class PrintSheet
    {
        public static double Mm(double mm) => mm / 25.4 * 96;
        public static double In(double inches) => inches * 96;

        public static readonly Size A4 = new Size(Mm(210), Mm(297));
        public static readonly Size Letter = new Size(In(8.5), In(11));

        // standard Avery label sheets (US Letter in inches, A4 in millimetres)
        public static readonly LabelSheetPreset[] LabelSheets =
        {
            new LabelSheetPreset("5160", "Avery 5160 · 30 a sheet · 2⅝ × 1 in · US Letter", Letter, 3, 10, In(2.625), In(1), In(0.1875), In(0.5), In(2.75), In(1)),
            new LabelSheetPreset("5167", "Avery 5167 · 80 a sheet · 1¾ × ½ in · US Letter", Letter, 4, 20, In(1.75), In(0.5), In(0.3), In(0.5), In(2.05), In(0.5)),
            new LabelSheetPreset("5163", "Avery 5163 · 10 a sheet · 4 × 2 in · US Letter", Letter, 2, 5, In(4), In(2), In(0.15625), In(0.5), In(4.1875), In(2)),
            new LabelSheetPreset("5164", "Avery 5164 · 6 a sheet · 4 × 3⅓ in · US Letter", Letter, 2, 3, In(4), In(10.0 / 3.0), In(0.15625), In(0.5), In(4.1875), In(10.0 / 3.0)),
            new LabelSheetPreset("22806", "Avery 22806 · 12 a sheet · 2 × 2 in · US Letter", Letter, 3, 4, In(2), In(2), In(0.625), In(0.625), In(2.625), In(2 + 7.0 / 12.0)),
            new LabelSheetPreset("L7160", "Avery L7160 · 21 a sheet · 63.5 × 38.1 mm · A4", A4, 3, 7, Mm(63.5), Mm(38.1), Mm(7.2), Mm(15.15), Mm(66), Mm(38.1)),
            new LabelSheetPreset("L7163", "Avery L7163 · 14 a sheet · 99.1 × 38.1 mm · A4", A4, 2, 7, Mm(99.1), Mm(38.1), Mm(4.65), Mm(15.15), Mm(101.6), Mm(38.1)),
            new LabelSheetPreset("L7165", "Avery L7165 · 8 a sheet · 99.1 × 67.7 mm · A4", A4, 2, 4, Mm(99.1), Mm(67.7), Mm(4.65), Mm(13.1), Mm(101.6), Mm(67.7)),
        };

        public static bool IsLabelSheet(string code) =>
            !string.IsNullOrEmpty(code) && Array.Exists(LabelSheets, s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));

        public static LabelSheetPreset FindLabelSheet(string code) =>
            Array.Find(LabelSheets, s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase)) ?? LabelSheets[0];

        public static readonly double Margin = Mm(10);       // left clear on every side: printers can't print to the edge
        public static readonly double LabelInset = Mm(1.5);  // left clear inside each die-cut label
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

        // the codes laid out on pages; `shrunk` when a code was bigger than the paper (or label) and was made smaller to fit
        public static FixedDocument Pages(IList<PrintCode> codes, PrintOptions options, out bool shrunk)
        {
            shrunk = false;
            var document = new FixedDocument();
            if (options.Labels != null)
            {
                LabelSheetPreset labels = options.Labels;
                Size paper = labels.Page;
                document.DocumentPaginator.PageSize = paper;
                if (codes.Count == 0) return document;
                double labelWidth = Math.Max(1, labels.LabelWidth - 2 * LabelInset), labelHeight = Math.Max(1, labels.LabelHeight - 2 * LabelInset);
                double labelCaptionHeight = CaptionHeight(options.Captions && codes.Any(c => !string.IsNullOrEmpty(c.Caption)), labelHeight);
                bool labelCaptions = labelCaptionHeight > 0;
                for (int first = 0; first < codes.Count; first += labels.PerSheet)
                {
                    FixedPage page = NewPage(paper);
                    for (int i = first; i < Math.Min(codes.Count, first + labels.PerSheet); i++)
                    {
                        int column = (i - first) % labels.Columns, row = (i - first) / labels.Columns;
                        double labelLeft = labels.Left + options.NudgeX + column * labels.PitchX;
                        double labelTop = labels.Top + options.NudgeY + row * labels.PitchY;
                        PrintCode code = codes[i];
                        bool hasCaption = labelCaptions && !string.IsNullOrEmpty(code.Caption);
                        double roomHeight = labelHeight - (hasCaption ? labelCaptionHeight : 0);
                        double k = Math.Min(1, Math.Min(labelWidth / code.Size.Width, roomHeight / code.Size.Height));
                        shrunk |= k < 1;
                        var size = new Size(code.Size.Width * k, code.Size.Height * k);
                        double totalHeight = size.Height + (hasCaption ? labelCaptionHeight : 0);
                        double x = labelLeft + (labels.LabelWidth - size.Width) / 2;
                        double y = labelTop + (labels.LabelHeight - totalHeight) / 2;
                        Place(page, code, new Rect(x, y, size.Width, size.Height));
                        if (hasCaption)
                        {
                            var caption = new TextBlock
                            {
                                Text = LongText.Cap(code.Caption), FontFamily = new FontFamily("Segoe UI"), FontSize = CaptionSize, Foreground = Brushes.Black,
                                Width = labelWidth, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                            };
                            FixedPage.SetLeft(caption, labelLeft + LabelInset);
                            FixedPage.SetTop(caption, y + size.Height + CaptionGap);
                            page.Children.Add(caption);
                        }
                    }
                    Add(document, page);
                }
                return document;
            }

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
            if (options.Labels != null) return options.Labels.PerSheet;
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
