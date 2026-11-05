using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace B0r3dQR.Svg
{
    // An SVG drawing, drawn the way Chromium draws an SVG picture, which is how the website's PNG,
    // JPG and PDF files get their SVG logos and frame pictures: as a still picture (no scripts, no
    // animation, nothing from outside the file but data: URLs, installed fonts only). Read once,
    // then drawn into any box as WPF vector drawing, so prints stay sharp.
    //
    // Chromium puts an SVG picture into the website's <image> box (meet for the logo, slice for a
    // frame's picture) by making the box the drawing's own viewport: its viewBox and its own
    // preserveAspectRatio fit it in, and the <image>'s meet or slice changes nothing. Without a
    // viewBox, one is made from its width and height (100% when they're missing or percentages) and
    // stretched to the box. Checked against Chromium 156 (Edge), see the tests.
    sealed class SvgImage
    {
        public const int MaxBytes = 16 * 1024 * 1024;
        const int MostNested = 3;           // SVG pictures inside SVG pictures

        internal readonly Element Root;
        internal readonly StyleSheet Sheet = new StyleSheet();
        internal readonly Dictionary<string, Element> Ids = new Dictionary<string, Element>();
        internal readonly int Depth;

        // the pictures its <image>s name, read once (a BitmapSource, an SvgImage, or null), and how
        // many pixels they hold (lock Pictures for both)
        internal readonly Dictionary<Element, object> Pictures = new Dictionary<Element, object>();
        internal long PicturePixels;

        // the last few drawings made, by viewport size, and how deep inside another picture each was
        // made (Budget's Deep and Masks there; 0 for a picture on its own)
        readonly List<(Size Size, int Deep, int Masks, Drawing Drawing)> made = new List<(Size, int, int, Drawing)>();

        SvgImage(Element root, int depth)
        {
            Root = root;
            Depth = depth;
            var walk = new Stack<Element>();
            walk.Push(root);
            var styles = new List<Element>();
            while (walk.Count > 0)
            {
                Element e = walk.Pop();
                string id = e["id"];
                if (id != null && !Ids.ContainsKey(id)) Ids[id] = e;
                if (e.Is("style")) styles.Add(e);
                for (int i = e.Children.Count - 1; i >= 0; i--) walk.Push(e.Children[i]);
            }
            // ids: the first in the file's order wins, as in browsers (the walk above goes in that order)
            foreach (Element style in styles)
            {
                string type = style["type"];
                if (type != null && type.Trim().Length > 0 && !type.Trim().Equals("text/css", StringComparison.OrdinalIgnoreCase)) continue;
                string media = style["media"];
                if (media != null && media.Trim().Length > 0 && !(media.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) || media.Trim().Equals("screen", StringComparison.OrdinalIgnoreCase))) continue;
                var css = new StringBuilder();
                foreach (object o in style.Content)
                    if (o is string text) css.Append(text);
                Sheet.Add(css.ToString());
            }
        }

        // An SVG file's bytes, read; SvgException with a plain reason when it isn't one a browser draws.
        public static SvgImage Read(byte[] bytes, string charset = null, int depth = 0)
        {
            if (depth > MostNested) throw new SvgException("it has SVG pictures nested more than " + MostNested + " deep");
            if (bytes.Length > MaxBytes) throw new SvgException("it's over " + MaxBytes / (1024 * 1024) + " MB");
            // the style steps start from nothing for a top-level picture, before even its size is worked
            // out (which applies styles too): the count left by a picture refused before it mustn't count
            // against this one. A picture inside another shares the count of the picture it's in.
            if (depth == 0) StyleSheet.Fresh();
            Element root = Xml.Parse(Xml.Decode(bytes, charset));
            if (root.Name != "svg") throw new SvgException("it isn't an SVG drawing (it's " + Xml.Quote((root.Prefix.Length > 0 ? root.Prefix + ":" : "") + root.Name) + ")");
            if (root.Namespace != Xml.SvgSpace)
                throw new SvgException(root.Namespace.Length == 0 ? "its <svg> has no xmlns=\"" + Xml.SvgSpace + "\", which browsers need to draw it" : "its <svg> isn't in SVG's namespace");
            return new SvgImage(root, depth);
        }

        // Does this look like an SVG file: is its first element <svg> (or <prefix:svg>), after any XML
        // declaration, comments and DOCTYPE? (Not a PNG, nor a web page with a drawing in it.)
        public static bool Looks(byte[] bytes)
        {
            string s = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 65536));
            int at = 0;
            while (true)
            {
                while (at < s.Length && (s[at] == (char)0xFEFF || char.IsWhiteSpace(s[at]))) at++;
                if (at >= s.Length || s[at] != '<') return false;
                string end = string.CompareOrdinal(s, at, "<?", 0, 2) == 0 ? "?>" : string.CompareOrdinal(s, at, "<!--", 0, 4) == 0 ? "-->" : null;
                if (end == null && string.CompareOrdinal(s, at, "<!DOCTYPE", 0, 9) == 0)
                {
                    int bracket = s.IndexOf('[', at), close = s.IndexOf('>', at);
                    end = bracket >= 0 && bracket < close ? "]>" : ">";
                }
                if (end == null) break;
                int found = s.IndexOf(end, at + 2, StringComparison.Ordinal);
                if (found < 0) return false;
                at = found + end.Length;
            }
            int name = at + 1, stop = name;
            while (stop < s.Length && !char.IsWhiteSpace(s[stop]) && s[stop] != '>' && s[stop] != '/') stop++;
            string tag = s.Substring(name, stop - name);
            return tag == "svg" || tag.EndsWith(":svg", StringComparison.Ordinal);
        }

        // ---------- data: URLs ----------

        // The bytes in a data: URL, as browsers read one: a media type and parameters, base64 (spaces
        // allowed, the padding optional) or percent-encoded text, which ends at a # as any address does.
        public static bool DataUrl(string url, out byte[] bytes, out string mediaType, out string charset)
        {
            bytes = null;
            mediaType = charset = null;
            string v = (url ?? "").Trim();
            if (!v.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
            int comma = v.IndexOf(',');
            if (comma < 0) return false;
            int hash = v.IndexOf('#', comma);
            string body = hash < 0 ? v.Substring(comma + 1) : v.Substring(comma + 1, hash - comma - 1);
            string[] header = v.Substring(5, comma - 5).Split(';');
            mediaType = header[0].Trim().ToLowerInvariant();
            bool base64 = false;
            for (int i = 1; i < header.Length; i++)
            {
                string p = header[i].Trim();
                if (p.Equals("base64", StringComparison.OrdinalIgnoreCase) && i == header.Length - 1) base64 = true;
                else if (p.StartsWith("charset=", StringComparison.OrdinalIgnoreCase)) charset = p.Substring(8).Trim('"', ' ');
            }
            if (base64)
            {
                var b = new StringBuilder(body.Length);
                foreach (char c in Uri.UnescapeDataString(body))
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r' && c != '\f') b.Append(c);
                if (b.Length % 4 == 0 && b.Length > 0 && b[b.Length - 1] == '=') b.Length -= b[b.Length - 2] == '=' ? 2 : 1;
                if (b.Length % 4 == 1) return false;
                while (b.Length % 4 != 0) b.Append('=');
                try
                {
                    bytes = Convert.FromBase64String(b.ToString());
                }
                catch (FormatException)
                {
                    return false;
                }
                return true;
            }
            // percent-encoded: the escapes are bytes, the rest is the address's own characters in UTF-8
            var raw = new List<byte>(body.Length);
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] == '%' && i + 2 < body.Length && Hex(body[i + 1]) >= 0 && Hex(body[i + 2]) >= 0)
                {
                    raw.Add((byte)(Hex(body[i + 1]) * 16 + Hex(body[i + 2])));
                    i += 2;
                }
                else if (char.IsSurrogate(body[i]) && i + 1 < body.Length)
                {
                    raw.AddRange(Encoding.UTF8.GetBytes(body.Substring(i, 2)));
                    i++;
                }
                else raw.AddRange(Encoding.UTF8.GetBytes(body[i].ToString()));
            }
            bytes = raw.ToArray();
            return true;
        }

        static int Hex(char c) => c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

        // ---------- sizes ----------

        // The size Chromium gives the picture on its own (an <img>'s natural size): its width and
        // height when they're lengths (not percentages); one of them and the viewBox's shape; else
        // the viewBox's shape in 300 × 150; else 300 × 150.
        public Size NaturalSize
        {
            get
            {
                var (w, h, ratio) = Own;
                if (w.HasValue && h.HasValue) return new Size(w.Value, h.Value);
                if (w.HasValue) return new Size(w.Value, ratio.HasValue ? w.Value / ratio.Value : 150);
                if (h.HasValue) return new Size(ratio.HasValue ? h.Value * ratio.Value : 300, h.Value);
                if (ratio.HasValue) return ratio.Value >= 2 ? new Size(300, 300 / ratio.Value) : new Size(150 * ratio.Value, 150);
                return new Size(300, 150);
            }
        }

        static double? Natural(string value, Style style)
        {
            if (!Length.TryParse(value, out Length l) || l.IsPercent) return null;
            return Math.Max(0, l.Px(0, style.FontSize, l.Unit == "ex" ? Renderer.XHeight(style) : 0));
        }

        // its root's width and height when they're lengths, and its viewBox's shape when it has one:
        // worked out once (a picture inside another is asked again for each copy of it)
        (double? Width, double? Height, double? Ratio) Own
        {
            get
            {
                if (own == null)
                {
                    Style style = Style.Of(Root, Style.Initial, Sheet);
                    own = (Natural(Root["width"], style), Natural(Root["height"], style), ViewBox(Root, out Rect box) && box.Width > 0 && box.Height > 0 ? box.Width / box.Height : (double?)null);
                }
                return own.Value;
            }
        }

        (double?, double?, double?)? own;

        // The size Chromium gives it in a room of `room` when it's stretched (an <image> with
        // preserveAspectRatio="none"): its own width and height, one of them and its shape, its shape
        // as big as fits, else the room.
        internal Size Concrete(Size room)
        {
            var (w, h, ratio) = Own;
            if (w.HasValue && h.HasValue) return new Size(w.Value, h.Value);
            if (w.HasValue) return new Size(w.Value, ratio.HasValue ? w.Value / ratio.Value : room.Height);
            if (h.HasValue) return new Size(ratio.HasValue ? h.Value * ratio.Value : room.Width, h.Value);
            if (ratio.HasValue) return ratio.Value >= room.Width / room.Height ? new Size(room.Width, room.Width / ratio.Value) : new Size(room.Height * ratio.Value, room.Height);
            return room;
        }

        // the root's width or height for the viewBox Chromium makes when there's none: a length, or a
        // percentage of the box (all of it when it's missing or isn't a length)
        internal static double Synthesized(string value, double whole, Style style)
        {
            if (!Length.TryParse(value, out Length l) || l.Value < 0) return whole;
            return l.Px(whole, style.FontSize, l.Unit == "ex" ? Renderer.XHeight(style) : 0);
        }

        // a viewBox attribute: four numbers; false when it's missing or broken (a negative size is broken)
        internal static bool ViewBox(Element e, out Rect box)
        {
            box = Rect.Empty;
            double[] v = Numbers.List(e["viewBox"]);
            if (v == null || v.Length != 4 || v[2] < 0 || v[3] < 0) return false;
            box = new Rect(v[0], v[1], v[2], v[3]);
            return true;
        }

        // The drawing with its root viewport `size` px across, clipped to it; SvgException when it has
        // too much to draw.
        public Drawing Render(Size size) => Render(size, new Budget());

        // the same, counted against the budget of the picture it's inside
        internal Drawing Render(Size size, Budget budget)
        {
            lock (made)
            {
                // one made at least as deep inside another picture goes no deeper here than it went there
                foreach (var m in made)
                    if (m.Size == size && budget.Deep <= m.Deep && budget.Masks <= m.Masks) return m.Drawing;
            }
            int deep = budget.Deep, masks = budget.Masks;
            Drawing drawing = new Renderer(this, budget).Root(size);
            drawing.Freeze();
            lock (made)
            {
                if (made.Count >= 4) made.RemoveAt(0);
                made.Add((size, deep, masks, drawing));
            }
            return drawing;
        }

        // The picture in a box, as the website's <image> puts it there (see the top).
        public void Draw(DrawingContext dc, Rect box) => Draw(dc, box, new Budget());

        // (the drawing is made before anything goes into `dc`, so one that stops leaves `dc` as it was)
        internal void Draw(DrawingContext dc, Rect box, Budget budget)
        {
            if (!(box.Width > 0 && box.Height > 0)) return;
            Drawing drawing = Render(box.Size, budget);
            dc.PushTransform(new TranslateTransform(box.X, box.Y));
            dc.DrawDrawing(drawing);
            dc.Pop();
        }
    }

    // How a viewBox fits a viewport: preserveAspectRatio's alignment, and meet or slice.
    struct Fit
    {
        public int X, Y;          // -1 min, 0 mid, 1 max; X = 2 for none
        public bool Slice;

        public static Fit Parse(string text)
        {
            var fit = new Fit();
            string[] words = (text ?? "").Trim().Split(new[] { ' ', '\t', '\n', '\r', '\f' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            if (i < words.Length && words[i] == "defer") i++;
            if (i >= words.Length) return fit;
            string align = words[i++];
            if (align == "none") fit.X = 2;
            else if (align.Length == 8 && align.StartsWith("x", StringComparison.Ordinal) && align[4] == 'Y')
            {
                int Part(string p) => p == "Min" ? -1 : p == "Mid" ? 0 : p == "Max" ? 1 : 9;
                int x = Part(align.Substring(1, 3)), y = Part(align.Substring(5, 3));
                if (x == 9 || y == 9) return new Fit();
                fit.X = x;
                fit.Y = y;
            }
            else return new Fit();
            if (i < words.Length)
            {
                if (words[i] == "slice") fit.Slice = true;
                else if (words[i] != "meet") return new Fit();
                i++;
            }
            return i == words.Length ? fit : new Fit();
        }

        // the transform that puts `box` (a viewBox) into `into`
        public Matrix Transform(Rect box, Rect into)
        {
            double sx = into.Width / box.Width, sy = into.Height / box.Height;
            if (X == 2) return new Matrix(sx, 0, 0, sy, into.X - box.X * sx, into.Y - box.Y * sy);
            double s = Slice ? Math.Max(sx, sy) : Math.Min(sx, sy);
            double tx = into.X - box.X * s + (X + 1) / 2.0 * (into.Width - box.Width * s);
            double ty = into.Y - box.Y * s + (Y + 1) / 2.0 * (into.Height - box.Height * s);
            return new Matrix(s, 0, 0, s, tx, ty);
        }
    }
}
