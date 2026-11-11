using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Qr;

namespace B0r3dQR.Tests
{
    // The windows, built as the program builds them and laid out without ever being shown.
    static partial class TestMain
    {
        const double MainWidth = 1000, MainHeight = 640;

        static MainWindow NewMain(double scale = 1)
        {
            var window = new MainWindow(new Settings()) { ScaleOverride = scale };
            LayOut(window.Root, MainWidth, MainHeight);
            return window;
        }

        // measures and arranges a window's content at a size (height: as tall as it needs, if not given)
        static Size LayOut(FrameworkElement root, double width, double height = double.PositiveInfinity)
        {
            root.Measure(new Size(width, height));
            double h = double.IsInfinity(height) ? Math.Ceiling(root.DesiredSize.Height) : height;
            root.Arrange(new Rect(0, 0, width, h));
            root.UpdateLayout();
            return new Size(width, h);
        }

        // types into the data box, then waits for the pause after typing, the new code and its check
        static void Type(MainWindow window, string text)
        {
            int before = window.Refreshes;
            window.DataBox.Text = text;
            Pump(() => window.Refreshes != before && window.CheckResult != "checking", 5000);
            Check(window.Refreshes != before, "typing didn't make a new code within 5 seconds");
        }

        static void WaitForCheck(MainWindow window) => Pump(() => window.CheckResult != "checking", 5000);

        static void ThemesMatch()
        {
            string[] names = { "Light", "Dark", "HighContrast" };
            List<string>[] keys = names.Select(n =>
                new ResourceDictionary { Source = new Uri("/b0r3dQR;component/Themes/" + n + ".xaml", UriKind.Relative) }
                    .Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k, StringComparer.Ordinal).ToList()).ToArray();
            for (int i = 1; i < names.Length; i++)
            {
                var missing = keys[0].Except(keys[i]).Concat(keys[i].Except(keys[0])).ToList();
                Check(missing.Count == 0, names[0] + " and " + names[i] + " differ in: " + string.Join(", ", missing));
            }

            // every colour named in the XAML and code is in the colour sets (or, for the few things
            // that aren't colours, like the text boxes' menu, in Controls.xaml)
            var controls = new ResourceDictionary { Source = new Uri("/b0r3dQR;component/Themes/Controls.xaml", UriKind.Relative) }
                .Keys.Cast<object>().Select(k => k.ToString()).ToList();
            string src = Path.Combine(RepoFolder, "src");
            if (Directory.Exists(src))
            {
                var used = new HashSet<string>();
                foreach (string file in Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories))
                    foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{DynamicResource ([A-Za-z]+)\}")) used.Add(m.Groups[1].Value);
                foreach (string file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
                {
                    string code = File.ReadAllText(file);
                    foreach (Match m in Regex.Matches(code, @"SetResourceReference\([^,]+,\s*""([A-Za-z]+)""\)")) used.Add(m.Groups[1].Value);
                    foreach (Match m in Regex.Matches(code, @"ShowCheck\(""[a-z-]+"",[^;]*,\s*""([A-Za-z]+)""\)")) used.Add(m.Groups[1].Value);
                    foreach (Match m in Regex.Matches(code, @"(?:Flash|ShowStage)\([^;]*?""(Good|Bad|Warn|Dim)""\)")) used.Add(m.Groups[1].Value);
                    foreach (Match call in Regex.Matches(code, @"ShowSummary\([^;]*\);"))   // ShowSummary(text, ok ? "Text" : "Warn")
                        foreach (Match m in Regex.Matches(call.Value, @"""([A-Z][A-Za-z]+)""")) used.Add(m.Groups[1].Value);
                }
                var unknown = used.Where(k => !keys[0].Contains(k) && !controls.Contains(k)).ToList();
                Check(used.Count > 15, "found only " + used.Count + " colour names in src (has the code moved?)");
                Check(unknown.Count == 0, "used but in no colour set: " + string.Join(", ", unknown));
            }

            Theme.Set(Look.Dark);
            bool darkOk = Theme.IsHighContrast || ((SolidColorBrush)Application.Current.Resources["Back"]).Color == Color.FromRgb(0x13, 0x13, 0x13);
            Theme.Set(Look.Light);
            bool lightOk = Theme.IsHighContrast || ((SolidColorBrush)Application.Current.Resources["Back"]).Color == Colors.White;
            Check(darkOk && lightOk, "switching the look didn't swap the colours");
        }

        sealed class Collect : TraceListener
        {
            public readonly List<string> Lines = new List<string>();
            public override void Write(string message) { }
            public override void WriteLine(string message) => Lines.Add(message);
        }

        static void WindowWithoutBindingErrors()
        {
            var errors = new Collect();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            try
            {
                foreach (Look look in new[] { Look.Light, Look.Dark })
                {
                    Theme.Set(look);
                    MainWindow window = NewMain();
                    Type(window, "https://b0r3d.org");
                    LayOut(window.Root, MainWidth, MainHeight);
                    var about = new AboutWindow();
                    LayOut(about.Root, 640);
                }
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
                Theme.Set(Look.Light);
            }
            Check(errors.Lines.Count == 0, errors.Lines.Count + " binding errors, first: " + errors.Lines.FirstOrDefault());
        }

        // the tab headers' typewriter font stays on the headers; what's on the tabs keeps the window's
        // font and, in the dark look too, its text colour
        static void TabsKeepTheirLookToThemselves()
        {
            Theme.Set(Look.Dark);
            try
            {
                MainWindow window = NewMain();
                foreach (TabItem tab in new[] { window.MakeTab, window.ScanTab })
                {
                    window.Tabs.SelectedItem = tab;
                    LayOut(window.Root, MainWidth, MainHeight);
                }
                string font = window.FontFamily.Source;
                var plain = new Control[] { window.DataBox, window.SavePngButton, window.ScanScreenButton, window.ClearButton };
                foreach (Control control in plain)
                    Check(control.FontFamily.Source == font && control.FontWeight == FontWeights.Normal, control.Name + " is in " + control.FontFamily.Source + " " + control.FontWeight + ", not the window's " + font);
                Check(window.ScanStatus.FontFamily.Source == font && window.InfoLine.FontFamily.Source == font, "the lines under the code and over the codes found aren't in " + font);

                Color text = ((SolidColorBrush)window.FindResource("Text")).Color;
                var headingStyle = (Style)window.FindResource("Heading");
                var headings = new List<TextBlock>();
                foreach (TabItem tab in new[] { window.MakeTab, window.ScanTab })
                    headings.AddRange(LogicalDescendants((DependencyObject)tab.Content).OfType<TextBlock>().Where(t => t.Style == headingStyle));
                Check(headings.Count >= 2, "only " + headings.Count + " headings found on the tabs");   // the Scan tab's; Make's are fold-out sections
                foreach (TextBlock heading in headings)
                    Check(heading.Foreground is SolidColorBrush brush && brush.Color == text, "the heading \"" + heading.Text + "\" is in " + heading.Foreground + ", not the text colour " + text);
            }
            finally
            {
                Theme.Set(Look.Light);
            }
        }

        static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject parent)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(parent))
            {
                if (!(child is DependencyObject element)) continue;
                yield return element;
                foreach (DependencyObject below in LogicalDescendants(element)) yield return below;
            }
        }

        static void WindowMakesTheCode()
        {
            MainWindow window = NewMain();
            Check(window.StageNote.Visibility == Visibility.Visible && window.StageNote.Text.StartsWith("Type"), "an empty window should ask for text: " + window.StageNote.Text);
            Check(!window.HasCode, "there's something to save before anything is typed");
            Pump(() => false, 100);
            Check(!window.SavePngButton.IsEnabled && !window.CopyButton.IsEnabled, "Save and Copy work with nothing to save");

            Type(window, "https://b0r3d.org");
            WaitForCheck(window);
            Check(window.CheckResult == "ok", "the check says " + window.CheckResult + ": " + window.CheckLine.Text);
            Pump(() => window.SavePngButton.IsEnabled, 2000);
            Check(window.SavePngButton.IsEnabled && window.SaveSvgButton.IsEnabled && window.CopyButton.IsEnabled, "Save and Copy didn't switch on for the code");
            Check(window.HasCode && window.Code.Version == 2, "https://b0r3d.org should be version 2 at level Q");
            Check(window.InfoLine.Text.Contains("17 characters") && window.InfoLine.Text.Contains("version 2") && window.InfoLine.Text.Contains("25 × 25"), "the info line: " + window.InfoLine.Text);
            var preview = window.Preview.Source as BitmapSource;
            Check(preview != null && preview.PixelWidth >= 33 * 4 && preview.PixelWidth == preview.PixelHeight, "the preview is " + preview?.PixelWidth + " × " + preview?.PixelHeight + " pixels");
            Check(window.SizeHint.Text.StartsWith("Saves as 200 × 200 pixels", StringComparison.Ordinal), "the website's 200 px should save as 200 × 200: " + window.SizeHint.Text);

            Type(window, "Line one\r\nLine two");
            Check(window.Code?.Text == "Line one\nLine two", "Windows line breaks should go into the code as plain line breaks");
            WaitForCheck(window);
            Check(window.CheckResult == "ok", "two lines: the check says " + window.CheckResult);
        }

        static void WindowExplainsProblems()
        {
            MainWindow window = NewMain();
            Type(window, new string('a', 3000));
            Check(window.StageNote.Visibility == Visibility.Visible && window.StageNote.Text.StartsWith("Too much"), "3,000 bytes at level Q: " + window.StageNote.Text);
            Check(!window.HasCode && window.Preview.Source == null && window.CheckLine.Text.Length == 0, "too much text left a code behind");
            Type(window, "");
            Check(window.StageNote.Text.StartsWith("Type") && !window.HasCode, "emptied: " + window.StageNote.Text);
            Check(window.DataHint.Visibility == Visibility.Visible, "the hint in the empty box should be back");
        }

        static void WindowOptions()
        {
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org/options");
            int version = window.Code.Version;
            window.LevelH.IsChecked = true;
            WaitForCheck(window);
            Check(window.Code.Level == QrLevel.H && window.Code.Version >= version && window.CheckResult == "ok", "level H: " + window.InfoLine.Text + " / " + window.CheckResult);
            Check(window.LevelHint.Text.StartsWith("High"), "the level's hint: " + window.LevelHint.Text);

            window.QuietBox.Value = 0;
            WaitForCheck(window);
            Check(window.WarnLine.Visibility == Visibility.Visible && window.WarnLine.Text.Contains("Only 0 modules of blank space"), "no quiet zone should warn: " + window.WarnLine.Text);
            Check(window.CheckResult == "ok" || window.CheckResult == "unreadable", "no quiet zone: the check says " + window.CheckResult);
            window.QuietBox.Value = 4;
            WaitForCheck(window);
            Check(window.WarnLine.Visibility == Visibility.Collapsed && window.CheckResult == "ok", "back to 4: " + window.CheckResult);

            window.SizeBox.Value = 2000;
            Check(window.SizeHint.Text.StartsWith("Saves as " + 2000.ToString("N0") + " × " + 2000.ToString("N0") + " pixels", StringComparison.Ordinal), "2000 pixels: " + window.SizeHint.Text);
            window.WholePixelsCheck.IsChecked = true;
            int modules = window.Code.Size + 8;
            int perModule = (int)Math.Round(2000.0 / modules);
            Check(window.SizeHint.Text.Contains((perModule * modules).ToString("N0") + " pixels: " + perModule + " pixels per module"), "whole pixels per module: " + window.SizeHint.Text);
            window.WholePixelsCheck.IsChecked = false;
            window.SizeBox.Value = 99999;
            Check(window.SizeBox.Value == QrPicture.MaxPixels, "the picture size should stop at " + QrPicture.MaxPixels);
        }

        static void WindowSharpAt150()
        {
            MainWindow window = NewMain(1.5);
            Type(window, "https://b0r3d.org");
            LayOut(window.Root, MainWidth, MainHeight);
            var preview = (BitmapSource)window.Preview.Source;
            double room = Math.Min(window.Stage.ActualWidth, window.Stage.ActualHeight) - 40;
            int pixels = (int)Math.Floor(room * 1.5);
            Check(Math.Abs(preview.PixelWidth - pixels) <= 1, "at 150 % the preview should fill " + pixels + " screen pixels, not " + preview.PixelWidth);
            Check(Math.Abs(window.Preview.Width * 1.5 - preview.PixelWidth) < 0.001, "the preview isn't shown pixel for pixel at 150 %");
        }

        static void WindowKeepsSettings()
        {
            string data = DataFolder.Path;
            DataFolder.Reset(Path.Combine(Work, "window-settings"));
            try
            {
                MainWindow window = NewMain();
                Type(window, "https://b0r3d.org/typed-secret-4711");
                window.LevelM.IsChecked = true;
                window.QuietBox.Value = 6;
                window.SizeBox.Value = 1500;
                Theme.Set(Look.Dark);
                window.SaveSettings();
                Settings back = Settings.Load();
                Check(back.Level == QrLevel.M && back.QuietZone == 6 && back.PictureSize == 1500 && back.Look == Look.Dark, "read back: " + back.ToText());
                string kept = File.ReadAllText(Path.Combine(DataFolder.Path, Settings.FileName));
                Check(!kept.Contains("typed-secret-4711") && !kept.Contains("b0r3d.org"), "what was typed got into " + Settings.FileName);
                string[] everything = Directory.GetFiles(DataFolder.Path, "*", SearchOption.AllDirectories);
                Check(everything.All(f => !File.ReadAllText(f).Contains("typed-secret-4711")), "what was typed got into a file in the data folder");
            }
            finally
            {
                Theme.Set(Look.Light);
                DataFolder.Reset(data);
            }
        }

        // Ctrl+S straight after typing, or with a number typed but not entered, uses what's there now
        static void ExportsCatchUp()
        {
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org/before");
            window.DataBox.Text = "https://b0r3d.org/after";  // and no pause for the timer
            window.SizeBox.TextField.Text = "3000";           // typed, not entered
            window.CatchUp();
            Check(window.Code != null && window.Code.Text == "https://b0r3d.org/after", "an export straight after typing would use \"" + Short(window.Code?.Text) + "\"");
            Check(window.SizeBox.Value == 3000, "the half-typed picture size wasn't taken: " + window.SizeBox.Value);
            Check(window.SizeHint.Text.Contains(3000.ToString("N0") + " × " + 3000.ToString("N0")), "the size line didn't follow: " + window.SizeHint.Text);
        }

        // two things on screen at once never share an access key (Alt+letter), and every list's
        // lines each have their own
        static void AccessKeysAreUnique()
        {
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org");
            string Key(object label)
            {
                string text = label as string ?? (label as TextBlock)?.Text ?? (label as AccessText)?.Text ?? "";
                int at = text.IndexOf('_');
                return at >= 0 && at + 1 < text.Length ? char.ToUpperInvariant(text[at + 1]).ToString() : null;
            }
            void Unique(string where, IEnumerable<string> keys)
            {
                var list = keys.ToList();
                Check(list.All(k => k != null), where + ": something has no access key");
                var twice = list.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                Check(twice.Count == 0, where + ": " + string.Join(", ", twice) + " used twice");
            }
            List<MenuItem> top = window.MenuBar.Items.OfType<MenuItem>().ToList();
            IEnumerable<string> always = top.Select(m => Key(m.Header)).Concat(new[] { Key(window.MakeTab.Header), Key(window.ScanTab.Header) });
            Unique("the Make tab", always.Concat(new ContentControl[] { window.CodeTypeLabel, window.SavePngButton, window.SaveSvgButton, window.CopyButton, window.ChooseLogoButton, window.PasteLogoButton,
                    window.BackgroundCheck, window.FrameCheck, window.MarkerCheck, window.WholePixelsCheck, window.PrintCheck }.Select(b => Key(b.Content)))
                .Concat(new[] { window.DataSection, window.StyleSection, window.LogoSection, window.FrameSection, window.SizeSection }.Select(s => Key(s.Header))));
            // with a barcode: its own settings in place of the QR code's (all of them, whichever type shows which)
            Unique("the Make tab with a barcode", always.Concat(new ContentControl[] { window.CodeTypeLabel, window.SavePngButton, window.SaveSvgButton, window.CopyButton,
                    window.ShowTextCheck, window.QuietZonesCheck, window.RectangularCheck, window.TransparentCheck, window.BarcodePrintCheck }.Select(b => Key(b.Content)))
                .Concat(new[] { window.BarcodeDataSection, window.BarcodeLookSection }.Select(s => Key(s.Header))));
            Unique("the Scan tab", always.Concat(new ContentControl[] { window.ScanScreenButton, window.ScanAnywhereCheck, window.HideToTrayButton, window.ScanCameraButton, window.OpenPictureButton, window.PastePictureButton, window.ScanFolderButton, window.ClearButton, window.CancelScanButton }.Select(b => Key(b.Content))));
            Unique("the tray menu", window.Tray.Menu.Items.OfType<MenuItem>().Select(m => Key(m.Header)));
            void Lists(MenuItem item)
            {
                List<MenuItem> lines = item.Items.OfType<MenuItem>().ToList();
                if (lines.Count == 0) return;
                Unique("the " + Key(item.Header) + " menu", lines.Select(l => Key(l.Header)));
                lines.ForEach(Lists);
            }
            top.ForEach(Lists);
        }

        // a screen reader lands on the text box inside a number box: it has the box's name
        static void NumberBoxesHaveNames()
        {
            MainWindow window = NewMain();
            foreach (var box in new[] { window.QuietBox, window.SizeBox })
            {
                string name = System.Windows.Automation.AutomationProperties.GetName(box.TextField);
                Check(name.Length > 0 && name == System.Windows.Automation.AutomationProperties.GetName(box), "a number box's text box has no name for screen readers (\"" + name + "\")");
            }
        }

        // the text boxes' right-click menu is b0r3d QR's own, so it takes the light or dark look
        static void TextMenusAreThemed()
        {
            MainWindow window = NewMain();
            var about = new AboutWindow();
            LayOut(about.Root, 640);
            foreach (TextBox box in new[] { window.DataBox, window.QuietBox.TextField, about.Licences })
            {
                ContextMenu menu = box.ContextMenu;
                Check(menu != null && menu.GetType() == typeof(ContextMenu), "a text box has Windows' own right-click menu, which stays light in dark mode");
                var commands = menu.Items.OfType<MenuItem>().Select(m => m.Command).ToList();
                Check(commands.Contains(ApplicationCommands.Cut) && commands.Contains(ApplicationCommands.Copy) && commands.Contains(ApplicationCommands.Paste), "the right-click menu lacks Cut, Copy or Paste");
                Check(menu.Items.OfType<MenuItem>().All(m => m.GetType() == typeof(MenuItem)), "the right-click menu's lines can't take the theme");
            }
            Check(!ReferenceEquals(window.DataBox.ContextMenu, window.QuietBox.TextField.ContextMenu), "two text boxes share one right-click menu");
        }

        static void AboutShowsItAll()
        {
            var about = new AboutWindow();
            LayOut(about.Root, 640);
            Check(about.VersionText.Text == "v" + Program.Version + " · Build: " + Program.Build, "the version line: " + about.VersionText.Text);
            Check(NewMain().VersionRun.Text == "b0r3d QR v" + Program.Version + " · Build: " + Program.Build, "the window's corner should show the version and the build apart");
            string licences = about.Licences.Text;
            // everyone whose work is in the exe: ZXing.Net (with Barcode4J's notice, which ZXing carries), zint (with its
            // Data Matrix, Reed-Solomon and QR mode authors, and GS1's syntax dictionary behind its AI table), qrcode-generator,
            // and the website's 18 caption fonts under the OFL
            foreach (string part in new[] { "Commons Clause", "MIT License", "Licensor: b0r3d.org", "ZXing.Net", "Apache License", "Barcode4J", "zint 2.16.0", "BSD 3-Clause",
                                            "Grandzebu", "Adrian Kennard", "Stefan Schmidt", "Cliff Hones", "Project Nayuki", "GS1 AISBL", "qrcode-generator", "Kazuhiko Arase",
                                            "Fontsource 5.3.0", "SIL OPEN FONT LICENSE Version 1.1", "The Pacifico Project Authors", "The Merriweather Project Authors", "IBM Corp." })
                Check(licences.Contains(part), "the licences don't mention " + part);
            foreach (WebFont font in WebFonts.All)
                Check(licences.Contains("\n" + font.Name + "\r\n    ") || licences.Contains("\n" + font.Name + "\n    "), "the licences don't give " + font.Name + "'s copyright");
            Check(!licences.Contains("is missing from this build"), "a licence file is missing from the build");
            Check(about.FolderText.Text.StartsWith(DataFolder.Path), "the settings folder line: " + about.FolderText.Text);
        }

        // Help → Check for a new version opens b0r3d QR's Releases page in the browser (b0r3d QR never
        // looks for itself), and Print… sits with Save and Copy under the code, there when they are
        static void NewVersionAndPrintButton()
        {
            MainWindow window = NewMain();
            MenuItem help = window.MenuBar.Items.OfType<MenuItem>().Single(m => (string)m.Header == "_Help");
            Check(help.Items.OfType<MenuItem>().Contains(window.NewVersionMenu), "Check for a new version isn't in the Help menu");
            Check(Program.ReleasesUrl == "https://github.com/still-b0r3d/b0r3d-QR-PC/releases", "the Releases page is " + Program.ReleasesUrl);
            var opened = new List<string>();
            Action<string> browser = MainWindow.Browser;
            MainWindow.Browser = opened.Add;
            try
            {
                window.NewVersionMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            finally
            {
                MainWindow.Browser = browser;
            }
            Check(opened.SequenceEqual(new[] { Program.ReleasesUrl }), "Check for a new version opened: " + string.Join(", ", opened));

            var row = (Panel)window.CopyButton.Parent;
            Check(window.PrintButton.Parent == row && row.Children.IndexOf(window.PrintButton) == row.Children.IndexOf(window.CopyButton) + 1,
                "Print… should come straight after Copy picture under the code");
            Check(window.PrintButton.Command == Commands.Print, "Print… isn't File → Print's command");
            Pump(() => !window.PrintButton.IsEnabled, 2000);   // the buttons catch up when WPF is idle
            Check(!window.PrintButton.IsEnabled && !window.CopyButton.IsEnabled, "with nothing typed there's nothing to print");
            Type(window, "https://b0r3d.org");
            Pump(() => window.PrintButton.IsEnabled, 2000);
            Check(window.PrintButton.IsEnabled && window.CopyButton.IsEnabled, "with a code, Print… should be there as Copy picture is");
            // a barcode: none while its data can't be made, then one to print (each state seen, not left over from the last)
            window.ChooseCodeType(Barcodes.BarcodeType.Ean13, "12345");
            WaitForCheck(window);
            Pump(() => !window.PrintButton.IsEnabled, 2000);
            Check(window.MakingBarcode && !window.HasCode && !window.PrintButton.IsEnabled, "an EAN-13 of 5 digits can't be made, so there's nothing to print");
            window.ChooseCodeType(Barcodes.BarcodeType.Ean13, "400638133393");
            WaitForCheck(window);
            Pump(() => window.PrintButton.IsEnabled, 2000);
            Check(window.MakingBarcode && window.HasCode && window.PrintButton.IsEnabled, "a barcode should print too");
        }

        // Scan from anywhere: a tray icon and Ctrl+Shift+Q while b0r3d QR is running, scanning the screen without
        // opening the window first when hidden in the tray, staying hidden on Esc, and opening on the Scan tab on a code
        static void ScanFromAnywhere()
        {
            string data = DataFolder.Path;
            DataFolder.Reset(Path.Combine(Work, "scan-anywhere"));
            try
            {
                MainWindow window = NewMain();
                Check(window.Tray != null && !window.Tray.Active && !window.ScanAnywhereMenu.IsChecked && window.ScanAnywhereCheck.IsChecked == false,
                    "Scan from anywhere is off until it's turned on (it takes Ctrl+Shift+Q from every other program)");
                window.ScanAnywhereCheck.IsChecked = true;
                Check(window.Tray.Active && window.ScanAnywhereMenu.IsChecked && Settings.Load().ScanAnywhere,
                    "turning it on on the Scan tab turns on the menu item, the tray and the saved setting");
                // the choice is kept as scan_from_anywhere; 1.3's scan_anywhere=on, which it saved for everyone who ran it,
                // leaves it off, and so do an ini without the line and no ini
                string ini = Path.Combine(DataFolder.Path, Settings.FileName);
                string saved = File.ReadAllText(ini);
                Check(saved.Contains("scan_from_anywhere=on") && !saved.Contains("scan_anywhere=") && new MainWindow(Settings.Load()).Tray.Active,
                    "the choice should be saved as scan_from_anywhere=on, and stay on");
                File.WriteAllText(ini, "look=light\r\nwhole_pixels=off\r\nscan_anywhere=on\r\n");
                Check(!Settings.Load().ScanAnywhere && !new MainWindow(Settings.Load()).Tray.Active, "1.3's scan_anywhere=on (saved for everyone) should leave it off");
                File.WriteAllText(ini, "look=light\r\nwhole_pixels=off\r\nscan_from_anywhere=off\r\n");
                Check(!Settings.Load().ScanAnywhere, "scan_from_anywhere=off should be off");
                File.WriteAllText(ini, "look=light\r\nwhole_pixels=off\r\n");
                Check(!Settings.Load().ScanAnywhere && !new Settings().ScanAnywhere, "an ini without the line, and no ini, should leave it off");
                window.ScanAnywhereCheck.IsChecked = false;
                Check(!window.Tray.Active && !window.ScanAnywhereMenu.IsChecked && !Settings.Load().ScanAnywhere,
                    "turning it off on the Scan tab turns off the menu item, the tray and the saved setting");
                window.OnHideToTray(null, null);
                Check(window.HiddenInTray && window.Tray.Active && window.ScanAnywhereCheck.IsChecked == true && window.ScanAnywhereMenu.IsChecked,
                    "hiding in the tray turns Scan from anywhere on");

                // the tray's cup icon
                IntPtr icon = Tray.MakeIcon(32);
                Check(icon != IntPtr.Zero, "couldn't make the tray's cup icon from the exe's resources");
                Tray.DestroyIcon(icon);

                // scanning from the tray while on the Make tab: Esc leaves the window hidden in the tray on the Make tab;
                // a chosen screen with a code brings the window back on the Scan tab with the code
                var screen = new Screens.Screen { Bounds = new Int32Rect(0, 0, 300, 200), Scale = 1, Main = true, Number = 1 };
                byte[] pixels = Canvas(300, 200);
                PutQr(pixels, 300, "https://b0r3d.org/anywhere", 60, 10, 4);
                BitmapSource shot = GrayPicture(pixels, 300, 200);
                SnipWindow activeSnip = null;
                window.MakeSnips = () =>
                {
                    activeSnip = new SnipWindow(screen, shot, true);
                    return new List<SnipWindow> { activeSnip };
                };
                window.Tabs.SelectedItem = window.MakeTab;
                window.Tray.TriggerScan();
                Check(window.Scanning && activeSnip != null && window.HiddenInTray && window.Tabs.SelectedItem == window.MakeTab,
                    "starting a scan from the tray opens the overlay without showing the window first");
                activeSnip.Close();
                Pump(() => !window.Scanning, 3000);
                Check(window.HiddenInTray && window.Tabs.SelectedItem == window.MakeTab && window.Found.Count == 0,
                    "cancelling a scan from the tray leaves b0r3d QR hidden in the tray");

                window.Tray.TriggerScan();
                Check(window.Scanning && activeSnip != null, "second scan from the tray");
                activeSnip.Drag(new Point(40, 40), new Point(41, 41));
                Pump(() => !window.Scanning, 10000);
                Check(!window.HiddenInTray && window.Tabs.SelectedItem == window.ScanTab && window.Found.Count == 1 && window.Found[0].Text == "https://b0r3d.org/anywhere",
                    "a code scanned from the tray opens the Scan tab with the code: " + window.ScanStatus.Text);
            }
            finally
            {
                DataFolder.Reset(data);
            }
        }

        // File → Save PDF reads the file back from the disk through Windows' PDF reader and scans it, as a batch's are,
        // for a QR code and a barcode, and the bottom line says so; one that reads as something else says that
        static void SavedPdfsReadBack()
        {
            string folder = Path.Combine(Work, "saved-pdfs");
            Fresh(folder);
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org/saved");
            void SavePdf(string name)
            {
                window.AskWhereStandIn = extension => Path.Combine(folder, name + extension);
                Commands.SavePdf.Execute(null, window);
                Pump(() => window.PdfReadBack != null, 15000);
            }
            SavePdf("qr");
            Check(File.Exists(Path.Combine(folder, "qr.pdf")) && window.PdfReadBack == "ok" && window.Note.Text == "Saved qr.pdf, read back and scanned.",
                "a QR code's PDF should be read back: " + window.PdfReadBack + " · " + window.Note.Text);
            window.ChooseCodeType(Barcodes.BarcodeType.Ean13, "400638133393");
            WaitForCheck(window);
            SavePdf("ean");
            Check(File.Exists(Path.Combine(folder, "ean.pdf")) && window.PdfReadBack == "ok" && window.Note.Text == "Saved ean.pdf, read back and scanned.",
                "a barcode's PDF should be read back with its own reader: " + window.PdfReadBack + " · " + window.Note.Text);
            window.ReadBackPdf(Path.Combine(folder, "qr.pdf"), (grays, width, height) => ScanCheck.Read(grays, width, height)?.Text, "something else");
            Pump(() => window.PdfReadBack != null, 15000);
            Check(window.PdfReadBack == "different" && window.Note.Text.StartsWith("Saved qr.pdf, but it didn't scan when read back", StringComparison.Ordinal),
                "a PDF that reads as other text should say so: " + window.PdfReadBack + " · " + window.Note.Text);
            window.AskWhereStandIn = null;
        }

        // The tray with Windows, with a stand-in for Shell_NotifyIcon (no icon is added to this PC's taskbar,
        // which Windows would remember) and, for the hotkey, Ctrl+Shift with F24, which no program holds,
        // in place of Q: so the check that a hotkey can't be taken twice always runs.
        static void TrayWithWindows()
        {
            string data = DataFolder.Path;
            DataFolder.Reset(Path.Combine(Work, "tray"));
            Directory.CreateDirectory(DataFolder.Path);
            var told = new List<uint>();   // what Shell_NotifyIcon was asked: 0 add, 1 change, 2 delete
            bool takes = true;
            Tray.NotifyStandIn = message => { told.Add(message); return takes; };
            uint key = Tray.HotkeyKey;
            Tray.HotkeyKey = 0x87;   // F24
            MainWindow window = null;
            try
            {
                // Windows' NOTIFYICONDATAW at its full size, which Windows knows (the one before stopped after the tip)
                int size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Tray.NotifyIconData));
                Check(size == (IntPtr.Size == 8 ? 976 : 956), "NOTIFYICONDATAW should be 976 bytes (956 in 32-bit), not " + size);

                // a hotkey held by one Tray can't be taken by a second; with it held elsewhere, the window says so
                using (var first = new Tray(NewMain()))
                using (var second = new Tray(NewMain()))
                {
                    Check(first.TryRegisterHotkeyOnly() && first.HotkeyRegistered && !second.TryRegisterHotkeyOnly(),
                        "while one Tray holds the hotkey, a second shouldn't be able to take it");
                    window = new MainWindow(new Settings { ScanAnywhere = true });
                    LayOut(window.Root, MainWidth, MainHeight);
                    window.StartTray();   // as when the window is on screen (it isn't shown here)
                    Check(!window.Tray.HotkeyRegistered && window.Tray.IconShown && window.ScanAnywhereNote.Text.Contains("Another program is using " + Tray.HotkeyText),
                        "with the hotkey held elsewhere, the tray works and the window says so: " + window.ScanAnywhereNote.Text);
                }

                // b0r3d QR's hidden window is a top-level one (a message-only one never hears Explorer start again),
                // never shown; when Explorer starts again (TaskbarCreated), the icon is added again
                IntPtr hidden = window.Tray.Handle;
                Check(hidden != IntPtr.Zero && GetAncestor(hidden, 1) == GetDesktopWindow() && !IsWindowVisible(hidden),
                    "the tray's window should be a hidden top-level window");
                told.Clear();
                SendMessageW(hidden, (uint)Tray.TaskbarCreated, IntPtr.Zero, IntPtr.Zero);
                Check(told.SequenceEqual(new uint[] { 0 }) && window.Tray.IconShown, "after Explorer starts again, the icon should be added again: " + string.Join(", ", told));

                // Hide in the tray only with the icon there to come back by: when Windows won't take it, the window stays
                takes = false;
                SendMessageW(hidden, (uint)Tray.TaskbarCreated, IntPtr.Zero, IntPtr.Zero);
                window.OnHideToTray(null, null);
                Check(!window.Tray.IconShown && !window.HiddenInTray && window.Note.Text == "Windows didn't add the tray icon, so b0r3d QR stays open." &&
                      window.ScanAnywhereNote.Text.StartsWith("Windows didn't add the tray icon", StringComparison.Ordinal),
                    "with no icon, Hide in the tray should leave the window open and say why: " + window.Note.Text + " / " + window.ScanAnywhereNote.Text);
                takes = true;
                SendMessageW(hidden, (uint)Tray.TaskbarCreated, IntPtr.Zero, IntPtr.Zero);
                window.OnHideToTray(null, null);
                Check(window.Tray.IconShown && window.HiddenInTray, "with the icon back, Hide in the tray hides the window");
                told.Clear();
                MainWindow closing = window;
                window = null;
                closing.Close();
                Check(told.SequenceEqual(new uint[] { 2 }) && !IsWindow(hidden), "closing should take the icon away and the hidden window with it: " + string.Join(", ", told));
            }
            finally
            {
                window?.Close();
                Tray.NotifyStandIn = null;
                Tray.HotkeyKey = key;
                DataFolder.Reset(data);
            }
        }

        // One b0r3d QR a data folder: the claim is the first copy's alone, by the folder however it's written;
        // another folder is another copy's; started again, it finds the running one's hidden window and has it
        // show itself
        static void OneCopyADataFolder()
        {
            string data = DataFolder.Path;
            string folder = Path.Combine(Work, "one copy");
            Directory.CreateDirectory(folder);
            DataFolder.Reset(folder);
            Check(SingleCopy.Name(folder) == SingleCopy.Name(folder.ToUpperInvariant() + "\\") && SingleCopy.Name(folder) != SingleCopy.Name(folder + " 2") &&
                  SingleCopy.Name(folder).StartsWith("b0r3dQR ", StringComparison.Ordinal),
                "a data folder's name: the same however it's written, another for another folder: " + SingleCopy.Name(folder));
            Check(SingleCopy.Claim(folder) == SingleCopy.Claimed.First, "the first copy for a data folder should get it");
            try
            {
                Check(SingleCopy.Claim(folder.ToUpperInvariant()) == SingleCopy.Claimed.Running, "a second copy for the same data folder shouldn't");
                // the running copy: its hidden window, titled for the folder, hidden in the tray
                MainWindow running = NewMain();
                running.Tray.Open();
                running.OnHideToTray(null, null);
                Check(running.HiddenInTray && FindWindowW(null, SingleCopy.WindowTitle) == running.Tray.Handle, "the running copy's hidden window should go by the folder's name");
                Check(SingleCopy.BringForward(2000), "a copy started again should find the running one's window, and it should answer");
                Pump(() => !running.HiddenInTray, 3000);
                Check(!running.HiddenInTray, "the running copy should show itself when another is started");
                running.Close();
                var gone = Stopwatch.StartNew();
                Check(!SingleCopy.BringForward(300) && gone.ElapsedMilliseconds < 2000, "with no copy's window there (closing, or still starting), it says it couldn't, after the wait");
            }
            finally
            {
                SingleCopy.Release();
                DataFolder.Reset(data);
            }
            Check(SingleCopy.Claim(folder) == SingleCopy.Claimed.First, "once the first copy has ended, the next one gets the folder");
            SingleCopy.Release();
            Check(SingleCopy.Claim(folder + " 2") == SingleCopy.Claimed.First, "a copy with a data folder of its own runs side by side");
            SingleCopy.Release();

            // a copy running as administrator: its claim is there, but this one may not open it (as a mutex this user is
            // denied stands in for): a copy running, never a crash at start
            string elevated = folder + " as administrator";
            var denied = new System.Security.AccessControl.MutexSecurity();
            denied.AddAccessRule(new System.Security.AccessControl.MutexAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User,
                System.Security.AccessControl.MutexRights.FullControl, System.Security.AccessControl.AccessControlType.Deny));
            using (var theirs = new System.Threading.Mutex(false, @"Local\" + SingleCopy.Name(elevated), out bool made, denied))
            {
                Check(made && SingleCopy.Claim(elevated) == SingleCopy.Claimed.Running, "a claim this copy may not open should count as a copy running");
            }

            // a copy running but not answering (busy or hung): its window found, no answer in time, said
            Check(SingleCopy.Claim(folder + " busy") == SingleCopy.Claimed.First, "a fresh folder's claim");
            try
            {
                var ready = new System.Threading.ManualResetEventSlim();
                bool answering = false;
                var busy = new System.Threading.Thread(() =>
                {
                    var hidden = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters(SingleCopy.WindowTitle)
                    {
                        Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000),
                    });
                    ready.Set();
                    while (!System.Threading.Volatile.Read(ref answering)) System.Threading.Thread.Sleep(10);   // handles no message meanwhile
                    hidden.Dispose();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
                });
                busy.SetApartmentState(System.Threading.ApartmentState.STA);
                busy.IsBackground = true;
                busy.Start();
                ready.Wait(5000);
                var waited = Stopwatch.StartNew();
                bool brought = SingleCopy.BringForward(1000, 300);
                long ms = waited.ElapsedMilliseconds;
                System.Threading.Volatile.Write(ref answering, true);
                busy.Join(5000);
                Check(!brought && ms >= 250 && ms < 2000, "a running copy that doesn't answer should be said, after " + ms + " ms");
            }
            finally
            {
                SingleCopy.Release();
            }
            Check(SingleCopy.NotBroughtForward.StartsWith("b0r3d QR is already running, but it didn't come to the front", StringComparison.Ordinal), "what's said then");
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr GetDesktopWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr FindWindowW(string className, string windowName);
    }
}
