using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using B0r3dQR.Qr;

namespace B0r3dQR
{
    // b0r3dQR.exe --selftest <file>: no window shown. Makes a code, draws it the way a PNG is saved,
    // reads it back, checks the SVG, builds the windows, and writes what it found to <file> as
    // key=value lines; the exit code is 0 when it all worked. The tests run it on the finished
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
                }
                lines.Add("windows=ok");
            }
            catch (Exception e)
            {
                ok = false;
                lines.Add("error=" + e.GetType().Name + ": " + e.Message.Replace('\r', ' ').Replace('\n', ' '));
            }
            lines.Add("result=" + (ok ? "ok" : "failed"));
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
