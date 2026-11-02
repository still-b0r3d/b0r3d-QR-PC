using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using B0r3dQR.Barcodes;
using B0r3dQR.Printing;
using B0r3dQR.Qr;

namespace B0r3dQR.Tests
{
    // Printing: the pages laid out (no printer is used: the paper is given).
    static partial class TestMain
    {
        static FixedPage PageOf(FixedDocument document, int index) => (FixedPage)document.Pages[index].Child;

        static Rect Where(UIElement element) => new Rect(FixedPage.GetLeft(element), FixedPage.GetTop(element), ((FrameworkElement)element).Width, ((FrameworkElement)element).Height);

        // a page drawn at `dpi`, on white
        static BitmapSource DrawPage(FixedPage page, Size paper, double dpi)
        {
            page.Measure(paper);
            page.Arrange(new Rect(paper));
            page.UpdateLayout();
            var picture = new RenderTargetBitmap((int)(paper.Width * dpi / 96), (int)(paper.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            var white = new DrawingVisual();
            using (DrawingContext dc = white.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(paper));
            picture.Render(white);
            picture.Render(page);
            return picture;
        }

        // a page read with the QR scan check's reader
        static string ReadPage(FixedPage page, Size paper, double dpi)
        {
            byte[] grays = ScanCheck.Grays(DrawPage(page, paper, dpi), out int w, out int h);
            return ScanCheck.Read(grays, w, h)?.Text;
        }

        // a picture over white paper (a see-through PNG's clear pixels would count as black)
        static BitmapSource OnPaper(BitmapSource picture)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var whole = new Rect(0, 0, picture.PixelWidth, picture.PixelHeight);
                dc.DrawRectangle(Brushes.White, null, whole);
                dc.DrawImage(picture, whole);
            }
            var white = new RenderTargetBitmap(picture.PixelWidth, picture.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            white.Render(visual);
            return white;
        }

        // the box round a picture's dark pixels, in its pixels (what's printed of a code, its blank margin left out)
        static Int32Rect DarkBox(BitmapSource picture)
        {
            byte[] grays = ScanCheck.Grays(picture, out int w, out int h);
            int left = w, top = h, right = -1, bottom = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (grays[y * w + x] < 128)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
            return right < 0 ? Int32Rect.Empty : new Int32Rect(left, top, right - left + 1, bottom - top + 1);
        }

        // One code alone: the middle of the page, exactly its print size; a sheet of copies, as many as fit, each with
        // its caption and cut marks; a code bigger than the paper made smaller to fit, and said so
        static void PrintingLaysOutPages()
        {
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org/print");
            window.PrintCheck.IsChecked = true;   // 40 mm at 300 DPI, the website's default print size
            Pump(() => false, 50);
            PrintWindow print = window.PrintWindowFor();
            print.PaperOverride = PrintSheet.A4;
            LayOut(print.Root, 760, 720);
            print.Lay();
            Check(print.Document.Pages.Count == 1, "one code alone: " + print.Document.Pages.Count + " pages");
            Image code = PageOf(print.Document, 0).Children.OfType<Image>().Single();
            Rect at = Where(code);
            double mm = at.Width / 96 * 25.4;
            Check(Math.Abs(mm - 40) < 0.1 && Math.Abs(at.Width - at.Height) < 0.01, "the code should print 40 mm wide, not " + mm.ToString("0.00") + " × " + (at.Height / 96 * 25.4).ToString("0.00") + " mm");
            Check(Math.Abs(at.X + at.Width / 2 - PrintSheet.A4.Width / 2) < 0.5 && Math.Abs(at.Y + at.Height / 2 - PrintSheet.A4.Height / 2) < 0.5, "it should be in the middle of the page: " + at);
            Check(ReadPage(PageOf(print.Document, 0), PrintSheet.A4, 200) == "https://b0r3d.org/print", "the printed page should scan as the code");
            Check(print.SizeLine.Text.Contains("40 × 40 mm"), "the size line: " + print.SizeLine.Text);

            // what's drawn is the code with its blank margin, as its PNG at the print size has it: with no background
            // too, where nothing marks the margin
            foreach (bool background in new[] { true, false })
            {
                window.BackgroundCheck.IsChecked = background;
                Pump(() => false, 50);
                PrintWindow drawn = window.PrintWindowFor();
                drawn.PaperOverride = PrintSheet.A4;
                LayOut(drawn.Root, 760, 720);
                drawn.Lay();
                Int32Rect printed = DarkBox(DrawPage(PageOf(drawn.Document, 0), PrintSheet.A4, 300));
                Int32Rect png = DarkBox(OnPaper(QrExport.Picture(new QrArt(window.CurrentDesign), window.PrintSettings, false)));
                Check(Math.Abs(printed.Width - png.Width) <= 2 && Math.Abs(printed.Height - png.Height) <= 2,
                    (background ? "on its background" : "with no background") + ": the printed code's dark part is " + printed.Width + " × " + printed.Height + " px at 300 DPI, its PNG's " + png.Width + " × " + png.Height);
            }
            window.BackgroundCheck.IsChecked = true;
            Pump(() => false, 50);

            // a sheet of copies: a page full to start with
            print.SheetChoice.IsChecked = true;
            int perPage = PrintSheet.PerPage(at.Size, new PrintOptions { Page = PrintSheet.A4, Sheet = true });
            Check(print.CopiesBox.Value == perPage && print.Document.Pages.Count == 1 && PageOf(print.Document, 0).Children.OfType<Image>().Count() == perPage && perPage >= 12,
                "a sheet of copies: " + print.CopiesBox.Value + " copies, " + PageOf(print.Document, 0).Children.OfType<Image>().Count() + " on the page, " + perPage + " fit");
            FixedPage sheet = PageOf(print.Document, 0);
            Check(sheet.Children.OfType<TextBlock>().Count() == perPage && sheet.Children.OfType<TextBlock>().All(t => t.Text == "https://b0r3d.org/print"),
                "a caption under each copy, its text: " + sheet.Children.OfType<TextBlock>().FirstOrDefault()?.Text);
            Check(sheet.Children.OfType<Path>().Count() == 1 && ((GeometryGroup)sheet.Children.OfType<Path>().Single().Data).Children.Count == perPage * 8, "8 cut marks for each code");
            List<Rect> codes = sheet.Children.OfType<Image>().Select(Where).ToList();
            Check(codes.All(r => Math.Abs(r.Width / 96 * 25.4 - 40) < 0.1) && codes.All(r => r.Left >= PrintSheet.Margin - 0.01 && r.Right <= PrintSheet.A4.Width - PrintSheet.Margin + 0.01),
                "every copy at its size, inside the margins");
            Check(!codes.Where((r, i) => codes.Skip(i + 1).Any(o => o.IntersectsWith(r) && o != r)).Any(), "copies overlap");
            print.CopiesBox.Value = perPage + 1;
            Check(print.Document.Pages.Count == 2 && PageOf(print.Document, 1).Children.OfType<Image>().Count() == 1, "one more copy than fits takes a second page");
            print.CaptionsCheck.IsChecked = false;
            print.MarksCheck.IsChecked = false;
            Check(!PageOf(print.Document, 0).Children.OfType<TextBlock>().Any() && !PageOf(print.Document, 0).Children.OfType<Path>().Any(), "no captions and no marks when they're off");

            // a Wi-Fi network's caption is its name, as Recent codes shows it: never its password
            Type(window, "WIFI:T:WPA;S:home;P:hunter2;;");
            PrintWindow wifi = window.PrintWindowFor();
            wifi.PaperOverride = PrintSheet.A4;
            LayOut(wifi.Root, 760, 720);
            wifi.SheetChoice.IsChecked = true;
            Check(PageOf(wifi.Document, 0).Children.OfType<TextBlock>().All(t => t.Text == "home"), "a Wi-Fi code's caption: " + PageOf(wifi.Document, 0).Children.OfType<TextBlock>().FirstOrDefault()?.Text);

            // bigger than the paper: shrunk to fit, said
            window.PrintWidthBox.Value = 400;
            Pump(() => false, 50);
            PrintWindow big = window.PrintWindowFor();
            big.PaperOverride = PrintSheet.A4;
            LayOut(big.Root, 760, 720);
            big.Lay();
            Rect bigAt = Where(PageOf(big.Document, 0).Children.OfType<Image>().Single());
            Check(big.Shrunk && big.ShrunkLine.Visibility == Visibility.Visible && bigAt.Width <= PrintSheet.A4.Width - 2 * PrintSheet.Margin + 0.01, "a 400 mm code should be shrunk to the paper: " + bigAt);
        }

        // A barcode prints whole pixels a module at its print size, as its files are; a batch prints on sheets with
        // each code's file name under it
        static void PrintingBarcodesAndBatches()
        {
            MainWindow window = NewMain();
            window.ChooseCodeType(BarcodeType.Ean13, "400638133393");
            WaitForCheck(window);
            PrintWindow print = window.PrintWindowFor();
            print.PaperOverride = PrintSheet.Letter;
            LayOut(print.Root, 760, 720);
            print.Lay();
            Rect at = Where(PageOf(print.Document, 0).Children.OfType<Image>().Single());
            int modulePx = BarcodeExport.ModulePx(window.BarcodeShown.Barcode, window.BarcodeSettings, window.BarcodePrintSettings);
            Check(Math.Abs(at.Width - window.BarcodeShown.W * modulePx) < 0.01, "the barcode at its picture's size: " + at.Width + " for " + window.BarcodeShown.W + " modules");
            // the bars and digits drawn as its PNG has them, with no background too, and the page scans as the barcode
            foreach (bool seeThrough in new[] { false, true })
            {
                window.TransparentCheck.IsChecked = seeThrough;
                WaitForCheck(window);
                PrintWindow drawn = window.PrintWindowFor();
                drawn.PaperOverride = PrintSheet.Letter;
                LayOut(drawn.Root, 760, 720);
                drawn.Lay();
                BitmapSource page = DrawPage(PageOf(drawn.Document, 0), PrintSheet.Letter, 96);
                Int32Rect printed = DarkBox(page), png = DarkBox(OnPaper(BarcodeExport.Picture(window.BarcodeShown, window.BarcodeSettings, window.BarcodePrintSettings)));
                Check(Math.Abs(printed.Width - png.Width) <= 1 && Math.Abs(printed.Height - png.Height) <= 1,
                    (seeThrough ? "with no background" : "on white") + ": the printed barcode's dark part is " + printed.Width + " × " + printed.Height + ", its PNG's " + png.Width + " × " + png.Height);
                byte[] grays = ScanCheck.Grays(page, out int pw, out int ph);
                Check(BarcodeExport.Read(grays, pw, ph, BarcodeFormats.Get(BarcodeType.Ean13)) == "4006381333931", "the printed barcode page should scan");
            }
            window.TransparentCheck.IsChecked = false;
            WaitForCheck(window);
            print.SheetChoice.IsChecked = true;
            Check(PageOf(print.Document, 0).Children.OfType<TextBlock>().All(t => t.Text == "4006381333931"), "a barcode's caption is what it holds");

            window.ChooseCodeType(null);
            BatchWindow batch = NewBatch(window, "url,frameText,fileName\nhttps://b0r3d.org/1,,first\nhttps://b0r3d.org/2,,second\nhttps://b0r3d.org/3,,third\n");
            PrintWindow sheets = batch.PrintWindowFor();
            sheets.PaperOverride = PrintSheet.A4;
            LayOut(sheets.Root, 760, 720);
            sheets.Lay();
            Check(sheets.SheetChoice.IsChecked == true && sheets.CopiesRow.Visibility != Visibility.Visible && sheets.Document.Pages.Count == 1, "a batch prints on a sheet, with no copies to choose");
            Check(PageOf(sheets.Document, 0).Children.OfType<TextBlock>().Select(t => t.Text).SequenceEqual(new[] { "first", "second", "third" }), "each code's file name under it");
            sheets.AloneChoice.IsChecked = true;
            Check(sheets.Document.Pages.Count == 3, "one a page: " + sheets.Document.Pages.Count + " pages");
            for (int p = 0; p < 3; p++)
                Check(ReadPage(PageOf(sheets.Document, p), PrintSheet.A4, 150) == "https://b0r3d.org/" + (p + 1), "page " + (p + 1) + " should hold row " + (p + 1) + "'s code");
            batch.Close();

            // a one-row batch says so; label paper with no room for a caption still lays out (no caption, no error)
            BatchWindow one = NewBatch(window, "url\nhttps://b0r3d.org/one\n");
            PrintWindow single = one.PrintWindowFor();
            Check(single.WhatLine.Text == "The batch's one code, at its own size.", "a one-row batch: " + single.WhatLine.Text);
            single.PaperOverride = new Size(PrintSheet.Mm(40), PrintSheet.Mm(12));
            LayOut(single.Root, 760, 720);
            single.Lay();
            Check(single.Document.Pages.Count == 1 && !PageOf(single.Document, 0).Children.OfType<TextBlock>().Any() && single.Shrunk, "a 40 × 12 mm label: one shrunk code, no room for a caption");
            one.Close();
        }
    }
}
