using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Design;
using B0r3dQR.Qr;
using B0r3dQR.Svg;
using Style = B0r3dQR.Svg.Style;

namespace B0r3dQR.Tests
{
    // SVG logos and frame pictures: b0r3d QR's own SVG reader, and the pictures it draws held against
    // what Chromium draws for the website (ChromiumSvgPictures.cs).
    static partial class TestMain
    {
        // How near is near enough: antialiasing differs a little between WPF and Chromium (each works out
        // how much of an edge pixel a shape covers in its own way), so pixels on edges can be off by a few
        // dozen levels; a shape a pixel out of place leaves a line of pixels off by far more. A pixel is
        // wrong when a channel is off by more than WrongBy (of 255, premultiplied); a picture passes with
        // at most Wrong of its pixels wrong (Text for text: fonts are hinted differently), and with what's
        // drawn (alpha over half) starting and ending within Off pixels of Chromium's at 1 px a px.
        const int WrongBy = 96;
        const double Wrong = 0.006, TextWrong = 0.03;
        const int Off = 1;

        static void SvgPicturesLikeChromium()
        {
            var problems = new List<string>();
            string dump = Environment.GetEnvironmentVariable("B0R3DQR_SVG_PICTURES");   // a folder for both pictures of each case, when working on the drawing
            var report = new StringBuilder();
            int compared = 0;
            foreach (var (name, svg, pictures) in ChromiumSvgPictures.Cases)
            {
                ImageSource picture = Images.Drawing(Encoding.UTF8.GetBytes(svg), null, out string why);
                if (picture == null)
                {
                    problems.Add(name + ": " + why);
                    continue;
                }
                for (int i = 0; i < ChromiumSvgPictures.Layouts.Length; i++)
                {
                    var layout = ChromiumSvgPictures.Layouts[i];
                    BitmapSource ours = InBox(picture, layout.OuterWidth, layout.OuterHeight, new Rect(10, 10, layout.Width, layout.Height), layout.Fit.EndsWith("slice", StringComparison.Ordinal), layout.Scale);
                    BitmapSource theirs = Images.FromBytes(Convert.FromBase64String(pictures[i]));
                    Difference d = Differ(ours, theirs);
                    compared++;
                    bool text = name.StartsWith("text:", StringComparison.Ordinal);
                    double allowed = (text ? TextWrong : Wrong) * d.Pixels;
                    int off = (int)Math.Ceiling(Off * layout.Scale) + (text ? 1 : 0);
                    string line = name + " [" + layout.Width + "x" + layout.Height + " " + layout.Fit + " at " + layout.Scale + "]: " + d.Wrong + " of " + d.Pixels + " pixels wrong (" +
                                  (100.0 * d.Wrong / d.Pixels).ToString("0.00") + " %), drawn " + d.Ours + " where Chromium drew " + d.Theirs;
                    report.AppendLine(line);
                    if (d.Wrong > allowed || !Near(d.Ours, d.Theirs, off)) problems.Add(line);
                    if (dump != null)
                    {
                        string file = Path.Combine(dump, Safe(name) + "-" + i);
                        File.WriteAllBytes(file + "-ours.png", Qr.QrExport.Png(ours));
                        File.WriteAllBytes(file + "-chromium.png", Qr.QrExport.Png(theirs));
                    }
                }
            }
            if (dump != null) File.WriteAllText(Path.Combine(dump, "report.txt"), report.ToString());
            Check(problems.Count == 0, problems.Count + " of " + compared + " differ from " + ChromiumSvgPictures.Browser + ":\n  " + string.Join("\n  ", problems.Take(25)));
            Check(compared == ChromiumSvgPictures.Cases.Length * ChromiumSvgPictures.Layouts.Length && compared >= 150, "only " + compared + " pictures were compared");

            // the comparison can tell: a picture with no viewBox put in the box by its own size (meet),
            // as other programs do, isn't what Chromium draws (it stretches it to the box)
            var stretched = ChromiumSvgPictures.Cases.First(c => c.Name.StartsWith("size: width and height, no viewBox", StringComparison.Ordinal));
            var natural = (DrawingImage)Images.Drawing(Encoding.UTF8.GetBytes(stretched.Svg), null, out _);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double k = Math.Min(100 / natural.Width, 100 / natural.Height);
                dc.DrawImage(natural, new Rect(10 + (100 - natural.Width * k) / 2, 10 + (100 - natural.Height * k) / 2, natural.Width * k, natural.Height * k));
            }
            var wrong = new RenderTargetBitmap(120, 120, 96, 96, PixelFormats.Pbgra32);
            wrong.Render(visual);
            Difference unlike = Differ(wrong, Images.FromBytes(Convert.FromBase64String(stretched.Pictures[0])));
            Check(unlike.Wrong > Wrong * unlike.Pixels && !Near(unlike.Ours, unlike.Theirs, Off), "a picture placed by its own size should differ from Chromium's: " + unlike.Wrong + " pixels wrong");
        }

        static string SvgUrl(string svg) => "data:image/svg+xml;base64," + Convert.ToBase64String(new UTF8Encoding(false).GetBytes(svg));

        // a logo the way design programs write one: a style sheet with classes, a gradient, a path and text
        // (#c08040 in its middle)
        const string TestLogo = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- a test logo -->\n<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\">" +
            "<defs><style>.cls-1{fill:url(#g)}.cls-2{fill:#c08040}</style><linearGradient id=\"g\" y2=\"1\" x2=\"0\"><stop offset=\"0\" stop-color=\"#c000c0\"/>" +
            "<stop offset=\"1\" stop-color=\"#600060\"/></linearGradient></defs><circle class=\"cls-1\" cx=\"32\" cy=\"32\" r=\"30\"/><rect class=\"cls-2\" x=\"20\" y=\"20\" width=\"24\" height=\"24\" rx=\"4\"/></svg>";

        static Color PixelAt(BitmapSource picture, int x, int y)
        {
            var bgra = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
            var px = new byte[4];
            bgra.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
            return Color.FromArgb(px[3], px[2], px[1], px[0]);
        }

        static bool Like(Color a, Color b, int by = 6) => Math.Abs(a.R - b.R) <= by && Math.Abs(a.G - b.G) <= by && Math.Abs(a.B - b.B) <= by;

        // b0r3d QR's SVG reader: XML as browsers read it, path data, colours and CSS
        static void SvgReaderLikeABrowser()
        {
            SvgImage Svg(string xml) => SvgImage.Read(Encoding.UTF8.GetBytes(xml));
            string Refused(string xml)
            {
                try
                {
                    SvgImage.Read(Encoding.UTF8.GetBytes(xml));
                    return null;
                }
                catch (SvgException x)
                {
                    return x.Message;
                }
            }
            const string NS = "xmlns=\"http://www.w3.org/2000/svg\"";

            // XML: entities declared in the DOCTYPE (an Illustrator file's namespaces), numbered characters, CDATA,
            // comments and processing instructions; refusals with the line
            SvgImage ai = Svg("<?xml version=\"1.0\"?>\n<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"x.dtd\" [\n<!ENTITY ns_svg \"http://www.w3.org/2000/svg\">\n<!ENTITY ns_ai \"http://ns.adobe.com/AdobeIllustrator/10.0/\">\n" +
                              "<!ENTITY two \"&#50;\"><!ENTITY both \"&two;&two;\">]>\n<svg xmlns=\"&ns_svg;\" xmlns:i=\"&ns_ai;\" width=\"&both;\"><?pi x?><!-- c --><style><![CDATA[ a > b ]]></style>" +
                              "<switch><foreignObject requiredExtensions=\"&ns_ai;\"><i:pgf/></foreignObject><g i:extraneous=\"self\"><text>&lt;&amp;&#x41;&#66;</text></g></switch></svg>");
            Check(ai.Root["width"] == "22" && ai.NaturalSize.Width == 22, "entities inside entities and numbered characters: width " + ai.Root["width"]);
            Element text = ai.Root.Children[1].Children[1].Children[0];
            Check(text.Name == "text" && (string)text.Content[0] == "<&AB", "the five entities and numbered characters in text: " + text.Content[0]);
            Check(ai.Root.Children[0].Content[0] as string == " a > b ", "CDATA is text as it is");
            var bytes = new List<byte>(Encoding.ASCII.GetBytes("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><svg " + NS + "><text>caf"));
            bytes.Add(0xE9);
            bytes.AddRange(Encoding.ASCII.GetBytes("</text></svg>"));
            Check((string)SvgImage.Read(bytes.ToArray()).Root.Children[0].Content[0] == "caf" + (char)0xE9, "a file in ISO-8859-1 says so, and is read that way");
            Check(SvgImage.Read(new UnicodeEncoding(false, true).GetPreamble().Concat(Encoding.Unicode.GetBytes("<svg " + NS + "/>")).ToArray()).Root.Name == "svg", "UTF-16 with its byte order mark");
            foreach (var (xml, why) in new[]
            {
                ("<svg " + NS + "><text>&nbsp;</text></svg>", "isn't defined"),
                ("<svg " + NS + "><g></svg>", "doesn't match"),
                ("<svg " + NS + " a=\"1\" a=\"2\"/>", "there twice"),
                ("<svg " + NS + " a=1/>", "in quotes"),
                ("<svg " + NS + " a=\"<\"/>", "has a <"),
                ("<svg/>", "has no xmlns"),
                ("<html xmlns=\"http://www.w3.org/1999/xhtml\"><svg " + NS + "/></html>", "isn't an SVG drawing (it's html)"),
                ("<svg " + NS + "/><svg " + NS + "/>", "follows the end of the drawing"),
                ("<svg " + NS + "><text>&#0;</text></svg>", "isn't a character"),
                ("<!DOCTYPE svg [<!ENTITY far SYSTEM \"file:///c:/windows/win.ini\">]><svg " + NS + "><text>&far;</text></svg>", "in another file"),
                ("", "empty"),
                ("<svg " + NS + ">\n\n<g>", "isn't closed"),
            })
            {
                string refused = Refused(xml);
                Check(refused != null && refused.Contains(why), "\"" + Short(xml) + "\" should be refused (" + why + "), not: " + (refused ?? "read"));
            }
            Check(Refused("<svg " + NS + ">\n\n<g></svg>").EndsWith("(line 3)", StringComparison.Ordinal), "a refusal says which line");
            // ...and a name or value from the file that it quotes is cut short: a name of a letter with 1,000 accents
            // (which ends the program where it's shown) comes back as its first few characters, the message a line
            string marked = "a" + new string((char)0x301, 1000), kept = "a" + new string((char)0x301, 36) + "...";
            foreach (var (xml, why) in new[]
            {
                ("<svg " + NS + "><" + marked + "></svg>", "doesn't match"),
                ("<svg " + NS + "><" + marked + ">", "isn't closed"),
                ("<svg " + NS + " " + marked + "=\"1\" " + marked + "=\"2\"/>", "there twice"),
                ("<svg " + NS + " " + marked + "/>", "has no ="),
                ("<svg " + NS + " " + marked + "=1/>", "in quotes"),
                ("<!DOCTYPE svg [<!ENTITY e \"&" + marked + ";\">]><svg " + NS + "><text>&e;</text></svg>", "isn't defined"),
                ("<!DOCTYPE svg [<!ENTITY e \"&" + marked + ";\"><!ENTITY " + marked + " SYSTEM \"x\">]><svg " + NS + "><text>&e;</text></svg>", "in another file"),
                ("<!DOCTYPE svg [<!ENTITY e \"&" + marked + ";\"><!ENTITY " + marked + " \"<g/>\">]><svg " + NS + "><text>&e;</text></svg>", "holds markup"),
                ("<!DOCTYPE svg [<!ENTITY e \"&" + marked + ";\"><!ENTITY " + marked + " \"&x\">]><svg " + NS + "><text>&e;</text></svg>", "isn't an entity"),
                ("<!DOCTYPE svg [<!ENTITY e \"&#" + marked + ";\">]><svg " + NS + "/>", "isn't a character"),
                ("<" + marked + " " + NS + "/>", "isn't an SVG drawing"),
            })
            {
                string refused = Refused(xml);
                Check(refused != null && refused.Contains(why) && refused.Contains(kept) && refused.Length < 120 && !Unicode.OverTheLimit(refused, 36),
                      "a refusal (" + why + ") should quote a long name cut short, not " + (refused == null ? "read" : refused.Length + " characters: " + Short(refused)));
            }

            // path data as Chromium reads it: implicit commands, numbers packed together, flags without spaces,
            // exponents, and everything before a mistake
            foreach (var (d, same) in new[]
            {
                ("M10 10 20 10 20 20z", "M10,10 L20,10 L20,20 Z"),
                ("M1.5.5.5 1", "M1.5,0.5 L0.5,1"),
                ("M.5-.5l1e1 0", "M0.5,-0.5 L10.5,-0.5"),
                ("M0 0 L10 10 X 20 20", "M0,0 L10,10"),
                ("M0 0 L10 0 L10", "M0,0 L10,0"),
                ("M 10,10 L 20,20,30,30", "M10,10 L20,20 L30,30"),
                ("m10 10h5v5h-5z", "M10,10 L15,10 L15,15 L10,15 Z"),
                ("M0 0 Q5 10 10 0 T20 0", "M0,0 Q5,10 10,0 Q15,-10 20,0"),
                ("M0 0 C0 10 10 10 10 0 S20 -10 20 0", "M0,0 C0,10 10,10 10,0 C10,-10 20,-10 20,0"),
                ("M0 0 S10 10 20 0", "M0,0 C0,0 10,10 20,0"),
                ("L10 10", ""),
            })
            {
                string ours = Figures.FromPathData(d).IsEmpty ? "" : PathGeometry.CreateFromGeometry(Figures.FromPathData(d).ToGeometry(FillRule.EvenOdd)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                string theirs = same.Length == 0 ? "" : PathGeometry.CreateFromGeometry(Geometry.Parse(same)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Check(ours == theirs, "\"" + d + "\" should be read as \"" + same + "\": " + ours + " / " + theirs);
            }
            Rect arc = Figures.FromPathData("M0 0a5 5 0 1110 0").ToGeometry(FillRule.Nonzero).Bounds;
            Check(Math.Abs(arc.X) < 1e-6 && Math.Abs(arc.Y + 5) < 1e-3 && Math.Abs(arc.Width - 10) < 1e-6 && Math.Abs(arc.Height - 5) < 1e-3, "an arc's flags written together (1110): " + arc);
            Rect grown = Figures.FromPathData("M0 0A1 1 0 0 0 20 0").ToGeometry(FillRule.Nonzero).Bounds;
            Check(Math.Abs(grown.Height - 10) < 1e-3, "an arc's radius too small to reach is grown: " + grown);

            // colours and paint
            Check(Paint.TryParse("url(#g) #e63a", out Paint p) && p.Kind == Paint.Kinds.Url && p.Id == "g" && p.Fallback == Paint.Kinds.Colour && p.FallbackColour == Color.FromArgb(0xAA, 0xEE, 0x66, 0x33),
                "a gradient with a colour to fall back on");
            Check(Paint.TryParse("url('#a b') currentColor", out p) && p.Id == "a b" && p.Fallback == Paint.Kinds.Current && Paint.TryParse("none", out p) && p.Kind == Paint.Kinds.None &&
                  !Paint.TryParse("nonsense", out _) && Paint.TryParse("url(other.svg#g)", out p) && p.Id == null, "currentColor, none, a mistake, and a gradient in another file");
            foreach (var (css, argb) in new[] { ("rgb(100% 50% 0%)", 0xFFFF8000u), ("hsla(120 100% 25% / 0.5)", 0x80008000u), ("lightgrey", 0xFFD3D3D3u), ("rebeccapurple", 0xFF663399u), ("transparent", 0u) })
            {
                Color? c = Design.Css.Colour(css);
                uint got = c == null ? 1 : (uint)(c.Value.A << 24 | c.Value.R << 16 | c.Value.G << 8 | c.Value.B);
                Check(got == argb, css + " should be " + argb.ToString("X8") + ", not " + got.ToString("X8"));
            }

            // CSS: selectors, specificity, !important, @media, comments, inherit
            SvgImage sheet = Svg("<svg " + NS + "><style>/* x */ @media print { rect { fill: red } } @media screen { .m { fill: #010101 } } rect:not(.x) { stroke: #020202 } " +
                               "g > rect:first-child { stroke-width: 3 } rect:nth-child(2n) { stroke-opacity: 0.5 } [data-k=\"v\"] { fill-opacity: 0.25 } #i.c { fill: #030303 !important } " +
                               "svg rect + rect { stroke-linecap: round } a::before { fill: blue } rect:hover { fill: blue } .c { fill: #040404 }</style>" +
                               "<g fill=\"#050505\"><rect class=\"m\"/><rect id=\"i\" class=\"c x\" data-k=\"v\" style=\"fill:#060606\"/><rect style=\"fill:inherit\"/></g></svg>");
            Style g = Style.Of(sheet.Root.Children[1], Style.Of(sheet.Root, Style.Initial, sheet.Sheet), sheet.Sheet);
            Style r1 = Style.Of(sheet.Root.Children[1].Children[0], g, sheet.Sheet), r2 = Style.Of(sheet.Root.Children[1].Children[1], g, sheet.Sheet), r3 = Style.Of(sheet.Root.Children[1].Children[2], g, sheet.Sheet);
            Check(r1.Fill.Colour == Color.FromRgb(1, 1, 1) && r1.Stroke.Colour == Color.FromRgb(2, 2, 2) && r1.StrokeWidth.Value == 3 && r1.LineCap == PenLineCap.Flat,
                "@media screen applies and print doesn't; :not(); > and :first-child");
            Check(r2.Fill.Colour == Color.FromRgb(3, 3, 3) && r2.Stroke.Kind == Paint.Kinds.None && r2.StrokeOpacity == 0.5 && r2.FillOpacity == 0.25 && r2.LineCap == PenLineCap.Round,
                "!important beats the style attribute; :not(.x) leaves it out; :nth-child(2n), [attr=value] and + apply");
            Check(r3.Fill.Colour == Color.FromRgb(5, 5, 5), "fill: inherit takes the group's");
            // numbers as CSS has them, where .NET reads more: NaN and Infinity aren't numbers, nor are "5.", "50 %" and
            // "0.5px" opacities, so they're left out as in Chromium (the parent's, or the one before, stays: WPF took a NaN
            // opacity for none at all, a stop of NaN for see-through, and drew no stroke with a miter limit of Infinity);
            // an opacity past a float's range is as big as a float goes, so 1e400 is 1 and -1e400 is 0
            SvgImage numbers = Svg("<svg " + NS + "><g fill-opacity=\"0.25\" stroke-opacity=\"25%\" stroke-miterlimit=\"1.9\"><rect fill-opacity=\"NaN\" stroke-opacity=\"Infinity\" opacity=\"NaN\" stroke-miterlimit=\"Infinity\"/>" +
                                   "<rect fill-opacity=\"1e400\" stroke-opacity=\"-1e400\" opacity=\"0.5\" style=\"opacity: NaN\" stroke-miterlimit=\"1.9.\"/><rect fill-opacity=\"5.\" stroke-opacity=\"50 %\" opacity=\"0.5px\"/>" +
                                   "<stop stop-opacity=\"NaN\"/></g></svg>");
            Style ng = Style.Of(numbers.Root.Children[0], Style.Of(numbers.Root, Style.Initial, numbers.Sheet), numbers.Sheet);
            Style[] n = numbers.Root.Children[0].Children.Select(c => Style.Of(c, ng, numbers.Sheet)).ToArray();
            Check(n[0].FillOpacity == 0.25 && n[0].StrokeOpacity == 0.25 && n[0].Opacity == 1 && n[0].MiterLimit == 1.9 && n[1].FillOpacity == 1 && n[1].StrokeOpacity == 0 && n[1].Opacity == 0.5 &&
                  n[1].MiterLimit == 1.9 && n[2].FillOpacity == 0.25 && n[2].StrokeOpacity == 0.25 && n[2].Opacity == 1 && n[3].StopOpacity == 1,
                  "opacities and miter limits should be CSS numbers: " + string.Join(" / ", n.Select(s => s.FillOpacity + " " + s.StrokeOpacity + " " + s.Opacity + " " + s.MiterLimit + " " + s.StopOpacity)));
            var mitred = Images.Drawing(Encoding.UTF8.GetBytes("<svg " + NS + " viewBox=\"0 0 100 100\"><path d=\"M10 90 L50 10 L90 90\" fill=\"none\" stroke=\"#000\" stroke-width=\"5\" stroke-miterlimit=\"Infinity\"/></svg>"), null, out _);
            Check(Ink(Premultiplied(InBox(mitred, 100, 100, new Rect(0, 0, 100, 100), false, 1), out _, out _), 100, 100).Width > 50, "a stroke with stroke-miterlimit=\"Infinity\" should still be drawn");
            // a miter limit WPF's pen draws nothing with (from about 1e15) is a stroke as Chromium draws it, the same as 1e10's
            foreach (string limit in new[] { "1e15", "1e20", "1e30" })
            {
                byte[] Stroked(string l) => Premultiplied(InBox(Images.Drawing(Encoding.UTF8.GetBytes("<svg " + NS + " viewBox=\"0 0 100 100\"><path d=\"M40 49 L50 30 L60 49\" fill=\"none\" stroke=\"#000\" stroke-width=\"4\" stroke-miterlimit=\"" + l + "\"/></svg>"), null, out _),
                                                                100, 100, new Rect(0, 0, 100, 100), false, 1), out _, out _);
                byte[] huge = Stroked(limit), ordinary = Stroked("1e10");
                Check(Ink(huge, 100, 100).Width > 15 && huge.SequenceEqual(ordinary), "a stroke with stroke-miterlimit=\"" + limit + "\" should be drawn as with 1e10");
            }

            // pictures inside, known by their bytes whatever the address says: PNG, JPEG, GIF and BMP are drawn
            // (as in Chromium), TIFF isn't
            Color brown = Color.FromRgb(0xC0, 0x80, 0x40), green = Color.FromRgb(0x40, 0xC0, 0x80), blue = Color.FromRgb(0x40, 0x80, 0xC0);
            BitmapSource square = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgr24, null, Enumerable.Repeat(new byte[] { 0x40, 0x80, 0xC0 }, 16).SelectMany(b => b).ToArray(), 12);
            foreach (BitmapEncoder encoder in new BitmapEncoder[] { new PngBitmapEncoder(), new JpegBitmapEncoder { QualityLevel = 100 }, new GifBitmapEncoder(), new BmpBitmapEncoder(), new TiffBitmapEncoder() })
            {
                encoder.Frames.Add(BitmapFrame.Create(square));
                var file = new MemoryStream();
                encoder.Save(file);
                var inside = Images.Drawing(Encoding.UTF8.GetBytes("<svg " + NS + " viewBox=\"0 0 4 4\"><image href=\"data:image/x;base64," + Convert.ToBase64String(file.ToArray()) + "\" width=\"4\" height=\"4\"/></svg>"), null, out _);
                Color middle = PixelAt(InBox(inside, 8, 8, new Rect(0, 0, 8, 8), false, 1), 4, 4);
                bool tiff = encoder is TiffBitmapEncoder;
                Check(tiff ? middle.A == 0 : Like(middle, brown, 10) && middle.A == 255, encoder.GetType().Name + " inside an SVG: " + middle);
            }

            // what isn't drawn is left out quietly: a pattern (its colour to fall back on instead), a mask, a filter
            // and markers; the shapes they're on are drawn without them
            var leftOut = Images.Drawing(Encoding.UTF8.GetBytes("<svg " + NS + " viewBox=\"0 0 30 10\"><defs><pattern id=\"p\" width=\"2\" height=\"2\"><rect width=\"1\" height=\"1\"/></pattern>" +
                "<mask id=\"m\"><rect width=\"30\" height=\"10\" fill=\"#fff\"/></mask><filter id=\"f\"><feGaussianBlur stdDeviation=\"1\"/></filter><marker id=\"k\"><circle r=\"5\"/></marker></defs>" +
                "<rect width=\"10\" height=\"10\" fill=\"url(#p) #c08040\"/><rect x=\"10\" width=\"10\" height=\"10\" fill=\"#40c080\" mask=\"url(#m)\" filter=\"url(#f)\"/>" +
                "<path d=\"M20 5h10\" stroke=\"#4080c0\" stroke-width=\"10\" marker-end=\"url(#k)\"/></svg>"), null, out string leftWhy);
            BitmapSource left = leftOut == null ? null : InBox(leftOut, 30, 10, new Rect(0, 0, 30, 10), false, 1);
            Check(left != null && Like(PixelAt(left, 5, 5), brown) && Like(PixelAt(left, 15, 5), green) && Like(PixelAt(left, 25, 5), blue),
                "patterns, masks, filters and markers should be left out quietly, the rest drawn: " + leftWhy);
        }

        // a designed code with an SVG logo: every way out of b0r3d QR draws it and reads back
        static void SvgLogosInEveryOutput()
        {
            QrDesign d = Sample("https://b0r3d.org/an-svg-logo");
            d.Logo = SvgUrl(TestLogo);
            QrArt art = new QrArt(d);
            Check(art.LogoProblem == null && art.Cleared > 0 && art.LevelUsed == QrLevel.Q, "the SVG logo should be drawn in its room: " + art.LogoProblem);
            var brown = Color.FromRgb(0xC0, 0x80, 0x40);
            int middle = (int)(art.W / 2);

            BitmapSource png = Images.FromBytes(QrExport.Png(QrExport.Picture(art, null, false)));
            Check(ReadPicture(png) == d.Data && Like(PixelAt(png, middle, middle), brown), "the PNG should read back with the logo in its middle: " + PixelAt(png, middle, middle));
            BitmapSource jpg = Images.FromBytes(QrExport.Jpg(QrExport.Picture(art, null, false, true)));
            Check(ReadPicture(jpg) == d.Data && Like(PixelAt(jpg, middle, middle), brown, 12), "the JPG should read back with the logo: " + PixelAt(jpg, middle, middle));
            BitmapSource pdf = PdfPicture(QrExport.Pdf(art, null, false, "x"));
            Check(ReadPicture(pdf) == d.Data && Like(PixelAt(pdf, pdf.PixelWidth / 2, pdf.PixelHeight / 2), brown), "the PDF's picture should read back with the logo");
            Check(art.ToSvg().Contains("href=\"" + d.Logo + "\""), "the SVG file should carry the logo as it is, as the website's does");

            // copy: a PNG and a bitmap on the clipboard
            BitmapSource picture = QrExport.Picture(art, null, false);
            DataObject copied = Exports.DataFor(picture, QrExport.Png(picture), "x", false);
            BitmapSource pasted = Images.FromBytes(((MemoryStream)copied.GetData("PNG")).ToArray());
            Check(ReadPicture(pasted) == d.Data && Like(PixelAt(pasted, middle, middle), brown), "the copied PNG should read back with the logo");

            // print: vector all the way (no picture of the logo in the drawing), and the page reads back
            var pictures = new List<Drawing>();
            void Walk(Drawing drawing)
            {
                if (drawing is ImageDrawing) pictures.Add(drawing);
                if (drawing is DrawingGroup group) foreach (Drawing child in group.Children) Walk(child);
            }
            Walk(art.ToDrawing());
            Check(pictures.Count == 0, "an SVG logo should be drawn as shapes, so prints stay sharp, not as " + pictures.Count + " pictures");
            var sheet = Printing.PrintSheet.Pages(new List<Printing.PrintCode> { new Printing.PrintCode { Drawing = art.ToDrawing(), Canvas = new Rect(0, 0, art.W, art.H), Size = Printing.PrintSheet.QrSize(art, new PrintSize(), false) } },
                                                  new Printing.PrintOptions(), out _);
            var page = (System.Windows.Documents.FixedPage)sheet.Pages[0].Child;
            page.Measure(Printing.PrintSheet.A4);
            page.Arrange(new Rect(Printing.PrintSheet.A4));
            page.UpdateLayout();
            var paper = new RenderTargetBitmap((int)Printing.PrintSheet.A4.Width * 2, (int)Printing.PrintSheet.A4.Height * 2, 192, 192, PixelFormats.Pbgra32);
            paper.Render(page);
            Check(ReadPicture(paper) == d.Data, "the printed page should read back");

            // a frame with an SVG picture behind it: drawn over its whole box (the picture's own meet, as in
            // Chromium) and the code still reads back
            QrDesign framed = d.Clone();
            framed.Logo = "";
            framed.Frame = Presets.ApplyFrame("Default Frame", null);
            framed.Frame.BackgroundImage = SvgUrl("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><rect width=\"10\" height=\"10\" fill=\"#40c080\"/></svg>");
            QrArt frame = new QrArt(framed);
            BitmapSource framedPng = Images.FromBytes(QrExport.Png(QrExport.Picture(frame, null, false)));
            Check(frame.FrameImageProblem == null && ReadPicture(framedPng) == framed.Data && Like(PixelAt(framedPng, 6, framedPng.PixelHeight / 2), Color.FromRgb(0x40, 0xC0, 0x80)),
                "the frame's SVG picture should fill the frame, stretched as Chromium stretches one with no viewBox: " + PixelAt(framedPng, 6, framedPng.PixelHeight / 2));

            // Recent codes keep the logo as it is and draw it again
            string kept = DataFolder.Path;
            DataFolder.Reset(Path.Combine(Work, "svg-recent"));
            try
            {
                RecentCodes.Clear();
                int id = RecentCodes.Add(RecentCodes.New(d, 1000));
                QrDesign back = RecentCodes.Open(id, new QrDesign());
                Check(id > 0 && back.Logo == d.Logo && new QrArt(back).LogoProblem == null, "a recent code with an SVG logo should open with it, as it was");
                var list = new RecentWindow(new Settings(), x => true);
                LayOut(list.Root, 600, 660);
                Check(list.Codes.Count == 1, "the Recent codes list should show it");
            }
            finally
            {
                DataFolder.Reset(kept);
            }

            // batch export with the SVG logo: each file read back, the logo in it
            List<Batch.BatchRow> rows = Batch.BatchRows.ReadQr("url\nhttps://b0r3d.org/one\nhttps://b0r3d.org/two\n", out bool captions);
            var batch = new Batch.BatchKind { Design = d, Frame = captions, Format = Batch.BatchFormat.Png };
            foreach (Batch.BatchItem item in Batch.BatchExport.Plan(rows, batch))
            {
                byte[] made = Batch.BatchExport.Make(item, batch, out _);
                BitmapSource file = Batch.BatchExport.Picture(made, item, batch);
                byte[] grays = Qr.ScanCheck.Grays(file, out int w, out int h);
                Check(Batch.BatchExport.Read(grays, w, h, batch) == item.Expected && Like(PixelAt(file, file.PixelWidth / 2, file.PixelHeight / 2), brown), "the batch's " + item.Name + " should read back with the logo");
            }
        }

        // choosing, pasting and dropping an SVG logo: kept as it is, as the website keeps an upload; one that
        // can't be drawn is refused, with why
        static void SvgLogosPastedAndDropped()
        {
            MainWindow window = NewMain();
            Type(window, "https://b0r3d.org/pasted");
            byte[] logo = new UTF8Encoding(false).GetBytes(TestLogo);
            var inkscape = new DataObject();
            inkscape.SetData("image/svg+xml", new MemoryStream(logo), false);
            inkscape.SetData("PNG", new MemoryStream(Convert.FromBase64String(Presets.LogoUrl("b0r3d-cup.png").Substring(22))), false);
            Check(window.LogoFromData(inkscape) && window.CurrentDesign.Logo == SvgUrl(TestLogo), "image/svg+xml should be taken before the PNG beside it, byte for byte");
            WaitForCheck(window);
            Check(window.Art.LogoProblem == null && window.CheckResult == "ok", "the pasted SVG logo should be drawn and the code read back: " + window.CheckLine.Text);

            window.LogoFromData(new DataObject(DataFormats.UnicodeText, "\r\n  " + TestLogo.Replace("<rect", "<rect stroke=\"#000\"") + "  \r\n"));
            Check(window.CurrentDesign.Logo == SvgUrl(TestLogo.Replace("<rect", "<rect stroke=\"#000\"")), "copied SVG text should be taken as the logo, without the space around it");
            string before = window.CurrentDesign.Logo;
            Check(!window.LogoFromData(new DataObject(DataFormats.UnicodeText, "just words <svg>")), "text that isn't an SVG file isn't a logo");
            Check(window.LogoFromData(new DataObject(DataFormats.UnicodeText, "<svg xmlns=\"http://www.w3.org/2000/svg\"><g></svg>")) && window.CurrentDesign.Logo == before &&
                  window.Note.Text.Contains("can't draw") && window.Note.Text.Contains("doesn't match"), "a broken SVG should be refused, saying why: " + window.Note.Text);

            Check(MainWindow.DesignPicture(Encoding.UTF8.GetBytes("<!-- made by hand -->\n<!DOCTYPE svg [<!ENTITY c \"#c08040\">]>\n<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"9\" height=\"9\" fill=\"&c;\"/></svg>"), 1024, out string problem)?
                  .StartsWith("data:image/svg+xml;base64,", StringComparison.Ordinal) == true, "an SVG file starting with a comment and a DOCTYPE is an SVG drawing: " + problem);
            Check(MainWindow.DesignPicture(Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body><svg xmlns=\"http://www.w3.org/2000/svg\"/></body></html>"), 1024, out problem) == null &&
                  problem.Contains("Windows can't read it"), "a web page with a drawing in it isn't an SVG file: " + problem);
            Check(Svg.SvgImage.Looks(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\n<svg:svg xmlns:svg=\"http://www.w3.org/2000/svg\"/>")), "an svg: prefix is still SVG");

            // a frame's picture from an SVG file, as Choose... keeps it
            window.FrameCheck.IsChecked = true;
            Pump(() => false, 50);
            window.CurrentDesign.Frame.BackgroundImage = MainWindow.DesignPicture(logo, 2048, out problem);
            window.Refresh();
            WaitForCheck(window);
            Check(window.CurrentDesign.Frame.BackgroundImage == SvgUrl(TestLogo) && window.Art.FrameImageProblem == null && window.FrameNote.Text.Length == 0, "an SVG frame picture should be kept and drawn: " + window.FrameNote.Text);
        }

        // hostile files: refused quickly with a plain reason, or drawn without the part that would never end
        static void HostileSvgsRefusedQuickly()
        {
            const string NS = "xmlns=\"http://www.w3.org/2000/svg\"";
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string Why(string svg)
            {
                watch.Restart();
                ImageSource drawn = Images.Drawing(Encoding.UTF8.GetBytes(svg), null, out string why);
                Check(watch.ElapsedMilliseconds < 5000, "took " + watch.ElapsedMilliseconds + " ms: " + Short(svg));
                return drawn == null ? why : null;
            }
            var laughs = new StringBuilder("<!DOCTYPE svg [<!ENTITY a0 \"ha!ha!ha!ha!\">");
            for (int i = 1; i < 8; i++) laughs.Append("<!ENTITY a" + i + " \"" + string.Concat(Enumerable.Repeat("&a" + (i - 1) + ";", 10)) + "\">");
            laughs.Append("]><svg " + NS + "><text>&a7;</text></svg>");
            Check(Why(laughs.ToString())?.Contains("grow far bigger") == true, "entities that grow without end should be refused");
            var deeper = new StringBuilder("<!DOCTYPE svg [<!ENTITY d0 \"x\">");
            for (int i = 1; i < 12; i++) deeper.Append("<!ENTITY d" + i + " \"&d" + (i - 1) + ";\">");
            Check(Why(deeper + "]><svg " + NS + "><text>&d11;</text></svg>")?.Contains("nested too deeply") == true, "entities inside entities 12 deep should be refused");
            var copies = new StringBuilder("<svg " + NS + "><defs><rect id=\"u0\" width=\"1\" height=\"1\"/>");
            for (int i = 1; i < 10; i++) copies.Append("<g id=\"u" + i + "\">" + string.Concat(Enumerable.Repeat("<use href=\"#u" + (i - 1) + "\"/>", 10)) + "</g>");
            copies.Append("</defs><use href=\"#u9\"/></svg>");
            Check(Why(copies.ToString())?.Contains("more to draw") == true, "a billion copies through <use> should be refused");
            Check(Why("<svg " + NS + ">" + string.Concat(Enumerable.Repeat("<g>", 300)) + string.Concat(Enumerable.Repeat("</g>", 300)) + "</svg>")?.Contains("nested more than 256") == true, "300 groups deep should be refused");
            Check(Why("<svg " + NS + "><style>x g g g g g g g g g g g rect { fill: red }</style>" + string.Concat(Enumerable.Repeat("<g>", 200)) + "<rect width=\"1\" height=\"1\"/>" +
                      string.Concat(Enumerable.Repeat("</g>", 200)) + "</svg>")?.Contains("styles take too long") == true, "a selector that tries every way up 200 groups should be stopped");
            // ...but the steps are counted for each drawing, so a busy style sheet drawn again and again in one
            // run is never stopped for the drawings before
            SvgImage busy = SvgImage.Read(Encoding.UTF8.GetBytes("<svg " + NS + "><style>x g g rect { fill: red }</style>" + string.Concat(Enumerable.Repeat("<g>", 50)) +
                                                                "<rect width=\"1\" height=\"1\"/>" + string.Concat(Enumerable.Repeat("</g>", 50)) + "</svg>"));
            busy.Render(new Size(10, 10));
            long once = StyleSheet.Steps;
            busy.Render(new Size(11, 10));
            Check(once > 10000 && StyleSheet.Steps == once, "each drawing should count its own steps: " + once + ", then " + StyleSheet.Steps);

            // a selector is matched one compound inside another, so one of 10,000 compounds over 10,000 rects
            // (the steps it takes stay under the limit) went 10,000 calls deep and overflowed the stack, which
            // ends the program with no message; :not() inside :not() and @media inside @media went as deep
            // while reading them. Past 32 compounds and 16 @media blocks the file is refused instead.
            string Rects(int n) => string.Concat(Enumerable.Repeat("<rect width=\"1\" height=\"1\"/>", n));
            foreach (string combinator in new[] { " ~ ", " + ", " > " })
            {
                string chain = "rect" + string.Concat(Enumerable.Repeat(combinator + "rect", 10000)) + ".deep";
                Check(Why("<svg " + NS + "><style>" + chain + " { fill: red }</style>" + Rects(10000) + "<rect class=\"deep\" width=\"1\" height=\"1\"/></svg>")?
                          .Contains("a selector of more than 32 parts") == true, "a selector of 10,001 compounds joined by '" + combinator.Trim() + "' should be refused");
            }
            Check(Why("<svg " + NS + "><style>rect" + string.Concat(Enumerable.Repeat(":not(", 20000)) + ".x" + new string(')', 20000) + " { fill: red }</style>" + Rects(10) + "</svg>")?
                      .Contains("a selector of more than 32 parts") == true, ":not() inside :not() 20,000 deep should be refused");
            Check(Why("<svg " + NS + "><style>" + string.Concat(Enumerable.Repeat("@media all {", 100000)) + "rect { fill: red }" + new string('}', 100000) + "</style>" + Rects(10) + "</svg>")?
                      .Contains("@media blocks nested more than 16 deep") == true, "@media inside @media 100,000 deep should be refused");
            // ...and a selector of 32 compounds (the two inside :not() among them), 16 @media blocks deep, still applies
            SvgImage longest = SvgImage.Read(Encoding.UTF8.GetBytes("<svg " + NS + "><style>" + string.Concat(Enumerable.Repeat("@media screen {", 16)) +
                                                                   "rect" + string.Concat(Enumerable.Repeat(" ~ rect", 28)) + " ~ rect:not(:not(.deep)) { fill: #c08040 }" + new string('}', 16) + "</style>" +
                                                                   Rects(40) + "<rect class=\"deep\" width=\"1\" height=\"1\"/></svg>"));
            Style deepest = Style.Of(longest.Root.Children[41], Style.Of(longest.Root, Style.Initial, longest.Sheet), longest.Sheet);
            Check(deepest.Fill.Colour == Color.FromRgb(0xC0, 0x80, 0x40), "a selector of 32 compounds inside 16 @media blocks should apply: " + deepest.Fill.Colour);

            // elements inside elements are drawn (and measured, for a clip path in their own units) one inside
            // another, a step deeper into the stack each, through <use>s too: 32 chains of 254 groups, each
            // ending in a <use> of the next, went 8,000 deep and overflowed it. Past 512 (each transform, clip
            // and opacity counting too, as WPF goes as deep again through them) the file is refused.
            string Chains(int chains, int deep)
            {
                var s = new StringBuilder("<defs><clipPath id=\"box\" clipPathUnits=\"objectBoundingBox\"><rect width=\"1\" height=\"1\"/></clipPath>");
                for (int c = 0; c < chains; c++)
                    s.Append("<g id=\"c" + c + "\">" + string.Concat(Enumerable.Repeat("<g>", deep - 1)) +
                             (c + 1 < chains ? "<use href=\"#c" + (c + 1) + "\"/>" : "<rect width=\"1\" height=\"1\" fill=\"#c08040\"/>") + string.Concat(Enumerable.Repeat("</g>", deep)));
                return s + "</defs>";
            }
            Check(Why("<svg " + NS + ">" + Chains(32, 254) + "<use href=\"#c0\"/></svg>")?.Contains("nested more than 512 deep") == true, "32 chains of 254 groups through <use> should be refused");
            Check(Why("<svg " + NS + ">" + Chains(32, 254) + "<g clip-path=\"url(#box)\"><use href=\"#c0\"/></g></svg>")?.Contains("nested more than 512 deep") == true,
                  "measuring 32 chains of 254 groups for a clip path should be refused");
            // clip paths of several shapes are masks in WPF, and the time it takes to paint them doubles with
            // each inside another (30 took minutes): past 16 the file is refused
            Check(Why("<svg " + NS + " viewBox=\"0 0 10 10\"><clipPath id=\"two\"><rect width=\"9\" height=\"9\"/><circle r=\"3\"/></clipPath>" + string.Concat(Enumerable.Repeat("<g clip-path=\"url(#two)\">", 30)) +
                      "<rect width=\"10\" height=\"10\"/>" + string.Concat(Enumerable.Repeat("</g>", 30)) + "</svg>")?.Contains("clip paths of more than one shape are nested more than 16 deep") == true,
                  "clip paths of two shapes nested 30 deep should be refused");
            // ...and as deep as can be, 254 groups with a transform each, drawn and painted with half the program's
            // stack (the other half for what's under the drawing in the program: WPF's layout and painting)
            string deepestLogo = "<svg " + NS + " viewBox=\"0 0 10 10\">" + string.Concat(Enumerable.Repeat("<g transform=\"translate(0.01 0)\">", 254)) + "<rect width=\"10\" height=\"10\" fill=\"#c08040\"/>" +
                                 string.Concat(Enumerable.Repeat("</g>", 254)) + "</svg>";
            Color painted = OnHalfTheStack(() =>
            {
                ImageSource drawn = Images.Drawing(Encoding.UTF8.GetBytes(deepestLogo), null, out string why);
                return drawn == null ? Colors.Transparent : PixelAt(InBox(drawn, 20, 20, new Rect(0, 0, 20, 20), false, 1), 10, 10);
            });
            Check(Like(painted, Color.FromRgb(0xC0, 0x80, 0x40)) && painted.A == 255, "254 groups with a transform each should be drawn and painted with half the stack: " + painted);

            // text: WPF takes up to 0.6 ms a character to lay it out as shapes, so 50,000 of them in one <text>
            // took 7 seconds and a 2 MB file minutes. Characters are counted as they're read, copies included,
            // and past 2,000 the file is refused.
            Check(Why("<svg " + NS + "><text>" + new string('a', 50000) + "</text></svg>")?.Contains("more text than b0r3d QR draws in one picture (over 2,000 characters") == true,
                  "a <text> of 50,000 characters should be refused");
            Check(Why("<svg " + NS + "><text>" + string.Concat(Enumerable.Repeat("<tspan>" + new string('a', 50) + "</tspan>", 1000)) + "</text></svg>")?.Contains("over 2,000 characters") == true,
                  "1,000 <tspan>s of 50 characters should be refused");
            Check(Why("<svg " + NS + "><defs><text id=\"t\">" + new string('a', 100) + "</text></defs>" + string.Concat(Enumerable.Repeat("<use href=\"#t\"/>", 30)) + "</svg>")?.Contains("over 2,000 characters") == true,
                  "a <text> of 100 characters used 30 times should be refused");
            // ...and white space that collapses to nothing is read all the same, again for each copy: a <text> of
            // 2,000,000 spaces used 50 times took a second, used 500 times minutes, drawing no more than "a a". What's
            // read of a text, copies included, counts with what's read of the attributes, so that file is refused; so
            // is a group of 20,000 elements that are never drawn, each copy looking at all of them again (100,000 of
            // them used 10,000 times took a minute)
            Check(Why("<svg " + NS + "><defs><text id=\"t\">a" + new string(' ', 2000000) + "a</text></defs>" + string.Concat(Enumerable.Repeat("<use href=\"#t\"/>", 50)) + "</svg>")?.Contains("more to read") == true,
                  "a <text> of 2,000,000 spaces used 50 times should be refused");
            Check(Why("<svg " + NS + "><defs><g id=\"g\">" + string.Concat(Enumerable.Repeat("<desc/>", 20000)) + "</g></defs>" + string.Concat(Enumerable.Repeat("<use href=\"#g\"/>", 1000)) + "</svg>")?.Contains("more to read") == true,
                  "a group of 20,000 elements that aren't drawn, used 1,000 times, should be refused");
            // ...while a file written out on many lines, its text indented inside each <text> (3,500 spaces and line
            // breaks among 900 characters drawn), draws just as the same file written on one line does
            var indented = new StringBuilder("<svg " + NS + " viewBox=\"0 0 300 200\">\n");
            var oneLine = new StringBuilder("<svg " + NS + " viewBox=\"0 0 300 200\">");
            for (int i = 0; i < 100; i++)
            {
                string at = "x=\"" + i % 10 * 30 + "\" y=\"" + (i / 10 * 20 + 15) + "\" font-size=\"9\"";
                indented.Append("  <text " + at + ">\n      b0r" + i + "\n      <tspan fill=\"#c08040\">\n        QR\n      </tspan>\n    </text>\n");
                oneLine.Append("<text " + at + ">b0r" + i + " <tspan fill=\"#c08040\">QR</tspan></text>");
            }
            Check(Why(indented + "</svg>") == null, "a file of 100 indented <text>s should be drawn");
            byte[] Pixels(string svg) => Premultiplied(InBox(Images.Drawing(Encoding.UTF8.GetBytes(svg), null, out _), 300, 200, new Rect(0, 0, 300, 200), false, 1), out _, out _);
            byte[] spread = Pixels(indented + "</svg>"), tight = Pixels(oneLine + "</svg>");
            Check(spread.SequenceEqual(tight) && Ink(spread, 300, 200).Width > 250, "100 indented <text>s should draw exactly as they do written on one line");
            // ...one letter with some 700 marks on it ends the program inside WPF's own text layout: past 64 the
            // file is refused (as many as that still draw)
            Check(Why("<svg " + NS + "><text>a" + new string((char)0x301, 1000) + "</text></svg>")?.Contains("more than 64 accents or other marks on one letter") == true,
                  "a letter with 1,000 accents should be refused");
            Check(Why("<svg " + NS + "><text>a" + new string((char)0x200D, 1000) + "</text></svg>")?.Contains("more than 64 accents") == true, "a letter with 1,000 joiners should be refused");
            string stems = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1D165), 500));
            Check(Why("<svg " + NS + "><text>a" + stems + "</text></svg>")?.Contains("more than 64 accents") == true, "a letter with 500 marks outside the 16-bit range should be refused");
            // ...counted as a caption counts them (Unicode.OverTheLimit): Unicode 17's marks, which Windows knows and
            // .NET's older Unicode calls unassigned (U+1AC1), and code points with no character yet; but a format
            // character ends the run, as it ends WPF's
            Check(Why("<svg " + NS + "><text>a" + new string((char)0x1AC1, 65) + "</text></svg>")?.Contains("more than 64 accents") == true, "a letter with 65 marks of Unicode 14 should be refused");
            Check(Why("<svg " + NS + "><text>a" + new string((char)0x1AC1, 64) + "</text></svg>") == null, "a letter with 64 marks of Unicode 14 should still be drawn");
            Check(Why("<svg " + NS + "><text>a" + new string((char)0x378, 65) + "</text></svg>")?.Contains("more than 64 accents") == true, "a letter with 65 unassigned code points after it should be refused");
            Check(Why("<svg " + NS + "><text>a" + new string((char)0x200B, 1000) + "</text></svg>") == null, "a letter with 1,000 zero-width spaces after it should be drawn");
            Check(Why("<svg " + NS + "><text>a" + new string((char)0x301, 64) + "</text><text>" + new string('a', 1800) + "</text><text x=\"" +
                      string.Concat(Enumerable.Repeat("0 ", 2000000)) + "\">b0r3d QR</text></svg>") == null,
                  "text of 2,000 characters, a letter with 64 accents and a list of 2,000,000 positions should still be drawn");
            // ...and text bigger than WPF lays out (it refuses a size past 35,791 px, which ended the drawing with an
            // exception) is drawn at Chromium's biggest size, 10,000 px (SvgFontSizesLikeChromium)
            Check(Why("<svg " + NS + " viewBox=\"0 0 10 10\"><text font-size=\"50000\">a</text><text font-size=\"1e9\">b</text></svg>") == null, "text 50,000 and 1,000,000,000 px tall should be drawn");
            // the other things a file under 16 MB can ask a lot of: shapes' points (a path's commands, a
            // polyline's points), gradient stops, a style sheet's rules, a dash array. WPF takes a few µs a
            // point to paint an outline, more for one whose lines all cross, so points are counted (copies
            // included), a shape of many counting for more, and past the cap the file is refused.
            Check(Why("<svg " + NS + "><path d=\"M0 0" + string.Concat(Enumerable.Repeat("L1 1L2 0", 500000)) + "\"/></svg>")?.Contains("more points") == true, "a path of a million commands should be refused");
            Check(Why("<svg " + NS + "><polyline points=\"" + string.Concat(Enumerable.Repeat("1,1 2,0 ", 500000)) + "\"/></svg>")?.Contains("more points") == true, "a polyline of a million points should be refused");
            Check(Why("<svg " + NS + "><polygon points=\"" + string.Concat(Enumerable.Repeat("1,1 2,0 ", 15000)) + "\"/></svg>")?.Contains("one shape can have up to") == true, "a single shape of 30,000 points should be refused");
            Check(Why("<svg " + NS + "><defs><path id=\"p\" d=\"M0 0" + string.Concat(Enumerable.Repeat("L1 1", 5000)) + "\"/></defs>" + string.Concat(Enumerable.Repeat("<use href=\"#p\"/>", 200)) + "</svg>")?.Contains("more points") == true,
                  "a path of 5,000 points copied 200 times should be refused");
            // a gradient's stops: WPF takes about 6 µs each to paint, so they're counted too (across the
            // brushes made from one gradient on many shapes: one gradient can hold only so many stops, since
            // each is an element)
            string Stops(int n) => string.Concat(Enumerable.Range(0, n).Select(i => "<stop offset=\"" + ((double)i / n).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\" stop-color=\"#" + (i % 2 == 0 ? "f00" : "00f") + "\"/>"));
            Check(Why("<svg " + NS + " viewBox=\"0 0 9 9\"><linearGradient id=\"g\">" + Stops(2000) + "</linearGradient>" +
                      string.Concat(Enumerable.Repeat("<rect width=\"9\" height=\"9\" fill=\"url(#g)\"/>", 200)) + "</svg>")?.Contains("more stops") == true, "a 2,000-stop gradient on 200 shapes should be refused");
            // a stroke-dasharray of thousands of values (read one at a time, so a giant one is caught before it's all read)
            Check(Why("<svg " + NS + "><path d=\"M0 0H9\" stroke=\"#000\" stroke-dasharray=\"" + string.Concat(Enumerable.Repeat("1 ", 2000000)) + "\"/></svg>")?.Contains("stroke-dasharray has more than") == true,
                  "a dash array of 2,000,000 values should be refused");
            // a file leaning on <use> to copy a heavy element a thousandfold: its attributes (read again
            // for each copy) and the declarations applied to each copy are counted, copies included
            Check(Why("<svg " + NS + "><defs><rect id=\"r\" width=\"1\" height=\"1\" style=\"" + string.Concat(Enumerable.Repeat("fill:red;", 100000)) + "\"/></defs>" +
                      "<g>" + string.Concat(Enumerable.Repeat("<use href=\"#r\"/>", 100)) + "</g>" + string.Concat(Enumerable.Repeat("<use href=\"#r\"/>", 100)) + "</svg>")?.Contains("styles take too long") == true,
                  "a rect with 100,000 style declarations copied through <use> should be refused");
            Check(Why("<svg " + NS + "><defs><rect id=\"r\" data-x=\"" + new string('y', 400000) + "\" width=\"1\" height=\"1\"/></defs><g>" + string.Concat(Enumerable.Repeat("<use href=\"#r\"/>", 100)) +
                      "</g>" + string.Concat(Enumerable.Repeat("<use href=\"#r\"/>", 100)) + "</svg>")?.Contains("more to read") == true, "a rect with a 400 KB attribute copied through <use> should be refused");
            // ...and the biggest of each that a real logo could hold still draws
            Check(Why("<svg " + NS + " viewBox=\"0 0 9 9\"><polyline points=\"" + string.Concat(Enumerable.Repeat("1,1 2,0 ", 2000)) + "\" fill=\"#c08040\"/>" +
                      "<linearGradient id=\"g\">" + Stops(1000) + "</linearGradient><rect width=\"9\" height=\"9\" fill=\"url(#g)\"/>" +
                      "<path d=\"M0 0H9\" stroke=\"#000\" stroke-dasharray=\"" + string.Concat(Enumerable.Repeat("1 2 ", 1000)) + "\"/></svg>") == null,
                  "a 4,000-point polyline, a 1,000-stop gradient and a 2,000-value dash array should still be drawn");
            Check(Why("<svg " + NS + "><!--" + new string('x', 17 * 1024 * 1024) + "--></svg>")?.Contains("over 16 MB") == true, "a file over 16 MB should be refused");
            Check(Why("<svg " + NS + "><g id=\"a\"><use href=\"#b\"/></g><g id=\"b\"><use href=\"#a\"/></g><use href=\"#a\"/><rect width=\"5\" height=\"5\"/></svg>") == null, "two <use>s of each other draw nothing, and the rest is drawn");
            Check(Why("<svg " + NS + "><path d=\"M0 0H1000000\" stroke=\"#000\" stroke-dasharray=\"0.001\"/></svg>") == null, "a dash pattern too fine for its line is drawn as a plain line, quickly");

            // pictures inside: one nested past three levels, one claiming a billion pixels, one on the web: not drawn
            string nested = "<svg " + NS + " viewBox=\"0 0 1 1\"><rect width=\"1\" height=\"1\" fill=\"#c08040\"/></svg>";
            for (int i = 0; i < 4; i++) nested = "<svg " + NS + " viewBox=\"0 0 1 1\"><image href=\"" + SvgUrl(nested) + "\" width=\"1\" height=\"1\"/></svg>";
            var deep = (DrawingImage)Images.Drawing(Encoding.UTF8.GetBytes(nested), null, out _);
            Check(deep != null && Ink(Premultiplied(InBox(deep, 20, 20, new Rect(0, 0, 20, 20), false, 1), out _, out _), 20, 20).Width == 0, "SVG pictures nested four deep: the deepest isn't drawn");
            var ihdr = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 1, 0x86, 0xA0, 0, 1, 0x86, 0xA0, 8, 6, 0, 0, 0, 0, 0, 0, 0 };
            string giant = "<svg " + NS + " viewBox=\"0 0 1 1\"><image href=\"data:image/png;base64," + Convert.ToBase64String(ihdr) + "\" width=\"1\" height=\"1\"/>" +
                           "<image href=\"https://example.com/x.png\" width=\"1\" height=\"1\"/><use href=\"other.svg#x\"/></svg>";
            var none = Images.Drawing(Encoding.UTF8.GetBytes(giant), null, out string giantWhy);
            Check(none != null && Ink(Premultiplied(InBox(none, 20, 20, new Rect(0, 0, 20, 20), false, 1), out _, out _), 20, 20).Width == 0,
                "a picture claiming 100,000 × 100,000 pixels, one on the web and a <use> of another file draw nothing: " + giantWhy);
            Check(new QrArt(new QrDesign { Data = "x", Logo = SvgUrl("<svg/>") }).LogoProblem == "b0r3d QR can't draw this SVG picture: its <svg> has no xmlns=\"http://www.w3.org/2000/svg\", which browsers need to draw it.",
                "a design's SVG logo that can't be drawn says why in plain words");
        }

        // Font sizes as Chromium has them (Edge 156): 10,000 px at most, a size past it being that size (font-size="50000",
        // "1e9", "1e400", the font shorthand, larger, and nine groups of 3e38em inside one another, whose size went past a
        // double to Infinity, then to NaN by 0em, 0% or 0ex of it, which WPF's text layout threw at; sizes between were
        // drawn up to 35,791 px), so 0em of the biggest is nothing, and em units are of the size drawn. Each is drawn
        // exactly as that size is (ChromiumSvgPictures holds a picture of them as Chromium draws it). And anything else
        // that stops a drawing is a refusal in a line, never an exception, which the tests hear of.
        static void SvgFontSizesLikeChromium()
        {
            const string NS = "xmlns=\"http://www.w3.org/2000/svg\"";
            string Infinite(string inner) => string.Concat(Enumerable.Repeat("<g font-size=\"3e38em\">", 9)) + inner + string.Concat(Enumerable.Repeat("</g>", 9));
            var wrong = new List<string>();
            // text in a viewBox 400 times its box, where 10,000 px is 25 px, as pixels (null when it isn't drawn)
            byte[] Drawn(string text)
            {
                try
                {
                    ImageSource drawn = Images.Drawing(Encoding.UTF8.GetBytes("<svg " + NS + " viewBox=\"0 0 40000 20000\">" + text + "</svg>"), null, out string why);
                    if (drawn != null) return Premultiplied(InBox(drawn, 100, 50, new Rect(0, 0, 100, 50), false, 1), out _, out _);
                    wrong.Add(Short(text) + " was refused: " + why);
                }
                catch (Exception x)
                {
                    wrong.Add(Short(text) + " threw " + x.GetType().Name + ": " + x.Message.Split('\n')[0].Trim());
                }
                return null;
            }
            void Expect(bool ok, string why)
            {
                if (!ok) wrong.Add(why);
            }
            bool Same(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);
            string H(string size, int x = 1000) => "<text x=\"" + x + "\" y=\"15000\" font-family=\"Arial\"" + size + ">H</text>";
            foreach (string zero in new[] { " font-size=\"0em\"", " font-size=\"0%\"", " font-size=\"0ex\"" })
                Expect(Drawn(Infinite(H(zero))) is byte[] blank && Ink(blank, 100, 50).Width == 0, "an H of" + zero + " inside nine groups of 3e38em should draw nothing, as in Chromium");
            Expect(Drawn(Infinite("<g font-size=\"0em\">" + H("") + "</g>")) is byte[] empty && Ink(empty, 100, 50).Width == 0, "an H in a group of 0em inside nine groups of 3e38em should draw nothing");
            SvgImage chain = SvgImage.Read(Encoding.UTF8.GetBytes("<svg " + NS + ">" + Infinite("<text font-size=\"0em\">a</text>") + "</svg>"));
            var sizes = new List<double>();
            Style style = Style.Of(chain.Root, Style.Initial, chain.Sheet);
            for (Element e = chain.Root; e.Children.Count > 0;)
            {
                e = e.Children[0];
                style = Style.Of(e, style, chain.Sheet);
                sizes.Add(style.FontSize);
            }
            Expect(sizes.Count == 10 && sizes.Take(9).All(s => s == Style.MostFontSize) && sizes[9] == 0, "each of nine groups of 3e38em should be 10,000 px, and 0em of them 0, not " + string.Join(", ", sizes));
            byte[] biggest = Drawn(H(" font-size=\"10000\""));
            Expect(biggest != null && Ink(biggest, 100, 50).Height >= 15 && !Same(Drawn(H(" font-size=\"9000\"")), biggest), "an H of 10,000 px should be 18 px tall here, and bigger than one of 9,000");
            foreach (string size in new[] { " font-size=\"20000\"", " font-size=\"50000\"", " font-size=\"1e9\"", " font-size=\"1e400\"", " font-size=\"3e38em\"", " style=\"font: 50000px Arial\"" })
                Expect(Same(Drawn(H(size)), biggest), "an H of" + size + " should be drawn at 10,000 px, as Chromium draws it");
            Expect(Same(Drawn("<g font-size=\"9000\">" + H(" font-size=\"larger\"") + "</g>"), biggest), "larger than 9,000 px should be 10,000 px");
            Expect(Same(Drawn(Infinite(H(""))), biggest), "nine groups of 3e38em inside one another should be 10,000 px, not infinite");
            // em units are of the size drawn: half an em of 1,000,000 px moves an H 5,000 along, and letter-spacing of a fifth
            // of one puts 2,000 after each
            Expect(Same(Drawn("<text x=\"1000\" y=\"15000\" font-family=\"Arial\" font-size=\"1e6\" dx=\"0.5em\">H</text>"), Drawn(H(" font-size=\"10000\"", 6000))),
                   "dx=\"0.5em\" of 1,000,000 px should move an H 5,000 along");
            Expect(Same(Drawn("<text x=\"1000\" y=\"15000\" font-family=\"Arial\" font-size=\"1e6\" letter-spacing=\"0.2em\">HH</text>"),
                        Drawn("<text x=\"1000\" y=\"15000\" font-family=\"Arial\" font-size=\"10000\" letter-spacing=\"2000\">HH</text>")), "letter-spacing=\"0.2em\" of 1,000,000 px should be 2,000");
            // ...and a size below a float's epsilon (2^-24 px here) is drawn as none, but em of it are worked out from what it says
            Expect(Same(Drawn("<g font-size=\"5.9604644775390625e-8\">" + H(" font-size=\"167772160000em\"") + "</g>"), biggest), "167,772,160,000em of 2^-24 px should be 10,000 px");
            Check(wrong.Count == 0, wrong.Count + " wrong: " + string.Join("; ", wrong));

            // anything else that stops a drawing (no bytes at all here, where a file's go) is a refusal in a line, never an
            // exception, and the tests hear of it (TestMain makes that a FAIL)
            Action<Exception> failing = Images.OnUnexpected;
            var heard = new List<Exception>();
            try
            {
                Images.OnUnexpected = heard.Add;
                ImageSource none = Images.Drawing(null, null, out string why);
                Check(none == null && heard.Count == 1 && why != null && why.StartsWith("something in it stopped the drawing (", StringComparison.Ordinal) && why.IndexOfAny(new[] { '\r', '\n' }) < 0,
                      "a drawing that stops should be refused in a line, and heard of: " + (why ?? "(no reason)") + ", heard " + heard.Count);
            }
            finally
            {
                Images.OnUnexpected = failing;
            }
        }

        // A refusal quoting the file (an entity's name, a numbered character's digits, an element's name, the character
        // where a name was expected) is one line of text: the characters WPF breaks a line at (CR, LF, U+000B, U+000C, NEL,
        // U+2028 and U+2029) and the other control characters are spaces in it. Each is laid out as the note shows one.
        static void SvgRefusalsInOneLine()
        {
            const string NS = "xmlns=\"http://www.w3.org/2000/svg\"";
            double Height(string text)
            {
                var block = new System.Windows.Controls.TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
                block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return block.DesiredSize.Height;
            }
            double line = Height("x");
            // (a text with its control characters and separators shown as their numbers)
            string Shown(string text) => text == null ? "read" : string.Concat(text.Select(o => char.IsControl(o) || o == (char)0x2028 || o == (char)0x2029 ? "{U+" + ((int)o).ToString("X4") + "}" : o.ToString()));
            var wrong = new List<string>();
            foreach (char c in new[] { (char)0x2028, (char)0x2029, (char)0x85, '\r', '\n', '\t', (char)0x0B, (char)0x0C, (char)0x7F, (char)0x80, (char)0x9F })
            {
                string ch = c.ToString();
                foreach (var (xml, why) in new[]
                {
                    ("<svg " + NS + "><text>&a" + ch + "b;</text></svg>", "isn't defined"),
                    ("<svg " + NS + "><text>&#1" + ch + "2;</text></svg>", "isn't a character"),
                    ("<!DOCTYPE svg [<!ENTITY e \"&a" + ch + "b;\">]><svg " + NS + "><text>&e;</text></svg>", "isn't defined"),
                    ("<svg " + NS + "><" + ch + "a/></svg>", "a name was expected"),
                    ("<svg " + NS + "><a" + ch + "b></svg>", ""),   // a name with it in (a C1 control's), or one before it
                })
                {
                    string refused;
                    try
                    {
                        SvgImage.Read(Encoding.UTF8.GetBytes(xml));
                        refused = null;
                    }
                    catch (SvgException x)
                    {
                        refused = x.Message;
                    }
                    if (refused == null || !refused.Contains(why) || refused.Any(char.IsControl) || refused.IndexOf((char)0x2028) >= 0 || refused.IndexOf((char)0x2029) >= 0 || Height(refused) > line * 1.5)
                        wrong.Add(Shown(xml.Replace(" " + NS, "")) + ": " + Shown(refused) + (refused == null ? "" : " (" + Math.Round(Height(refused) / line) + " lines)"));
                }
                // a design's picture that's a path on the website, or on the web: its note quotes the path or the host
                foreach (string value in new[] { "/img/a" + ch + "b.png", "https://example.org/a" + ch + "b.png", "https://exa" + ch + "mple.org/a.png" })
                {
                    Images.Load(value, out string note);
                    if (note == null || note.Any(char.IsControl) || note.IndexOf((char)0x2028) >= 0 || note.IndexOf((char)0x2029) >= 0 || Height(note) > line * 1.5)
                        wrong.Add("a design's picture " + Shown(value) + ": " + Shown(note) + (note == null ? "" : " (" + Math.Round(Height(note) / line) + " lines)"));
                }
            }
            // a long path is cut short without leaving half of an emoji (its two halves at 36 and 37)
            string path = "/img/" + new string('a', 31) + char.ConvertFromUtf32(0x1F600) + "b.png";
            Images.Load(path, out string cut);
            bool halves = cut == null || cut.Where((o, i) => char.IsHighSurrogate(o) && (i + 1 == cut.Length || !char.IsLowSurrogate(cut[i + 1])) ||
                                                              char.IsLowSurrogate(o) && (i == 0 || !char.IsHighSurrogate(cut[i - 1]))).Any();
            if (halves || !cut.Contains("/img/" + new string('a', 31) + "...")) wrong.Add("a design's picture with a long path: " + Shown(cut));
            // only the start of a text is quoted, so millions of characters with a letter over the limit cost no more than a
            // short text (capping all of them took over a second and 600 MB for 10 million)
            string longOne = "/logos/x" + new string((char)0x301, 150) + new string('a', 4000000);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string quoted = Xml.Quote(longOne);
            watch.Stop();
            if (quoted != "/logos/x" + new string((char)0x301, 29) + "..." || watch.ElapsedMilliseconds > 200)
                wrong.Add("4 million characters with a letter over the limit, quoted in " + watch.ElapsedMilliseconds + " ms: " + Shown(quoted));
            Check(wrong.Count == 0, wrong.Count + " refusals and notes aren't one line of plain text: " + string.Join("; ", wrong.Take(8)));
        }

        // Emoji in an SVG picture's text as Chromium draws them (Edge 156, the website's way): in their own colours, from
        // Segoe UI Emoji's layers and palette as a caption's (Chromium shades the same emoji, from the font's COLR version
        // 1), whatever the fill's colour or gradient; whole in the fill and again in the stroke, each time as see-through as
        // that paint (so with a stroke and no fill, once, and with neither, not at all); all an emoji covers in a clip path,
        // not its plain glyph with holes; sequences, ⭐ and ❤️ from Segoe UI Emoji and a text heart (❤) in the fill; one
        // placed by an x list, or spaced, kept whole. Their layers count as shapes drawn, so 1,000 of the emoji with the
        // most are refused; and made-up tables that don't add up leave the text in its fill, never stopping it.
        static void SvgEmojiInTheirColours()
        {
            if (!QrArt.Installed("Segoe UI Emoji")) throw new SkipException("Segoe UI Emoji isn't installed on this PC");
            const string NS = "xmlns=\"http://www.w3.org/2000/svg\"";
            new Typeface(new FontFamily("Segoe UI Emoji"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out GlyphTypeface font);
            ColourGlyphs tables = ColourGlyphs.Of(font);
            Check(tables != null, "Segoe UI Emoji should have colour tables");
            // an emoji's own colours (not its greys and whites, which other things have too)
            List<Color> Own(int c) => tables.ColoursOf(font.CharacterToGlyphMap[c]).Where(o => o.HasValue && Math.Max(o.Value.R, Math.Max(o.Value.G, o.Value.B)) - Math.Min(o.Value.R, Math.Min(o.Value.G, o.Value.B)) >= 60)
                                                                                  .Select(o => o.Value).Distinct().ToList();
            List<Color> smiley = Own(0x1F600), heart = Own(0x2764), green = new List<Color> { Color.FromRgb(0x00, 0xA0, 0x00) }, blue = new List<Color> { Color.FromRgb(0, 0, 0xFF) };
            Check(smiley.Count > 0 && heart.Count > 0, "the smiley and the heart should have colours in their layers");

            // `inside` in a 240 x 100 picture, 2 pixels to a px: of what's drawn (alpha over half) between x and x + 100 px,
            // the share like any of `colours`, the alpha in the middle of its box, and how much of its box it fills
            var wrong = new List<string>();
            (double Share, int Alpha, double Fills) At(string inside, double x, List<Color> colours)
            {
                ImageSource drawn = Images.Drawing(Encoding.UTF8.GetBytes("<svg " + NS + " viewBox=\"0 0 240 100\">" + inside + "</svg>"), null, out string why);
                if (drawn == null)
                {
                    wrong.Add(Short(inside) + " was refused: " + why);
                    return (0, 0, 0);
                }
                byte[] pixels = Premultiplied(InBox(drawn, 240, 100, new Rect(0, 0, 240, 100), false, 2), out _, out _);
                int all = 0, like = 0, x0 = 480, x1 = -1, y0 = 200, y1 = -1;
                for (int py = 0; py < 200; py++)
                    for (int px = (int)(x * 2); px < (int)(x * 2) + 200 && px < 480; px++)
                    {
                        int i = (py * 480 + px) * 4;
                        if (pixels[i + 3] <= 127) continue;
                        x0 = Math.Min(x0, px);
                        x1 = Math.Max(x1, px);
                        y0 = Math.Min(y0, py);
                        y1 = Math.Max(y1, py);
                        all++;
                        var c = Color.FromRgb((byte)(pixels[i + 2] * 255 / pixels[i + 3]), (byte)(pixels[i + 1] * 255 / pixels[i + 3]), (byte)(pixels[i] * 255 / pixels[i + 3]));
                        if (colours.Exists(o => Like(c, o, 16))) like++;
                    }
                if (all == 0) return (0, 0, 0);
                int middle = ((y0 + y1) / 2 * 480 + (x0 + x1) / 2) * 4;
                return ((double)like / all, pixels[middle + 3], (double)all / ((x1 - x0 + 1) * (y1 - y0 + 1)));
            }
            string Text(string attributes, string inside) => "<text x=\"10\" y=\"80\" font-family=\"Arial\" font-size=\"80\" " + attributes + ">" + inside + "</text>";
            void Expect(string name, (double Share, int Alpha, double Fills) got, double least, double most, int? alpha = null)
            {
                if (got.Share < least || got.Share > most || alpha.HasValue && Math.Abs(got.Alpha - alpha.Value) > 12)
                    wrong.Add(name + ": " + Percent(got.Share) + " (from " + Percent(least) + " to " + Percent(most) + ")" + (alpha.HasValue ? ", alpha " + got.Alpha + " (" + alpha + ")" : ""));
            }
            string face = Chars(0x1F600);

            Expect("a smiley filled green is its own colours", At(Text("fill=\"#00a000\"", face), 0, smiley), 0.5, 1);
            Expect("...and none of the green", At(Text("fill=\"#00a000\"", face), 0, green), 0, 0.01);
            Expect("...while the A beside it is green", At(Text("fill=\"#00a000\"", face + "A"), 120, green), 0.6, 1);
            Expect("a smiley filled with a gradient is its own colours", At("<linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#00a000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient>" +
                                                                         Text("fill=\"url(#g)\"", face), 0, smiley), 0.5, 1);
            Expect("a smiley with no fill and a blue stroke is drawn once, in its colours", At(Text("fill=\"none\" stroke=\"#0000ff\" stroke-width=\"4\"", face), 0, smiley), 0.5, 1);
            Expect("...and none of the blue", At(Text("fill=\"none\" stroke=\"#0000ff\" stroke-width=\"4\"", face), 0, blue), 0, 0.01);
            Expect("a smiley with no fill and no stroke isn't drawn", At(Text("fill=\"none\"", face), 0, smiley), 0, 0, 0);
            Expect("a smiley with fill-opacity 0.5 is half see-through, whole", At(Text("fill=\"#00a000\" fill-opacity=\"0.5\"", face), 0, smiley), 0, 1, 128);
            Expect("...as with a colour half see-through", At(Text("fill=\"rgba(0,160,0,0.5)\"", face), 0, smiley), 0, 1, 128);
            Expect("...but solid under a half see-through stroke, drawn again over it", At(Text("fill=\"#00a000\" stroke=\"#0000ff\" stroke-opacity=\"0.5\"", face), 0, smiley), 0.5, 1, 255);
            Expect("a bold italic smiley is its own colours", At(Text("fill=\"#00a000\" font-weight=\"bold\" font-style=\"italic\"", face), 0, smiley), 0.5, 1);
            Expect("an emoji heart (with U+FE0F) is its own colours", At(Text("fill=\"#00a000\"", Chars(0x2764, 0xFE0F)), 0, heart), 0.5, 1);
            Expect("...and a text heart is the fill's colour", At(Text("fill=\"#00a000\"", Chars(0x2764)), 0, green), 0.6, 1);
            Expect("a smiley before an A placed by an x list is whole and its own colours", At(Text("fill=\"#00a000\" dx=\"0 20\"", face + "A"), 0, smiley), 0.5, 1);
            Expect("a smiley spaced from the letter after it is whole and its own colours", At(Text("fill=\"#00a000\" letter-spacing=\"20\"", face + "A"), 0, smiley), 0.5, 1);
            string family = Chars(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467);
            Expect("a family (ZWJ) is one emoji in its own colours, not the fill's", At(Text("fill=\"#00a000\"", family), 0, green), 0, 0.01);
            var clipped = At("<clipPath id=\"c\">" + Text("", face) + "</clipPath><rect width=\"240\" height=\"100\" fill=\"#0000ff\" clip-path=\"url(#c)\"/>", 0, blue);
            if (clipped.Fills < 0.7) wrong.Add("a smiley in a clip path should clip to all it covers, its eyes and mouth too, not " + Percent(clipped.Fills) + " of its box");

            // 1,000 of the emoji with the most layers (two chars each: the 2,000 characters a picture may draw) are refused
            // in good time when their layers come to more than the 100,000 shapes a picture draws
            int most = 0, biggest = 0;
            foreach (var pair in font.CharacterToGlyphMap)
            {
                int layers = tables.ColoursOf(pair.Value).Count;
                if (pair.Key > 0xFFFF && layers > most) (most, biggest) = (layers, pair.Key);
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Images.Drawing(Encoding.UTF8.GetBytes("<svg " + NS + "><text font-size=\"9\">" + string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(biggest), 1000)) + "</text></svg>"), null, out string refused);
            if (!(watch.ElapsedMilliseconds < 5000 && (most * 1000 > Budget.MostDrawn ? refused?.Contains("more to draw") == true : refused == null)))
                wrong.Add("1,000 emoji of " + most + " layers took " + watch.ElapsedMilliseconds + " ms: " + (refused ?? "drawn"));

            // made-up tables: the smiley as its own outline in red, a star on it in the text's paint (palette entry 0xFFFF),
            // the star green in a green fill; and tables that don't add up (as Read never gives them): the text is drawn,
            // in its fill
            Func<GlyphTypeface, ColourGlyphs> colours = ColourGlyphs.Of;
            ushort glyph = font.CharacterToGlyphMap[0x1F600], star = font.CharacterToGlyphMap[0x2B50];
            byte[] made = ColourFont(new[] { (glyph, new[] { (glyph, (ushort)0), (star, (ushort)0xFFFF) }) }, new[] { Colors.Red });
            try
            {
                ColourGlyphs.Of = f => f.Equals(font) ? ColourGlyphs.Read(new MemoryStream(made), f.GlyphCount) : null;
                Expect("with made-up tables, a smiley is red", At(Text("fill=\"#00a000\"", face), 0, new List<Color> { Colors.Red }), 0.1, 1);
                Expect("...with a star on it in the fill's green (0xFFFF)", At(Text("fill=\"#00a000\"", face), 0, green), 0.1, 1);
                ColourGlyphs.Of = f => f.Equals(font) ? new ColourGlyphs(new[] { glyph }, new ushort[] { 0 }, new ushort[] { 3 }, new[] { glyph }, new ushort[] { 0 }, new[] { 0xFFFF0000u }) : null;
                Expect("with tables that don't add up, a smiley is the fill's colour", At(Text("fill=\"#00a000\"", face), 0, green), 0.3, 1);
            }
            finally
            {
                ColourGlyphs.Of = colours;
            }
            Check(wrong.Count == 0, wrong.Count + " wrong: " + string.Join("; ", wrong));
        }

        static string Safe(string name) => new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Substring(0, Math.Min(60, name.Length));

        // `work` on its own thread with half the stack b0r3d QR's window thread has (1 MB, as the exe asks of
        // Windows): a drawing that overflows it ends the tests, as it would end the program
        static T OnHalfTheStack<T>(Func<T> work)
        {
            T result = default;
            Exception problem = null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    result = work();
                }
                catch (Exception x)
                {
                    problem = x;
                }
            }, 512 * 1024);
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (problem != null) throw new CheckException("on half the stack: " + problem);
            return result;
        }

        // a picture put in a box the website's way (Images.Draw, as QrArt draws a logo or a frame's
        // picture), on a see-through canvas, `scale` pixels to a px
        static BitmapSource InBox(ImageSource picture, double width, double height, Rect box, bool slice, double scale)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                if (slice) dc.PushClip(new RectangleGeometry(box));
                Images.Draw(dc, picture, box, slice);
                if (slice) dc.Pop();
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap((int)Math.Round(width * scale), (int)Math.Round(height * scale), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        sealed class Difference
        {
            public int Pixels, Wrong;
            public Int32Rect Ours, Theirs;   // where alpha is over half; Width 0 for nothing
        }

        static byte[] Premultiplied(BitmapSource picture, out int width, out int height)
        {
            var p = new FormatConvertedBitmap(picture, PixelFormats.Pbgra32, null, 0);
            width = p.PixelWidth;
            height = p.PixelHeight;
            var pixels = new byte[width * height * 4];
            p.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }

        static Difference Differ(BitmapSource ours, BitmapSource theirs)
        {
            byte[] a = Premultiplied(ours, out int w, out int h), b = Premultiplied(theirs, out int w2, out int h2);
            if (w != w2 || h != h2) throw new CheckException("the pictures' sizes differ: " + w + " × " + h + " and " + w2 + " × " + h2);
            var d = new Difference { Pixels = w * h, Ours = Ink(a, w, h), Theirs = Ink(b, w, h) };
            for (int i = 0; i < a.Length; i += 4)
            {
                int most = 0;
                for (int c = 0; c < 4; c++) most = Math.Max(most, Math.Abs(a[i + c] - b[i + c]));
                if (most > WrongBy) d.Wrong++;
            }
            return d;
        }

        static Int32Rect Ink(byte[] pixels, int w, int h)
        {
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (pixels[(y * w + x) * 4 + 3] > 127)
                    {
                        x0 = Math.Min(x0, x);
                        y0 = Math.Min(y0, y);
                        x1 = Math.Max(x1, x);
                        y1 = Math.Max(y1, y);
                    }
            return x1 < 0 ? new Int32Rect() : new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        static bool Near(Int32Rect a, Int32Rect b, int off)
        {
            if (a.Width == 0 || b.Width == 0) return a.Width == b.Width;
            return Math.Abs(a.X - b.X) <= off && Math.Abs(a.Y - b.Y) <= off && Math.Abs(a.X + a.Width - b.X - b.Width) <= off && Math.Abs(a.Y + a.Height - b.Y - b.Height) <= off;
        }
    }
}
