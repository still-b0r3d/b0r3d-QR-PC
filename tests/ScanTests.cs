using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Qr;
using B0r3dQR.Scan;
using ZXing;
using ZXing.Common;
using Reader = B0r3dQR.Scan.Reader;

namespace B0r3dQR.Tests
{
    // Reading codes: the reader, pictures, folders, the clipboard, the screen and the Scan tab.
    static partial class TestMain
    {
        // ---------- drawing test pictures ----------

        // a white canvas of grey levels
        static byte[] Canvas(int width, int height, byte shade = 255) => Enumerable.Repeat(shade, width * height).ToArray();

        // a QR code made by b0r3d QR, k pixels a module, with a 4-module quiet zone
        static void PutQr(byte[] canvas, int canvasWidth, string text, int left, int top, int k, QrLevel level = QrLevel.Q, bool inverted = false)
        {
            QrCode code = QrCode.Make(text, level);
            for (int my = -4; my < code.Size + 4; my++)
                for (int mx = -4; mx < code.Size + 4; mx++)
                {
                    bool dark = mx >= 0 && my >= 0 && mx < code.Size && my < code.Size && code[my, mx];
                    byte shade = dark != inverted ? (byte)0 : (byte)255;
                    for (int py = 0; py < k; py++)
                        for (int px = 0; px < k; px++)
                            canvas[(top + (my + 4) * k + py) * canvasWidth + left + (mx + 4) * k + px] = shade;
                }
        }

        // a code drawn from ZXing's writer: 2D codes k pixels a module; 1D codes k pixels a bar
        // module, `height` pixels tall, or turned on their side
        static void PutZxing(byte[] canvas, int canvasWidth, BarcodeFormat format, string text, int left, int top, int k, int height = 0, bool sideways = false,
            IDictionary<EncodeHintType, object> hints = null)
        {
            BitMatrix m = new MultiFormatWriter().encode(text, format, 0, 0, hints);
            bool oneD = m.Height == 1 || height > 0;
            int rows = oneD ? Math.Max(1, height / k) : m.Height;
            for (int my = -4; my < rows + 4; my++)
                for (int mx = -10; mx < m.Width + 10; mx++)
                {
                    bool dark = mx >= 0 && my >= 0 && mx < m.Width && my < rows && m[mx, oneD ? 0 : my];
                    for (int py = 0; py < k; py++)
                        for (int px = 0; px < k; px++)
                        {
                            int x = (mx + 10) * k + px, y = (my + 4) * k + py;
                            if (sideways) { int t = x; x = y; y = t; }
                            canvas[(top + y) * canvasWidth + left + x] = dark ? (byte)0 : (byte)255;
                        }
                }
        }

        static List<FoundCode> ReadCanvas(byte[] canvas, int width, int height, string source = "test") =>
            Reader.ReadAll(canvas, width, height, source, CancellationToken.None);

        // marked "can't be uploaded" (0) for Windows' cloud clipboard
        static bool OffTheCloud(IDataObject data) =>
            data.GetData("CanUploadToCloudClipboard") is MemoryStream flag && flag.Length == 4 && BitConverter.ToInt32(flag.ToArray(), 0) == 0;

        static BitmapSource GrayPicture(byte[] canvas, int width, int height)
        {
            BitmapSource picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, canvas, width);
            picture.Freeze();
            return picture;
        }

        // ---------- the reader ----------

        static void ReadsEveryCode()
        {
            const int w = 1500, h = 980;
            byte[] canvas = Canvas(w, h);
            PutQr(canvas, w, "https://b0r3d.org", 40, 40, 6);
            PutQr(canvas, w, "WIFI:T:WPA;S:home;P:hunter2;;", 520, 40, 5);
            PutZxing(canvas, w, BarcodeFormat.DATA_MATRIX, "b0r3d data matrix", 1040, 60, 8);
            PutZxing(canvas, w, BarcodeFormat.EAN_13, "9506000134352", 40, 560, 3, 150);
            PutZxing(canvas, w, BarcodeFormat.CODE_128, "B0R3D-128", 640, 600, 3, 120);
            List<FoundCode> found = ReadCanvas(canvas, w, h, "the test picture");
            string seen = string.Join(" | ", found.Select(f => f.Format + ": " + Short(f.Text) + " at " + (int)f.X + "," + (int)f.Y));
            Check(found.Count == 5, found.Count + " codes found, not 5: " + seen);

            FoundCode link = found.Single(f => f.Text == "https://b0r3d.org");
            Check(link.Kind == "Link" && link.Detail == "b0r3d.org" && link.Format == "QR code" && link.IsWebLink && link.CanMakeAgain, "the link: " + link.Kind + " / " + link.Summary);
            Check(link.Level == QrLevel.Q, "the link's error correction should read back as Q, not " + link.Level);
            FoundCode wifi = found.Single(f => f.Text.StartsWith("WIFI:"));
            Check(wifi.Kind == "Wi-Fi network" && wifi.Detail == "network home, WPA" && !wifi.IsWebLink, "the Wi-Fi code: " + wifi.Kind + " / " + wifi.Detail);
            Check(!wifi.Summary.Contains("hunter2"), "the Wi-Fi password is in the summary line");
            FoundCode product = found.Single(f => f.Format == "EAN-13");
            Check(product.Kind == "Product number" && product.Text == "9506000134352" && product.CanMakeAgain && product.Barcode == Barcodes.BarcodeType.Ean13, "the EAN-13: " + product.Kind + ", made again as " + product.Barcode);
            Check(found.Any(f => f.Format == "Data Matrix" && f.Text == "b0r3d data matrix" && f.Barcode == Barcodes.BarcodeType.DataMatrix), "no Data Matrix to make again: " + seen);
            Check(found.Any(f => f.Format == "Code 128" && f.Text == "B0R3D-128" && f.Barcode == Barcodes.BarcodeType.Code128), "no Code 128 to make again: " + seen);
            Check(found.All(f => f.HasThumbnail && f.Thumbnail.PixelWidth <= 192 && f.Thumbnail.PixelHeight <= 192 && f.Source == "the test picture"), "a code without a proper thumbnail or source");
            // reading order: the top row first, left to right
            Check(found.Take(3).Select(f => f.Format).SequenceEqual(new[] { "QR code", "QR code", "Data Matrix" }), "not in reading order: " + seen);
        }

        static void ReadsLightOnDark()
        {
            const int w = 600, h = 600;
            byte[] canvas = Canvas(w, h, 0);
            PutQr(canvas, w, "light on dark", 100, 100, 8, inverted: true);
            List<FoundCode> found = ReadCanvas(canvas, w, h);
            Check(found.Count == 1 && found[0].Text == "light on dark", "a light code on a dark background wasn't read");
        }

        static void ReadsSmallCodeOnABigScreen()
        {
            const int w = 3840, h = 2160;
            byte[] canvas = Canvas(w, h, 40);
            for (int y = 300; y < 1500; y++) for (int x = 300; x < 2400; x++) canvas[y * w + x] = 230;  // a light window
            PutQr(canvas, w, "https://b0r3d.org/small", 3000, 1500, 3);
            var watch = Stopwatch.StartNew();
            List<FoundCode> found = ReadCanvas(canvas, w, h);
            Check(found.Count == 1 && found[0].Text == "https://b0r3d.org/small", "a small code on a 4K screen wasn't found");
            Check(watch.ElapsedMilliseconds < 15000, "reading a 4K screen took " + watch.ElapsedMilliseconds + " ms");
        }

        static void ReadsCodesAwayFromTheMiddle()
        {
            // a 4K screen full of heading-sized letters, with an Aztec code (a train ticket, say) and
            // a Data Matrix in the corners, where those codes' own finders don't look
            const int w = 3840, h = 2160;
            byte[] canvas = Canvas(w, h);
            var random = new Random(31);
            for (int top = 40; top + 40 < h; top += 60)
                for (int left = 40; left + 30 < w; left += 34)
                    for (int dot = 0; dot < 35; dot++)   // 5 x 7 dots of 5 pixels, two in three of them dark
                        if (random.Next(3) > 0)
                            for (int y = 0; y < 5; y++)
                                for (int x = 0; x < 5; x++)
                                    canvas[(top + dot / 5 * 5 + y) * w + left + dot % 5 * 5 + x] = 0;
            PutZxing(canvas, w, BarcodeFormat.AZTEC, "https://b0r3d.org/ticket/12345", 3420, 60, 6);
            PutZxing(canvas, w, BarcodeFormat.DATA_MATRIX, "b0r3d in the corner", 60, 1840, 6);
            var watch = Stopwatch.StartNew();
            List<FoundCode> found = ReadCanvas(canvas, w, h);
            string seen = string.Join(" | ", found.Select(f => f.Format + ": " + Short(f.Text) + " at " + (int)f.X + "," + (int)f.Y));
            Check(found.Count == 2, found.Count + " codes found, not 2: " + seen);
            Check(found.Any(f => f.Format == "Aztec" && f.Kind == "Link" && f.Text == "https://b0r3d.org/ticket/12345" && f.X > 3400 && f.Y < 300), "no Aztec in the top corner: " + seen);
            Check(found.Any(f => f.Format == "Data Matrix" && f.Text == "b0r3d in the corner" && f.X < 400 && f.Y > 1800), "no Data Matrix in the bottom corner: " + seen);
            Check(watch.ElapsedMilliseconds < 15000, "reading took " + watch.ElapsedMilliseconds + " ms");
            Console.WriteLine("      (a 4K screen of letters read in " + watch.ElapsedMilliseconds + " ms)");
        }

        static void ReadsSidewaysBarcode()
        {
            const int w = 500, h = 900;
            byte[] canvas = Canvas(w, h);
            PutZxing(canvas, w, BarcodeFormat.EAN_13, "9506000134352", 60, 40, 4, 200, sideways: true);
            List<FoundCode> found = ReadCanvas(canvas, w, h);
            Check(found.Count == 1 && found[0].Text == "9506000134352", "a barcode on its side: " + found.Count + " codes (" + string.Join(", ", found.Select(f => f.Text)) + ")");
        }

        // tiny and flat pictures (a 1x1 spacer, a near-white thumbnail, a dark banner) made one of
        // ZXing's readers throw; they're just pictures with no code
        static void OddPicturesHaveNoCode()
        {
            foreach (var (w, h, shade) in new[] { (1, 1, 255), (1, 1, 0), (2, 2, 128), (16, 16, 250), (24, 24, 128), (64, 32, 130), (400, 30, 20), (30, 400, 240) })
            {
                List<FoundCode> found = null;
                try
                {
                    found = ReadCanvas(Canvas(w, h, (byte)shade), w, h);
                }
                catch (Exception e)
                {
                    Check(false, "a " + w + "x" + h + " picture of grey " + shade + " threw " + e.GetType().Name + ": " + e.Message);
                }
                Check(found.Count == 0, "a " + w + "x" + h + " plain picture gave " + found.Count + " codes");
            }
        }

        static void NothingThereSaysSo()
        {
            const int w = 800, h = 600;
            var random = new Random(4711);
            byte[] noise = new byte[w * h];
            random.NextBytes(noise);
            Check(ReadCanvas(noise, w, h).Count == 0, "noise was read as a code");
            Check(ReadCanvas(Canvas(w, h), w, h).Count == 0, "a blank picture was read as a code");
        }

        static void SeeThroughCountsAsWhite()
        {
            // a black code on a see-through background, as a PNG saved from the website can be
            const int w = 400, h = 400;
            byte[] gray = Canvas(w, h);
            PutQr(gray, w, "on glass", 40, 40, 8);
            var bgra = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++) bgra[4 * i + 3] = gray[i] == 0 ? (byte)255 : (byte)0;  // black where dark, clear elsewhere
            BitmapSource glass = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
            glass.Freeze();
            Gray g = Pictures.Grays(glass, "glass.png");
            Check(g.Pixels[0] == 255, "a see-through pixel came out as " + g.Pixels[0] + ", not white");
            Check(ReadCanvas(g.Pixels, g.Width, g.Height).Select(f => f.Text).SequenceEqual(new[] { "on glass" }), "a code on a see-through background wasn't read");
        }

        // ---------- files, folders, the clipboard ----------

        static void ReadsPictureFiles()
        {
            string folder = Path.Combine(Work, "files");
            Fresh(folder);
            const int w = 360, h = 360;
            byte[] canvas = Canvas(w, h);
            PutQr(canvas, w, "from a file", 30, 30, 8);
            BitmapSource picture = GrayPicture(canvas, w, h);
            var makers = new Dictionary<string, Func<BitmapEncoder>>
            {
                { "code.png", () => new PngBitmapEncoder() },
                { "code.jpg", () => new JpegBitmapEncoder { QualityLevel = 92 } },
                { "code.bmp", () => new BmpBitmapEncoder() },
                { "code.gif", () => new GifBitmapEncoder() },
            };
            foreach (var maker in makers)
            {
                BitmapEncoder encoder = maker.Value();
                encoder.Frames.Add(BitmapFrame.Create(picture));
                string file = Path.Combine(folder, maker.Key);
                using (FileStream stream = File.Create(file)) encoder.Save(stream);
                List<Gray> pages = Pictures.Load(file);
                Check(pages.Count == 1 && pages[0].Name == maker.Key, maker.Key + ": " + pages.Count + " pages");
                Check(ReadCanvas(pages[0].Pixels, pages[0].Width, pages[0].Height).Select(f => f.Text).SequenceEqual(new[] { "from a file" }), maker.Key + " wasn't read");
            }

            // a two-page TIFF with the code on page 2
            var tiff = new TiffBitmapEncoder();
            tiff.Frames.Add(BitmapFrame.Create(GrayPicture(Canvas(w, h), w, h)));
            tiff.Frames.Add(BitmapFrame.Create(picture));
            string tiffFile = Path.Combine(folder, "scan.tif");
            using (FileStream stream = File.Create(tiffFile)) tiff.Save(stream);
            List<Gray> tiffPages = Pictures.Load(tiffFile);
            Check(tiffPages.Count == 2 && tiffPages[1].Name == "scan.tif, page 2", "the TIFF's pages: " + string.Join(", ", tiffPages.Select(p => p.Name)));
            Check(ReadCanvas(tiffPages[1].Pixels, tiffPages[1].Width, tiffPages[1].Height).Count == 1, "page 2 of the TIFF wasn't read");

            // a multi-page PDF and a rotated PDF read through Windows' built-in PDF reader
            string multiPdf = Path.Combine(folder, "pages.pdf");
            File.WriteAllBytes(multiPdf, TestVectorQrPdf(new[] { "pdf page 1", "pdf page 2", "pdf page 3" }, 595, 842));
            List<Gray> pdfPages = Pictures.Load(multiPdf);
            Check(pdfPages.Count == 3 && pdfPages[0].Name == "pages.pdf, page 1" && pdfPages[2].Name == "pages.pdf, page 3", "the PDF's pages: " + string.Join(", ", pdfPages.Select(p => p.Name)));
            for (int i = 0; i < 3; i++)
                Check(ReadCanvas(pdfPages[i].Pixels, pdfPages[i].Width, pdfPages[i].Height).Select(f => f.Text).SequenceEqual(new[] { "pdf page " + (i + 1) }), "PDF page " + (i + 1) + " wasn't read");

            string rotatedPdf = Path.Combine(folder, "rotated.pdf");
            File.WriteAllBytes(rotatedPdf, TestVectorQrPdf(new[] { "rotated pdf" }, 595, 842, rotate: 90));
            List<Gray> rotPages = Pictures.Load(rotatedPdf);
            Check(rotPages.Count == 1 && rotPages[0].Width > rotPages[0].Height && ReadCanvas(rotPages[0].Pixels, rotPages[0].Width, rotPages[0].Height).Select(f => f.Text).SequenceEqual(new[] { "rotated pdf" }),
                "rotated PDF wasn't read");

            // a password-protected PDF and a damaged PDF say what's wrong in words
            string lockedPdf = Path.Combine(folder, "locked.pdf");
            File.WriteAllBytes(lockedPdf, TestEncryptedQrPdf("secret", "pw"));
            try
            {
                Pictures.Load(lockedPdf);
                Check(false, "a password-protected PDF opened without a password");
            }
            catch (PictureException e)
            {
                Check(e.Message == "it's protected by a password", "the message for a password-protected PDF: " + e.Message);
            }
            string brokenPdf = Path.Combine(folder, "broken.pdf");
            File.WriteAllText(brokenPdf, "not a pdf");
            try
            {
                Pictures.Load(brokenPdf);
                Check(false, "a text file passed as a PDF");
            }
            catch (PictureException e)
            {
                Check(e.Message == "it isn't a PDF, or it's damaged", "the message for a broken PDF: " + e.Message);
            }

            // what isn't a picture says so in words
            string notPicture = Path.Combine(folder, "notes.png");
            File.WriteAllText(notPicture, "this is not a picture");
            try
            {
                Pictures.Load(notPicture);
                Check(false, "a text file passed as a picture");
            }
            catch (PictureException e)
            {
                Check(e.Message.Length > 0 && !e.Message.Contains("Exception"), "the message: " + e.Message);
            }
            try
            {
                Pictures.Load(Path.Combine(folder, "gone.png"));
                Check(false, "a missing file passed as a picture");
            }
            catch (PictureException e)
            {
                Check(e.Message.Contains("isn't there"), "the message for a missing file: " + e.Message);
            }
        }

        // A PDF's pages are drawn one at a time, each read before the next is drawn and only one drawn at once in
        // a folder scan, so a long PDF takes the memory of one page (a 50-page one took over a gigabyte when every
        // page was drawn first); a poster's page at 40 million pixels at most, as any picture. Past page 50, a page that can't be
        // read, and Cancel between pages: each said, or the codes on the other pages kept.
        static void ReadsPdfsPageByPage()
        {
            string folder = Path.Combine(Work, "pdfs");
            Fresh(folder);
            try
            {
                // Windows' PDF reader is loaded first thing (LoadEarly), its type named only in a method that's never
                // inlined: so a Windows without it starts, and says so when a PDF is opened
                System.Reflection.MethodInfo loader = typeof(PdfPages).GetMethod("LoadReader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Check(PdfPages.LoadEarly() && loader != null && (loader.MethodImplementationFlags & System.Reflection.MethodImplAttributes.NoInlining) != 0,
                    "the reader should load, from a method of its own that's never inlined");

                // 52 small pages, codes on pages 1, 2, 50 and 51: each page handed over with only it drawn since
                // the one before, 50 read and the other 2 said
                string longPdf = Path.Combine(folder, "long.pdf");
                File.WriteAllBytes(longPdf, TestVectorQrPdf(Enumerable.Range(1, 52).Select(p => p == 1 || p == 2 || p == 50 || p == 51 ? "page " + p : null).ToList(), 72, 72));
                int before = PdfPages.Drawn;
                var handed = new List<(string Name, int Drawn)>();
                PdfPages.Reading reading = PdfPages.Read(longPdf, CancellationToken.None, page => handed.Add((page.Name, PdfPages.Drawn - before)));
                Check(reading.Pages == 52 && reading.Read == 50 && reading.Skipped == 2 && reading.Failed.Count == 0,
                    "52 pages: " + reading.Pages + " counted, " + reading.Read + " read, " + reading.Skipped + " left");
                Check(handed.Select(h => h.Drawn).SequenceEqual(Enumerable.Range(1, 50)) && handed[0].Name == "long.pdf, page 1" && handed[49].Name == "long.pdf, page 50",
                    "each page should be handed over before the next is drawn: " + string.Join(", ", handed.Select(h => h.Drawn)));
                Check(reading.LeftOut == "pages 51 to 52 weren't read (b0r3d QR reads a PDF's first 50 pages)", "what was left out: " + reading.LeftOut);
                MainWindow.ScanOutcome longOne = MainWindow.ReadFilesNow(new[] { longPdf }, CancellationToken.None, t => { });
                Check(longOne.Codes.Select(c => c.Text).SequenceEqual(new[] { "page 1", "page 2", "page 50" }) && longOne.Codes[2].Source == "long.pdf, page 50" &&
                      longOne.Summary == "3 codes in long.pdf; pages 51 to 52 weren't read (b0r3d QR reads a PDF's first 50 pages).",
                    "the long PDF's codes and summary: " + longOne.Summary);

                // a page that can't be read: the codes on the pages before and after it kept, and it said, alone or in a folder
                string damaged = Path.Combine(folder, "damaged.pdf");
                File.WriteAllBytes(damaged, TestVectorQrPdf(new[] { "first page", "second page", "third page" }, 144, 144));
                PdfPages.FailPage = page => page == 2;
                MainWindow.ScanOutcome partly = MainWindow.ReadFilesNow(new[] { damaged }, CancellationToken.None, t => { });
                Check(partly.Codes.Select(c => c.Text).SequenceEqual(new[] { "first page", "third page" }) &&
                      partly.Summary == "2 codes in damaged.pdf; page 2 couldn't be read (Windows' PDF reader couldn't draw it).",
                    "a PDF with a page that can't be read: " + partly.Summary);
                // (page 2 of long.pdf can't be read now either)
                MainWindow.ScanOutcome inFolder = MainWindow.ReadFilesNow(new[] { folder }, CancellationToken.None, t => { });
                FoundCode note = inFolder.Codes.SingleOrDefault(c => c.IsProblem && c.Source == "damaged.pdf");
                FoundCode longNote = inFolder.Codes.SingleOrDefault(c => c.IsProblem && c.Source == "long.pdf");
                Check(note != null && note.Kind == "Not read in full" && note.Detail == "page 2 couldn't be read (Windows' PDF reader couldn't draw it)" &&
                      longNote != null && longNote.Detail == "page 2 couldn't be read (Windows' PDF reader couldn't draw it); pages 51 to 52 weren't read (b0r3d QR reads a PDF's first 50 pages)" &&
                      inFolder.Codes.Where(c => !c.IsProblem).Select(c => c.Text).SequenceEqual(new[] { "first page", "third page", "page 1", "page 50" }) &&
                      inFolder.Summary == "4 codes in 2 of 2 pictures in pdfs; 2 not read in full.",
                    "a folder of PDFs read in part: " + inFolder.Summary + " | " + string.Join(" | ", inFolder.Codes.Select(c => c.Source + ": " + c.Kind + " " + c.Detail)));
                string single = Path.Combine(folder, "single.pdf");
                File.WriteAllBytes(single, TestVectorQrPdf(new[] { "alone" }, 144, 144));
                PdfPages.FailPage = page => true;
                MainWindow.ScanOutcome none = MainWindow.ReadFilesNow(new[] { single }, CancellationToken.None, t => { });
                Check(none.Codes.Count == 0 && none.Summary == "Couldn't open single.pdf: page 1 couldn't be read (Windows' PDF reader couldn't draw it).", "a PDF none of whose pages can be read: " + none.Summary);
                PdfPages.FailPage = null;
                File.Delete(single);

                // Cancel between pages: no page drawn after it
                using (var stop = new CancellationTokenSource())
                {
                    int at = PdfPages.Drawn, pages = 0;
                    try
                    {
                        PdfPages.Read(damaged, stop.Token, page => { if (++pages == 2) stop.Cancel(); });
                        Check(false, "a PDF read carried on after Cancel");
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    Check(pages == 2 && PdfPages.Drawn - at == 2, "Cancel on page 2 should leave page 3 undrawn: " + (PdfPages.Drawn - at) + " drawn");
                }

                // Windows' PDF reader not answering on page 2 (its drawing never ending): given up on after the time it's
                // allowed, the rest of the file said, page 1's code kept, and the one-page lock let go of, so the next
                // read goes on; Cancel while it waits ends it at once, the lock let go of too
                int answer = PdfPages.AnswerMs;
                try
                {
                    PdfPages.AnswerMs = 300;
                    PdfPages.StallPage = page => page == 2;
                    var stuck = Stopwatch.StartNew();
                    var kept = new List<string>();
                    PdfPages.Reading given = PdfPages.Read(damaged, CancellationToken.None, page => kept.AddRange(Reader.ReadAll(page.Pixels, page.Width, page.Height, page.Name, CancellationToken.None).Select(f => f.Text)));
                    long stuckMs = stuck.ElapsedMilliseconds;
                    Check(given.Read == 1 && kept.SequenceEqual(new[] { "first page" }) && given.GaveUpAt == 2 && stuckMs < 5000 &&
                          given.LeftOut == "page 2 couldn't be read (Windows' PDF reader stopped answering); page 3 wasn't read (Windows' PDF reader stopped answering)",
                        "a reader that stops answering on page 2, after " + stuckMs + " ms: " + given.Read + " read, " + given.LeftOut);
                    PdfPages.StallPage = null;
                    Check(PdfPages.Read(damaged, CancellationToken.None, page => { }).Read == 3, "after a page given up on, the next read should go on (the lock let go of)");

                    PdfPages.AnswerMs = 60000;
                    PdfPages.StallPage = page => page == 1;
                    using (var stop = new CancellationTokenSource(300))
                    {
                        var waited = Stopwatch.StartNew();
                        try
                        {
                            PdfPages.Read(damaged, stop.Token, page => { });
                            Check(false, "a read waiting on a reader that doesn't answer went on after Cancel");
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        Check(waited.ElapsedMilliseconds < 5000, "Cancel should end the wait at once, not after " + waited.ElapsedMilliseconds + " ms");
                    }
                    PdfPages.StallPage = null;
                    Check(PdfPages.Read(damaged, CancellationToken.None, page => { }).Read == 3, "after Cancel, the next read should go on (the lock let go of)");
                }
                finally
                {
                    PdfPages.AnswerMs = answer;
                    PdfPages.StallPage = null;
                }

                // three PDFs of four pages in a folder: every code read, never two pages drawn at once
                string three = Path.Combine(folder, "three");
                Fresh(three);
                for (int f = 1; f <= 3; f++)
                    File.WriteAllBytes(Path.Combine(three, "doc" + f + ".pdf"), TestVectorQrPdf(Enumerable.Range(1, 4).Select(p => "doc " + f + " page " + p).ToList(), 144, 144));
                PdfPages.MostAtOnce = 0;
                MainWindow.ScanOutcome all = MainWindow.ReadFilesNow(new[] { three }, CancellationToken.None, t => { });
                Check(all.Codes.Count(c => !c.IsProblem) == 12 && PdfPages.MostAtOnce == 1, "three PDFs at once: " + all.Summary + ", " + PdfPages.MostAtOnce + " pages drawn at once at most");

                // a poster's page (A0) at 40 million pixels at most, as any picture is (about 160 DPI); twelve Letter
                // pages take the memory of one
                string poster = Path.Combine(folder, "poster.pdf");
                File.WriteAllBytes(poster, TestVectorQrPdf(new[] { "poster" }, 2384, 3370));
                List<Gray> posterPage = PdfPages.Load(poster);
                long posterPixels = (long)posterPage[0].Width * posterPage[0].Height;
                Check(PdfPages.MaxPagePixels == Pictures.MaxPixels && posterPixels <= PdfPages.MaxPagePixels && posterPixels > PdfPages.MaxPagePixels * 0.95 && posterPage[0].Height > posterPage[0].Width,
                    "an A0 page should be drawn at up to 40 million pixels: " + posterPage[0].Width + " × " + posterPage[0].Height);
                posterPage = null;
                string letters = Path.Combine(folder, "letters.pdf");
                File.WriteAllBytes(letters, TestVectorQrPdf(Enumerable.Range(1, 12).Select(p => (string)null).ToList(), 612, 792));
                long Held()
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    return Process.GetCurrentProcess().PrivateMemorySize64;
                }
                long start = Held(), most = 0;
                int letterPages = 0;
                PdfPages.Read(letters, CancellationToken.None, page => { letterPages++; most = Math.Max(most, Held() - start); });
                Console.WriteLine("      (12 Letter pages at 200 DPI read with at most " + most / (1 << 20) + " MB more held; drawn first, they held about 180 MB)");
                Check(letterPages == 12 && most < 100L << 20, "12 Letter pages held " + most / (1 << 20) + " MB more at most");
            }
            finally
            {
                PdfPages.FailPage = null;
            }
        }

        // a folder to check a batch: every picture gets a line, its codes or why there are none
        static void ReadsFolders()
        {
            string folder = Path.Combine(Work, "Photos");
            Fresh(folder);
            const int w = 360, h = 360;
            foreach (var (name, text) in new[] { ("a.png", "first"), ("b.png", "second"), ("c.png", null) })
            {
                byte[] canvas = Canvas(w, h);
                if (text != null) PutQr(canvas, w, text, 30, 30, 8);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(GrayPicture(canvas, w, h)));
                using (FileStream stream = File.Create(Path.Combine(folder, name))) png.Save(stream);
            }
            var spacer = new GifBitmapEncoder();  // a 1x1 spacer, which once stopped the whole folder
            spacer.Frames.Add(BitmapFrame.Create(GrayPicture(Canvas(1, 1), 1, 1)));
            using (FileStream stream = File.Create(Path.Combine(folder, "spacer.gif"))) spacer.Save(stream);
            File.WriteAllBytes(Path.Combine(folder, "photo.heic"), Enumerable.Range(0, 400).Select(i => (byte)(i * 37)).ToArray());  // not really a picture
            File.WriteAllText(Path.Combine(folder, "readme.txt"), "not a picture");
            Directory.CreateDirectory(Path.Combine(folder, "inside"));
            File.Copy(Path.Combine(folder, "a.png"), Path.Combine(folder, "inside", "deeper.png"));

            var progress = new List<string>();
            MainWindow.ScanOutcome outcome = MainWindow.ReadFilesNow(new[] { folder }, CancellationToken.None, t => { lock (progress) progress.Add(t); });
            string lines = string.Join(" | ", outcome.Codes.Select(c => c.Source + ": " + c.Kind + (c.IsProblem ? "" : " " + c.Text)));
            Check(outcome.Codes.Where(c => !c.IsProblem).Select(c => c.Text).SequenceEqual(new[] { "first", "second" }), "the folder's codes: " + lines);
            Check(outcome.Codes.Select(c => c.Source).SequenceEqual(new[] { "a.png", "b.png", "c.png", "photo.heic", "spacer.gif" }), "a line for every picture, by name: " + lines);
            FoundCode empty = outcome.Codes.Single(c => c.Source == "c.png");
            Check(empty.IsProblem && empty.Kind == "No code found" && empty.HasThumbnail && !empty.HasText && !empty.IsCode, "the picture with no code: " + empty.Kind);
            FoundCode broken = outcome.Codes.Single(c => c.Source == "photo.heic");
            Check(broken.IsProblem && broken.Kind == "Couldn't open" && broken.Detail.Length > 0 && !broken.Detail.Contains("Exception"), "the broken picture: " + broken.Kind + " / " + broken.Detail);
            Check(outcome.Summary == "2 codes in 2 of 5 pictures in Photos; no code in 2; 1 couldn't be opened. Folders inside it aren't read.", "the summary: " + outcome.Summary);
            Check(progress.Count == 5, progress.Count + " progress lines for 5 pictures");

            MainWindow.ScanOutcome one = MainWindow.ReadFilesNow(new[] { Path.Combine(folder, "c.png") }, CancellationToken.None, t => { });
            Check(one.Codes.Count == 0 && one.Summary.StartsWith("No code in c.png"), "one picture with no code: " + one.Summary);
            string ticketPdf = Path.Combine(folder, "ticket.pdf");
            File.WriteAllBytes(ticketPdf, TestVectorQrPdf(new[] { "https://b0r3d.org/ticket" }, 300, 300));
            MainWindow.ScanOutcome pdf = MainWindow.ReadFilesNow(new[] { ticketPdf }, CancellationToken.None, t => { });
            Check(pdf.Codes.Count == 1 && pdf.Codes[0].Text == "https://b0r3d.org/ticket" && pdf.Summary == "1 code in ticket.pdf", "a PDF should read its code: " + pdf.Summary);

            using (var stopped = new CancellationTokenSource())
            {
                stopped.Cancel();
                try
                {
                    MainWindow.ReadFilesNow(new[] { folder }, stopped.Token, t => { });
                    Check(false, "a cancelled folder scan carried on");
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        static void ReadsCopiedAndDropped()
        {
            const int w = 300, h = 300;
            byte[] canvas = Canvas(w, h);
            PutQr(canvas, w, "copied", 20, 20, 8);
            BitmapSource picture = GrayPicture(canvas, w, h);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(picture));
            var bytes = new MemoryStream();
            png.Save(bytes);

            var asPng = new DataObject();
            asPng.SetData("PNG", new MemoryStream(bytes.ToArray()), false);
            Check(Pictures.CanRead(asPng), "a copied PNG isn't readable");
            Pictures.From(asPng, out string[] files, out BitmapSource fromPng);
            Check(files == null && fromPng != null && fromPng.PixelWidth == w, "a copied PNG didn't come through as a picture");

            var asBitmap = new DataObject();
            asBitmap.SetImage(picture);
            Pictures.From(asBitmap, out files, out BitmapSource fromBitmap);
            Check(fromBitmap != null, "a copied bitmap didn't come through");

            var asFiles = new DataObject();
            asFiles.SetFileDropList(new StringCollection { @"C:\some\picture.png", @"C:\some\folder" });
            Pictures.From(asFiles, out files, out BitmapSource none);
            Check(files != null && files.Length == 2 && none == null, "copied files didn't come through as files");

            var asText = new DataObject();
            asText.SetText("just words");
            Check(!Pictures.CanRead(asText), "plain text should go to the data box, not the scanner");

            // Ctrl+V reads a copied picture or files, and leaves anything with text to the text boxes
            var cells = new DataObject();  // what a spreadsheet copies: the text and a picture of it
            cells.SetText("a\tb");
            cells.SetImage(picture);
            Check(MainWindow.PasteReads(asPng) && MainWindow.PasteReads(asFiles) && MainWindow.PasteReads(asBitmap), "Ctrl+V should read a copied picture or files");
            Check(!MainWindow.PasteReads(asText) && !MainWindow.PasteReads(cells), "Ctrl+V with text copied should paste the text, not read a picture");

            MainWindow.ScanOutcome outcome = MainWindow.ReadPicturesNow("the copied picture", new[] { ("the copied picture", fromPng) }, null, CancellationToken.None);
            Check(outcome.Codes.Count == 1 && outcome.Codes[0].Text == "copied" && outcome.Summary == "1 code in the copied picture", "the copied picture: " + outcome.Summary);
        }

        // ---------- the screen ----------

        static void ScreensInRealPixels()
        {
            List<Screens.Screen> screens = Screens.All();
            if (screens.Count == 0) throw new SkipException("Windows reports no screens here");
            Check(screens.All(s => s.Bounds.Width > 0 && s.Bounds.Height > 0 && s.Scale >= 1), "a screen with no size or scaling");
            Check(screens.Select(s => s.Number).SequenceEqual(Enumerable.Range(1, screens.Count)), "the screens aren't numbered 1, 2, 3");
            for (int i = 1; i < screens.Count; i++) Check(screens[i].Bounds.X >= screens[i - 1].Bounds.X, "the screens aren't numbered left to right");
            Screens.Screen main = screens.FirstOrDefault(s => s.Main) ?? screens[0];
            BitmapSource part = Screens.Capture(new Int32Rect(main.Bounds.X, main.Bounds.Y, 64, 48));
            Check(part.PixelWidth == 64 && part.PixelHeight == 48 && part.IsFrozen, "a 64x48 picture of the screen came out " + part.PixelWidth + "x" + part.PixelHeight);
        }

        static void SnipBoxesInRealPixels()
        {
            // at 150 %, a 1000-wide window covers 1500 real pixels
            Int32Rect box = SnipWindow.ToPixels(new Rect(100, 50, 200, 100), new Size(1000, 600), 1500, 900);
            Check(box.X == 150 && box.Y == 75 && box.Width == 300 && box.Height == 150, "the box came out " + box);
            Int32Rect edge = SnipWindow.ToPixels(new Rect(900, 500, 300, 300), new Size(1000, 600), 1500, 900);
            Check(edge.X + edge.Width <= 1500 && edge.Y + edge.Height <= 900, "a box past the edge wasn't cut to the screen: " + edge);

            const int w = 600, h = 400;
            byte[] canvas = Canvas(w, h);
            PutQr(canvas, w, "boxed", 300, 60, 6);
            var screen = new Screens.Screen { Bounds = new Int32Rect(0, 0, w, h), Scale = 1, Main = true, Number = 1 };
            var snip = new SnipWindow(screen, GrayPicture(canvas, w, h), true);
            LayOut((FrameworkElement)snip.Content, w, h);
            var choices = new List<(SnipWindow.Choice, Int32Rect)>();
            snip.Chosen += (s, c, r) => choices.Add((c, r));
            snip.Drag(new Point(280, 40), new Point(560, 330));
            Check(choices.Count == 1 && choices[0].Item1 == SnipWindow.Choice.Box && choices[0].Item2 == new Int32Rect(280, 40, 280, 290), "a dragged box gave " + string.Join(", ", choices));
            snip.Drag(new Point(10, 10), new Point(12, 11));
            Check(choices.Count == 1, "the window chose twice");

            var click = new SnipWindow(screen, GrayPicture(canvas, w, h), false);
            LayOut((FrameworkElement)click.Content, w, h);
            SnipWindow.Choice clicked = SnipWindow.Choice.Cancel;
            click.Chosen += (s, c, r) => clicked = c;
            click.Drag(new Point(100, 100), new Point(102, 103));
            Check(clicked == SnipWindow.Choice.Screen, "a click should read the whole screen, not " + clicked);

            // what the box holds reads back
            var crop = new CroppedBitmap(GrayPicture(canvas, w, h), choices[0].Item2);
            crop.Freeze();
            MainWindow.ScanOutcome outcome = MainWindow.ReadPicturesNow("the box you drew", new[] { ("the box on the screen", (BitmapSource)crop) }, null, CancellationToken.None);
            Check(outcome.Codes.Count == 1 && outcome.Codes[0].Text == "boxed" && outcome.Summary == "1 code in the box you drew", "the box: " + outcome.Summary);
        }

        // The screens' overlays (built, never shown): closing one some other way (Alt+F4), or
        // switching to another program, closes them all and brings b0r3d QR back; a click on one
        // reads that screen.
        static void OverlaysAlwaysComeBack()
        {
            MainWindow window = NewMain();
            var left = new Screens.Screen { Bounds = new Int32Rect(0, 0, 300, 200), Scale = 1, Main = true, Number = 1 };
            var right = new Screens.Screen { Bounds = new Int32Rect(300, 0, 300, 200), Scale = 1, Main = false, Number = 2 };
            BitmapSource shot = GrayPicture(Canvas(300, 200), 300, 200);
            List<SnipWindow> Overlays() => new List<SnipWindow> { new SnipWindow(left, shot, true), new SnipWindow(right, shot, false) };

            List<SnipWindow> closed = Overlays();
            window.WatchSnips(closed);
            Check(window.Scanning, "waiting for a box should count as scanning (the Scan commands are off)");
            closed[1].Close();
            Pump(() => !window.Scanning, 3000);
            Check(!window.Scanning && closed.All(s => s.IsClosed), "closing one screen's overlay should close them all and bring b0r3d QR back");

            List<SnipWindow> left2 = Overlays();
            window.WatchSnips(left2);
            window.OnAwayDuringSnip(null, EventArgs.Empty);
            Pump(() => false, 100);
            Check(!window.Scanning && left2.All(s => s.IsClosed), "switching to another program should close the overlays and bring b0r3d QR back");

            List<SnipWindow> clicked = Overlays();
            window.WatchSnips(clicked);
            clicked[1].Drag(new Point(50, 50), new Point(52, 51));
            Check(clicked.All(s => s.IsClosed), "a click on one screen should close every overlay");
            Pump(() => !window.Scanning, 15000);
            Check(window.ScanStatus.Text.StartsWith("No code on screen 2", StringComparison.Ordinal), "a click on screen 2 read \"" + window.ScanStatus.Text + "\"");

            // Enter: every screen, worded as such
            byte[] withCode = Canvas(300, 200);
            PutQr(withCode, 300, "on screen 2", 60, 10, 4);
            MainWindow.ScanOutcome none = MainWindow.ReadPicturesNow("your screens", new[] { ("screen 1", shot), ("screen 2", shot) }, "Zoom in.", CancellationToken.None);
            MainWindow.ScanOutcome one = MainWindow.ReadPicturesNow("your screens", new[] { ("screen 1", shot), ("screen 2", GrayPicture(withCode, 300, 200)) }, "Zoom in.", CancellationToken.None);
            Check(none.Summary == "No code on your screens. Zoom in." && one.Summary == "1 code on your screens" && one.Codes[0].Source == "screen 2",
                "every screen: \"" + none.Summary + "\" and \"" + one.Summary + "\"");
        }

        // the scanned text exactly (a vCard's Windows line breaks too), and plain QR codes only
        static void MakeAgainKeepsTheText()
        {
            MainWindow window = NewMain();
            var vcard = new FoundCode { Text = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Ruckus\r\nEND:VCARD", Kind = "Contact", Format = "QR code", CanMakeAgain = true };
            vcard.Level = QrLevel.M;
            window.MakeAgain(vcard);
            Check(window.Code?.Text == vcard.Text, "the code made again holds \"" + (window.Code?.Text ?? "").Replace("\r", "\\r").Replace("\n", "\\n") + "\"");
            Check(window.LevelM.IsChecked == true, "the code's error correction (M) wasn't kept");
            Type(window, "typed\r\nhere");
            Check(window.Code?.Text == "typed\nhere", "typed text should still get plain line breaks, as on the website");

            const int w = 420, h = 420;
            byte[] canvas = Canvas(w, h);
            PutZxing(canvas, w, BarcodeFormat.QR_CODE, "0109506000134352", 60, 60, 8, hints: new Dictionary<EncodeHintType, object> { { EncodeHintType.GS1_FORMAT, true } });
            List<FoundCode> found = ReadCanvas(canvas, w, h);
            Check(found.Count == 1 && found[0].Format == "GS1 QR code" && !found[0].CanMakeAgain, "a GS1 QR code: " + string.Join(", ", found.Select(f => f.Format + (f.CanMakeAgain ? " (can be made again)" : ""))));
            Check(found[0].Text == "(01)09506000134352", "GS1 data reads in round brackets, not " + Show(found[0].Text));

            // GS1-128 keeps its fields apart (the reader is told to expect GS1), and is made again as GS1-128;
            // a GS1 Data Matrix too, without the reader's leading GS
            const int wide = 900, tall = 320;
            canvas = Canvas(wide, tall);
            // (ZXing's Code 128 writer takes ñ for FNC1; its Data Matrix one starts GS1 data with it itself)
            PutZxing(canvas, wide, BarcodeFormat.CODE_128, "ñ0109506000134352" + "10ABCñ21XYZ", 20, 100, 2, 120);
            found = ReadCanvas(canvas, wide, tall);
            Check(found.Count == 1 && found[0].Format == "GS1-128" && found[0].Text == "(01)09506000134352(10)ABC(21)XYZ" && found[0].Barcode == Barcodes.BarcodeType.Gs1_128,
                "GS1-128: " + string.Join(", ", found.Select(f => f.Format + " " + Show(f.Text) + " as " + f.Barcode)));
            canvas = Canvas(wide, tall);
            // (digits only: with letters after them ZXing.Net's writer ends the code in a way its reader takes for more text)
            PutZxing(canvas, wide, BarcodeFormat.DATA_MATRIX, "0109506000134352" + "1012345", 60, 30, 8, hints: new Dictionary<EncodeHintType, object> { { EncodeHintType.GS1_FORMAT, true }, { EncodeHintType.DATA_MATRIX_COMPACT, true } });
            found = ReadCanvas(canvas, wide, tall);
            Check(found.Count == 1 && found[0].Format == "GS1 DataMatrix" && found[0].Text == "(01)09506000134352(10)12345",
                "GS1 Data Matrix: " + string.Join(", ", found.Select(f => f.Format + " " + Show(f.Text) + " as " + f.Barcode)));
        }

        // ---------- what codes are ----------

        static void KindsInWords()
        {
            void Expect(string text, BarcodeFormat format, string kind, string detail)
            {
                Kinds.Describe(text, format, out string k, out string d);
                Check(k == kind && d == detail, "\"" + Short(text) + "\" is \"" + k + "\" / \"" + d + "\", not \"" + kind + "\" / \"" + detail + "\"");
            }
            Expect("https://www.b0r3d.org/x", BarcodeFormat.QR_CODE, "Link", "b0r3d.org");
            Expect("https://аpple.com/x", BarcodeFormat.QR_CODE, "Link", "аpple.com (xn--pple-43d.com)");  // a Cyrillic а
            Expect("otpauth-migration://offline?data=CjEKCkhlbGxvId6tvu8", BarcodeFormat.QR_CODE, "Sign-in codes (two-factor export)", "");
            Expect("WIFI:T:WPA;S:home;P:secret;;", BarcodeFormat.QR_CODE, "Wi-Fi network", "network home, WPA");
            Expect("WIFI:S:cafe;T:nopass;H:true;;", BarcodeFormat.QR_CODE, "Wi-Fi network", "network cafe, no password, hidden");
            Expect("WIFI:T:SAE;S:my\\;net;P:x;;", BarcodeFormat.QR_CODE, "Wi-Fi network", "network my;net, WPA3");
            Expect("BEGIN:VCARD\nVERSION:3.0\nN:Cat;Ruckus\nFN:Ruckus the Cat\nEND:VCARD", BarcodeFormat.QR_CODE, "Contact", "Ruckus the Cat");
            Expect("MECARD:N:Cat,Ruckus;TEL:123;;", BarcodeFormat.QR_CODE, "Contact", "Cat Ruckus");
            Expect("BEGIN:VEVENT\nSUMMARY:Party\nEND:VEVENT", BarcodeFormat.QR_CODE, "Calendar event", "Party");
            Expect("mailto:someone@example.com?subject=Hi", BarcodeFormat.QR_CODE, "Email", "someone@example.com");
            Expect("MATMSG:TO:a@example.com;SUB:Hi;;", BarcodeFormat.QR_CODE, "Email", "a@example.com");
            Expect("tel:+15555550123", BarcodeFormat.QR_CODE, "Phone number", "+15555550123");
            Expect("SMSTO:+15555550123:hello", BarcodeFormat.QR_CODE, "Text message", "+15555550123");
            Expect("geo:51.5,-0.12", BarcodeFormat.QR_CODE, "Location", "51.5,-0.12");
            Expect("BCD\n002\n1\nSCT\nBIC\nRuckus Ltd\nDE89370400440532013000\nEUR12.50", BarcodeFormat.QR_CODE, "Payment (EPC)", "to Ruckus Ltd, EUR 12.50");
            Expect("otpauth://totp/b0r3d:rukus?secret=JBSWY3DPEHPK3PXP&issuer=b0r3d", BarcodeFormat.QR_CODE, "Sign-in code (two-factor)", "b0r3d:rukus");
            Expect("rtsp://camera/stream", BarcodeFormat.QR_CODE, "Address (rtsp)", "");
            Expect("Just some words", BarcodeFormat.QR_CODE, "Text", "");
            Expect("9780306406157", BarcodeFormat.EAN_13, "Book number (ISBN)", "");
            Expect("15555550123", BarcodeFormat.UPC_A, "Product number", "");  // looks like a phone number, isn't one
            Check(Kinds.FormatName(BarcodeFormat.CODE_128, "x", "]C1") == "GS1-128" && Kinds.FormatName(BarcodeFormat.DATA_MATRIX, "x", "]d2") == "GS1 DataMatrix" &&
                Kinds.FormatName(BarcodeFormat.ITF, "15400141288763", null) == "ITF-14" && Kinds.FormatName(BarcodeFormat.QR_CODE, "x", "]Q2") == "QR code with an ECI marker",
                "a code type's name is wrong");
            Kinds.Describe("otpauth://totp/x?secret=TOPSECRET", BarcodeFormat.QR_CODE, out string kind2, out string detail2);
            Check(!detail2.Contains("TOPSECRET"), "a sign-in secret got into the detail line");
            Kinds.Describe("otpauth://totp?issuer=ACME/EU&secret=TOPSECRET", BarcodeFormat.QR_CODE, out string kind3, out string detail3);
            Check(kind3 == "Sign-in code (two-factor)" && !detail3.Contains("TOPSECRET"), "with no label, a sign-in secret got into the detail line: " + detail3);
        }

        static void LinksAskFirst()
        {
            Uri Link(string text) { Check(Kinds.WebLink(text, out Uri u), text + " isn't taken as a web link"); return u; }
            Check(Kinds.LinkWarnings(Link("https://b0r3d.org/qr")).Count == 0, "an ordinary https link shouldn't come with warnings");
            Check(Kinds.LinkWarnings(Link("http://example.com")).Any(w => w.Contains("isn't encrypted")), "http should be called unencrypted");
            Check(Kinds.LinkWarnings(Link("https://192.0.2.7/login")).Any(w => w.Contains("numbered address")), "an IP address should be pointed out");
            Check(Kinds.LinkWarnings(Link("https://xn--80ak6aa92e.com/")).Any(w => w.Contains("other alphabets")), "a look-alike address should be pointed out");
            Check(Kinds.LinkWarnings(Link("https://аpple.com/")).Any(w => w.Contains("other alphabets") && w.Contains("xn--pple-43d.com")),
                "a look-alike written in Cyrillic letters (as .NET keeps it) should be pointed out with its real name");
            Check(Kinds.LinkWarnings(Link("https://bank.example@evil.example/")).Any(w => w.Contains("evil.example")), "a user-name trick should say where it really goes");
            foreach (string notWeb in new[] { "javascript:alert(1)", "file:///C:/Windows/notepad.exe", "ftp://example.com/x", "mailto:a@example.com", "just text" })
                Check(!Kinds.WebLink(notWeb, out Uri ignored), notWeb + " shouldn't be offered as a link to open");
            string question = MainWindow.LinkQuestion(Link("http://example.com/x"));
            Check(question.Contains("http://example.com/x") && question.Contains("isn't encrypted"), "the question: " + question);
        }

        // ---------- the Scan tab ----------

        static void ScanTabShowsAndMakesAgain()
        {
            MainWindow window = NewMain();
            window.Tabs.SelectedItem = window.ScanTab;
            LayOut(window.Root, MainWidth, MainHeight);
            Check(window.FoundHint.Visibility == Visibility.Visible, "the empty list should say what goes there");

            const int w = 700, h = 360;
            byte[] canvas = Canvas(w, h);
            PutQr(canvas, w, "https://b0r3d.org/again", 20, 20, 6, QrLevel.H);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(GrayPicture(canvas, w, h)));
            var stream = new MemoryStream();
            png.Save(stream);
            var dropped = new DataObject();
            dropped.SetData("PNG", new MemoryStream(stream.ToArray()), false);
            window.ReadData(dropped, "the dropped picture", "nothing");
            Pump(() => !window.Scanning, 15000);
            Check(!window.Scanning, "the scan didn't finish");
            Check(window.Found.Count == 1 && window.ScanStatus.Text == "1 code in the dropped picture", "after a drop: " + window.Found.Count + " codes, \"" + window.ScanStatus.Text + "\"");
            LayOut(window.Root, MainWidth, MainHeight);
            Check(window.FoundList.Items.Count == 1 && window.FoundHint.Visibility == Visibility.Collapsed, "the list doesn't show the code");

            // what a screen reader hears: the card by what it is, its buttons with it, never a type name
            var names = new List<string>();
            void Hear(AutomationPeer peer, int depth)
            {
                foreach (AutomationPeer child in peer.GetChildren() ?? new List<AutomationPeer>())
                {
                    names.Add(child.GetName() ?? "");
                    if (depth < 12) Hear(child, depth + 1);
                }
            }
            AutomationPeer list = UIElementAutomationPeer.CreatePeerForElement(window.FoundScroll);
            Check(list != null, "the list of codes has no automation peer at all");
            Hear(list, 0);
            string heard = string.Join(" | ", names.Where(n => n.Length > 0));
            Console.WriteLine("      (a screen reader hears: " + heard + ")");
            Check(names.Any(n => n.StartsWith("Link", StringComparison.Ordinal)), "the card isn't called by what it is: " + heard);
            Check(names.Contains("Copy text (Link, b0r3d.org)") && names.Contains("Make this code again (Link, b0r3d.org)"), "the card's buttons don't say which code: " + heard);
            Check(!names.Any(n => n.Contains("B0r3dQR.")), "a screen reader would hear a type name: " + heard);
            Check(AutomationProperties.GetLiveSetting(window.ScanStatus) == AutomationLiveSetting.Polite, "the line over the codes isn't read out when it changes");

            window.MakeAgain(window.Found[0]);
            Check(window.Tabs.SelectedItem == window.MakeTab, "Make this code again should go to the Make tab");
            Check(window.DataBox.Text == "https://b0r3d.org/again" && window.Code?.Text == "https://b0r3d.org/again", "the Make tab has \"" + window.DataBox.Text + "\"");
            Check(window.LevelH.IsChecked == true, "the code's error correction (H) wasn't kept");

            DataObject copied = Exports.TextData("WIFI:S:x;P:secret;;");
            Check((string)copied.GetData(DataFormats.UnicodeText) == "WIFI:S:x;P:secret;;" && OffTheCloud(copied), "Copy text should keep the text out of the cloud clipboard");
            // Ctrl+C in a text box: WPF hands its copy round as this event before the clipboard gets it
            var typed = new DataObject(DataFormats.UnicodeText, "WIFI:S:x;P:secret;;");
            window.DataBox.RaiseEvent(new DataObjectCopyingEventArgs(typed, false));
            Check(OffTheCloud(typed), "Ctrl+C in a text box would let the text go to the cloud clipboard");
            // ...in the other windows' text boxes too: a template's fields (an IBAN, a contact's details), About's
            var fromForm = new DataObject(DataFormats.UnicodeText, "DE89370400440532013000");
            window.TemplateFor(Design.TemplateKind.Epc).Input("iban").RaiseEvent(new DataObjectCopyingEventArgs(fromForm, false));
            Check(OffTheCloud(fromForm), "Ctrl+C in a template's field would let the text go to the cloud clipboard");
            var fromAbout = new DataObject(DataFormats.UnicodeText, "MIT License");
            new AboutWindow().Licences.RaiseEvent(new DataObjectCopyingEventArgs(fromAbout, false));
            Check(OffTheCloud(fromAbout), "Ctrl+C in About's licences would let the text go to the cloud clipboard");
            var dragged = new DataObject(DataFormats.UnicodeText, "dragged");
            window.DataBox.RaiseEvent(new DataObjectCopyingEventArgs(dragged, true));
            Check(!OffTheCloud(dragged), "text dragged out of a text box never reaches the clipboard, so it isn't marked");

            window.ReadData(new DataObject(), "the copied picture", "Nothing to read on the clipboard.");
            Check(window.ScanStatus.Text == "Nothing to read on the clipboard." && !window.Scanning, "an empty clipboard: \"" + window.ScanStatus.Text + "\"");
        }

        // a multi-page PDF with vector QR codes for testing the PDF scanner
        static byte[] TestVectorQrPdf(IList<string> pageTexts, double pageWidthPt, double pageHeightPt, int rotate = 0)
        {
            var pdf = new MemoryStream();
            var offsets = new List<long>();
            void WriteAscii(string s) { byte[] b = System.Text.Encoding.ASCII.GetBytes(s); pdf.Write(b, 0, b.Length); }
            void StartObj(int id) { offsets.Add(pdf.Position); WriteAscii(id + " 0 obj\n"); }

            WriteAscii("%PDF-1.4\n");
            StartObj(1); WriteAscii("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            var kids = string.Join(" ", Enumerable.Range(0, pageTexts.Count).Select(i => (3 + i * 2) + " 0 R"));
            StartObj(2); WriteAscii("<< /Type /Pages /Kids [" + kids + "] /Count " + pageTexts.Count + " >>\nendobj\n");

            string rotPart = rotate != 0 ? " /Rotate " + rotate : "";
            for (int i = 0; i < pageTexts.Count; i++)
            {
                int pageObj = 3 + i * 2, contentObj = pageObj + 1;
                byte[] streamBytes = TestVectorQrStream(pageTexts[i], pageWidthPt, pageHeightPt);
                StartObj(pageObj);
                WriteAscii(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.###} {1:0.###}]{2} /Contents {3} 0 R >>\nendobj\n",
                    pageWidthPt, pageHeightPt, rotPart, contentObj));
                StartObj(contentObj);
                WriteAscii("<< /Length " + streamBytes.Length + " >>\nstream\n");
                pdf.Write(streamBytes, 0, streamBytes.Length);
                WriteAscii("endstream\nendobj\n");
            }
            long xref = pdf.Position;
            WriteAscii("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
            foreach (long o in offsets) WriteAscii(o.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n");
            WriteAscii("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            return pdf.ToArray();
        }

        static byte[] TestVectorQrStream(string text, double pageWidthPt, double pageHeightPt)
        {
            if (text == null)   // a blank page
                return System.Text.Encoding.ASCII.GetBytes(string.Format(System.Globalization.CultureInfo.InvariantCulture, "1 1 1 rg 0 0 {0:0.##} {1:0.##} re f\n", pageWidthPt, pageHeightPt));
            QrCode code = QrCode.Make(text, QrLevel.M);
            int quiet = 4, totalModules = code.Size + quiet * 2;
            double boxSize = Math.Min(pageWidthPt, pageHeightPt) * 0.5, cell = boxSize / totalModules;
            double originX = (pageWidthPt - boxSize) / 2.0, originY = (pageHeightPt - boxSize) / 2.0;
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "1 1 1 rg 0 0 {0:0.##} {1:0.##} re f\n0 0 0 rg\n", pageWidthPt, pageHeightPt);
            for (int r = 0; r < code.Size; r++)
                for (int c = 0; c < code.Size; c++)
                    if (code[r, c])
                    {
                        double px = originX + (c + quiet) * cell, py = originY + (totalModules - 1 - (r + quiet)) * cell;
                        sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} {2:0.###} re\n", px, py, cell);
                    }
            sb.Append("f\n");
            return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
        }

        static byte[] TestEncryptedQrPdf(string qrText, string userPassword)
        {
            byte[] pad = {
                0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
                0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
            };
            byte[] raw = System.Text.Encoding.ASCII.GetBytes(userPassword);
            byte[] padded = new byte[32];
            Array.Copy(raw, 0, padded, 0, Math.Min(raw.Length, 32));
            if (raw.Length < 32) Array.Copy(pad, 0, padded, raw.Length, 32 - raw.Length);
            byte[] Rc4(byte[] key, byte[] data)
            {
                byte[] s = new byte[256];
                for (int i = 0; i < 256; i++) s[i] = (byte)i;
                for (int i = 0, j = 0; i < 256; i++) { j = (j + s[i] + key[i % key.Length]) & 255; byte t = s[i]; s[i] = s[j]; s[j] = t; }
                byte[] res = new byte[data.Length];
                for (int k = 0, x = 0, y = 0; k < data.Length; k++) { x = (x + 1) & 255; y = (y + s[x]) & 255; byte t = s[x]; s[x] = s[y]; s[y] = t; res[k] = (byte)(data[k] ^ s[(s[x] + s[y]) & 255]); }
                return res;
            }
            string Hex(byte[] bytes) => "<" + string.Concat(bytes.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))) + ">";
            byte[] fileId = System.Text.Encoding.ASCII.GetBytes("0123456789ABCDEF");
            int permissions = -4;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] ownerKey = md5.ComputeHash(padded).Take(5).ToArray();
                byte[] O = Rc4(ownerKey, padded);
                var keyInput = new MemoryStream();
                keyInput.Write(padded, 0, 32);
                keyInput.Write(O, 0, 32);
                keyInput.Write(BitConverter.GetBytes(permissions), 0, 4);
                keyInput.Write(fileId, 0, 16);
                byte[] encKey = md5.ComputeHash(keyInput.ToArray()).Take(5).ToArray();
                byte[] U = Rc4(encKey, pad);
                byte[] objKeyInput = new byte[10];
                Array.Copy(encKey, 0, objKeyInput, 0, 5);
                objKeyInput[5] = 4;
                byte[] objKey = md5.ComputeHash(objKeyInput).Take(10).ToArray();
                byte[] encStream = Rc4(objKey, TestVectorQrStream(qrText, 200, 200));

                var pdf = new MemoryStream();
                var offsets = new List<long>();
                void WriteAscii(string s) { byte[] b = System.Text.Encoding.ASCII.GetBytes(s); pdf.Write(b, 0, b.Length); }
                void StartObj(int id) { offsets.Add(pdf.Position); WriteAscii(id + " 0 obj\n"); }
                WriteAscii("%PDF-1.4\n");
                StartObj(1); WriteAscii("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                StartObj(2); WriteAscii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                StartObj(3); WriteAscii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>\nendobj\n");
                StartObj(4); WriteAscii("<< /Length " + encStream.Length + " >>\nstream\n");
                pdf.Write(encStream, 0, encStream.Length);
                WriteAscii("\nendstream\nendobj\n");
                StartObj(5); WriteAscii("<< /Filter /Standard /V 1 /R 2 /Length 40 /P " + permissions + " /O " + Hex(O) + " /U " + Hex(U) + " >>\nendobj\n");
                long xref = pdf.Position;
                WriteAscii("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
                foreach (long o in offsets) WriteAscii(o.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n");
                WriteAscii("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R /Encrypt 5 0 R /ID [" + Hex(fileId) + " " + Hex(fileId) + "] >>\nstartxref\n" + xref + "\n%%EOF\n");
                return pdf.ToArray();
            }
        }
    }
}
