using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Barcodes;
using B0r3dQR.Batch;
using B0r3dQR.Design;
using B0r3dQR.Qr;

namespace B0r3dQR
{
    // what to do with a file that's already in the folder
    enum Clash { Number, Replace, Skip }

    // Batch export, as the website's Batch export does it: a CSV's rows, each made into a file with the Make tab's
    // design (QR codes) or settings (barcodes) and named as the website names them; here straight into a folder, or
    // into a zip if asked, every file read back once it's written.
    public partial class BatchWindow : Window
    {
        const long MostCsvBytes = 20 * 1024 * 1024;

        readonly BatchKind kind;
        readonly string what;          // "QR codes", "EAN-13 barcodes"
        readonly Settings settings;
        List<BatchRow> rows = new List<BatchRow>();
        List<BatchItem> items = new List<BatchItem>();
        int shown, planning;
        bool planned;                  // `items` are the rows of the file and kind of file showing
        bool busy, stopping, showing;
        string madeIn;                 // where the last files went (a folder, or the zip), for Show the files
        string clashFolder;            // a folder waiting for the answer about files already there

        // for the tests: where the files go, instead of asking (null: Don't make them); and how a row's file is made
        internal Func<string> AskFolder, AskZip;
        internal Func<BatchItem, BatchKind, byte[]> MakeFile = (item, kind) => BatchExport.Make(item, kind, out _);
        internal Task Running { get; private set; } = Task.CompletedTask;
        internal IReadOnlyList<BatchItem> Items => items;
        internal BatchKind Kind => kind;

        internal BatchWindow(BatchKind kind, string what, Settings settings = null)
        {
            InitializeComponent();
            this.kind = kind;
            this.what = what;
            this.settings = settings;
            AskFolder = () => FileDialogs.PickFolder(this);
            AskZip = () => FileDialogs.Save(this, "Save the batch as a zip file", "Zip file", ".zip", ZipName);
            bool qr = kind.Barcode == null;
            WhatLine.Text = (qr ? "QR codes with the Make tab's design" : what + " with the Make tab's barcode settings") +
                            ": a file for every row of a CSV, named as the website names them.";
            GuideText.Text = qr ? QrGuide : BarcodeGuide;
            TextChoice.Visibility = qr ? Visibility.Visible : Visibility.Collapsed;
            showing = true;
            TextStyleBox.ItemsSource = new[] { "Half blocks, compact", "Full blocks", "ASCII, # for the background" };
            TextStyleBox.SelectedIndex = 0;
            FrameCheck.IsChecked = qr && kind.Design.Frame != null;
            showing = false;
            ShowFormat();
            Drop += OnDrop;
            DragOver += (s, e) =>
            {
                e.Effects = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            SourceInitialized += (s, e) => Theme.TitleBar(this);
            Closing += (s, e) =>
            {
                // a batch being made stops first (it's on this window's thread); a question waiting is just dropped
                clashFolder = null;
                if (!busy || Running.IsCompleted) return;
                stopping = true;
                e.Cancel = true;
                Running.ContinueWith(_ => Dispatcher.BeginInvoke(new Action(Close)));
            };
        }

        const string QrGuide =
            "The first row names the columns. Each row after it is a code: its text (a link, or any text) in the first column, then a frame caption and a file name " +
            "if you like. A frameFontFamily column sets the caption's font. A file with firstName and lastName columns is a file of contacts: each row becomes a " +
            "contact card, with any of org, position, phoneWork, phonePrivate, phoneMobile, email, website, street, zipcode, city, state, country, version " +
            "(2.1, 3.0 or 4.0; 3.0 if left out), frameText and fileName. A file without a name in it is named after its caption, else its text. " +
            "Commas, semicolons or tabs; UTF-8 or Windows-1252. The files are made with the Make tab's design, and a frame shows each row's caption.";

        const string BarcodeGuide =
            "A code a row: its text in the first column and a file name in the second, if you like. A first row naming the columns is optional: data, code, " +
            "barcode, value, text or url for the code, and fileName, file name, file or name for the file. A file without a name is named after its text. " +
            "Commas, semicolons or tabs; UTF-8 or Windows-1252.";

        // the website's zip names
        string ZipName => kind.Barcode != null ? BarcodeFormats.Get(kind.Barcode.Value).Id + "-batch.zip"
            : kind.Format == BatchFormat.Text ? "qr-codes-ascii-" + new[] { "unicode-half", "unicode-full", "ascii" }[(int)kind.TextStyle] + ".zip" : "qr-codes.zip";

        // ---------- the CSV ----------

        void OnChoose(object sender, RoutedEventArgs e)
        {
            string path;
            try
            {
                path = FileDialogs.OpenOne(this, "Choose a CSV file", "CSV file", "*.csv;*.txt");
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Open dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path != null) LoadCsv(path);
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (busy || !(e.Data.GetData(DataFormats.FileDrop) is string[] files) || files.Length == 0) return;
            LoadCsv(files[0]);
        }

        internal void LoadCsv(string path)
        {
            string text;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MostCsvBytes)
                {
                    ShowReadProblem("That file is too big for a batch: over 20 MB.");
                    return;
                }
                // shared as a browser or Notepad opens it, so a file still open in Excel can be read
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    text = Csv.Read(copy.ToArray());
                }
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is ArgumentException || x is System.Security.SecurityException)
            {
                ShowReadProblem("Couldn't read that file: " + x.Message);
                return;
            }
            LoadCsvText(Path.GetFileName(path), text);
        }

        // the rows of a CSV's text (`name` for the line saying which file it is)
        internal void LoadCsvText(string name, string text)
        {
            if (busy) return;
            bool hasFrameText = false;
            List<BatchRow> read = kind.Barcode != null ? BatchRows.ReadBarcodes(text) : BatchRows.ReadQr(text, out hasFrameText);
            if (read == null || read.Count == 0)
            {
                ShowReadProblem(kind.Barcode != null ? "No codes found in " + name + "." :
                    "That CSV can't be used: the first row names the columns, and every row after it needs its text in the first column " +
                    "(in a file of contacts, a firstName and a lastName).");
                return;
            }
            ReadProblem.Visibility = Visibility.Collapsed;
            rows = read;
            shown = 0;
            FileLine.Text = LongText.Cap(name + " · " + Count(rows.Count, "row"));
            if (kind.Barcode == null)
            {
                // a frame when the file has captions, as the website does on reading it
                showing = true;
                FrameCheck.IsChecked = hasFrameText;
                kind.Frame = hasFrameText;
                showing = false;
            }
            ClearOutcome();
            Replan();
        }

        void ShowReadProblem(string text)
        {
            ReadProblem.Text = LongText.Cap(text);
            ReadProblem.Visibility = Visibility.Visible;
            Say(ReadProblem);
        }

        static string Count(int n, string thing) => n == 1 ? "1 " + thing : n.ToString("N0") + " " + thing + "s";

        // which rows can be made, and the files' names (off the window's thread: hundreds of rows take a moment)
        void Replan()
        {
            if (rows.Count == 0) return;
            int mine = ++planning;
            var these = rows;
            BatchKind snapshot = Snapshot();
            StatusLine.Text = rows.Count > 200 ? "Checking " + Count(rows.Count, "row") + "…" : "";
            // nothing to make or print from the last file or kind of file while this one's checked
            planned = false;
            MakeButton.IsEnabled = PrintButton.IsEnabled = false;
            Task.Run(() => BatchExport.Plan(these, snapshot)).ContinueWith(done => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (mine != planning) return;   // the format or the file has changed since
                if (done.IsFaulted)
                {
                    items = new List<BatchItem>();
                    RowsPanel.Visibility = Visibility.Collapsed;
                    StatusLine.Text = LongText.Cap("Couldn't check the rows: " + done.Exception.GetBaseException().Message);
                    return;
                }
                planned = true;
                items = done.Result;
                StatusLine.Text = "";
                ShowRows();
            })));
        }

        // the settings as they are now, for work off the window's thread
        BatchKind Snapshot() => new BatchKind
        {
            Barcode = kind.Barcode, Design = kind.Design, Frame = kind.Frame, Print = kind.Print, WholePixels = kind.WholePixels,
            Look = kind.Look, Format = kind.Format, TextStyle = kind.TextStyle, Markdown = kind.Markdown,
        };

        void ShowRows()
        {
            RowsPanel.Visibility = Visibility.Visible;
            List<BatchItem> problems = items.Where(i => i.Problem != null).ToList();
            ProblemsList.Items.Clear();
            if (problems.Count == 0)
            {
                ProblemsHeading.Text = items.Count == 1 ? "The row can be made." : "All " + items.Count.ToString("N0") + " rows can be made.";
                ProblemsHeading.SetResourceReference(TextBlock.ForegroundProperty, "Good");
            }
            else
            {
                ProblemsHeading.Text = problems.Count == items.Count
                    ? (items.Count == 1 ? "The row can't be made as " : "None of the rows can be made as ") + what + ":"
                    : Count(problems.Count, "row") + " of " + items.Count.ToString("N0") + " can't be made, and will be left out:";
                ProblemsHeading.SetResourceReference(TextBlock.ForegroundProperty, "Bad");
                foreach (BatchItem item in problems.Take(50))
                    ProblemsList.Items.Add(Line("Row " + item.Row.Row + ": " + BatchExport.Glimpse(item.Row.Data) + " · " + item.Problem));
                if (problems.Count > 50) ProblemsList.Items.Add(Line("…and " + (problems.Count - 50).ToString("N0") + " more."));
            }
            ShowRow();
            ShowMakeButton();
        }

        // a line of the lists, which quote the rows' text, the files' names and why they can't be made: capped (LongText)
        static TextBlock Line(string text) => new TextBlock { Text = LongText.Cap(text), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 1, 0, 1) };

        void ShowMakeButton()
        {
            int makeable = items.Count(i => i.Problem == null);
            MakeButton.Content = makeable == 1 ? "_Make the file…" : "_Make " + makeable.ToString("N0") + " files…";
            MakeButton.IsEnabled = PrintButton.IsEnabled = !busy && planned && makeable > 0;
        }

        // the codes that can be made, on sheets (or one a page), each with its file name under it
        void OnPrintRows(object sender, RoutedEventArgs e)
        {
            PrintWindow window = PrintWindowFor();
            if (window == null) return;
            window.Owner = this;
            window.ShowDialog();
        }

        internal PrintWindow PrintWindowFor()
        {
            var codes = new List<Printing.PrintCode>();
            foreach (BatchItem item in items.Where(i => i.Problem == null))
            {
                Printing.PrintCode code = BatchExport.PrintCodeFor(item, kind);
                if (code != null) codes.Add(code);
            }
            if (codes.Count == 0) return null;
            return new PrintWindow(codes, Printing.PrintSheet.SizeNote(codes[0].Size, kind.Print, codes.Count > 1), kind.Print.Unit, settings);
        }

        // ---------- each row in turn ----------

        void OnPrevious(object sender, RoutedEventArgs e) => ShowRow(shown - 1);

        void OnNext(object sender, RoutedEventArgs e) => ShowRow(shown + 1);

        internal void ShowRow(int index = -1)
        {
            if (items.Count == 0) return;
            if (index >= 0) shown = Math.Max(0, Math.Min(items.Count - 1, index));
            BatchItem item = items[shown];
            RowNumber.Text = "Row " + (shown + 1).ToString("N0") + " of " + items.Count.ToString("N0") + " · line " + item.Row.Row + " of the file";
            PreviousButton.IsEnabled = shown > 0;
            NextButton.IsEnabled = shown < items.Count - 1;
            RowFields.Children.Clear();
            void Field(string label, string value, bool code = false)
            {
                if (string.IsNullOrEmpty(value)) return;
                RowFields.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = (Brush)FindResource("Dim"), Margin = new Thickness(0, 4, 0, 0) });
                var box = new TextBlock { Text = LongText.Cap(value.Length > 300 ? value.Substring(0, 297) + "…" : value), TextWrapping = TextWrapping.Wrap };
                if (code) box.FontFamily = new FontFamily("Consolas");
                RowFields.Children.Add(box);
            }
            BatchRow row = item.Row;
            if (row.Contact != null)
            {
                // a contact, from the fields its vCard was written from
                Field("Name", (Get(row.Contact, "firstName") + " " + Get(row.Contact, "lastName")).Trim());
                Field("Company", Joined(row.Contact, "org", "position"));
                Field("Email", Get(row.Contact, "email"));
                Field("Phone", Joined(row.Contact, "phoneWork", "phoneMobile", "phonePrivate"));
            }
            else Field("Data", row.Data, true);
            if (kind.Barcode == null && kind.Frame && kind.Format != BatchFormat.Text) Field("Frame text", row.FrameText.Length > 0 ? row.FrameText : QrFrame.DefaultText + " (the default)");
            if (kind.Barcode == null && row.Font.Length > 0) Field("Font family", row.Font);
            if (item.Name != null) Field("File", item.Name + kind.Extension, true);
            if (item.Problem != null)
            {
                var problem = new TextBlock { Text = LongText.Cap(item.Problem), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
                problem.SetResourceReference(TextBlock.ForegroundProperty, "Bad");
                RowFields.Children.Add(problem);
            }
            RowPicture.Source = item.Problem == null ? Thumbnail(item) : null;
        }

        static string Get(Dictionary<string, string> fields, string name) => fields.TryGetValue(name, out string value) ? value : "";

        // the fields of those names that aren't empty, in that order, joined by " · "
        static string Joined(Dictionary<string, string> fields, params string[] names) => string.Join(" · ", names.Select(name => Get(fields, name)).Where(value => value.Length > 0));

        // the row's code drawn small, as its file will show it
        BitmapSource Thumbnail(BatchItem item)
        {
            try
            {
                if (kind.Barcode != null)
                {
                    BarcodeSvg made = Barcode.Make(item.Row.Data, kind.Barcode.Value, kind.Look.Options, out _);
                    if (made == null) return null;
                    var art = new BarcodeArt(made);
                    return art.Render(Math.Max(1, Math.Floor(276.0 / Math.Max(art.W, art.H))), Css.Paint(kind.Look.Color), kind.Look.Transparent ? (Color?)null : Css.Paint(kind.Look.Background));
                }
                QrDesign design = BatchExport.DesignFor(kind, item.Row);
                if (kind.Format == BatchFormat.Text)
                {
                    design.Frame = null;
                    design.Logo = null;
                }
                var qr = new QrArt(design);
                double k = 276 / Math.Max(qr.W, qr.H);
                return qr.Render(k, (int)Math.Ceiling(qr.W * k), (int)Math.Ceiling(qr.H * k));
            }
            catch (QrException)
            {
                return null;
            }
        }

        // ---------- the kind of file ----------

        void OnFormat(object sender, RoutedEventArgs e)
        {
            if (showing || kind == null) return;   // (XAML sets the first choice while the window is built)
            ShowFormat();
            ClearOutcome();
            Replan();
        }

        void OnTextStyle(object sender, RoutedEventArgs e)
        {
            if (showing || kind == null) return;
            ShowFormat();
            Replan();
        }

        void OnFrame(object sender, RoutedEventArgs e)
        {
            if (showing || kind == null) return;
            kind.Frame = FrameCheck.IsChecked == true;
            ShowRow();
        }

        void ShowFormat()
        {
            kind.Format = PngChoice.IsChecked == true ? BatchFormat.Png : JpgChoice.IsChecked == true ? BatchFormat.Jpg : SvgChoice.IsChecked == true ? BatchFormat.Svg :
                          PdfChoice.IsChecked == true ? BatchFormat.Pdf : BatchFormat.Text;
            kind.TextStyle = (QrExport.TextStyle)Math.Max(0, TextStyleBox.SelectedIndex);
            kind.Markdown = MarkdownCheck.IsChecked == true;
            kind.Frame = kind.Barcode == null && FrameCheck.IsChecked == true;
            bool text = kind.Format == BatchFormat.Text;
            TextOptions.Visibility = text ? Visibility.Visible : Visibility.Collapsed;
            FrameCheck.Visibility = kind.Barcode == null && !text ? Visibility.Visible : Visibility.Collapsed;
            SettingsLine.Text = kind.Barcode != null ? "Made with the Make tab's barcode settings (colours, size, text, bar height); change them there." :
                text ? (kind.TextStyle == QrExport.TextStyle.Ascii ? "Most scanners can't read the ASCII kind: it's for plain text, email and terminals. " : "") +
                       "Text uses the Make tab's error correction and size, with no logo or frame." :
                "Made with the Make tab's design and size; change them there.";
        }

        // ---------- making the files ----------

        void OnMake(object sender, RoutedEventArgs e)
        {
            if (busy || items.Count(i => i.Problem == null) == 0) return;
            ClearOutcome();
            if (ZipChoice.IsChecked == true)
            {
                string zip;
                try
                {
                    zip = AskZip();
                }
                catch (COMException x)
                {
                    MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (zip != null) Start(MakeZip(zip));
                return;
            }
            string folder;
            try
            {
                folder = AskFolder();
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' folder dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (folder == null) return;
            // names cut to fit the folder's path, which can't be so long that they'd hardly tell the rows apart
            int longest = BatchExport.LongestName(folder, kind.Extension);
            if (longest < BatchExport.ShortestName)
            {
                StatusLine.Text = LongText.Cap("Nothing was made: " + folder + " is too far down for files to go in it (Windows allows 260 characters for a file's whole path). Choose a folder nearer the top of the drive.");
                StatusLine.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
                LastOutcome = "the folder's path is too long";
                Say(StatusLine);
                return;
            }
            int already = items.Count(i => i.Problem == null && File.Exists(Path.Combine(folder, BatchExport.Fit(i.Name, longest) + kind.Extension)));
            if (already == 0)
            {
                Start(MakeIn(folder, Clash.Number));
                return;
            }
            // asked once, for all of them
            clashFolder = folder;
            ClashLine.Text = LongText.Cap((already == 1 ? "1 of the files is" : already.ToString("N0") + " of the files are") + " already in " + folder + ". What should happen to " +
                                          (already == 1 ? "it?" : "them?"));
            ClashPanel.Visibility = Visibility.Visible;
            Busy(true, asking: true);
            Say(ClashLine);
            NumberButton.Focus();
        }

        void OnNumber(object sender, RoutedEventArgs e) => Answer(Clash.Number);

        void OnReplace(object sender, RoutedEventArgs e) => Answer(Clash.Replace);

        void OnSkip(object sender, RoutedEventArgs e) => Answer(Clash.Skip);

        void OnClashCancel(object sender, RoutedEventArgs e)
        {
            ClashPanel.Visibility = Visibility.Collapsed;
            clashFolder = null;
            Busy(false);
            MakeButton.Focus();
        }

        // the answer about files already there
        internal void Answer(Clash clash)
        {
            if (clashFolder == null) return;
            string folder = clashFolder;
            clashFolder = null;
            ClashPanel.Visibility = Visibility.Collapsed;
            Busy(false);
            Start(MakeIn(folder, clash));
        }

        internal bool Asking => clashFolder != null;

        // a batch under way; a fault in it is the window's, as any other is (it isn't lost in the task)
        void Start(Task task)
        {
            Running = task;
            task.ContinueWith(t => Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("The batch export failed.", t.Exception.GetBaseException()))),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        void OnStop(object sender, RoutedEventArgs e) => stopping = true;

        void Busy(bool on, bool asking = false)
        {
            busy = on;
            ChooseButton.IsEnabled = ExampleButton.IsEnabled = Formats.IsEnabled = TextOptions.IsEnabled = FrameCheck.IsEnabled = FolderChoice.IsEnabled = ZipChoice.IsEnabled = !on;
            CancelButton.Visibility = on && !asking ? Visibility.Visible : Visibility.Collapsed;
            ShowMakeButton();
        }

        // Into a folder: each file written whole (never half a file), then read back from the disk.
        async Task MakeIn(string folder, Clash clash)
        {
            List<BatchItem> todo = items.Where(i => i.Problem == null).ToList();
            BatchKind snapshot = Snapshot();
            var outcome = new Outcome();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // each name cut to fit the folder's path (Windows' 260 characters), its extension kept
            int longest = BatchExport.LongestName(folder, snapshot.Extension);
            Busy(true);
            stopping = false;
            try
            {
                for (int n = 0; n < todo.Count && !stopping; n++)
                {
                    BatchItem item = todo[n];
                    string name = BatchExport.Fit(item.Name, longest);
                    string file = name + snapshot.Extension;
                    // a name this batch has used already (Windows doesn't tell capitals apart, and names cut short can
                    // meet) is always numbered: the answer about files already there is about those, never this batch's own
                    if (taken.Contains(file)) file = BatchExport.Numbered(folder, name, snapshot.Extension, taken, longest);
                    else if (File.Exists(Path.Combine(folder, file)))
                    {
                        if (clash == Clash.Skip)
                        {
                            outcome.Skipped++;
                            continue;
                        }
                        if (clash == Clash.Number) file = BatchExport.Numbered(folder, name, snapshot.Extension, taken, longest);
                    }
                    taken.Add(file);
                    string path = Path.Combine(folder, file);
                    Progress(n, todo.Count, file);
                    byte[] bytes = MakeOne(item, snapshot, outcome, file);
                    if (bytes == null) continue;
                    string problem = await Task.Run(() => WriteAndReadBack(path, bytes, out byte[] back) ?? (Same(back, bytes) ? null : "it came back from the disk different"));
                    if (problem != null)
                    {
                        outcome.Failed.Add(file + ": " + problem);
                        continue;
                    }
                    outcome.Made++;
                    if (!await ReadsBack(bytes, item, snapshot)) outcome.Unread.Add(file);
                }
            }
            finally
            {
                Busy(false);
            }
            madeIn = folder;
            Finish(outcome, todo.Count, folder);
        }

        // Into a zip: written beside it under a temporary name and put in its place once whole (whatever stops it
        // first, the temporary file is deleted); then each file read back out of the finished zip, one at a time (a
        // batch can be bigger than the memory there is).
        async Task MakeZip(string zipPath)
        {
            List<BatchItem> todo = items.Where(i => i.Problem == null).ToList();
            BatchKind snapshot = Snapshot();
            var outcome = new Outcome();
            var made = new List<(BatchItem Item, string Entry, byte[] Hash)>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Busy(true);
            stopping = false;
            string temporary = zipPath + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    for (int n = 0; n < todo.Count && !stopping; n++)
                    {
                        BatchItem item = todo[n];
                        string entry = item.Name + snapshot.Extension;
                        for (int k = 2; names.Contains(entry); k++) entry = item.Name + " (" + k + ")" + snapshot.Extension;
                        names.Add(entry);
                        Progress(n, todo.Count, entry);
                        byte[] bytes = MakeOne(item, snapshot, outcome, entry);
                        if (bytes == null) continue;
                        await Task.Run(() =>
                        {
                            using (Stream into = zip.CreateEntry(entry, CompressionLevel.Optimal).Open()) into.Write(bytes, 0, bytes.Length);
                        });
                        made.Add((item, entry, Hash(bytes)));
                    }
                }
                if (stopping)
                {
                    StatusLine.Text = "Stopped: no zip file was made.";
                    Say(StatusLine);
                    return;
                }
                // whole, into its place (a zip there already is replaced: its name was chosen in the Save dialog), as
                // every file b0r3d QR saves goes in, on USB sticks that can't swap files too
                await Task.Run(() => Exports.IntoPlace(temporary, zipPath));
                // each file back out of the zip as it is on the disk
                StatusLine.Text = "Reading the files back…";
                using (ZipArchive zip = await Task.Run(() => ZipFile.OpenRead(zipPath)))
                    foreach (var (item, entry, hash) in made)
                    {
                        byte[] inZip = await Task.Run(() =>
                        {
                            ZipArchiveEntry found = zip.GetEntry(entry);
                            if (found == null) return null;
                            using (Stream from = found.Open())
                            using (var copy = new MemoryStream())
                            {
                                from.CopyTo(copy);
                                return copy.ToArray();
                            }
                        });
                        if (inZip == null || !Hash(inZip).SequenceEqual(hash))
                        {
                            outcome.Failed.Add(entry + ": it came back out of the zip different");
                            continue;
                        }
                        outcome.Made++;
                        if (!await ReadsBack(inZip, item, snapshot)) outcome.Unread.Add(entry);
                    }
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is InvalidDataException || x is System.Security.SecurityException)
            {
                StatusLine.Text = LongText.Cap("Couldn't make " + Path.GetFileName(zipPath) + ": " + x.Message);
                Say(StatusLine);
                return;
            }
            finally
            {
                TryDelete(temporary);   // gone already once the zip is in its place
                Busy(false);
            }
            madeIn = zipPath;
            Finish(outcome, todo.Count, Path.GetFileName(zipPath));
        }

        sealed class Outcome
        {
            public int Made, Skipped;
            public readonly List<string> Failed = new List<string>(), Unread = new List<string>();
        }

        // A row's file, or null after noting why it couldn't be made (a picture too big to draw, say). Whatever goes
        // wrong in making one row's file is that row's, as the rows that can't be made at all are: it's listed with
        // why, and the other rows are still made.
        byte[] MakeOne(BatchItem item, BatchKind snapshot, Outcome outcome, string file)
        {
            try
            {
                return MakeFile(item, snapshot);
            }
            catch (Exception x)
            {
                outcome.Failed.Add(file + ": " + (x is OutOfMemoryException ? "there isn't enough memory for a picture that big" :
                                                  x is QrException || x is IOException ? x.Message : "it couldn't be made (" + x.Message + ")"));
                return null;
            }
        }

        // writes a file whole, then reads it back from the disk; null when that worked, else why not
        static string WriteAndReadBack(string path, byte[] bytes, out byte[] back)
        {
            back = null;
            try
            {
                Exports.Write(path, bytes);
                back = File.ReadAllBytes(path);
                return null;
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException)
            {
                return x.Message;
            }
        }

        static bool Same(byte[] a, byte[] b) => a != null && b != null && a.Length == b.Length && a.SequenceEqual(b);

        static byte[] Hash(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) return sha.ComputeHash(bytes);
        }

        // does the file scan as its row says? (its picture drawn here, read off the window's thread)
        static async Task<bool> ReadsBack(byte[] bytes, BatchItem item, BatchKind snapshot)
        {
            BitmapSource picture;
            try
            {
                picture = BatchExport.Picture(bytes, item, snapshot);
            }
            catch (Exception x) when (x is NotSupportedException || x is FileFormatException || x is ArgumentException || x is QrException || x is OutOfMemoryException)
            {
                return false;
            }
            if (picture == null) return false;
            byte[] grays = ScanCheck.Grays(picture, out int width, out int height);
            string read = await Task.Run(() => BatchExport.Read(grays, width, height, snapshot));
            return read == item.Expected;
        }

        void Progress(int n, int total, string file)
        {
            StatusLine.Text = LongText.Cap("Making " + (n + 1).ToString("N0") + " of " + total.ToString("N0") + ": " + file);
        }

        void Finish(Outcome outcome, int total, string where)
        {
            int leftOut = items.Count(i => i.Problem != null);
            var said = new StringBuilder();
            if (stopping) said.Append("Stopped. ");
            said.Append(outcome.Made == 0 ? "No files were made" : "Made " + Count(outcome.Made, "file") + (outcome.Made < total ? " of " + total.ToString("N0") : "") + " in " + where);
            said.Append(outcome.Made > 0 && outcome.Unread.Count == 0 ? ", each read back and scanned." : ".");
            if (outcome.Skipped > 0) said.Append(" " + Count(outcome.Skipped, "file") + (outcome.Skipped == 1 ? " was" : " were") + " already there and left as they were.");
            if (leftOut > 0) said.Append(" " + Count(leftOut, "row") + " couldn't be made (listed above).");
            if (outcome.Failed.Count > 0) said.Append(" " + Count(outcome.Failed.Count, "file") + (outcome.Failed.Count == 1 ? " wasn't" : " weren't") + " made:");
            if (outcome.Unread.Count > 0) said.Append(" " + Count(outcome.Unread.Count, "file") + " didn't scan when read back: try more contrast or a larger size.");
            StatusLine.Text = LongText.Cap(said.ToString());
            StatusLine.SetResourceReference(TextBlock.ForegroundProperty, outcome.Failed.Count + outcome.Unread.Count > 0 || outcome.Made == 0 ? "Warn" : "Good");
            OutcomeList.Items.Clear();
            foreach (string line in outcome.Failed.Concat(outcome.Unread.Select(f => f + ": didn't scan when read back")).Take(30)) OutcomeList.Items.Add(Line(line));
            OpenFolderButton.Visibility = outcome.Made > 0 ? Visibility.Visible : Visibility.Collapsed;
            LastOutcome = outcome.Made + " made, " + outcome.Skipped + " skipped, " + outcome.Failed.Count + " failed, " + outcome.Unread.Count + " unread";
            Say(StatusLine);
        }

        // for the tests: "48 made, 0 skipped, 0 failed, 0 unread"
        internal string LastOutcome { get; private set; } = "";

        void ClearOutcome()
        {
            StatusLine.Text = "";
            StatusLine.ClearValue(TextBlock.ForegroundProperty);
            OutcomeList.Items.Clear();
            OpenFolderButton.Visibility = Visibility.Collapsed;
            LastOutcome = "";
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException)
            {
            }
        }

        // Explorer at the files: the folder, or the zip picked out in its folder
        void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            if (madeIn == null) return;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", Directory.Exists(madeIn) ? "\"" + madeIn + "\"" : "/select,\"" + madeIn + "\"") { UseShellExecute = true });
            }
            catch (Exception x) when (x is Win32Exception || x is InvalidOperationException)
            {
                MessageBox.Show(this, "Couldn't open Explorer:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OnClose(object sender, RoutedEventArgs e) => Close();

        // ---------- an example to start from ----------

        void OnExample(object sender, RoutedEventArgs e)
        {
            var types = kind.Barcode != null
                ? new List<FileDialogs.FileType> { new FileDialogs.FileType("CSV file", ".csv") }
                : new List<FileDialogs.FileType> { new FileDialogs.FileType("Example: links and text, with captions, file names and fonts", ".csv"), new FileDialogs.FileType("Example: contacts", ".csv") };
            string path;
            int chosen;
            try
            {
                path = FileDialogs.SaveAs(this, "Save an example CSV", types, 0, kind.Barcode != null ? BarcodeFormats.Get(kind.Barcode.Value).Id + "-batch-example.csv" : "batch-example.csv", out chosen);
            }
            catch (COMException x)
            {
                MessageBox.Show(this, "Windows' Save dialog didn't open:\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path == null) return;
            try
            {
                Exports.Write(path, new UTF8Encoding(false).GetBytes(Example(chosen)));
                StatusLine.Text = LongText.Cap("Saved " + Path.GetFileName(path) + ". Fill it in with a spreadsheet or a text editor, then choose it here.");
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is NotSupportedException || x is System.Security.SecurityException)
            {
                MessageBox.Show(this, "Couldn't save " + Path.GetFileName(path) + ":\n\n" + x.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // the examples (b0r3d QR's own, with every column the website reads; kept as files in the exe, assets\batch)
        internal string Example(int which)
        {
            if (kind.Barcode != null)
                return "data,fileName\r\n" + Quote(BarcodeFormats.Get(kind.Barcode.Value).Example) + ",first_code\r\n";
            using (Stream stream = typeof(BatchWindow).Assembly.GetManifestResourceStream(which == 1 ? "B0r3dQR.batch-example-contacts.csv" : "B0r3dQR.batch-example.csv"))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }

        static string Quote(string value) => value.IndexOfAny(new[] { ',', '"', ';', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

        void Say(TextBlock line)
        {
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.CreatePeerForElement(line)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
