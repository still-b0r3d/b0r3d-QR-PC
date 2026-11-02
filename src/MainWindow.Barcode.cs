using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using B0r3dQR.Barcodes;
using B0r3dQR.Design;
using B0r3dQR.Qr;

namespace B0r3dQR
{
    // The Make tab with one of the website's other barcode types (its src/components/BarcodeCreate.vue):
    // the Code type list, what's typed for each type, the barcode's settings, the barcode drawn with its
    // scan check and "What's in this code", and its files.
    public partial class MainWindow
    {
        BarcodeType? codeType;          // null: a QR code
        // what's typed for each type, kept while b0r3d QR runs; each starts with the website's example
        readonly Dictionary<BarcodeType, string> barcodeTexts = new Dictionary<BarcodeType, string>();
        BarcodeLook look;               // the barcode settings, kept apart from QR designs as the website keeps them
        PrintSize barcodePrint;
        BarcodeArt barcodeArt;          // the barcode drawn, or null
        string barcodePrepared, barcodeExpected;   // what was encoded (before any check digit), and what a scanner should read
        string barcodeMadeAgain;        // a scanned barcode given by "Make this code again", exactly as it was read
        DispatcherTimer barcodeTyping;

        internal bool MakingBarcode => codeType != null;

        // for the tests
        internal BarcodeType? CodeType => codeType;
        internal BarcodeArt BarcodeShown => barcodeArt;
        internal string BarcodeExpected => barcodeExpected;
        internal BarcodeLook BarcodeSettings => look;
        internal PrintSize BarcodePrintSettings => barcodePrint;

        void SetUpBarcodes()
        {
            look = settings.BarcodeLook.Clone();
            barcodePrint = settings.BarcodePrint.Clone();
            barcodeTyping = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };   // the website waits a moment after typing
            barcodeTyping.Tick += (s, e) => RefreshBarcode();

            showing = true;
            // QR first, then the website's groups, as its Code type list has them (the items' alignment set here:
            // WPF's own style looks it up from the list before they're in it, a binding error for each)
            ComboBoxItem Item(string content) => new ComboBoxItem { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
            var items = new List<ComboBoxItem> { Item("QR code") };
            foreach (string group in BarcodeFormats.Groups)
            {
                ComboBoxItem heading = Item(group);
                heading.IsEnabled = false;
                heading.FontWeight = FontWeights.SemiBold;
                items.Add(heading);
                foreach (BarcodeFormat f in BarcodeFormats.All.Where(f => f.Group == group))
                {
                    ComboBoxItem item = Item(f.Label);
                    item.Tag = f.Type;
                    item.Padding = new Thickness(18, 3, 6, 3);
                    items.Add(item);
                }
            }
            CodeTypeBox.ItemsSource = items;
            CodeTypeBox.SelectedIndex = 0;
            BarcodePrintUnitBox.ItemsSource = new[] { "mm", "in" };
            BarcodePrintDpiBox.ItemsSource = PrintSize.Resolutions.Select(d => d + " DPI").ToList();
            ShowBarcodeSettings();
            showing = false;
        }

        // the settings' controls from `look` and the print size
        void ShowBarcodeSettings()
        {
            bool was = showing;
            showing = true;
            ShowTextCheck.IsChecked = look.ShowText;
            QuietZonesCheck.IsChecked = look.QuietZones;
            QuietZonesNote.Visibility = look.QuietZones ? Visibility.Collapsed : Visibility.Visible;
            BarHeightBox.Value = look.BarHeight;
            RectangularCheck.IsChecked = look.AllowRectangular;
            BarsColorBox.Value = look.Color;
            BarcodeBackgroundBox.Value = look.Background;
            BarcodeBackgroundBox.IsEnabled = !look.Transparent;
            TransparentCheck.IsChecked = look.Transparent;
            ScaleBox.Value = look.Scale;
            BarcodePrintCheck.IsChecked = barcodePrint.On;
            BarcodePrintPanel.Visibility = barcodePrint.On ? Visibility.Visible : Visibility.Collapsed;
            BarcodePrintWidthBox.Decimals = barcodePrint.Unit == "in" ? 3 : 2;
            BarcodePrintWidthBox.Value = barcodePrint.Width;
            BarcodePrintUnitBox.SelectedItem = barcodePrint.Unit;
            BarcodePrintDpiBox.SelectedIndex = Array.IndexOf(PrintSize.Resolutions, barcodePrint.Dpi);
            showing = was;
        }

        void OnCodeType(object sender, SelectionChangedEventArgs e)
        {
            if (Quiet || !(CodeTypeBox.SelectedItem is ComboBoxItem item)) return;
            if (!item.IsEnabled)
            {
                // a group's heading, reached by typing its first letter (WPF's search doesn't skip it): no choice
                showing = true;
                CodeTypeBox.SelectedItem = ((IEnumerable<ComboBoxItem>)CodeTypeBox.ItemsSource).First(i => Equals(i.Tag as BarcodeType?, codeType));
                showing = false;
                return;
            }
            ChooseCodeType(item.Tag as BarcodeType?);
        }

        // QR codes, or a barcode type with what was last typed for it (the website's example the first time)
        internal void ChooseCodeType(BarcodeType? type, string text = null)
        {
            if (!MakingBarcode) CatchUp();
            barcodeTyping.Stop();
            codeType = type;
            showing = true;
            CodeTypeBox.SelectedItem = ((IEnumerable<ComboBoxItem>)CodeTypeBox.ItemsSource).First(i => Equals(i.Tag as BarcodeType?, type));
            showing = false;
            QrPanel.Visibility = type == null ? Visibility.Visible : Visibility.Collapsed;
            BarcodePanel.Visibility = type == null ? Visibility.Collapsed : Visibility.Visible;
            Preview.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, type == null ? "The QR code" : BarcodeFormats.Get(type.Value).Label + " barcode");
            if (type == null)
            {
                barcodeArt = null;
                Refresh();
                return;
            }
            BarcodeFormat format = BarcodeFormats.Get(type.Value);
            if (text != null) barcodeTexts[type.Value] = text;
            showing = true;
            BarcodeDataBox.Text = barcodeTexts.TryGetValue(type.Value, out string kept) ? kept : format.Example;
            BarcodeDataBox.FontFamily = new FontFamily(format.Linear && !format.Gs1 ? "Consolas" : "Segoe UI");
            showing = false;
            BarcodeHint.Text = format.Hint;
            LinearOptions.Visibility = format.Linear ? Visibility.Visible : Visibility.Collapsed;
            QuietZonesRow.Visibility = format.KeepsQuietZones ? Visibility.Collapsed : Visibility.Visible;
            RectangularCheck.Visibility = type == BarcodeType.DataMatrix || type == BarcodeType.Gs1DataMatrix ? Visibility.Visible : Visibility.Collapsed;
            RefreshBarcode();
        }

        void OnBarcodeDataChanged(object sender, TextChangedEventArgs e)
        {
            if (Quiet || codeType == null) return;
            barcodeTexts[codeType.Value] = BarcodeDataBox.Text;
            barcodeTyping.Stop();
            barcodeTyping.Start();
        }

        // what's typed, with Windows line breaks made plain as the website's text box gives them; a scanned
        // barcode stays exactly as it was read until it's changed
        string BarcodeText => barcodeMadeAgain != null && BarcodeDataBox.Text == barcodeMadeAgain ? barcodeMadeAgain : BarcodeDataBox.Text.Replace("\r\n", "\n");

        // makes the barcode again from the settings, and shows it with everything said about it
        internal void RefreshBarcode()
        {
            barcodeTyping.Stop();
            ticket++;
            art = null;
            BarcodeType type = codeType.Value;
            BarcodeFormat format = BarcodeFormats.Get(type);
            string text = BarcodeText;
            BarcodeSvg made = Barcodes.Barcode.Make(text, type, look.Options, out string error);
            barcodeArt = made == null ? null : new BarcodeArt(made);
            string said = BarcodeError.Text;
            BarcodeError.Text = error ?? "";
            BarcodeError.Visibility = error != null ? Visibility.Visible : Visibility.Collapsed;
            if (error != null && error != said) Say(BarcodeError);
            if (barcodeArt != null)
            {
                barcodePrepared = format.Prepare(text);
                barcodeExpected = format.ExpectedText(barcodePrepared);
                StageNote.Visibility = Visibility.Collapsed;
                Preview.Visibility = Visibility.Visible;
                RenderBarcodePreview();
                ShowBarcodeInfo();
                StartBarcodeCheck();
            }
            else
            {
                barcodePrepared = barcodeExpected = null;
                ShowStage("Fix the data to see the barcode.", false);
                Preview.Source = null;
                Preview.Visibility = Visibility.Collapsed;
                CheckResult = "";
                CheckLine.Text = "";
                WarnLine.Visibility = Visibility.Collapsed;
                InfoLine.Text = "";
                InCodeRows.Children.Clear();
                InCodeText.Text = "";
                InCodeText.Visibility = Visibility.Collapsed;
                InCodeFooter.Text = "";
            }
            ShowScaleHint();
            ShowBarcodePrint();
            CommandManager.InvalidateRequerySuggested();
        }

        // The preview: whole screen pixels per module when it fits, so the bars are sharp; a barcode wider
        // than the room is drawn at one pixel per module and shown smaller.
        void RenderBarcodePreview()
        {
            if (barcodeArt == null) return;
            const double margin = 20;
            double roomW = Stage.ActualWidth - 2 * margin, roomH = Stage.ActualHeight - 2 * margin;
            if (roomW <= 0 || roomH <= 0)
            {
                Preview.Source = null;   // not laid out yet: the stage's SizeChanged brings it back
                return;
            }
            double scale = Scale;
            double fit = Math.Min(roomW * scale / barcodeArt.W, roomH * scale / barcodeArt.H);
            BitmapSource picture = barcodeArt.Render(Math.Max(1, Math.Floor(fit)), Css.Paint(look.Color), look.Transparent ? (Color?)null : Css.Paint(look.Background));
            double shrink = fit < 1 ? fit : 1;
            Preview.Source = picture;
            Preview.Width = picture.PixelWidth * shrink / scale;
            Preview.Height = picture.PixelHeight * shrink / scale;
        }

        // The scan check: the barcode drawn as a PNG is (4 pixels per module, on white), read back off the
        // window's thread with the type's reader, as the website does
        void StartBarcodeCheck()
        {
            int mine = ticket;
            string expected = barcodeExpected;
            BarcodeFormat format = BarcodeFormats.Get(codeType.Value);
            ShowCheck("checking", "Checking that it scans…", "Dim");
            BitmapSource picture = BarcodeExport.CheckPicture(barcodeArt, look);
            byte[] grays = ScanCheck.Grays(picture, out int width, out int height);
            byte[] roomy = ScanCheck.Grays(BarcodeExport.CheckPicture(barcodeArt, look, BarcodeExport.CheckRoom), out int roomyWidth, out int roomyHeight);
            Task.Run(() => BarcodeExport.Read(grays, width, height, format) ?? BarcodeExport.Read(roomy, roomyWidth, roomyHeight, format))
                .ContinueWith(done => Dispatcher.BeginInvoke(new Action(() => ShowBarcodeCheck(mine, expected, done))));
        }

        void ShowBarcodeCheck(int mine, string expected, Task<string> done)
        {
            if (mine != ticket) return;   // the barcode has changed since
            if (done.IsFaulted) ShowCheck("error", "Couldn't run the scan check.", "Bad");
            else if (done.Result == null) ShowCheck("unreadable", "✗ Didn't scan in a test decode. Try more contrast or a larger size.", "Bad");
            else if (done.Result != expected) ShowCheck("mismatch", "✗ Scans, but reads as different text: " + done.Result, "Bad");
            else ShowCheck("ok", "✓ Scans. A test decode matches your data exactly.", "Good");
        }

        // the warnings, and "What's in this code" as the website lists it for a barcode
        void ShowBarcodeInfo()
        {
            InfoLine.Text = "";
            List<string> warnings = BarcodeExport.Warnings(look);
            WarnLine.Text = string.Join("\n", warnings.Select(w => "⚠ " + w));
            WarnLine.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BarcodeFormat format = BarcodeFormats.Get(codeType.Value);
            var rows = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Type", format.Label + (format.Gs1 ? " (GS1 data)" : "")),
                new KeyValuePair<string, string>("Stored", barcodeExpected),
            };
            // the check digit added: what's stored after what was typed, or where the type writes the number anew
            // (an ISBN-10 as its EAN-13, a 6-digit UPC-E with its number system first) the last digit. The website
            // cuts the typed length off there too, and shows "157" for an ISBN-10.
            if (barcodeExpected != barcodePrepared)
                rows.Add(new KeyValuePair<string, string>("Check digit", (barcodeExpected.StartsWith(barcodePrepared, StringComparison.Ordinal)
                    ? barcodeExpected.Substring(barcodePrepared.Length) : barcodeExpected.Substring(barcodeExpected.Length - 1)) + ", added for you"));
            rows.Add(new KeyValuePair<string, string>("Size", barcodeArt.W + " × " + barcodeArt.H + " modules, blank margin included"));
            ShowInCode(rows, "Stored");
            InCodeText.Text = "";
            InCodeText.Visibility = Visibility.Collapsed;
            InCodeFooter.Text = "";
        }

        void ShowScaleHint() =>
            ScaleHint.Text = barcodeArt != null
                ? "Downloads are " + barcodeArt.W * look.Scale + " × " + barcodeArt.H * look.Scale + " px. Use Size for print for a physical size."
                : "A module is the narrowest bar or square.";

        void ShowBarcodePrint()
        {
            BarcodePrintWarn.Visibility = Visibility.Collapsed;
            if (!barcodePrint.On) return;
            if (barcodePrint.Problem != null)
            {
                BarcodePrintHint.Text = barcodePrint.Problem + " Files use Size until then.";
                return;
            }
            if (barcodeArt == null)
            {
                BarcodePrintHint.Text = barcodePrint.PixelWidth.ToString("N0", CultureInfo.InvariantCulture) + " px across. Applies to every saved picture, SVG and PDF, and to copies.";
                return;
            }
            BarcodePrintHint.Text = BarcodeExport.PrintLine(barcodeArt.Barcode, barcodePrint, out string warning) + " Applies to every saved picture, SVG and PDF, and to copies.";
            if (warning != null)
            {
                BarcodePrintWarn.Text = "⚠ " + warning;
                BarcodePrintWarn.Visibility = Visibility.Visible;
            }
        }

        // ---------- the settings ----------

        void OnBarcodeOption(object sender, RoutedEventArgs e)
        {
            if (Quiet || look == null) return;
            look.ShowText = ShowTextCheck.IsChecked == true;
            look.QuietZones = QuietZonesCheck.IsChecked == true;
            look.AllowRectangular = RectangularCheck.IsChecked == true;
            look.Transparent = TransparentCheck.IsChecked == true;
            QuietZonesNote.Visibility = look.QuietZones ? Visibility.Collapsed : Visibility.Visible;
            BarcodeBackgroundBox.IsEnabled = !look.Transparent;
            if (MakingBarcode) RefreshBarcode();
        }

        void OnBarHeight(object sender, EventArgs e)
        {
            if (Quiet || look == null) return;
            look.BarHeight = WebsiteWhole(BarHeightBox.Value, Barcodes.Barcode.DefaultBarHeight, Barcodes.Barcode.MinBarHeight, Barcodes.Barcode.MaxBarHeight);
            if (MakingBarcode) RefreshBarcode();
        }

        void OnBarcodeColour(object sender, EventArgs e)
        {
            if (Quiet || look == null) return;
            look.Color = BarsColorBox.Value;
            look.Background = BarcodeBackgroundBox.Value;
            if (MakingBarcode) RefreshBarcode();
        }

        void OnBarcodeScale(object sender, EventArgs e)
        {
            if (Quiet || look == null) return;
            look.Scale = WebsiteWhole(ScaleBox.Value, 4, BarcodeLook.MinScale, BarcodeLook.MaxScale);
            ShowScaleHint();
        }

        // a number box's value as the website takes it: Math.round (halves up), the default for 0, then the limits
        static int WebsiteWhole(double value, int fallback, int min, int max)
        {
            double whole = value == 0 || double.IsNaN(value) ? fallback : Math.Floor(value + 0.5);
            return (int)Math.Max(min, Math.Min(max, whole));
        }

        void OnBarcodePrintOn(object sender, RoutedEventArgs e)
        {
            if (Quiet || barcodePrint == null) return;
            barcodePrint.On = BarcodePrintCheck.IsChecked == true;
            BarcodePrintPanel.Visibility = barcodePrint.On ? Visibility.Visible : Visibility.Collapsed;
            ShowBarcodePrint();
        }

        void OnBarcodePrint(object sender, EventArgs e)
        {
            if (Quiet || barcodePrint == null) return;
            barcodePrint.Width = BarcodePrintWidthBox.Value;
            if (BarcodePrintDpiBox.SelectedIndex >= 0) barcodePrint.Dpi = PrintSize.Resolutions[BarcodePrintDpiBox.SelectedIndex];
            ShowBarcodePrint();
        }

        // a new unit keeps the same length on paper
        void OnBarcodePrintUnit(object sender, SelectionChangedEventArgs e)
        {
            if (Quiet || barcodePrint == null || !(BarcodePrintUnitBox.SelectedItem is string unit) || unit == barcodePrint.Unit) return;
            double millimetres = barcodePrint.Millimetres;
            barcodePrint.Unit = unit;
            barcodePrint.Width = unit == "in" ? millimetres / 25.4 : millimetres;
            showing = true;
            BarcodePrintWidthBox.Decimals = unit == "in" ? 3 : 2;
            BarcodePrintWidthBox.Value = barcodePrint.Width;
            showing = false;
            ShowBarcodePrint();
        }

        // ---------- the files ----------

        // the website's file name for a barcode: its type
        string BarcodeFileName => BarcodeFormats.Get(codeType.Value).Id;

        // the files are made from what's shown when they're asked for, whatever changes while the dialog is open
        void SaveBarcodePicture(string title, string typeName, string extension, bool jpg)
        {
            BarcodeArt these = barcodeArt;
            BarcodeLook kept = look.Clone();
            PrintSize printed = barcodePrint.Clone();
            Action remember = RememberShown();
            string path = AskWhere(title, typeName, extension);
            if (path == null) return;
            bool saved = Save(path, () =>
            {
                BitmapSource picture = BarcodeExport.Picture(these, kept, printed, jpg);
                return jpg ? QrExport.Jpg(picture, 95) : QrExport.Png(picture);   // the website asks for 0.95 for barcodes
            });
            if (saved) remember();
        }

        void SaveBarcodeSvg()
        {
            BarcodeArt these = barcodeArt;
            BarcodeLook kept = look.Clone();
            PrintSize printed = barcodePrint.Clone();
            Action remember = RememberShown();
            string path = AskWhere("Save the barcode as an SVG drawing", "SVG drawing", ".svg");
            if (path != null && Save(path, () => new UTF8Encoding(false).GetBytes(BarcodeExport.Svg(these.Barcode, kept, printed)))) remember();
        }

        void SaveBarcodePdf()
        {
            BarcodeArt these = barcodeArt;
            BarcodeLook kept = look.Clone();
            PrintSize printed = barcodePrint.Clone();
            Action remember = RememberShown();
            string path = AskWhere("Save the barcode as a PDF", "PDF document", ".pdf");
            if (path != null && Save(path, () => BarcodeExport.Pdf(these, kept, printed, Path.GetFileNameWithoutExtension(path)))) remember();
        }

        // a barcode from Recent codes: its type, text and settings back, as the website opens one
        void OpenRecentBarcode(RecentCode code)
        {
            look = code.BarcodeLook.Clone();
            ShowBarcodeSettings();
            barcodeMadeAgain = code.BarcodeText;   // its text exactly as it was kept, Windows line breaks too
            Tabs.SelectedItem = MakeTab;
            ChooseCodeType(code.Barcode.Value, code.BarcodeText);
        }

        // a scanned barcode made again: its type, and its text as the type takes it
        internal void MakeBarcodeAgain(BarcodeType type, string text)
        {
            text = BarcodeFormats.Get(type).NormalizeRead(text);
            Tabs.SelectedItem = MakeTab;
            barcodeMadeAgain = text;
            ChooseCodeType(type, text);
        }
    }
}
