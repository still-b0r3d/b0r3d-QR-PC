using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using B0r3dQR.Scan;

namespace B0r3dQR
{
    // The Scan tab: reading codes off the screen and from pictures, folders, the clipboard and drops.
    public partial class MainWindow
    {
        // what a scan found, in reading order (in a batch of pictures, with a line for each one
        // that had no code or couldn't be read), and the line that sums it up
        internal sealed class ScanOutcome
        {
            public List<FoundCode> Codes = new List<FoundCode>();
            public string Summary;
        }

        const int MaxShown = 500;

        readonly ObservableCollection<FoundCode> found = new ObservableCollection<FoundCode>();
        CancellationTokenSource scanning;   // the reading going on now, or null
        List<SnipWindow> snips;             // set while the screens wait for a box
        Tray tray;                          // the tray icon and Ctrl+Shift+Q while b0r3d QR is running
        bool scanAnywhere = true;
        bool windowOnScreen, hiddenInTray, fromAnywhere;
        bool steppedAside;                  // hidden while the screens are pictured
        bool draggingOut;                   // the code is being dragged out of this window
        string madeAgain;                   // the scanned text given to the Make tab, made exactly as it was

        // for the tests: stands in for capturing and covering the real screens
        internal Func<List<SnipWindow>> MakeSnips;
        internal ObservableCollection<FoundCode> Found => found;
        internal bool Scanning => scanning != null || snips != null;
        internal Tray Tray => tray;
        internal bool HiddenInTray => hiddenInTray;

        void SetUpScan()
        {
            FoundList.ItemsSource = found;
            found.CollectionChanged += (s, e) => FoundHint.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            tray = new Tray(this);
            SetScanAnywhere(settings.ScanAnywhere, false);
        }

        internal void StartTray()
        {
            windowOnScreen = true;
            tray.SetActive(scanAnywhere, true);
            ShowScanAnywhereNote();
        }

        void StopTray() => tray?.Dispose();

        internal void SetScanAnywhere(bool on, bool save = true)
        {
            scanAnywhere = on;
            ScanAnywhereMenu.IsChecked = on;
            ScanAnywhereCheck.IsChecked = on;
            tray?.SetActive(on, windowOnScreen);
            ShowScanAnywhereNote();
            if (save && ready)
            {
                settings.ScanAnywhere = on;
                settings.Save();
                ShowNote();
            }
        }

        void ShowScanAnywhereNote()
        {
            ScanAnywhereNote.Text = !scanAnywhere
                ? "Turn this on to keep an icon in the tray and use " + Tray.HotkeyText + " in any program while b0r3d QR is running."
                : windowOnScreen && tray != null && !tray.HotkeyRegistered
                ? "The tray icon is ready (click it to scan the screen). Another program is using " + Tray.HotkeyText + "."
                : "While b0r3d QR is running, click its tray icon or press " + Tray.HotkeyText + " in any program to scan the screen without opening this window first.";
        }

        void OnScanAnywhereMenu(object sender, RoutedEventArgs e) => SetScanAnywhere(ScanAnywhereMenu.IsChecked);

        void OnScanAnywhereCheck(object sender, RoutedEventArgs e)
        {
            if (ready) SetScanAnywhere(ScanAnywhereCheck.IsChecked == true);
        }

        internal void OnHideToTray(object sender, RoutedEventArgs e)
        {
            if (!scanAnywhere) SetScanAnywhere(true);
            hiddenInTray = true;
            if (IsVisible) Hide();
        }

        internal void ShowFromTray()
        {
            hiddenInTray = false;
            if (windowOnScreen && !IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = maximized ? WindowState.Maximized : WindowState.Normal;
            if (IsVisible) Activate();
        }

        // from the tray icon or Ctrl+Shift+Q: scans the screen without opening the main window first
        internal void ScanScreenFromAnywhere()
        {
            if (Scanning) return;
            if (hiddenInTray || !IsVisible || WindowState == WindowState.Minimized)
            {
                fromAnywhere = true;
                snips = new List<SnipWindow>();
                CommandManager.InvalidateRequerySuggested();
                ShowSnips();
                return;
            }
            OnScanScreen(this, null);
        }

        void WhenNotScanning(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !Scanning;
            e.Handled = true;
        }

        void OnTabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source == Tabs && Tabs.SelectedItem == MakeTab) RenderPreview();
        }

        // Ctrl+V with a picture (or picture files) copied and no text reads it, wherever the focus
        // is; with text, a text box pastes it as usual. Ctrl+Shift+V (Paste picture) in a text box
        // with text copied pastes the text, as it does in most programs.
        void OnPreviewKeys(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.V || Scanning) return;
            bool shift = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
            if (Keyboard.Modifiers != ModifierKeys.Control && !shift) return;
            IDataObject data;
            try
            {
                data = Clipboard.GetDataObject();
            }
            catch (ExternalException)
            {
                return;
            }
            if (shift)
            {
                if (!(Keyboard.FocusedElement is TextBox box) || box.IsReadOnly || !HoldsText(data)) return;  // Paste picture does it
                e.Handled = true;
                box.Paste();
            }
            else if (PasteReads(data))
            {
                e.Handled = true;
                ReadData(data, "the copied picture", NothingCopied);
            }
        }

        static bool HoldsText(IDataObject data)
        {
            try
            {
                return data != null && (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text));
            }
            catch (COMException)
            {
                return false;
            }
        }

        // What nothing here takes (text dragged anywhere but the data box, pictures while a scan
        // runs) is refused: the window lets things be dropped on all of it, and a drop nobody
        // answers can look like a move to the program it came from, which then deletes its copy.
        void OnDragElsewhere(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        // Esc stops a scan (once menus and boxes have had their turn)
        void OnKeys(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || scanning == null) return;
            e.Handled = true;
            OnCancelScan(this, e);
        }

        // what Ctrl+V reads rather than leaves to a text box: a picture or files, and no text
        internal static bool PasteReads(IDataObject data) => Pictures.CanRead(data) && !HoldsText(data);

        const string NothingCopied = "Nothing to read on the clipboard: copy a picture, or picture files, first.";

        // ---------- the screen ----------

        // b0r3d QR steps aside (at once: no fade), then every screen is pictured and covered
        void OnScanScreen(object sender, ExecutedRoutedEventArgs e)
        {
            if (Scanning) return;
            snips = new List<SnipWindow>();
            CommandManager.InvalidateRequerySuggested();
            Native.Transitions(this, false);
            steppedAside = true;
            Hide();
            var pause = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };  // for the desktop under this window to repaint
            pause.Tick += (s, a) => { pause.Stop(); ShowSnips(); };
            pause.Start();
        }

        // (whatever goes wrong, b0r3d QR comes back: it's hidden, with nothing in the taskbar)
        void ShowSnips()
        {
            var shown = new List<SnipWindow>();
            try
            {
                if (MakeSnips != null)
                {
                    shown = MakeSnips();
                    if (shown.Count == 0)
                    {
                        snips = null;
                        fromAnywhere = false;
                        ShowFromTray();
                        ComeBack();
                        ScanProblem("Windows says there are no screens to picture.");
                        return;
                    }
                    WatchSnips(shown);
                    return;
                }
                foreach (Screens.Screen screen in Screens.All())
                    shown.Add(new SnipWindow(screen, Screens.Capture(screen.Bounds), true));
                if (shown.Count == 0)
                {
                    snips = null;
                    fromAnywhere = false;
                    ShowFromTray();
                    ComeBack();
                    ScanProblem("Windows says there are no screens to picture.");
                    return;
                }
                WatchSnips(shown);
                Point pointer = Screens.Pointer();
                SnipWindow under = shown.FirstOrDefault(w => Contains(w.Screen.Bounds, pointer)) ?? shown.FirstOrDefault(w => w.Screen.Main) ?? shown[0];
                foreach (SnipWindow snip in shown)
                {
                    Screens.Cover(snip, snip.Screen.Bounds);
                    snip.Show();
                    Screens.Cover(snip, snip.Screen.Bounds);  // again: arriving on a screen with other scaling can resize it
                }
                under.Activate();
            }
            catch (Exception x)
            {
                fromAnywhere = false;
                ShowFromTray();
                if (snips != null && snips.Count > 0)
                {
                    OnSnipChosen(snips[0], SnipWindow.Choice.Cancel, Int32Rect.Empty);
                }
                else
                {
                    snips = null;
                    foreach (SnipWindow snip in shown) snip.Close();  // each holds a whole screen's picture
                    ComeBack();
                }
                ScanProblem("Couldn't take a picture of the screen: " + x.Message);
            }
        }

        // The screens' windows: a choice on any of them (or one being closed) ends the wait for
        // all of them, and so does switching to another program, which they'd otherwise hide.
        internal void WatchSnips(List<SnipWindow> shown)
        {
            snips = shown;
            foreach (SnipWindow snip in shown) snip.Chosen += OnSnipChosen;
            if (Application.Current != null) Application.Current.Deactivated += OnAwayDuringSnip;
        }

        internal void OnAwayDuringSnip(object sender, EventArgs e)
        {
            if (snips != null && snips.Count > 0) OnSnipChosen(snips[0], SnipWindow.Choice.Cancel, Int32Rect.Empty);
        }

        static bool Contains(Int32Rect area, Point p) => p.X >= area.X && p.Y >= area.Y && p.X < area.X + area.Width && p.Y < area.Y + area.Height;

        internal void OnSnipChosen(SnipWindow from, SnipWindow.Choice choice, Int32Rect box)
        {
            List<SnipWindow> all = snips != null && snips.Count > 0 ? snips : new List<SnipWindow> { from };
            snips = null;
            if (Application.Current != null) Application.Current.Deactivated -= OnAwayDuringSnip;
            foreach (SnipWindow snip in all)
            {
                snip.Chosen -= OnSnipChosen;
                if (!snip.IsClosed) snip.Close();
            }
            bool startedAway = fromAnywhere;
            fromAnywhere = false;
            if (choice == SnipWindow.Choice.Cancel)
            {
                ComeBack();
                if (!startedAway) Tabs.SelectedItem = ScanTab;
                CommandManager.InvalidateRequerySuggested();
                return;
            }
            if (startedAway) ShowFromTray();
            ComeBack();
            Tabs.SelectedItem = ScanTab;
            CommandManager.InvalidateRequerySuggested();

            bool several = all.Count > 1;
            string Name(SnipWindow w) => several ? "screen " + w.Screen.Number : "the screen";
            var pictures = new List<(string Source, BitmapSource Picture)>();
            string where, tip;
            switch (choice)
            {
                case SnipWindow.Choice.Box:
                    var crop = new CroppedBitmap(from.Shot, box);
                    crop.Freeze();
                    pictures.Add(("the box on " + Name(from), crop));
                    where = "the box you drew";
                    tip = "Draw the box around the whole code, with a little room around it.";
                    break;
                case SnipWindow.Choice.Screen:
                    pictures.Add((Name(from), from.Shot));
                    where = Name(from);
                    tip = "If the code is small, zoom in on it and try again, or drag a box around it.";
                    break;
                default:
                    foreach (SnipWindow w in all) pictures.Add((Name(w), w.Shot));
                    where = several ? "your screens" : "the screen";
                    tip = "If the code is small, zoom in on it and try again, or drag a box around it.";
                    break;
            }
            ReadPictures(where, pictures, tip);
        }

        // back after the screens were pictured (only shown again if it stepped aside for them)
        void ComeBack()
        {
            if (steppedAside)
            {
                steppedAside = false;
                Show();
            }
            Native.Transitions(this, true);
            if (IsVisible) Activate();
        }

        // ---------- pictures ----------

        void OnOpenPicture(object sender, ExecutedRoutedEventArgs e)
        {
            string[] files;
            try
            {
                files = FileDialogs.OpenPictures(this);
            }
            catch (COMException x)
            {
                ScanProblem("Windows' Open dialog didn't open: " + x.Message);
                return;
            }
            if (files != null && files.Length > 0) ReadFiles(files);
        }

        void OnScanFolder(object sender, ExecutedRoutedEventArgs e)
        {
            string folder;
            try
            {
                folder = FileDialogs.PickFolder(this);
            }
            catch (COMException x)
            {
                ScanProblem("Windows' folder dialog didn't open: " + x.Message);
                return;
            }
            if (folder != null) ReadFiles(new[] { folder });
        }

        void OnPastePicture(object sender, ExecutedRoutedEventArgs e)
        {
            IDataObject data;
            try
            {
                data = Clipboard.GetDataObject();
            }
            catch (ExternalException)
            {
                ScanProblem("Couldn't look at the clipboard: another program is using it. Try again.");
                return;
            }
            ReadData(data, "the copied picture", NothingCopied);
        }

        // pictures, files or a folder dragged onto the window are read; text still goes to the data
        // box, a picture dropped on the Logo section is the logo, and a design file opens (MakeDrop)
        void OnPreviewDragOver(object sender, DragEventArgs e)
        {
            if (Refuse(e) || MakeDrop(e, false)) return;
            if (Scanning || !Pictures.CanRead(e.Data)) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        void OnPreviewDrop(object sender, DragEventArgs e)
        {
            if (Refuse(e) || MakeDrop(e, true)) return;
            if (Scanning || !Pictures.CanRead(e.Data)) return;
            e.Handled = true;
            ReadData(e.Data, "the dropped picture", "Nothing to read in what was dropped.");
        }

        // The code dragged out of this window and back over it: nothing here takes it (not even
        // the data box), so letting go of it here doesn't count as the code being used.
        bool Refuse(DragEventArgs e)
        {
            if (!draggingOut) return false;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return true;
        }

        // what was copied or dropped: files and folders, or a picture
        internal void ReadData(IDataObject data, string pictureName, string nothing)
        {
            Tabs.SelectedItem = ScanTab;
            string[] files = null;
            BitmapSource picture = null;
            try
            {
                if (data != null) Pictures.From(data, out files, out picture);
            }
            catch (ExternalException)
            {
                // the other program let go of it: nothing to read
            }
            if (files != null) ReadFiles(files);
            else if (picture != null) ReadPictures(pictureName, new List<(string, BitmapSource)> { (pictureName, picture) }, null);
            else ScanProblem(nothing);
        }

        internal void ReadFiles(IList<string> paths)
        {
            Run("Opening…", (cancel, progress) => ReadFilesNow(paths, cancel, progress));
        }

        // Pictures and folders (a folder's own pictures, not the folders inside it), a few at a
        // time. In a batch, every picture gets a line: its codes, or why there are none. Safe on
        // any thread.
        internal static ScanOutcome ReadFilesNow(IList<string> paths, CancellationToken cancel, Action<string> progress)
        {
            var files = new List<(string Path, bool InFolder)>();
            var unlisted = new List<string>();   // folders that couldn't be looked in
            string folderName = null;
            bool foldersInside = false;
            foreach (string path in paths)
            {
                if (!Directory.Exists(path))
                {
                    files.Add((path, false));
                    continue;
                }
                string name = Path.GetFileName(path.TrimEnd('\\'));
                folderName = name.Length > 0 ? name : path;
                try
                {
                    files.AddRange(Pictures.InFolder(path).Select(f => (f, true)));
                    foldersInside |= Directory.EnumerateDirectories(path).Any();
                }
                catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is System.Security.SecurityException)
                {
                    unlisted.Add(folderName + " (" + x.Message.TrimEnd('.') + ")");
                }
            }
            bool batch = files.Count > 1 || folderName != null;

            var lines = new List<FoundCode>[files.Count];
            string firstProblem = null, firstLeftOut = null;
            int withCodes = 0, empty = 0, unreadable = 0, online = 0, partly = 0, done = 0;
            var options = new ParallelOptions { CancellationToken = cancel, MaxDegreeOfParallelism = Math.Max(1, Math.Min(3, Environment.ProcessorCount / 2)) };
            Parallel.For(0, files.Count, options, i =>
            {
                string file = files[i].Path, name = Path.GetFileName(file);
                var here = new List<FoundCode>();
                if (files[i].InFolder && Pictures.OnlyOnline(file))
                {
                    Interlocked.Increment(ref online);
                    here.Add(FoundCode.Problem("Not read: only online", "it isn't on this PC yet; open it once, then scan again", name));
                }
                else
                {
                    try
                    {
                        BitmapSource look = null;
                        // a PDF's pages one at a time, each read before the next is drawn; a page that can't
                        // be read, and pages past the 50th, are said, and the codes on the others kept
                        string leftOut = Pictures.Read(file, cancel, page =>
                        {
                            here.AddRange(Reader.ReadAll(page.Pixels, page.Width, page.Height, page.Name, cancel));
                            if (look == null && batch) look = Pictures.Thumbnail(page);
                        });
                        if (here.Count > 0) Interlocked.Increment(ref withCodes);
                        else
                        {
                            Interlocked.Increment(ref empty);
                            here.Add(FoundCode.Problem("No code found", "", name, look));
                        }
                        if (leftOut != null)
                        {
                            Interlocked.Increment(ref partly);
                            Interlocked.CompareExchange(ref firstLeftOut, leftOut, null);
                            here.Add(FoundCode.Problem("Not read in full", leftOut, name));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception x)
                    {
                        // one picture's trouble never ends the others
                        Interlocked.Increment(ref unreadable);
                        string why = x is PictureException ? x.Message : "Windows couldn't read it (" + x.Message.TrimEnd('.') + ")";
                        Interlocked.CompareExchange(ref firstProblem, name + ": " + why, null);
                        here.Clear();
                        here.Add(FoundCode.Problem("Couldn't open", why, name));
                    }
                }
                lines[i] = here;
                progress("Reading " + Interlocked.Increment(ref done) + " of " + files.Count + "…");
            });

            var outcome = new ScanOutcome { Codes = lines.SelectMany(l => l).Where(c => batch || !c.IsProblem).ToList() };
            int codes = outcome.Codes.Count(c => !c.IsProblem);
            string inFolder = folderName != null ? " in " + folderName : "";
            string inside = foldersInside ? " Folders inside it aren't read." : "";
            string notListed = unlisted.Count > 0 ? "; couldn't look in " + string.Join(", ", unlisted) : "";
            if (!batch && files.Count == 1)
            {
                string name = Path.GetFileName(files[0].Path);
                string leftOut = firstLeftOut != null ? "; " + firstLeftOut + "." : null;
                outcome.Summary = unreadable > 0 ? "Couldn't open " + firstProblem + "." :
                    codes == 0 ? "No code in " + name + (leftOut ?? ". If the code is small in the picture, crop closer to it and try again.") :
                    Count(codes) + " in " + name + leftOut;
            }
            else if (files.Count == 0)
            {
                outcome.Summary = "No pictures" + (folderName != null ? inFolder : " there") + notListed + "." + inside;
            }
            else
            {
                var parts = new List<string> { Count(codes) + " in " + withCodes + " of " + files.Count + (files.Count == 1 ? " picture" : " pictures") + inFolder };
                if (empty > 0) parts.Add("no code in " + empty);
                if (unreadable > 0) parts.Add(unreadable + " couldn't be opened");
                if (partly > 0) parts.Add(partly + " not read in full");
                if (online > 0) parts.Add(online + " only online, not read");
                outcome.Summary = string.Join("; ", parts) + notListed + "." + inside;
            }
            return outcome;
        }

        // the screen's pictures or a copied one, each screen at the same time
        void ReadPictures(string where, IList<(string Source, BitmapSource Picture)> pictures, string tip)
        {
            Run("Reading " + where + "…", (cancel, progress) => ReadPicturesNow(where, pictures, tip, cancel));
        }

        internal static ScanOutcome ReadPicturesNow(string where, IList<(string Source, BitmapSource Picture)> pictures, string tip, CancellationToken cancel)
        {
            var each = new List<FoundCode>[pictures.Count];
            var trouble = new string[pictures.Count];   // why a picture couldn't be read (one screen's trouble never stops the others)
            Parallel.For(0, pictures.Count, new ParallelOptions { CancellationToken = cancel }, i =>
            {
                try
                {
                    Gray gray = Pictures.Grays(pictures[i].Picture, pictures[i].Source);
                    each[i] = Reader.ReadAll(gray.Pixels, gray.Width, gray.Height, pictures[i].Source, cancel);
                }
                catch (Exception x) when (!(x is OperationCanceledException))
                {
                    each[i] = new List<FoundCode>();
                    trouble[i] = x.Message.TrimEnd('.');
                }
            });
            var outcome = new ScanOutcome { Codes = each.SelectMany(c => c).ToList() };
            string at = where.StartsWith("the box", StringComparison.Ordinal) || where.StartsWith("the copied", StringComparison.Ordinal) || where.StartsWith("the dropped", StringComparison.Ordinal) ? " in " : " on ";
            string[] failed = Enumerable.Range(0, pictures.Count).Where(i => trouble[i] != null).Select(i => pictures[i].Source + " (" + trouble[i] + ")").ToArray();
            if (failed.Length == pictures.Count)
                outcome.Summary = "Couldn't read " + (pictures.Count == 1 ? where + " (" + trouble[0] + ")" : string.Join("; ", failed)) + ".";
            else
                outcome.Summary = (outcome.Codes.Count == 0 ? "No code" + at + where : Count(outcome.Codes.Count) + at + where) +
                    (failed.Length > 0 ? "; couldn't read " + string.Join("; ", failed) : "") +
                    (outcome.Codes.Count == 0 ? "." + (tip == null ? "" : " " + tip) : "");
            return outcome;
        }

        static string Count(int codes) => codes == 1 ? "1 code" : codes + " codes";

        // Reads off the window's thread, with Cancel, then shows what it found.
        void Run(string busy, Func<CancellationToken, Action<string>, ScanOutcome> work)
        {
            scanning?.Cancel();
            var cancel = new CancellationTokenSource();
            scanning = cancel;
            Tabs.SelectedItem = ScanTab;
            found.Clear();
            ShowSummary(busy, "Text");
            CancelScanButton.Visibility = Visibility.Visible;
            ClearButton.IsEnabled = false;
            CommandManager.InvalidateRequerySuggested();
            Dispatcher ui = Dispatcher;
            void Progress(string text) => ui.BeginInvoke(new Action(() => { if (scanning == cancel) ScanStatus.Text = LongText.Cap(text); }));
            Task.Run(() => work(cancel.Token, Progress)).ContinueWith(done => ui.BeginInvoke(new Action(() => Finished(cancel, done))));
        }

        void Finished(CancellationTokenSource cancel, Task<ScanOutcome> done)
        {
            if (scanning != cancel) return;  // a newer scan took over
            scanning = null;
            CancelScanButton.Visibility = Visibility.Collapsed;
            ClearButton.IsEnabled = true;
            CommandManager.InvalidateRequerySuggested();
            Exception problem = done.Exception?.GetBaseException();
            if (cancel.IsCancellationRequested || problem is OperationCanceledException)
            {
                ShowSummary("Stopped.", "Dim");
            }
            else if (problem != null)
            {
                ScanProblem("Reading stopped: " + problem.Message);
            }
            else
            {
                List<FoundCode> codes = done.Result.Codes;
                foreach (FoundCode code in codes.Take(MaxShown)) found.Add(code);
                bool any = codes.Any(c => !c.IsProblem);
                ShowSummary(done.Result.Summary + (codes.Count > MaxShown ? " (the first " + MaxShown + " lines are shown)" : ""), any ? "Text" : "Warn");
            }
            cancel.Dispose();
        }

        // the line over the codes; screen readers hear it too (it's a polite live region). It names files,
        // folders and cameras, and quotes Windows: capped.
        void ShowSummary(string text, string colour)
        {
            ScanStatus.Text = LongText.Cap(text);
            ScanStatus.SetResourceReference(TextBlock.ForegroundProperty, colour);
            if (text.Length > 0 && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.CreatePeerForElement(ScanStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        void ScanProblem(string text)
        {
            Tabs.SelectedItem = ScanTab;
            ShowSummary(text, "Warn");
        }

        void OnCancelScan(object sender, RoutedEventArgs e)
        {
            if (scanning == null) return;
            scanning.Cancel();
            ShowSummary("Stopping…", "Dim");
        }

        void OnClearFound(object sender, RoutedEventArgs e)
        {
            found.Clear();
            ShowSummary("", "Text");
        }

        // ---------- each code found ----------

        static FoundCode CodeOf(object sender) => (sender as FrameworkElement)?.DataContext as FoundCode;

        void OnCopyFound(object sender, RoutedEventArgs e)
        {
            FoundCode code = CodeOf(sender);
            if (code == null || !code.HasText) return;
            try
            {
                Clipboard.SetDataObject(Exports.TextData(code.Text), true);
                Flash("Copied what the code holds", "Good");
            }
            catch (ExternalException)
            {
                Flash("Couldn't copy: another program is using the clipboard. Try again.", "Bad");
            }
        }

        // only web links, and only after a question that shows where it goes
        void OnOpenFound(object sender, RoutedEventArgs e)
        {
            FoundCode code = CodeOf(sender);
            if (code == null || !Kinds.WebLink(code.Text, out Uri uri)) return;
            bool odd = Kinds.LinkWarnings(uri).Count > 0;
            MessageBoxResult answer = MessageBox.Show(this, LinkQuestion(uri), "Open link", MessageBoxButton.YesNo, odd ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes) OpenInBrowser(this, uri.AbsoluteUri);
        }

        internal static string LinkQuestion(Uri uri)
        {
            List<string> warnings = Kinds.LinkWarnings(uri);
            return "Open this link in your browser?\n\n" + uri.AbsoluteUri + (warnings.Count > 0 ? "\n\n" + string.Join("\n", warnings) : "");
        }

        void OnMakeAgain(object sender, RoutedEventArgs e)
        {
            FoundCode code = CodeOf(sender);
            if (code != null && code.CanMakeAgain) MakeAgain(code);
        }

        // The Make tab, with the scanned text exactly as it was (Windows line breaks too, which
        // typed text loses) and its error correction, when the code said; a barcode as its own type.
        internal void MakeAgain(FoundCode code)
        {
            Tabs.SelectedItem = MakeTab;
            if (code.Barcode != null)
            {
                MakeBarcodeAgain(code.Barcode.Value, code.Text);
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    BarcodeDataBox.Focus();
                    BarcodeDataBox.CaretIndex = BarcodeDataBox.Text.Length;
                }));
                Flash("Made from the scanned code: the same type and text", "Good");
                return;
            }
            if (MakingBarcode) ChooseCodeType(null);
            if (code.Level.HasValue) LevelButton(code.Level.Value).IsChecked = true;
            madeAgain = code.Text;
            DataBox.Text = code.Text;
            CatchUp();
            // (once the Make tab is back on screen: focusing the box before that does nothing)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                DataBox.Focus();
                DataBox.CaretIndex = DataBox.Text.Length;
            }));
            Flash(code.Level.HasValue ? "Made from the scanned code: the same text and error correction" : "Made from the scanned code's text", "Good");
        }
    }
}
