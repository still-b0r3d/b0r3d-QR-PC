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
            MainWindow.ScanOutcome pdf = MainWindow.ReadFilesNow(new[] { Path.Combine(folder, "ticket.pdf") }, CancellationToken.None, t => { });
            Check(pdf.Summary.Contains("Scan the screen"), "a PDF should point to scanning the screen: " + pdf.Summary);

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
            var vcard = new FoundCode { Text = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Rukus\r\nEND:VCARD", Kind = "Contact", Format = "QR code", CanMakeAgain = true };
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
            Expect("BEGIN:VCARD\nVERSION:3.0\nN:Cat;Rukus\nFN:Rukus the Cat\nEND:VCARD", BarcodeFormat.QR_CODE, "Contact", "Rukus the Cat");
            Expect("MECARD:N:Cat,Rukus;TEL:123;;", BarcodeFormat.QR_CODE, "Contact", "Cat Rukus");
            Expect("BEGIN:VEVENT\nSUMMARY:Party\nEND:VEVENT", BarcodeFormat.QR_CODE, "Calendar event", "Party");
            Expect("mailto:someone@example.com?subject=Hi", BarcodeFormat.QR_CODE, "Email", "someone@example.com");
            Expect("MATMSG:TO:a@example.com;SUB:Hi;;", BarcodeFormat.QR_CODE, "Email", "a@example.com");
            Expect("tel:+15555550123", BarcodeFormat.QR_CODE, "Phone number", "+15555550123");
            Expect("SMSTO:+15555550123:hello", BarcodeFormat.QR_CODE, "Text message", "+15555550123");
            Expect("geo:51.5,-0.12", BarcodeFormat.QR_CODE, "Location", "51.5,-0.12");
            Expect("BCD\n002\n1\nSCT\nBIC\nRukus Ltd\nDE89370400440532013000\nEUR12.50", BarcodeFormat.QR_CODE, "Payment (EPC)", "to Rukus Ltd, EUR 12.50");
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

            window.ReadData(new DataObject(), "the copied picture", "Nothing to read on the clipboard.");
            Check(window.ScanStatus.Text == "Nothing to read on the clipboard." && !window.Scanning, "an empty clipboard: \"" + window.ScanStatus.Text + "\"");
        }
    }
}
