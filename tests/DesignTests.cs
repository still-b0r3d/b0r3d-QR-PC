using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Design;
using B0r3dQR.Qr;

namespace B0r3dQR.Tests
{
    // The designer: shapes, logo, frame, files and checks, against the website's own numbers.
    static partial class TestMain
    {
        static QrDesign Sample(string data = "https://b0r3d.org")
        {
            QrDesign d = Presets.Apply("Plain", new QrDesign());
            d.Data = data;
            return d;
        }

        // the art drawn at its own size on white, read back
        static string ReadArt(QrArt art, double scale = 1)
        {
            BitmapSource picture = art.Render(scale, (int)Math.Floor(art.W * scale), (int)Math.Floor(art.H * scale), Colors.White);
            byte[] grays = ScanCheck.Grays(picture, out int w, out int h);
            return ScanCheck.Read(grays, w, h)?.Text;
        }

        // every dot, ring and centre shape the website draws, in every combination, reads back
        static void EveryShapeReadsBack()
        {
            var failed = new List<string>();
            foreach (string dots in Presets.DotTypes)
                foreach (string ring in Presets.CornerSquareTypes)
                    foreach (string centre in Presets.CornerDotTypes)
                    {
                        QrDesign d = Sample("https://b0r3d.org/shapes");
                        d.DotsType = dots;
                        d.CornersSquareType = ring;
                        d.CornersDotType = centre;
                        d.Width = d.Height = 330;
                        if (ReadArt(new QrArt(d)) != d.Data) failed.Add(dots + "/" + ring + "/" + centre);
                    }
            Check(failed.Count == 0, failed.Count + " of 72 shape combinations didn't read back: " + string.Join(", ", failed.Take(8)));
        }

        // the shapes are the website's: a dot is a circle, rounded corners as it rounds them
        static void ShapesLikeTheWebsite()
        {
            QrDesign d = Sample();
            d.DotsType = "dots";
            d.Margin = 0;
            d.Width = d.Height = 250;   // 25 modules: 10 px each
            string svg = new QrArt(d).ToSvg();
            Check(svg.Contains("class=\"qr-dots\"") && svg.Contains("class=\"qr-corner-square\"") && svg.Contains("class=\"qr-corner-dot\""), "the SVG should have the website's three paths");
            Check(Regex.IsMatch(svg, "class=\"qr-dots\"[^>]*d=\"M[^\"]*A5 5 0 0 1"), "dots should be circles of radius half a module");
            d.DotsType = "unknown-type";
            d.CornersSquareType = "nonsense";
            QrArt odd = new QrArt(d);
            Check(odd.ToSvg().Contains("A17.5 17.5"), "an unknown ring type should be drawn extra-rounded (radius 1.75 modules), as on the website");
            Check(ReadArt(odd) == d.Data, "a design with unknown types should still draw and read back");
        }

        // the website's logo room: its examples for https://a.co at Q and the presets' b0r3d.org at H
        static void LogoRoomLikeTheWebsite()
        {
            QrDesign d = Sample("https://a.co");
            d.Logo = Presets.LogoUrl("b0r3d-cup.png");
            d.Margin = 0;
            d.Width = d.Height = 200;
            QrArt art = new QrArt(d);
            Check(art.Code.Size == 25 && art.Cleared == 7 && art.LogoModules == 5, "https://a.co at Q: " + art.Code.Size + " modules, " + art.Cleared + " cleared, logo " + art.LogoModules + " (the website: 25, 7, 5)");
            Check(art.ToSvg().Contains("class=\"qr-logo\" x=\"80\" y=\"80\" width=\"40\" height=\"40\""), "the logo box should be at 80 px, 40 px across, as on the website");
            QrDesign cup = Presets.Apply("b0r3d Cup", Sample());
            QrArt cupArt = new QrArt(cup);
            Check(cupArt.Code.Size == 29 && cupArt.Cleared == 7 && cupArt.LogoModules == 5 && cupArt.LevelUsed == QrLevel.H, "b0r3d Cup: " + cupArt.Code.Size + " modules, " + cupArt.Cleared + " cleared, logo " + cupArt.LogoModules);
            Check(ReadArt(cupArt, 2) == cup.Data && ReadArt(new QrArt(Presets.Apply("Rukus", Sample())), 2) == cup.Data, "the b0r3d Cup and Rukus presets should read back with their logos");
            QrDesign low = Sample();
            low.Level = QrLevel.L;
            low.Logo = Presets.LogoUrl("rukus.png");
            QrArt raised = new QrArt(low);
            Check(raised.LevelUsed == QrLevel.Q && raised.LevelRaised, "a logo should raise L to Q");
            low.Logo = "https://example.com/logo.png";
            QrArt web = new QrArt(low);
            Check(web.LogoProblem != null && web.LogoProblem.Contains("never goes online") && web.LevelUsed == QrLevel.L && web.Cleared == 0, "a logo on the web is left out with a note, and clears nothing");
            low.Logo = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle cx=\"5\" cy=\"5\" r=\"5\"/></svg>"));
            QrArt drawing = new QrArt(low);
            Check(drawing.LogoProblem == null && drawing.LevelUsed == QrLevel.Q && drawing.Cleared > 0 && drawing.ToSvg().Contains("class=\"qr-logo\"") && drawing.ToSvg().Contains(low.Logo),
                "an SVG logo should clear its room and raise L to Q as on the website, be drawn, and go into SVG files as it is: " + drawing.LogoProblem);

            // a phone photo saved sideways, with a note of how to turn it
            var wide = BitmapSource.Create(20, 10, 96, 96, PixelFormats.Bgr24, null, new byte[20 * 10 * 3], 20 * 3);
            var meta = new BitmapMetadata("jpg");
            meta.SetQuery("System.Photo.Orientation", (ushort)6);
            var jpeg = new JpegBitmapEncoder();
            jpeg.Frames.Add(BitmapFrame.Create(wide, null, meta, null));
            byte[] sideways;
            using (var bytes = new MemoryStream())
            {
                jpeg.Save(bytes);
                sideways = bytes.ToArray();
            }
            BitmapSource upright = Images.FromBytes(sideways);
            Check(upright.PixelWidth == 10 && upright.PixelHeight == 20, "a photo saved sideways should be turned upright, as browsers show it, not " + upright.PixelWidth + " × " + upright.PixelHeight);
            byte[] png = Convert.FromBase64String(Presets.LogoUrl("b0r3d-cup.png").Substring("data:image/png;base64,".Length));
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(png)).Replace("-", "").ToLowerInvariant();
            Check(png.Length == 27918 && hash.StartsWith("a620fc2801ee13ff", StringComparison.Ordinal), "the b0r3d Cup logo should be the website's PNG, byte for byte");
        }

        // the website's frame: a bottom caption with the Default Frame around a 200 px code
        static void FrameLikeTheWebsite()
        {
            QrDesign d = Sample();
            d.Frame = Presets.ApplyFrame("Default Frame", null);
            QrArt art = new QrArt(d);
            Check(Math.Abs(art.W - 234) < 1e-9 && Math.Abs(art.H - 271.6) < 1e-9, "the framed picture is " + art.W + " × " + art.H + ", the website's 234 × 271.6");
            string svg = art.ToSvg();
            Check(svg.Contains("<g transform=\"translate(17 17)\">") && svg.Contains("<text x=\"117\" y=\"251\" font-family=\"sans-serif\"") && svg.Contains("<tspan x=\"117\" dy=\"0\">Scan for more info</tspan>"),
                "the code should sit at (17, 17) and the caption's baseline at 251, as on the website");
            QrExport.Size size = QrExport.PictureSize(art, null, false);
            Check(size.Width == 239 && size.Height == 278, "the framed PNG is " + size.Width + " × " + size.Height + ", the website's 239 × 278");
            Check(ReadArt(art) == d.Data, "the framed code should read back");

            // the website sizes framed pictures from its preview, where the browser wraps the caption
            d.Frame.Text = "Scan to see our menu";
            QrArt menu = new QrArt(d);
            QrExport.Size menuSize = QrExport.PictureSize(menu, null, false);
            Check(menu.CaptionLines.Count == 2 && menuSize.Width == 234 && menuSize.Height == 293,
                "\"Scan to see our menu\" should save as the website's 234 × 293 (two lines drawn, one in its preview), not " + menuSize.Width + " × " + menuSize.Height);
            d.Frame.Text = QrFrame.DefaultText;
            d.Frame.Position = "right";
            QrExport.Size side = QrExport.PictureSize(new QrArt(d), null, false);
            Check(side.Width == 450 && side.Height == 234, "a caption on the right should save as the website's 450 × 234, not " + side.Width + " × " + side.Height);
            d.Frame.Position = "bottom";

            // the website's SVG moves each line down from the one before; an empty line moves nothing
            d.Frame.Text = "A\n\nB";
            Check(new QrArt(d).ToSvg().Contains("<tspan x=\"117\" dy=\"0\">A</tspan><tspan x=\"117\" dy=\"21.6\"></tspan><tspan x=\"117\" dy=\"21.6\">B</tspan>"),
                "an empty caption line should be written as the website writes it");
            d.Frame.Text = "Bell\u0007 & <b>";
            string bell = new QrArt(d).ToSvg();
            new System.Xml.XmlDocument().LoadXml(bell);
            Check(bell.Contains(">Bell &amp; &lt;b&gt;</tspan>"), "a control character in a caption should be left out of the SVG, so it stays valid XML");
            d.Frame.Text = QrFrame.DefaultText;
            d.Frame.BackgroundImage = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"));
            Check(new QrArt(d).ToSvg().Contains("clip-path=\"url(#frame-bg-clip)\" href=\"" + d.Frame.BackgroundImage), "a frame's SVG picture should go into the SVG, clipped as the website clips it");
            d.Frame.BackgroundImage = null;
            List<string> lines = QrArt.Wrap("A caption long enough to need wrapping onto several lines", 200);
            Check(lines.All(l => l.Length <= 18) && lines.Count == 4, "a 200 px column holds 18 characters a line: " + string.Join(" | ", lines));
            foreach (string place in new[] { "top", "left", "right" })
            {
                d.Frame.Position = place;
                Check(ReadArt(new QrArt(d)) == d.Data, "a caption on the " + place + " should still read back");
            }
        }

        // Captions with CRs (from a design file saved elsewhere, or a batch CSV saved with CR LF line breaks) against
        // the website's own SVG files for them and Chromium's drawing of those files (WebsiteCaptions): the picture's
        // size; the caption's lines in b0r3d QR's SVG, each one's text character for character, CRs and all, and its x
        // and dy (b0r3d QR writes the website's numbers to 4 places); and the drawing the preview, PNG, JPG and PDF
        // are made from, where a browser draws the file's CRs as spaces: what's left on each line, where its first
        // character starts and on which baseline. Then the pictures' size, which the website takes from its preview,
        // where a CR is a space too.
        static void CaptionsWithCRsLikeTheWebsite()
        {
            var wrong = new List<string>();
            double furthest = 0;
            foreach (var c in WebsiteCaptions.Files)
            {
                var d = new QrDesign
                {
                    Data = "https://b0r3d.org",
                    Frame = new QrFrame { Text = c.Text, Position = c.Position, CaptionWidth = c.CaptionWidth, FontFamily = c.Font.Length > 0 ? c.Font : null, BorderWidth = "2px", BorderRadius = "8px", Padding = "16px" },
                };
                var art = new QrArt(d);
                string name = c.Position + " " + Show(c.Text) + (c.Font.Length > 0 ? " in " + c.Font : "");
                if (art.W != c.Width || art.H != c.Height)
                {
                    wrong.Add(name + ": " + art.W + " × " + art.H + ", the website's " + c.Width + " × " + c.Height);
                    continue;
                }
                string svg = art.ToSvg();
                Match text = Regex.Match(svg, "<text x=\"([^\"]*)\" y=\"([^\"]*)\"[^>]*>(.*?)</text>", RegexOptions.Singleline);
                MatchCollection spans = Regex.Matches(text.Groups[3].Value, "<tspan x=\"([^\"]*)\" dy=\"([^\"]*)\">(.*?)</tspan>", RegexOptions.Singleline);
                bool same = text.Success && text.Groups[1].Value == QrArt.N(c.TextX) && text.Groups[2].Value == QrArt.N(c.TextY) && spans.Count == c.Lines.Length;
                for (int i = 0; same && i < spans.Count; i++)
                    same = spans[i].Groups[1].Value == QrArt.N(c.LineX[i]) && spans[i].Groups[2].Value == QrArt.N(c.LineDy[i]) && spans[i].Groups[3].Value == c.Lines[i];
                if (!same)
                {
                    wrong.Add(name + ": the SVG's caption is " + Show(text.Groups[0].Value) + ", the website's lines " + string.Join(" | ", c.Lines.Select(Show)));
                    continue;
                }
                // the drawing: every line Chromium draws something of, at its place, and nothing else
                List<string> drawn = QrArt.DrawnLines(art.CaptionLines);
                List<GlyphRun> runs = GlyphRunsIn(art);
                int lines = 0;
                for (int i = 0; i < c.Lines.Length; i++)
                {
                    if (drawn[i].Length != c.Characters[i])
                    {
                        wrong.Add(name + ": line " + (i + 1) + " draws " + Show(drawn[i]) + ", Chromium " + c.Characters[i] + " characters");
                        continue;
                    }
                    if (c.Characters[i] == 0) continue;
                    lines++;
                    List<GlyphRun> on = runs.Where(r => Math.Abs(r.BaselineOrigin.Y - c.StartY[i]) < 0.001).ToList();
                    if (on.Count == 0)
                    {
                        if (drawn[i].Trim().Length > 0) wrong.Add(name + ": nothing drawn on line " + (i + 1) + "'s baseline, " + c.StartY[i]);
                        continue;
                    }
                    // a line is centred, so its start may be off by half as much as its width may be off from
                    // Chromium's (0.5 px, as FontTests allows); a space more or less moves it about 2.5 px
                    double gap = on.Min(r => r.BaselineOrigin.X) - c.StartX[i];
                    furthest = Math.Max(furthest, Math.Abs(gap));
                    if (Math.Abs(gap) > 0.25) wrong.Add(name + ": line " + (i + 1) + " starts at " + on.Min(r => r.BaselineOrigin.X) + ", Chromium's " + c.StartX[i]);
                }
                if (runs.Any(r => !c.StartY.Any(y => Math.Abs(r.BaselineOrigin.Y - y) < 0.001))) wrong.Add(name + ": a line drawn where Chromium draws none");
            }
            Console.WriteLine("      " + WebsiteCaptions.Files.Length + " of the website's SVG files; the drawing's lines within " + furthest.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + " px of Chromium's");
            Check(wrong.Count == 0, wrong.Count + " wrong: " + string.Join("; ", wrong.Take(8)));

            // The website saves PNG, JPG and PDF at the size that covers its preview, at the SVG's own shape. A CR
            // ending a line keeps it from taking more room there, a caption of CRs alone takes none, and a line that
            // only fits without its CR makes the SVG a line taller but not the preview.
            QrExport.Size Saved(string caption) => QrExport.PictureSize(new QrArt(new QrDesign
            {
                Data = "https://b0r3d.org",
                Frame = new QrFrame { Text = caption, BorderWidth = "2px", Padding = "16px" },
            }), null, false);
            string Pixels(string caption)
            {
                QrExport.Size s = Saved(caption);
                return s.Width + " × " + s.Height;
            }
            Check(Pixels("Scan me\r") == Pixels("Scan me") && Pixels("Scan me\r\nI dare you") == Pixels("Scan me\nI dare you"),
                "a CR at the end of a line should change nothing in the pictures' size: " + Pixels("Scan me\r") + ", " + Pixels("Scan me\r\nI dare you"));
            Check(Pixels("\r") == "236 × 273", "a caption of a CR alone should save at the website's 236 × 273 (its preview has no caption line), not " + Pixels("\r"));
            Check(Pixels("abcdefgh ijklmnopq\r\nx") == "236 × 316", "a line that only fits without its CR should save at the website's 236 × 316 (three lines in the SVG, two in its preview), not " +
                Pixels("abcdefgh ijklmnopq\r\nx"));
        }

        // print size: the website's numbers
        static void PrintSizeLikeTheWebsite()
        {
            Check(new PrintSize { On = true, Width = 40 }.PixelWidth == 472 && new PrintSize { On = true, Width = 2, Unit = "in" }.PixelWidth == 600, "40 mm and 2 in at 300 DPI should be 472 and 600 px");
            QrDesign d = Sample();
            d.Data = new string('a', 30);   // version 3 at Q: 29 modules
            QrArt art = new QrArt(d);
            Check(art.Code.Size == 29, "30 a's at Q should make 29 modules, not " + art.Code.Size);
            string line = QrExport.PrintLine(art, new PrintSize { On = true, Width = 37 }, out string warning);
            Check(line == "437 × 437 px. Each module is 1.00 mm; scans from up to about 29 cm." && warning == null, "37 mm: " + line);
            QrExport.PrintLine(art, new PrintSize { On = true, Width = 10 }, out warning);
            Check(warning != null && warning.StartsWith("Modules under about 0.40 mm", StringComparison.Ordinal), "10 mm should warn about small modules");
            Check(new PrintSize { On = true, Width = 2000 }.Problem != null && !new PrintSize { On = true, Width = 2000 }.Applies, "2000 mm should be refused");
            string svg = QrExport.Svg(art, new PrintSize { On = true, Width = 40 });
            Check(svg.Contains("width=\"40mm\" height=\"40mm\" viewBox=\"0 0 200 200\""), "a print size should make the SVG 40mm across, its drawing kept");
            string half = QrExport.PrintLine(new QrArt(Sample()), new PrintSize { On = true, Width = 16.5 }, out warning);
            Check(half.EndsWith("about 13 cm.", StringComparison.Ordinal), "12.5 cm should round up to 13, as the website rounds: " + half);
            QrDesign framed = Sample();
            framed.Frame = Presets.ApplyFrame("Default Frame", null);
            QrArt framedArt = new QrArt(framed);
            string framedLine = QrExport.PrintLine(framedArt, new PrintSize { On = true, Width = 40 }, out warning);
            Check(framedLine.StartsWith("472 × 548 px. Each module is 1.04 mm", StringComparison.Ordinal), "a framed code at 40 mm: " + framedLine);
            Check(QrExport.Svg(framedArt, new PrintSize { On = true, Width = 40 }).Contains("width=\"40mm\" height=\"46.427mm\""), "a framed SVG at 40 mm should be 46.427 mm tall");
        }

        // the files: PNG and JPG read back at the website's sizes, the PDF is the picture at its size on paper, text reads back
        static void ExportsLikeTheWebsite()
        {
            QrDesign d = Sample();
            QrArt art = new QrArt(d);
            byte[] pngBytes = QrExport.Png(QrExport.Picture(art, null, false));
            SaveTemp("design.png", pngBytes);
            var decoded = Images.FromBytes(pngBytes);
            Check(decoded.PixelWidth == 200 && decoded.PixelHeight == 200, "the PNG should be the website's 200 × 200, not " + decoded.PixelWidth + " × " + decoded.PixelHeight);
            Check(ReadPicture(decoded) == d.Data, "the PNG doesn't read back");
            byte[] jpg = QrExport.Jpg(QrExport.Picture(art, null, false, true));
            Check(jpg[0] == 0xFF && jpg[1] == 0xD8 && ReadPicture(Images.FromBytes(jpg)) == d.Data, "the JPG doesn't read back");
            QrDesign clear = d.Clone();
            clear.Background = "transparent";
            BitmapSource seeThrough = Images.FromBytes(QrExport.Jpg(QrExport.Picture(new QrArt(clear), null, false, true)));
            Check(Corner(seeThrough) == Colors.White, "a JPG of a see-through code should be on white");
            QrDesign halfClear = d.Clone();
            halfClear.Background = "#ffffff80";
            Check(Corner(QrExport.Picture(new QrArt(halfClear), null, false, true)) == Colors.White, "a JPG of a partly see-through background (#ffffff80) should have white paper under it");

            byte[] pdf = QrExport.Pdf(art, null, false, "b0r3d.org");
            string head = Encoding.ASCII.GetString(pdf, 0, Math.Min(pdf.Length, 4000));
            Check(head.StartsWith("%PDF-1.4", StringComparison.Ordinal) && head.Contains("/MediaBox [0 0 150 150]") && !head.Contains("/Subtype /Image") && head.Contains("0 0 200 200 re\n"),
                "the PDF should be 150 pt square (200 px at 96 to the inch) and drawn as vector shapes");
            Check(ReadPicture(PdfPicture(pdf)) == d.Data, "the PDF doesn't read back");

            foreach (QrExport.TextStyle style in new[] { QrExport.TextStyle.HalfBlocks, QrExport.TextStyle.FullBlocks, QrExport.TextStyle.Ascii })
            {
                string text = QrExport.Text(d, style);
                bool[,] grid = TextGrid(text, style);
                QrCode code = QrCode.Make(d.Data, d.Level);
                int n = code.Size + 8, wrong = 0;
                for (int r = 0; r < n; r++)
                    for (int c = 0; c < n; c++)
                        if (grid[r, c] != (r >= 4 && c >= 4 && r < n - 4 && c < n - 4 && code[r - 4, c - 4])) wrong++;
                Check(wrong == 0 && !text.EndsWith("\n", StringComparison.Ordinal), style + ": " + wrong + " modules wrong in the text");
            }
            Check(QrExport.Markdown("x") == "```\nx\n```\n", "the .md file should fence the text");
            Check(pdf[9] == '%' && pdf[10] == 0xE2 && pdf[13] == 0xD3, "the PDF's second line should mark it as binary");

            // print size: its DPI in the PNG and the JPG, the PDF's page the width on paper
            var print = new PrintSize { On = true, Width = 40 };
            byte[] printed = QrExport.Png(QrExport.Picture(art, print, false));
            int phys = Find(printed, "pHYs");
            Check(phys > 0 && BigEndian(printed, phys + 4) == 11811 && BigEndian(printed, phys + 8) == 11811 && printed[phys + 12] == 1, "a 300 DPI PNG should say 11811 pixels a metre");
            byte[] printedJpg = QrExport.Jpg(QrExport.Picture(art, print, false, true));
            Check(Find(printedJpg, "JFIF") == 6 && printedJpg[13] == 1 && printedJpg[14] * 256 + printedJpg[15] == 300 && printedJpg[16] * 256 + printedJpg[17] == 300, "a 300 DPI JPG should say 300 dots an inch");
            Check(Encoding.ASCII.GetString(QrExport.Pdf(art, print, false, "x"), 0, 2000).Contains("/MediaBox [0 0 113.39 113.39]"), "a 40 mm PDF should be 113.39 pt across");
            string clearPdf = Encoding.GetEncoding(28591).GetString(QrExport.Pdf(new QrArt(clear), null, false, "x"));
            Check(!clearPdf.Contains("/Subtype /Image") && !clearPdf.Contains("1 1 1 rg\n0 0 200 200 re"), "a see-through code's PDF should draw only the modules, with no background rectangle");
            QrArt cupArt = new QrArt(Presets.Apply("b0r3d Cup", d.Clone()));
            byte[] cupPdf = QrExport.Pdf(cupArt, null, false, "cup");
            string cupPdfText = Encoding.GetEncoding(28591).GetString(cupPdf);
            Check(cupPdfText.Contains("/Subtype /Image") && cupPdfText.Contains("/SMask ") && ReadPicture(PdfPicture(cupPdf)) == d.Data,
                "a raster logo's PDF should embed the logo picture with a soft mask alongside the vector modules and read back");
            QrDesign round = d.Clone();
            round.BorderRadius = 16;
            Check(new QrArt(round).ToSvg().Contains("<clipPath id=\"qr-export-clip\"><rect width=\"200\" height=\"200\" rx=\"16\" ry=\"16\"/></clipPath>"), "rounded corners should clip the SVG as the website's does");

            // too big a picture is refused with a reason, not drawn until memory runs out
            QrDesign huge = d.Clone();
            huge.Width = huge.Height = 4000;
            huge.Frame = Presets.ApplyFrame("Default Frame", null);
            huge.Frame.Text = string.Join(" ", Enumerable.Repeat("word", 200));
            try
            {
                QrExport.Picture(new QrArt(huge), null, false);
                Check(false, "a picture of over a billion pixels should be refused");
            }
            catch (QrException e)
            {
                Check(e.Message.StartsWith("That picture would be ", StringComparison.Ordinal), e.Message);
            }

            // text: a logo doesn't raise the level, except with the marker (the website then uses the picture's code)
            QrDesign logoAtL = Presets.Apply("b0r3d Cup", d.Clone()), plainAtL = d.Clone();
            logoAtL.Level = plainAtL.Level = QrLevel.L;
            Check(QrExport.Text(logoAtL, QrExport.TextStyle.FullBlocks) == QrExport.Text(plainAtL, QrExport.TextStyle.FullBlocks), "text with a logo at L should stay at L");
            QrDesign plainAtQ = d.Clone();
            logoAtL.Utf8Marker = plainAtQ.Utf8Marker = true;
            plainAtQ.Level = QrLevel.Q;
            Check(QrExport.Text(logoAtL, QrExport.TextStyle.FullBlocks) == QrExport.Text(plainAtQ, QrExport.TextStyle.FullBlocks), "marked text with a logo at L should be the picture's code, at Q");
        }

        static int Find(byte[] bytes, string ascii)
        {
            byte[] what = Encoding.ASCII.GetBytes(ascii);
            for (int i = 0; i + what.Length <= bytes.Length; i++)
                if (what.Select((b, k) => bytes[i + k] == b).All(x => x)) return i;
            return -1;
        }

        static long BigEndian(byte[] b, int at) => ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3];

        static string SaveTemp(string name, byte[] bytes)
        {
            string path = Path.Combine(Work, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        static string ReadPicture(BitmapSource picture)
        {
            byte[] grays = ScanCheck.Grays(picture, out int w, out int h);
            return ScanCheck.Read(grays, w, h)?.Text;
        }

        static Color Corner(BitmapSource picture)
        {
            var bgra = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
            var px = new byte[4];
            bgra.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), px, 4, 0);
            return Color.FromArgb(px[3], px[2], px[1], px[0]);
        }

        // the first page of one of b0r3d QR's PDFs, rendered by Windows' PDF reader
        static BitmapSource PdfPicture(byte[] pdf) => PdfFile.ReadPicture(pdf);

        // the text export back into modules (true = dark)
        static bool[,] TextGrid(string text, QrExport.TextStyle style)
        {
            string[] lines = text.Split('\n');
            if (style == QrExport.TextStyle.HalfBlocks)
            {
                int n = lines[0].Length;
                var grid = new bool[n, n];
                for (int i = 0; i < lines.Length; i++)
                    for (int c = 0; c < n; c++)
                    {
                        char ch = lines[i][c];
                        if (2 * i < n) grid[2 * i, c] = ch == '█' || ch == '▀';
                        if (2 * i + 1 < n) grid[2 * i + 1, c] = ch == '█' || ch == '▄';
                    }
                return grid;
            }
            int size = lines.Length;
            var cells = new bool[size, size];
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                {
                    string pair = lines[r].Substring(2 * c, 2);
                    cells[r, c] = style == QrExport.TextStyle.FullBlocks ? pair == "██" : pair == "  ";
                }
            return cells;
        }

        // design files: the website's example opens, ours open on the website's terms, refusals in its words
        static void DesignFilesLikeTheWebsite()
        {
            const string example = "{\"schemaVersion\":1,\"props\":{\"data\":\"https://b0r3d.org\",\"image\":\"\",\"width\":200,\"height\":200,\"margin\":4," +
                "\"dotsOptions\":{\"color\":\"#000000\",\"type\":\"square\"},\"cornersSquareOptions\":{\"color\":\"#000000\",\"type\":\"square\"}," +
                "\"cornersDotOptions\":{\"color\":\"#000000\",\"type\":\"square\"},\"imageOptions\":{\"padding\":1},\"qrOptions\":{\"errorCorrectionLevel\":\"Q\",\"typeNumber\":0}}," +
                "\"style\":{\"borderRadius\":\"0px\",\"background\":\"#ffffff\"},\"frame\":null}";
            QrDesign read = DesignFile.Read(example, new QrDesign());
            Check(read.Data == "https://b0r3d.org" && read.Width == 200 && read.Margin == 4 && read.DotsType == "square" && read.Level == QrLevel.Q && read.Frame == null && read.LogoSize == null,
                "the website's example design didn't open as it should");

            QrDesign cup = Presets.Apply("b0r3d Cup", Sample());
            cup.Frame = Presets.ApplyFrame("Dark Frame", null);
            cup.Frame.Text = "Two\nlines";
            cup.Utf8Marker = true;
            cup.LogoSize = 0.3;
            string written = DesignFile.Write(cup);
            QrDesign back = DesignFile.Read(written, new QrDesign());
            Check(DesignFile.Write(back) == written, "a design written and read back should write the same");
            Check(Regex.IsMatch(written, "^\\{\\s*\"schemaVersion\": 1,\\s*\"useEci26\": true,\\s*\"props\""), "the file should start with schemaVersion 1, then useEci26 when it's on");
            foreach (string key in new[] { "\"dotsOptions\"", "\"cornersSquareOptions\"", "\"cornersDotOptions\"", "\"imageOptions\"", "\"qrOptions\"", "\"borderRadius\": \"16px\"", "\"captionWidth\": 200" })
                Check(written.Contains(key), "the file should always have " + key);
            Check(written.Contains(Presets.LogoUrl("b0r3d-cup.png")), "the preset's logo should be saved as its data: URL");
            string withoutData = DesignFile.Write(cup, false);
            Check(!withoutData.Contains("\"data\""), "the kept design shouldn't hold the text");

            void Refused(string json, string message, string why)
            {
                try
                {
                    DesignFile.Read(json, new QrDesign());
                    Check(false, why + " should be refused");
                }
                catch (DesignFileException e)
                {
                    Check(e.Message == message, why + ": \"" + e.Message + "\"");
                }
            }
            Refused("{\"schemaVersion\":2,\"props\":{},\"style\":{\"borderRadius\":\"0px\"}}", DesignFile.Newer, "a newer file");
            Refused("not json", DesignFile.NotADesign, "not JSON");
            Refused("{\"props\":{},\"style\":{}}", DesignFile.NotADesign, "a style without borderRadius");
            Refused("{\"props\":{\"image\":\"javascript:alert(1)\"},\"style\":{\"borderRadius\":\"0px\"}}", DesignFile.NotADesign, "a javascript: logo");
            Refused("{\"props\":{\"image\":\"\\u0001javascript:alert(1)\"},\"style\":{\"borderRadius\":\"0px\"}}", DesignFile.NotADesign, "a logo with a control character before javascript:");
            Refused("{\"props\":{\"image\":\"java\\tscript:alert(1)\"},\"style\":{\"borderRadius\":\"0px\"}}", DesignFile.NotADesign, "a logo with a tab inside javascript:");
            Refused("{\"props\":{},\"style\":{\"borderRadius\":\"0px\"},\"frame\":{\"text\":\"x\",\"position\":\"middle\",\"style\":{}}}", DesignFile.NotADesign, "a frame in the middle");
            Check(Encoding.UTF8.GetBytes(DesignFile.Newer).Length > DesignFile.Newer.Length && DesignFile.Newer.Contains("’"), "the website's messages use a typographic apostrophe");

            QrDesign legacy = DesignFile.Read("{\"props\":{\"data\":\"old\",\"imageOptions\":{\"imageSize\":\"\"}},\"style\":{\"borderRadius\":\"\"}}", new QrDesign());
            Check(legacy.Data == "old" && legacy.LogoSpace == 1 && legacy.Margin == 4 && legacy.LogoSize == 0 && legacy.CornersSquareType == "extra-rounded" && legacy.BorderRadius == 0,
                "a file from before version 1 should open with the website's defaults for what's missing, and an emptied Image size as 0, as the website's sums take it");

            // emptied number fields are saved as "", which the website draws as 0
            QrDesign emptied = DesignFile.Read("{\"schemaVersion\":null,\"useEci26\":null,\"props\":{\"margin\":\"\",\"imageOptions\":{\"padding\":\"\"}},\"style\":{\"borderRadius\":\"0px\"}}", new QrDesign());
            Check(emptied.Margin == 0 && emptied.LogoSpace == 0 && !emptied.Utf8Marker, "emptied Margin and Logo space fields should open as 0, and a null schemaVersion and useEci26 as not there");
            QrDesign cupEmptied = Presets.Apply("b0r3d Cup", Sample());
            cupEmptied.LogoSpace = 0;
            Check(new QrArt(cupEmptied).LogoModules == 7, "the b0r3d Cup with no logo space should have a 7 × 7 logo, as on the website");

            // odd values: not numbers, a huge width, CSS's grey spellings, lengths the website's browser takes
            QrDesign odd = DesignFile.Read("{\"props\":{\"margin\":\"NaN\",\"width\":\"Infinity\"},\"style\":{\"borderRadius\":\"0px\"},\"frame\":{\"text\":\"x\",\"position\":\"top\",\"captionWidth\":1e10," +
                "\"style\":{\"textColor\":\"grey\",\"backgroundColor\":\"lightgrey\",\"borderColor\":\"DarkSlateGrey\",\"borderWidth\":\"\",\"borderRadius\":\"auto\",\"padding\":\"1e1px\"}}}", new QrDesign());
            Check(odd.Margin == 0 && odd.Width == 200 && odd.Frame.CaptionWidth == 600,
                "\"NaN\" and \"Infinity\" shouldn't be numbers, and a huge caption width should be the widest: " + odd.Margin + ", " + odd.Width + ", " + odd.Frame.CaptionWidth);
            Check(Css.Colour("grey") == Color.FromRgb(128, 128, 128) && Css.Colour("LightGrey") == Css.Colour("lightgray") && Css.Colour("rebeccapurple") == Color.FromRgb(0x66, 0x33, 0x99),
                "CSS's colour names should be read as browsers read them");
            Check(new QrArt(new QrDesign { Data = "x", Frame = odd.Frame }).ToSvg().Contains("stroke-width=\"2\""), "an empty border width should draw the website's 2 px");
            Check(DesignFile.Write(odd).Contains("\"borderWidth\": \"2px\""), "an empty length should be saved as what it draws, which the website opens");
        }

        // the colour and quiet-zone warnings, and the data checks, in the website's words
        static void ChecksLikeTheWebsite()
        {
            QrDesign d = Sample();
            Check(Checks.Warnings(d).Count == 0, "black on white with a 4-module quiet zone shouldn't warn");
            Check(Checks.Warnings(Presets.Apply("b0r3d Cup", d)).Count == 0 && Checks.Warnings(Presets.Apply("Rukus", d)).Count == 0, "the brand presets shouldn't warn");
            QrDesign inverted = d.Clone();
            inverted.Background = "#000000";
            inverted.DotsColor = inverted.CornersSquareColor = inverted.CornersDotColor = "#ffffff";
            Check(Checks.Warnings(inverted).Any(w => w.StartsWith("Light code on a dark background", StringComparison.Ordinal)), "light on dark should warn");
            QrDesign grey = d.Clone();
            grey.DotsColor = "#aaaaaa";
            Check(Checks.Warnings(grey).Any(w => w.StartsWith("Low contrast (2.3:1)", StringComparison.Ordinal)), "grey on white: " + string.Join(" | ", Checks.Warnings(grey)));
            QrDesign red = d.Clone();
            red.DotsColor = "#ff0000";
            Check(Checks.Warnings(red).Any(w => w.StartsWith("Low contrast (3.9:1)", StringComparison.Ordinal)), "red on white (3.998:1) should round down to 3.9:1: " + string.Join(" | ", Checks.Warnings(red)));
            System.Globalization.CultureInfo wasCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
                Check(Checks.BareDomain("WWW.INFO.COM") && Css.Colour("INDIGO") != null, "Turkish Windows: capital I in domain and colour names");
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = wasCulture;
            }
            QrDesign clear = d.Clone();
            clear.Background = "transparent";
            clear.Margin = 2;
            List<string> two = Checks.Warnings(clear);
            Check(two.Count == 2 && two[0].StartsWith("Transparent background", StringComparison.Ordinal) && two[1].StartsWith("Only 2 modules", StringComparison.Ordinal), "see-through with 2 modules: " + string.Join(" | ", two));

            foreach (string yes in new[] { "b0r3d.org", "www.example.com", "b0r3d.org/page?a=1#top", "shop.example.co.uk:8080/x" })
                Check(Checks.BareDomain(yes), yes + " should look like an address without https://");
            foreach (string no in new[] { "https://b0r3d.org", "mailto:a@b0r3d.org", "a@b0r3d.org", "hello world", "WIFI:S:x;;", "tel:+15555550123", "tel:5555550123", "v1.2.3", "notes.txt is here" })
                Check(!Checks.BareDomain(no), no + " shouldn't look like an address without https://");
            List<string> names = Checks.TrackingNames("https://b0r3d.org/p?a=1&utm_medium=qr&b=%20x&gclid=9#frag", out string clean);
            Check(clean == "https://b0r3d.org/p?a=1&b=%20x#frag" && names.SequenceEqual(new[] { "utm_medium", "gclid" }), "tracking removed: " + clean);
            Check(Checks.TrackingNames("https://youtu.be/x?si=abc", out string yt).Count == 1 && yt == "https://youtu.be/x" && Checks.TrackingNames("https://b0r3d.org/?si=1", out string _).Count == 0,
                "si is tracking on YouTube, not elsewhere");
            // and the website's own findings (WebsiteLinkChecks.cs), names in small letters as toLowerCase makes them
            var links = new List<string>();
            foreach (var t in WebsiteTrackingChecks)
            {
                List<string> found = Checks.TrackingNames(t.Text, out string without);
                if (!found.SequenceEqual(t.Tracking) || without != t.Clean)
                    links.Add(Show(t.Text) + ": [" + string.Join(", ", found) + "] " + Show(without) + " (the website: [" + string.Join(", ", t.Tracking) + "] " + Show(t.Clean) + ")");
            }
            Check(links.Count == 0, links.Count + " of " + WebsiteTrackingChecks.Length + " links' tracking differs from the website's: " + string.Join(" | ", links.Take(3)));
            List<Checks.DataCheck> all = Checks.DataChecks("  b0r3d.org ");
            Check(all.Count == 2 && all[0].Fixed == "b0r3d.org" && all[1].Fixed == "https://b0r3d.org", "spaces around a bare address: " + string.Join(" | ", all.Select(c => c.Button)));
            Check(Checks.DataChecks("  https://b0r3d.org/?utm_source=qr ").Any(c => c.Button == "Remove them" && c.Fixed == "https://b0r3d.org/"), "tracking in a link with spaces around it should be found, as the website trims first");

            Check(two[1].Contains("Leave at least 4 (Quiet zone)"), "the quiet-zone warning should name the Quiet zone box: " + two[1]);
            // a see-through frame colour still counts, over white paper
            QrDesign framed = clear.Clone();
            framed.Frame = Presets.ApplyFrame("Default Frame", null);
            framed.Frame.BackgroundColor = "rgba(0, 0, 0, 0.7)";
            List<string> dim = Checks.Warnings(framed);
            Check(dim.Count == 1 && dim[0].StartsWith("Low contrast (2.4:1)", StringComparison.Ordinal), "black on a 70 % black frame: " + string.Join(" | ", dim));
        }
    }
}
