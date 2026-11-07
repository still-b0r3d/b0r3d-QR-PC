using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Design;

namespace B0r3dQR.Qr
{
    // A design drawn: what qr.b0r3d.org exports for it, measured in the export's own px (the code's
    // side S, quiet zone included; with a frame, the whole frame W × H). The preview, the PNG, JPG
    // and PDF files, the clipboard and the scan check are all drawn from one WPF drawing; the SVG
    // file writes the same shapes. Shapes, logo room and frame layout follow the website's export.
    sealed class QrArt
    {
        public readonly QrDesign Design;
        public readonly QrCode Code;
        public readonly QrLevel LevelUsed;
        public readonly bool LevelRaised;       // L or M raised to Q for the logo
        public readonly double S, W, H;         // px
        public readonly string LogoProblem;     // why a design's logo isn't drawn, or null
        public readonly string FrameImageProblem;
        public readonly int Cleared;            // modules across the square cleared for the logo (0: no logo)
        public readonly double LogoModules;     // the logo's box, modules across
        public readonly double LogoSpaceUsed;   // the space it actually got, modules

        readonly double margin, module;         // the quiet zone in modules; px per module
        readonly Outline dots, rings, centres;
        readonly ImageSource logo, frameImage;  // a bitmap, or an SVG drawing (Images.Draw places either)
        readonly Rect logoBox;                  // px, from the code's top-left
        readonly Point codeAt;                  // the code's top-left in the picture
        readonly double padding, border, radius;
        readonly List<string> lines = new List<string>();
        readonly double captionX, firstBaseline;

        public const double CaptionSize = 18, LineHeight = CaptionSize * 1.2;

        public QrArt(QrDesign design)
        {
            Design = design;
            logo = Images.Load(design.Logo, out LogoProblem);
            LevelUsed = design.LevelUsed;
            LevelRaised = LevelUsed != design.Level;
            Code = Made(design.Data, LevelUsed, design.Version, design.Utf8Marker);
            int n = Code.Size;
            margin = double.IsNaN(design.Margin) ? 0 : Math.Max(0, design.Margin);
            S = Math.Max(1, Math.Min(design.Width, design.Height));
            module = S / (n + 2 * margin);

            // The square of modules cleared for the logo, drawn or not (the website clears it for
            // any logo it has: QrDesign.LogoCounts). The logo may take its share (LogoSize, 0 to 1)
            // of the modules the level can lose; the square is the biggest that holds no more than
            // that, at most three tenths of the code across, with an odd side so it has a middle
            // module, and never less than one module.
            int first = 0, last = -1;
            if (design.LogoCounts)
            {
                double share = design.LogoSize ?? QrDesign.DefaultLogoSize;
                if (double.IsNaN(share)) share = QrDesign.DefaultLogoSize;
                share = Math.Max(0, Math.Min(1, share));
                int room = (int)Math.Floor(share * Recoverable(LevelUsed) * n * n);
                int side = Math.Min((int)Math.Sqrt(room), (int)(0.3 * n));
                int a = Math.Max(1, side % 2 == 1 ? side : side - 1);
                Cleared = a;
                first = (n - a) / 2;
                last = first + a - 1;
                double space = double.IsNaN(design.LogoSpace) ? 0 : Math.Max(0, Math.Min(design.LogoSpace, (a - 1) / 2.0));
                LogoSpaceUsed = space;
                LogoModules = a - 2 * space;
                double box = LogoModules * module, at = (margin + first + space) * module;
                logoBox = new Rect(at, at, box, box);
            }

            dots = Dots(Code, design.DotsType, margin, first, last);
            rings = new Outline();
            centres = new Outline();
            foreach (var (r, c) in new[] { (0, 0), (0, n - 7), (n - 7, 0) })
            {
                Ring(rings, design.CornersSquareType, margin + c, margin + r);
                Centre(centres, design.CornersDotType, margin + c + 2, margin + r + 2);
            }

            W = H = S;
            QrFrame f = design.Frame;
            if (f == null) return;
            frameImage = Images.Load(f.BackgroundImage, out FrameImageProblem);
            padding = Css.Pixels(f.Padding, 12);
            border = Css.Pixels(f.BorderWidth, 2);
            radius = Css.Pixels(f.BorderRadius, 8);

            // The frame holds two boxes in a line, the code (S square) and the caption's block (its
            // column wide, its lines tall): side by side for a caption on the left or right, one above
            // the other otherwise (any other position is drawn as "bottom"). Along that line
            // they're the padding apart and padding + border in from the frame's ends; across it the
            // frame is as big as the bigger box, padding + border in, and side by side both boxes are
            // centred in it. (Sums are added in the order the website adds them, so every size comes
            // out to the same last digit as in its files.)
            bool beside = f.Beside, captionFirst = f.Position == "top" || f.Position == "left";
            double column = beside && f.CaptionWidth > 0 ? f.CaptionWidth : S;
            lines = Wrap(string.IsNullOrEmpty(f.Text) ? QrFrame.DefaultText : f.Text, column);
            double tall = lines.Count * LineHeight;
            double block = beside ? column : tall;                   // the caption's block, along the line
            double along = S + block + 3 * padding + 2 * border;      // the frame, along the line
            double across = Math.Max(S, beside ? tall : S) + 2 * padding + 2 * border;
            double firstAt = padding + border;                        // where the first box starts along the line
            double secondAt = (captionFirst ? block : S) + 2 * padding + border;
            double codeAlong = captionFirst ? secondAt : firstAt, blockAlong = captionFirst ? firstAt : secondAt;
            if (beside)
            {
                W = along;
                H = across;
                codeAt = new Point(codeAlong, (H - S) / 2);
                captionX = blockAlong + column / 2;
                firstBaseline = (H - tall) / 2 + CaptionSize;
            }
            else
            {
                W = across;
                H = along;
                codeAt = new Point(firstAt, codeAlong);
                captionX = W / 2;
                firstBaseline = blockAlong + CaptionSize;
            }
        }

        // what a code at each level can lose and still be read, as the QR standard has it
        static double Recoverable(QrLevel level)
        {
            switch (level)
            {
                case QrLevel.L: return 0.07;
                case QrLevel.M: return 0.15;
                case QrLevel.Q: return 0.25;
                default: return 0.30;
            }
        }

        // The code for a design, made again only when its text or settings change (a caption or a
        // colour changes nothing in it): a version 40 code takes a moment to make.
        static readonly object codeGate = new object();
        static QrCode lastCode;
        static bool lastMarker;

        static QrCode Made(string text, QrLevel level, int version, bool marker)
        {
            lock (codeGate)
            {
                QrCode c = lastCode;
                if (c != null && c.Text == text && c.Level == level && c.AskedVersion == Math.Max(0, Math.Min(40, version)) && lastMarker == marker) return c;
            }
            QrCode made = QrCode.Make(text, level, version, marker);
            lock (codeGate)
            {
                lastCode = made;
                lastMarker = marker;
            }
            return made;
        }

        // ---------- the shapes, in modules from the picture's top-left (quiet zone included) ----------

        static bool InFinder(int r, int c, int n) => (r < 7 && c < 7) || (r < 7 && c >= n - 7) || (r >= n - 7 && c < 7);

        static Outline Dots(QrCode code, string type, double margin, int first, int last)
        {
            int n = code.Size;
            var o = new Outline();
            bool Dark(int r, int c) => r >= 0 && c >= 0 && r < n && c < n && code[r, c];
            bool Drawn(int r, int c) => code[r, c] && !InFinder(r, c, n) && !(r >= first && r <= last && c >= first && c <= last);
            switch (type)
            {
                case "dots":
                case "rounded":
                case "extra-rounded":
                case "classy":
                case "classy-rounded":
                    for (int r = 0; r < n; r++)
                        for (int c = 0; c < n; c++)
                        {
                            if (!Drawn(r, c)) continue;
                            double x = margin + c, y = margin + r;
                            if (type == "dots")
                            {
                                o.Circle(x + 0.5, y + 0.5, 0.5);
                                continue;
                            }
                            if (type == "rounded")
                            {
                                o.RoundedRectangle(x, y, 1, 1, 0.25, 0.25, 0.25, 0.25);
                                continue;
                            }
                            bool up = Dark(r - 1, c), down = Dark(r + 1, c), left = Dark(r, c - 1), right = Dark(r, c + 1);
                            double tl = !up && !left ? 1 : 0, tr = !up && !right ? 1 : 0, br = !down && !right ? 1 : 0, bl = !down && !left ? 1 : 0;
                            if (type == "extra-rounded")
                                o.RoundedRectangle(x, y, 1, 1, tl * 0.5, tr * 0.5, br * 0.5, bl * 0.5);
                            else
                            {
                                double rr = type == "classy" ? 0.3 : 0.5;
                                o.RoundedRectangle(x, y, 1, 1, tl * rr, 0, br * rr, 0);
                            }
                        }
                    break;
                default:   // square: dark modules side by side in a row make one rectangle
                    for (int r = 0; r < n; r++)
                        for (int c = 0; c < n; )
                        {
                            if (!Drawn(r, c)) { c++; continue; }
                            int start = c;
                            while (c < n && Drawn(r, c)) c++;
                            o.Rectangle(margin + start, margin + r, c - start, 1);
                        }
                    break;
            }
            return o;
        }

        // a finder's ring: 7 x 7 with a 5 x 5 hole
        static void Ring(Outline o, string type, double x, double y)
        {
            switch (type)
            {
                case "square":
                    o.Rectangle(x, y, 7, 7);
                    o.Rectangle(x + 1, y + 1, 5, 5, true);
                    break;
                case "dot":
                    o.Circle(x + 3.5, y + 3.5, 3.5);
                    o.Circle(x + 3.5, y + 3.5, 2.5, true);
                    break;
                case "rounded":
                    o.RoundedRectangle(x, y, 7, 7, 0.875, 0.875, 0.875, 0.875);
                    o.RoundedRectangle(x + 1, y + 1, 5, 5, 0.625, 0.625, 0.625, 0.625, true);
                    break;
                default:   // extra-rounded, and anything the website doesn't know
                    o.RoundedRectangle(x, y, 7, 7, 1.75, 1.75, 1.75, 1.75);
                    o.RoundedRectangle(x + 1, y + 1, 5, 5, 1.25, 1.25, 1.25, 1.25, true);
                    break;
            }
        }

        // a finder's 3 x 3 centre
        static void Centre(Outline o, string type, double x, double y)
        {
            switch (type)
            {
                case "dot":
                    o.Circle(x + 1.5, y + 1.5, 1.5);
                    break;
                case "rounded":
                    o.RoundedRectangle(x, y, 3, 3, 0.75, 0.75, 0.75, 0.75);
                    break;
                default:   // square, and anything else
                    o.Rectangle(x, y, 3, 3);
                    break;
            }
        }

        // ---------- the caption ----------

        // The caption's lines in the files, as the website lays them out: a line break (LF) always
        // ends a line; otherwise words (cut at spaces, a run of spaces counting as one) go on a line
        // for as long as it stays within the column, every character guessed at 0.6 of the font size.
        // A word too long for the column gets a line to itself, and a paragraph with no words is an
        // empty line. A CR is a character like any other here, as on the website: a CR LF break
        // leaves its CR at the end of the line, where it counts, so a line that only just fits
        // without it wraps a word sooner; a lone CR is part of the word it's in.
        internal static List<string> Wrap(string text, double column)
        {
            double perCharacter = 0.6 * CaptionSize;
            var lines = new List<string>();
            var line = new StringBuilder();
            foreach (string paragraph in text.Split('\n'))
            {
                foreach (string word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    bool tooWide = line.Length > 0 && (line.Length + 1 + word.Length) * perCharacter > column;
                    if (tooWide)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                    }
                    if (line.Length > 0) line.Append(' ');
                    line.Append(word);
                }
                lines.Add(line.ToString());
                line.Clear();
            }
            return lines;
        }

        public IReadOnlyList<string> CaptionLines => lines;

        // What the website's SVG shows of each line once a browser reads it: the file's CRs come in
        // as line breaks, and SVG text draws a line break, a tab and a space all as white space, a
        // run of it as one space (across lines too) and none at the very start or end of the
        // caption. A line left with nothing has no letter for its move down to act on, like an
        // empty line. Without a CR a caption is drawn line by line as it is, as b0r3d QR always has
        // (its lines have single spaces between words, and any tabs WPF draws its own way).
        internal static List<string> DrawnLines(IReadOnlyList<string> lines)
        {
            var drawn = new List<string>(lines);
            if (!lines.Any(l => l.IndexOf('\r') >= 0)) return drawn;
            bool afterSpace = true;   // so white space at the start is left out
            int last = -1;            // the last line with something on it
            for (int i = 0; i < drawn.Count; i++)
            {
                var s = new StringBuilder(drawn[i].Length);
                foreach (char c in drawn[i])
                {
                    bool space = c == ' ' || c == '\t' || c == '\r';
                    if (space && afterSpace) continue;
                    s.Append(space ? ' ' : c);
                    afterSpace = space;
                }
                drawn[i] = s.ToString();
                if (s.Length > 0) last = i;
            }
            if (last >= 0 && drawn[last].EndsWith(" ", StringComparison.Ordinal)) drawn[last] = drawn[last].Substring(0, drawn[last].Length - 1);
            return drawn;
        }

        // The lines the caption takes in the website's preview of the frame, which sizes its saved
        // pictures: the browser's own line breaking of the text at 18 px, in the caption's font, the
        // same one that draws it (the page's when it has none: Inter if it's installed, else
        // system-ui, Segoe UI; the website doesn't load its Inter web font for the page), in a column
        // of `column` px. Runs of spaces count as one and line breaks are kept (white-space:
        // pre-line), a word wider than the column keeps a line to itself, and a line break at the
        // very end adds no line. A CR is a space there: a CR LF is just a line break and a lone CR
        // goes between words. A blank last line (white space with no line break after it) takes no
        // room, so a caption of CRs alone takes none. One of spaces or tabs alone keeps the line
        // b0r3d QR has always given it, though the website's preview probably gives it none too (not
        // measured yet): a known difference, left as it was.
        internal static int PreviewLines(string text, string fontCss, double column)
        {
            CaptionFont font = !string.IsNullOrWhiteSpace(fontCss) ? CaptionFont.For(fontCss) : CaptionFont.Page;
            bool Fits(string line) => !(font.Text(line, CaptionSize, Brushes.Black, CultureInfo.InvariantCulture).Width > column);
            List<string> paragraphs = text.Split('\n').ToList();
            string end = paragraphs[paragraphs.Count - 1];
            if (end.Trim(' ', '\t', '\r').Length == 0 && (paragraphs.Count > 1 || end.IndexOf('\r') >= 0)) paragraphs.RemoveAt(paragraphs.Count - 1);
            int lines = 0;
            foreach (string paragraph in paragraphs)
            {
                lines++;   // every paragraph takes a line, an empty one too
                string[] words = paragraph.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                string line = words.Length > 0 ? words[0] : "";   // a line's first word is on it, however wide
                for (int w = 1; w < words.Length; w++)
                {
                    string joined = line + " " + words[w];
                    if (Fits(joined)) line = joined;
                    else
                    {
                        lines++;
                        line = words[w];
                    }
                }
            }
            return lines;
        }

        // whether a font family is installed on this PC (the website's web fonts are in the exe instead:
        // CaptionFont); the tests can pretend one is
        internal static Func<string, bool> Installed = OnThisPc;

        static HashSet<string> installed;

        static bool OnThisPc(string family)
        {
            if (installed == null)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FontFamily f in Fonts.SystemFontFamilies) names.Add(f.Source);
                installed = names;
            }
            return installed.Contains(family);
        }

        // ---------- as a WPF drawing, in the picture's px ----------

        public Drawing ToDrawing()
        {
            var group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                QrFrame f = Design.Frame;
                if (f == null)
                {
                    bool clip = Design.BorderRadius > 0;
                    if (clip) dc.PushClip(new RectangleGeometry(new Rect(0, 0, S, S), Design.BorderRadius, Design.BorderRadius));
                    DrawCode(dc, new Point(0, 0));
                    if (clip) dc.Pop();
                }
                else
                {
                    var box = new Rect(border / 2, border / 2, Math.Max(0, W - border), Math.Max(0, H - border));
                    Pen pen = border > 0 ? new Pen(Brush(f.BorderColor), border) : null;
                    dc.DrawRoundedRectangle(Brush(f.BackgroundColor), pen, box, radius, radius);
                    if (frameImage != null)
                    {
                        dc.PushClip(new RectangleGeometry(new Rect(0, 0, W, H), radius, radius));
                        Images.Draw(dc, frameImage, new Rect(0, 0, W, H), true);
                        dc.Pop();
                        if (pen != null) dc.DrawRoundedRectangle(null, pen, box, radius, radius);
                    }
                    DrawCode(dc, codeAt);
                    CaptionFont font = CaptionFont.For(f.FontFamily);   // with none: Arial, the sans-serif the website's file names
                    Brush ink = Brush(f.TextColor);
                    double y = firstBaseline;
                    List<string> drawn = DrawnLines(lines);
                    for (int i = 0; i < drawn.Count; i++)
                    {
                        // as browsers draw the website's SVG: each line after the first moves down a
                        // line, but an empty line has no letter for its move to act on, so it moves
                        // nothing and its room is left at the bottom; a line is centred with any space
                        // it ends in; emoji in their colours
                        if (drawn[i].Length == 0) continue;
                        if (i > 0) y += LineHeight;
                        FormattedText text = font.Text(drawn[i], CaptionSize, ink, CultureInfo.CurrentUICulture);
                        ColourGlyphs.DrawText(dc, text, new Point(captionX - text.WidthIncludingTrailingWhitespace / 2, y - text.Baseline));
                    }
                }
            }
            group.Freeze();
            return group;
        }

        void DrawCode(DrawingContext dc, Point at)
        {
            if (!Design.Transparent) dc.DrawRectangle(Brush(Design.Background), null, new Rect(at.X, at.Y, S, S));
            dc.DrawGeometry(Brush(Design.DotsColor), null, dots.ToGeometry(module, at.X, at.Y));
            dc.DrawGeometry(Brush(Design.CornersSquareColor), null, rings.ToGeometry(module, at.X, at.Y));
            dc.DrawGeometry(Brush(Design.CornersDotColor), null, centres.ToGeometry(module, at.X, at.Y));
            if (logo != null)
            {
                Images.Draw(dc, logo, new Rect(at.X + logoBox.X, at.Y + logoBox.Y, logoBox.Width, logoBox.Height), false);
            }
        }

        static Brush Brush(string css)
        {
            var b = new SolidColorBrush(Css.Paint(css));
            b.Freeze();
            return b;
        }

        // The drawing as pixels: `scale` pixels to one of the picture's px, on a canvas of the given
        // size (the website crops less than a pixel at the right and bottom). `under` fills the
        // canvas first (a JPG's background), or null to keep see-through parts.
        public BitmapSource Render(double scale, int pixelWidth, int pixelHeight, Color? under = null, double dpi = 96)
        {
            pixelWidth = Math.Max(1, pixelWidth);
            pixelHeight = Math.Max(1, pixelHeight);
            double k = 96 / dpi;   // a bitmap at `dpi` makes each of WPF's 96ths of an inch dpi/96 pixels
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                if (under.HasValue) dc.DrawRectangle(new SolidColorBrush(under.Value), null, new Rect(0, 0, pixelWidth * k, pixelHeight * k));
                dc.PushTransform(new ScaleTransform(scale * k, scale * k));
                dc.DrawDrawing(ToDrawing());
                dc.Pop();
            }
            var picture = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
            picture.Render(visual);
            picture.Freeze();
            return picture;
        }

        // ---------- as an SVG file ----------

        // the SVG the website writes; `physical` ("40mm" and its height) for a print size, or null
        public string ToSvg(string physicalWidth = null, string physicalHeight = null)
        {
            var s = new StringBuilder();
            string w = N(W), h = N(H);
            s.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"");
            s.Append(" width=\"").Append(physicalWidth ?? w).Append("\" height=\"").Append(physicalHeight ?? h).Append("\"");
            s.Append(" viewBox=\"0 0 ").Append(w).Append(' ').Append(h).Append("\">");
            QrFrame f = Design.Frame;
            if (f == null)
            {
                bool clip = Design.BorderRadius > 0;
                if (clip)
                {
                    string r = N(Design.BorderRadius);
                    s.Append("<defs><clipPath id=\"qr-export-clip\"><rect width=\"").Append(w).Append("\" height=\"").Append(h)
                     .Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r).Append("\"/></clipPath></defs><g clip-path=\"url(#qr-export-clip)\">");
                }
                if (!Design.Transparent) s.Append("<rect width=\"").Append(w).Append("\" height=\"").Append(h).Append("\" fill=\"").Append(Attr(Design.Background)).Append("\"/>");
                CodePaths(s, 0, 0);
                LogoImage(s, 0, 0);
                if (clip) s.Append("</g>");
            }
            else
            {
                string r = N(radius), b = N(border);
                s.Append("<rect x=\"").Append(N(border / 2)).Append("\" y=\"").Append(N(border / 2)).Append("\" width=\"").Append(N(Math.Max(0, W - border)))
                 .Append("\" height=\"").Append(N(Math.Max(0, H - border))).Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
                 .Append("\" fill=\"").Append(Attr(f.BackgroundColor)).Append("\" stroke=\"").Append(Attr(f.BorderColor)).Append("\" stroke-width=\"").Append(b).Append("\"/>");
                if (frameImage != null || IsData(f.BackgroundImage))   // a picture b0r3d QR can't draw (a broken one) still goes in the file
                {
                    s.Append("<defs><clipPath id=\"frame-bg-clip\"><rect width=\"").Append(w).Append("\" height=\"").Append(h).Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
                     .Append("\"/></clipPath></defs><image x=\"0\" y=\"0\" width=\"").Append(w).Append("\" height=\"").Append(h)
                     .Append("\" preserveAspectRatio=\"xMidYMid slice\" clip-path=\"url(#frame-bg-clip)\" href=\"").Append(Attr(f.BackgroundImage))
                     .Append("\" xlink:href=\"").Append(Attr(f.BackgroundImage)).Append("\"/>");
                    s.Append("<rect x=\"").Append(N(border / 2)).Append("\" y=\"").Append(N(border / 2)).Append("\" width=\"").Append(N(Math.Max(0, W - border)))
                     .Append("\" height=\"").Append(N(Math.Max(0, H - border))).Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
                     .Append("\" fill=\"none\" stroke=\"").Append(Attr(f.BorderColor)).Append("\" stroke-width=\"").Append(b).Append("\"/>");
                }
                s.Append("<g transform=\"translate(").Append(N(codeAt.X)).Append(' ').Append(N(codeAt.Y)).Append(")\">");
                if (!Design.Transparent) s.Append("<rect class=\"qr-bg\" width=\"").Append(N(S)).Append("\" height=\"").Append(N(S)).Append("\" fill=\"").Append(Attr(Design.Background)).Append("\"/>");
                CodePaths(s, 0, 0);
                s.Append("</g>");
                LogoImage(s, codeAt.X, codeAt.Y);
                // the website's lines: each moved down from the one before (dy), so a viewer lays out
                // empty lines exactly as it does in the website's own files
                string family = string.IsNullOrEmpty(f.FontFamily) ? "sans-serif" : f.FontFamily;
                s.Append("<text x=\"").Append(N(captionX)).Append("\" y=\"").Append(N(firstBaseline)).Append("\" font-family=\"").Append(Attr(family))
                 .Append("\" font-size=\"").Append(N(CaptionSize)).Append("\" fill=\"").Append(Attr(f.TextColor)).Append("\" text-anchor=\"middle\">");
                for (int i = 0; i < lines.Count; i++)
                    s.Append("<tspan x=\"").Append(N(captionX)).Append("\" dy=\"").Append(i == 0 ? "0" : N(LineHeight)).Append("\">").Append(Text(lines[i])).Append("</tspan>");
                s.Append("</text>");
            }
            s.Append("</svg>\n");
            return s.ToString();
        }

        void CodePaths(StringBuilder s, double x, double y)
        {
            s.Append("<path class=\"qr-dots\" fill=\"").Append(Attr(Design.DotsColor)).Append("\" fill-rule=\"evenodd\" d=\"").Append(dots.ToSvgPath(module, x, y)).Append("\"/>");
            s.Append("<path class=\"qr-corner-square\" fill=\"").Append(Attr(Design.CornersSquareColor)).Append("\" fill-rule=\"evenodd\" d=\"").Append(rings.ToSvgPath(module, x, y)).Append("\"/>");
            s.Append("<path class=\"qr-corner-dot\" fill=\"").Append(Attr(Design.CornersDotColor)).Append("\" d=\"").Append(centres.ToSvgPath(module, x, y)).Append("\"/>");
        }

        // the logo, as the website writes it: an SVG one as it is, and also one b0r3d QR can't draw
        // itself, since a viewer might; never one on the web, which b0r3d QR leaves out
        void LogoImage(StringBuilder s, double x, double y)
        {
            if (!Design.LogoCounts) return;
            s.Append("<image class=\"qr-logo\" x=\"").Append(N(x + logoBox.X)).Append("\" y=\"").Append(N(y + logoBox.Y)).Append("\" width=\"").Append(N(logoBox.Width))
             .Append("\" height=\"").Append(N(logoBox.Height)).Append("\" preserveAspectRatio=\"xMidYMid meet\" href=\"").Append(Attr(Design.Logo))
             .Append("\" xlink:href=\"").Append(Attr(Design.Logo)).Append("\"/>");
        }

        internal static string N(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
            double r = Math.Round(v, 4);
            return (r == 0 ? 0 : r).ToString("0.####", CultureInfo.InvariantCulture);
        }

        static bool IsData(string v) => v != null && v.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

        static string Attr(string v) => Text(v).Replace("\"", "&quot;");

        // text for the SVG: escaped, and without the characters XML can't hold at all (control
        // characters from a design file, half an emoji), which would make the file unreadable
        static string Text(string v)
        {
            var s = new StringBuilder((v ?? "").Length);
            string t = v ?? "";
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (char.IsHighSurrogate(c) && i + 1 < t.Length && char.IsLowSurrogate(t[i + 1]))
                {
                    s.Append(c).Append(t[++i]);
                    continue;
                }
                if ((c < ' ' && c != '\t' && c != '\n' && c != '\r') || char.IsSurrogate(c) || c == '￾' || c == '￿') continue;
                s.Append(c == '&' ? "&amp;" : c == '<' ? "&lt;" : c == '>' ? "&gt;" : c.ToString());
            }
            return s.ToString();
        }
    }
}
