using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using B0r3dQR.Barcodes;
using B0r3dQR.Design;
using B0r3dQR.Qr;

namespace B0r3dQR
{
    // Recent codes, as the website lists them: the codes saved, copied or dragged out, newest first,
    // each with a picture (none for a Wi-Fi network with its password), to open again or delete;
    // the switch that adds them; and Clear all, which takes a second, separate click.
    public partial class RecentWindow : Window
    {
        readonly Settings settings;
        readonly Func<int, bool> open;        // opens a code in the main window; false when it's gone
        readonly Dictionary<int, ImageSource> pictures = new Dictionary<int, ImageSource>();   // each code drawn once while the window's open
        List<RecentCode> codes = new List<RecentCode>();
        bool confirming, showing, damaged;
        DateTime armedAt;

        // the last code couldn't be kept (the main window says so here)
        internal static bool LastFailed;

        internal RecentWindow(Settings settings, Func<int, bool> open)
        {
            InitializeComponent();
            this.settings = settings;
            this.open = open;
            showing = true;
            RememberCheck.IsChecked = settings.RecentCodes;
            showing = false;
            Refresh();
            SourceInitialized += (s, e) => Theme.TitleBar(this);
        }

        internal IReadOnlyList<RecentCode> Codes => codes;

        internal void Refresh()
        {
            CodesList.Children.Clear();
            List<RecentCode> loaded = RecentCodes.Load(out RecentTrouble trouble);
            codes = loaded ?? new List<RecentCode>();
            damaged = trouble == RecentTrouble.Damaged;
            string problem = null;
            if (!DataFolder.CanKeep) problem = "Recent codes aren't kept here: b0r3d QR can't keep anything in its data folder. " + DataFolder.WhyNotKept;
            else if (trouble == RecentTrouble.Busy) problem = "b0r3dQR recent.json in the data folder couldn't be opened just now; another program may be using it. Close this and open Recent codes again in a moment.";
            else if (trouble == RecentTrouble.Newer) problem = "b0r3dQR recent.json in the data folder is from a newer b0r3d QR, so this one leaves it as it is.";
            else if (damaged) problem = "b0r3dQR recent.json in the data folder can't be read, so it's left as it is. Clear all starts it again.";
            else if (LastFailed) problem = "The last code couldn't be kept: b0r3dQR recent.json couldn't be written. Delete some codes, or check the data folder (Help → About shows where it is).";
            ProblemLine.Text = problem ?? "";
            ProblemLine.Visibility = problem != null ? Visibility.Visible : Visibility.Collapsed;
            EmptyLine.Text = settings.RecentCodes
                ? "Codes you save, copy or drag out will appear here."
                : "No codes are being added. Turn it on below to keep the codes you make.";
            EmptyLine.Visibility = codes.Count == 0 && DataFolder.CanKeep && loaded != null ? Visibility.Visible : Visibility.Collapsed;
            foreach (RecentCode code in codes) CodesList.Children.Add(Row(code));
            RememberCheck.IsEnabled = DataFolder.CanKeep;
            CountLine.Text = codes.Count > 0 ? codes.Count + " of " + RecentCodes.Most + " codes, about " + RecentCodes.SizeText(codes.Sum(c => (long)c.Size)) : "";
            ClearButton.Visibility = codes.Count > 0 || (damaged && DataFolder.CanKeep) ? Visibility.Visible : Visibility.Collapsed;
            if (codes.Count == 0 && !damaged) confirming = false;
            ShowConfirm();
        }

        FrameworkElement Row(RecentCode code)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6), Tag = code.Id };
            var picture = new Border { Width = 56, Height = 56, Margin = new Thickness(0, 0, 12, 0), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Top };
            picture.SetResourceReference(Border.BackgroundProperty, "Stage");
            picture.Child = code.Sensitive ? Lock() : Thumbnail(code);
            DockPanel.SetDock(picture, Dock.Left);
            row.Children.Add(picture);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
            var openButton = new Button { Content = "Open" };
            AutomationProperties.SetName(openButton, "Open " + code.Name);
            openButton.Click += (s, e) => Open(code);
            var deleteButton = new Button { Content = "Delete", Margin = new Thickness(6, 0, 0, 0) };
            AutomationProperties.SetName(deleteButton, "Delete " + code.Name);
            deleteButton.Click += (s, e) => Delete(code);
            buttons.Children.Add(openButton);
            buttons.Children.Add(deleteButton);
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);

            var words = new StackPanel();
            words.Children.Add(new TextBlock { Text = code.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var when = new TextBlock { Text = code.SavedLocal.ToString("g", CultureInfo.CurrentCulture) + (code.Sensitive ? " · picture and password hidden" : ""), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
            when.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
            words.Children.Add(when);
            if (code.Omitted.Count > 0)
            {
                var omitted = new TextBlock
                {
                    Text = code.Omitted.Contains("logo") ? "The logo was too large to keep; add it again after opening." : "The frame background was too large to keep; add it again after opening.",
                    FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
                };
                omitted.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
                words.Children.Add(omitted);
            }
            row.Children.Add(words);
            return row;
        }

        // the code drawn small from its design, as the list's picture of it (a code's id is never
        // used again, so its picture stays right)
        UIElement Thumbnail(RecentCode code)
        {
            if (!pictures.TryGetValue(code.Id, out ImageSource source))
            {
                try
                {
                    if (code.Barcode != null)
                    {
                        // a barcode in its own colours, whole pixels per module
                        BarcodeLook look = code.BarcodeLook;
                        BarcodeSvg made = Barcodes.Barcode.Make(code.BarcodeText, code.Barcode.Value, look.Options, out _);
                        BarcodeArt barcode = made == null ? null : new BarcodeArt(made);
                        source = barcode?.Render(Math.Max(1, Math.Floor(112.0 / Math.Max(barcode.W, barcode.H))), Css.Paint(look.Color),
                                                 look.Transparent ? (Color?)null : Css.Paint(look.Background));
                    }
                    else
                    {
                        var art = new QrArt(DesignFile.Read(code.Design, new QrDesign()));
                        double k = 112 / Math.Max(art.W, art.H);
                        source = art.Render(k, (int)Math.Ceiling(art.W * k), (int)Math.Ceiling(art.H * k));
                    }
                }
                catch (Exception e) when (e is DesignFileException || e is QrException)
                {
                    source = null;
                }
                pictures[code.Id] = source;
            }
            if (source == null) return null;
            var image = new Image { Source = source, Stretch = Stretch.Uniform, Margin = new Thickness(4) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        // a padlock, for a code with a password in it
        static UIElement Lock()
        {
            var path = new Path
            {
                Data = Geometry.Parse("M6,11 H18 V20 H6 Z M8.5,11 V8 A3.5,3.5 0 0 1 15.5,8 V11"),
                StrokeThickness = 1.5, Width = 28, Height = 28, Stretch = Stretch.Uniform,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            path.SetResourceReference(Shape.StrokeProperty, "Dim");
            return path;
        }

        void Say(string text)
        {
            StatusLine.Text = text;
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.CreatePeerForElement(StatusLine)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        internal void Open(RecentCode code)
        {
            confirming = false;
            if (open(code.Id))
            {
                Close();
                return;
            }
            Say("That code isn't here any more. It may have been deleted in another b0r3d QR window.");
            Refresh();
        }

        internal void Delete(RecentCode code)
        {
            confirming = false;
            int index = codes.FindIndex(c => c.Id == code.Id);
            bool deleted = RecentCodes.Delete(code.Id);
            if (deleted) LastFailed = false;
            Say(deleted ? "Deleted " + code.Name + "." : "Couldn't delete it. Try again.");
            Refresh();
            FocusAfter(index);
        }

        // the next code's Open button, or the line that says there are none
        void FocusAfter(int index)
        {
            if (codes.Count == 0)
            {
                EmptyLine.Focus();
                return;
            }
            if (CodesList.Children[Math.Max(0, Math.Min(index, codes.Count - 1))] is DockPanel row && row.Children[1] is StackPanel buttons) buttons.Children[0].Focus();
        }

        void OnClear(object sender, RoutedEventArgs e) => ClearAll();

        // The first click asks; the second, a separate click at least half a second later, deletes
        // them all (the button that confirms is where Clear all was, so a double-click can't).
        internal void ClearAll(bool now = false)
        {
            if (!confirming)
            {
                confirming = true;
                armedAt = DateTime.UtcNow;
                ShowConfirm();
                return;
            }
            if (!now && (DateTime.UtcNow - armedAt).TotalMilliseconds < 500) return;
            confirming = false;
            bool cleared = RecentCodes.Clear();
            if (cleared) LastFailed = false;
            Say(cleared ? "All recent codes deleted." : "Couldn't delete them. Try again.");
            Refresh();
            EmptyLine.Focus();
        }

        void OnCancelClear(object sender, RoutedEventArgs e)
        {
            confirming = false;
            ShowConfirm();
            ClearButton.Focus();
        }

        void ShowConfirm()
        {
            ClearButton.Content = !confirming ? "C_lear all" : damaged ? "Empty the file" : "Delete all " + codes.Count + " codes";
            if (confirming) ClearButton.SetResourceReference(ForegroundProperty, "Bad");
            else ClearButton.ClearValue(ForegroundProperty);
            CancelClearButton.Visibility = confirming ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnRemember(object sender, RoutedEventArgs e)
        {
            if (showing) return;
            bool on = RememberCheck.IsChecked == true;
            settings.RecentCodes = on;
            if (!settings.Save())
            {
                if (!on)
                {
                    // off for this run at least: it's the text typed that's kept
                    LastFailed = false;
                    Say("New codes won’t be added while b0r3d QR is open, but the setting couldn't be saved for next time.");
                    Refresh();
                    return;
                }
                settings.RecentCodes = false;
                showing = true;
                RememberCheck.IsChecked = false;
                showing = false;
                Say("Couldn't change this setting. Try again.");
                return;
            }
            if (!on) LastFailed = false;
            Say(on ? "New codes will be added here." : "New codes won’t be added. The ones above stay until you delete them.");
            Refresh();
        }
    }
}
