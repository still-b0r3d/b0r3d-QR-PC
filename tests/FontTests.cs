using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Batch;
using B0r3dQR.Design;
using B0r3dQR.Qr;

namespace B0r3dQR.Tests
{
    // The website's caption fonts, built into b0r3d QR: drawn and measured as Chromium does on the website's page.
    static partial class TestMain
    {
        // a caption measured as the website's page measures it: its font, or the page's own (css "")
        static double CaptionWidth(string css, string text)
        {
            CaptionFont font = css.Length > 0 ? CaptionFont.For(css) : CaptionFont.Page;
            return font.Text(text, QrArt.CaptionSize, Brushes.Black, CultureInfo.InvariantCulture).WidthIncludingTrailingWhitespace;
        }

        // Where WPF's own text shaping does what Chromium's (HarfBuzz) doesn't, so the width can't match: the caption,
        // b0r3d QR's width less Chromium's, and why. Each has to stay as it is, and every other width within 0.5 px.
        static readonly (string Css, string Text, double Gap, string Why)[] ShapingGaps =
        {
            ("'Merriweather', serif", "Wi-Fi: b0r3d_guest 0123456789 & more!", 2.763, "WPF never kerns a letter with a hyphen-minus (Merriweather kerns i- and -F)"),
            ("'Merriweather', serif", "Wi-Fi for guests", 2.767, "the same: Merriweather's i- and -F"),
            ("'Pacifico', cursive", "Съешь же ещё этих мягких французских булок", 0.56, "WPF uses no kerning in Cyrillic from a file with none under cyrl (Pacifico's is under DFLT, which HarfBuzz falls back to)"),
            ("'Pacifico', cursive", "Чуєш їх, доцю, га? Кумедна ж ти", 0.4915, "the same: Pacifico's Cyrillic kerning"),
            ("'Poppins', sans-serif", "a᫁b", 8.997, "a mark no font has is drawn as the font's empty box, which HarfBuzz gives no width in a font without glyph classes like Poppins (the box is classed as the mark it stands for) and WPF draws at its width"),
            ("'Poppins', sans-serif", "á᫁b", 8.997, "the same: Poppins's empty box for a mark"),
            ("'Poppins', sans-serif", "a᪰b", 8.997, "the same: Poppins's empty box for a mark"),
            ("'Poppins', sans-serif", "Scan me a᫁ now", 9.001, "the same: Poppins's empty box for a mark"),
        };

        static void CaptionWidthsLikeChromium()
        {
            var wrong = new List<string>();
            int close = 0;
            double worst = 0;
            string worstCase = "";
            foreach (var (css, text, width) in WebsiteFontWidths.Widths)
            {
                double gap = CaptionWidth(css, text) - width;
                string name = (css.Length > 0 ? css : "the page's font") + " \"" + Short(text) + "\"";
                var known = ShapingGaps.FirstOrDefault(g => g.Css == css && g.Text == text);
                if (known.Text != null)
                {
                    if (Math.Abs(gap - known.Gap) > 0.01) wrong.Add(name + ": " + gap.ToString("0.###", CultureInfo.InvariantCulture) + " px from Chromium's, where " + known.Gap + " was known (" + known.Why + ")");
                    continue;
                }
                if (Math.Abs(gap) <= 0.01) close++;
                if (Math.Abs(gap) > worst)
                {
                    worst = Math.Abs(gap);
                    worstCase = name;
                }
                if (Math.Abs(gap) > 0.5) wrong.Add(name + ": " + gap.ToString("0.###", CultureInfo.InvariantCulture) + " px from Chromium's " + width.ToString("0.####", CultureInfo.InvariantCulture));
            }
            var lines = new List<string>();
            foreach (var (css, text, column, side, count) in WebsiteFontWidths.Lines)
            {
                int ours = QrArt.PreviewLines(text, css, column);
                if (ours != count) lines.Add((css.Length > 0 ? css : "the page's font") + " \"" + Short(text) + "\" in " + column + " px: " + ours + " lines, Chromium " + count);
            }
            Console.WriteLine("      " + close + " of " + (WebsiteFontWidths.Widths.Length - ShapingGaps.Length) + " widths within 0.01 px of Chromium's; the furthest " + worst.ToString("0.####", CultureInfo.InvariantCulture) +
                              " px (" + worstCase + "); " + ShapingGaps.Length + " known gaps from WPF's own shaping");
            foreach (string w in wrong.Take(40)) Console.WriteLine("      " + w);
            foreach (string l in lines.Take(40)) Console.WriteLine("      " + l);
            Check(wrong.Count == 0 && lines.Count == 0, wrong.Count + " of " + WebsiteFontWidths.Widths.Length + " widths and " + lines.Count + " of " + WebsiteFontWidths.Lines.Length + " layouts differ from Chromium's");
        }

        // the font files a drawing's text comes from, by file name (the caption's glyphs; the code is shapes)
        static List<string> FontFilesIn(Drawing drawing)
        {
            var files = new List<string>();
            void Walk(Drawing d)
            {
                if (d is DrawingGroup group) foreach (Drawing child in group.Children) Walk(child);
                else if (d is GlyphRunDrawing glyphs) files.Add(Path.GetFileName(glyphs.GlyphRun.GlyphTypeface.FontUri.AbsolutePath).ToLowerInvariant());
            }
            Walk(drawing);
            return files.Distinct().OrderBy(f => f, StringComparer.Ordinal).ToList();
        }

        static readonly string[] WebsiteWebFonts = { "Roboto", "Inter", "Open Sans", "Lato", "Montserrat", "Poppins", "Oswald", "Raleway", "Nunito", "Playfair Display",
                                                      "Merriweather", "JetBrains Mono", "Fira Code", "Source Code Pro", "IBM Plex Mono", "Inconsolata", "Pacifico", "Bebas Neue" };

        // Every face of the 18 fonts is a file in the exe's own resources that WPF opens and finds the font in (not an
        // installed font of that name), with letters of its own range; each font measures apart from its fallback; and
        // the website's font comes before an installed font of the same name, as a page's @font-face does.
        static void WebFontsLoadFromTheExe()
        {
            Check(WebFonts.All.Select(f => f.Name).SequenceEqual(WebsiteWebFonts), "the fonts: " + string.Join(", ", WebFonts.All.Select(f => f.Name)));
            Check(WebsiteWebFonts.All(n => MainWindow.WebsiteFonts.Any(w => w.Label == n && w.Value.StartsWith("'" + n + "', "))), "the font list should name each web font as the website does");
            XmlLanguage english = XmlLanguage.GetLanguage("en-us");
            var problems = new List<string>();
            int faces = 0;
            foreach (WebFont font in WebFonts.All)
                foreach (WebFontFace face in font.Faces)
                {
                    faces++;
                    if (!new Typeface(face.Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out GlyphTypeface glyphs))
                    {
                        problems.Add(face.File + " doesn't open");
                        continue;
                    }
                    string uri = glyphs.FontUri.ToString();
                    if (glyphs.FontUri.Scheme != "pack" || !uri.EndsWith("/fonts/" + face.File, StringComparison.OrdinalIgnoreCase)) problems.Add(face.File + " opened " + uri);
                    // the name Windows gives the file, which WPF finds it by (Merriweather's has its STAT table's "18pt")
                    if (!glyphs.Win32FamilyNames.TryGetValue(english.GetEquivalentCulture(), out string family) || family != face.Inside || !family.StartsWith(font.Name))
                        problems.Add(face.File + " is " + family);
                    if (!face.Characters.Any(glyphs.CharacterToGlyphMap.ContainsKey)) problems.Add(face.File + " has nothing in its range");
                }
            Check(problems.Count == 0, string.Join("; ", problems.Take(10)));
            Check(faces == 95 && Directory.GetFiles(Path.Combine(RepoFolder, "assets", "fonts"), "*.ttf").Length == 95, faces + " faces");

            string[] samples = { "Scan for more info", "The quick brown fox jumps over the lazy dog", "Wi-Fi: b0r3d_guest 0123456789 & more!" };
            foreach (var (label, value) in MainWindow.WebsiteFonts.Where(w => WebsiteWebFonts.Contains(w.Label)))
            {
                string fallback = value.Split(',').Last().Trim();
                double apart = samples.Sum(s => Math.Abs(CaptionWidth(value, s) - CaptionWidth(fallback, s)));
                Check(apart > 5, label + " measures like " + fallback + " (" + apart.ToString("0.##", CultureInfo.InvariantCulture) + " px apart in all)");
            }

            Func<string, bool> installed = QrArt.Installed;
            QrArt.Installed = name => true;   // as if every font were installed too
            try
            {
                foreach (string name in WebsiteWebFonts)
                {
                    CaptionFont font = CaptionFont.For("'" + name + "', sans-serif");
                    Check(font.Faces[0] is WebFontFace face && face.Font.Name == name, name + " should come from the exe before an installed " + name);
                }
                Check(CaptionFont.Page.Faces[0] is PcFace, "the page's own font isn't the Inter web font (the website doesn't load it for the page)");
            }
            finally
            {
                QrArt.Installed = installed;
            }
            Check(CaptionFont.Page.Faces[0].Family.Source == (QrArt.Installed("Inter") ? "Inter" : "Segoe UI"), "the page's font: " + CaptionFont.Page.Faces[0].Family.Source);
        }

        // Which face draws a character, as Chromium picks it: by the faces' ranges, the last @font-face first; a face
        // whose range names a character its file hasn't got passes it on; a letter split from its accent where the face
        // has both (HarfBuzz), and a face asked only when its range has one of the characters still to draw.
        static void CharactersFromTheBrowsersFace()
        {
            string Face(string css, string text, out string spelt)
            {
                var picked = CaptionFont.For(css).Pick(text);
                Check(picked.Count == 1, "\"" + text + "\" should be one character");
                spelt = picked[0].Spelt;
                return picked[0].Face is WebFontFace web ? web.Subset : picked[0].Face.Family.Source;
            }
            string Points(string s) => string.Join(" ", Unicode.Points(s).Select(c => "U+" + c.ToString("X4")));
            const string roboto = "'Roboto', sans-serif";
            foreach (var (text, subset) in new[] { ("A", "latin"),     // also in Roboto's math and symbols files, but not in their ranges
                                                    ("Я", "cyrillic"), ("Ѣ", "cyrillic-ext"), ("ά", "greek"), ("Ὅ", "greek-ext"), ("ế", "vietnamese"), ("ł", "latin-ext"),
                                                    ("α", "math"),         // in greek's range and file and in math's: the later @font-face
                                                    ("₫", "latin-ext"),    // vietnamese's and latin-ext's: the later
                                                    ("◊", "symbols"),      // math's and symbols': the later
                                                    ("−", "latin"),        // math's and latin's: the later
                                                    ("ā", "latin-ext"),    // Roboto's latin file has no macron to put on an a
                                                    ("ἀ", "Arial") })      // in greek-ext's range, but its file has only Ὅ of U+1F00-1FFF: the next font
            {
                string got = Face(roboto, text, out string spelt);
                Check(got == subset && spelt == text, "Roboto's " + Points(text) + " should come from its " + subset + " face, not " + got + " (" + Points(spelt) + ")");
            }
            // Montserrat's latin face has the macron (U+0304, in its range), so HarfBuzz draws the ā of "rā" there as a and
            // the macron, kerned with the r; a lone ā never reaches the latin face (its range hasn't got ā): latin-ext's ā
            var macron = CaptionFont.For("'Montserrat', sans-serif").Pick("rā");
            Check(((WebFontFace)macron[1].Face).Subset == "latin" && macron[1].Spelt == "a" + (char)0x304, "Montserrat's rā should be latin's a and macron: " + Points(macron[1].Spelt));
            Check(Face("'Montserrat', sans-serif", "ā", out string alone) == "latin-ext" && alone == "ā", "a lone ā in Montserrat should be latin-ext's");
            // a lone ǖ never reaches Bebas Neue's latin face (its range hasn't got it), and its latin-ext face has no glyph
            // for it nor for ü: the next font, Arial, draws it, as in Chromium
            Check(Face("'Bebas Neue', sans-serif", "ǖ", out _) == "Arial", "Bebas Neue's ǖ should come from Arial");
            // an e and an acute typed apart: Roboto's latin face has é, so the two are drawn as é
            Check(Face(roboto, "e" + (char)0x301, out string joined) == "latin" && joined == "é", "Roboto's e and acute: " + Points(joined));
            // a letter with a mark no font has (U+1AC1, a mark since Unicode 14; U+1AB0, older), where the last font has the
            // letter too: from the first face that has the letter, which draws the mark as its empty box, as Chromium does
            // (the acute still joined to the a); a letter only Windows finds a font for (क): to the last font as before, for
            // Windows (Chromium draws the two as the first font's boxes; WPF finds क a font of its own whatever it's given)
            foreach (string mark in new[] { "᫁", "᪰", "́᫁" })
            {
                Check(Face(roboto, "a" + mark, out string boxed) == "latin" && boxed == (mark.Length == 1 ? "a" + mark : "á᫁"),
                    "Roboto's a and " + Points(mark) + " should come from its latin face, the mark as its box: " + Points(boxed));
            }
            Check(Face(roboto, "ж᫁", out _) == "cyrillic", "Roboto's ж and U+1AC1 should come from its cyrillic face");
            Check(Face(roboto, "क᫁", out string windows) == "Times New Roman" && windows == "क᫁", "क and U+1AC1 should go to Times New Roman, for Windows");

            // a Cyrillic word in Montserrat with a space: the space from the latin face, the letters from the cyrillic one
            var words = CaptionFont.For("'Montserrat', sans-serif").Pick("Привет мир");
            string faces = string.Join(" ", words.Select(p => ((WebFontFace)p.Face).Subset));
            Check(faces == "cyrillic cyrillic cyrillic cyrillic cyrillic cyrillic latin cyrillic cyrillic cyrillic", "Привет мир: " + faces);
        }

        // CSS's generic families as Chromium has them on Windows (monospace is Consolas, not Courier New), installed
        // fonts by name, and a font the website doesn't ship falling back as before (Arial when nothing in the list is
        // there); the default caption measured in the page's font and drawn in Arial, as the website's files name it
        static void GenericFontsAndOthers()
        {
            foreach (var (css, family) in new[] { ("monospace", "Consolas"), ("sans-serif", "Arial"), ("serif", "Times New Roman"), ("cursive", "Comic Sans MS"), ("system-ui", "Segoe UI"),
                                                  ("'Courier New', monospace", "Courier New"), ("Verdana, sans-serif", "Verdana"), ("'No Such Font', sans-serif", "Arial"),
                                                  ("'No Such Font', monospace", "Consolas"), ("'No Such Font'", "Arial"), ("", "Arial"), (null, "Arial") })
            {
                CaptionFont font = CaptionFont.For(css);
                Check(font.Faces[0] is PcFace && font.Faces[0].Family.Source == family, (css ?? "no font") + " should be " + family + ", not " + font.Faces[0].Family.Source);
                Check(font.Faces.Last().Family.Source == "Times New Roman", (css ?? "no font") + ": Chromium tries Times New Roman, its standard font, after the list");
            }
            double monospace = WebsiteFontWidths.Widths.Single(w => w.Css == "monospace" && w.Text == "Scan for more info").Width;
            Check(Math.Abs(CaptionWidth("monospace", "Scan for more info") - monospace) < 0.01, "monospace should measure as Consolas does in Chromium (" + monospace + ")");

            foreach (var (css, file) in new[] { ("'No Such Font', sans-serif", "arial.ttf"), (null, "arial.ttf"), ("monospace", "consola.ttf"), ("'Courier New', monospace", "cour.ttf") })
            {
                var design = new QrDesign { Data = "https://b0r3d.org", Frame = new QrFrame { Text = "Scan me", FontFamily = css } };
                List<string> drawn = FontFilesIn(new QrArt(design).ToDrawing());
                Check(drawn.SequenceEqual(new[] { file }), (css ?? "no font") + " drew its caption from " + string.Join(", ", drawn));
            }
        }

        // A framed design in Pacifico draws Pacifico's own glyphs, from the exe, in the drawing that the preview, PNG,
        // JPG, PDF, copy, drag, print and Recent codes all come from, and its PNG isn't the Comic Sans one and scans;
        // its SVG names the font as the website writes it. A batch's frameFontFamily column uses the fonts too.
        static void WebFontsInTheFiles()
        {
            QrArt Framed(string css) => new QrArt(new QrDesign { Data = "https://b0r3d.org", Frame = new QrFrame { Text = "Scan me", FontFamily = css } });
            QrArt pacifico = Framed("'Pacifico', cursive"), comic = Framed("cursive");
            List<string> drawn = FontFilesIn(pacifico.ToDrawing());
            Check(drawn.SequenceEqual(new[] { "pacifico-latin-400-normal.ttf" }), "Pacifico's caption came from " + string.Join(", ", drawn));
            Check(FontFilesIn(comic.ToDrawing()).SequenceEqual(new[] { "comic.ttf" }), "cursive should be Comic Sans MS");
            Check(pacifico.ToSvg().Contains("font-family=\"'Pacifico', cursive\""), "the SVG should name the font as the website writes it");

            BitmapSource Png(QrArt art) => BitmapDecoder.Create(new MemoryStream(QrExport.Png(QrExport.Picture(art, new PrintSize(), false))), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            BitmapSource inPacifico = Png(pacifico), inComic = Png(comic);
            byte[] a = ScanCheck.Grays(inPacifico, out int w, out int h), b = ScanCheck.Grays(inComic, out int w2, out int h2);
            Check(w == w2 && h == h2, "the two frames should be the same size: " + w + " × " + h + " and " + w2 + " × " + h2);
            int differ = 0;
            for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 64) differ++;
            Check(differ > 100, "the Pacifico PNG's caption looks like Comic Sans' (" + differ + " pixels apart)");
            Check(ScanCheck.Read(a, w, h)?.Text == "https://b0r3d.org", "the PNG in Pacifico should scan");

            List<BatchRow> rows = BatchRows.ReadQr("url,frameText,fileName,frameFontFamily\nhttps://b0r3d.org/a,Scan me,a,Pacifico\nhttps://b0r3d.org/b,Hello there,b,\"'Bebas Neue', sans-serif\"\n" +
                                                   "https://b0r3d.org/c,Plain,c,Default\nhttps://b0r3d.org/d,Odd,d,No Such Font\n", out bool framed);
            var kind = new BatchKind { Design = new QrDesign(), Frame = framed, Format = BatchFormat.Png };
            List<BatchItem> items = BatchExport.Plan(rows, kind);
            string[] expected = { "pacifico-latin-400-normal.ttf", "bebas-neue-latin-400-normal.ttf", "arial.ttf", "arial.ttf" };
            Check(framed && items.Count == 4, "the batch: " + items.Count + " rows");
            for (int i = 0; i < items.Count; i++)
            {
                List<string> files = FontFilesIn(new QrArt(BatchExport.DesignFor(kind, items[i].Row)).ToDrawing());
                Check(files.SequenceEqual(new[] { expected[i] }), "batch row " + (i + 1) + " (" + items[i].Row.Font + ") drew its caption from " + string.Join(", ", files));
            }
            byte[] made = BatchExport.Make(items[0], kind, out _);
            byte[] back = ScanCheck.Grays(BatchExport.Picture(made, items[0], kind), out int bw, out int bh);
            Check(BatchExport.Read(back, bw, bh, kind) == items[0].Expected, "the Pacifico row's PNG should scan");
        }

        // text from code points, half of a surrogate pair as the one char it is (so no odd character sits in the source)
        static string Chars(params int[] points)
        {
            var s = new System.Text.StringBuilder();
            foreach (int c in points) s.Append(c >= 0xD800 && c <= 0xDFFF ? ((char)c).ToString() : char.ConvertFromUtf32(c));
            return s.ToString();
        }

        static string Hex(string text) => string.Join(" ", Unicode.Points(text).Select(c => "U+" + c.ToString("X4")));

        // Half an emoji (a lone surrogate) or a noncharacter in a caption, from a design file or a paste: no font has a
        // glyph for it, so it goes to the last font as it is (a box or nothing, as a browser shows it), and the caption
        // is measured and drawn into the preview, the files and a batch row's file as any other, never an error.
        static void OddCharactersInCaptions()
        {
            string[] fonts = { null, "'Roboto', sans-serif", "'Pacifico', cursive", "monospace" };
            int[] odd = { 0xD83D, 0xDE00, 0xFFFF, 0xFDD0, 0x10FFFF };
            string[] texts = { Chars('a', 0xD83D, 'b'), Chars(0xDE00), Chars('x', 0xFFFF), Chars(0xFDD0), Chars(0x10FFFF) };
            var problems = new List<string>();
            foreach (string css in fonts)
                foreach (string text in texts)
                {
                    string name = (css ?? "the default font") + ", " + Hex(text);
                    try
                    {
                        CaptionFont font = CaptionFont.For(css);
                        font.Text(text, QrArt.CaptionSize, Brushes.Black, CultureInfo.InvariantCulture);
                        foreach (var (element, face, spelt) in font.Pick(text).Where(p => Unicode.Points(p.Element).Exists(c => odd.Contains(c))))
                            if (face != font.Faces.Last() || spelt != element) problems.Add(name + ": " + Hex(element) + " should go to the last font as it is, not " + face.Family.Source + " as " + Hex(spelt ?? ""));
                        QrArt.PreviewLines("Scan " + text + " for more info", css, 120);
                        var art = new QrArt(new QrDesign { Data = "https://b0r3d.org/odd", Frame = new QrFrame { Text = "Scan " + text + " me", FontFamily = css } });
                        art.ToDrawing();
                        BitmapSource picture = QrExport.Picture(art, new PrintSize(), false);
                        QrExport.Png(picture);
                        QrExport.Jpg(QrExport.Picture(art, new PrintSize(), false, true));
                        QrExport.Pdf(art, new PrintSize(), false, "odd");
                        if (ScanCheck.Read(ScanCheck.Grays(picture, out int w, out int h), w, h)?.Text != "https://b0r3d.org/odd") problems.Add(name + ": the PNG doesn't scan");
                    }
                    catch (Exception x)
                    {
                        problems.Add(name + ": " + x.GetType().Name + ": " + x.Message);
                    }
                }
            Check(problems.Count == 0, problems.Count + " problems: " + string.Join("; ", problems.Take(6)));

            // a design file whose caption ends in half an emoji (JSON's \ud83d, which the website's files can hold) opens
            // into the window, and its preview is drawn and scans
            string path = Path.Combine(Work, "half-an-emoji-" + DesignFile.FileName);
            File.WriteAllText(path, DesignFile.Write(new QrDesign { Data = "https://b0r3d.org/half", Frame = new QrFrame { Text = "Scan HALF" } }).Replace("Scan HALF", "Scan \\ud83d"));
            MainWindow window = NewMain();
            window.OpenDesignFile(path);
            WaitForCheck(window);
            Check(window.CurrentDesign.Frame?.Text == "Scan " + (char)0xD83D && window.Preview.Source != null && window.CheckResult == "ok",
                "the design file with half an emoji in its caption should open and draw: " + window.CheckLine.Text);

            // a batch row whose caption has U+FFFF (valid UTF-8 in a CSV) makes its file, which scans
            string folder = Path.Combine(Work, "odd-batch");
            Fresh(folder);
            BatchWindow batch = NewBatch(window, "url,frameText,fileName\nhttps://b0r3d.org/odd," + Chars('S', 'c', 'a', 'n', ' ', 0xFFFF, ' ', 'm', 'e') + ",odd\n");
            MakeBatch(batch, folder);
            Check(batch.LastOutcome == "1 made, 0 skipped, 0 failed, 0 unread" && File.Exists(Path.Combine(folder, "odd.png")), "the batch row with U+FFFF in its caption: " + batch.LastOutcome + " · " + batch.StatusLine.Text);
            batch.Close();
        }

        // Emoji that are one picture made of several characters stay one character, as Unicode's grapheme clusters
        // (UAX #29) and Chromium have them: joined by a ZWJ, with a skin tone, a variation selector, a keycap's mark, a
        // flag's two letters or a flag's tags. Each comes from one font, as one run, measuring as Segoe UI Emoji draws
        // it, as Chromium draws it from Segoe UI Emoji when no font of the list has it: in the default caption (drawn
        // and in the preview) and in Roboto, Poppins and Pacifico. Joiners and selectors a font hasn't got don't send
        // the letter before them to another font (HarfBuzz hides them), but U+FE0F after an emoji asks for its picture.
        static void EmojiSequencesStayWhole()
        {
            if (!QrArt.Installed("Segoe UI Emoji")) throw new SkipException("Segoe UI Emoji isn't installed on this PC");
            var sequences = new (string Name, string Text)[]
            {
                ("woman technologist", Chars(0x1F469, 0x200D, 0x1F4BB)), ("rainbow flag", Chars(0x1F3F3, 0xFE0F, 0x200D, 0x1F308)),
                ("heart on fire", Chars(0x2764, 0xFE0F, 0x200D, 0x1F525)), ("family", Chars(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467)),
                ("thumbs up, medium skin tone", Chars(0x1F44D, 0x1F3FD)), ("woman technologist, medium skin tone", Chars(0x1F469, 0x1F3FD, 0x200D, 0x1F4BB)),
                ("red heart", Chars(0x2764, 0xFE0F)), ("keycap #", Chars('#', 0xFE0F, 0x20E3)), ("United States flag", Chars(0x1F1FA, 0x1F1F8)),
                ("England flag", Chars(0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE007F)),
            };
            var fonts = new (string Name, CaptionFont Font)[]
            {
                ("the default caption", CaptionFont.For(null)), ("the default caption's preview", CaptionFont.Page), ("Roboto", CaptionFont.For("'Roboto', sans-serif")),
                ("Poppins", CaptionFont.For("'Poppins', sans-serif")), ("Pacifico", CaptionFont.For("'Pacifico', cursive")),
            };
            var wrong = new List<string>();
            foreach (var (name, text) in sequences)
            {
                double emoji = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Emoji"), QrArt.CaptionSize, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;
                foreach (var (fontName, font) in fonts)
                {
                    var picked = font.Pick("Scan " + text + " me");
                    var whole = picked.Where(p => p.Element.Length > 1).ToList();
                    double width = font.Text(text, QrArt.CaptionSize, Brushes.Black, CultureInfo.InvariantCulture).WidthIncludingTrailingWhitespace;
                    if (whole.Count != 1 || whole[0].Element != text || whole[0].Face.Family.Source != "Segoe UI Emoji" || Math.Abs(width - emoji) > 0.01)
                        wrong.Add(name + " in " + fontName + ": " + string.Join(" ", picked.Skip(5).Take(picked.Count - 8).Select(p => "[" + Hex(p.Element) + " from " + p.Face.Family.Source + "]")) +
                                  ", " + width.ToString("0.###", CultureInfo.InvariantCulture) + " px wide, Segoe UI Emoji " + emoji.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }
            Check(wrong.Count == 0, wrong.Count + " wrong: " + string.Join("; ", wrong.Take(6)));

            // a third flag letter is a character of its own; a variation selector stays on its character (葛 with
            // VS17, ≩ with VS1); the default caption's line breaks see one emoji
            var flags = CaptionFont.For(null).Pick(Chars(0x1F1FA, 0x1F1F8, 0x1F1EC));
            Check(flags.Count == 2 && flags[0].Element == Chars(0x1F1FA, 0x1F1F8), "three flag letters: " + string.Join(" ", flags.Select(p => "[" + Hex(p.Element) + "]")));
            var selected = CaptionFont.For(null).Pick(Chars(0x845B, 0xE0100, 0x2269, 0xFE00));
            Check(selected.Count == 2 && selected[0].Element == Chars(0x845B, 0xE0100), "variation selectors: " + string.Join(" ", selected.Select(p => "[" + Hex(p.Element) + "]")));
            string family = Chars(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467);
            Check(QrArt.PreviewLines(string.Join(" ", Enumerable.Repeat(family, 7)), "", 200) == 1, "seven families should fit a 200 px line, as one emoji each");

            // a joiner or a variation selector Pacifico hasn't got leaves its letter in Pacifico, as HarfBuzz hides them;
            // U+FE0F after ☺ asks for the emoji, which Consolas (it has ☺) hasn't got: Segoe UI Emoji's, as in Chromium
            CaptionFont pacifico = CaptionFont.For("'Pacifico', cursive");
            CaptionFace letter = pacifico.Pick("ab")[0].Face;
            foreach (int hidden in new[] { 0x200D, 0x200C, 0xFE0E, 0xFE0F, 0xE0067 })
            {
                var picked = pacifico.Pick("a" + Chars(hidden) + "b");
                Check(picked.Count == 2 && picked[0].Face == letter && picked[1].Face == letter, "Pacifico's a and U+" + hidden.ToString("X4") + ": " + string.Join(" ", picked.Select(p => "[" + Hex(p.Element) + " from " + p.Face.Family.Source + "]")));
            }
            var smiley = CaptionFont.For("monospace").Pick(Chars(0x263A, 0xFE0F));
            Check(smiley.Count == 1 && smiley[0].Face.Family.Source == "Segoe UI Emoji", "monospace's ☺ with U+FE0F should be Segoe UI Emoji's, not " + smiley[0].Face.Family.Source);
            Check(CaptionFont.For("monospace").Pick(Chars(0x263A, 0xFE0E))[0].Face.Family.Source == "Consolas", "monospace's ☺ with U+FE0E should be Consolas'");
        }

        // The runs of glyphs in a design's drawing as WPF lays them out, in the order they're drawn (a caption's lines in
        // turn): drawn with every font in one colour, as before emoji had theirs. (WPF's own record of a run's characters
        // can be wrong for an emoji made of several, so the tests find a run by its line.)
        static List<GlyphRun> GlyphRunsIn(QrArt art)
        {
            Func<GlyphTypeface, ColourGlyphs> colours = ColourGlyphs.Of;
            ColourGlyphs.Of = font => null;
            Drawing plain;
            try
            {
                plain = art.ToDrawing();
            }
            finally
            {
                ColourGlyphs.Of = colours;
            }
            var runs = new List<GlyphRun>();
            void Walk(Drawing d)
            {
                if (d is DrawingGroup group) foreach (Drawing child in group.Children) Walk(child);
                else if (d is GlyphRunDrawing glyphs) runs.Add(glyphs.GlyphRun);
            }
            Walk(plain);
            return runs;
        }

        // In a picture drawn `scale` pixels to the design's px: the share of the pixels in a box (in the design's px, a
        // pixel in from each side) that are what's wanted
        static Func<Rect, Func<Color, bool>, double> Shares(BitmapSource picture, double scale)
        {
            var bgra = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            return (box, wanted) =>
            {
                int all = 0, found = 0;
                for (int y = Math.Max(0, (int)Math.Ceiling(box.Top * scale) + 1); y < Math.Min(h, (int)Math.Floor(box.Bottom * scale) - 1); y++)
                    for (int x = Math.Max(0, (int)Math.Ceiling(box.Left * scale) + 1); x < Math.Min(w, (int)Math.Floor(box.Right * scale) - 1); x++)
                    {
                        int i = (y * w + x) * 4;
                        all++;
                        if (wanted(Color.FromRgb(px[i + 2], px[i + 1], px[i]))) found++;
                    }
                return all == 0 ? 0 : (double)found / all;
            };
        }

        static string Percent(double share) => (100 * share).ToString("0", CultureInfo.InvariantCulture) + " %";

        static BitmapSource Decoded(byte[] file) => BitmapDecoder.Create(new MemoryStream(file), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];

        // A caption's emoji in their own colours, from Segoe UI Emoji's COLR layers and CPAL palette, as Chromium draws
        // them on the website's page (WPF draws a font in one colour), each where WPF lays it out: a smiley, a star with
        // U+FE0F, a ZWJ family and a skin tone are mostly their layers' colours (the smiley's yellow) and none of the
        // caption's, in the drawing the preview shows, the PNG, the JPG, the PDF and a batch row's PNG. The letters, a
        // flag's letters (Windows' emoji font has no flags, nor layers for their letters) and an emoji drawn as text (❤
        // without U+FE0F, Segoe UI Symbol's) stay in the caption's colour.
        static void EmojiInTheirColours()
        {
            if (!QrArt.Installed("Segoe UI Emoji")) throw new SkipException("Segoe UI Emoji isn't installed on this PC");
            string[] emoji = { Chars(0x1F600), Chars(0x2B50, 0xFE0F), Chars(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467), Chars(0x1F44D, 0x1F3FD) };
            string[] plain = { "Hi", Chars(0x1F1FA, 0x1F1F8), Chars(0x2764) };
            string[] lines = plain.Take(1).Concat(emoji).Concat(plain.Skip(1)).ToArray();   // one of them a line
            Color ink = Color.FromRgb(0x00, 0xA0, 0x00);   // a green none of these emoji has
            var art = new QrArt(new QrDesign { Data = "https://b0r3d.org/emoji", Width = 300, Height = 300, Frame = new QrFrame { Text = string.Join("\n", lines), TextColor = "#00a000" } });
            List<GlyphRun> drawn = GlyphRunsIn(art);
            Check(drawn.Count == lines.Length, "the caption's lines should each be one run of glyphs, not " + drawn.Count + " runs");
            var runs = new Dictionary<string, GlyphRun>();
            for (int i = 0; i < lines.Length; i++) runs[lines[i]] = drawn[i];

            // each emoji's colours, from its font's own tables (not the greys and whites, which the frame has too)
            var colours = new Dictionary<string, List<Color>>();
            foreach (string e in emoji)
            {
                ColourGlyphs tables = ColourGlyphs.Of(runs[e].GlyphTypeface);
                Check(tables != null, Hex(e) + " is drawn from " + runs[e].GlyphTypeface.FontUri + ", which has no colour tables");
                colours[e] = runs[e].GlyphIndices.SelectMany(tables.ColoursOf).Where(c => c.HasValue && Math.Max(c.Value.R, Math.Max(c.Value.G, c.Value.B)) - Math.Min(c.Value.R, Math.Min(c.Value.G, c.Value.B)) >= 60)
                                                 .Select(c => c.Value).Distinct().ToList();
                Check(colours[e].Count > 0, Hex(e) + " has no colours in its layers");
            }
            Color smiley = ColourGlyphs.Of(runs[emoji[0]].GlyphTypeface).ColoursOf(runs[emoji[0]].GlyphIndices[0])[0].Value;
            Check(smiley.R > 200 && smiley.G > 120 && smiley.B < 120, "the smiley's face should be yellow in the palette, not " + smiley);
            // the SVG keeps the caption as text, as the website's does, for its viewer to draw
            string svg = art.ToSvg();
            Check(lines.All(l => svg.Contains("\">" + l + "</tspan>")), "the SVG should have each line of the caption as it is");

            var print = new PrintSize { On = true, Width = 60, Unit = "mm", Dpi = 300 };
            double k = QrExport.PictureSize(art, print, false).Scale;
            BitmapSource pdf = PdfFile.ReadPicture(QrExport.Pdf(art, print, false, "emoji"));
            var pictures = new (string Name, BitmapSource Picture, double Scale, int By)[]
            {
                ("the preview at 150 %", art.Render(1.5, (int)Math.Ceiling(art.W * 1.5), (int)Math.Ceiling(art.H * 1.5)), 1.5, 8),
                ("the PNG", Decoded(QrExport.Png(QrExport.Picture(art, print, false))), k, 8),
                ("the JPG", Decoded(QrExport.Jpg(QrExport.Picture(art, print, false, true))), k, 40),
                ("the PDF", pdf, pdf.PixelWidth / art.W, 8),
            };
            var wrong = new List<string>();
            foreach (var (name, picture, scale, by) in pictures)
            {
                Func<Rect, Func<Color, bool>, double> share = Shares(picture, scale);
                foreach (string e in emoji)
                {
                    Rect box = runs[e].BuildGeometry().Bounds;
                    double own = share(box, c => colours[e].Any(o => Like(c, o, by))), caption = share(box, c => Like(c, ink, by));
                    if (own < 0.25 || caption > 0.02) wrong.Add(name + ": " + Hex(e) + " is " + Percent(own) + " its own colours and " + Percent(caption) + " the caption's");
                }
                // anti-aliased on white, so each pixel a blend of the two: red and blue alike, green as much or more
                if (name == "the PNG")
                    foreach (string p in plain)
                    {
                        Rect box = runs[p].BuildGeometry().Bounds;
                        double other = share(box, c => Math.Abs(c.R - c.B) > 12 || c.G + 12 < c.R), caption = share(box, c => Like(c, ink, by));
                        if (other > 0 || caption < 0.02) wrong.Add(name + ": " + Hex(p) + " is " + Percent(caption) + " the caption's colour and " + Percent(other) + " others");
                    }
            }

            // a batch row's PNG, its caption the smiley
            List<BatchRow> rows = BatchRows.ReadQr("url,frameText,fileName\nhttps://b0r3d.org/e," + emoji[0] + ",e\n", out bool framed);
            var kind = new BatchKind { Design = new QrDesign(), Frame = framed, Format = BatchFormat.Png };
            BatchItem item = BatchExport.Plan(rows, kind)[0];
            var rowArt = new QrArt(BatchExport.DesignFor(kind, item.Row));
            BitmapSource made = Decoded(BatchExport.Make(item, kind, out _));
            double yellow = Shares(made, made.PixelWidth / rowArt.W)(GlyphRunsIn(rowArt)[0].BuildGeometry().Bounds, c => Like(c, smiley, 8));
            if (yellow < 0.25) wrong.Add("a batch row's PNG: the smiley is " + Percent(yellow) + " its yellow");
            Check(wrong.Count == 0, wrong.Count + " wrong: " + string.Join("; ", wrong.Take(8)));
        }

        // A colour font's tables are read once (about 3 ms for Segoe UI Emoji) and kept by the font's file: the same font
        // however WPF hands it out (a run that falls back to it has another GlyphTypeface than the one asked for by name)
        // and in the styles WPF makes of it, bold and italic (SVG text can ask for them), gives the one copy, and caption
        // after caption meets the fonts it met before
        static void ColourTablesReadOnce()
        {
            if (!QrArt.Installed("Segoe UI Emoji")) throw new SkipException("Segoe UI Emoji isn't installed on this PC");
            GlyphTypeface Face(FontStyle style, FontWeight weight)
            {
                new Typeface(new FontFamily("Segoe UI Emoji"), style, weight, FontStretches.Normal).TryGetGlyphTypeface(out GlyphTypeface face);
                return face;
            }
            ColourGlyphs tables = ColourGlyphs.Of(Face(FontStyles.Normal, FontWeights.Normal));
            int met = ColourGlyphs.Met;
            var fallback = new FormattedText(Chars(0x1F600), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 20, Brushes.Black, 1.0);
            var drawn = new DrawingGroup();
            using (DrawingContext dc = drawn.Open()) dc.DrawText(fallback, new Point());
            GlyphTypeface fellBack = null;
            void Walk(Drawing d)
            {
                if (d is DrawingGroup group) foreach (Drawing child in group.Children) Walk(child);
                else if (d is GlyphRunDrawing glyphs) fellBack = glyphs.GlyphRun.GlyphTypeface;
            }
            Walk(drawn);
            var faces = new[] { ("fallen back to", fellBack), ("bold", Face(FontStyles.Normal, FontWeights.Bold)), ("italic", Face(FontStyles.Italic, FontWeights.Normal)), ("bold italic", Face(FontStyles.Italic, FontWeights.Bold)) };
            var wrong = new List<string>();
            foreach (var (name, face) in faces)
                if (face == null || !ReferenceEquals(ColourGlyphs.Of(face), tables)) wrong.Add("Segoe UI Emoji " + name + (face == null ? " isn't there" : " (" + face.StyleSimulations + ") has its own tables"));
            if (ColourGlyphs.Met != met) wrong.Add((ColourGlyphs.Met - met) + " more fonts kept for those");
            // 20 captions with emoji in three fonts, drawn: then 20 more, which meet no font that isn't kept
            void Captions(int from)
            {
                for (int i = from; i < from + 20; i++)
                    new QrArt(new QrDesign { Data = "https://b0r3d.org/" + i, Frame = new QrFrame { Text = "Hi " + Chars(0x1F600 + i % 30), FontFamily = new[] { null, "'Pacifico', cursive", "serif" }[i % 3] } }).Render(1, 300, 380);
            }
            Captions(0);
            met = ColourGlyphs.Met;
            Captions(20);
            if (ColourGlyphs.Met != met) wrong.Add("20 more captions kept " + (ColourGlyphs.Met - met) + " more fonts");
            Check(wrong.Count == 0, string.Join("; ", wrong));
        }

        // A font file of a COLR table (version 0) and a CPAL palette: each base glyph drawn as the layers given, a glyph
        // and a palette entry each (0xFFFF: the text's colour)
        static byte[] ColourFont((ushort Glyph, (ushort Glyph, ushort Colour)[] Layers)[] bases, Color[] palette)
        {
            void U16(List<byte> to, int v) => to.AddRange(new[] { (byte)(v >> 8), (byte)v });
            void U32(List<byte> to, long v) => to.AddRange(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
            var colr = new List<byte>();
            U16(colr, 0);
            U16(colr, bases.Length);
            U32(colr, 14);
            U32(colr, 14 + 6 * bases.Length);
            U16(colr, bases.Sum(b => b.Layers.Length));
            int first = 0;
            foreach (var (glyph, layers) in bases)
            {
                U16(colr, glyph);
                U16(colr, first);
                U16(colr, layers.Length);
                first += layers.Length;
            }
            foreach (var (glyph, colour) in bases.SelectMany(b => b.Layers))
            {
                U16(colr, glyph);
                U16(colr, colour);
            }
            var cpal = new List<byte>();
            foreach (int v in new[] { 0, palette.Length, 1, palette.Length }) U16(cpal, v);
            U32(cpal, 14);
            U16(cpal, 0);
            foreach (Color c in palette) cpal.AddRange(new[] { c.B, c.G, c.R, c.A });
            var font = new List<byte>();
            U32(font, 0x00010000);
            foreach (int v in new[] { 2, 0, 0, 0 }) U16(font, v);
            foreach (var (tag, table, at) in new[] { ("COLR", colr, 44), ("CPAL", cpal, 44 + colr.Count) })
            {
                font.AddRange(System.Text.Encoding.ASCII.GetBytes(tag));
                U32(font, 0);
                U32(font, at);
                U32(font, table.Count);
            }
            font.AddRange(colr);
            font.AddRange(cpal);
            return font.ToArray();
        }

        // A font's colour tables are read as anything from outside is. Made-up COLR and CPAL tables given to Segoe UI
        // Emoji draw its glyphs as their layers, in the palette's colours and, for entry 0xFFFF, the caption's. Tables that
        // don't add up (a glyph's layers past the layers, a layer past the font's glyphs, counts past their table, a colour
        // past the palette, places past the file, glyphs out of order, a collection or a later version, every cut-short
        // copy and copies with bytes changed) never stop a drawing: the font is drawn in one colour, as one without a COLR
        // table is.
        static void ColourTablesFromOutside()
        {
            if (!QrArt.Installed("Segoe UI Emoji")) throw new SkipException("Segoe UI Emoji isn't installed on this PC");
            string smiley = Chars(0x1F600), star = Chars(0x2B50, 0xFE0F);
            Color ink = Color.FromRgb(0x00, 0xA0, 0x00), red = Color.FromRgb(0xFF, 0x00, 0x00);
            var art = new QrArt(new QrDesign { Data = "https://b0r3d.org/tables", Frame = new QrFrame { Text = smiley + "\n" + star, TextColor = "#00a000" } });
            List<GlyphRun> drawn = GlyphRunsIn(art);
            Check(drawn.Count == 2, "the two lines should each be one run of glyphs, not " + drawn.Count + " runs");
            var runs = new Dictionary<string, GlyphRun> { [smiley] = drawn[0], [star] = drawn[1] };
            GlyphTypeface emoji = runs[smiley].GlyphTypeface;
            ushort face = runs[smiley].GlyphIndices[0], starGlyph = runs[star].GlyphIndices[0];
            // the smiley as its own outline in red with a star on it in the text's colour, the star as its own outline in red
            byte[] good = ColourFont(new[] { (face, new[] { (face, (ushort)0), (starGlyph, (ushort)0xFFFF) }), (starGlyph, new[] { (starGlyph, (ushort)0) }) }.OrderBy(b => b.Item1).ToArray(), new[] { red });
            const int colr = 44, bases = colr + 14, layers = bases + 12, cpal = layers + 12;   // where the tables' parts are in it

            // drawn through the seam: the smiley's share of red and of the caption's colour, and the star's of red
            (double SmileyRed, double SmileyInk, double StarRed) Drawn(byte[] font)
            {
                Func<GlyphTypeface, ColourGlyphs> colours = ColourGlyphs.Of;
                ColourGlyphs.Of = f => f.Equals(emoji) ? ColourGlyphs.Read(new MemoryStream(font), f.GlyphCount) : null;
                try
                {
                    Func<Rect, Func<Color, bool>, double> share = Shares(art.Render(2, (int)Math.Ceiling(art.W * 2), (int)Math.Ceiling(art.H * 2)), 2);
                    Rect a = runs[smiley].BuildGeometry().Bounds, b = runs[star].BuildGeometry().Bounds;
                    return (share(a, c => Like(c, red, 8)), share(a, c => Like(c, ink, 8)), share(b, c => Like(c, red, 8)));
                }
                finally
                {
                    ColourGlyphs.Of = colours;
                }
            }
            var (smileyRed, smileyInk, starRed) = Drawn(good);
            Check(smileyRed > 0.05 && smileyInk > 0.25 && starRed > 0.25, "with made-up tables the smiley should be red with a star in the caption's colour (palette entry 0xFFFF) and the star red: " +
                  Percent(smileyRed) + " red and " + Percent(smileyInk) + " the caption's, the star " + Percent(starRed) + " red");

            byte[] With(int at, params int[] values)   // the good file with 16-bit values from `at` on
            {
                byte[] copy = (byte[])good.Clone();
                for (int i = 0; i < values.Length; i++)
                {
                    copy[at + 2 * i] = (byte)(values[i] >> 8);
                    copy[at + 2 * i + 1] = (byte)values[i];
                }
                return copy;
            }
            var broken = new (string Name, byte[] Font)[]
            {
                ("a glyph's layers past the layer records", With(bases + 2, 3)),
                ("a layer past the font's glyphs", With(layers, 0xFFFE)),
                ("more base glyphs than the table holds", With(colr + 2, 0xFFFF)),
                ("more layers than the table holds", With(colr + 12, 0xFFFF)),
                ("base glyphs placed past the table", With(colr + 4, 0x7FFF, 0xFFFF)),
                ("layers placed past the end of the file", With(colr + 8, 0, good.Length)),
                ("a colour past the palette", With(layers + 2, 1)),
                ("a palette of more colours than the table holds", With(cpal + 2, 0xFFFF)),
                ("more colour records than the table holds", With(cpal + 2, 0xFFFF, 1, 0xFFFF)),
                ("colours placed past the end of the file", With(cpal + 8, 0x7FFF, 0xFFFF)),
                ("a COLR table past the end of the file", With(20, 0x7FFF, 0xFFFF)),
                ("more tables than the file holds", With(4, 0xFFFF)),
                ("base glyphs out of order", With(bases, 0xFFFF)),
                ("a later version of COLR", With(colr, 2)),
                ("a font collection", With(0, 0x7474, 0x6366)),
                ("no COLR table (renamed)", With(12, 0x584F)),
            };
            var wrong = new List<string>();
            foreach (var (name, font) in broken)
            {
                try
                {
                    if (ColourGlyphs.Read(new MemoryStream(font), emoji.GlyphCount) != null) wrong.Add(name + ": read as if fine");
                    var (red1, ink1, red2) = Drawn(font);
                    if (ink1 < 0.25 || red1 > 0 || red2 > 0) wrong.Add(name + ": the smiley is " + Percent(ink1) + " the caption's colour and " + Percent(red1) + " red, the star " + Percent(red2) + " red");
                }
                catch (Exception x)
                {
                    wrong.Add(name + ": " + x.GetType().Name + ": " + x.Message);
                }
            }

            // tables that don't add up in a way Read never lets through (made here as they are: a glyph's layers past the
            // layer records, a colour past the palette) never stop a drawing either: a run of glyphs whose drawing in
            // colour fails is WPF's own run, in the caption's colour
            foreach (var (name, made) in new[]
            {
                ("made with a glyph's layers past the layers", new ColourGlyphs(new[] { face }, new ushort[] { 0 }, new ushort[] { 3 }, new[] { face }, new ushort[] { 0 }, new[] { 0xFFFF0000u })),
                ("made with a colour past the palette", new ColourGlyphs(new[] { face }, new ushort[] { 0 }, new ushort[] { 1 }, new[] { face }, new ushort[] { 5 }, new[] { 0xFFFF0000u })),
            })
            {
                Func<GlyphTypeface, ColourGlyphs> colours = ColourGlyphs.Of;
                ColourGlyphs.Of = f => f.Equals(emoji) ? made : null;
                try
                {
                    Func<Rect, Func<Color, bool>, double> share = Shares(art.Render(2, (int)Math.Ceiling(art.W * 2), (int)Math.Ceiling(art.H * 2)), 2);
                    Rect a = runs[smiley].BuildGeometry().Bounds;
                    double red1 = share(a, c => Like(c, red, 8)), ink1 = share(a, c => Like(c, ink, 8));
                    if (ink1 < 0.25 || red1 > 0) wrong.Add(name + ": the smiley is " + Percent(ink1) + " the caption's colour and " + Percent(red1) + " red");
                }
                catch (Exception x)
                {
                    wrong.Add(name + ": " + x.GetType().Name + ": " + x.Message);
                }
                finally
                {
                    ColourGlyphs.Of = colours;
                }
            }

            // every cut-short copy, and copies with a few bytes changed: read without an exception, and drawn
            var random = new Random(17);
            var changed = new List<byte[]>();
            for (int length = 0; length < good.Length; length++) changed.Add(good.Take(length).ToArray());
            for (int n = 0; n < 3000; n++)
            {
                byte[] copy = (byte[])good.Clone();
                for (int c = random.Next(1, 4); c > 0; c--) copy[random.Next(copy.Length)] = (byte)random.Next(256);
                changed.Add(copy);
            }
            int fine = 0;
            foreach (byte[] font in changed)
            {
                try
                {
                    if (ColourGlyphs.Read(new MemoryStream(font), emoji.GlyphCount) == null) continue;
                    if (fine++ < 12) Drawn(font);
                }
                catch (Exception x)
                {
                    wrong.Add("a changed copy: " + x.GetType().Name + ": " + x.Message);
                    break;
                }
            }
            Console.WriteLine("      " + changed.Count + " cut-short and changed copies read, " + fine + " of them still tables that add up, " + Math.Min(fine, 12) + " of those drawn");
            Check(wrong.Count == 0, wrong.Count + " wrong: " + string.Join("; ", wrong.Take(6)));
        }

        // A letter with 5,000 marks on it: WPF ends the whole program (exit code 0x80000003) when it lays out a letter
        // with about 720, so a caption keeps a letter's first 100 (CaptionFont.MostMarks). Drawn in a second run of this
        // exe (--marks), so that if it ever ends a program, it's that one: the preview's lines, the drawing and a PNG.
        // The marks are an accent, and two that Windows knows and .NET Framework's older Unicode calls unassigned.
        static void ManyMarksOnALetter()
        {
            var start = new ProcessStartInfo(Path.Combine(TestsFolder, "b0r3dQRTests.exe"), "--marks 5000") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (Process run = Process.Start(start))
            {
                string said = run.StandardOutput.ReadToEnd();
                if (!run.WaitForExit(30000))
                {
                    run.Kill();
                    Check(false, "the run drawing 5,000 marks was still going after 30 seconds, so it was stopped");
                }
                Check(run.ExitCode == 0 && said.Contains("drawn"), "a caption with 5,000 marks on a letter ended the run that drew it (exit code " + run.ExitCode + "): " + said.Trim());
            }
            foreach (var (letter, mark) in ManyMarks)
            {
                var picked = CaptionFont.For("'Roboto', sans-serif").Pick(letter + string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(mark), 5000)) + " b");
                Check(picked.Count == 3 && Unicode.Points(picked[0].Element).Count == 1 + CaptionFont.MostMarks,
                    "the letter should keep its first " + CaptionFont.MostMarks + " U+" + mark.ToString("X4") + ": " + Unicode.Points(picked[0].Element).Count);
            }
        }

        // the letters and marks --marks draws: an accent; a mark of Unicode 14 (nonspacing); a Vedic sign of Unicode 10
        // (a spacing mark) on a Devanagari letter
        static readonly (string Letter, int Mark)[] ManyMarks = { ("a", 0x301), ("a", 0x1AC1), ("क", 0x1CF7) };

        // what --marks <count> [<mark>] draws, in a run of its own: a caption of a letter with that many marks, in the
        // default font, Roboto and Pacifico, through the preview's line breaks, the drawing and a PNG, for each of the
        // marks (or only the one given, in hex), saying which as it starts it
        static int DrawManyMarks(int count, string only)
        {
            SetErrorMode(0x0001 | 0x0002);   // if WPF ends this run, Windows reports nothing and shows no box about it
            foreach (var (letter, mark) in ManyMarks)
            {
                if (only != null && !string.Equals(mark.ToString("X4"), only.PadLeft(4, '0'), StringComparison.OrdinalIgnoreCase)) continue;
                Console.WriteLine("U+" + mark.ToString("X4"));
                string text = letter + string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(mark), count)) + " b";
                foreach (string css in new[] { null, "'Roboto', sans-serif", "'Pacifico', cursive" })
                {
                    QrArt.PreviewLines(text, css, 200);
                    var art = new QrArt(new QrDesign { Data = "https://b0r3d.org/marks", Frame = new QrFrame { Text = text, FontFamily = css } });
                    QrExport.Png(QrExport.Picture(art, new PrintSize(), false));
                }
            }
            Console.WriteLine("drawn");
            return 0;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern uint SetErrorMode(uint mode);

        // A caption's font names are a design file's or a CSV's text: one that WPF would read as a place to load a font
        // from (anything before a '#'), or a path, is never taken as a font on this PC, even one installed under that name
        static void FontNamesAreNeverPlaces()
        {
            Func<string, bool> installed = QrArt.Installed;
            QrArt.Installed = name => true;   // as if a font of every name were installed
            try
            {
                foreach (string name in new[] { "file://server/share/x.ttf#Arial", "file:///C:/Fonts/x.ttf#Arial", "\\\\server\\share\\#Arial", "fonts/x.ttf#Arial", "#Arial", "C:\\Fonts\\x.ttf", "Ari#al" })
                {
                    CaptionFont font = CaptionFont.For("'" + name + "', serif");
                    string faces = string.Join(", ", font.Faces.Select(f => f.Family.Source));
                    Check(faces == "Times New Roman", "'" + name + "' should never be a font: " + faces);
                }
                Check(CaptionFont.For("'Comic Sans MS', serif").Faces[0].Family.Source == "Comic Sans MS", "an installed font by its name should still be one");
            }
            finally
            {
                QrArt.Installed = installed;
            }
        }
    }
}
