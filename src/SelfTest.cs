using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using B0r3dQR.Barcodes;
using B0r3dQR.Qr;

namespace B0r3dQR
{
    // b0r3dQR.exe --selftest <file>: no window shown. Makes a code, draws it the way a PNG is saved,
    // reads it back (as the scan check and as the Scan tab), checks the SVG, builds the windows
    // (the Scan tab with a card, and a screen's overlay too), draws a designed code (a preset's logo,
    // a frame), makes and reads back a barcode of each kind (the Make tab with one too), a small batch
    // in a zip and a printed page, fills a data template and builds the Recent codes window, and writes
    // what it found to <file> as key=value lines; the exit code is 0 when it all worked. The tests run it on the finished
    // one-file exe, on its own in a folder, to prove it works with nothing beside it.
    static class SelfTest
    {
        public const string Text = "b0r3d QR self-test: héllo ✓ https://b0r3d.org";

        public static int Run(string reportPath)
        {
            var lines = new List<string>();
            bool ok = true;
            try
            {
                lines.Add("version=" + Program.FullVersion);
                lines.Add("dpi=" + Native.DpiAwareness());
                lines.Add("zxing=" + typeof(ZXing.BarcodeReaderGeneric).Assembly.GetName().Name);

                QrCode code = QrCode.Make(Text, QrLevel.Q);
                QrShapes shapes = QrShapes.For(code, ScanCheck.StandardQuietZone);
                var picture = QrPicture.Render(shapes, ScanCheck.PixelsPerModule);
                byte[] grays = ScanCheck.Grays(picture, out int width, out int height);
                ScanCheck.Reading reading = ScanCheck.Read(grays, width, height);
                bool readBack = reading != null && reading.Text == Text;
                lines.Add("read=" + (readBack ? "ok" : "failed"));
                ok &= readBack;

                byte[] png = QrPicture.Png(picture);
                bool pngOk = png.Length > 8 && png[0] == 0x89 && png[1] == (byte)'P';
                lines.Add("png=" + (pngOk ? "ok" : "failed"));
                ok &= pngOk;

                // the Scan tab's reader finds it too, with what it is
                List<Scan.FoundCode> scanned = Scan.Reader.ReadAll(grays, width, height, "the self-test", System.Threading.CancellationToken.None);
                bool scanOk = scanned.Count == 1 && scanned[0].Text == Text && scanned[0].Format == "QR code" && scanned[0].HasThumbnail;
                lines.Add("scan=" + (scanOk ? "ok" : "failed"));
                ok &= scanOk;

                string svg = QrPicture.Svg(shapes, width);
                bool svgOk = svg.Contains("viewBox=\"0 0 " + shapes.Width + " " + shapes.Width + "\"") && svg.Contains(shapes.ToSvgPath());
                lines.Add("svg=" + (svgOk ? "ok" : "failed"));
                ok &= svgOk;

                // the windows, built from inside this exe and laid out (never shown), in both looks:
                // their pages, styles and pictures all load after the merge
                var app = new App();
                app.InitializeComponent();
                foreach (Look look in new[] { Look.Light, Look.Dark })
                {
                    Theme.Set(look);
                    var window = new MainWindow(new Settings());
                    window.DataBox.Text = Text;
                    window.Refresh();
                    window.Root.Measure(new Size(1000, 640));
                    window.Root.Arrange(new Rect(0, 0, 1000, 640));
                    window.Root.UpdateLayout();  // the preview is drawn once the stage has a size
                    var about = new AboutWindow();
                    about.Root.Measure(new Size(640, double.PositiveInfinity));
                    about.Root.Arrange(new Rect(about.Root.DesiredSize));
                    about.Root.UpdateLayout();
                    if (!window.HasCode || window.Preview.Source == null) throw new InvalidOperationException("the main window made no code in the " + look + " look");

                    // the Scan tab with a card for the code read above, and a screen's overlay
                    window.Tabs.SelectedItem = window.ScanTab;
                    if (scanned.Count > 0) window.Found.Add(scanned[0]);
                    window.Root.Measure(new Size(1000, 640));
                    window.Root.Arrange(new Rect(0, 0, 1000, 640));
                    window.Root.UpdateLayout();
                    if (scanned.Count > 0 && window.FoundList.ItemContainerGenerator.ContainerFromIndex(0) == null) throw new InvalidOperationException("the Scan tab showed no card in the " + look + " look");
                    var snip = new SnipWindow(new Screens.Screen { Bounds = new Int32Rect(0, 0, 400, 300), Scale = 1, Main = true, Number = 1 }, picture, true);
                    var overlay = (FrameworkElement)snip.Content;
                    overlay.Measure(new Size(400, 300));
                    overlay.Arrange(new Rect(0, 0, 400, 300));
                    overlay.UpdateLayout();
                    snip.Close();
                }
                lines.Add("windows=ok");

                // the designer inside this exe: the b0r3d Cup preset (its logo is one of the exe's
                // resources) in a frame, drawn and read back
                var cup = Design.Presets.Apply("b0r3d Cup", new Design.QrDesign { Data = Text });
                cup.Frame = Design.Presets.ApplyFrame("Default Frame", null);
                var art = new QrArt(cup);
                ReadBack(art, out string designed);
                bool designerOk = art.Cleared > 0 && art.LogoProblem == null && designed == Text && art.ToSvg().Contains("class=\"qr-logo\"");
                lines.Add("designer=" + (designerOk ? "ok" : "failed"));
                ok &= designerOk;

                // the other barcodes inside this exe: one made by each kind of encoder (bars, Data Matrix,
                // Aztec, PDF417), drawn as the scan check draws them and read back with their own reader;
                // and the Make tab with a barcode in it
                bool barcodesOk = true;
                foreach (BarcodeType type in new[] { BarcodeType.Ean13, BarcodeType.DataMatrix, BarcodeType.Aztec, BarcodeType.Pdf417 })
                {
                    string data = type == BarcodeType.Ean13 ? "400638133393" : Text;
                    BarcodeFormat format = BarcodeFormats.Get(type);
                    BarcodeSvg made = Barcode.Make(data, type, new BarcodeOptions(), out _);
                    if (made == null)
                    {
                        barcodesOk = false;
                        continue;
                    }
                    byte[] bars = ScanCheck.Grays(BarcodeExport.CheckPicture(new BarcodeArt(made), new BarcodeLook()), out int bw, out int bh);
                    barcodesOk &= BarcodeExport.Read(bars, bw, bh, format) == format.ExpectedText(format.Prepare(data));
                }
                var making = new MainWindow(new Settings());
                making.ChooseCodeType(BarcodeType.Aztec, Text);
                making.Root.Measure(new Size(1000, 640));
                making.Root.Arrange(new Rect(0, 0, 1000, 640));
                making.Root.UpdateLayout();
                barcodesOk &= making.BarcodeShown != null && making.Preview.Source != null;
                lines.Add("barcodes=" + (barcodesOk ? "ok" : "failed"));
                ok &= barcodesOk;

                // batch export inside this exe: a two-row CSV made into PNG files in a zip (held in memory), each read
                // back out of it and scanned
                List<Batch.BatchRow> rows = Batch.BatchRows.ReadQr("url,frameText\nb0r3d QR self-test row,Scan me\nsecond row,\n", out bool framed);
                var batch = new Batch.BatchKind { Design = new Design.QrDesign(), Frame = framed, Format = Batch.BatchFormat.Png };
                List<Batch.BatchItem> items = Batch.BatchExport.Plan(rows, batch);
                bool batchOk = items.Count == 2 && items.TrueForAll(i => i.Problem == null);
                using (var memory = new MemoryStream())
                {
                    using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
                        foreach (Batch.BatchItem item in items)
                        {
                            byte[] made = Batch.BatchExport.Make(item, batch, out _);
                            using (Stream entry = zip.CreateEntry(item.Name + ".png").Open()) entry.Write(made, 0, made.Length);
                        }
                    memory.Position = 0;
                    using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Read))
                        foreach (Batch.BatchItem item in items)
                        {
                            var copy = new MemoryStream();
                            using (Stream entry = zip.GetEntry(item.Name + ".png").Open()) entry.CopyTo(copy);
                            byte[] back = ScanCheck.Grays(Batch.BatchExport.Picture(copy.ToArray(), item, batch), out int bw2, out int bh2);
                            batchOk &= Batch.BatchExport.Read(back, bw2, bh2, batch) == item.Expected;
                        }
                }
                lines.Add("batch=" + (batchOk ? "ok" : "failed"));
                ok &= batchOk;

                // printing inside this exe: a page with the code on it at its size, drawn as a printer would, read back
                var printed = new QrArt(new Design.QrDesign { Data = Text });
                var sheet = Printing.PrintSheet.Pages(new List<Printing.PrintCode> { new Printing.PrintCode { Drawing = printed.ToDrawing(), Canvas = new Rect(0, 0, printed.W, printed.H), Size = Printing.PrintSheet.QrSize(printed, new PrintSize(), false) } },
                                                      new Printing.PrintOptions(), out _);
                var page = (System.Windows.Documents.FixedPage)sheet.Pages[0].Child;
                page.Measure(Printing.PrintSheet.A4);
                page.Arrange(new Rect(Printing.PrintSheet.A4));
                page.UpdateLayout();
                var paper = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Printing.PrintSheet.A4.Width, (int)Printing.PrintSheet.A4.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                paper.Render(page);
                byte[] pageGrays = ScanCheck.Grays(paper, out int pw, out int ph);
                bool printOk = ScanCheck.Read(pageGrays, pw, ph)?.Text == Text;

                // the Batch export and Print windows, built from inside this exe (their pages and styles load after
                // the merge) and laid out
                var batchWindow = new BatchWindow(batch, "QR codes");
                batchWindow.LoadCsvText("self-test.csv", "url,frameText\nb0r3d QR self-test row,Scan me\n");
                batchWindow.Root.Measure(new Size(660, 760));
                batchWindow.Root.Arrange(new Rect(0, 0, 660, 760));
                batchWindow.Root.UpdateLayout();
                batchWindow.Close();
                var printWindow = new PrintWindow(new Printing.PrintCode { Drawing = printed.ToDrawing(), Canvas = new Rect(0, 0, printed.W, printed.H), Size = new Size(200, 200) }, "self-test", "mm")
                {
                    PaperOverride = Printing.PrintSheet.A4,
                };
                printWindow.Root.Measure(new Size(760, 720));
                printWindow.Root.Arrange(new Rect(0, 0, 760, 720));
                printWindow.Lay();
                printWindow.Root.UpdateLayout();
                printOk &= printWindow.Document?.Pages.Count == 1 && printWindow.PagePicture.Source != null;
                printWindow.Close();
                lines.Add("print=" + (printOk ? "ok" : "failed"));
                ok &= printOk;

                // the data templates' window (a Wi-Fi text read into its form and written again) and
                // the Recent codes window, built from inside this exe
                var forms = new Dictionary<Design.TemplateKind, Dictionary<string, string>>();
                var template = new TemplateWindow(Design.TemplateKind.Wifi, "WIFI:T:WPA;S:self-test;P:pw;;", forms);
                template.Root.Measure(new Size(600, 680));
                template.Root.Arrange(new Rect(0, 0, 600, 680));
                template.Root.UpdateLayout();
                bool templatesOk = template.Save() && template.Result == "WIFI:T:WPA;S:self-test;P:pw;;";
                var recent = new RecentWindow(new Settings(), id => false);
                recent.Root.Measure(new Size(600, 660));
                recent.Root.Arrange(new Rect(0, 0, 600, 660));
                recent.Root.UpdateLayout();
                recent.Close();
                lines.Add("templates=" + (templatesOk ? "ok" : "failed"));
                ok &= templatesOk;
            }
            catch (Exception e)
            {
                ok = false;
                lines.Add("error=" + e.GetType().Name + ": " + e.Message.Replace('\r', ' ').Replace('\n', ' '));
            }
            lines.Add("result=" + (ok ? "ok" : "failed"));
            return Report(reportPath, lines, ok);
        }

        // a design drawn at twice its size on white, read back
        static void ReadBack(QrArt art, out string text)
        {
            var picture = art.Render(2, (int)(art.W * 2), (int)(art.H * 2), System.Windows.Media.Colors.White);
            byte[] grays = ScanCheck.Grays(picture, out int w, out int h);
            text = ScanCheck.Read(grays, w, h)?.Text;
        }

        static int Report(string reportPath, List<string> lines, bool ok)
        {
            try
            {
                File.WriteAllLines(reportPath, lines);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
            {
                return 3;
            }
            return ok ? 0 : 1;
        }
    }
}
