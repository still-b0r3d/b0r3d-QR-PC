using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Design;
using B0r3dQR.Qr;

namespace B0r3dQR.Tests
{
    // The Make tab's designer, in the window (laid out, never shown).
    static partial class TestMain
    {
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        // a preset from the list, a frame on and off and back, Randomize
        static void PresetsAndFrameInTheWindow()
        {
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org");
            window.PresetBox.SelectedItem = "Rukus";
            WaitForCheck(window);
            Check(window.Art.Cleared > 0 && window.Code.Level == QrLevel.H && window.CurrentDesign.CornersDotColor == "#c0396b", "the Rukus preset: " + window.InfoLine.Text);
            Check(window.CheckResult == "ok", "Rukus should scan: " + window.CheckLine.Text);
            window.FrameCheck.IsChecked = true;
            WaitForCheck(window);
            Check(window.CurrentDesign.Frame != null && Math.Abs(window.Art.W - 234) < 1e-9 && window.CheckResult == "ok", "the frame: " + window.Art.W + " wide, " + window.CheckResult);
            Check((string)window.PresetBox.SelectedItem == "Rukus", "a frame shouldn't unselect the preset");
            int drawn = window.Refreshes;
            window.CaptionBox.Text = "Two\r\nlines";
            Check(window.CurrentDesign.Frame.Text == "Two\nlines" && window.Refreshes == drawn, "the caption should be drawn after a pause in the typing, like the data");
            window.CatchUp();
            Check(window.Art.CaptionLines.Count == 2, "the caption should take plain line breaks");
            window.FrameCheck.IsChecked = false;
            Check(window.CurrentDesign.Frame == null, "unticking should take the frame off");
            window.FrameCheck.IsChecked = true;
            Check(window.CurrentDesign.Frame?.Text == "Two\nlines", "ticking again should bring the same frame back");
            window.FrameRight.IsChecked = true;
            Check(window.FrameWidthRow.Visibility == Visibility.Visible && window.CurrentDesign.Frame.Position == "right", "a caption on the right should show its width");
            window.FrameBorderBox.Text = "16";   // no unit: the website refuses it
            window.CatchUp();
            Check(window.CurrentDesign.Frame.BorderWidth == "1px" && window.FrameBorderBox.Text == "1px", "a length without a unit should be refused, as on the website");
            string dots = window.CurrentDesign.DotsColor;
            Click(window.RandomizeButton);
            Check(window.CurrentDesign.Logo == Presets.LogoUrl("rukus.png") && Regex6(window.CurrentDesign.DotsColor) && window.CurrentDesign.Frame != null,
                "Randomize should keep the logo and the frame and give six-digit colours: " + window.CurrentDesign.DotsColor);
        }

        static bool Regex6(string colour) => System.Text.RegularExpressions.Regex.IsMatch(colour, "^#[0-9a-f]{6}$");

        // a logo pasted or dropped, kept the way the website keeps an upload
        static void LogoInTheWindow()
        {
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org/logo");
            byte[] cup = Convert.FromBase64String(Presets.LogoUrl("b0r3d-cup.png").Substring(22));
            var copied = new DataObject();
            copied.SetData("PNG", new MemoryStream(cup), false);
            Check(window.LogoFromData(copied), "a copied picture should be taken as the logo");
            WaitForCheck(window);
            Check(window.CurrentDesign.Logo.StartsWith("data:image/png;base64,", StringComparison.Ordinal) && window.Art.Cleared > 0, "the logo should be drawn, in a cleared square");
            Check(window.CheckResult == "ok" && window.LogoHint.Text.StartsWith("Logo ", StringComparison.Ordinal), "with the logo: " + window.CheckLine.Text + " / " + window.LogoHint.Text);
            Click(window.RemoveLogoButton);
            Check(!window.CurrentDesign.HasLogo && window.Art.Cleared == 0, "Remove should take the logo out");

            // the website's upload rules: small pictures as they are, big ones shrunk to 1024 px
            Check(MainWindow.DesignPicture(cup, 1024, out string problem) == Presets.LogoUrl("b0r3d-cup.png"), "a small PNG should be kept byte for byte");
            var big = new RenderTargetBitmap(2000, 1000, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen()) dc.DrawRectangle(Brushes.Teal, null, new Rect(0, 0, 2000, 1000));
            big.Render(visual);
            string shrunk = MainWindow.DesignPicture(QrExport.Png(big), 1024, out problem);
            BitmapSource back = Images.FromBytes(Convert.FromBase64String(shrunk.Substring(shrunk.IndexOf(',') + 1)));
            Check(back.PixelWidth == 1024 && back.PixelHeight == 512, "a 2000 px picture should be shrunk to 1024 px, not " + back.PixelWidth);
            Check(MainWindow.DesignPicture(System.Text.Encoding.UTF8.GetBytes("not a picture"), 1024, out problem) == null && problem != null, "a file that isn't a picture should be refused");
            string svg = MainWindow.DesignPicture(System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), 1024, out problem);
            Check(svg.StartsWith("data:image/svg+xml;base64,", StringComparison.Ordinal), "an SVG should be kept as it is, for the website");
        }

        // a design file opened into the window: the design and its text
        static void DesignFilesInTheWindow()
        {
            QrDesign saved = Presets.Apply("b0r3d Cup", new QrDesign());
            saved.Data = "https://b0r3d.org/from-a-file";
            saved.Frame = Presets.ApplyFrame("Dark Frame", null);
            string path = Path.Combine(Work, DesignFile.FileName);
            File.WriteAllText(path, DesignFile.Write(saved));
            MainWindow window = NewMain();
            window.OpenDesignFile(path);
            WaitForCheck(window);
            Check(window.DataBox.Text == saved.Data && window.CurrentDesign.Frame?.TextColor == "#ffffff" && (string)window.PresetBox.SelectedItem == "b0r3d Cup",
                "the design didn't open into the window");
            Check(DesignFile.Write(window.CurrentDesign) == DesignFile.Write(saved), "the design should come back exactly as it was saved");
            Check(window.CheckResult == "ok", "the opened design should scan: " + window.CheckLine.Text);
        }

        // the design is kept for next time, never its text
        static void DesignKeptWithoutText()
        {
            string data = DataFolder.Path;
            DataFolder.Reset(Path.Combine(Work, "kept-design"));
            try
            {
                MainWindow window = NewMain();
                Type(window, "https://b0r3d.org/secret-text-4711");
                window.PresetBox.SelectedItem = "Dots";
                window.SaveSettings();
                string kept = File.ReadAllText(Path.Combine(DataFolder.Path, MainWindow.KeptDesignFile));
                Check(kept.Contains("\"type\": \"dots\"") && !kept.Contains("secret-text-4711") && !kept.Contains("\"data\""), "the kept design: " + kept.Substring(0, Math.Min(200, kept.Length)));
                MainWindow next = NewMain();
                Check(next.CurrentDesign.DotsType == "dots" && next.DataBox.Text.Length == 0, "the next window should start from the kept design, with no text");

                // a frame length emptied in the window still comes back next time
                next.FrameCheck.IsChecked = true;
                next.FramePaddingBox.Text = "";
                next.CatchUp();
                next.SaveSettings();
                MainWindow third = NewMain();
                Check(third.CurrentDesign.DotsType == "dots" && third.CurrentDesign.Frame != null, "a design with an emptied frame length should still be kept");

                // the first 0.3 start after 0.2: its level, quiet zone and picture size, at whole pixels
                File.Delete(Path.Combine(DataFolder.Path, MainWindow.KeptDesignFile));
                string ini = Path.Combine(DataFolder.Path, "old.ini");
                File.WriteAllText(ini, "look=windows\r\nlevel=H\r\nquiet_zone=2\r\npicture_size=1000\r\n");
                Settings old = Settings.Load(ini);
                Check(old.FromOlder && old.WholePixels, "an ini from 0.2 should be known as one, its pictures at whole pixels");
                MainWindow upgraded = new MainWindow(old);
                Check(upgraded.CurrentDesign.Width == 1000 && upgraded.CurrentDesign.Level == QrLevel.H && upgraded.CurrentDesign.Margin == 2 && upgraded.WholePixelsCheck.IsChecked == true,
                    "0.2's level, quiet zone and 1000 px pictures should carry over");
                upgraded.Close();
            }
            finally
            {
                DataFolder.Reset(data);
            }
        }

        // the checks under the data box and their one-click fixes, one after another
        static void DataChecksInTheWindow()
        {
            MainWindow window = NewMain();
            Type(window, "  b0r3d.org/?utm_source=qr ");
            Button Fix(string label) => window.DataChecksPanel.Children.OfType<DockPanel>().SelectMany(p => p.Children.OfType<Button>()).FirstOrDefault(b => (string)b.Content == label);
            Check(Fix("Trim") != null && Fix("Add https://") != null, "spaces around a bare address should offer Trim and Add https://");
            Click(Fix("Trim"));
            Check(window.DataBox.Text == "b0r3d.org/?utm_source=qr", "Trim: \"" + window.DataBox.Text + "\"");
            Click(Fix("Add https://"));
            Check(window.DataBox.Text == "https://b0r3d.org/?utm_source=qr" && Fix("Remove them") != null, "Add https://: \"" + window.DataBox.Text + "\"");
            Click(Fix("Remove them"));
            Check(window.DataBox.Text == "https://b0r3d.org/" && window.DataChecksPanel.Children.Count == 0, "Remove them: \"" + window.DataBox.Text + "\"");
        }

        // print size in the window: the website's line, and inches kept as the same length
        static void PrintSizeInTheWindow()
        {
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org");
            window.PrintCheck.IsChecked = true;
            Check(window.PrintHint.Text.StartsWith("472 × 472 px. Each module is", StringComparison.Ordinal), "40 mm at 300 DPI: " + window.PrintHint.Text);
            Check(window.SizeHint.Text.StartsWith("Saves as 472 × 472 pixels, at the print size", StringComparison.Ordinal), "the size line with print size on: " + window.SizeHint.Text);
            window.PrintUnitBox.SelectedItem = "in";
            Check(Math.Abs(window.PrintWidthBox.Value - 1.575) < 1e-9 && window.PrintSettings.PixelWidth == 472, "40 mm in inches should show as 1.575 in and stay 472 px at 300 DPI, not " + window.PrintWidthBox.Value);
            window.PrintDpiBox.SelectedIndex = 0;
            window.PrintDpiBox.SelectedIndex = 1;
            window.PrintUnitBox.SelectedItem = "mm";
            Check(Math.Abs(window.PrintSettings.Millimetres - 40) < 1e-9 && window.PrintSettings.PixelWidth == 472, "back in mm it should still be 40 mm, not " + window.PrintSettings.Millimetres);
            window.PrintCheck.IsChecked = false;
            Check(window.SizeHint.Text.StartsWith("Saves as 200 × 200 pixels", StringComparison.Ordinal), "print size off: " + window.SizeHint.Text);
        }

        // a number typed with a point means the same whatever the PC's language; a comma where it's the decimal mark
        static void NumbersInAnyLanguage()
        {
            MainWindow window = NewMain();
            var was = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                void Typed(Controls.NumberBox box, string text, double expected)
                {
                    box.TextField.Text = text;
                    box.Commit();
                    Check(box.Value == expected, "\"" + text + "\" on a German PC should be " + expected + ", not " + box.Value);
                }
                Typed(window.QuietBox, "4.5", 4.5);
                Typed(window.QuietBox, "2,5", 2.5);
                Typed(window.LogoSpaceBox, "0.5", 0.5);
                Typed(window.PrintWidthBox, "40.5", 40.5);
                Typed(window.PrintWidthBox, "1.000,5", 1000);   // 1000.5 mm is over the 1000 mm the box takes
                Typed(window.SizeBox, "1000", 1000);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = was;
            }
        }
    }
}
