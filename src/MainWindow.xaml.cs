using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using B0r3dQR.Qr;

namespace B0r3dQR
{
    public partial class MainWindow : Window
    {
        readonly Settings settings;
        QrCode code;        // null while there's nothing to show
        QrShapes shapes;
        int ticket;         // goes up with every new code; a check for an older one is ignored
        readonly bool ready;
        readonly DispatcherTimer typingPause, noteTimer;
        Point dragStart;
        bool dragArmed;
        bool maximized;     // the window's last state that wasn't minimized, for closing from the taskbar

        // for the tests: a pretend screen scaling (1.5 = 150 %), how the last check came out, the code
        internal double? ScaleOverride;
        internal string CheckResult { get; private set; } = "";
        internal QrCode Code => code;
        internal bool HasCode => shapes != null;
        internal int Refreshes => ticket;

        internal MainWindow(Settings settings)
        {
            this.settings = settings;
            InitializeComponent();
            VersionRun.Text = Program.Name + " " + Program.FullVersion;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            if (settings.Maximized) WindowState = WindowState.Maximized;
            maximized = settings.Maximized;
            StateChanged += (s, e) => { if (WindowState != WindowState.Minimized) maximized = WindowState == WindowState.Maximized; };
            LevelButton(settings.Level).IsChecked = true;
            QuietBox.Value = settings.QuietZone;
            SizeBox.Value = settings.PictureSize;
            CheckLine.ToolTip = "Every change is drawn the way it's saved and read back with a scanner (ZXing), the way a phone camera would read it.";

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
            ShowLook();
            ShowNote();
            ShowCup();
            ready = true;
            Refresh();
        }

        // Brings what's typed and any half-typed number up to date, so a save, copy or drag never
        // uses the input from before (Ctrl+S straight after typing, or a picture size not yet entered).
        internal void CatchUp()
        {
            QuietBox.Commit();
            SizeBox.Commit();
            if (typingPause.IsEnabled)
            {
                typingPause.Stop();
                Refresh();
            }
        }

        string DataText => DataBox.Text.Replace("\r\n", "\n");

        QrLevel Level =>
            LevelL.IsChecked == true ? QrLevel.L : LevelM.IsChecked == true ? QrLevel.M : LevelH.IsChecked == true ? QrLevel.H : QrLevel.Q;

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

        double Scale => ScaleOverride ?? VisualTreeHelper.GetDpi(this).DpiScaleX;

        // whole pixels per module for saved pictures, from Picture size
        int PicturePixelsPerModule => QrPicture.PixelsPerModule(SizeBox.Value, shapes.Width);

        // makes the code again from the text and the options, and shows it
        internal void Refresh()
        {
            ticket++;
            DataHint.Visibility = DataBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            LevelHint.Text = LevelText(Level);
            code = null;
            shapes = null;
            string text = DataText;
            if (text.Length == 0)
            {
                ShowStage("Type or paste something on the left to make a code.", false);
            }
            else
            {
                try
                {
                    code = QrCode.Make(text, Level);
                    shapes = QrShapes.For(code, QuietBox.Value);
                }
                catch (QrException e)
                {
                    code = null;
                    ShowStage(e.Message, true);
                }
            }

            if (shapes != null)
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
            }
            ShowSize();
            CommandManager.InvalidateRequerySuggested();
        }

        static string LevelText(QrLevel level)
        {
            switch (level)
            {
                case QrLevel.L: return "Low: about 7% of the code can be damaged or covered and it still reads. The smallest codes.";
                case QrLevel.M: return "Medium: about 15% of the code can be damaged or covered and it still reads.";
                case QrLevel.H: return "High: about 30% of the code can be damaged or covered and it still reads. The biggest codes.";
                default: return "Quartile: about 25% of the code can be damaged or covered and it still reads. A good choice for print.";
            }
        }

        void ShowStage(string text, bool problem)
        {
            StageNote.Text = text;
            StageNote.SetResourceReference(TextBlock.ForegroundProperty, problem ? "Bad" : "Dim");
            StageNote.Visibility = Visibility.Visible;
        }

        // The preview: the code drawn at whole device pixels per module, as big as fits, shown
        // pixel for pixel, so it's sharp at any scaling (the 150 % TV included).
        void RenderPreview()
        {
            if (shapes == null) return;
            const double margin = 20;
            double room = Math.Min(Stage.ActualWidth, Stage.ActualHeight) - 2 * margin;
            if (room <= 0)
            {
                Preview.Source = null;  // not laid out yet: the stage's SizeChanged brings it back
                return;
            }
            double scale = Scale;
            int devicePixels = (int)Math.Floor(room * scale);
            int perModule = Math.Max(1, devicePixels / shapes.Width);
            BitmapSource picture = QrPicture.Render(shapes, perModule);
            Preview.Source = picture;
            Preview.Stretch = Stretch.Fill;
            if (picture.PixelWidth <= devicePixels)
            {
                Preview.Width = picture.PixelWidth / scale;
                Preview.Height = picture.PixelHeight / scale;
            }
            else
            {
                Preview.Width = room;  // a tiny window: shrink to fit
                Preview.Height = room;
            }
        }

        void ShowCup() => CupPicture.Source = Cup.For(32, Scale);

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            RenderPreview();
            ShowCup();
        }

        // The scan check: the code drawn as a saved picture is, then read back off the window's
        // thread. A quiet zone under 4 modules gets a second try with room around it.
        void StartCheck()
        {
            int mine = ticket;
            QrCode current = code;
            ShowCheck("checking", "Checking it reads back…", "Dim");
            byte[] grays = ScanCheck.Grays(QrPicture.Render(shapes, ScanCheck.PixelsPerModule), out int width, out int height);
            byte[] roomy = null;
            int roomyWidth = 0, roomyHeight = 0;
            if (shapes.QuietZone < ScanCheck.StandardQuietZone)
                roomy = ScanCheck.Grays(QrPicture.Render(QrShapes.For(current, ScanCheck.StandardQuietZone), ScanCheck.PixelsPerModule), out roomyWidth, out roomyHeight);
            Task.Run(() =>
            {
                ScanCheck.Reading asIs = ScanCheck.Read(grays, width, height);
                ScanCheck.Reading withRoom = roomy != null && (asIs == null || asIs.Text != current.Text) ? ScanCheck.Read(roomy, roomyWidth, roomyHeight) : null;
                return new[] { asIs, withRoom };
            }).ContinueWith(done => Dispatcher.BeginInvoke(new Action(() => ShowCheckResult(mine, current, done))));
        }

        void ShowCheckResult(int mine, QrCode current, Task<ScanCheck.Reading[]> done)
        {
            if (mine != ticket) return;  // the code has changed since
            if (done.IsFaulted)
            {
                ShowCheck("failed", "✗ The check couldn't run: " + done.Exception.GetBaseException().Message, "Bad");
                return;
            }
            ScanCheck.Reading asIs = done.Result[0], withRoom = done.Result[1];
            if (asIs != null && asIs.Text == current.Text) ShowCheck("ok", "✓ Reads back exactly", "Good");
            else if (withRoom != null && withRoom.Text == current.Text) ShowCheck("needs-room", "⚠ Reads back only with white space around it", "Warn");
            else if (asIs != null) ShowCheck("different", "✗ Reads back as different text", "Bad");
            else ShowCheck("failed", "✗ Didn't read back", "Bad");
        }

        void ShowCheck(string result, string text, string colour)
        {
            CheckResult = result;
            CheckLine.Text = text;
            CheckLine.SetResourceReference(TextBlock.ForegroundProperty, colour);
        }

        void ShowInfo()
        {
            CultureInfo c = CultureInfo.CurrentCulture;
            int chars = code.CharacterCount, bytes = code.ByteCount;
            InfoLine.Text = chars.ToString("N0", c) + (chars == 1 ? " character" : " characters") + " · " +
                bytes.ToString("N0", c) + (bytes == 1 ? " byte" : " bytes") + (code.IsPlainAscii ? "" : " (UTF-8)") + " · " +
                "version " + code.Version + " · " + code.Size + " × " + code.Size + " modules · level " + code.Level;
            bool tight = shapes.QuietZone < ScanCheck.StandardQuietZone;
            WarnLine.Text = tight ? "Quiet zone under 4 modules: leave white space around the code wherever you put it, or scanners may not find it." : "";
            WarnLine.Visibility = tight ? Visibility.Visible : Visibility.Collapsed;
        }

        void ShowSize()
        {
            if (shapes == null)
            {
                SizeHint.Text = "Saved pictures come out at whole pixels per module, so every module is sharp.";
                return;
            }
            int perModule = PicturePixelsPerModule;
            string pixels = (perModule * shapes.Width).ToString("N0", CultureInfo.CurrentCulture);
            SizeHint.Text = "Saves as " + pixels + " × " + pixels + " pixels: " + perModule + (perModule == 1 ? " pixel" : " pixels") + " per module, so every module is sharp.";
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

        void OnDataChanged(object sender, TextChangedEventArgs e)
        {
            if (!ready) return;
            DataHint.Visibility = DataBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            typingPause.Stop();
            typingPause.Start();
        }

        void OnLevel(object sender, RoutedEventArgs e)
        {
            if (ready) Refresh();
        }

        void OnQuietChanged(object sender, EventArgs e)
        {
            if (ready) Refresh();
        }

        void OnSizeChanged(object sender, EventArgs e)
        {
            if (ready) ShowSize();
        }

        void OnStageSize(object sender, SizeChangedEventArgs e) => RenderPreview();

        // there's a code, or one is on its way (typed a moment ago); asked while the window is
        // still being built too, before the typing timer exists
        void WhenThereIsACode(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = shapes != null || typingPause != null && typingPause.IsEnabled && DataBox.Text.Length > 0;
            e.Handled = true;
        }

        void OnSavePng(object sender, ExecutedRoutedEventArgs e)
        {
            CatchUp();
            if (shapes == null) return;
            QrShapes these = shapes;
            int perModule = PicturePixelsPerModule;
            string path = AskWhere("Save the code as a PNG picture", "PNG picture", ".png");
            if (path != null) Save(path, () => QrPicture.Png(QrPicture.Render(these, perModule)));
        }

        void OnSaveSvg(object sender, ExecutedRoutedEventArgs e)
        {
            CatchUp();
            if (shapes == null) return;
            QrShapes these = shapes;
            int pixels = PicturePixelsPerModule * shapes.Width;
            string path = AskWhere("Save the code as an SVG drawing", "SVG drawing", ".svg");
            if (path != null) Save(path, () => new UTF8Encoding(false).GetBytes(QrPicture.Svg(these, pixels)));
        }

        string AskWhere(string title, string typeName, string extension)
        {
            try
            {
                return SaveDialog.Ask(this, title, typeName, extension, Exports.SuggestedName(code.Text) + extension);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        void Save(string path, Func<byte[]> bytes)
        {
            try
            {
                Exports.Write(path, bytes());
                Flash("Saved " + Path.GetFileName(path), "Good");
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException)
            {
                MessageBox.Show(this, "Couldn't save " + Path.GetFileName(path) + ":\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OnCopyPicture(object sender, ExecutedRoutedEventArgs e)
        {
            CatchUp();
            if (shapes == null) return;
            BitmapSource picture = QrPicture.Render(shapes, PicturePixelsPerModule);
            try
            {
                Clipboard.SetDataObject(Exports.DataFor(picture, QrPicture.Png(picture), Exports.SuggestedName(code.Text), false), true);
                Flash("Copied the code as a picture", "Good");
            }
            catch (ExternalException)
            {
                Flash("Couldn't copy: another program is using the clipboard. Try again.", "Bad");
            }
        }

        void OnPreviewDown(object sender, MouseButtonEventArgs e)
        {
            if (shapes == null) return;
            dragStart = e.GetPosition(this);
            dragArmed = true;
        }

        void OnPreviewMove(object sender, MouseEventArgs e)
        {
            if (!dragArmed) return;
            if (e.LeftButton != MouseButtonState.Pressed || shapes == null)
            {
                dragArmed = false;
                return;
            }
            Point now = e.GetPosition(this);
            if (Math.Abs(now.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(now.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragArmed = false;
            CatchUp();
            if (shapes == null) return;
            BitmapSource picture = QrPicture.Render(shapes, PicturePixelsPerModule);
            DataObject data = Exports.DataFor(picture, QrPicture.Png(picture), Exports.SuggestedName(code.Text), true);
            try
            {
                DragDrop.DoDragDrop(Preview, data, DragDropEffects.Copy);
            }
            catch (ExternalException)
            {
                // the program it was dropped on failed; there's nothing to undo here
            }
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

        void OnAbout(object sender, ExecutedRoutedEventArgs e)
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

        // the options, the look and the window's size, into b0r3dQR.ini
        internal void SaveSettings()
        {
            QuietBox.Commit();
            SizeBox.Commit();
            settings.Level = Level;
            settings.QuietZone = QuietBox.Value;
            settings.PictureSize = SizeBox.Value;
            settings.Look = Theme.Choice;
            settings.Maximized = WindowState == WindowState.Maximized || WindowState == WindowState.Minimized && maximized;
            Rect normal = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (!normal.IsEmpty && normal.Width > 0 && normal.Height > 0)
            {
                settings.WindowWidth = normal.Width;
                settings.WindowHeight = normal.Height;
            }
            settings.Save();
            ShowNote();
        }
    }
}
