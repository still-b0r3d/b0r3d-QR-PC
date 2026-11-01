using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using B0r3dQR.Qr;

namespace B0r3dQR
{
    // b0r3dQR.exe --selftest <file>: no window shown. Makes a code, draws it the way a PNG is saved,
    // reads it back (as the scan check and as the Scan tab), checks the SVG, builds the windows
    // (the Scan tab with a card, and a screen's overlay too), draws a designed code (a preset's logo,
    // a frame), fills a data template and builds the Recent codes window, and writes what it found to <file>
    // as key=value lines; the exit code is 0 when it all worked. The tests run it on the finished
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
