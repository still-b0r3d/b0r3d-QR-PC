using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR.Tests
{
    // b0r3dQRTests.exe --shots <folder>: draws the main window (empty, with a code, and the Scan
    // tab with codes read from a picture), About, and the screen-scanning overlay, in light and
    // dark, at 100 % and 150 %, into PNG files, straight from the controls. Nothing shows on
    // screen and nothing takes focus; menus that pop open aren't in them.
    static partial class TestMain
    {
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

                var about = new AboutWindow();
                Size size = LayOut(about.Root, 640);
                Shoot(about.Root, size.Width, size.Height, 1, Path.Combine(folder, "about-" + name + ".png"));
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
