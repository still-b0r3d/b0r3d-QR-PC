using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using B0r3dQR.Barcodes;
using B0r3dQR.Batch;

namespace B0r3dQR.Tests
{
    // Batch export: the website's CSV rules and file names, and the Batch export window making files.
    static partial class TestMain
    {
        // every CSV in the website's test data: the same rows, captions, file names and fonts, the same files' names
        static void BatchFilesLikeTheWebsite()
        {
            var wrong = new List<string>();
            void Same(string what, IEnumerable<string> ours, IEnumerable<string> theirs)
            {
                string[] a = ours.ToArray(), b = theirs.ToArray();
                if (!a.SequenceEqual(b)) wrong.Add(what + ": ours [" + string.Join(" | ", a.Select(Show)) + "], the website's [" + string.Join(" | ", b.Select(Show)) + "]");
            }
            foreach (var c in WebsiteQrBatches)
            {
                List<BatchRow> rows = BatchRows.ReadQr(c.Csv, out bool hasFrameText);
                if (!c.Valid)
                {
                    if (rows != null) wrong.Add(c.Name + ": the website says Invalid CSV, but " + rows.Count + " rows were read");
                    continue;
                }
                if (rows == null)
                {
                    wrong.Add(c.Name + ": refused, but the website takes it");
                    continue;
                }
                Same(c.Name + " data", rows.Select(r => r.Data), c.Data);
                Same(c.Name + " captions", rows.Select(r => r.FrameText), c.FrameTexts);
                Same(c.Name + " file names", rows.Select(r => r.FileName), c.FileNames);
                Same(c.Name + " fonts", rows.Select(r => r.Font), c.Fonts);
                Same(c.Name + " files", BatchRows.QrNames(rows), c.ExportNames);
                if (hasFrameText != c.HasFrameText) wrong.Add(c.Name + ": a frame " + (hasFrameText ? "on" : "off") + ", the website's " + (c.HasFrameText ? "on" : "off"));
            }
            foreach (var c in WebsiteBarcodeBatches)
            {
                List<BatchRow> rows = BatchRows.ReadBarcodes(c.Csv);
                Same(c.Name + " rows", rows.Select(r => r.Row + ":" + r.Data + ":" + r.FileName), c.Items.Select(i => i.Row + ":" + i.Data + ":" + (i.FileName ?? "")));
                Same(c.Name + " files", BatchRows.BarcodeNames(rows), c.ExportNames);
            }
            foreach (var c in WebsiteCsvDecoding)
                if (Csv.Read(c.Bytes) != c.Text) wrong.Add("the bytes " + BitConverter.ToString(c.Bytes) + " read as " + Show(Csv.Read(c.Bytes)) + ", the website's " + Show(c.Text));
            Check(wrong.Count == 0, wrong.Count + " differences:\n      " + string.Join("\n      ", wrong.Take(12)));

            // the text export's names: the file name, else qr-<n from 0>, _1, _2 for one taken
            var named = new List<BatchRow> { new BatchRow { Data = "a" }, new BatchRow { Data = "b", FileName = "x y" }, new BatchRow { Data = "c", FileName = "x y" }, new BatchRow { Data = "d", FileName = "x_y" } };
            Check(BatchRows.TextNames(named, new[] { 0, 1, 2, 3 }).SequenceEqual(new[] { "qr-0", "x_y", "x_y_1", "x_y_2" }), "the text names: " + string.Join(", ", BatchRows.TextNames(named, new[] { 0, 1, 2, 3 })));
            // two names the same only once made safe: the second isn't lost (the website's zip keeps one of them)
            var clash = new List<BatchRow> { new BatchRow { Data = "1", FileName = "a b" }, new BatchRow { Data = "2", FileName = "a_b" } };
            Check(BatchRows.QrNames(clash).SequenceEqual(new[] { "a_b", "a_b-1" }), "names alike once safe: " + string.Join(", ", BatchRows.QrNames(clash)));
            // a name over 255 characters is capped so it writes into a folder and unpacks from a zip on Windows
            var longRow = new List<BatchRow> { new BatchRow { Data = "1", FrameText = new string('a', 300) }, new BatchRow { Data = "2", FrameText = new string('a', 300) } };
            List<string> longNames = BatchRows.QrNames(longRow);
            Check(longNames[0].Length == BatchRows.MostNameLength && longNames[1] == longNames[0] + "-1", "long batch names capped: " + longNames[0].Length + ", " + longNames[1].Length);
            // 10,000 rows of one name: linear time, same names
            var many = Enumerable.Range(0, 10000).Select(i => new BatchRow { Row = i + 2, Data = "https://b0r3d.org", FileName = "same" }).ToList();
            List<string> manyBarcodes = BatchRows.BarcodeNames(many);
            Check(manyBarcodes[0] == "same" && manyBarcodes[1] == "same_2" && manyBarcodes[9999] == "same_10000", "10,000 repeated barcode names: " + manyBarcodes[9999]);
        }

        // a batch window for the Make tab as it is, with this CSV read in
        static BatchWindow NewBatch(MainWindow window, string csv)
        {
            BatchWindow batch = window.OpenBatch(null, false);
            LayOut(batch.Root, 660, 760);
            batch.LoadCsvText("test.csv", csv);
            Pump(() => batch.Items.Count > 0, 5000);
            return batch;
        }

        static void MakeBatch(BatchWindow batch, string folder, bool zip = false)
        {
            batch.AskFolder = () => folder;
            batch.AskZip = () => folder;
            (zip ? batch.ZipChoice : batch.FolderChoice).IsChecked = true;
            batch.MakeButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(() => batch.Asking || batch.Running.IsCompleted && batch.LastOutcome != "", 30000);
        }

        // A QR batch into a folder: a file for each row, named as the website names it, each read back; files already
        // there asked about once (numbered, replaced or skipped); a row too long for a code listed and left out
        static void BatchIntoAFolder()
        {
            string folder = Path.Combine(Work, "batch-folder");
            Fresh(folder);
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org");
            string csv = "url,frameText,fileName\nhttps://b0r3d.org/one,Scan me,first\nhttps://b0r3d.org/two,,\nplain text,Hello,\n" + new string('x', 3000) + ",,toolong\n";
            BatchWindow batch = NewBatch(window, csv);
            Check(batch.Items.Count == 4 && batch.Items.Count(i => i.Problem != null) == 1 && batch.Items[3].Problem != null, "the rows: " + string.Join(" | ", batch.Items.Select(i => i.Name + " " + i.Problem)));
            Check(batch.FrameCheck.IsChecked == true && batch.Kind.Frame, "captions in the file should put a frame on, as the website does");
            Check(BatchRows.ReadQr(batch.Example(0), out bool exampleFramed)?.Count == 4 && exampleFramed && BatchRows.ReadQr(batch.Example(1), out _)?.Count == 2,
                "the examples it saves should be batches it reads");
            Check(batch.ProblemsList.Items.Count == 1 && batch.MakeButton.IsEnabled && (batch.MakeButton.Content as string).Contains("3 files"), "the list of rows that can't be made, and Make 3 files: " + batch.MakeButton.Content);
            MakeBatch(batch, folder);
            string[] files = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            Check(batch.LastOutcome == "3 made, 0 skipped, 0 failed, 0 unread", "made: " + batch.LastOutcome + " · " + batch.StatusLine.Text);
            Check(files.SequenceEqual(new[] { "Hello.png", "first.png", "two.png" }), "the files: " + string.Join(", ", files));
            Check(!Directory.GetFiles(folder, "*.tmp").Any(), "a temporary file was left behind");

            // again: asked once about the three already there
            MakeBatch(batch, folder);
            Check(batch.Asking && batch.ClashPanel.Visibility == System.Windows.Visibility.Visible && batch.ClashLine.Text.StartsWith("3 of the files are already in", StringComparison.Ordinal),
                "files already there should be asked about: " + batch.ClashLine.Text);
            batch.Answer(Clash.Number);
            Pump(() => batch.Running.IsCompleted && batch.LastOutcome != "", 30000);
            Check(File.Exists(Path.Combine(folder, "first (2).png")) && File.Exists(Path.Combine(folder, "two (2).png")) && batch.LastOutcome == "3 made, 0 skipped, 0 failed, 0 unread",
                "numbered: " + string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName)));
            MakeBatch(batch, folder);
            batch.Answer(Clash.Skip);
            Pump(() => batch.Running.IsCompleted && batch.LastOutcome != "", 30000);
            Check(batch.LastOutcome == "0 made, 3 skipped, 0 failed, 0 unread" && Directory.GetFiles(folder).Length == 6, "skipped: " + batch.LastOutcome);
            File.WriteAllText(Path.Combine(folder, "first.png"), "not a picture");
            MakeBatch(batch, folder);
            batch.Answer(Clash.Replace);
            Pump(() => batch.Running.IsCompleted && batch.LastOutcome != "", 30000);
            Check(batch.LastOutcome == "3 made, 0 skipped, 0 failed, 0 unread" && new FileInfo(Path.Combine(folder, "first.png")).Length > 100 && Directory.GetFiles(folder).Length == 6,
                "replaced: " + batch.LastOutcome);
            batch.Close();
        }

        // A folder far down a drive: each file's name cut to fit Windows' 260 characters for a whole path (the temporary
        // name it's written under first too), its extension kept, and names that meet once cut numbered; a folder too far
        // down for names of 12 characters says so and makes nothing. (Names were only cut to 200 characters, so a file in
        // a folder like this failed.)
        static void BatchIntoAFolderFarDown()
        {
            string far = Path.Combine(Work, "far");
            Fresh(far);
            string deep = far;
            while (deep.Length < 190) deep = Path.Combine(deep, "folder-inside-folder");
            Directory.CreateDirectory(deep);
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org");
            string common = new string('n', 150);
            BatchWindow batch = NewBatch(window, "url,fileName\nhttps://b0r3d.org/one," + common + "a" + new string('z', 49) + "\nhttps://b0r3d.org/two," + common + "b" + new string('z', 49) + "\nhttps://b0r3d.org/three,short\n");
            Check(batch.Items.Count == 3 && batch.Items[0].Name.Length == 200, "the rows: " + string.Join(" | ", batch.Items.Select(i => i.Name.Length + " " + i.Problem)));
            MakeBatch(batch, deep);
            int longest = BatchExport.LongestName(deep, ".png");
            string[] files = Directory.GetFiles(deep).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            string[] expected = { new string('n', longest) + ".png", new string('n', longest - 4) + " (2).png", "short.png" };
            Check(batch.LastOutcome == "3 made, 0 skipped, 0 failed, 0 unread" && files.SequenceEqual(expected.OrderBy(f => f, StringComparer.Ordinal), StringComparer.Ordinal),
                "into " + deep.Length + " characters of folder: " + batch.LastOutcome + " · " + batch.StatusLine.Text + " · " + string.Join(", ", files.Select(f => f.Length + " " + f.Substring(0, Math.Min(f.Length, 12)))));
            Check(Directory.GetFiles(deep).All(f => f.Length <= BatchExport.MostPath) && Path.Combine(deep, "." + expected[0] + ".1a2b3c4d.tmp").Length <= BatchExport.MostPath && longest >= BatchExport.ShortestName,
                "each file and its temporary name should fit in 259 characters");
            Check(!Directory.GetFiles(deep, "*.tmp").Any(), "a temporary file was left behind");

            // again, numbered after the ones there, within the same room
            MakeBatch(batch, deep);
            batch.Answer(Clash.Number);
            Pump(() => batch.Running.IsCompleted && batch.LastOutcome != "", 30000);
            Check(batch.LastOutcome == "3 made, 0 skipped, 0 failed, 0 unread" && Directory.GetFiles(deep).Length == 6 && Directory.GetFiles(deep).All(f => f.Length <= BatchExport.MostPath),
                "numbered far down: " + batch.LastOutcome + " · " + string.Join(", ", Directory.GetFiles(deep).Select(f => f.Length)));

            // too far down: said, nothing made
            string tooFar = deep;
            while (tooFar.Length < 236) tooFar = Path.Combine(tooFar, "x");
            Directory.CreateDirectory(tooFar);
            MakeBatch(batch, tooFar);
            Check(batch.LastOutcome == "the folder's path is too long" && batch.StatusLine.Text.StartsWith("Nothing was made: ", StringComparison.Ordinal) && Directory.GetFiles(tooFar).Length == 0,
                "a folder too far down: " + batch.LastOutcome + " · " + batch.StatusLine.Text);
            batch.Close();
        }

        // Names that differ only in capitals (Windows doesn't tell them apart) never overwrite or skip each other; see-through
        // PNGs and text files read back as on paper; a CSV another program has open still reads; a row left out doesn't
        // take a name from the next; "Default" is the default font; nothing prints from the last file while one's checked
        static void BatchCornerCases()
        {
            string folder = Path.Combine(Work, "batch-corners");
            Fresh(folder);
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org");
            window.ChooseCodeType(BarcodeType.Code128);
            BatchWindow cases = NewBatch(window, "data\nABC\nabc\n");
            File.WriteAllText(Path.Combine(folder, "ABC.png"), "already here");
            MakeBatch(cases, folder);
            cases.Answer(Clash.Replace);
            Pump(() => cases.Running.IsCompleted && cases.LastOutcome != "", 30000);
            string[] files = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
            Check(cases.LastOutcome == "2 made, 0 skipped, 0 failed, 0 unread" && files.Length == 2 && files.Contains("abc (2).png") && new FileInfo(Path.Combine(folder, "ABC.png")).Length > 100,
                "Replace with names alike but for capitals: " + cases.LastOutcome + " · " + string.Join(", ", files));
            cases.Close();

            // a row the type refuses leaves its name for the next (the website names only what it makes)
            window.ChooseCodeType(BarcodeType.Ean13);
            BatchWindow refused = NewBatch(window, "data,fileName\n9506000134352,shelf\n4006381333932,box\n4006381333931,box\n");
            Check(refused.Items[1].Problem != null && refused.Items[1].Name == null && refused.Items[2].Name == "box", "names after a refused row: " + string.Join(", ", refused.Items.Select(i => i.Name ?? "-")));
            refused.Close();

            // see-through PNGs read back as on white paper
            window.ChooseCodeType(null);
            window.BackgroundCheck.IsChecked = false;
            Pump(() => false, 50);
            BatchWindow clear = NewBatch(window, "url\nhttps://b0r3d.org/clear\n");
            Check(clear.PrintButton.IsEnabled && clear.MakeButton.IsEnabled, "made ready");
            string clearFolder = Path.Combine(folder, "clear");
            Fresh(clearFolder);
            MakeBatch(clear, clearFolder);
            Check(clear.LastOutcome == "1 made, 0 skipped, 0 failed, 0 unread", "a see-through PNG should read back: " + clear.LastOutcome);
            // nothing to make or print from the last file while the next is checked
            clear.LoadCsvText("next.csv", "url\nhttps://b0r3d.org/next\n");
            Check(!clear.PrintButton.IsEnabled && !clear.MakeButton.IsEnabled, "Print them… and Make should wait while the rows are checked");
            Pump(() => clear.MakeButton.IsEnabled, 5000);
            clear.Close();
            window.BackgroundCheck.IsChecked = true;

            // text files read from their characters, whatever the design's colours; "Default" is the default font
            window.DotsColorBox.TextField.Text = "#ffffff";
            window.DotsColorBox.Commit();
            window.BackgroundColorBox.TextField.Text = "#000000";
            window.BackgroundColorBox.Commit();
            Pump(() => false, 50);
            BatchWindow text = NewBatch(window, "url,frameText,fileName,frameFontFamily\n" + new string('x', 3000) + ",,code,\nhello,Hi,code,Default\n");
            text.TextChoice.IsChecked = true;
            Pump(() => text.MakeButton.IsEnabled, 5000);
            Check(text.Items[1].Name == "code", "a text file's name after a refused row: " + text.Items[1].Name);
            Check(BatchExport.DesignFor(text.Kind, text.Items[1].Row).Frame?.FontFamily == null, "\"Default\" should be the frame's own font");
            string textFolder = Path.Combine(folder, "text");
            Fresh(textFolder);
            MakeBatch(text, textFolder);
            Check(text.LastOutcome == "1 made, 0 skipped, 0 failed, 0 unread" && File.Exists(Path.Combine(textFolder, "code.txt")), "text read back from its characters: " + text.LastOutcome);
            text.Close();

            // a CSV another program holds open for writing (as Excel does) still reads
            string held = Path.Combine(folder, "held.csv");
            File.WriteAllText(held, "url\nhttps://b0r3d.org/held\n");
            using (new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                BatchWindow open = window.OpenBatch(held, false);
                Pump(() => open.Items.Count > 0, 5000);
                Check(open.Items.Count == 1 && open.ReadProblem.Visibility != System.Windows.Visibility.Visible, "a CSV open in another program: " + open.ReadProblem.Text);
                open.Close();
            }
        }

        // Every kind of file, into a zip: each file back out of the zip and scanned; barcodes too, a row its type
        // refuses listed with the website's message
        static void BatchIntoAZip()
        {
            string folder = Path.Combine(Work, "batch-zip");
            Fresh(folder);
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org");
            BatchWindow batch = NewBatch(window, "url,frameText,fileName\nhttps://b0r3d.org/a,,a\nhttps://b0r3d.org/b,,b\n");
            foreach (var (choice, extension) in new[] { (batch.PngChoice, ".png"), (batch.JpgChoice, ".jpg"), (batch.SvgChoice, ".svg"), (batch.PdfChoice, ".pdf"), (batch.TextChoice, ".txt") })
            {
                choice.IsChecked = true;
                Pump(() => batch.Items.All(i => i.Name != null) && batch.MakeButton.IsEnabled, 5000);
                string zip = Path.Combine(folder, "codes" + extension + ".zip");
                MakeBatch(batch, zip, true);
                Check(batch.LastOutcome == "2 made, 0 skipped, 0 failed, 0 unread", extension + ": " + batch.LastOutcome + " · " + batch.StatusLine.Text);
                using (ZipArchive archive = ZipFile.OpenRead(zip))
                    Check(archive.Entries.Select(e => e.FullName).OrderBy(n => n).SequenceEqual(new[] { "a" + extension, "b" + extension }), extension + ": the zip holds " + string.Join(", ", archive.Entries.Select(e => e.FullName)));
            }
            Check(!Directory.GetFiles(folder, "*.tmp").Any(), "a temporary zip was left behind");
            batch.Close();

            window.ChooseCodeType(BarcodeType.Ean13);
            BatchWindow barcodes = NewBatch(window, "data,fileName\n9506000134352,first\n4006381333932,bad\n400638133393,\n");
            Check(barcodes.Items.Count == 3 && barcodes.Items[1].Problem == BarcodeFormats.Get(BarcodeType.Ean13).Check("4006381333932") && barcodes.Items[2].Name == "400638133393",
                "the barcode rows: " + string.Join(" | ", barcodes.Items.Select(i => i.Name + " " + i.Problem)));
            Check(barcodes.FrameCheck.Visibility != System.Windows.Visibility.Visible && barcodes.TextChoice.Visibility != System.Windows.Visibility.Visible, "barcodes have no frame or text files");
            string barcodeFolder = Path.Combine(folder, "barcodes");
            Fresh(barcodeFolder);
            MakeBatch(barcodes, barcodeFolder);
            Check(barcodes.LastOutcome == "2 made, 0 skipped, 0 failed, 0 unread" && File.Exists(Path.Combine(barcodeFolder, "first.png")) && File.Exists(Path.Combine(barcodeFolder, "400638133393.png")),
                "barcodes made: " + barcodes.LastOutcome + " · " + string.Join(", ", Directory.GetFiles(barcodeFolder).Select(Path.GetFileName)));
            barcodes.Close();
        }

        // A row whose file can't be made, for whatever reason (here a made-up one: the error odd text once gave), is
        // listed with why and the other rows are still made, into a zip or a folder, and the summary says so; nothing
        // half-made is left, a zip's temporary file included, nor when the batch is stopped half-way
        static void BatchRowThatFails()
        {
            string folder = Path.Combine(Work, "batch-fails");
            Fresh(folder);
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org");
            BatchWindow batch = NewBatch(window, "url,frameText,fileName\nhttps://b0r3d.org/a,,a\nhttps://b0r3d.org/b,,b\nhttps://b0r3d.org/c,,c\n");
            batch.MakeFile = (item, kind) => item.Name == "b" ? throw new ArgumentException("Invalid Unicode code point found at index 0.") : BatchExport.Make(item, kind, out _);
            foreach (bool zip in new[] { true, false })
            {
                string into = zip ? Path.Combine(folder, "codes.zip") : Path.Combine(folder, "files");
                if (!zip) Fresh(into);
                MakeBatch(batch, into, zip);
                string[] made;
                if (zip)
                    using (ZipArchive archive = ZipFile.OpenRead(into)) made = archive.Entries.Select(e => e.FullName).ToArray();
                else made = Directory.GetFiles(into).Select(Path.GetFileName).ToArray();
                List<string> listed = batch.OutcomeList.Items.Cast<System.Windows.Controls.TextBlock>().Select(t => t.Text).ToList();
                string where = zip ? "into a zip" : "into a folder";
                Check(batch.LastOutcome == "2 made, 0 skipped, 1 failed, 0 unread" && made.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(new[] { "a.png", "c.png" }),
                    where + ": " + batch.LastOutcome + " · " + string.Join(", ", made) + " · " + batch.StatusLine.Text);
                Check(batch.StatusLine.Text.Contains("1 file wasn't made:") && listed.Count == 1 && listed[0] == "b.png: it couldn't be made (Invalid Unicode code point found at index 0.)",
                    where + ", the row that failed should be listed with why: " + batch.StatusLine.Text + " · " + string.Join(" | ", listed));
            }
            Check(!Directory.GetFiles(folder, "*.tmp", SearchOption.AllDirectories).Any(), "a temporary file was left behind: " + string.Join(", ", Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(Path.GetFileName)));

            // stopped half-way, at the second row: no zip, and no temporary file
            batch.MakeFile = (item, kind) =>
            {
                if (item.Name == "b") batch.CancelButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                return BatchExport.Make(item, kind, out _);
            };
            string stopped = Path.Combine(folder, "stopped.zip");
            batch.AskZip = () => stopped;
            batch.ZipChoice.IsChecked = true;
            batch.MakeButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(() => batch.Running.IsCompleted, 30000);
            Check(!File.Exists(stopped) && batch.StatusLine.Text == "Stopped: no zip file was made." && !Directory.GetFiles(folder, "*.tmp").Any(),
                "stopped: " + batch.StatusLine.Text + " · " + string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName)));
            batch.Close();
        }

        // Rows named as Windows names its devices (CON, aux, Com1…) get _ on their files' names in all three naming rules
        // (Windows 10 makes no file called CON.png), and a batch of them into a folder makes every file
        static void BatchDeviceNames()
        {
            string[] given = { "CON", "aux", "Com1", "lpt9", "NUL", "prn", "COM0", "CONSOLE", "CON" };
            var rows = given.Select((name, i) => new BatchRow { Row = i + 2, Data = "https://b0r3d.org/" + i, FileName = name }).ToList();
            string[] qr = BatchRows.QrNames(rows).ToArray(), barcodes = BatchRows.BarcodeNames(rows).ToArray(), text = BatchRows.TextNames(rows, Enumerable.Range(0, rows.Count).ToList()).ToArray();
            Check(qr.SequenceEqual(new[] { "CON_", "aux_", "Com1_", "lpt9_", "NUL_", "prn_", "COM0", "CONSOLE", "CON-8" }), "QR names: " + string.Join(", ", qr));
            Check(barcodes.SequenceEqual(new[] { "CON_", "aux_", "Com1_", "lpt9_", "NUL_", "prn_", "COM0", "CONSOLE", "CON__2" }), "barcode names: " + string.Join(", ", barcodes));
            Check(text.SequenceEqual(new[] { "CON_", "aux_", "Com1_", "lpt9_", "NUL_", "prn_", "COM0", "CONSOLE", "CON__1" }), "text names: " + string.Join(", ", text));
            // a name from the code's text, too
            Check(BatchRows.QrNames(new List<BatchRow> { new BatchRow { Data = "nul" } }).Single() == "nul_", "a row whose text is nul: " + BatchRows.QrNames(new List<BatchRow> { new BatchRow { Data = "nul" } }).Single());

            string folder = Path.Combine(Work, "batch-devices");
            Fresh(folder);
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org");
            BatchWindow batch = NewBatch(window, "url,fileName\nhttps://b0r3d.org/a,CON\nhttps://b0r3d.org/b,aux\nhttps://b0r3d.org/c,nul\n");
            MakeBatch(batch, folder);
            string[] files = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            Check(batch.LastOutcome == "3 made, 0 skipped, 0 failed, 0 unread" && files.SequenceEqual(new[] { "CON_.png", "aux_.png", "nul_.png" }),
                "into a folder: " + batch.LastOutcome + " · " + string.Join(", ", files) + " · " + batch.StatusLine.Text);
            batch.Close();
        }

        // Saving over a file where Windows can't swap files (some USB sticks): the old file is moved out of the way, the
        // new one in, and the old one deleted (moves need no room, so a nearly full stick loses nothing); when the new one
        // can't go in, the old one goes back; a read-only file stays as it is. A batch's zip goes in the same way as every
        // other file saved, and nothing temporary is left
        static void SavingWhereFilesCantBeSwapped()
        {
            string folder = Path.Combine(Work, "no-swapping");
            Fresh(folder);
            Action<string, string> swap = Exports.Swap, move = Exports.Move;
            int swaps = 0;
            Exports.Swap = (temporary, full) =>
            {
                swaps++;
                throw new IOException("The request is not supported.");
            };
            string Left() => string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
            try
            {
                string file = Path.Combine(folder, "code.png");
                Exports.Write(file, new byte[] { 1, 2, 3 });
                Exports.Write(file, new byte[] { 4, 5 });
                Check(swaps == 1 && File.ReadAllBytes(file).SequenceEqual(new byte[] { 4, 5 }) && Left() == "code.png",
                    "a file saved over another: " + swaps + " swaps, " + BitConverter.ToString(File.ReadAllBytes(file)) + " · " + Left());

                // the new file can't go in once the old one's out of the way: the old one goes back
                Exports.Move = (from, to) =>
                {
                    if (from.EndsWith(".tmp", StringComparison.Ordinal) && string.Equals(to, file, StringComparison.OrdinalIgnoreCase)) throw new IOException("The process cannot access the file because it is being used by another process.");
                    move(from, to);
                };
                try
                {
                    Exports.Write(file, new byte[] { 6, 7, 8 });
                    Check(false, "a save whose new file can't go in should fail, for the window to say so");
                }
                catch (IOException)
                {
                }
                Exports.Move = move;
                Check(File.ReadAllBytes(file).SequenceEqual(new byte[] { 4, 5 }) && Left() == "code.png", "after a new file that couldn't go in: " + BitConverter.ToString(File.ReadAllBytes(file)) + " · " + Left());

                // a read-only file
                File.SetAttributes(file, FileAttributes.ReadOnly);
                try
                {
                    Exports.Write(file, new byte[] { 9 });
                    Check(false, "a read-only file was saved over where files can't be swapped");
                }
                catch (UnauthorizedAccessException)
                {
                }
                finally
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Check(File.ReadAllBytes(file).SequenceEqual(new byte[] { 4, 5 }) && Left() == "code.png", "the read-only file: " + BitConverter.ToString(File.ReadAllBytes(file)) + " · " + Left());
                swaps = 0;

                MainWindow window = NewMain();
                Type(window, "https://b0r3d.org");
                BatchWindow batch = NewBatch(window, "url,fileName\nhttps://b0r3d.org/a,first\nhttps://b0r3d.org/b,second\n");
                string zip = Path.Combine(folder, "codes.zip");
                File.WriteAllText(zip, "an old zip");
                MakeBatch(batch, zip, zip: true);
                Check(swaps == 1, "the zip should go in as every file saved does: " + swaps + " swaps");
                Check(batch.LastOutcome == "2 made, 0 skipped, 0 failed, 0 unread", "the zip saved over another: " + batch.LastOutcome + " · " + batch.StatusLine.Text);
                using (ZipArchive archive = ZipFile.OpenRead(zip))
                {
                    string[] entries = archive.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                    Check(entries.SequenceEqual(new[] { "first.png", "second.png" }), "in the zip: " + string.Join(", ", entries));
                }
                Check(Left() == "code.png, codes.zip", "something was left behind: " + Left());
                batch.Close();
            }
            finally
            {
                Exports.Swap = swap;
                Exports.Move = move;
            }
        }
    }
}
