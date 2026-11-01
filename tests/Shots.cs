using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR.Tests
{
    // b0r3dQRTests.exe --shots <folder>: draws the main window (empty, and with a code) and About,
    // in light and dark, at 100 % and 150 %, into PNG files, straight from the controls. Nothing
    // shows on screen and nothing takes focus; menus that pop open aren't in them.
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

                var about = new AboutWindow();
                Size size = LayOut(about.Root, 640);
                Shoot(about.Root, size.Width, size.Height, 1, Path.Combine(folder, "about-" + name + ".png"));
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
