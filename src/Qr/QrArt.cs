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

            // the square of modules cleared for the logo, drawn or not (the website clears it for
            // any logo it has: QrDesign.LogoCounts)
            int first = 0, last = -1;
            if (design.LogoCounts)
            {
                double ratio = design.LogoSize ?? QrDesign.DefaultLogoSize;
                ratio = double.IsNaN(ratio) ? QrDesign.DefaultLogoSize : Math.Max(0, Math.Min(1, ratio));
                double factor = LevelUsed == QrLevel.L ? 0.07 : LevelUsed == QrLevel.M ? 0.15 : LevelUsed == QrLevel.Q ? 0.25 : 0.30;
                int budget = (int)Math.Floor(ratio * factor * n * n);
                int a = (int)Math.Floor(Math.Sqrt(budget));
                if (a % 2 == 0 && a > 1) a--;
                if (a < 1) a = 1;
                int cap = Math.Max(1, (int)Math.Floor(0.3 * n));
                if (a > cap) a = cap % 2 == 0 && cap > 1 ? cap - 1 : cap;
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
            double column = f.Beside ? (f.CaptionWidth > 0 ? f.CaptionWidth : S) : S;
            lines = Wrap(string.IsNullOrEmpty(f.Text) ? QrFrame.DefaultText : f.Text, column);
            double text = lines.Count * LineHeight, p = padding, b = border;
            switch (f.Position)
            {
                case "top":
                    W = S + 2 * p + 2 * b;
                    H = S + text + 3 * p + 2 * b;
                    codeAt = new Point(p + b, text + 2 * p + b);
                    captionX = W / 2;
                    firstBaseline = p + b + CaptionSize;
                    break;
                case "left":
                    W = S + column + 3 * p + 2 * b;
                    H = Math.Max(S, text) + 2 * p + 2 * b;
                    codeAt = new Point(column + 2 * p + b, (H - S) / 2);
                    captionX = p + b + column / 2;
                    firstBaseline = (H - text) / 2 + CaptionSize;
                    break;
                case "right":
                    W = S + column + 3 * p + 2 * b;
                    H = Math.Max(S, text) + 2 * p + 2 * b;
                    codeAt = new Point(p + b, (H - S) / 2);
                    captionX = S + 2 * p + b + column / 2;
                    firstBaseline = (H - text) / 2 + CaptionSize;
                    break;
                default:   // bottom
                    W = S + 2 * p + 2 * b;
                    H = S + text + 3 * p + 2 * b;
                    codeAt = new Point(p + b, p + b);
                    captionX = W / 2;
                    firstBaseline = S + 2 * p + b + CaptionSize;
                    break;
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

        // The website's caption lines: paragraphs at line breaks, words at spaces (runs of spaces
        // count as one), a line full when it would pass the column, widths estimated at 0.6 of the
        // font size per character, as the website's export does.
        internal static List<string> Wrap(string text, double column)
        {
            var result = new List<string>();
            double perChar = 0.6 * CaptionSize;
            foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                string[] words = paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0)
                {
                    result.Add("");
                    continue;
                }
                string line = "";
                foreach (string word in words)
                {
                    if (line.Length > 0 && (line.Length + 1 + word.Length) * perChar > column)
                    {
                        result.Add(line);
                        line = word;
                    }
                    else line = line.Length == 0 ? word : line + " " + word;
                }
                result.Add(line);
            }
            return result;
        }

        public IReadOnlyList<string> CaptionLines => lines;

        // The lines the caption takes in the website's preview of the frame, which sizes its saved
        // pictures: the browser's own line breaking of the text at 18 px, in the caption's font, the
        // same one that draws it (the page's when it has none: Inter if it's installed, else
        // system-ui, Segoe UI; the website doesn't load its Inter web font for the page), in a column
        // of `column` px. Runs of spaces count as one and line breaks are kept (white-space:
        // pre-line), a word wider than the column keeps a line to itself, and a line break at the
        // very end adds no line.
        internal static int PreviewLines(string text, string fontCss, double column)
        {
            CaptionFont font = !string.IsNullOrWhiteSpace(fontCss) ? CaptionFont.For(fontCss) : CaptionFont.Page;
            double Width(string s) => font.Text(s, CaptionSize, Brushes.Black, CultureInfo.InvariantCulture).Width;
            string[] paragraphs = text.Replace("\r\n", "\n").Split('\n');
            int count = paragraphs.Length;
            if (count > 1 && paragraphs[count - 1].Trim(' ', '\t').Length == 0) count--;
            int result = 0;
            for (int p = 0; p < count; p++)
            {
                string[] words = paragraphs[p].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                result++;
                string line = "";
                foreach (string word in words)
                {
                    string longer = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && Width(longer) > column)
                    {
                        result++;
                        line = word;
                    }
                    else line = longer;
                }
            }
            return result;
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
                    for (int i = 0; i < lines.Count; i++)
                    {
                        // as browsers draw the website's SVG: each line after the first moves down a
                        // line, but an empty line has no letter for its move to act on, so it moves
                        // nothing and its room is left at the bottom; emoji in their colours
                        if (lines[i].Length == 0) continue;
                        if (i > 0) y += LineHeight;
                        FormattedText text = font.Text(lines[i], CaptionSize, ink, CultureInfo.CurrentUICulture);
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
