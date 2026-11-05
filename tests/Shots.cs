using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR.Tests
{
    // b0r3dQRTests.exe --shots <folder>: draws the main window (empty, with a code, and the Scan
    // tab with codes read from a picture), About, and the screen-scanning overlay, in light and
    // dark, at 100 % and 150 %, into PNG files, straight from the controls. Nothing shows on
    // screen and nothing takes focus; menus that pop open aren't in them. The README's pictures
    // (docs\images) are some of these, under the same names.
    static partial class TestMain
    {
        // the settings folder About shows in the pictures (they go in the README): never a real one
        const string ShownDataFolder = @"D:\Tools\b0r3d QR\b0r3dQR data";

        // a cup drawn in SVG (steam, a gradient, a handle and text), as a logo pasted from a design program
        const string ShotSvgLogo = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\"><defs><linearGradient id=\"mug\" x2=\"0\" y2=\"1\">" +
            "<stop offset=\"0\" stop-color=\"#d040d0\"/><stop offset=\"1\" stop-color=\"#700070\"/></linearGradient></defs>" +
            "<path d=\"M22 5c-3 4 3 6 0 10M32 3c-3 4 3 6 0 10M42 5c-3 4 3 6 0 10\" fill=\"none\" stroke=\"#9a9a9a\" stroke-width=\"2.5\" stroke-linecap=\"round\"/>" +
            "<path d=\"M48 26h4a7 7 0 0 1 0 14h-4\" fill=\"none\" stroke=\"#700070\" stroke-width=\"4\"/>" +
            "<path d=\"M10 19h38v23a12 12 0 0 1-12 12H22a12 12 0 0 1-12-12z\" fill=\"url(#mug)\"/>" +
            "<text x=\"29\" y=\"40\" font-family=\"Consolas, monospace\" font-size=\"10\" font-weight=\"bold\" fill=\"#fff\" text-anchor=\"middle\">b0r3d</text></svg>";

        static int TakeShots(string folder)
        {
            Directory.CreateDirectory(folder);
            foreach (Look look in new[] { Look.Light, Look.Dark })
            {
                Theme.Set(look);
                string name = look.ToString().ToLowerInvariant();
                foreach (double scale in new[] { 1.0, 1.5 })
                {
                    MainWindow window = NewMain(scale);
                    Type(window, "https://b0r3d.org");
                    WaitForCheck(window);
                    Pump(() => window.SavePngButton.IsEnabled, 2000);  // the buttons catch up when WPF is idle
                    LayOut(window.Root, MainWidth, MainHeight);
                    Shoot(window.Root, MainWidth, MainHeight, scale, Path.Combine(folder, "main-" + name + "-" + (int)(scale * 100) + ".png"));
                }
                MainWindow empty = NewMain();
                Shoot(empty.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "empty-" + name + ".png"));

                // the designer: the b0r3d Cup preset in a Default Frame, with the Logo, Frame and Size
                // sections open instead of Data and Style
                MainWindow designer = NewMain();
                Type(designer, "https://b0r3d.org");
                designer.PresetBox.SelectedItem = "b0r3d Cup";
                designer.FrameCheck.IsChecked = true;
                Pump(() => false, 50);
                WaitForCheck(designer);
                LayOut(designer.Root, MainWidth, MainHeight);
                Shoot(designer.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "design-" + name + ".png"));
                designer.DataSection.IsExpanded = false;
                designer.StyleSection.IsExpanded = false;
                designer.LogoSection.IsExpanded = true;
                designer.FrameSection.IsExpanded = true;
                LayOut(designer.Root, MainWidth, MainHeight);
                Pump(() => false, 50);
                LayOut(designer.Root, MainWidth, MainHeight);
                Shoot(designer.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "sections-" + name + ".png"));
                designer.LogoSection.IsExpanded = false;
                designer.FrameSection.IsExpanded = false;
                designer.PrintCheck.IsChecked = true;
                designer.InCodeSection.IsExpanded = true;
                LayOut(designer.Root, MainWidth, MainHeight);
                Pump(() => false, 50);
                LayOut(designer.Root, MainWidth, MainHeight);
                Shoot(designer.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "size-" + name + ".png"));

                // the b0r3d Cup preset's look with an SVG drawing pasted as its logo, the Logo section open
                MainWindow svgLogo = NewMain();
                Type(svgLogo, "https://b0r3d.org");
                svgLogo.PresetBox.SelectedItem = "b0r3d Cup";
                Pump(() => false, 50);
                svgLogo.LogoFromData(new DataObject(DataFormats.UnicodeText, ShotSvgLogo));
                WaitForCheck(svgLogo);
                svgLogo.DataSection.IsExpanded = false;
                svgLogo.StyleSection.IsExpanded = false;
                svgLogo.LogoSection.IsExpanded = true;
                LayOut(svgLogo.Root, MainWidth, MainHeight);
                Pump(() => false, 50);
                LayOut(svgLogo.Root, MainWidth, MainHeight);
                Shoot(svgLogo.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "svg-logo-" + name + ".png"));
                // a frame's caption in two of the website's own fonts, built into b0r3d QR, with the Frame section open
                foreach (var (font, caption) in new[] { ("Pacifico", "Scan me for the menu"), ("Bebas Neue", "Scan for more info") })
                {
                    MainWindow captioned = NewMain();
                    Type(captioned, "https://b0r3d.org");
                    captioned.FrameCheck.IsChecked = true;
                    captioned.CaptionBox.Text = caption;
                    captioned.FontBox.SelectedItem = captioned.FontBox.Items.Cast<object>().First(item => item.ToString() == font);
                    captioned.CatchUp();
                    WaitForCheck(captioned);
                    captioned.DataSection.IsExpanded = false;
                    captioned.StyleSection.IsExpanded = false;
                    captioned.FrameSection.IsExpanded = true;
                    LayOut(captioned.Root, MainWidth, MainHeight);
                    Pump(() => false, 50);
                    LayOut(captioned.Root, MainWidth, MainHeight);
                    Shoot(captioned.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "caption-" + font.ToLowerInvariant().Replace(' ', '-') + "-" + name + ".png"));
                }

                // barcodes: an EAN-13 with its check digit added and what's in it, and a Data Matrix
                MainWindow barcodes = NewMain();
                barcodes.ChooseCodeType(Barcodes.BarcodeType.Ean13, "400638133393");
                WaitForCheck(barcodes);
                barcodes.InCodeSection.IsExpanded = true;
                Pump(() => barcodes.SavePngButton.IsEnabled, 2000);
                LayOut(barcodes.Root, MainWidth, MainHeight);
                Pump(() => false, 50);
                LayOut(barcodes.Root, MainWidth, MainHeight);
                Shoot(barcodes.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "barcode-ean13-" + name + ".png"));
                barcodes.ChooseCodeType(Barcodes.BarcodeType.DataMatrix, "https://b0r3d.org");
                WaitForCheck(barcodes);
                LayOut(barcodes.Root, MainWidth, MainHeight);
                Pump(() => false, 50);
                LayOut(barcodes.Root, MainWidth, MainHeight);
                Shoot(barcodes.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "barcode-datamatrix-" + name + ".png"));

                // batch export: a CSV with captions and a row too long, its second row showing; and the print window
                // with a sheet of copies
                MainWindow batchMain = NewMain();
                Type(batchMain, "https://b0r3d.org");
                BatchWindow batchShot = NewBatch(batchMain, "url,frameText,fileName\nhttps://b0r3d.org,Visit b0r3d.org,b0r3d_home\nhttps://qr.b0r3d.org,Make your own,qr_maker\n" +
                                                        new string('x', 3000) + ",Too long,too_long\nPlain text works too,,plain_text\n");
                batchShot.ShowRow(1);
                LayOut(batchShot.Root, 660, 760);
                Pump(() => false, 50);
                LayOut(batchShot.Root, 660, 760);
                Shoot(batchShot.Root, 660, 760, 1, Path.Combine(folder, "batch-export-" + name + ".png"));
                batchShot.Close();
                PrintWindow printShot = batchMain.PrintWindowFor();
                printShot.PaperOverride = Printing.PrintSheet.A4;
                LayOut(printShot.Root, 760, 720);
                printShot.SheetChoice.IsChecked = true;
                LayOut(printShot.Root, 760, 720);
                Pump(() => false, 50);
                LayOut(printShot.Root, 760, 720);
                Shoot(printShot.Root, 760, 720, 1, Path.Combine(folder, "print-" + name + ".png"));
                printShot.Close();

                // the data templates: the Wi-Fi form filled from a code's text, an EPC payment's form
                // after Save with a message, and a contact's example, two fields to a row
                MainWindow templated = NewMain();
                Type(templated, "WIFI:T:WPA;S:Rukus's nap spot;P:hunter2;;");
                LayOut(templated.Root, MainWidth, MainHeight);
                Shoot(templated.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "templates-" + name + ".png"));
                TemplateWindow wifiForm = templated.TemplateFor(Design.TemplateKind.Wifi);
                LayOut(wifiForm.Root, 600, 680);
                Shoot(wifiForm.Root, 600, 680, 1, Path.Combine(folder, "template-wifi-" + name + ".png"));
                TemplateWindow payment = templated.TemplateFor(Design.TemplateKind.Epc);
                ((System.Windows.Controls.TextBox)payment.Input("name")).Text = "Rukus Cat";
                ((System.Windows.Controls.TextBox)payment.Input("iban")).Text = "DE89 3704 0044 0532 0130 01";
                payment.Save();
                LayOut(payment.Root, 600, 680);
                Shoot(payment.Root, 600, 680, 1, Path.Combine(folder, "template-epc-" + name + ".png"));
                TemplateWindow card = templated.TemplateFor(Design.TemplateKind.VCard);
                card.ExampleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                LayOut(card.Root, 600, 680);
                Shoot(card.Root, 600, 680, 1, Path.Combine(folder, "template-vcard-" + name + ".png"));

                // Recent codes with three codes in it, one of them a Wi-Fi network with its password
                string kept = DataFolder.Path;
                DataFolder.Reset(Path.Combine(Work, "shots-recent-" + name));
                try
                {
                    Design.RecentCodes.Clear();
                    var settings = new Settings { RecentNoteShown = true };
                    var maker = new MainWindow(settings);
                    LayOut(maker.Root, MainWidth, MainHeight);
                    foreach (string data in new[] { "https://b0r3d.org", "WIFI:T:WPA;S:home;P:hunter2;;", "BEGIN:VCARD\nVERSION:3.0\nN:Cat;Rukus;;;\nFN:Rukus Cat\nEND:VCARD" })
                    {
                        Type(maker, data);
                        maker.Remember(maker.CurrentDesign);
                    }
                    var recent = new RecentWindow(settings, id => true);
                    LayOut(recent.Root, 600, 660);
                    Shoot(recent.Root, 600, 660, 1, Path.Combine(folder, "recent-" + name + ".png"));
                }
                finally
                {
                    DataFolder.Reset(kept);
                }

                // the Scan tab after reading a picture with three codes
                MainWindow scanner = NewMain();
                scanner.Tabs.SelectedItem = scanner.ScanTab;
                const int w = 1100, h = 420;
                byte[] canvas = Canvas(w, h);
                PutQr(canvas, w, "https://b0r3d.org", 20, 20, 6);
                PutQr(canvas, w, "WIFI:T:WPA;S:home;P:hunter2;;", 330, 20, 5);
                PutZxing(canvas, w, ZXing.BarcodeFormat.EAN_13, "9506000134352", 640, 60, 3, 150);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(GrayPicture(canvas, w, h)));
                var bytes = new MemoryStream();
                png.Save(bytes);
                var dropped = new DataObject();
                dropped.SetData("PNG", new MemoryStream(bytes.ToArray()), false);
                scanner.ReadData(dropped, "the dropped picture", "nothing");
                Pump(() => !scanner.Scanning, 15000);
                LayOut(scanner.Root, MainWidth, MainHeight);
                Pump(() => false, 200);
                LayOut(scanner.Root, MainWidth, MainHeight);
                Shoot(scanner.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "scan-" + name + ".png"));

                // the camera window reading a pretend camera's picture, with two cameras to choose from;
                // and its message when Windows' privacy settings keep the camera off (never a real camera)
                var reading = new CameraWindow(new FakeCameras());
                LayOut(reading.Root, 720, 620);
                reading.ShowForShots(CameraPicture(1280, 720, "https://b0r3d.org/camera"),
                    new List<Camera.CameraInfo> { new Camera.CameraInfo { Name = "Integrated Webcam" }, new Camera.CameraInfo { Name = "USB Camera" } });
                LayOut(reading.Root, 720, 620);
                Pump(() => false, 50);
                LayOut(reading.Root, 720, 620);
                Shoot(reading.Root, 720, 620, 1, Path.Combine(folder, "camera-" + name + ".png"));
                reading.Close();
                CameraWindow denied = StartCamera(new FakeCameras(new FakeCamera { Name = "Integrated Webcam", OpenTrouble = Camera.Webcams.Problem(unchecked((int)0x80070005), "Windows didn't start Integrated Webcam") }));
                WaitForCamera(denied, () => denied.Problem != null, 5000);
                Shoot(denied.Root, 720, 620, 1, Path.Combine(folder, "camera-denied-" + name + ".png"));
                denied.Close();

                // a folder of labels checked as a batch: a line for every picture, with or without a code
                string labels = Path.Combine(Work, "shots-" + name, "Labels");
                Fresh(labels);
                foreach (var (file, text) in new[] { ("label-001.png", "https://b0r3d.org/1"), ("label-002.png", null), ("label-003.png", "https://b0r3d.org/3") })
                {
                    byte[] label = Canvas(300, 300);
                    if (text != null) PutQr(label, 300, text, 20, 20, 8);
                    else for (int y = 120; y < 180; y++) for (int x = 40; x < 260; x++) label[y * 300 + x] = 60;  // a smudge where the code should be
                    var labelPng = new PngBitmapEncoder();
                    labelPng.Frames.Add(BitmapFrame.Create(GrayPicture(label, 300, 300)));
                    using (FileStream stream = File.Create(Path.Combine(labels, file))) labelPng.Save(stream);
                }
                File.WriteAllText(Path.Combine(labels, "label-004.png"), "not really a picture");
                MainWindow batch = NewMain();
                batch.ReadFiles(new[] { labels });
                Pump(() => !batch.Scanning, 15000);
                LayOut(batch.Root, MainWidth, MainHeight);
                Pump(() => false, 200);
                LayOut(batch.Root, MainWidth, MainHeight);
                Shoot(batch.Root, MainWidth, MainHeight, 1, Path.Combine(folder, "batch-" + name + ".png"));

                // About names the settings folder: a made-up one here, so no picture shows this PC's folders
                string real = DataFolder.Path;
                DataFolder.Reset(ShownDataFolder);
                try
                {
                    var about = new AboutWindow();
                    Size size = LayOut(about.Root, 640);
                    Shoot(about.Root, size.Width, size.Height, 1, Path.Combine(folder, "about-" + name + ".png"));
                }
                finally
                {
                    DataFolder.Reset(real);
                }
            }

            // scanning the screen: a screen frozen and dimmed, with a box being drawn
            {
                Theme.Set(Look.Dark);
                MainWindow behind = NewMain();
                Type(behind, "https://b0r3d.org/snip");
                LayOut(behind.Root, MainWidth, MainHeight);
                var shot = new RenderTargetBitmap((int)MainWidth, (int)MainHeight, 96, 96, PixelFormats.Pbgra32);
                shot.Render(behind.Root);
                shot.Freeze();
                var screen = new Screens.Screen { Bounds = new Int32Rect(0, 0, (int)MainWidth, (int)MainHeight), Scale = 1, Main = true, Number = 1 };
                var snip = new SnipWindow(screen, shot, true);
                var root = (FrameworkElement)snip.Content;
                LayOut(root, MainWidth, MainHeight);
                snip.ShowBoxForShots(new Rect(560, 70, 400, 400));
                LayOut(root, MainWidth, MainHeight);
                Shoot(root, MainWidth, MainHeight, 1, Path.Combine(folder, "snip.png"));
            }
            Theme.Set(Look.Light);
            Console.WriteLine("pictures in " + folder);
            return 0;
        }

        static void Shoot(FrameworkElement root, double width, double height, double scale, string file)
        {
            var picture = new RenderTargetBitmap((int)Math.Round(width * scale), (int)Math.Round(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            picture.Render(root);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(picture));
            using (FileStream stream = File.Create(file)) png.Save(stream);
        }
    }
}
