using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Barcodes;
using B0r3dQR.Design;
using B0r3dQR.Qr;

namespace B0r3dQR.Batch
{
    enum BatchFormat { Png, Jpg, Svg, Pdf, Text }

    // What a batch makes: QR codes in the window's design, or barcodes of the window's type in its look; and the
    // kind of file.
    sealed class BatchKind
    {
        public BarcodeType? Barcode;
        public QrDesign Design;                  // QR codes: the design, its text and caption replaced by each row's
        public bool Frame;                       // QR codes: in a frame, each with its row's caption
        public PrintSize Print = new PrintSize();
        public bool WholePixels;
        public BarcodeLook Look;                 // barcodes
        public BatchFormat Format;
        public QrExport.TextStyle TextStyle;     // text files
        public bool Markdown;

        public string Extension => Format == BatchFormat.Png ? ".png" : Format == BatchFormat.Jpg ? ".jpg" : Format == BatchFormat.Svg ? ".svg" :
                                   Format == BatchFormat.Pdf ? ".pdf" : Markdown ? ".md" : ".txt";
    }

    // one file of a batch: its row, its name (without the extension), why it can't be made (null when it can), and
    // what reading it back must give
    sealed class BatchItem
    {
        public BatchRow Row;
        public string Name;
        public string Problem;
        public string Expected;
    }

    // Making a batch's files, each read back: pictures and PDFs from their bytes, SVG and text by their code.
    static class BatchExport
    {
        // The files a batch makes, named as the website names them, with the rows that can't be made and why (the
        // checks and encoders the window uses). Safe on any thread.
        public static List<BatchItem> Plan(IList<BatchRow> rows, BatchKind kind)
        {
            var items = new List<BatchItem>();
            for (int i = 0; i < rows.Count; i++)
            {
                var item = new BatchItem { Row = rows[i] };
                if (kind.Barcode != null)
                {
                    BarcodeFormat format = BarcodeFormats.Get(kind.Barcode.Value);
                    BarcodeSvg made = Barcode.Make(rows[i].Data, kind.Barcode.Value, kind.Look.Options, out string error);
                    if (made == null) item.Problem = error;
                    else item.Expected = format.ExpectedText(format.Prepare(rows[i].Data));
                }
                else
                {
                    try
                    {
                        QrLevel level = kind.Format == BatchFormat.Text ? kind.Design.Level : kind.Design.LevelUsed;
                        QrCode.Make(rows[i].Data, level, kind.Design.Version, kind.Design.Utf8Marker);
                        item.Expected = rows[i].Data;
                    }
                    catch (QrException x)
                    {
                        item.Problem = x.Message;
                    }
                }
                items.Add(item);
            }
            // The names. The QR page's pictures name every row (its batch never leaves one out); the barcode page and
            // the text export name only the rows they make, so a row left out doesn't take a name from the next.
            if (kind.Barcode == null && kind.Format != BatchFormat.Text)
            {
                List<string> names = BatchRows.QrNames(rows);
                for (int i = 0; i < items.Count; i++) items[i].Name = names[i];
            }
            else
            {
                var places = new List<int>();
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Problem == null) places.Add(i);
                List<BatchItem> made = places.Select(i => items[i]).ToList();
                List<string> names = kind.Barcode != null ? BatchRows.BarcodeNames(made.Select(i => i.Row).ToList())
                    : BatchRows.TextNames(made.Select(i => i.Row).ToList(), places);
                for (int i = 0; i < made.Count; i++) made[i].Name = names[i];
            }
            return items;
        }

        // A QR row's design: the window's, with the row's text, and its caption and font when it's framed (the
        // website's default caption for a row without one, the design's font for a row without one, the default
        // font for "Default"). Text files have no logo: the website's batch writes them at the level chosen.
        public static QrDesign DesignFor(BatchKind kind, BatchRow row)
        {
            QrDesign design = kind.Design.Clone();
            design.Data = row.Data;
            if (kind.Format == BatchFormat.Text) design.Logo = null;
            if (!kind.Frame) design.Frame = null;
            else
            {
                if (design.Frame == null) design.Frame = new QrFrame();
                design.Frame.Text = row.FrameText.Length > 0 ? row.FrameText : QrFrame.DefaultText;
                if (row.Font.Length > 0)
                {
                    string font = MainWindow.WebsiteFontValue(row.Font);
                    design.Frame.FontFamily = font.Length > 0 ? font : null;
                }
            }
            return design;
        }

        // the file for a row (the window's thread: the pictures are drawn with WPF)
        public static byte[] Make(BatchItem item, BatchKind kind, out string svgOrText)
        {
            svgOrText = null;
            if (kind.Barcode != null)
            {
                BarcodeSvg made = Barcode.Make(item.Row.Data, kind.Barcode.Value, kind.Look.Options, out string error);
                if (made == null) throw new QrException(error);
                var art = new BarcodeArt(made);
                switch (kind.Format)
                {
                    case BatchFormat.Png: return QrExport.Png(BarcodeExport.Picture(art, kind.Look, kind.Print));
                    case BatchFormat.Jpg: return QrExport.Jpg(BarcodeExport.Picture(art, kind.Look, kind.Print, true), 95);
                    case BatchFormat.Pdf: return BarcodeExport.Pdf(art, kind.Look, kind.Print, PdfTitle(item, kind));
                    default:
                        svgOrText = BarcodeExport.Svg(made, kind.Look, kind.Print);
                        return new UTF8Encoding(false).GetBytes(svgOrText);
                }
            }
            QrDesign design = DesignFor(kind, item.Row);
            if (kind.Format == BatchFormat.Text)
            {
                svgOrText = QrExport.Text(design, kind.TextStyle);
                if (kind.Markdown) svgOrText = QrExport.Markdown(svgOrText);
                return new UTF8Encoding(false).GetBytes(svgOrText);
            }
            var qr = new QrArt(design);
            switch (kind.Format)
            {
                case BatchFormat.Png: return QrExport.Png(QrExport.Picture(qr, kind.Print, kind.WholePixels));
                case BatchFormat.Jpg: return QrExport.Jpg(QrExport.Picture(qr, kind.Print, kind.WholePixels, true));
                case BatchFormat.Pdf: return QrExport.Pdf(qr, kind.Print, kind.WholePixels, PdfTitle(item, kind));
                default:
                    svgOrText = QrExport.Svg(qr, kind.Print);
                    return new UTF8Encoding(false).GetBytes(svgOrText);
            }
        }

        // the website's PDF titles: the CSV's file name, else "QR code <n>" (QR, n from 1 after the header) or the text
        static string PdfTitle(BatchItem item, BatchKind kind) =>
            item.Row.FileName.Length > 0 ? item.Row.FileName : kind.Barcode != null ? item.Row.Data : "QR code " + (item.Row.Row - 1);

        // The picture a scanner would see in a file (the window's thread): a picture's or PDF's own pixels, on white
        // where they're see-through, as on paper; a text file's characters as the squares they stand for; for SVG, the
        // code drawn as the scan check draws it (the file itself is checked against what was made)
        public static BitmapSource Picture(byte[] bytes, BatchItem item, BatchKind kind)
        {
            switch (kind.Format)
            {
                case BatchFormat.Png:
                case BatchFormat.Jpg:
                    using (var stream = new MemoryStream(bytes))
                    {
                        BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                        return OnWhite(frame);
                    }
                case BatchFormat.Pdf:
                    return PdfFile.ReadPicture(bytes);
                case BatchFormat.Text:
                    return TextPicture(new UTF8Encoding(false).GetString(bytes), kind.TextStyle);
            }
            if (kind.Barcode != null)
            {
                BarcodeSvg made = Barcode.Make(item.Row.Data, kind.Barcode.Value, kind.Look.Options, out _);
                return made == null ? null : BarcodeExport.CheckPicture(new BarcodeArt(made), kind.Look);
            }
            var art = new QrArt(DesignFor(kind, item.Row));
            double k = Math.Min(1, 1600 / Math.Max(art.W, art.H));
            return art.Render(k, (int)Math.Floor(art.W * k), (int)Math.Floor(art.H * k), Colors.White);
        }

        // a picture over white
        static BitmapSource OnWhite(BitmapSource picture)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var whole = new System.Windows.Rect(0, 0, picture.PixelWidth, picture.PixelHeight);
                dc.DrawRectangle(Brushes.White, null, whole);
                dc.DrawImage(picture, whole);
            }
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            var white = new RenderTargetBitmap(picture.PixelWidth, picture.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            white.Render(visual);
            white.Freeze();
            return white;
        }

        // A text file's code drawn back from its characters, 4 pixels a module, black on white: half blocks are two
        // modules each (▀ the top, ▄ the bottom, █ both), full blocks are ██ for a dark module, and ASCII is ## for a
        // light one; a Markdown file's ``` lines are left out
        static BitmapSource TextPicture(string text, QrExport.TextStyle style)
        {
            List<string> lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            int fence = lines.FindIndex(l => l.StartsWith("```", StringComparison.Ordinal));
            if (fence >= 0)
            {
                int close = lines.FindIndex(fence + 1, l => l.StartsWith("```", StringComparison.Ordinal));
                lines = lines.GetRange(fence + 1, (close < 0 ? lines.Count : close) - fence - 1);
            }
            var grid = new List<bool[]>();
            foreach (string line in lines)
            {
                if (style == QrExport.TextStyle.HalfBlocks)
                {
                    grid.Add(line.Select(c => c == '█' || c == '▀').ToArray());
                    grid.Add(line.Select(c => c == '█' || c == '▄').ToArray());
                }
                else grid.Add(Enumerable.Range(0, line.Length / 2).Select(i => style == QrExport.TextStyle.FullBlocks ? line[2 * i] == '█' : line[2 * i] == ' ').ToArray());
            }
            const int k = 4;
            int columns = Math.Max(1, grid.Count == 0 ? 1 : grid.Max(r => r.Length)), height = Math.Max(1, grid.Count) * k, width = columns * k;
            var pixels = Enumerable.Repeat((byte)255, width * height).ToArray();
            for (int r = 0; r < grid.Count; r++)
                for (int c = 0; c < grid[r].Length; c++)
                    if (grid[r][c])
                        for (int y = 0; y < k; y++)
                            for (int x = 0; x < k; x++) pixels[(r * k + y) * width + c * k + x] = 0;
            BitmapSource picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
            picture.Freeze();
            return picture;
        }

        // what a scanner reads from a file's picture, as the window's scan check reads it (any thread)
        public static string Read(byte[] grays, int width, int height, BatchKind kind)
        {
            if (kind.Barcode == null) return ScanCheck.Read(grays, width, height)?.Text;
            BarcodeFormat format = BarcodeFormats.Get(kind.Barcode.Value);
            return BarcodeExport.Read(grays, width, height, format) ?? BarcodeExport.Read(Padded(grays, width, height, kind, out int w, out int h), w, h, format);
        }

        // the picture with blank room round it in its background, for the readers that need it (see CheckPicture)
        static byte[] Padded(byte[] grays, int width, int height, BatchKind kind, out int paddedWidth, out int paddedHeight)
        {
            int pad = Math.Max(8, Math.Min(width, height) / 4);
            paddedWidth = width + 2 * pad;
            paddedHeight = height + 2 * pad;
            Color paper = kind.Look.Transparent ? Colors.White : Css.Paint(kind.Look.Background);
            double a = paper.A / 255.0;
            byte shade = (byte)Math.Round((0.299 * paper.R + 0.587 * paper.G + 0.114 * paper.B) * a + 255 * (1 - a));
            var padded = Enumerable.Repeat(shade, paddedWidth * paddedHeight).ToArray();
            for (int y = 0; y < height; y++) Buffer.BlockCopy(grays, y * width, padded, (y + pad) * paddedWidth + pad, width);
            return padded;
        }

        // a row's code for printing: its shapes at the size its PDF has, its file name as the caption (the window's
        // thread); null when it can't be made
        public static Printing.PrintCode PrintCodeFor(BatchItem item, BatchKind kind)
        {
            if (kind.Barcode != null)
            {
                BarcodeSvg made = Barcode.Make(item.Row.Data, kind.Barcode.Value, kind.Look.Options, out _);
                if (made == null) return null;
                var art = new BarcodeArt(made);
                return new Printing.PrintCode
                {
                    Drawing = art.ToDrawing(Css.Paint(kind.Look.Color), kind.Look.Transparent ? (Color?)null : Css.Paint(kind.Look.Background)),
                    Canvas = new System.Windows.Rect(0, 0, art.W, art.H),
                    Size = Printing.PrintSheet.BarcodeSize(art, kind.Look, kind.Print),
                    Caption = item.Name,
                };
            }
            try
            {
                QrDesign design = DesignFor(kind, item.Row);
                var qr = new QrArt(design);
                return new Printing.PrintCode { Drawing = qr.ToDrawing(), Canvas = new System.Windows.Rect(0, 0, qr.W, qr.H), Size = Printing.PrintSheet.QrSize(qr, kind.Print, kind.WholePixels), Caption = item.Name };
            }
            catch (QrException)
            {
                return null;
            }
        }

        // A name not taken in a folder: "name (2).png", "name (3).png" and so on, as Windows numbers copies
        public static string Numbered(string folder, string name, string extension, ICollection<string> taken)
        {
            string candidate = name + extension;
            for (int n = 2; taken.Contains(candidate) || File.Exists(Path.Combine(folder, candidate)); n++) candidate = name + " (" + n + ")" + extension;
            return candidate;
        }

        // a short look at a row's text for the list: one line, at most 60 characters
        public static string Glimpse(string text)
        {
            string line = Regex.Replace(text ?? "", "\r\n|\r|\n", " ⏎ ");
            return line.Length <= 60 ? line : line.Substring(0, 57) + "…";
        }
    }
}
