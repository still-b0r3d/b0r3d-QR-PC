using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Barcodes;
using B0r3dQR.Camera;
using B0r3dQR.Design;
using B0r3dQR.Qr;
using B0r3dQR.Scan;

namespace B0r3dQR.Tests
{
    // WPF's text layout ends the whole program when a line has a letter with about 720 marks on it (a
    // caption is drawn through Unicode.Clusters for this reason). A code someone else made can hold such
    // text, so every place the program shows text from outside itself must cap the marks on each letter,
    // the same way, before it's laid out: marks Windows knows and .NET Framework's older Unicode doesn't
    // (U+1AC1, a mark since Unicode 14) too. The crashing cases are drawn offscreen in a run of this exe
    // of its own (--longtext), so if the fix were missing it would end that run, not the test run.
    static partial class TestMain
    {
        // real words, with the most marks and joined letters on one letter their scripts have, which every
        // place that caps the marks must leave as they are: Sanskrit's kārtsnya (र्त्स्न्य, nine letters and
        // signs in one), Bengali, Tamil, Thai (tone marks over vowels, sara am), Khmer (a consonant under a
        // consonant under another: ស្ត្រី), Myanmar (kinzi), Tibetan (om mani padme hum, ཧཱུྃ), Arabic and
        // Hebrew with all their vowel signs (and the Bible's cantillation marks), Korean (and old Hangul
        // written in jamo), and Vietnamese with its accents apart (NFD: two on one letter)
        static readonly string[] RealText =
        {
            "स्वतंत्रता दिवस की शुभकामनाएँ। कार्त्स्न्य, ज्ञान, क्ष्म्य",
            "আমি তোমাকে ভালোবাসি। স্বাধীনতা, ক্ষ্ম, রবীন্দ্রনাথ",
            "வணக்கம், தமிழ்நாடு, ஸ்ரீ, க்ஷ",
            "สวัสดีครับ ภาษาไทย น้ำ ที่นี่ กี่ปี",
            "សួស្តី ភាសាខ្មែរ ស្ត្រី ព្រះរាជាណាចក្រកម្ពុជា",
            "မင်္ဂလာပါ မြန်မာဘာသာ သင်္ချိုင်း",
            "བཀྲ་ཤིས་བདེ་ལེགས། ཨོཾ་མ་ཎི་པདྨེ་ཧཱུྃ། སྐྱབས་",
            "السَّلَامُ عَلَيْكُمْ — بِسْمِ ٱللَّهِ ٱلرَّحْمَٰنِ ٱلرَّحِيمِ",
            "שָׁלוֹם עֲלֵיכֶם — בְּרֵאשִׁ֖ית בָּרָ֣א אֱלֹהִ֑ים",
            "안녕하세요 한국어 ᄒᆞᆫ글",
            "Xin chào, cảm ơn! Tiếng Việt có dấu",
        };

        static void NoOutsideTextEndsTheProgram()
        {
            string dir = Path.Combine(Work, "longtext");
            var start = new ProcessStartInfo(Path.Combine(TestsFolder, "b0r3dQRTests.exe"), "--longtext \"" + dir + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            string said;
            using (Process run = Process.Start(start))
            {
                said = run.StandardOutput.ReadToEnd();
                if (!run.WaitForExit(60000))
                {
                    run.Kill();
                    Check(false, "the run showing text with many marks was still going after 60 seconds, so it was stopped");
                }
                Check(run.ExitCode == 0 && said.Contains("ok"),
                    "showing text with many marks on a letter ended the run or a check failed (exit code " + run.ExitCode + "): " + said.Trim());
            }
            foreach (string line in said.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && l != "ok" && !l.StartsWith("step: "))) Console.WriteLine("      " + line);
        }

        // The one rule every place that shows outside text caps the marks by (Unicode.Capped for text boxes
        // and blocks, Clusters for captions, OverTheLimit for an SVG picture's text): the same string back
        // (nothing allocated) when no run is over the limit, else a letter keeps its first MostMarks and the
        // rest are left out; what's on a letter by Unicode 17, not by .NET Framework's older Unicode; and real
        // words in every script kept whole
        static void OneRuleForWhatsOnALetter()
        {
            const int most = CaptionFont.MostMarks;
            string plain = "Scan me for a web link";
            Check(ReferenceEquals(Unicode.Capped(plain, most), plain), "a text with no long run should come back as itself");
            string capped = Unicode.Capped("a" + new string((char)0x301, 1000) + " b", most);
            Check(Unicode.Points(capped).Count == 1 + most + 2, "a letter should keep its first " + most + " marks: " + Unicode.Points(capped).Count);
            Check(Unicode.Capped("a" + new string((char)0x301, most) + "b", most).Length == 2 + most, "exactly " + most + " marks are kept whole");
            // ...by Unicode 17's marks, not .NET's older ones: marks of Unicode 14 (nonspacing), 13 and 10 (spacing)
            foreach (int mark in new[] { 0x1AC1, 0x1DFA, 0x16FF0, 0x1CF7 })
            {
                string m = char.ConvertFromUtf32(mark);
                List<int> kept = Unicode.Points(Unicode.Capped("a" + string.Concat(Enumerable.Repeat(m, 1000)) + " b", most));
                Check(kept.Count == 1 + most + 2 && kept[most] == mark, "a letter should keep its first " + most + " U+" + mark.ToString("X4") + ": " + kept.Count);
                string whole = "a" + string.Concat(Enumerable.Repeat(m, most)) + "b";
                Check(ReferenceEquals(Unicode.Capped(whole, most), whole), "exactly " + most + " U+" + mark.ToString("X4") + " are kept whole");
            }
            // ...and a code point Unicode hasn't given a character yet counts towards the cap (a later Windows may
            // know it as a mark), but is a character of its own to the fonts, as in Chromium
            List<int> unassigned = Unicode.Points(Unicode.Capped("a" + new string((char)0x378, 1000) + " b", most));
            Check(unassigned.Count == 1 + most + 2, "a letter should keep its first " + most + " unassigned code points: " + unassigned.Count);
            Check(Unicode.Clusters("a͸́b", most).SequenceEqual(new[] { "a", "͸́", "b" }),
                "an unassigned code point should be a character of its own: " + string.Join(" | ", Unicode.Clusters("a͸́b", most).Select(Show)));
            // ...a format character, a space or a line ends the run, as it ends WPF's (U+0378 doesn't: it counts)
            foreach (string end in new[] { "​", "⁠", "­", " ", "\n", "؀", "\U00013430" })
            {
                string runs = "a" + string.Concat(Enumerable.Repeat(new string((char)0x301, most) + end, 20));
                Check(ReferenceEquals(Unicode.Capped(runs, most), runs), "U+" + char.ConvertToUtf32(end, 0).ToString("X4") + " should end a letter's run of marks");
            }

            // the guard's quick count (what the SVG reader refuses by too) and the captions' clusters leave out the same:
            // 2,000 texts of letters, marks old and new, joiners, emoji, code points with no character yet, controls and
            // spaces, in runs around the limit
            var random = new Random(23);
            string[] parts = { "a", "क", "́", "᫁", "᳷", "्", "‍", "‌", "️", "\U000E0100", "\U00016FF0", "͸", "\U0001F469", "\U0001F3FD", "\U0001F1FA",
                               "7", " ", "\n", "​", "؀", "\uD800", "￿" };
            for (int n = 0; n < 2000; n++)
            {
                var text = new StringBuilder();
                while (text.Length < 400)
                {
                    string part = parts[random.Next(parts.Length)];
                    text.Insert(text.Length, part, random.Next(4) == 0 ? random.Next(80, 130) : 1);
                }
                string s = text.ToString();
                bool over = Unicode.OverTheLimit(s, most), cut = string.Concat(Unicode.Clusters(s, most)) != s;
                Check(over == cut, "the quick count says " + (over ? "over" : "not over") + " the limit where the clusters " + (cut ? "leave some out" : "keep it all") + ": " + Show(s.Substring(0, 60)));
            }

            // ...and a text box's guard, which looks only at the letters around a change, caps the text just as the cap on the
            // whole of it does: 2,000 changes (of the same parts, typed, pasted or deleted anywhere, halves of emoji too) to a
            // text within the limit
            var edits = new Random(29);
            string Some(int length)
            {
                var text = new StringBuilder();
                while (text.Length < length) text.Insert(text.Length, parts[edits.Next(parts.Length)], edits.Next(4) == 0 ? edits.Next(40, 130) : 1);
                return text.ToString();
            }
            for (int n = 0; n < 2000; n++)
            {
                string before = Unicode.Capped(Some(300), most);
                int at = edits.Next(before.Length + 1), gone = edits.Next(Math.Min(20, before.Length - at) + 1);
                string added = edits.Next(3) == 0 ? "" : Some(edits.Next(1, 150));
                string after = before.Substring(0, at) + added + before.Substring(at + gone);
                List<int> cuts = null;
                string guarded = LongText.Check(after, new List<LongText.Change> { new LongText.Change(at, added.Length, gone) }, ref cuts, false, out _) ?? after;
                Check(guarded == Unicode.Capped(after, most), "the guard, looking around a change at " + at + " (" + gone + " gone, " + added.Length + " in), capped " + Show(after.Substring(0, Math.Min(60, after.Length))) +
                    " otherwise than the cap on the whole text");

                // a paste there instead: what fits goes in as it is (the same string), and what goes in never puts a letter
                // over the limit (but where marks after it go on its last letter, which the guard then sees to)
                string pasted = Some(edits.Next(1, 200));
                string kept = Unicode.Insertable(before, at, at + gone, pasted, most);
                string into = before.Substring(0, at) + kept + before.Substring(at + gone);
                Unicode.Around(before, at + gone, at + gone, out _, out int marksAfter);
                if (!Unicode.OverTheLimit(before.Substring(0, at) + pasted + before.Substring(at + gone), most))
                    Check(ReferenceEquals(kept, pasted), "a paste that fits should go in as it is: " + Show(pasted.Substring(0, Math.Min(60, pasted.Length))));
                else if (marksAfter == at + gone)
                    Check(!Unicode.OverTheLimit(into, most), "a paste should go in with what puts a letter over the limit left out: " + Show(pasted.Substring(0, Math.Min(60, pasted.Length))));
            }

            // real words in every script come through the cap as they are, as the same string, and the SVG reader
            // draws them
            foreach (string text in RealText)
            {
                Check(ReferenceEquals(Unicode.Capped(text, most), text), "the cap changed real text: " + text);
                Check(string.Concat(Unicode.Clusters(text, most)) == text, "a caption left part of real text out: " + text);
                Images.Drawing(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><text y=\"20\">" + text + "</text></svg>"), null, out string why);
                Check(why == null, "an SVG picture of real text was refused (" + why + "): " + text);
            }
        }

        // The cap on a whole text (what every line and note that shows outside text goes through): on 10,000 characters
        // it takes well under a millisecond on this PC and allocates nothing, in every script (.NET's own category of a
        // character past U+FFFF made a string of it each time: 400 KB a call for a text of emoji). A text box's guard
        // looks only at the letters around each change, so a keystroke costs about the same in 100,000 characters as in
        // 10,000 (it went through the whole text at every change: 3.6 ms of a 4.15 ms keystroke at 100,900 characters,
        // and 2.9 ms in 100,000 of Hindi now). The bounds on time are loose, for a busy PC.
        static void GuardOnLongText()
        {
            AppDomain.MonitoringIsEnabled = true;
            foreach (string sample in new[] { "Scan this code to see our menu. ", "扫描此代码查看我们的菜单。", "अधिक जानकारी के लिए स्कैन करें। ", "I ❤️ NY \U0001F469‍\U0001F4BB \U0001F44D\U0001F3FD " })
            {
                string text = Repeated(sample, 10000);
                Unicode.Capped(text, CaptionFont.MostMarks);
                long before = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) Check(ReferenceEquals(Unicode.Capped(text, CaptionFont.MostMarks), text), "10,000 characters with no long run should come back as themselves");
                double ms = watch.Elapsed.TotalMilliseconds / 100;
                long allocated = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - before;
                Check(ms < 10 && allocated < 1024 * 1024, "the cap on 10,000 characters (\"" + Short(sample) + "\") took " + ms.ToString("0.00") + " ms a call and allocated " + allocated / 100 + " bytes a call");
            }

            // a keystroke at the end of a text box's text, the best of three rounds of 100 (a busy PC slows some)
            double Keystroke(string sample, int length)
            {
                var box = new TextBox { AcceptsReturn = true, Text = Repeated(sample, length) };
                double best = double.MaxValue;
                for (int round = 0; round < 3; round++)
                {
                    var watch = Stopwatch.StartNew();
                    for (int i = 0; i < 100; i++)
                    {
                        box.Select(box.Text.Length, 0);
                        Typed(box, "x");
                    }
                    best = Math.Min(best, watch.Elapsed.TotalMilliseconds / 100);
                }
                return best;
            }
            foreach (string sample in new[] { "अधिक जानकारी के लिए स्कैन करें। ", "I ❤️ NY \U0001F469‍\U0001F4BB \U0001F44D\U0001F3FD " })
            {
                double shortText = Keystroke(sample, 10000), longText = Keystroke(sample, 100000);
                Check(longText < 3 * shortText + 0.5, "a keystroke in 100,000 characters (\"" + Short(sample) + "\") took " + longText.ToString("0.00") + " ms, and in 10,000 " +
                    shortText.ToString("0.00") + " ms: the text boxes' guard should look only at the letters around a change");
            }
        }

        // `length` characters of a sample again and again (never ending in half an emoji)
        static string Repeated(string sample, int length)
        {
            string text = string.Concat(Enumerable.Repeat(sample, length / sample.Length + 1)).Substring(0, length);
            return char.IsHighSurrogate(text[text.Length - 1]) ? text.Substring(0, text.Length - 1) + "." : text;
        }

        // The text boxes' guard, in boxes with an undo history as a window's have (and never laid out): a paste, a drop
        // or typing that would put more than 100 marks on a letter goes in with the rest left out, in one step that Ctrl+Z
        // undoes back to the text from before (never to one with too many marks), with the caret after what went in; a key
        // that would put one more on a letter at the limit does nothing; text set from code, or a deletion that puts two
        // letters' marks on one, is capped as it changes, with the caret where the change ended. The letters cut short
        // are kept track of: typing elsewhere keeps their note, deleting them ends it, and Ctrl+Z brings it back with them.
        static void GuardKeepsUndoAndCaret()
        {
            string heavy = "a" + new string((char)0x301, 1000), kept = "a" + new string((char)0x301, CaptionFont.MostMarks);
            TextBox Box(string text)
            {
                var box = new TextBox { AcceptsReturn = true };
                new Grid().Children.Add(box);
                box.ApplyTemplate();   // (its undo history comes with its template, in a parent)
                box.Text = text;
                return box;
            }
            string Said(TextBox box) => "\"" + box.Text.Replace(((char)0x301).ToString(), "") + "\" with " + box.Text.Count(c => c == (char)0x301) + " marks, the caret at " + box.CaretIndex;

            // a paste, a drop and typing in the middle of a text
            foreach (string how in new[] { "a paste", "a drop", "typing" })
            {
                TextBox box = Box("start END");
                box.Select(6, 0);
                if (how == "typing") Typed(box, heavy);
                else RealPaste(box, heavy, how == "a drop");
                Check(box.Text == "start " + kept + "END" && box.CaretIndex == 6 + kept.Length && box.SelectionLength == 0,
                    how + " of 1,000 marks on a letter in the middle should leave 100, with the caret after them: " + Said(box));
                Check(LongText.GetTrimmed(box), how + " with marks left out should be noted");
                box.Undo();
                Check(box.Text == "start END" && !LongText.GetTrimmed(box), "Ctrl+Z after " + how + " should give back the text from before it, with no note: " + Said(box));
                box.Redo();
                Check(box.Text == "start " + kept + "END" && LongText.GetTrimmed(box), "Ctrl+Y after " + how + " should bring it back capped, with its note: " + Said(box));
            }

            // a letter typed with its 100 marks, then a key that puts a 101st on it: that one's left out, the caret stays
            // after the letter, and the note says so; Ctrl+Z undoes the typing
            TextBox full = Box("start END");
            full.Select(6, 0);
            Typed(full, kept);
            Check(!LongText.GetTrimmed(full), "a letter typed with 100 marks has none left out");
            Typed(full, "́");
            Check(full.Text == "start " + kept + "END" && full.CaretIndex == 6 + kept.Length && LongText.GetTrimmed(full), "a 101st mark typed should be left out, with the note: " + Said(full));
            full.Undo();
            Check(full.Text == "start END", "Ctrl+Z should undo the typing: " + Said(full));

            // ...and a mark pasted onto it is left out the same way, nothing going in
            TextBox onto = Box("start " + kept + "END");
            onto.Select(6 + kept.Length, 0);
            RealPaste(onto, "́́");
            Check(onto.Text == "start " + kept + "END" && onto.CaretIndex == 6 + kept.Length && LongText.GetTrimmed(onto), "marks pasted onto a letter with 100 should be left out, with the note: " + Said(onto));

            // text set from code: capped as it changes, the caret where the change ended (all of it: the end)
            TextBox set = Box("start END");
            set.Text = "start " + heavy + " END";
            Check(set.Text == "start " + kept + " END" && set.CaretIndex == set.Text.Length && LongText.GetTrimmed(set), "text set with 1,000 marks on a letter: " + Said(set));
            set.Undo();
            Check(set.Text == "start END", "Ctrl+Z after text set with too many marks should give back what was there: " + Said(set));

            // a deletion that puts the marks of two lines on one letter: capped, the caret where the deletion was (after the
            // letter it's now in: a caret never goes between a letter and its marks)
            string sixty = new string((char)0x301, 60);
            TextBox joined = Box("a" + sixty + "\n" + sixty + " END");
            joined.Select(61, 1);
            joined.SelectedText = "";
            Check(joined.Text == kept + " END" && joined.CaretIndex == kept.Length && LongText.GetTrimmed(joined),
                "deleting the line break between two runs of marks should leave 100 on the letter, the caret after it: " + Said(joined));

            // the letter cut short is kept track of: typing elsewhere keeps the note; deleting it ends it; Ctrl+Z brings both back
            TextBox noted = Box("start END");
            noted.Select(6, 0);
            RealPaste(noted, heavy);
            noted.Select(0, 0);
            Typed(noted, "x");
            Check(noted.Text == "xstart " + kept + "END" && LongText.GetTrimmed(noted), "typing elsewhere shouldn't end the note while the letter cut short is there: " + Said(noted));
            noted.Select(7, kept.Length);
            noted.SelectedText = "";
            Check(noted.Text == "xstart END" && !LongText.GetTrimmed(noted), "with the letter cut short deleted, the note should go: " + Said(noted));
            noted.Undo();
            Check(noted.Text == "xstart " + kept + "END" && LongText.GetTrimmed(noted), "Ctrl+Z bringing back the letter cut short should bring back its note: " + Said(noted));
        }

        // A paste of `text` as Ctrl+V makes one, or a drop: through the box's own editor and its paste event, in one
        // change, the caret then after what went in (TextEditorCopyPaste.Paste); from a data object of its own, never the
        // clipboard, which is the owner's
        static void RealPaste(TextBox box, string text, bool drop = false)
        {
            object editor = typeof(TextBoxBase).GetProperty("TextEditor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(box);
            MethodInfo paste = typeof(TextBoxBase).Assembly.GetType("System.Windows.Documents.TextEditorCopyPaste").GetMethod("_DoPaste", BindingFlags.Static | BindingFlags.NonPublic);
            using (box.DeclareChangeBlock())
            {
                if ((bool)paste.Invoke(null, new object[] { editor, new DataObject(DataFormats.UnicodeText, text), drop }))
                    box.Select(box.SelectionStart + box.SelectionLength, 0);
            }
        }

        // text typed, as the keyboard brings it (an emoji panel or an input method's result sends several characters at once)
        static void Typed(TextBox box, string text) =>
            box.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, box, text)) { RoutedEvent = TextCompositionManager.TextInputEvent });

        // What --longtext draws, in a run of its own: outside text with 1,000 marks on a letter in every
        // place that shows it. Each place is drawn offscreen, so if any ends a program it's this run; it
        // prints "ok" when every place drew and every check held, else "FAIL <what>", and the name of each
        // place as it starts ("step: <what>"). With `only`, just the places with those words in their names.
        static int DrawLongText(string dir, string only)
        {
            SetErrorMode(0x0001 | 0x0002);   // if WPF ends this run, Windows reports nothing and shows no box
            string heavy = "a" + new string((char)0x301, 1000) + " b";   // 1,003 code points, 2,003 bytes
            string capped = "a" + new string((char)0x301, CaptionFont.MostMarks) + " b";
            // marks Windows knows and .NET Framework's older Unicode calls unassigned: a nonspacing mark of Unicode
            // 14 on a letter, and a Vedic spacing mark of Unicode 10 on a Devanagari letter
            string newer = "a" + new string((char)0x1AC1, 760) + " b";   // over 723, in 2,283 bytes
            string newerCapped = "a" + new string((char)0x1AC1, CaptionFont.MostMarks) + " b";
            string vedic = "क" + new string((char)0x1CF7, 1000) + " b";
            var problems = new List<string>();
            void Problem(string what) => problems.Add(what);
            void Try(string what, Action step)
            {
                if (only != null && !only.Split(' ').All(word => what.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)) return;
                Console.WriteLine("step: " + what);
                try { step(); }
                catch (Exception x) { Problem(what + ": " + x.GetType().Name + " " + x.Message); }
            }

            Directory.CreateDirectory(dir);
            DataFolder.Reset(Path.Combine(dir, "data"));

            // 1. a QR picture holding the heavy text, read through the Scan tab, its card laid out and
            //    drawn; Copy text gives the exact text; Make this code again makes a shorter (different)
            //    code and says so; the same for a code of the newer marks
            void Card(string what, string text, string shorter)
            {
                const int w = 820, h = 820;
                byte[] canvas = Canvas(w, h);
                PutQr(canvas, w, text, 10, 10, 4, QrLevel.L);   // level L fits 2,953 bytes
                MainWindow.ScanOutcome outcome = MainWindow.ReadPicturesNow("the dropped picture",
                    new[] { ("the dropped picture", GrayPicture(canvas, w, h)) }, null, CancellationToken.None);
                FoundCode code = outcome.Codes.FirstOrDefault(c => !c.IsProblem);
                if (code == null || code.Text != text) { Problem(what + ": the heavy code didn't read back (" + outcome.Codes.Count + " codes)"); return; }
                MainWindow scan = NewMain();
                scan.Tabs.SelectedItem = scan.ScanTab;
                scan.Found.Add(code);
                Draw(scan.Root, MainWidth, MainHeight);
                string exact = Exports.TextData(code.Text).GetData(DataFormats.UnicodeText) as string;
                if (exact != text) Problem(what + ": Copy text didn't give the exact text (" + (exact == null ? "null" : exact.Length + " chars") + ")");
                // Make this code again: the data box caps it, so a different (shorter) code, with a note
                scan.MakeAgain(code);
                Draw(scan.Root, MainWidth, MainHeight);
                if (scan.Code == null || scan.Code.Text != shorter) Problem(what + ": Make again didn't make the capped code: " + Show(scan.Code?.Text));
                if (scan.WarnLine.Visibility != Visibility.Visible || !scan.WarnLine.Text.Contains("left out"))
                    Problem(what + ": Make again showed no trim note: " + scan.WarnLine.Visibility + " \"" + Short(scan.WarnLine.Text) + "\"");
            }
            Try("the Scan tab's card", () => Card("the Scan tab's card", heavy, capped));
            Try("the Scan tab's card of newer marks", () => Card("the Scan tab's card of newer marks", newer, newerCapped));

            // 2. the heavy code read through the camera window with a pretend camera, its card laid out
            Try("the camera's card", () =>
            {
                string cameraText = "a" + new string((char)0x301, 760) + " b";   // >720 marks, small enough to fit a QR a camera reads
                var cam = new FakeCamera { Name = "Test camera", Width = 1280, Height = 1280, Code = cameraText };
                var cameras = new FakeCameras(cam);
                CameraWindow window = StartCamera(cameras);
                WaitForCamera(window, () => window.IsClosed && window.CameraLetGo, 25000);
                if (!window.IsClosed || window.Code == null) { Problem("the camera's card: the camera didn't read the heavy code (" + cam.Shown + " pictures)"); return; }
                if (window.Code.Text != cameraText) { Problem("the camera read the wrong text"); return; }
                MainWindow main = NewMain();
                main.ShowCameraCode(window);
                Draw(main.Root, MainWidth, MainHeight);
            });

            // 3. a design file with the heavy text as its caption and data, opened into the window
            Try("a design file", () =>
            {
                string path = Path.Combine(dir, DesignFile.FileName);
                var design = new QrDesign { Data = "a" + new string((char)0x301, 760) + " b", Frame = new QrFrame { Text = heavy } };   // data fits level Q
                File.WriteAllText(path, DesignFile.Write(design));
                MainWindow window = NewMain();
                window.OpenDesignFile(path);
                WaitForCheck(window);
                Draw(window.Root, MainWidth, MainHeight);
            });

            // 4. a paste into the data box, the caption box and a template's field (set as a paste or drop
            //    sets the box's content, which the guard caps on the same TextChanged, before layout), of
            //    the heavy text, then of the newer marks
            void Pasted(string text)
            {
                MainWindow window = NewMain();
                window.Tabs.SelectedItem = window.MakeTab;
                window.FrameCheck.IsChecked = true;
                Paste(window.DataBox, text);
                Paste(window.CaptionBox, text);
                WaitForCheck(window);
                Draw(window.Root, MainWidth, MainHeight);
                TemplateWindow template = window.TemplateFor(TemplateKind.Text);
                if (template.Input("text") is TextBox field) Paste(field, text);
                Draw((FrameworkElement)template.Content, 600, 680);
            }
            Try("a paste", () => Pasted(heavy));
            Try("a paste of newer marks", () => Pasted(newer));

            // 5. Recent codes with such an entry, its window laid out
            Try("Recent codes", () =>
            {
                var kept = new QrDesign { Data = "a" + new string((char)0x301, 760) + " b", Frame = new QrFrame { Text = heavy } };
                RecentCodes.Add(RecentCodes.New(kept, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                var window = new RecentWindow(new Settings(), id => false);
                Draw((FrameworkElement)window.Content, 600, 660);
            });

            // 6. the barcode data box with a Data Matrix of the heavy text, laid out, with the note
            Try("the barcode data box", () =>
            {
                MainWindow window = NewMain();
                window.ChooseCodeType(BarcodeType.DataMatrix, heavy);
                WaitForCheck(window);
                Draw(window.Root, MainWidth, MainHeight);
                if (window.BarcodeExpected != capped) Problem("the barcode didn't encode the capped text: " + Show(window.BarcodeExpected));
                if (window.WarnLine.Visibility != Visibility.Visible || !window.WarnLine.Text.Contains("left out"))
                    Problem("the barcode data box showed no trim note: " + window.WarnLine.Visibility);
            });

            // 7. 200 letters each with 100 marks in a text box and a text block, laid out and drawn: the
            //    cap (MostMarks per letter) stays safe when it's repeated down a whole line, of the newer
            //    marks too
            Try("200 capped letters", () =>
            {
                foreach (char mark in new[] { (char)0x301, (char)0x1AC1 })
                {
                    string many = string.Concat(Enumerable.Repeat("a" + new string(mark, CaptionFont.MostMarks), 200));
                    var stack = new StackPanel { Width = 560 };
                    stack.Children.Add(new TextBox { Text = many, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true });
                    stack.Children.Add(new TextBlock { Text = many, TextWrapping = TextWrapping.Wrap });
                    var root = new Grid { Width = 600, Background = Brushes.White };
                    root.Children.Add(stack);
                    Draw(root, 600, double.PositiveInfinity);
                }
            });

            // 8. an SVG picture whose text has the newer marks or the Vedic sign on a letter: refused (as one
            //    with 1,000 accents is) before WPF lays it out, by the same count of marks
            void Refused(string what, string text)
            {
                ImageSource drawn = Images.Drawing(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><text y=\"20\">" + text + "</text></svg>"), null, out string why);
                if (drawn != null || why?.Contains("more than 64 accents or other marks on one letter") != true) Problem(what + ": not refused (" + Show(why) + ")");
            }
            Try("an SVG picture of newer marks", () => Refused("an SVG picture of newer marks", "a" + new string((char)0x1AC1, 1000) + " b"));
            Try("an SVG picture of a Vedic sign", () => Refused("an SVG picture of a Vedic sign", vedic));

            // From here, each window is also walked once it's laid out and drawn: every text in it must be as the cap leaves
            // it (Uncapped), so a place showing outside text uncapped fails even where its text is too short to end the program.
            // An SVG drawing whose reader refuses it with a name from the file (an attribute there twice, called a and 1,000
            // accents), as a data: URL too
            string named = "a" + new string((char)0x301, 1000);
            string twice = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><g " + named + "=\"1\" " + named + "=\"2\"/></svg>";
            string twiceUrl = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(twice));

            // 9. a link whose tracking parameter's name is a and 1,000 accents, written as percent escapes (the data box
            //    holds plain ASCII, so it caps nothing): the warning under the box names it
            Try("a link's tracking parameters", () =>
            {
                MainWindow window = NewMain();
                window.Tabs.SelectedItem = window.MakeTab;
                Paste(window.DataBox, "https://b0r3d.org/?utm_a" + string.Concat(Enumerable.Repeat("%CC%81", 1000)) + "=1");
                window.Refresh();
                Draw(window.Root, MainWidth, MainHeight);
                if (window.DataChecksPanel.Children.Count == 0) Problem("a link's tracking parameters: no warning under the data box");
                Uncapped("the Make tab with a link's tracking parameters", window, problems);
            });

            // 10. that SVG pasted as the logo: the bottom line says why it can't be used, in the reader's words
            Try("an SVG logo pasted with a name the reader quotes", () =>
            {
                MainWindow window = NewMain();
                window.Tabs.SelectedItem = window.MakeTab;
                Paste(window.DataBox, "https://b0r3d.org");
                window.Refresh();
                if (!window.LogoFromData(new DataObject(DataFormats.UnicodeText, twice)) || !window.Note.Text.Contains("there twice")) Problem("an SVG logo pasted: no word of why it can't be used: \"" + Short(window.Note.Text) + "\"");
                Draw(window.Root, MainWidth, MainHeight);
                Uncapped("the Make tab after an SVG logo was refused", window, problems);
            });

            // 11. a design file with outside text in all it shows: its data and caption, a caption font that no list has,
            //     colours that aren't colours, and that SVG as its logo and frame picture (said in the warnings, the logo's
            //     note and the frame's), every section open
            Try("a design file with outside text everywhere", () =>
            {
                string path = Path.Combine(dir, DesignFile.FileName);
                var design = new QrDesign
                {
                    Data = "a" + new string((char)0x301, 760) + " b", DotsColor = heavy, CornersSquareColor = heavy, CornersDotColor = heavy, Background = heavy,
                    DotsType = heavy, Logo = twiceUrl,
                    Frame = new QrFrame { Text = heavy, FontFamily = heavy, BackgroundImage = twiceUrl },
                };
                File.WriteAllText(path, DesignFile.Write(design));
                MainWindow window = NewMain();
                window.OpenDesignFile(path);
                foreach (Expander section in new[] { window.DataSection, window.StyleSection, window.LogoSection, window.FrameSection, window.SizeSection, window.InCodeSection }) section.IsExpanded = true;
                WaitForCheck(window);
                Draw(window.Root, MainWidth, MainHeight);
                if (!window.WarnLine.Text.Contains("there twice")) Problem("a design file: the warnings don't say why the logo and frame picture aren't drawn: \"" + Short(window.WarnLine.Text) + "\"");
                Uncapped("the Make tab with a design file of outside text", window, problems);
                // ...and that code printed on a sheet of copies, each with its caption (what Recent codes calls it)
                PrintWindow print = window.PrintWindowFor();
                print.PaperOverride = Printing.PrintSheet.A4;
                print.SheetChoice.IsChecked = true;
                LayOut(print.Root, 760, 720);
                print.Lay();
                Draw(print.Root, 760, 720);
                Uncapped("Print with a code of outside text", print, problems);
            });

            // 12. a folder given with --data that can't be used, quoted in why settings aren't kept: the bottom line, About
            //     and Recent codes say it
            Try("a --data folder that can't be used", () =>
            {
                DataFolder.Choose("\"" + heavy + "|\"", dir);   // (| is never in a folder's name)
                try
                {
                    if (DataFolder.WhyNotKept?.Contains(heavy) != true)
                    {
                        Problem("a --data folder that can't be used: it isn't quoted in why settings aren't kept: \"" + Short(DataFolder.WhyNotKept) + "\"");
                        return;
                    }
                    MainWindow window = NewMain();
                    Draw(window.Root, MainWidth, MainHeight);
                    Uncapped("the main window with settings not kept", window, problems);
                    var about = new AboutWindow();
                    Draw(about.Root, 640, double.PositiveInfinity);
                    Uncapped("About", about, problems);
                    var recent = new RecentWindow(new Settings(), id => false);
                    Draw((FrameworkElement)recent.Content, 600, 660);
                    Uncapped("Recent codes with settings not kept", recent, problems);
                }
                finally
                {
                    DataFolder.Reset(Path.Combine(dir, "data"));
                }
            });

            // 13. Recent codes from a file whose entry names itself a and 1,000 accents (the list shows a code by the name
            //     its file gives), laid out; then that code deleted, which the line under the list says by name
            Try("Recent codes with an entry of outside text", () =>
            {
                var entry = new Dictionary<string, object>
                {
                    ["id"] = 1, ["key"] = "qr:qr:https://b0r3d.org", ["label"] = heavy, ["title"] = heavy, ["sensitive"] = false, ["omitted"] = new List<object>(),
                    ["savedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["design"] = Json.Parse(DesignFile.Write(new QrDesign { Data = "https://b0r3d.org" })),
                };
                Directory.CreateDirectory(DataFolder.Path);
                File.WriteAllText(RecentCodes.Path, Json.Write(new Dictionary<string, object> { ["v"] = 1, ["next"] = 2, ["codes"] = new List<object> { entry } }), Encoding.UTF8);
                try
                {
                    var window = new RecentWindow(new Settings(), id => false);
                    if (window.Codes.Count != 1) Problem("Recent codes: the entry wasn't listed (" + window.Codes.Count + " codes)");
                    Draw((FrameworkElement)window.Content, 600, 660);
                    Uncapped("Recent codes", window, problems);
                    if (window.Codes.Count == 1) window.Delete(window.Codes[0]);
                    Draw((FrameworkElement)window.Content, 600, 660);
                    Uncapped("Recent codes with the entry deleted", window, problems);
                }
                finally
                {
                    File.Delete(RecentCodes.Path);
                }
            });

            // 14. two cameras that call themselves 1,000 marks long: the list of them, the first one's trouble in Windows'
            //     words (with its name), then the second chosen, which reads a code: the Scan tab says where it came from
            Try("cameras with names of outside text", () =>
            {
                string first = "USB " + heavy, second = "Rear " + heavy;
                var one = new FakeCamera { Name = first, OpenTrouble = Webcams.Problem(unchecked((int)0x80070005), "Windows didn't start " + first) };
                var two = new FakeCamera { Name = second, Code = "https://b0r3d.org" };
                CameraWindow window = StartCamera(new FakeCameras(one, two));
                WaitForCamera(window, () => window.Problem != null, 15000);
                if (window.Problem == null) { Problem("cameras: the first camera's trouble wasn't shown"); return; }
                Draw(window.Root, 720, 620);
                Uncapped("the camera window with a camera's trouble", window, problems);
                window.CameraBox.SelectedIndex = 1;
                WaitForCamera(window, () => window.IsClosed && window.CameraLetGo, 25000);
                if (window.Code == null) { Problem("cameras: the second camera didn't read its code (" + two.Shown + " pictures)"); return; }
                MainWindow main = NewMain();
                main.ShowCameraCode(window);
                Draw(main.Root, MainWidth, MainHeight);
                Uncapped("the Scan tab with a code from a camera", main, problems);
            });

            // 15. a picture and a folder named with a and 101 accents (a name holds 255 characters: never enough to end the
            //     program, but over the cap), read through the Scan tab: its line and the cards name them
            Try("a picture and a folder with names of outside text", () =>
            {
                string picture = Path.Combine(dir, "b" + new string((char)0x301, 101) + ".png"), folder = Path.Combine(dir, "a" + new string((char)0x301, 101));
                const int w = 300, h = 300;
                byte[] canvas = Canvas(w, h);
                PutQr(canvas, w, "https://b0r3d.org", 40, 40, 6);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(GrayPicture(canvas, w, h)));
                using (var file = File.Create(picture)) png.Save(file);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "x.png"), "not a picture");
                foreach (string read in new[] { picture, folder })
                {
                    MainWindow window = NewMain();
                    window.ReadFiles(new[] { read });
                    Pump(() => !window.Scanning, 15000);
                    if (window.Found.Count == 0) Problem("the Scan tab listed nothing for " + Path.GetFileName(read).Length + " characters' name");
                    Draw(window.Root, MainWidth, MainHeight);
                    Uncapped("the Scan tab after reading a " + (read == folder ? "folder" : "picture") + " of outside name", window, problems);
                }
            });

            // 16. a batch CSV named with a and 101 accents, with a row whose caption, file name and font are 290 accents (shown
            //    cut to 297 characters) and a row too long to make: the rows' list, that row, and the row too long
            Try("a batch CSV of outside text", () =>
            {
                string csv = Path.Combine(dir, "c" + new string((char)0x301, 101) + ".csv"), cell = "a" + new string((char)0x301, 290);
                File.WriteAllText(csv, "data,frameText,fileName,frameFontFamily\n" + cell + "," + cell + "," + cell + "," + cell + "\n" + "a" + new string((char)0x301, 2000) + ",x,y,z\n", Encoding.UTF8);
                MainWindow main = NewMain();
                BatchWindow batch = main.OpenBatch(csv, false);
                Pump(() => batch.Items.Count == 2, 15000);
                if (batch.Items.Count != 2) { Problem("a batch CSV: its rows weren't read (" + batch.Items.Count + ")"); return; }
                batch.FrameCheck.IsChecked = true;
                Draw((FrameworkElement)batch.Content, 660, 760);
                Uncapped("Batch export", batch, problems);
                batch.ShowRow(1);
                Draw((FrameworkElement)batch.Content, 660, 760);
                Uncapped("Batch export on the row too long", batch, problems);
                // ...and the row that can be made printed on a sheet, with its file name under it
                PrintWindow print = batch.PrintWindowFor();
                if (print == null) { Problem("a batch CSV: nothing to print"); return; }
                print.PaperOverride = Printing.PrintSheet.A4;
                LayOut(print.Root, 760, 720);
                print.Lay();
                Draw(print.Root, 760, 720);
                Uncapped("Print with a batch of outside text", print, problems);
            });

            // 17. a template's form filled from a text whose Wi-Fi network is a and 1,000 accents: the box keeps 100 with the
            //     note under it, its value is what it shows, and Save's text goes to the data box with the Make tab's note
            Try("a template form filled from outside text", () =>
            {
                var template = new TemplateWindow(TemplateKind.Wifi, "WIFI:S:" + heavy + ";T:WPA;P:secret;;", new Dictionary<TemplateKind, Dictionary<string, string>>());
                Draw((FrameworkElement)template.Content, 600, 680);
                Uncapped("a template's form", template, problems);
                if (!template.CutNoted("ssid")) Problem("a template's form: no note under the network's name that its marks were left out");
                if (template.Values["ssid"] != (template.Input("ssid") as TextBox)?.Text) Problem("a template's form: the network's name isn't what its box shows");
                if (!template.Save()) { Problem("a template's form: Save didn't take it"); return; }
                MainWindow window = NewMain();
                window.UseTemplateText(template.Result, template.Shortened);
                Draw(window.Root, MainWidth, MainHeight);
                if (!window.WarnLine.Text.Contains(LongText.DataNote)) Problem("a template's text with a field's marks left out: no note on the Make tab: \"" + Short(window.WarnLine.Text) + "\"");
                Uncapped("the Make tab with a template's text", window, problems);
            });

            // 18. the data box's note stays while its code holds less than what was pasted: through typing elsewhere,
            //    until the letter cut short is deleted
            Try("the data box's note while its code holds less", () =>
            {
                MainWindow window = NewMain();
                window.Tabs.SelectedItem = window.MakeTab;
                window.DataBox.Text = "start END";
                window.DataBox.Select(6, 0);
                RealPaste(window.DataBox, heavy);
                window.DataBox.Select(0, 0);
                Typed(window.DataBox, "x");
                window.Refresh();
                Draw(window.Root, MainWidth, MainHeight);
                if (!window.WarnLine.Text.Contains(LongText.DataNote)) Problem("the data box: the note went with typing elsewhere, the code still short: \"" + Short(window.WarnLine.Text) + "\"");
                int at = window.DataBox.Text.IndexOf((char)0x301) - 1;   // the letter cut short
                window.DataBox.Select(at, CaptionFont.MostMarks + 1);
                window.DataBox.SelectedText = "";
                window.Refresh();
                if (window.WarnLine.Text.Contains(LongText.DataNote)) Problem("the data box: the note stayed with the letter cut short deleted");
            });

            if (problems.Count == 0) Console.WriteLine("ok");
            else foreach (string p in problems) Console.WriteLine("FAIL " + p);
            TryDelete(dir);
            return problems.Count == 0 ? 0 : 1;
        }

        // lays a window's content out and draws it offscreen, as the screen would (the layout, not the
        // draw, is where WPF would end the program)
        static void Draw(FrameworkElement root, double width, double height)
        {
            Size size = LayOut(root, width, height);
            var picture = new RenderTargetBitmap(Math.Max(1, (int)size.Width), Math.Max(1, Math.Min(4000, (int)size.Height)), 96, 96, PixelFormats.Pbgra32);
            picture.Render(root);
        }

        // puts text into a box as a paste or a drop does: the content changes, which the guard caps on the
        // TextChanged before the box is laid out (a real paste and drop go through the same event)
        static void Paste(TextBox box, string text)
        {
            box.Focus();
            box.SelectAll();
            box.SelectedText = text;
        }

        // Every text a window shows, found in its visual and logical trees once it's laid out: text blocks and their runs,
        // text boxes, labels and buttons, headers, tool tips, the items of a list that shows them as their text, the window's
        // title. Each must be as the cap leaves it (Unicode.Capped gives it back as itself), or the place it's in shows
        // outside text uncapped: a problem, whether or not its text was long enough to end the program.
        static void Uncapped(string where, Window window, List<string> problems)
        {
            var seen = new HashSet<object>();
            void Shows(object at, string what, string text)
            {
                if (text == null || ReferenceEquals(LongText.Cap(text), text)) return;
                string problem = where + ": " + Named(at) + what + " shows a letter with more than " + CaptionFont.MostMarks + " marks on it (" + text.Length + " characters)";
                if (!problems.Contains(problem)) problems.Add(problem);   // (a text block's text is its logical child too)
            }
            void Visit(object node, object parent)
            {
                if (node == null || !seen.Add(node)) return;
                if (node is string text)
                {
                    Shows(parent, "", text);
                    return;
                }
                if (!(node is DependencyObject d)) return;
                if (d is Window w) Shows(w, "'s title", w.Title);
                if (d is TextBlock block) Shows(block, "", block.Text);
                if (d is TextBox box) Shows(box, "", box.Text);
                if (d is AccessText access) Shows(access, "", access.Text);
                if (d is System.Windows.Documents.Run run) Shows(run, "", run.Text);
                if (d is FrameworkElement element)
                {
                    if (element.ToolTip is string tip) Shows(element, "'s tool tip", tip);
                    else if (element.ToolTip is ToolTip inTip && inTip.Content is string tipText) Shows(element, "'s tool tip", tipText);
                }
                if (d is HeaderedContentControl headed && headed.Header is string header) Shows(headed, "'s header", header);
                if (d is HeaderedItemsControl menu && menu.Header is string menuHeader) Shows(menu, "'s header", menuHeader);
                // a list that shows its items as their text (one with a template of its own has them in its visual tree)
                if (d is ItemsControl list && list.ItemTemplate == null && list.ItemTemplateSelector == null && string.IsNullOrEmpty(list.DisplayMemberPath))
                    foreach (object item in list.Items)
                    {
                        if (item is DependencyObject) Visit(item, list);
                        else if (item != null) Shows(list, "'s item", item.ToString());
                    }
                foreach (object child in LogicalTreeHelper.GetChildren(d)) Visit(child, d);
                if (d is Visual)
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Visit(VisualTreeHelper.GetChild(d, i), d);
            }
            Visit(window, null);
        }

        static string Named(object element) =>
            element is FrameworkElement f && f.Name.Length > 0 ? f.Name : element is FrameworkContentElement c && c.Name.Length > 0 ? c.Name : element?.GetType().Name ?? "something";
    }
}
