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

            // the text export's names (TextExportModal.vue): the file name, else qr-<n from 0>, _1, _2 for one taken
            var named = new List<BatchRow> { new BatchRow { Data = "a" }, new BatchRow { Data = "b", FileName = "x y" }, new BatchRow { Data = "c", FileName = "x y" }, new BatchRow { Data = "d", FileName = "x_y" } };
            Check(BatchRows.TextNames(named, new[] { 0, 1, 2, 3 }, ".txt").SequenceEqual(new[] { "qr-0", "x_y", "x_y_1", "x_y_2" }), "the text names: " + string.Join(", ", BatchRows.TextNames(named, new[] { 0, 1, 2, 3 }, ".txt")));
            // two names the same only once made safe: the second isn't lost (the website's zip keeps one of them)
            var clash = new List<BatchRow> { new BatchRow { Data = "1", FileName = "a b" }, new BatchRow { Data = "2", FileName = "a_b" } };
            Check(BatchRows.QrNames(clash).SequenceEqual(new[] { "a_b", "a_b-1" }), "names alike once safe: " + string.Join(", ", BatchRows.QrNames(clash)));
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
    }
}
