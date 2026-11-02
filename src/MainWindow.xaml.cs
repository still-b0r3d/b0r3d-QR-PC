using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace B0r3dQR
{
    // The window: the Make tab (MainWindow.Make.cs), the Scan tab (MainWindow.Scan.cs), the menus,
    // the bottom line, and keeping the settings.
    public partial class MainWindow : Window
    {
        readonly Settings settings;
        readonly bool ready;
        readonly DispatcherTimer typingPause, noteTimer;
        bool maximized;     // the window's last state that wasn't minimized, for closing from the taskbar

        // for the tests: a pretend screen scaling (1.5 = 150 %)
        internal double? ScaleOverride;

        internal MainWindow(Settings settings)
        {
            this.settings = settings;
            InitializeComponent();
            VersionRun.Text = Program.Name + " " + Program.VersionText;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            if (settings.Maximized) WindowState = WindowState.Maximized;
            maximized = settings.Maximized;
            StateChanged += (s, e) => { if (WindowState != WindowState.Minimized) maximized = WindowState == WindowState.Maximized; };

            typingPause = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            typingPause.Tick += (s, e) => { typingPause.Stop(); Refresh(); };
            noteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            noteTimer.Tick += (s, e) => { noteTimer.Stop(); ShowNote(); };

            Theme.Changed += ShowLook;
            SourceInitialized += (s, e) => Theme.TitleBar(this);
            Loaded += (s, e) => DataBox.Focus();
            Closing += OnClosing;
            Closed += (s, e) => Theme.Changed -= ShowLook;

            Exports.ClearDragFolder();
            SetUpScan();
            SetUpMake();
            SetUpBarcodes();
            ShowLook();
            ShowNote();
            ShowCup();
            ready = true;
            Refresh();
        }

        double Scale => ScaleOverride ?? VisualTreeHelper.GetDpi(this).DpiScaleX;

        void ShowCup() => CupPicture.Source = Cup.For(28, Scale);

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            RenderPreview();
            ShowCup();
        }

        void ShowLook()
        {
            LookWindows.IsChecked = Theme.Choice == Look.Windows;
            LookLight.IsChecked = Theme.Choice == Look.Light;
            LookDark.IsChecked = Theme.Choice == Look.Dark;
        }

        // the bottom line: why settings aren't kept, or why the last save of them failed
        void ShowNote()
        {
            string why = DataFolder.WhyNotKept ?? DataFolder.LastProblem ?? "";
            Note.Text = why;
            Note.ToolTip = why.Length > 0 ? why : null;
            Note.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
        }

        // a few seconds of news in the bottom line ("Saved b0r3d.org.png")
        void Flash(string text, string colour)
        {
            Note.Text = text;
            Note.ToolTip = null;
            Note.SetResourceReference(TextBlock.ForegroundProperty, colour);
            noteTimer.Stop();
            noteTimer.Start();
        }

        void OnLook(object sender, RoutedEventArgs e)
        {
            var look = (Look)Enum.Parse(typeof(Look), (string)((MenuItem)sender).Tag);
            settings.Look = look;
            Theme.Set(look);
            ShowLook();
            settings.Save();
            ShowNote();
        }

        void OnAbout(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        void OnMaker(object sender, RoutedEventArgs e) => OpenInBrowser(this, Program.MakerUrl);

        // a link opens in your browser; b0r3d QR itself never goes online
        internal static void OpenInBrowser(Window owner, string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception x) when (x is Win32Exception || x is InvalidOperationException)
            {
                MessageBox.Show(owner, "Couldn't open " + url + ":\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OnExit(object sender, RoutedEventArgs e) => Close();

        void OnClosing(object sender, CancelEventArgs e)
        {
            SaveSettings();
            Exports.ClearDragFolder();
        }

        // the look, the window's size, print size and the design (never its text) into the data folder
        internal void SaveSettings()
        {
            CatchUp();
            settings.Level = design.Level;
            settings.QuietZone = (int)Math.Round(design.Margin);
            settings.PictureSize = (int)Math.Round(Math.Max(design.Width, design.Height));
            settings.Look = Theme.Choice;
            settings.Print = print.Clone();
            settings.WholePixels = wholePixels;
            settings.BarcodeLook = look.Clone();
            settings.BarcodePrint = barcodePrint.Clone();
            settings.Maximized = WindowState == WindowState.Maximized || WindowState == WindowState.Minimized && maximized;
            Rect normal = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (!normal.IsEmpty && normal.Width > 0 && normal.Height > 0)
            {
                settings.WindowWidth = normal.Width;
                settings.WindowHeight = normal.Height;
            }
            settings.Save();
            KeepDesign();
            ShowNote();
        }
    }
}
