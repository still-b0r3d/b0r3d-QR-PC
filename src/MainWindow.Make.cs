using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Barcodes;
using B0r3dQR.Design;
using B0r3dQR.Qr;
using B0r3dQR.Scan;

namespace B0r3dQR
{
    // The Make tab: the design's settings on the left in the website's sections, the code drawn on
    // the right as the website exports it, with the scan check, the warnings and "What's in this
    // code", and the code's files.
    public partial class MainWindow
    {
        // the design kept between runs (never its text), in the data folder
        public const string KeptDesignFile = "b0r3dQR design.json";

        QrDesign design = new QrDesign();
        QrArt art;                 // the design drawn, or null while there's nothing to show
        PrintSize print = new PrintSize();
        bool wholePixels;
        QrFrame lastFrame;         // brought back when "Add a frame" is ticked again
        string lastBackground = "#ffffff";
        bool showing;              // the controls are being set from the design: their events change nothing
        bool Quiet => showing || !ready;   // ...nor while the window is still being built
        int ticket;                // goes up with every new drawing; a check of an older one is ignored
        Point dragStart;
        bool dragArmed;
        readonly Random random = new Random();
        // what's typed in each data template's form, kept while b0r3d QR runs (as the website keeps it)
        readonly Dictionary<TemplateKind, Dictionary<string, string>> templateForms = new Dictionary<TemplateKind, Dictionary<string, string>>();
        int templateAtOpen = -1;   // the template the list was on when it opened
        bool templateChosen;       // an item in the open list was clicked or chosen with Enter
        int noteCode;              // the code the note about Recent codes is about

        // for the tests
        internal string CheckResult { get; private set; } = "";
        internal QrCode Code => art?.Code;
        internal QrArt Art => art;
        internal QrDesign CurrentDesign => design;
        internal bool HasCode => MakingBarcode ? barcodeArt != null : art != null;
        internal int Refreshes => ticket;
        internal PrintSize PrintSettings => print;

        sealed class FontChoice
        {
            public string Label, Value;   // Value: the CSS family list the design keeps (null: Default)
            public override string ToString() => Label;
        }

        internal static readonly (string Label, string Value)[] WebsiteFonts =
        {
            ("Arial", "Arial, sans-serif"), ("Verdana", "Verdana, sans-serif"), ("Roboto", "'Roboto', sans-serif"), ("Inter", "'Inter', sans-serif"),
            ("Open Sans", "'Open Sans', sans-serif"), ("Lato", "'Lato', sans-serif"), ("Montserrat", "'Montserrat', sans-serif"), ("Poppins", "'Poppins', sans-serif"),
            ("Oswald", "'Oswald', sans-serif"), ("Raleway", "'Raleway', sans-serif"), ("Nunito", "'Nunito', sans-serif"), ("Georgia", "Georgia, serif"),
            ("Times New Roman", "'Times New Roman', serif"), ("Playfair Display", "'Playfair Display', serif"), ("Merriweather", "'Merriweather', serif"),
            ("Courier New", "'Courier New', monospace"), ("JetBrains Mono", "'JetBrains Mono', monospace"), ("Fira Code", "'Fira Code', monospace"),
            ("Source Code Pro", "'Source Code Pro', monospace"), ("IBM Plex Mono", "'IBM Plex Mono', monospace"), ("Inconsolata", "'Inconsolata', monospace"),
            ("Pacifico", "'Pacifico', cursive"), ("Bebas Neue", "'Bebas Neue', sans-serif"),
        };

        // a caption font from a batch CSV, as the website reads it: one of its fonts by name or by its CSS, else as written;
        // "" for its first choice, Default (the frame's own font)
        internal static string WebsiteFontValue(string font)
        {
            if (font == "Default") return "";
            foreach (var (label, value) in WebsiteFonts)
                if (value == font || label == font) return value;
            return font;
        }

        readonly List<FontChoice> fonts = new List<FontChoice>();

        void SetUpMake()
        {
            showing = true;
            PresetBox.ItemsSource = Presets.Names;
            DotsTypeBox.ItemsSource = Presets.DotTypes;
            SquaresTypeBox.ItemsSource = Presets.CornerSquareTypes;
            CentresTypeBox.ItemsSource = Presets.CornerDotTypes;
            FramePresetBox.ItemsSource = Presets.FrameNames;
            TemplateBox.ItemsSource = TemplateForms.Menu.Select(TemplateForms.Label).ToList();
            TemplateBox.PreviewKeyDown += (s, e) => { if (TemplateBox.IsDropDownOpen && e.Key == Key.Enter) templateChosen = true; };
            TemplateBox.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, e) =>
            {
                if (TemplateBox.IsDropDownOpen && ItemsControl.ContainerFromElement(TemplateBox, e.OriginalSource as DependencyObject) is ComboBoxItem)
                    templateChosen = true;
            }), true);
            var versions = new List<string> { "Auto (smallest that fits)" };
            for (int v = 1; v <= 40; v++) versions.Add(v + " (" + (17 + 4 * v) + " × " + (17 + 4 * v) + ")");
            VersionBox.ItemsSource = versions;
            PrintUnitBox.ItemsSource = new[] { "mm", "in" };
            PrintDpiBox.ItemsSource = PrintSize.Resolutions.Select(d => d + " DPI").ToList();
            fonts.Add(new FontChoice { Label = "Default", Value = null });
            foreach (var (label, value) in WebsiteFonts)   // the website's web fonts are inside b0r3d QR
                fonts.Add(new FontChoice { Label = label + (WebFonts.Named(label) != null || QrArt.Installed(label) ? "" : " (not on this PC)"), Value = value });
            foreach (string family in Fonts.SystemFontFamilies.Select(f => f.Source).Where(f => !WebsiteFonts.Any(w => w.Label == f)).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
                fonts.Add(new FontChoice { Label = LongText.Cap(family), Value = "'" + family + "', sans-serif" });
            FontBox.ItemsSource = fonts;
            CheckLine.ToolTip = "Every change is drawn the way it's saved and read back with a scanner (ZXing), the way a phone camera would read it.";
            InCodeFooter.Text = Checks.EverythingLine;
            showing = false;

            print = settings.Print.Clone();
            wholePixels = settings.WholePixels;
            design = KeptDesign() ?? FirstDesign();
            if (!design.Transparent) lastBackground = design.Background;
            ShowDesign();
        }

        // the design from last time, without its text, or null
        static QrDesign KeptDesign()
        {
            try
            {
                string path = Path.Combine(DataFolder.Path, KeptDesignFile);
                if (!DataFolder.CanKeep || !File.Exists(path)) return null;
                QrDesign kept = DesignFile.Read(File.ReadAllText(path, Encoding.UTF8), new QrDesign());
                kept.Data = "";
                return kept;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is DesignFileException || e is System.Security.SecurityException)
            {
                return null;
            }
        }

        // the website's Plain preset, with the options an older b0r3d QR kept (its pictures' size too,
        // which it made at whole pixels per module)
        QrDesign FirstDesign()
        {
            QrDesign d = Presets.Apply("Plain", new QrDesign());
            d.Level = settings.Level;
            d.Margin = settings.QuietZone;
            if (settings.FromOlder) d.Width = d.Height = Math.Max(SizeBox.Minimum, Math.Min(SizeBox.Maximum, settings.PictureSize));
            return d;
        }

        void KeepDesign() => DataFolder.TryWrite(KeptDesignFile, DesignFile.Write(design, false));

        // ---------- the controls from the design ----------

        void ShowDesign()
        {
            showing = true;
            try
            {
                PresetBox.SelectedItem = Presets.Matching(design);
                DotsTypeBox.SelectedItem = Shown(design.DotsType, Presets.DotTypes, "square");
                SquaresTypeBox.SelectedItem = Shown(design.CornersSquareType, Presets.CornerSquareTypes, "extra-rounded");
                CentresTypeBox.SelectedItem = Shown(design.CornersDotType, Presets.CornerDotTypes, "square");
                DotsColorBox.Value = design.DotsColor;
                SquaresColorBox.Value = design.CornersSquareColor;
                CentresColorBox.Value = design.CornersDotColor;
                BackgroundCheck.IsChecked = !design.Transparent;
                BackgroundColorBox.Value = design.Transparent ? lastBackground : design.Background;
                BackgroundColorBox.IsEnabled = !design.Transparent;
                RadiusBox.Value = design.BorderRadius;
                LogoSizeSlider.Value = design.LogoSize ?? QrDesign.DefaultLogoSize;
                LogoSizeText.Text = (design.LogoSize ?? QrDesign.DefaultLogoSize).ToString("0.00", CultureInfo.InvariantCulture);
                LogoSpaceBox.Value = design.LogoSpace;
                LevelButton(design.Level).IsChecked = true;
                VersionBox.SelectedIndex = Math.Max(0, Math.Min(40, design.Version));
                MarkerCheck.IsChecked = design.Utf8Marker;
                QuietBox.Value = design.Margin;
                SizeBox.Value = Math.Max(design.Width, design.Height);
                WholePixelsCheck.IsChecked = wholePixels;
                PrintCheck.IsChecked = print.On;
                PrintPanel.Visibility = print.On ? Visibility.Visible : Visibility.Collapsed;
                PrintWidthBox.Decimals = WidthDecimals;
                PrintWidthBox.Value = print.Width;
                PrintUnitBox.SelectedItem = print.Unit;
                PrintDpiBox.SelectedIndex = Array.IndexOf(PrintSize.Resolutions, print.Dpi);
                ShowFrame();
            }
            finally
            {
                showing = false;
            }
        }

        static string Shown(string type, string[] types, string fallback) => types.Contains(type) ? type : fallback;

        void ShowFrame()
        {
            QrFrame f = design.Frame;
            FrameCheck.IsChecked = f != null;
            FramePanel.Visibility = f != null ? Visibility.Visible : Visibility.Collapsed;
            if (f == null) return;
            FramePresetBox.SelectedItem = Presets.FrameNames.FirstOrDefault(n => SameFrameLook(Presets.ApplyFrame(n, f), f));
            string shown = AsEdited(f.Text).Replace("\n", "\r\n");   // each line break once, whatever kind the design has
            if (CaptionBox.Text != shown && AsEdited(CaptionBox.Text) != f.Text) CaptionBox.Text = shown;
            (f.Position == "top" ? FrameTop : f.Position == "left" ? FrameLeft : f.Position == "right" ? FrameRight : FrameBottom).IsChecked = true;
            FrameWidthRow.Visibility = f.Beside ? Visibility.Visible : Visibility.Collapsed;
            FrameWidthBox.Value = f.CaptionWidth + 200;
            FrameTextColorBox.Value = f.TextColor;
            FrameBackColorBox.Value = f.BackgroundColor;
            FrameBorderColorBox.Value = f.BorderColor;
            FrameBorderBox.Text = f.BorderWidth;
            FrameRadiusBox.Text = f.BorderRadius;
            FramePaddingBox.Text = f.Padding;
            FontChoice font = fonts.FirstOrDefault(c => c.Value == f.FontFamily);
            if (font == null)
            {
                font = new FontChoice { Label = LongText.Cap("From the design: " + f.FontFamily), Value = f.FontFamily };
                fonts.Insert(1, font);
                FontBox.Items.Refresh();
            }
            FontBox.SelectedItem = font;
            FrameNoPictureButton.IsEnabled = !string.IsNullOrEmpty(f.BackgroundImage);
        }

        static bool SameFrameLook(QrFrame a, QrFrame b) =>
            a.TextColor == b.TextColor && a.BackgroundColor == b.BackgroundColor && a.BorderColor == b.BorderColor && a.BorderWidth == b.BorderWidth &&
            a.BorderRadius == b.BorderRadius && a.Padding == b.Padding && string.IsNullOrEmpty(b.FontFamily) && string.IsNullOrEmpty(b.BackgroundImage);

        // ---------- the design from the controls ----------

        void Changed()
        {
            if (!ready || showing) return;
            Refresh();
        }

        void OnPreset(object sender, SelectionChangedEventArgs e)
        {
            if (Quiet || !(PresetBox.SelectedItem is string name)) return;
            if (design.Frame != null) lastFrame = design.Frame;
            design = Presets.Apply(name, design);
            lastBackground = design.Background;
            ShowDesign();
            Changed();
        }

        void OnRandomize(object sender, RoutedEventArgs e)
        {
            design = Presets.Randomize(design, random);
            lastBackground = design.Background;
            ShowDesign();
            Changed();
        }

        void OnPartShape(object sender, SelectionChangedEventArgs e)
        {
            if (Quiet || !(((ComboBox)sender).SelectedItem is string type)) return;
            if (sender == DotsTypeBox) design.DotsType = type;
            else if (sender == SquaresTypeBox) design.CornersSquareType = type;
            else design.CornersDotType = type;
            Changed();
        }

        void OnPartColour(object sender, EventArgs e)
        {
            if (Quiet) return;
            string colour = ((Controls.ColorBox)sender).Value;
            if (sender == DotsColorBox) design.DotsColor = colour;
            else if (sender == SquaresColorBox) design.CornersSquareColor = colour;
            else if (sender == CentresColorBox) design.CornersDotColor = colour;
            else if (Css.IsTransparent(colour))
            {
                // "transparent" typed as the background's colour is the same as no background
                design.Background = "transparent";
                ShowDesign();
            }
            else
            {
                lastBackground = colour;
                if (!design.Transparent) design.Background = colour;
            }
            Changed();
        }

        void OnBackground(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            if (BackgroundCheck.IsChecked == true) design.Background = lastBackground;
            else
            {
                if (!design.Transparent) lastBackground = design.Background;
                design.Background = "transparent";
            }
            BackgroundColorBox.IsEnabled = !design.Transparent;
            Changed();
        }

        void OnRadius(object sender, EventArgs e)
        {
            if (Quiet) return;
            design.BorderRadius = RadiusBox.Value;
            Changed();
        }

        void OnLogoSize(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Quiet || LogoSizeText == null) return;
            design.LogoSize = Math.Round(LogoSizeSlider.Value, 2);
            LogoSizeText.Text = design.LogoSize.Value.ToString("0.00", CultureInfo.InvariantCulture);
            Changed();
        }

        void OnLogoSpace(object sender, EventArgs e)
        {
            if (Quiet) return;
            design.LogoSpace = LogoSpaceBox.Value;
            Changed();
        }

        void OnFrameOn(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            if (FrameCheck.IsChecked == true) design.Frame = lastFrame?.Clone() ?? Presets.ApplyFrame("Default Frame", null);
            else
            {
                lastFrame = design.Frame;
                design.Frame = null;
            }
            ShowDesign();
            Changed();
        }

        void OnFramePreset(object sender, SelectionChangedEventArgs e)
        {
            if (Quiet || !(FramePresetBox.SelectedItem is string name) || design.Frame == null) return;
            design.Frame = Presets.ApplyFrame(name, design.Frame);
            ShowDesign();
            Changed();
        }

        // the caption is drawn after a pause in the typing, like the data
        void OnCaption(object sender, TextChangedEventArgs e)
        {
            if (Quiet || design.Frame == null) return;
            design.Frame.Text = AsEdited(CaptionBox.Text);
            typingPause.Stop();
            typingPause.Start();
        }

        // A caption as a browser's text box gives it once it's edited: CR LF and a lone CR are each a
        // line break (LF). Until its box is edited the website keeps a design's caption as the file
        // has it, CRs and all, and so does b0r3d QR.
        internal static string AsEdited(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

        void OnFramePlace(object sender, RoutedEventArgs e)
        {
            if (Quiet || design.Frame == null) return;
            design.Frame.Position = (string)((RadioButton)sender).Tag;
            FrameWidthRow.Visibility = design.Frame.Beside ? Visibility.Visible : Visibility.Collapsed;
            Changed();
        }

        void OnFrameWidth(object sender, EventArgs e)
        {
            if (Quiet || design.Frame == null) return;
            design.Frame.CaptionWidth = (int)Math.Round(FrameWidthBox.Value) - 200;
            Changed();
        }

        void OnFrameColour(object sender, EventArgs e)
        {
            if (Quiet || design.Frame == null) return;
            string colour = ((Controls.ColorBox)sender).Value;
            if (sender == FrameTextColorBox) design.Frame.TextColor = colour;
            else if (sender == FrameBackColorBox) design.Frame.BackgroundColor = colour;
            else design.Frame.BorderColor = colour;
            ShowDesign();
            Changed();
        }

        void OnFrameLengthKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            OnFrameLength(sender, null);
            e.Handled = true;
        }

        // a frame length takes "16px", "0" or another CSS length, the way the website's fields do
        void OnFrameLength(object sender, RoutedEventArgs e)
        {
            if (Quiet || design.Frame == null) return;
            var box = (TextBox)sender;
            string typed = box.Text.Trim();
            string was = box == FrameBorderBox ? design.Frame.BorderWidth : box == FrameRadiusBox ? design.Frame.BorderRadius : design.Frame.Padding;
            if (typed == was) return;
            if (typed.Length > 0 && !Css.IsLength(typed))
            {
                box.Text = was;
                Flash("Give it a length the website takes too, like 16px", "Warn");
                return;
            }
            if (box == FrameBorderBox) design.Frame.BorderWidth = typed;
            else if (box == FrameRadiusBox) design.Frame.BorderRadius = typed;
            else design.Frame.Padding = typed;
            ShowDesign();
            Changed();
        }

        void OnFont(object sender, SelectionChangedEventArgs e)
        {
            if (Quiet || design.Frame == null || !(FontBox.SelectedItem is FontChoice font)) return;
            design.Frame.FontFamily = font.Value;
            Changed();
        }

        void OnLevel(object sender, RoutedEventArgs e)
        {
            if (Quiet || !ready) return;
            design.Level = LevelL.IsChecked == true ? QrLevel.L : LevelM.IsChecked == true ? QrLevel.M : LevelH.IsChecked == true ? QrLevel.H : QrLevel.Q;
            Changed();
        }

        RadioButton LevelButton(QrLevel level)
        {
            switch (level)
            {
                case QrLevel.L: return LevelL;
                case QrLevel.M: return LevelM;
                case QrLevel.H: return LevelH;
                default: return LevelQ;
            }
        }

        void OnVersion(object sender, SelectionChangedEventArgs e)
        {
            if (Quiet || VersionBox.SelectedIndex < 0) return;
            design.Version = VersionBox.SelectedIndex;
            Changed();
        }

        void OnMarker(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            design.Utf8Marker = MarkerCheck.IsChecked == true;
            Changed();
        }

        void OnQuietChanged(object sender, EventArgs e)
        {
            if (Quiet) return;
            design.Margin = QuietBox.Value;
            Changed();
        }

        void OnSizeChanged(object sender, EventArgs e)
        {
            if (Quiet) return;
            design.Width = design.Height = SizeBox.Value;
            Changed();
        }

        void OnWholePixels(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            wholePixels = WholePixelsCheck.IsChecked == true;
            ShowSize();
        }

        void OnPrint(object sender, RoutedEventArgs e)
        {
            if (Quiet) return;
            print.On = PrintCheck.IsChecked == true;
            PrintPanel.Visibility = print.On ? Visibility.Visible : Visibility.Collapsed;
            ShowSize();
            ShowPrint();
        }

        void OnPrintChanged(object sender, EventArgs e)
        {
            if (Quiet) return;
            // the box shows the width rounded: it's taken only once it's changed, so a length kept
            // exact through a change of unit stays exact
            if (PrintWidthBox.Value != Math.Round(print.Width, PrintWidthBox.Decimals)) print.Width = PrintWidthBox.Value;
            if (PrintDpiBox.SelectedIndex >= 0) print.Dpi = PrintSize.Resolutions[PrintDpiBox.SelectedIndex];
            ShowSize();
            ShowPrint();
        }

        // a change of unit keeps the length on paper, exactly (the website keeps the number)
        void OnPrintUnit(object sender, SelectionChangedEventArgs e)
        {
            if (Quiet || !(PrintUnitBox.SelectedItem is string unit) || unit == print.Unit) return;
            double millimetres = print.Millimetres;
            print.Unit = unit;
            print.Width = unit == "in" ? millimetres / 25.4 : millimetres;
            showing = true;
            PrintWidthBox.Decimals = WidthDecimals;
            PrintWidthBox.Value = print.Width;
            showing = false;
            ShowSize();
            ShowPrint();
        }

        // the width to a hundredth of a millimetre, or a thousandth of an inch as the website writes inches
        int WidthDecimals => print.Unit == "in" ? 3 : 2;

        void OnDataChanged(object sender, TextChangedEventArgs e)
        {
            if (!ready) return;
            DataHint.Visibility = DataBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            typingPause.Stop();
            typingPause.Start();
        }

        void OnStageSize(object sender, SizeChangedEventArgs e) => RenderPreview();

        // Brings what's typed and any half-typed number up to date, so a save, copy or drag never
        // uses the input from before (Ctrl+S straight after typing, a size typed but not entered).
        internal void CatchUp()
        {
            if (MakingBarcode)
            {
                foreach (Controls.NumberBox box in new[] { BarHeightBox, ScaleBox, BarcodePrintWidthBox }) box.Commit();
                foreach (Controls.ColorBox box in new[] { BarsColorBox, BarcodeBackgroundBox }) box.Commit();
                if (barcodeTyping.IsEnabled) RefreshBarcode();
                return;
            }
            foreach (Controls.NumberBox box in new[] { QuietBox, SizeBox, RadiusBox, LogoSpaceBox, FrameWidthBox, PrintWidthBox }) box.Commit();
            foreach (Controls.ColorBox box in new[] { DotsColorBox, SquaresColorBox, CentresColorBox, BackgroundColorBox, FrameTextColorBox, FrameBackColorBox, FrameBorderColorBox }) box.Commit();
            if (design.Frame != null)
                foreach (TextBox box in new[] { FrameBorderBox, FrameRadiusBox, FramePaddingBox }) OnFrameLength(box, null);
            if (typingPause.IsEnabled)
            {
                typingPause.Stop();
                Refresh();
            }
        }

        // what's typed, with Windows line breaks made plain as the website makes them; a scanned
        // code given by "Make this code again" stays exactly as it was until it's changed
        string DataText => madeAgain != null && DataBox.Text == madeAgain ? madeAgain : DataBox.Text.Replace("\r\n", "\n");

        // ---------- drawing ----------

        // draws the design again from the controls, and shows it with everything said about it
        internal void Refresh()
        {
            if (MakingBarcode)
            {
                RefreshBarcode();
                return;
            }
            ticket++;
            design.Data = DataText;
            DataHint.Visibility = DataBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowDataChecks();
            ShowTemplateChoice();
            art = null;
            if (design.Data.Length == 0)
            {
                ShowStage("Type or paste something on the left to make a code.", false);
            }
            else
            {
                try
                {
                    art = new QrArt(design);
                }
                catch (QrException e)
                {
                    ShowStage(e.Message, true);
                }
            }

            if (art != null)
            {
                StageNote.Visibility = Visibility.Collapsed;
                Preview.Visibility = Visibility.Visible;
                RenderPreview();
                ShowInfo();
                StartCheck();
            }
            else
            {
                Preview.Source = null;
                Preview.Visibility = Visibility.Collapsed;
                CheckResult = "";
                CheckLine.Text = "";
                WarnLine.Visibility = Visibility.Collapsed;
                InfoLine.Text = "";
                InCodeRows.Children.Clear();
                InCodeText.Text = "";
            }
            showing = true;
            PresetBox.SelectedItem = Presets.Matching(design);
            showing = false;
            ShowLevelHint();
            ShowVersionHint();
            MarkerNote.Visibility = art != null && art.Code.MarkerLeftOut ? Visibility.Visible : Visibility.Collapsed;
            ShowLogoNote();
            FrameNote.Text = LongText.Cap(art?.FrameImageProblem ?? "");   // (an SVG's problem can quote a name from the file)
            ShowSize();
            ShowPrint();
            CommandManager.InvalidateRequerySuggested();
        }

        void ShowStage(string text, bool problem)
        {
            StageNote.Text = LongText.Cap(text);
            StageNote.SetResourceReference(TextBlock.ForegroundProperty, problem ? "Bad" : "Dim");
            StageNote.Visibility = Visibility.Visible;
        }

        // The preview: the design drawn at the screen's own pixels, as big as fits, shown pixel for
        // pixel, so it's sharp at any scaling (150 % included).
        void RenderPreview()
        {
            if (MakingBarcode)
            {
                RenderBarcodePreview();
                return;
            }
            if (art == null) return;
            const double margin = 20;
            double roomW = Stage.ActualWidth - 2 * margin, roomH = Stage.ActualHeight - 2 * margin;
            if (roomW <= 0 || roomH <= 0)
            {
                Preview.Source = null;   // not laid out yet: the stage's SizeChanged brings it back
                return;
            }
            double scale = Scale;
            double fit = Math.Min(roomW / art.W, roomH / art.H);
            int pw = Math.Max(1, (int)Math.Floor(art.W * fit * scale)), ph = Math.Max(1, (int)Math.Floor(art.H * fit * scale));
            BitmapSource picture = art.Render(pw / art.W, pw, ph);
            Preview.Source = picture;
            Preview.Width = pw / scale;
            Preview.Height = ph / scale;
        }

        // The scan check: the design drawn as it's saved (at its own size, on white paper), read back
        // off the window's thread, as the website does.
        void StartCheck()
        {
            int mine = ticket;
            string expected = design.Data;
            ShowCheck("checking", "Checking that it scans…", "Dim");
            double k = Math.Min(1, 1600 / Math.Max(art.W, art.H));
            BitmapSource picture = art.Render(k, (int)Math.Floor(art.W * k), (int)Math.Floor(art.H * k), Colors.White);
            byte[] grays = ScanCheck.Grays(picture, out int width, out int height);
            Task.Run(() => ScanCheck.Read(grays, width, height))
                .ContinueWith(done => Dispatcher.BeginInvoke(new Action(() => ShowCheckResult(mine, expected, done))));
        }

        void ShowCheckResult(int mine, string expected, Task<ScanCheck.Reading> done)
        {
            if (mine != ticket) return;  // the design has changed since
            if (done.IsFaulted) ShowCheck("error", "Couldn't run the scan check.", "Bad");
            else if (done.Result == null) ShowCheck("unreadable", "✗ Didn't scan in a test decode. Try more contrast, plainer dots, a smaller logo or more error correction.", "Bad");
            else if (done.Result.Text != expected) ShowCheck("mismatch", "✗ Scans, but reads as different text: " + done.Result.Text, "Bad");
            else ShowCheck("ok", "✓ Scans. A test decode matches your data exactly.", "Good");
        }

        // the check's line, which screen readers say when its result changes (as the website's does); what a
        // scan read is quoted, capped
        void ShowCheck(string result, string text, string colour)
        {
            CheckResult = result;
            text = LongText.Cap(text);
            CheckLine.Text = text;
            CheckLine.SetResourceReference(TextBlock.ForegroundProperty, colour);
            if (result != "checking" && text != lastSaidCheck)
            {
                lastSaidCheck = text;
                Say(CheckLine);
            }
        }

        string lastSaidCheck = "", lastSaidDataChecks = "";

        // a polite live region: screen readers say it when it changes
        static void Say(UIElement element)
        {
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        void ShowInfo()
        {
            QrCode code = art.Code;
            CultureInfo c = CultureInfo.InvariantCulture;   // English text, English numbers
            int chars = code.CharacterCount, bytes = code.ByteCount;
            InfoLine.Text = chars.ToString("N0", c) + (chars == 1 ? " character" : " characters") + " · " +
                bytes.ToString("N0", c) + (bytes == 1 ? " byte" : " bytes") + (code.IsPlainAscii ? "" : " (UTF-8)") + " · " +
                "version " + code.Version + " · " + code.Size + " × " + code.Size + " modules · level " + code.Level + (art.LevelRaised ? " (raised for the logo)" : "");
            var warnings = Checks.Warnings(design);
            if (art.LogoProblem != null) warnings.Insert(0, "The logo isn't drawn. " + art.LogoProblem);
            if (art.FrameImageProblem != null) warnings.Insert(0, "The frame's picture isn't drawn. " + art.FrameImageProblem);
            // the data box caps the marks on each letter (LongText); say so when it did, since the code
            // then holds less than the text that was typed, pasted or made again
            if (LongText.GetTrimmed(DataBox)) warnings.Insert(0, LongText.DataNote);
            ShowWarnings(warnings);

            ShowInCode(Checks.InTheCode(art), null);
            InCodeText.Text = code.Text;
            InCodeText.Visibility = Visibility.Visible;
            InCodeFooter.Text = Checks.EverythingLine;
        }

        // the warnings under the code, a line each (a picture's problem can quote a name from its file: capped)
        void ShowWarnings(List<string> warnings)
        {
            WarnLine.Text = LongText.Cap(string.Join("\n", warnings.Select(w => "⚠ " + w)));
            WarnLine.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // "What's in this code", a name and a value to a row (the row named `typed` in a typewriter font;
        // a barcode's text is one, capped)
        void ShowInCode(IEnumerable<KeyValuePair<string, string>> rows, string typed)
        {
            InCodeRows.Children.Clear();
            InCodeRows.RowDefinitions.Clear();
            int row = 0;
            foreach (KeyValuePair<string, string> pair in rows)
            {
                InCodeRows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var name = new TextBlock { Text = pair.Key, Margin = new Thickness(0, 2, 14, 2) };
                name.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
                var value = new TextBlock { Text = LongText.Cap(pair.Value), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
                if (pair.Key == typed) value.FontFamily = new FontFamily("Consolas");
                Grid.SetRow(name, row);
                Grid.SetRow(value, row);
                Grid.SetColumn(value, 1);
                InCodeRows.Children.Add(name);
                InCodeRows.Children.Add(value);
                row++;
            }
        }

        void ShowLevelHint()
        {
            string name = Checks.LevelName(design.Level);
            int percent = design.Level == QrLevel.L ? 7 : design.Level == QrLevel.M ? 15 : design.Level == QrLevel.Q ? 25 : 30;
            QrLevel? suggested = Checks.Suggested(design.Data);
            string hint = name + ": about " + percent + "% of the code can be dirty, scratched or covered and it still scans. Higher levels make the code denser.";
            if (suggested.HasValue) hint += suggested.Value == design.Level ? " Suggested for this much text." : " Suggested for this much text: " + Checks.LevelName(suggested.Value) + ".";
            if (art != null && art.LevelRaised) hint += " A logo covers part of the code, so High (25%) is used.";
            LevelHint.Text = hint;
        }

        void ShowVersionHint()
        {
            if (art != null && art.Code.Grown)
            {
                VersionHint.Text = "Too much data for version " + art.Code.AskedVersion + ", so version " + art.Code.Version + " is used.";
                VersionHint.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
            }
            else
            {
                VersionHint.Text = "A fixed size keeps every code in a batch the same size.";
                VersionHint.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
            }
        }

        void ShowLogoNote()
        {
            RemoveLogoButton.IsEnabled = design.HasLogo;
            if (!design.HasLogo)
            {
                LogoNote.Text = "Or drop a picture here. It goes in the middle, and the dots behind it are left out.";
                LogoNote.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
                LogoHint.Text = "";
                return;
            }
            if (art?.LogoProblem != null)
            {
                LogoNote.Text = LongText.Cap(art.LogoProblem);
                LogoNote.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
                LogoHint.Text = "";
                return;
            }
            LogoNote.Text = "Logo set. Drop or paste another to change it.";
            LogoNote.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
            if (art == null || art.Cleared == 0)
            {
                LogoHint.Text = "";
                return;
            }
            string cleared = art.Cleared + " × " + art.Cleared;
            string logo = Math.Round(art.LogoModules, 2).ToString("0.##", CultureInfo.InvariantCulture);
            LogoHint.Text = design.LogoSpace > art.LogoSpaceUsed + 1e-9
                ? "Only room for " + Math.Round(art.LogoSpaceUsed, 2).ToString("0.##", CultureInfo.InvariantCulture) + ": the logo keeps at least 1 of the " + cleared + " cleared modules."
                : "Logo " + logo + " × " + logo + " of the " + cleared + " cleared modules. Space comes out of the logo so the code still scans; a larger size (QR version) fits both.";
        }

        void ShowSize()
        {
            if (art == null)
            {
                SizeHint.Text = "Saved pictures come out at this size, as on the website.";
                return;
            }
            QrExport.Size size = QrExport.PictureSize(art, print, wholePixels);
            CultureInfo c = CultureInfo.InvariantCulture;   // English text, English numbers
            string hint = "Saves as " + size.Width.ToString("N0", c) + " × " + size.Height.ToString("N0", c) + " pixels";
            if (print.Applies) hint += ", at the print size below.";
            else if (wholePixels && design.Frame == null)
            {
                int per = (int)Math.Round(size.Width / (art.Code.Size + 2 * Math.Max(0, design.Margin)));
                hint += ": " + per + (per == 1 ? " pixel" : " pixels") + " per module, so every square is sharp.";
            }
            else hint += ".";
            SizeHint.Text = hint;
        }

        void ShowPrint()
        {
            PrintWarn.Visibility = Visibility.Collapsed;
            if (!print.On) return;
            if (print.Problem != null)
            {
                PrintHint.Text = print.Problem + " Files use the picture size until then.";
                return;
            }
            if (art == null)
            {
                PrintHint.Text = print.PixelWidth.ToString("N0", CultureInfo.InvariantCulture) + " px across. Applies to every saved picture, SVG and PDF, and to copies.";
                return;
            }
            PrintHint.Text = QrExport.PrintLine(art, print, out string warning) + " Applies to every saved picture, SVG and PDF, and to copies.";
            if (warning != null)
            {
                PrintWarn.Text = "⚠ " + warning;
                PrintWarn.Visibility = Visibility.Visible;
            }
        }

        // the checks on the typed text, each with its one-click fix; screen readers hear new ones
        // (live regions, as on the website)
        void ShowDataChecks()
        {
            DataChecksPanel.Children.Clear();
            List<Checks.DataCheck> checks = Checks.DataChecks(DataText);
            var messages = new List<TextBlock>();
            foreach (Checks.DataCheck check in checks)
            {
                var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                string fixedText = check.Fixed;
                var fix = new Button { Content = check.Button, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                fix.Click += (s, e) =>
                {
                    DataBox.Text = fixedText;
                    madeAgain = DataBox.Text;
                    DataBox.Focus();
                    DataBox.CaretIndex = DataBox.Text.Length;
                    typingPause.Stop();
                    Refresh();
                };
                DockPanel.SetDock(fix, Dock.Right);
                // (a link's tracking parameters are named as written, percent escapes and all undone: capped)
                string words = LongText.Cap(check.Message);
                var message = new TextBlock { Text = "⚠ " + words, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                message.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
                AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
                AutomationProperties.SetHelpText(fix, words);
                row.Children.Add(fix);
                row.Children.Add(message);
                DataChecksPanel.Children.Add(row);
                messages.Add(message);
            }
            string said = string.Join("\n", checks.Select(c => c.Message));
            if (said == lastSaidDataChecks) return;
            lastSaidDataChecks = said;
            if (messages.Count > 0)
                Dispatcher.BeginInvoke(new Action(() => messages.ForEach(m => Say(m))), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ---------- the code's files ----------

        // there's a code, or one is on its way (typed a moment ago); asked while the window is
        // still being built too, before the typing timer exists
        void WhenThereIsACode(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = MakingBarcode ? barcodeArt != null || barcodeTyping.IsEnabled : WhenThereIsAQrCode();
            e.Handled = true;
        }

        // text exports and design files are for QR codes only, as on the website
        void WhenThereIsAQrCode(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !MakingBarcode && WhenThereIsAQrCode();
            e.Handled = true;
        }

        bool WhenThereIsAQrCode() => art != null || typingPause != null && typingPause.IsEnabled && DataBox.Text.Length > 0;

        void WhenMakingQr(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !MakingBarcode;
            e.Handled = true;
        }

        string FileName => MakingBarcode ? BarcodeFileName : Exports.SuggestedName(design.Data);

        void OnSavePng(object sender, ExecutedRoutedEventArgs e) => SavePicture("Save the code as a PNG picture", "PNG picture", ".png", false);

        void OnSaveJpg(object sender, ExecutedRoutedEventArgs e) => SavePicture("Save the code as a JPG picture", "JPG picture", ".jpg", true);

        void SavePicture(string title, string typeName, string extension, bool jpg)
        {
            CatchUp();
            if (MakingBarcode)
            {
                if (barcodeArt != null) SaveBarcodePicture(title.Replace("the code", "the barcode"), typeName, extension, jpg);
                return;
            }
            if (art == null) return;
            QrArt these = art;
            string path = AskWhere(title, typeName, extension);
            if (path == null) return;
            bool saved = Save(path, () =>
            {
                BitmapSource picture = QrExport.Picture(these, print, wholePixels, jpg);
                return jpg ? QrExport.Jpg(picture) : QrExport.Png(picture);
            });
            if (saved) Remember(these.Design);
        }

        void OnSaveSvg(object sender, ExecutedRoutedEventArgs e)
        {
            CatchUp();
            if (MakingBarcode)
            {
                if (barcodeArt != null) SaveBarcodeSvg();
                return;
            }
            if (art == null) return;
            QrArt these = art;
            string path = AskWhere("Save the code as an SVG drawing", "SVG drawing", ".svg");
            if (path != null && Save(path, () => new UTF8Encoding(false).GetBytes(QrExport.Svg(these, print)))) Remember(these.Design);
        }

        void OnSavePdf(object sender, ExecutedRoutedEventArgs e)
        {
            CatchUp();
            if (MakingBarcode)
            {
                if (barcodeArt != null) SaveBarcodePdf();
                return;
            }
            if (art == null) return;
            QrArt these = art;
            string path = AskWhere("Save the code as a PDF", "PDF document", ".pdf");
            if (path != null && Save(path, () => QrExport.Pdf(these, print, wholePixels, Path.GetFileNameWithoutExtension(path)))) Remember(these.Design);
        }

        // the website's three kinds of text, each as plain text or Markdown
        static readonly FileDialogs.FileType[] TextTypes =
        {
            new FileDialogs.FileType("Text in half blocks, compact", ".txt"),
            new FileDialogs.FileType("Text in full blocks", ".txt"),
            new FileDialogs.FileType("Text in ASCII, # for the background", ".txt"),
            new FileDialogs.FileType("Markdown, half blocks", ".md"),
            new FileDialogs.FileType("Markdown, full blocks", ".md"),
            new FileDialogs.FileType("Markdown, ASCII", ".md"),
        };

        void OnSaveText(object sender, ExecutedRoutedEventArgs e)
        {
            CatchUp();
            if (art == null) return;
            QrDesign these = design.Clone();
            string path;
            int kind;
            try
            {
                path = FileDialogs.SaveAs(this, "Save the code as text", TextTypes, 0, FileName + ".txt", out kind);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path == null) return;
            QrExport.TextStyle style = kind % 3 == 1 ? QrExport.TextStyle.FullBlocks : kind % 3 == 2 ? QrExport.TextStyle.Ascii : QrExport.TextStyle.HalfBlocks;
            string text = QrExport.Text(these, style);
            if (kind >= 3) text = QrExport.Markdown(text);
            Save(path, () => new UTF8Encoding(false).GetBytes(text));
            if (style == QrExport.TextStyle.Ascii) Flash("Saved. Most scanners can't read the ASCII kind: it's for plain text, email and terminals.", "Warn");
        }

        string AskWhere(string title, string typeName, string extension)
        {
            try
            {
                return FileDialogs.Save(this, title, typeName, extension, FileName + extension);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        // true when the file was written
        bool Save(string path, Func<byte[]> bytes)
        {
            try
            {
                Exports.Write(path, bytes());
                Flash("Saved " + Path.GetFileName(path), "Good");
                return true;
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException || x is QrException)
            {
                MessageBox.Show(this, "Couldn't save " + Path.GetFileName(path) + ":\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (OutOfMemoryException)
            {
                MessageBox.Show(this, "Couldn't save " + Path.GetFileName(path) + ":\n\nThere isn't enough memory for a picture that big. Use a smaller picture size or print size.",
                    Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }

        // the picture to copy or drag, or null after saying why there's none (too big to draw)
        BitmapSource PictureToShare()
        {
            try
            {
                return MakingBarcode ? BarcodeExport.Picture(barcodeArt, look, barcodePrint) : QrExport.Picture(art, print, wholePixels);
            }
            catch (QrException x)
            {
                Flash(x.Message, "Bad");
            }
            catch (OutOfMemoryException)
            {
                Flash("There isn't enough memory for a picture that big. Use a smaller picture size or print size.", "Bad");
            }
            return null;
        }

        void OnCopyPicture(object sender, ExecutedRoutedEventArgs e)
        {
            CatchUp();
            if (!HasCode) return;
            Action remember = RememberShown();
            BitmapSource picture = PictureToShare();
            if (picture != null && Copy(Exports.DataFor(picture, QrExport.Png(picture), FileName, false), "Copied the code as a picture")) remember();
        }

        void OnCopySvg(object sender, ExecutedRoutedEventArgs e)
        {
            CatchUp();
            if (!HasCode) return;
            Action remember = RememberShown();
            string svg = MakingBarcode ? BarcodeExport.Svg(barcodeArt.Barcode, look, barcodePrint) : QrExport.Svg(art, print);
            if (Copy(Exports.TextData(svg), "Copied the code as SVG")) remember();
        }

        // what adds the code on show to Recent codes, taken now (a QR design, or a barcode with its text and settings)
        internal Action RememberShown()
        {
            if (!MakingBarcode)
            {
                QrDesign shown = art.Design;
                return () => Remember(shown);
            }
            RecentCode code = RecentCodes.NewBarcode(codeType.Value, BarcodeText, look, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return () => RememberCode(code);
        }

        void OnCopyText(object sender, ExecutedRoutedEventArgs e)
        {
            CatchUp();
            if (art != null) Copy(Exports.TextData(QrExport.Text(design, QrExport.TextStyle.HalfBlocks)), "Copied the code as text (half blocks)");
        }

        // true when it's on the clipboard
        bool Copy(DataObject data, string done)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                Flash(done, "Good");
                return true;
            }
            catch (ExternalException)
            {
                Flash("Couldn't copy: another program is using the clipboard. Try again.", "Bad");
                return false;
            }
        }

        void OnPreviewDown(object sender, MouseButtonEventArgs e)
        {
            if (!HasCode) return;
            dragStart = e.GetPosition(this);
            dragArmed = true;
        }

        void OnPreviewMove(object sender, MouseEventArgs e)
        {
            if (!dragArmed) return;
            if (e.LeftButton != MouseButtonState.Pressed || !HasCode)
            {
                dragArmed = false;
                return;
            }
            Point now = e.GetPosition(this);
            if (Math.Abs(now.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(now.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragArmed = false;
            CatchUp();
            if (!HasCode) return;
            BitmapSource picture = PictureToShare();
            if (picture == null) return;
            DataObject data = Exports.DataFor(picture, QrExport.Png(picture), FileName, true);
            Action remember = RememberShown();
            draggingOut = true;  // dropped back on this window, it isn't something to scan
            try
            {
                // a drop somewhere takes the code, the way a copy does
                if (DragDrop.DoDragDrop(Preview, data, DragDropEffects.Copy) != DragDropEffects.None) remember();
            }
            catch (ExternalException)
            {
                // the program it was dropped on failed; there's nothing to undo here
            }
            finally
            {
                draggingOut = false;
            }
        }

        // ---------- design files ----------

        void OnSaveDesign(object sender, ExecutedRoutedEventArgs e)
        {
            CatchUp();
            string path;
            try
            {
                path = FileDialogs.Save(this, "Save the design, to open here or on qr.b0r3d.org", "Design file", ".json", DesignFile.FileName);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            QrDesign saved = design.Clone();
            // a design saved with text in it goes to Recent codes, as on the website
            if (path != null && Save(path, () => new UTF8Encoding(false).GetBytes(DesignFile.Write(saved))) && Js.Trim(saved.Data ?? "").Length > 0) Remember(saved);
        }

        void OnOpenDesign(object sender, ExecutedRoutedEventArgs e)
        {
            string path;
            try
            {
                path = FileDialogs.OpenOne(this, "Open a design (saved here or on qr.b0r3d.org)", "Design files", "*.json");
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Open dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) OpenDesignFile(path);
        }

        internal void OpenDesignFile(string path)
        {
            QrDesign opened;
            try
            {
                if (new FileInfo(path).Length > 50 * 1024 * 1024) throw new DesignFileException(DesignFile.NotADesign);
                opened = DesignFile.Read(File.ReadAllText(path, Encoding.UTF8), design);
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is System.Security.SecurityException)
            {
                MessageBox.Show(this, "Couldn't open " + Path.GetFileName(path) + ":\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (DesignFileException x)
            {
                MessageBox.Show(this, x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ShowOpened(opened);
            Flash("Opened " + Path.GetFileName(path), "Good");
        }

        // a design opened (from a file or Recent codes) into the window, its text exactly as it was
        void ShowOpened(QrDesign opened)
        {
            Tabs.SelectedItem = MakeTab;
            if (MakingBarcode) ChooseCodeType(null);
            design = opened;
            if (!design.Transparent) lastBackground = design.Background;
            madeAgain = design.Data;
            ShowDesign();
            DataBox.Text = design.Data;
            typingPause.Stop();
            Refresh();
        }

        // ---------- data templates ----------

        // the list shows the template the text is, or none while there's no text, as on the website
        void ShowTemplateChoice()
        {
            bool was = showing;
            showing = true;
            string text = DataText;
            TemplateBox.SelectedIndex = Js.Trim(text).Length == 0 ? -1 : Array.IndexOf(TemplateForms.Menu, Templates.Detect(text, out _));
            if (TemplateBox.IsDropDownOpen) templateAtOpen = TemplateBox.SelectedIndex;   // the text's template, not one chosen
            showing = was;
            TemplateHint.Visibility = TemplateBox.SelectedIndex < 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnTemplate(object sender, SelectionChangedEventArgs e) =>
            TemplateHint.Visibility = TemplateBox.SelectedIndex < 0 ? Visibility.Visible : Visibility.Collapsed;

        void OnTemplateOpened(object sender, EventArgs e)
        {
            templateAtOpen = TemplateBox.SelectedIndex;
            templateChosen = false;
        }

        // A template chosen in the open list opens its form. On the closed list the arrow keys only
        // move through the templates (Edit… opens the one showing), so the list can be looked
        // through from the keyboard without a form opening at every step.
        void OnTemplateClosed(object sender, EventArgs e) =>
            Dispatcher.BeginInvoke(new Action(() =>   // Enter in the open list chooses only once it has closed
            {
                if (Quiet || TemplateBox.SelectedIndex < 0 || (TemplateBox.SelectedIndex == templateAtOpen && !templateChosen)) return;
                OpenTemplate(TemplateForms.Menu[TemplateBox.SelectedIndex]);
            }), System.Windows.Threading.DispatcherPriority.Input);

        void OnEditTemplate(object sender, RoutedEventArgs e) =>
            OpenTemplate(TemplateBox.SelectedIndex >= 0 ? TemplateForms.Menu[TemplateBox.SelectedIndex] : TemplateKind.Text);

        // The "Data to encode" window for a template; what it writes becomes the code's text.
        void OpenTemplate(TemplateKind kind)
        {
            CatchUp();
            bool shortened = false;
            UseTemplateText(AskTemplate != null ? AskTemplate(kind) : TemplateWindow.Ask(this, kind, DataText, templateForms, out shortened), shortened);
        }

        // for the tests: stands in for the window a template opens
        internal Func<TemplateKind, string> AskTemplate;

        // `shortened`: a field's text had marks left out in the form, so the code holds less than was put in it, which
        // the Make tab says as it does of the data box's own (LongText)
        internal void UseTemplateText(string written, bool shortened = false)
        {
            // cancelled, or nothing written: the text stays (an empty one never goes over it)
            if (string.IsNullOrEmpty(written))
            {
                ShowTemplateChoice();
                return;
            }
            DataBox.Text = written;
            madeAgain = DataBox.Text;
            if (shortened) LongText.MarkCut(DataBox);
            typingPause.Stop();
            Refresh();
        }

        // for the tests: the window a template would open, as OpenTemplate opens it
        internal TemplateWindow TemplateFor(TemplateKind kind) => new TemplateWindow(kind, DataText, templateForms);

        // ---------- Recent codes ----------

        // Adds the code just saved, copied or dragged out to Recent codes while that's on; the first
        // time, a note says where it went and offers to stop.
        internal void Remember(QrDesign snapshot)
        {
            if (!settings.RecentCodes || !DataFolder.CanKeep || Js.Trim(snapshot?.Data ?? "").Length == 0) return;
            RememberCode(RecentCodes.New(snapshot, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }

        // the same for any code, a QR code's design or a barcode
        internal void RememberCode(RecentCode code)
        {
            if (!settings.RecentCodes || !DataFolder.CanKeep) return;
            int id = RecentCodes.Add(code, out RecentTrouble trouble);
            RecentWindow.LastFailed = trouble == RecentTrouble.Unwritable;
            if (id == 0)
            {
                string why = trouble == RecentTrouble.Busy ? "its file in the data folder is in use. Try again in a moment."
                    : trouble == RecentTrouble.Damaged ? "its file in the data folder can't be read (Recent codes says more)."
                    : trouble == RecentTrouble.Newer ? "its file in the data folder is from a newer b0r3d QR."
                    : "its file in the data folder couldn't be written.";
                // after what the save or copy said, so that isn't lost
                Flash((noteTimer.IsEnabled && Note.Text.Length > 0 ? Note.Text.TrimEnd('.') + ". " : "") + "Not added to Recent codes: " + why, "Warn");
                return;
            }
            if (settings.RecentNoteShown) return;
            settings.RecentNoteShown = true;
            settings.Save();
            noteCode = id;
            RecentNoteProblem.Visibility = Visibility.Collapsed;
            RecentNote.Visibility = Visibility.Visible;
            Say(RecentNoteText);
        }

        void OnRecent(object sender, RoutedEventArgs e) => new RecentWindow(settings, OpenRecent) { Owner = this }.ShowDialog();

        // a code from Recent codes: a QR code opened like a design file with its text, or a barcode with its
        // type, text and settings; false when it's gone
        internal bool OpenRecent(int id)
        {
            RecentCode code = RecentCodes.Find(id);
            if (code == null) return false;
            if (code.Barcode != null) OpenRecentBarcode(code);
            else
            {
                QrDesign opened = RecentCodes.Open(id, design);
                if (opened == null) return false;
                ShowOpened(opened);
            }
            Flash("Opened from Recent codes", "Good");
            return true;
        }

        // "Don't keep codes": no more are added (for this run at least, if the setting can't be
        // saved), and the one the note is about goes
        void OnNoteStop(object sender, RoutedEventArgs e)
        {
            settings.RecentCodes = false;
            bool stopped = settings.Save();
            bool deleted = noteCode == 0 || RecentCodes.Delete(noteCode);
            if (deleted) noteCode = 0;
            if (stopped && deleted)
            {
                RecentNote.Visibility = Visibility.Collapsed;
                return;
            }
            RecentNoteProblem.Text = stopped ? "No more codes will be added, but this one couldn't be deleted. Open Recent codes to delete it."
                : deleted ? "No more codes will be added while b0r3d QR is open, but the setting couldn't be saved for next time."
                : "No more codes will be added while b0r3d QR is open, but the setting couldn't be saved for next time, and this code couldn't be deleted. Open Recent codes to delete it.";
            RecentNoteProblem.Visibility = Visibility.Visible;
        }

        void OnNoteView(object sender, RoutedEventArgs e)
        {
            RecentNote.Visibility = Visibility.Collapsed;
            OnRecent(sender, e);
        }

        void OnNoteOk(object sender, RoutedEventArgs e) => RecentNote.Visibility = Visibility.Collapsed;

        // ---------- pictures for the design ----------

        void OnChooseLogo(object sender, RoutedEventArgs e)
        {
            string path = ChoosePicture("Choose a logo");
            if (path != null) LogoFromFile(path);
        }

        void OnPasteLogo(object sender, RoutedEventArgs e)
        {
            IDataObject data;
            try
            {
                data = Clipboard.GetDataObject();
            }
            catch (ExternalException)
            {
                Flash("Couldn't look at the clipboard: another program is using it. Try again.", "Bad");
                return;
            }
            if (!LogoFromData(data)) Flash("Nothing to use on the clipboard: copy a picture or an SVG drawing first.", "Warn");
        }

        void OnRemoveLogo(object sender, RoutedEventArgs e)
        {
            design.Logo = "";
            Changed();
        }

        void OnFramePicture(object sender, RoutedEventArgs e)
        {
            if (design.Frame == null) return;
            string path = ChoosePicture("Choose a picture for the frame's background");
            if (path == null) return;
            string url = PictureFromFile(path, 2048);
            if (url == null) return;
            design.Frame.BackgroundImage = url;
            ShowDesign();
            Changed();
        }

        void OnFrameNoPicture(object sender, RoutedEventArgs e)
        {
            if (design.Frame == null) return;
            design.Frame.BackgroundImage = null;
            ShowDesign();
            Changed();
        }

        string ChoosePicture(string title)
        {
            try
            {
                return FileDialogs.OpenOne(this, title, "Pictures", FileDialogs.PictureTypes + ";*.svg");
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Open dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        void LogoFromFile(string path)
        {
            string url = PictureFromFile(path, 1024);
            if (url == null) return;
            design.Logo = url;
            Tabs.SelectedItem = MakeTab;
            LogoSection.IsExpanded = true;
            Changed();
        }

        string PictureFromFile(string path, int largest)
        {
            byte[] bytes;
            try
            {
                if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new IOException("it's over 64 MB");
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is System.Security.SecurityException)
            {
                MessageBox.Show(this, "Couldn't open " + Path.GetFileName(path) + ":\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            string url = DesignPicture(bytes, largest, out string problem);
            if (url == null) Flash("Couldn't use " + Path.GetFileName(path) + ": " + problem, "Bad");
            return url;
        }

        // a picture copied or dropped, as the logo; false when there was none. An SVG drawing (as
        // image/svg+xml, the way design programs copy one, or as its text) comes before a picture of it.
        internal bool LogoFromData(IDataObject data)
        {
            if (data == null) return false;
            string[] files;
            BitmapSource picture;
            byte[] svg;
            try
            {
                Pictures.From(data, out files, out picture);
                svg = files != null && files.Length > 0 ? null : SvgIn(data);
            }
            catch (ExternalException)
            {
                return false;
            }
            if (files != null && files.Length > 0 && File.Exists(files[0]))
            {
                LogoFromFile(files[0]);
                return true;
            }
            if (svg == null && picture == null) return false;
            string url = DesignPicture(svg ?? QrExport.Png(picture), 1024, out string problem);
            if (url == null)
            {
                Flash("Couldn't use that picture: " + problem, "Bad");
                return true;
            }
            design.Logo = url;
            Tabs.SelectedItem = MakeTab;
            LogoSection.IsExpanded = true;
            Changed();
            return true;
        }

        // An SVG drawing in what's copied or dragged: image/svg+xml (Inkscape and other design programs
        // put one there), or text that's an SVG file (Figma's Copy as SVG, a text editor); else null.
        static byte[] SvgIn(IDataObject data)
        {
            try
            {
                if (data.GetDataPresent("image/svg+xml"))
                {
                    object svg = data.GetData("image/svg+xml");
                    byte[] bytes = svg is MemoryStream stream ? stream.ToArray() : svg is byte[] raw ? raw : svg is string text ? new UTF8Encoding(false).GetBytes(text) : null;
                    if (bytes != null && Svg.SvgImage.Looks(bytes)) return bytes;
                }
                if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string copied)
                {
                    string text = copied.Trim();
                    // (the start says whether it's one, before all of a long text is turned into bytes)
                    if (Svg.SvgImage.Looks(new UTF8Encoding(false).GetBytes(text.Length > 65536 ? text.Substring(0, 65536) : text))) return new UTF8Encoding(false).GetBytes(text);
                }
            }
            catch (Exception x) when (x is ExternalException || x is OutOfMemoryException || x is ArgumentException)
            {
                // another program's data that won't come out: nothing to use
            }
            return null;
        }

        // A picture for a design, as the website keeps an upload: as it is when it's small enough
        // (at most `largest` pixels each way and 256 KB) and a kind browsers show; otherwise shrunk
        // to `largest` pixels on its longer side, as a JPEG if it was one and a PNG otherwise,
        // whichever of the two comes out smaller (turned the right way up, as browsers draw photos).
        // SVG and GIF files are kept as they are: an SVG one only once it's been read and drawn.
        internal static string DesignPicture(byte[] bytes, int largest, out string problem)
        {
            problem = null;
            if (Svg.SvgImage.Looks(bytes))
            {
                if (Images.Drawing(bytes, null, out string why) != null) return "data:image/svg+xml;base64," + Convert.ToBase64String(bytes);
                problem = "it's an SVG drawing b0r3d QR can't draw: " + why + ".";
                return null;
            }
            BitmapDecoder decoder;
            try
            {
                decoder = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            }
            catch (Exception x) when (x is NotSupportedException || x is FileFormatException || x is ArgumentException || x is IOException ||
                                      x is InvalidOperationException || x is ExternalException || x is OverflowException)
            {
                problem = "Windows can't read it as a picture.";
                return null;
            }
            string type = decoder is PngBitmapDecoder ? "image/png" : decoder is JpegBitmapDecoder ? "image/jpeg" : decoder is GifBitmapDecoder ? "image/gif" : null;
            string original = type == null ? null : "data:" + type + ";base64," + Convert.ToBase64String(bytes);
            if (type == "image/gif") return original;
            BitmapSource frame = Images.Upright(decoder.Frames[0]);
            int longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (original != null && longest <= largest && bytes.Length <= 256 * 1024) return original;
            double scale = Math.Min(1, largest / (double)longest);
            BitmapSource shrunk = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
            bool jpeg = type == "image/jpeg";
            byte[] again;
            if (jpeg)
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
                encoder.Frames.Add(BitmapFrame.Create(shrunk));
                using (var output = new MemoryStream())
                {
                    encoder.Save(output);
                    again = output.ToArray();
                }
            }
            else again = QrExport.Png(shrunk);
            string smaller = "data:" + (jpeg ? "image/jpeg" : "image/png") + ";base64," + Convert.ToBase64String(again);
            return original != null && original.Length <= smaller.Length ? original : smaller;
        }

        // A picture dropped on the Logo section becomes the logo, and a design file dropped anywhere
        // opens; true when the drag was taken.
        bool MakeDrop(DragEventArgs e, bool drop)
        {
            if (draggingOut || Scanning) return false;
            string designFile = DesignFileIn(e.Data);
            if (designFile != null)
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                if (drop) OpenDesignFile(designFile);
                return true;
            }
            string csvFile = CsvFileIn(e.Data);
            if (csvFile != null && Tabs.SelectedItem == MakeTab)
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                // (after the drop is done: a window shown inside it would hold up Explorer)
                if (drop) Dispatcher.BeginInvoke(new Action(() => OpenBatch(csvFile)));
                return true;
            }
            if (Tabs.SelectedItem != MakeTab || !LogoSection.IsVisible || PdfOrFolderIn(e.Data)) return false;
            Point at = e.GetPosition(LogoSection);
            if (at.X < 0 || at.Y < 0 || at.X > LogoSection.ActualWidth || at.Y > LogoSection.ActualHeight) return false;
            if (!Pictures.CanRead(e.Data) && SvgIn(e.Data) == null) return false;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            if (drop) LogoFromData(e.Data);
            return true;
        }

        static string DesignFileIn(IDataObject data)
        {
            try
            {
                if (data != null && data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths &&
                    paths.Length == 1 && string.Equals(Path.GetExtension(paths[0]), ".json", StringComparison.OrdinalIgnoreCase))
                    return paths[0];
            }
            catch (COMException)
            {
            }
            return null;
        }

        static bool PdfOrFolderIn(IDataObject data)
        {
            try
            {
                if (data != null && data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths)
                    return paths.Any(p => string.Equals(Path.GetExtension(p), ".pdf", StringComparison.OrdinalIgnoreCase) || Directory.Exists(p));
            }
            catch (COMException)
            {
            }
            return false;
        }
    }
}
