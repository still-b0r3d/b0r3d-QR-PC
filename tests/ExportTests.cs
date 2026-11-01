using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using B0r3dQR.Qr;

namespace B0r3dQR.Tests
{
    static partial class TestMain
    {
        static void PngIsExact()
        {
            QrCode code = QrCode.Make("https://b0r3d.org/qr", QrLevel.M);
            QrShapes shapes = QrShapes.For(code, 4);
            const int k = 7;
            string file = Path.Combine(Work, "exact.png");
            Exports.Write(file, QrPicture.Png(QrPicture.Render(shapes, k)));

            BitmapFrame frame = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Check(frame.PixelWidth == shapes.Width * k && frame.PixelHeight == shapes.Width * k, "the PNG is " + frame.PixelWidth + "x" + frame.PixelHeight);
            // PNG keeps pixels per metre as a whole number, so 96 DPI comes back as 95.99
            Check(Math.Abs(frame.DpiX - 96) < 0.1 && Math.Abs(frame.DpiY - 96) < 0.1, "the PNG says " + frame.DpiX + " DPI");
            byte[] grays = ScanCheck.Grays(frame, out int width, out int height);
            for (int my = 0; my < shapes.Width; my++)
                for (int mx = 0; mx < shapes.Width; mx++)
                {
                    bool inside = mx >= 4 && my >= 4 && mx < 4 + code.Size && my < 4 + code.Size;
                    byte want = inside && code[my - 4, mx - 4] ? (byte)0 : (byte)255;
                    for (int py = 0; py < k; py++)
                        for (int px = 0; px < k; px++)
                        {
                            byte got = grays[(my * k + py) * width + mx * k + px];
                            if (got != want) Check(false, "module " + mx + "," + my + " pixel " + px + "," + py + " is " + got + ", not " + want);
                        }
                }
            ScanCheck.Reading reading = ScanCheck.Read(grays, width, height);
            Check(reading != null && reading.Text == code.Text, "the saved PNG read back as \"" + Short(reading?.Text) + "\"");
        }

        static void SvgIsExact()
        {
            QrCode code = QrCode.Make("SVG test 123 é", QrLevel.H);
            QrShapes shapes = QrShapes.For(code, 4);
            string file = Path.Combine(Work, "exact.svg");
            Exports.Write(file, new UTF8Encoding(false).GetBytes(QrPicture.Svg(shapes, 500)));

            XDocument doc = XDocument.Load(file);
            XNamespace svg = "http://www.w3.org/2000/svg";
            XElement root = doc.Root;
            string w = shapes.Width.ToString();
            Check(root.Name == svg + "svg", "the root is " + root.Name);
            Check((string)root.Attribute("width") == "500" && (string)root.Attribute("height") == "500", "the SVG isn't 500 pixels across");
            Check((string)root.Attribute("viewBox") == "0 0 " + w + " " + w, "viewBox " + (string)root.Attribute("viewBox"));
            XElement rect = root.Element(svg + "rect");
            Check(rect != null && (string)rect.Attribute("fill") == "#ffffff" && (string)rect.Attribute("width") == w, "no white background");
            XElement path = root.Element(svg + "path");
            Check(path != null && (string)path.Attribute("fill") == "#000000", "no black path");

            // the path read back into a grid is exactly the code
            string d = (string)path.Attribute("d");
            var grid = new bool[shapes.Width, shapes.Width];
            int covered = 0;
            foreach (Match m in Regex.Matches(d, @"M(\d+) (\d+)h(\d+)v1h-(\d+)z"))
            {
                int x = int.Parse(m.Groups[1].Value), y = int.Parse(m.Groups[2].Value), length = int.Parse(m.Groups[3].Value);
                Check(m.Groups[4].Value == m.Groups[3].Value, "a run that doesn't close: " + m.Value);
                for (int i = x; i < x + length; i++) grid[i, y] = true;
                covered += m.Length;
            }
            Check(covered == d.Length, "the path has something besides runs in it");
            for (int y = 0; y < shapes.Width; y++)
                for (int x = 0; x < shapes.Width; x++)
                {
                    bool inside = x >= 4 && y >= 4 && x < 4 + code.Size && y < 4 + code.Size;
                    Check(grid[x, y] == (inside && code[y - 4, x - 4]), "the SVG's module " + x + "," + y + " is wrong");
                }

            // drawn from the file's own path data (as a program reading the SVG would) it reads back
            var visual = new DrawingVisual();
            RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, shapes.Width * 4, shapes.Width * 4));
                dc.PushTransform(new ScaleTransform(4, 4));
                dc.DrawGeometry(Brushes.Black, null, Geometry.Parse(d));
                dc.Pop();
            }
            var picture = new RenderTargetBitmap(shapes.Width * 4, shapes.Width * 4, 96, 96, PixelFormats.Pbgra32);
            picture.Render(visual);
            byte[] grays = ScanCheck.Grays(picture, out int pw, out int ph);
            ScanCheck.Reading reading = ScanCheck.Read(grays, pw, ph);
            Check(reading != null && reading.Text == code.Text, "the SVG drawn from its path read back as \"" + Short(reading?.Text) + "\"");
        }

        static void SavingIsSafe()
        {
            string folder = Path.Combine(Work, "saving");
            Fresh(folder);
            string file = Path.Combine(folder, "save.png");
            File.WriteAllBytes(file + ".tmp", new byte[] { 7 });  // someone else's file that happens to have that name
            Exports.Write(file, new byte[] { 1, 2, 3 });
            Exports.Write(file, new byte[] { 4, 5 });
            Check(File.ReadAllBytes(file).SequenceEqual(new byte[] { 4, 5 }), "saving over a file didn't replace it");
            Check(File.Exists(file + ".tmp") && File.ReadAllBytes(file + ".tmp")[0] == 7, "saving destroyed an existing save.png.tmp");
            string[] leftOver = Directory.GetFiles(folder).Select(Path.GetFileName).Where(n => n != "save.png" && n != "save.png.tmp").ToArray();
            Check(leftOver.Length == 0, "saving left behind " + string.Join(", ", leftOver));

            string missing = Path.Combine(Work, "no such folder", "x.png");
            try
            {
                Exports.Write(missing, new byte[] { 1 });
                Check(false, "saving into a missing folder should fail (for the window to say so)");
            }
            catch (DirectoryNotFoundException)
            {
            }

            string readOnly = Path.Combine(Work, "read-only.png");
            File.WriteAllBytes(readOnly, new byte[] { 9 });
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            try
            {
                Exports.Write(readOnly, new byte[] { 1 });
                Check(false, "a read-only file was saved over");
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is IOException)
            {
            }
            finally
            {
                File.SetAttributes(readOnly, FileAttributes.Normal);
            }
            Check(File.ReadAllBytes(readOnly).SequenceEqual(new byte[] { 9 }), "the read-only file changed");
            Check(!Directory.GetFiles(Work, "*.tmp").Any(), "the refused save left a temporary file behind");
        }

        static void CopyAndDragData()
        {
            QrCode code = QrCode.Make("https://b0r3d.org/copy", QrLevel.Q);
            BitmapSource picture = QrPicture.Render(QrShapes.For(code, 4), 5);
            byte[] png = QrPicture.Png(picture);

            DataObject copy = Exports.DataFor(picture, png, "b0r3d.org", false);
            Check(copy.GetDataPresent(DataFormats.Bitmap), "Copy gives no picture");
            Check(copy.GetDataPresent("PNG"), "Copy gives no PNG");
            Check(!copy.GetDataPresent(DataFormats.FileDrop), "Copy shouldn't make a file");
            var cloud = copy.GetData("CanUploadToCloudClipboard") as MemoryStream;
            Check(cloud != null && cloud.ToArray().SequenceEqual(new byte[4]), "Copy should keep the picture out of Windows' cloud clipboard");
            var stream = (MemoryStream)copy.GetData("PNG");
            BitmapFrame fromCopy = BitmapDecoder.Create(new MemoryStream(stream.ToArray()), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            byte[] grays = ScanCheck.Grays(fromCopy, out int width, out int height);
            Check(ScanCheck.Read(grays, width, height)?.Text == code.Text, "the copied PNG doesn't read back");

            DataObject drag = Exports.DataFor(picture, png, "b0r3d.org", true);
            StringCollection files = drag.GetFileDropList();
            Check(files.Count == 1, "dragging gives " + files.Count + " files");
            Check(files[0] == Path.Combine(DataFolder.Path, "b0r3dQR drag", "b0r3d.org.png"), "the dragged file is " + files[0]);
            Check(File.ReadAllBytes(files[0]).SequenceEqual(png), "the dragged file isn't the PNG");

            // where settings can't be kept, dragging still gives the picture, just no file
            string data = DataFolder.Path;
            DataFolder.Choose(null, Path.Combine(Path.GetTempPath(), "Temp1_b0r3dQR.zip"));
            DataObject noFile = Exports.DataFor(picture, png, "b0r3d.org", true);
            DataFolder.Reset(data);
            Check(noFile.GetDataPresent(DataFormats.Bitmap) && !noFile.GetDataPresent(DataFormats.FileDrop), "with nowhere to keep it, drag should give the picture and no file");

            Exports.ClearDragFolder();
            Check(!Directory.Exists(Exports.DragFolder), "the drag folder wasn't cleared");
        }

        static void FileNames()
        {
            void Expect(string text, string name)
            {
                string got = Exports.SuggestedName(text);
                Check(got == name, "\"" + text + "\" gave \"" + got + "\", not \"" + name + "\"");
            }
            Expect("https://www.b0r3d.org/qr?x=1", "b0r3d.org");
            Expect("http://example.com", "example.com");
            Expect("https://someone:hunter2@example.com/x", "example.com");
            Expect("Hello there, world!", "Hello-there-world");
            Expect("WIFI:T:WPA;S:home net;P:secret;;", "wifi-home-net");
            Expect("WIFI:S:my\\;cafe;T:WPA;P:hunter2;;", "wifi-my-cafe");
            Expect("WIFI:T:nopass;;", "wifi");
            // pasted text often starts with characters you can't see
            Expect("﻿WIFI:T:WPA;S:home;P:secret;;", "wifi-home");
            Expect("​WIFI:T:WPA;S:home;P:secret;;", "wifi-home");
            Expect("  wifi:T:WPA;S:home;P:secret;;", "wifi-home");
            Expect("otpauth://totp/b0r3d:me?secret=JBSWY3DPEHPK3PXP", "otpauth");
            Expect("rtsp://admin:hunter2@192.0.2.1/stream", "rtsp");
            Expect("mailto:someone@example.com?subject=Hi", "email");
            Expect("tel:+15555550123", "phone");
            Expect("BEGIN:VCARD\nVERSION:3.0\nFN:Someone\nEND:VCARD", "contact");
            Expect("BCD\n002\n1\nSCT\n\nName\nDE89370400440532013000", "payment");
            Expect("", "qr-code");
            Expect("   ", "qr-code");
            Expect("!!!", "qr-code");
            Expect("con", "qr-code");
            Expect("COM1", "qr-code");
            Expect("lpt9.txt", "qr-code");
            Expect("a/b\\c:d*e?f\"g<h>i|j", "a-b-c-d-e-f-g-h-i-j");
            Check(Exports.SuggestedName(new string('a', 100)).Length == 40, "a long name should be cut to 40 characters");
            foreach (string secret in new[] { "WIFI:T:WPA;S:home;P:secret;;", "﻿WIFI:T:WPA;S:home;P:secret;;", "otpauth://totp/x?secret=secret", "rtsp://user:secret@host/" })
                Check(!Exports.SuggestedName(secret).Contains("secret"), "a secret got into the file name for \"" + Short(secret) + "\"");
        }
    }
}
