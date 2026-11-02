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
            Unique("the Scan tab", always.Concat(new[] { window.ScanScreenButton, window.OpenPictureButton, window.PastePictureButton, window.ScanFolderButton, window.ClearButton, window.CancelScanButton }.Select(b => Key(b.Content))));
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
            foreach (string part in new[] { "Commons Clause", "MIT License", "Licensor: b0r3d.org", "ZXing.Net", "Apache License", "zint 2.16.0", "BSD 3-Clause", "Grandzebu" })
                Check(licences.Contains(part), "the licences don't mention " + part);
            Check(!licences.Contains("is missing from this build"), "a licence file is missing from the build");
            Check(about.FolderText.Text.StartsWith(DataFolder.Path), "the settings folder line: " + about.FolderText.Text);
        }
    }
}
