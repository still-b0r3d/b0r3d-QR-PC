using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using B0r3dQR.Barcodes;
using B0r3dQR.Design;
using B0r3dQR.Qr;
using B0r3dQR.Scan;
using BarcodeFormat = B0r3dQR.Barcodes.BarcodeFormat;

namespace B0r3dQR.Tests
{
    // Barcodes other than QR, against what the website's own barcode maker made (zint 2.16.0 in zxing-wasm).
    static partial class TestMain
    {
        static BarcodeType TypeOf(string id) => BarcodeFormats.ById(id).Type;

        // every case in the website's test data: the same SVG, character for character, or the same message
        static void BarcodesLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (var c in WebsiteBarcodes)
            {
                var options = new BarcodeOptions { ShowText = c.ShowText, AllowRectangular = c.Rectangular, BarHeight = c.BarHeight, QuietZones = c.QuietZones };
                BarcodeSvg made = Barcode.Make(c.Text, TypeOf(c.Type), options, out string error);
                string what = c.Type + " " + Show(c.Text.Length > 40 ? c.Text.Substring(0, 40) + "…" : c.Text) + (c.ShowText ? "" : " no text") +
                              (c.Rectangular ? " rectangles" : "") + (c.BarHeight != 50 ? " height " + c.BarHeight : "") + (c.QuietZones ? "" : " no margins");
                if (c.Error != null)
                {
                    if (made != null || error != c.Error) wrong.Add(what + ": should say " + Show(c.Error) + ", says " + Show(error));
                }
                else if (made == null) wrong.Add(what + ": refused: " + error);
                else if (made.Svg != c.Svg || made.Width != c.Width || made.Height != c.Height) wrong.Add(what + ": " + FirstDifference(made.Svg, c.Svg));
            }
            Check(wrong.Count == 0, wrong.Count + " of " + WebsiteBarcodes.Length + " differ from the website:\n      " + string.Join("\n      ", wrong.Take(15)));
        }

        // Data Matrix, Aztec and PDF417 alone: zint's modules for the website, row for row, or its message
        static void BarcodeMatricesLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (var c in WebsiteBarcodeMatrices)
            {
                List<bool[]> rows = Barcode.Modules(c.Text, TypeOf(c.Type), c.Rectangular, out string error);
                string what = c.Type + " " + Show(c.Text.Length > 40 ? c.Text.Substring(0, 40) + "…" : c.Text) + (c.Rectangular ? " rectangles" : "");
                if (c.Error != null)
                {
                    if (rows != null || error != c.Error) wrong.Add(what + ": should say " + Show(c.Error) + ", says " + Show(error));
                    continue;
                }
                if (rows == null)
                {
                    wrong.Add(what + ": refused: " + error);
                    continue;
                }
                string[] ours = rows.Select(r => new string(r.Select(dark => dark ? '1' : '0').ToArray())).ToArray();
                if (ours.SequenceEqual(c.Rows)) continue;
                int row = 0;
                while (row < ours.Length && row < c.Rows.Length && ours[row] == c.Rows[row]) row++;
                wrong.Add(what + ": " + ours.Length + " rows of " + (ours.Length > 0 ? ours[0].Length : 0) + ", the website's " + c.Rows.Length + " of " + c.Rows[0].Length + "; they part at row " + row);
            }
            Check(wrong.Count == 0, wrong.Count + " of " + WebsiteBarcodeMatrices.Length + " differ from the website:\n      " + string.Join("\n      ", wrong.Take(15)));
        }

        // where two texts part, with a little of each around it
        static string FirstDifference(string ours, string theirs)
        {
            int i = 0;
            while (i < ours.Length && i < theirs.Length && ours[i] == theirs[i]) i++;
            int from = Math.Max(0, i - 40);
            string Part(string s) => s.Substring(Math.Min(from, s.Length), Math.Min(100, s.Length - Math.Min(from, s.Length)));
            return "at " + i + ", ours " + Show(Part(ours)) + "\n        website " + Show(Part(theirs));
        }

        // Each type's example, drawn as the scan check draws it, is found by the Scan tab's reader as that
        // type, and made again from what was read it is the same barcode, character for character (a
        // book's EAN-13 comes back as its ISBN, GS1 data in round brackets)
        static void BarcodesScanAndMakeAgain()
        {
            var wrong = new List<string>();
            foreach (BarcodeFormat format in BarcodeFormats.All)
            {
                BarcodeSvg made = Barcode.Make(format.Example, format.Type, new BarcodeOptions(), out string error);
                if (made == null)
                {
                    wrong.Add(format.Id + ": its example is refused: " + error);
                    continue;
                }
                BitmapSource picture = BarcodeExport.CheckPicture(new BarcodeArt(made), new BarcodeLook());
                byte[] grays = ScanCheck.Grays(picture, out int w, out int h);
                List<FoundCode> found = Scan.Reader.ReadAll(grays, w, h, "test", CancellationToken.None);
                if (found.Count != 1)
                {
                    wrong.Add(format.Id + ": " + found.Count + " codes found (" + string.Join(", ", found.Select(f => f.Format)) + ")");
                    continue;
                }
                FoundCode code = found[0];
                if (code.Barcode != format.Type || !code.CanMakeAgain)
                {
                    wrong.Add(format.Id + ": read as " + code.Format + " " + Show(code.Text) + ", to make again as " + (code.Barcode?.ToString() ?? "nothing"));
                    continue;
                }
                BarcodeSvg again = Barcode.Make(format.NormalizeRead(code.Text), format.Type, new BarcodeOptions(), out error);
                if (again == null) wrong.Add(format.Id + ": made again from " + Show(code.Text) + ", refused: " + error);
                else if (again.Svg != made.Svg) wrong.Add(format.Id + ": made again from " + Show(code.Text) + ": " + FirstDifference(again.Svg, made.Svg));
            }
            Check(wrong.Count == 0, wrong.Count + " of " + BarcodeFormats.All.Length + " types:\n      " + string.Join("\n      ", wrong));
        }

        // a type chosen from the Code type list, once its barcode is made and checked
        static void ChooseType(MainWindow window, BarcodeType? type)
        {
            window.CodeTypeBox.SelectedItem = window.CodeTypeBox.Items.OfType<ComboBoxItem>().First(i => Equals(i.Tag as BarcodeType?, type));
            WaitForCheck(window);
        }

        static void TypeBarcode(MainWindow window, string text)
        {
            int before = window.Refreshes;
            window.BarcodeDataBox.Text = text;
            Pump(() => window.Refreshes != before && window.CheckResult != "checking", 5000);
            Check(window.Refreshes != before, "typing didn't make a new barcode within 5 seconds");
        }

        static string InCode(MainWindow window, string row)
        {
            UIElement[] cells = window.InCodeRows.Children.Cast<UIElement>().ToArray();
            for (int i = 0; i + 1 < cells.Length; i += 2)
                if (((TextBlock)cells[i]).Text == row) return ((TextBlock)cells[i + 1]).Text;
            return null;
        }

        // The Code type list and the barcode settings: every type made from its example and checked by a scan,
        // only its own options shown, the website's messages, each type keeping its own text, QR code again
        static void BarcodesInTheWindow()
        {
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org");
            List<ComboBoxItem> items = window.CodeTypeBox.Items.OfType<ComboBoxItem>().ToList();
            List<BarcodeType> types = items.Where(i => i.Tag is BarcodeType).Select(i => (BarcodeType)i.Tag).ToList();
            Check(items[0].Content as string == "QR code" && items[0].IsEnabled && ReferenceEquals(window.CodeTypeBox.SelectedItem, items[0]), "the list should start with QR code, chosen");
            Check(types.SequenceEqual(BarcodeFormats.All.Select(f => f.Type)), "the list should have the website's 13 types in its order: " + string.Join(", ", types));
            Check(items.Skip(1).Where(i => !(i.Tag is BarcodeType)).All(i => !i.IsEnabled) && items.Count(i => !(i.Tag is BarcodeType)) == 1 + BarcodeFormats.Groups.Length,
                "each group of types should have a heading that can't be chosen");

            foreach (BarcodeType type in types)
            {
                BarcodeFormat format = BarcodeFormats.Get(type);
                ChooseType(window, type);
                Check(window.MakingBarcode && window.CodeType == type && window.BarcodeDataBox.Text == format.Example, format.Id + ": its example should be made");
                Check(window.BarcodeShown != null && window.Preview.Source != null && window.CheckResult == "ok", format.Id + ": the check says " + window.CheckResult + ": " + window.CheckLine.Text);
                Check(window.QrPanel.Visibility == Visibility.Collapsed && window.BarcodePanel.Visibility == Visibility.Visible, format.Id + ": the QR code's settings are still out");
                Check((window.LinearOptions.Visibility == Visibility.Visible) == format.Linear, format.Id + ": the text and bar height options belong to barcodes with bars");
                Check((window.RectangularCheck.Visibility == Visibility.Visible) == (type == BarcodeType.DataMatrix || type == BarcodeType.Gs1DataMatrix), format.Id + ": rectangles are Data Matrix's");
                Check(InCode(window, "Type").StartsWith(format.Label, StringComparison.Ordinal) && InCode(window, "Stored") == window.BarcodeExpected, format.Id + ": what's in the code: " + InCode(window, "Type") + " / " + InCode(window, "Stored"));
                Check(window.BarcodeHint.Text == format.Hint, format.Id + ": the hint");
            }

            // a check digit added, said so; the website's messages for a wrong one and for too few digits
            ChooseType(window, BarcodeType.Ean13);
            TypeBarcode(window, "400638133393");
            Check(window.CheckResult == "ok" && window.BarcodeExpected == "4006381333931" && InCode(window, "Check digit") == "1, added for you", "EAN-13's check digit: " + InCode(window, "Check digit"));
            TypeBarcode(window, "4006381333932");
            string wrongDigit = BarcodeFormats.Get(BarcodeType.Ean13).Check("4006381333932");
            Check(window.BarcodeShown == null && window.BarcodeError.Text == wrongDigit && window.BarcodeError.Visibility == Visibility.Visible && wrongDigit != null,
                "a wrong check digit: " + window.BarcodeError.Text);
            Pump(() => !window.SavePngButton.IsEnabled, 2000);
            Check(!window.SavePngButton.IsEnabled && !window.HasCode, "with nothing made there's nothing to save");
            TypeBarcode(window, "4006381333931");
            Pump(() => window.SavePngButton.IsEnabled, 2000);
            Check(window.BarcodeShown != null && window.BarcodeError.Visibility == Visibility.Collapsed && window.SavePngButton.IsEnabled, "fixed, the barcode comes back");

            // the check digit where the type writes the number anew: the barcode's own check digit
            ChooseType(window, BarcodeType.Isbn);
            TypeBarcode(window, "0-306-40615-2");
            Check(window.BarcodeExpected == "9780306406157" && InCode(window, "Check digit") == "7, added for you", "an ISBN-10's check digit: " + InCode(window, "Check digit"));
            ChooseType(window, BarcodeType.UpcE);
            TypeBarcode(window, "123456");
            Check(window.BarcodeExpected == "01234565" && InCode(window, "Check digit") == "5, added for you", "a 6-digit UPC-E's check digit: " + InCode(window, "Check digit"));
            // refused, What's in this code has nothing of the QR code's left in it
            TypeBarcode(window, "12");
            Check(window.InCodeText.Visibility == Visibility.Collapsed && window.InCodeFooter.Text == "" && window.InCodeRows.Children.Count == 0, "a refused barcode's What's in this code");

            // a group's heading reached by typing its letter isn't a choice: the type stays
            ChooseType(window, BarcodeType.Ean13);
            window.CodeTypeBox.SelectedItem = items.First(i => i.Content as string == BarcodeFormats.Groups[2]);
            Check(window.CodeType == BarcodeType.Ean13 && window.MakingBarcode && ReferenceEquals(window.CodeTypeBox.SelectedItem, items.First(i => Equals(i.Tag as BarcodeType?, BarcodeType.Ean13))),
                "a heading chosen should leave EAN-13 chosen, not " + (window.CodeType?.ToString() ?? "QR code"));

            // the number boxes as the website takes them: halves round up, 0 is the default
            window.BarHeightBox.Value = 22.5;
            WaitForCheck(window);
            Check(window.BarcodeSettings.BarHeight == 23, "bar height 22.5 should be 23, as the website rounds it, not " + window.BarcodeSettings.BarHeight);
            window.BarHeightBox.Value = 0;
            window.ScaleBox.Value = 0;
            WaitForCheck(window);
            Check(window.BarcodeSettings.BarHeight == 50 && window.BarcodeSettings.Scale == 4, "0 should be the default: height " + window.BarcodeSettings.BarHeight + ", scale " + window.BarcodeSettings.Scale);
            window.ScaleBox.Value = 2.5;
            Check(window.BarcodeSettings.Scale == 3, "2.5 pixels per module should be 3, not " + window.BarcodeSettings.Scale);
            window.ScaleBox.Value = 4;

            // the scan check: light on dark Data Matrix reads, as on the website; so does an EAN-13 without quiet zones
            void Colour(Controls.ColorBox box, string colour)
            {
                box.TextField.Text = colour;
                box.Commit();
            }
            ChooseType(window, BarcodeType.DataMatrix);
            Colour(window.BarsColorBox, "#ffffff");
            Colour(window.BarcodeBackgroundBox, "#000000");
            WaitForCheck(window);
            Check(window.CheckResult == "ok", "light on dark Data Matrix: the check says " + window.CheckResult);
            Colour(window.BarsColorBox, "#000000");
            Colour(window.BarcodeBackgroundBox, "#ffffff");
            ChooseType(window, BarcodeType.Ean13);
            window.QuietZonesCheck.IsChecked = false;
            WaitForCheck(window);
            Check(window.CheckResult == "ok", "EAN-13 without quiet zones: the check says " + window.CheckResult);
            window.QuietZonesCheck.IsChecked = true;
            WaitForCheck(window);

            // the options: text, bar height and quiet zones change the drawing as zint draws it
            string Svg() => window.BarcodeShown.Barcode.Svg;
            string withText = Svg();
            window.ShowTextCheck.IsChecked = false;
            WaitForCheck(window);
            Check(withText.Contains("<text") && !Svg().Contains("<text") && window.CheckResult == "ok", "the text under the bars goes");
            window.ShowTextCheck.IsChecked = true;
            window.BarHeightBox.Value = 100;
            WaitForCheck(window);
            Check(window.BarcodeShown.H > BarcodeSvg.From(withText).Height && window.BarcodeSettings.BarHeight == 100, "taller bars: " + window.BarcodeShown.H);
            window.BarHeightBox.Value = 50;
            WaitForCheck(window);
            Check(Svg() == withText, "the default height again is the first drawing again");
            window.QuietZonesCheck.IsChecked = false;
            WaitForCheck(window);
            Check(window.BarcodeShown.W < BarcodeSvg.From(withText).Width && window.QuietZonesNote.Visibility == Visibility.Visible, "without quiet zones it's narrower, with the note");
            window.QuietZonesCheck.IsChecked = true;
            WaitForCheck(window);

            // each type keeps its own text; QR code again is the code typed before
            ChooseType(window, BarcodeType.Code128);
            TypeBarcode(window, "B0R3D 128");
            ChooseType(window, BarcodeType.Ean13);
            Check(window.BarcodeDataBox.Text == "4006381333931", "EAN-13 should keep its own text: " + window.BarcodeDataBox.Text);
            ChooseType(window, BarcodeType.Code128);
            Check(window.BarcodeDataBox.Text == "B0R3D 128", "Code 128 should keep its own text");
            ChooseType(window, null);
            Check(!window.MakingBarcode && window.Code?.Text == "https://b0r3d.org" && window.QrPanel.Visibility == Visibility.Visible && window.CheckResult == "ok",
                "QR code again: " + window.Code?.Text);
        }

        // a barcode's files as the website makes them: whole pixels per module, its colours, a print size
        static void BarcodeFilesLikeTheWebsite()
        {
            BarcodeSvg made = Barcode.Make("4006381333931", BarcodeType.Ean13, new BarcodeOptions(), out string error);
            Check(made != null, "no barcode: " + error);
            var art = new BarcodeArt(made);
            var look = new BarcodeLook { Color = "#123456", Background = "#fedcba", Scale = 3 };
            var print = new PrintSize();

            BitmapSource picture = BarcodeExport.Picture(art, look, print, false);
            Check(picture.PixelWidth == art.W * 3 && picture.PixelHeight == art.H * 3, "the picture is " + picture.PixelWidth + " × " + picture.PixelHeight + ", not " + art.W * 3 + " × " + art.H * 3);
            byte[] grays = ScanCheck.Grays(picture, out int w, out int h);
            Check(BarcodeExport.Read(grays, w, h, BarcodeFormats.Get(BarcodeType.Ean13)) == "4006381333931", "the picture doesn't read back");
            var bgra = new byte[w * h * 4];
            new FormatConvertedBitmap(picture, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(bgra, w * 4, 0);
            Check(Paints(bgra, 0, 0xfe, 0xdc, 0xba) && Paints(bgra, FirstInk(bgra, w, h / 3, 0xfe), 0x12, 0x34, 0x56), "the picture's colours: the background and the bars");
            var jpgLook = look.Clone();
            jpgLook.Background = "transparent";
            BitmapSource jpgPicture = BarcodeExport.Picture(art, jpgLook, print, true);
            var jpgPixels = new byte[jpgPicture.PixelWidth * jpgPicture.PixelHeight * 4];
            new FormatConvertedBitmap(jpgPicture, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(jpgPixels, jpgPicture.PixelWidth * 4, 0);
            Check(Paints(jpgPixels, 0, 0xff, 0xff, 0xff), "a JPG of a see-through background is on white, not black");

            string svg = BarcodeExport.Svg(made, look, print);
            Check(svg.Contains("fill=\"#123456\"") && svg.Contains("fill=\"#fedcba\"") && svg.Contains("viewBox=\"0 0 " + made.Width + " " + made.Height + "\""), "the SVG's colours and size");
            look.Transparent = true;
            Check(!BarcodeExport.Svg(made, look, print).Contains("#fedcba"), "a transparent background is left out of the SVG");
            look.Transparent = false;

            print.On = true;
            print.Width = 40;
            print.Unit = "mm";
            // whole pixels per module at the resolution (as the website does it), so up to the width asked for
            string printed = BarcodeExport.Svg(made, look, print);
            Match size = Regex.Match(printed, "<svg width=\"([0-9.]+)mm\" height=\"([0-9.]+)mm\"");
            BarcodeGuidance guide = BarcodeExport.Guidance(print, made);
            double mm = size.Success ? double.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            Check(size.Success && Math.Abs(mm - guide.ModuleMm * made.Width) < 0.001 && Math.Abs(guide.ModuleMm - guide.ModulePx * 25.4 / print.Dpi) < 1e-9 && mm <= 40,
                "a print size in the SVG: " + printed.Substring(0, Math.Min(200, printed.Length)));

            // the PDF with a print size: the page is that size, its picture whole pixels a module at its DPI
            CheckPdf(BarcodeExport.Pdf(art, look, print, "test"), art, art.W * guide.ModulePx * 72.0 / print.Dpi, art.H * guide.ModulePx * 72.0 / print.Dpi, guide.ModulePx, "with a print size");
            // without: the PNG's size at 96 px an inch, drawn sharper (whole pixels a module near 300 DPI, at most 4096 px wide)
            print.On = false;
            int sharper = Math.Max(3, Math.Min((int)Math.Ceiling(3 * 300 / 96.0), 4096 / art.W));
            CheckPdf(BarcodeExport.Pdf(art, look, print, "test"), art, art.W * 3 * 0.75, art.H * 3 * 0.75, sharper, "at the picture's size");
        }

        // one of our barcode PDFs: its page size in points, its picture's size and colours, and that the picture reads back
        static void CheckPdf(byte[] pdf, BarcodeArt art, double pageWidth, double pageHeight, int modulePx, string what)
        {
            string text = Encoding.GetEncoding(28591).GetString(pdf);   // one character a byte
            Match page = Regex.Match(text, @"/MediaBox \[0 0 ([0-9.]+) ([0-9.]+)\]");
            Match image = Regex.Match(text, @"/Subtype /Image /Width ([0-9]+) /Height ([0-9]+) /ColorSpace /DeviceRGB [^>]*/Length ([0-9]+) >>\nstream\n");
            Check(text.StartsWith("%PDF-", StringComparison.Ordinal) && page.Success && image.Success, "the PDF " + what + " has no page size or picture");
            double Number(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);
            Check(Math.Abs(Number(page.Groups[1]) - pageWidth) < 0.01 && Math.Abs(Number(page.Groups[2]) - pageHeight) < 0.01,
                "the PDF " + what + " is " + page.Groups[1].Value + " × " + page.Groups[2].Value + " pt, not " + pageWidth + " × " + pageHeight);
            int w = int.Parse(image.Groups[1].Value), h = int.Parse(image.Groups[2].Value);
            Check(w == art.W * modulePx && h == art.H * modulePx, "the PDF's picture " + what + " is " + w + " × " + h + ", not " + modulePx + " px a module");
            byte[] rgb;
            int start = image.Index + image.Length, length = int.Parse(image.Groups[3].Value);
            using (var input = new MemoryStream(pdf, start + 2, length - 2))   // past zlib's two-byte header
            using (var inflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                inflate.CopyTo(output);
                rgb = output.ToArray();
            }
            var bgra = new byte[w * h * 4];
            var grays = new byte[w * h];
            for (int i = 0; i < w * h; i++)
            {
                bgra[4 * i] = rgb[3 * i + 2];
                bgra[4 * i + 1] = rgb[3 * i + 1];
                bgra[4 * i + 2] = rgb[3 * i];
                bgra[4 * i + 3] = 255;
                grays[i] = (byte)((rgb[3 * i] * 299 + rgb[3 * i + 1] * 587 + rgb[3 * i + 2] * 114) / 1000);
            }
            Check(Paints(bgra, 0, 0xfe, 0xdc, 0xba) && Paints(bgra, FirstInk(bgra, w, h / 3, 0xfe), 0x12, 0x34, 0x56), "the PDF's picture " + what + ": its colours");
            Check(BarcodeExport.Read(grays, w, h, BarcodeFormats.Get(BarcodeType.Ean13)) == "4006381333931", "the PDF's picture " + what + " doesn't read back");
        }

        // a pixel of a BGRA picture is this colour
        static bool Paints(byte[] bgra, int pixel, byte r, byte g, byte b) => pixel >= 0 && bgra[4 * pixel + 2] == r && bgra[4 * pixel + 1] == g && bgra[4 * pixel] == b;

        // the first pixel along a row whose red isn't the background's
        static int FirstInk(byte[] bgra, int width, int row, byte paperRed)
        {
            for (int x = 0; x < width; x++)
                if (bgra[4 * (row * width + x) + 2] != paperRed) return row * width + x;
            return -1;
        }

        // A scanned barcode made again: its type and text, exactly as read; back to QR codes after
        static void BarcodeMadeAgain()
        {
            MainWindow window = NewMain();
            var scanned = new FoundCode { Text = "B0R3D\r\nTWO LINES", Kind = "Text", Format = "Code 128", CanMakeAgain = true, Barcode = BarcodeType.Code128 };
            window.MakeAgain(scanned);
            WaitForCheck(window);
            Check(window.MakingBarcode && window.CodeType == BarcodeType.Code128 && window.BarcodeExpected == scanned.Text, "made again as " + window.CodeType + ": " + Show(window.BarcodeExpected));
            Check(window.CheckResult == "ok", "the check says " + window.CheckResult);
            TypeBarcode(window, "typed\r\nhere");
            Check(window.BarcodeExpected == "typed\nhere", "typed text should get plain line breaks, as on the website");

            var gs1 = new FoundCode { Text = "(01)09506000134352(10)ABC", Kind = "Text", Format = "GS1-128", CanMakeAgain = true, Barcode = BarcodeType.Gs1_128 };
            window.MakeAgain(gs1);
            WaitForCheck(window);
            Check(window.CodeType == BarcodeType.Gs1_128 && window.BarcodeDataBox.Text == gs1.Text && window.CheckResult == "ok", "GS1-128 made again: " + window.CheckResult);

            var qr = new FoundCode { Text = "https://b0r3d.org/again", Kind = "Link", Format = "QR code", CanMakeAgain = true };
            window.MakeAgain(qr);
            WaitForCheck(window);
            Check(!window.MakingBarcode && window.Code?.Text == qr.Text, "a QR code after a barcode should be made as a QR code");
        }

        // the barcode settings kept for next time; Recent codes keep a barcode's type, text and settings
        static void BarcodeSettingsKept()
        {
            string data = DataFolder.Path;
            string folder = Path.Combine(Work, "barcode-settings");
            DataFolder.Reset(folder);
            try
            {
                var s = new Settings();
                s.BarcodeLook = new BarcodeLook { Color = "#112233", Background = "#ddeeff", Transparent = true, Scale = 7, ShowText = false, AllowRectangular = true, QuietZones = false, BarHeight = 85 };
                s.BarcodePrint = new PrintSize { On = true, Width = 2.5, Unit = "in", Dpi = 600 };
                Check(s.Save(), "couldn't save: " + DataFolder.WhyNotKept);
                Settings back = Settings.Load();
                BarcodeLook l = back.BarcodeLook;
                Check(l.Color == "#112233" && l.Background == "#ddeeff" && l.Transparent && l.Scale == 7 && !l.ShowText && l.AllowRectangular && !l.QuietZones && l.BarHeight == 85,
                    "the barcode look read back: " + back.ToText());
                Check(back.BarcodePrint.On && back.BarcodePrint.Width == 2.5 && back.BarcodePrint.Unit == "in" && back.BarcodePrint.Dpi == 600, "the barcode print size read back: " + back.ToText());
                File.WriteAllText(Path.Combine(folder, Settings.FileName), "barcode_colour=reddish\nbarcode_scale=99\nbarcode_bar_height=3\nbarcode_text=maybe\n");
                Settings odd = Settings.Load();
                Check(odd.BarcodeLook.Color == "#000000" && odd.BarcodeLook.Scale == BarcodeLook.MaxScale && odd.BarcodeLook.BarHeight == Barcode.MinBarHeight && odd.BarcodeLook.ShowText,
                    "garbled barcode settings should be the defaults or brought in: " + odd.ToText());
                // the colours the colour boxes write: see-through ones too
                var seeThrough = new Settings();
                seeThrough.BarcodeLook = new BarcodeLook { Color = "#00000080", Background = "transparent" };
                Check(seeThrough.Save() && Settings.Load().BarcodeLook.Color == "#00000080" && Settings.Load().BarcodeLook.Background == "transparent", "see-through colours read back as " + Settings.Load().ToText());

                // through the window: set there, kept on closing, back in a new window
                var keeping = new Settings();
                var first = new MainWindow(keeping);
                LayOut(first.Root, MainWidth, MainHeight);
                first.ChooseCodeType(BarcodeType.Code128, "B0R3D");
                first.BarHeightBox.Value = 80;
                first.ShowTextCheck.IsChecked = false;
                first.SaveSettings();
                var second = new MainWindow(Settings.Load());
                Check(second.BarcodeSettings.BarHeight == 80 && !second.BarcodeSettings.ShowText && second.BarHeightBox.Value == 80 && second.ShowTextCheck.IsChecked == false,
                    "the barcode settings in a new window: height " + second.BarcodeSettings.BarHeight + ", text " + second.BarcodeSettings.ShowText);

                var window = new MainWindow(new Settings());
                LayOut(window.Root, MainWidth, MainHeight);
                window.ChooseCodeType(BarcodeType.Code39, "B0R3D");
                var look = new BarcodeLook { Color = "#440000", Scale = 2, BarHeight = 70 };
                Check(RecentCodes.Add(RecentCodes.NewBarcode(BarcodeType.Code39, "B0R3D", look, 1700000000000)) > 0, "the barcode wasn't kept");
                RecentCode kept = RecentCodes.Load().First();
                Check(kept.Barcode == BarcodeType.Code39 && kept.BarcodeText == "B0R3D" && kept.BarcodeLook.Color == "#440000" && kept.BarcodeLook.BarHeight == 70 && kept.Name.StartsWith("Code 39", StringComparison.Ordinal),
                    "the recent barcode read back as " + kept.Barcode + " " + Show(kept.BarcodeText) + ", " + kept.Name);
                window.ChooseCodeType(null);
                Check(window.OpenRecent(kept.Id), "Open didn't open the barcode");
                WaitForCheck(window);
                Check(window.CodeType == BarcodeType.Code39 && window.BarcodeDataBox.Text == "B0R3D" && window.BarcodeSettings.Color == "#440000" && window.BarcodeSettings.BarHeight == 70 && window.CheckResult == "ok",
                    "the recent barcode opened as " + window.CodeType + " " + Show(window.BarcodeDataBox.Text));
                var list = new RecentWindow(new Settings(), id => true);
                LayOut(list.Root, 600, 660);
                Check(((Border)((DockPanel)list.CodesList.Children[0]).Children[0]).Child is Image, "a recent barcode has a picture in the list");
                list.Close();

                // a scanned barcode made again, kept (as Save, Copy and drag keep it) and opened: its text exactly
                var scanned = new FoundCode { Text = "B0R3D\r\nTWO", Kind = "Text", Format = "Code 128", CanMakeAgain = true, Barcode = BarcodeType.Code128 };
                window.MakeAgain(scanned);
                WaitForCheck(window);
                window.RememberShown()();
                RecentCode madeAgain = RecentCodes.Load().First();
                Check(madeAgain.Barcode == BarcodeType.Code128 && madeAgain.BarcodeText == scanned.Text, "the made-again barcode was kept as " + Show(madeAgain.BarcodeText));
                window.ChooseCodeType(null);
                Check(window.OpenRecent(madeAgain.Id), "Open didn't open the made-again barcode");
                WaitForCheck(window);
                Check(window.BarcodeExpected == scanned.Text && window.CheckResult == "ok", "the made-again barcode opened as " + Show(window.BarcodeExpected));
                window.RememberShown()();
                Check(RecentCodes.Load().Count(c => c.Barcode == BarcodeType.Code128) == 1, "keeping it again should replace its entry, not add one");
            }
            finally
            {
                DataFolder.Reset(data);
            }
        }
    }
}
