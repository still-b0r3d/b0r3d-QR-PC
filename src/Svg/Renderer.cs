using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR.Svg
{
    // How much one picture (and the SVG pictures inside it) may take to draw: a file can be small
    // and still ask for millions of copies of itself through <use>, or for things inside one another
    // deeper than the program's stack holds.
    sealed class Budget
    {
        public const int MostDrawn = 100000;   // elements drawn or measured, copies made by <use> included
        // What's read of the elements' attributes and of the text in them, copies included, each element
        // looked at (drawn or not) counting as 8 characters, as long as it takes: each copy is read again
        // (its path data, transform list, style, the white space in its text, the elements in it that are
        // never drawn), so one element of a few MB copied a thousand times would take minutes. 32 million
        // characters take about a second.
        public const long MostRead = 32L * 1000 * 1000;
        const int LookedAt = 8;
        // Points of the shapes' outlines, copies included, a shape of many points counting for more
        // (n + n² / 1,000): WPF takes a few µs a point to paint an outline, but more for one whose lines
        // cross each other many times, about as many times more as it has points (10,000 points that
        // all cross take it 0.6 s, 30,000 take 6). So these take it a few seconds at most, and there
        // can be as many as in 100,000 rectangles, or one shape of 20,000.
        public const long MostPoints = 420000;
        public const int MostInOneShape = 20000;   // n + n² / 1,000 = MostPoints
        // Gradient stops in the brushes made, copies included: WPF takes about 6 µs to paint each.
        public const int MostStops = 200000;
        // Characters of text laid out, copies included: WPF takes 0.15 ms (a Latin letter) to 0.6 ms (a
        // Chinese character on its own) to lay each out and turn it into shapes, so these take it a second
        // at most, and as long again to paint.
        public const int MostCharacters = 2000;
        // Elements inside elements (through <use>s, clip paths and the SVG pictures inside too), and the
        // layers each puts around what's inside it (a transform, a clip, opacity): drawing goes a step
        // deeper into the program's stack for each, and WPF goes as deep again for each layer when it
        // freezes, measures and paints the drawing. 512 of them take about a third of a 1 MB stack.
        public const int MostDeep = 512;
        // Clip paths of several shapes (or with clip paths of their own), which WPF paints as masks,
        // inside one another: the time WPF takes to paint them doubles with each one.
        public const int MostMasks = 16;

        public int Drawn;
        long read, points;
        int stops, characters, deep, masks;
        readonly Stack<bool> layers = new Stack<bool>();   // the layers pushed, true for a mask

        public int Deep => deep;
        public int Masks => masks;

        public void One() => Shapes(1);

        // shapes drawn that aren't elements: the layers of a colour font's glyphs (an emoji's)
        public void Shapes(int count)
        {
            if ((Drawn += count) > MostDrawn)
                throw new SvgException("it has more to draw than b0r3d QR draws in one picture (over " + MostDrawn.ToString("N0", CultureInfo.InvariantCulture) + " shapes, copies included)");
        }

        // an element's attributes read (Element.Size), or a text's characters (white space too)
        public void Read(int size)
        {
            if ((read += size) > MostRead)
                throw new SvgException("it has more to read than b0r3d QR reads for one picture (over " + MostRead.ToString("N0", CultureInfo.InvariantCulture) + " characters of attributes and text, copies included)");
        }

        // an element looked at, drawn or not
        public void Look() => Read(LookedAt);

        // a shape's outline of `count` points
        public void Points(int count)
        {
            if ((points += count + (long)count * count / 1000) > MostPoints)
                throw new SvgException("its shapes have more points than b0r3d QR draws in one picture (over " + MostPoints.ToString("N0", CultureInfo.InvariantCulture) + ", copies included; one shape can have up to " +
                                       MostInOneShape.ToString("N0", CultureInfo.InvariantCulture) + ")");
        }

        // a gradient brush of `count` stops
        public void Stops(int count)
        {
            if ((stops += count) > MostStops)
                throw new SvgException("its gradients have more stops than b0r3d QR draws in one picture (over " + MostStops.ToString("N0", CultureInfo.InvariantCulture) + ", copies included)");
        }

        // one more character of text to lay out
        public void Character()
        {
            if (++characters > MostCharacters)
                throw new SvgException("it has more text than b0r3d QR draws in one picture (over " + MostCharacters.ToString("N0", CultureInfo.InvariantCulture) + " characters, copies included)");
        }

        // a step deeper: an element inside another, or a layer
        public void In()
        {
            if (++deep > MostDeep)
                throw new SvgException("its groups, <use>s and clip paths are nested more than " + MostDeep + " deep (each transform, clip and opacity on them counts as one more)");
        }

        public void Out() => deep--;

        // a layer around what's drawn next; a mask is a clip path of several shapes
        public void Layer(bool mask = false)
        {
            In();
            layers.Push(mask);
            if (mask && ++masks > MostMasks) throw new SvgException("its clip paths of more than one shape are nested more than " + MostMasks + " deep");
        }

        // the last layer taken off
        public void Unlayer()
        {
            Out();
            if (layers.Pop()) masks--;
        }
    }

    // Paints an SvgImage into WPF drawing, as Chromium paints an SVG picture: shapes and paths,
    // groups, inner <svg>s, <use> and <symbol>, <switch>, gradients, clip paths, opacity, strokes
    // with dashes, transforms, pictures inside (data: URLs), and text in the fonts on this PC, its
    // emoji in their colours.
    // Masks, filters, patterns, markers and text on a path aren't drawn (what they're on is, without
    // them), and nothing from outside the file is either, as in a browser's SVG picture.
    sealed class Renderer
    {
        const int MostUses = 32;                     // <use> inside <use>, clip paths inside clip paths
        const long MostPixels = 64L * 1000 * 1000;   // pixels of the pictures inside one picture
        const int MostDashes = 100000;               // dashes along one stroke; more are drawn as a plain line
        // WPF draws no stroke at all with a miter limit from about 1e15, where Chromium draws one. Pen bevels every corner
        // sharper than a sine of 1e-9, so no corner WPF mitres needs a limit past 1e9: one past 1e10 draws the same.
        const double MostMiterLimit = 1e10;

        enum Axis { X, Y, Other }

        readonly SvgImage image;
        readonly Budget budget;
        readonly Stack<Size> viewports = new Stack<Size>();
        readonly List<Element> following = new List<Element>();   // the <use>s and clip paths being drawn, against loops
        readonly Dictionary<Element, Style> documentStyles = new Dictionary<Element, Style>();

        public Renderer(SvgImage image, Budget budget)
        {
            this.image = image;
            this.budget = budget;
        }

        static readonly HashSet<string> Drawn = new HashSet<string>
        {
            "g", "a", "svg", "use", "switch", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "image",
        };

        // ---------- the picture ----------

        // The drawing in a viewport `size` px across, clipped to it. Its bounds are the whole viewport.
        public Drawing Root(Size size)
        {
            if (budget.Drawn == 0) StyleSheet.Fresh();   // a picture on its own (the steps of pictures inside it count with its own)
            var group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                var whole = new Rect(0, 0, Math.Max(0, size.Width), Math.Max(0, size.Height));
                dc.DrawRectangle(Brushes.Transparent, null, whole);
                if (whole.Width > 0 && whole.Height > 0)
                {
                    int pushes = 0;
                    dc.PushClip(new RectangleGeometry(whole));
                    Pushed(ref pushes);
                    Contents(dc, whole);
                    Pop(dc, pushes);
                }
            }
            return group;
        }

        void Contents(DrawingContext dc, Rect whole)
        {
            Element root = image.Root;
            Style style = Styled(root, Style.Initial);
            if (!style.Display || !Passes(root)) return;
            budget.One();
            Matrix map = Matrix.Identity;
            Size inner;
            if (SvgImage.ViewBox(root, out Rect box))
            {
                if (box.Width == 0 || box.Height == 0) return;   // an empty viewBox draws nothing
                map = Fit.Parse(root["preserveAspectRatio"]).Transform(box, whole);
                inner = box.Size;
            }
            else
            {
                // Chromium's way with an SVG picture that has no viewBox: one is made from its width and
                // height (a percentage of the box, or all of it when they're missing), and stretched
                // to fill the box, as preserveAspectRatio="none"
                double w = SvgImage.Synthesized(root["width"], whole.Width, style), h = SvgImage.Synthesized(root["height"], whole.Height, style);
                inner = new Size(w, h);
                if (w > 0 && h > 0) map = new Matrix(whole.Width / w, 0, 0, whole.Height / h, 0, 0);
            }
            if (inner.Width <= 0 || inner.Height <= 0) inner = whole.Size;
            int pushes = 0;
            dc.PushTransform(new MatrixTransform(map));
            Pushed(ref pushes);
            viewports.Push(inner);
            if (Effects(dc, root, style, ref pushes)) Children(dc, root, style);
            viewports.Pop();
            Pop(dc, pushes);
        }

        // a layer pushed (a transform, a clip, opacity; a mask: a clip path of several shapes), counted
        // in the budget for how deep the drawing goes
        void Pushed(ref int pushes, bool mask = false)
        {
            pushes++;
            budget.Layer(mask);
        }

        void Pop(DrawingContext dc, int pushes)
        {
            for (; pushes > 0; pushes--)
            {
                dc.Pop();
                budget.Unlayer();
            }
        }

        void Children(DrawingContext dc, Element e, Style s)
        {
            foreach (Element child in e.Children) Draw(dc, child, s);
        }

        // one element and what's in it; `useSize` is a <use>'s width and height for the <svg> it shows
        void Draw(DrawingContext dc, Element e, Style parent, Size? useSize = null)
        {
            budget.Look();
            if (e.Namespace != Xml.SvgSpace || !Drawn.Contains(e.Name) || !Passes(e)) return;
            budget.One();
            Style s = Styled(e, parent);
            if (!s.Display) return;
            Matrix? transform = s.Transform ?? Transforms.Parse(e["transform"]);
            bool moved = transform.HasValue && !transform.Value.IsIdentity;
            if (moved && !transform.Value.HasInverse) return;   // squashed flat: nothing to see
            budget.In();
            int pushes = 0;
            if (moved)
            {
                dc.PushTransform(new MatrixTransform(transform.Value));
                Pushed(ref pushes);
            }
            if (Effects(dc, e, s, ref pushes))
            {
                switch (e.Name)
                {
                    case "g":
                    case "a":
                        Children(dc, e, s);
                        break;
                    case "switch":
                        Switch(dc, e, s);
                        break;
                    case "svg":
                        Inner(dc, e, s, useSize);
                        break;
                    case "use":
                        Use(dc, e, s);
                        break;
                    case "image":
                        Picture(dc, e, s);
                        break;
                    case "text":
                        Text(dc, e, s);
                        break;
                    default:
                        Shape(dc, e, s);
                        break;
                }
            }
            Pop(dc, pushes);
            budget.Out();
        }

        // SVG's conditions: systemLanguage (this is an English program), and requiredExtensions,
        // which Chromium only knows for <foreignObject>, never drawn here (Illustrator's files lean on
        // that: their <switch> skips its own data for the drawing after it). Worked out once for each
        // element: each copy asks again.
        bool Passes(Element e)
        {
            if (!passes.TryGetValue(e, out bool ok)) passes[e] = ok = Conditions(e);
            return ok;
        }

        readonly Dictionary<Element, bool> passes = new Dictionary<Element, bool>();

        static bool Conditions(Element e)
        {
            if (e["requiredExtensions"] != null) return false;   // even an empty one, as in Chromium
            string languages = e["systemLanguage"];
            if (languages == null) return true;
            foreach (string language in languages.Split(','))
            {
                string l = language.Trim();
                if (l.Equals("en", StringComparison.OrdinalIgnoreCase) || l.StartsWith("en-", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // the first child that passes its conditions
        void Switch(DrawingContext dc, Element e, Style s)
        {
            foreach (Element child in e.Children)
            {
                budget.Look();
                if (child.Namespace != Xml.SvgSpace || !Passes(child)) continue;
                if (!Drawn.Contains(child.Name) && child.Name != "foreignObject") continue;
                Draw(dc, child, s);
                return;
            }
        }

        // opacity and the clip path; false when there's nothing left to see
        bool Effects(DrawingContext dc, Element e, Style s, ref int pushes)
        {
            if (s.Opacity <= 0) return false;
            // a clip path that isn't there clips nothing, as in Chromium
            if (s.ClipPath != null && Find(s.ClipPath, "clipPath") is Element clip && !PushClip(dc, clip, e, s, ref pushes)) return false;
            if (s.Opacity < 1)
            {
                dc.PushOpacity(s.Opacity);   // the whole group at once, as SVG's opacity is
                Pushed(ref pushes);
            }
            return true;
        }

        // an element in the file by its id, when it's one of the kinds asked for
        Element Find(string id, params string[] kinds)
        {
            if (string.IsNullOrEmpty(id) || !image.Ids.TryGetValue(id, out Element found) || found.Namespace != Xml.SvgSpace) return null;
            return Array.IndexOf(kinds, found.Name) >= 0 ? found : null;
        }

        // what an href names inside the file; null for anything else (another file, the web)
        Element Referenced(Element e)
        {
            string href = Href(e);
            if (href == null) return null;
            href = href.Trim();
            if (!href.StartsWith("#", StringComparison.Ordinal) || href.Length < 2) return null;
            return image.Ids.TryGetValue(href.Substring(1), out Element found) ? found : null;
        }

        // href, else the older xlink:href
        static string Href(Element e) => e["href"] ?? e["href", Xml.XLinkSpace];

        // an element's style inside `parent`, its attributes read (counted: each copy reads them again)
        Style Styled(Element e, Style parent)
        {
            budget.Read(e.Size);
            return Style.Of(e, parent, image.Sheet);
        }

        // an element's style in its own place in the file (for gradient stops and clip paths, which
        // take theirs from where they are, not from what uses them)
        Style DocumentStyle(Element e)
        {
            if (e == null) return Style.Initial;
            if (documentStyles.TryGetValue(e, out Style s)) return s;
            // from the outermost element not worked out yet inwards, in a loop rather than a call inside
            // a call for each element it's inside
            var path = new Stack<Element>();
            for (Element up = e; up != null && !documentStyles.ContainsKey(up); up = up.Parent) path.Push(up);
            while (path.Count > 0)
            {
                Element next = path.Pop();
                s = Styled(next, next.Parent == null ? Style.Initial : documentStyles[next.Parent]);
                documentStyles[next] = s;
            }
            return s;
        }

        // ---------- lengths ----------

        double Px(Length l, Axis axis, Style s)
        {
            Size v = viewports.Count > 0 ? viewports.Peek() : new Size(300, 150);
            double whole = axis == Axis.X ? v.Width : axis == Axis.Y ? v.Height : Math.Sqrt((v.Width * v.Width + v.Height * v.Height) / 2);
            return l.Px(whole, s.FontSize, l.Unit == "ex" ? XHeight(s) : 0);
        }

        // a geometry attribute (or the CSS property of the same name), in px; `fallback` when it's
        // missing or isn't a length
        double Len(Element e, Style s, string name, Axis axis, double fallback)
        {
            double? v = Optional(e, s, name, axis, true);
            return v ?? fallback;
        }

        // the same, or null when it's missing, isn't a length or (unless `negatives`) is below 0: auto
        double? Optional(Element e, Style s, string name, Axis axis, bool negatives = false)
        {
            string text = s.Geometry != null && s.Geometry.TryGetValue(name, out string css) ? css : e[name];
            if (text == null || !Length.TryParse(text, out Length l)) return null;
            double v = Px(l, axis, s);
            if (double.IsNaN(v) || double.IsInfinity(v) || !negatives && v < 0) return null;
            return v;
        }

        // ---------- inner viewports ----------

        void Inner(DrawingContext dc, Element e, Style s, Size? useSize)
        {
            double x = Len(e, s, "x", Axis.X, 0), y = Len(e, s, "y", Axis.Y, 0);
            double w = useSize?.Width ?? Optional(e, s, "width", Axis.X) ?? Px(new Length(100, "%"), Axis.X, s);
            double h = useSize?.Height ?? Optional(e, s, "height", Axis.Y) ?? Px(new Length(100, "%"), Axis.Y, s);
            if (!(w > 0 && h > 0)) return;
            Viewport(dc, e, s, new Rect(x, y, w, h));
        }

        // What's inside an inner <svg> or a <symbol>, drawn into its viewport through its viewBox and
        // preserveAspectRatio, and clipped to the viewport unless its overflow is visible.
        void Viewport(DrawingContext dc, Element e, Style s, Rect port)
        {
            Matrix map;
            Size inner;
            if (SvgImage.ViewBox(e, out Rect box))
            {
                if (box.Width == 0 || box.Height == 0) return;
                map = Fit.Parse(e["preserveAspectRatio"]).Transform(box, port);
                inner = box.Size;
            }
            else
            {
                map = new Matrix(1, 0, 0, 1, port.X, port.Y);
                inner = port.Size;
            }
            int pushes = 0;
            if (!(s.Overflow == "visible" || s.Overflow == "auto"))
            {
                dc.PushClip(new RectangleGeometry(port));
                Pushed(ref pushes);
            }
            dc.PushTransform(new MatrixTransform(map));
            Pushed(ref pushes);
            viewports.Push(inner);
            Children(dc, e, s);
            viewports.Pop();
            Pop(dc, pushes);
        }

        // <use>: the element it names, drawn here as if it were inside it
        void Use(DrawingContext dc, Element e, Style s)
        {
            Element target = Referenced(e);
            if (target == null || target.Namespace != Xml.SvgSpace) return;
            // a <use> of itself or of something it's inside draws nothing, as in browsers
            for (Element up = e; up != null; up = up.Parent)
                if (up == target) return;
            if (following.Contains(target) || following.Count >= MostUses) return;
            double x = Len(e, s, "x", Axis.X, 0), y = Len(e, s, "y", Axis.Y, 0);
            following.Add(target);
            int moved = 0;
            dc.PushTransform(new TranslateTransform(x, y));
            Pushed(ref moved);
            try
            {
                double? w = Optional(e, s, "width", Axis.X), h = Optional(e, s, "height", Axis.Y);
                if (target.Name == "symbol")
                {
                    if (Passes(target))
                    {
                        budget.One();
                        Style symbol = Styled(target, s);
                        int pushes = 0;
                        double sw = w ?? Px(new Length(100, "%"), Axis.X, s), sh = h ?? Px(new Length(100, "%"), Axis.Y, s);
                        if (symbol.Display && sw > 0 && sh > 0 && Effects(dc, target, symbol, ref pushes)) Viewport(dc, target, symbol, new Rect(0, 0, sw, sh));
                        Pop(dc, pushes);
                    }
                }
                else if (target.Name == "svg" && (w.HasValue || h.HasValue))
                {
                    Style inner = Styled(target, s);
                    Size size = new Size(w ?? Optional(target, inner, "width", Axis.X) ?? Px(new Length(100, "%"), Axis.X, s),
                                         h ?? Optional(target, inner, "height", Axis.Y) ?? Px(new Length(100, "%"), Axis.Y, s));
                    Draw(dc, target, s, size);
                }
                else Draw(dc, target, s);
            }
            finally
            {
                Pop(dc, moved);
                following.Remove(target);
            }
        }

        // ---------- shapes ----------

        // a basic shape's or a path's outline, or null when it has none (a rectangle of no width...);
        // its points counted (each copy is drawn again)
        Figures Outline(Element e, Style s)
        {
            Figures f = OutlineOf(e, s);
            if (f != null) budget.Points(f.Count);
            return f;
        }

        Figures OutlineOf(Element e, Style s)
        {
            switch (e.Name)
            {
                case "path":
                    return Figures.FromPathData(e["d"], Budget.MostInOneShape);
                case "rect":
                {
                    double w = Optional(e, s, "width", Axis.X) ?? 0, h = Optional(e, s, "height", Axis.Y) ?? 0;
                    if (!(w > 0 && h > 0)) return null;
                    double? rx = Optional(e, s, "rx", Axis.X), ry = Optional(e, s, "ry", Axis.Y);
                    double ax = Math.Min(rx ?? ry ?? 0, w / 2), ay = Math.Min(ry ?? rx ?? 0, h / 2);
                    return Figures.Rectangle(Len(e, s, "x", Axis.X, 0), Len(e, s, "y", Axis.Y, 0), w, h, ax, ay);
                }
                case "circle":
                {
                    double r = Optional(e, s, "r", Axis.Other) ?? 0;
                    return r > 0 ? Figures.Ellipse(Len(e, s, "cx", Axis.X, 0), Len(e, s, "cy", Axis.Y, 0), r, r) : null;
                }
                case "ellipse":
                {
                    double? rx = Optional(e, s, "rx", Axis.X), ry = Optional(e, s, "ry", Axis.Y);
                    double ax = rx ?? ry ?? 0, ay = ry ?? rx ?? 0;   // one alone is both, as in Chromium
                    return ax > 0 && ay > 0 ? Figures.Ellipse(Len(e, s, "cx", Axis.X, 0), Len(e, s, "cy", Axis.Y, 0), ax, ay) : null;
                }
                case "line":
                {
                    var f = new Figures();
                    f.Move(new Point(Len(e, s, "x1", Axis.X, 0), Len(e, s, "y1", Axis.Y, 0)));
                    f.Line(new Point(Len(e, s, "x2", Axis.X, 0), Len(e, s, "y2", Axis.Y, 0)));
                    return f;
                }
                case "polyline":
                    return Figures.Points(e["points"], false, Budget.MostInOneShape);
                case "polygon":
                    return Figures.Points(e["points"], true, Budget.MostInOneShape);
            }
            return null;
        }

        void Shape(DrawingContext dc, Element e, Style s)
        {
            if (!s.Visible) return;
            Figures f = Outline(e, s);
            if (f == null || f.IsEmpty) return;
            Geometry g = f.ToGeometry(s.FillRule);
            Rect box = g.Bounds;
            Pen pen = Pen(s, box, f, out Geometry tips);
            PaintShape(dc, g, Brush(s.Fill, s.FillOpacity, box, s), pen, s, f, tips);
        }

        // fill and stroke in the order paint-order asks for; the tips of mitred corners drawn bevelled
        // (see Pen); round and square caps on lines of no length
        static void PaintShape(DrawingContext dc, Geometry g, Brush fill, Pen pen, Style s, Figures f, Geometry tips = null)
        {
            if (s.StrokeFirst && fill != null && pen != null)
            {
                dc.DrawGeometry(null, pen, g);
                if (tips != null) dc.DrawGeometry(pen.Brush, null, tips);
                dc.DrawGeometry(fill, null, g);
            }
            else
            {
                if (fill != null || pen != null) dc.DrawGeometry(fill, pen, g);
                if (tips != null) dc.DrawGeometry(pen.Brush, null, tips);
            }
            if (pen == null || f == null || pen.StartLineCap == PenLineCap.Flat) return;
            double half = pen.Thickness / 2;
            foreach (Point p in f.ZeroLength())
            {
                if (pen.StartLineCap == PenLineCap.Round) dc.DrawEllipse(pen.Brush, null, p, half, half);
                else dc.DrawRectangle(pen.Brush, null, new Rect(p.X - half, p.Y - half, 2 * half, 2 * half));
            }
        }

        // An element's object bounding box in its own user space: what its fill covers, as SVG
        // measures it for gradients and clip paths in objectBoundingBox units.
        Rect Bounds(Element e, Style s)
        {
            switch (e.Name)
            {
                case "text":
                    return Lay(e, s).Box;
                case "image":
                    return PictureBox(e, s, Loaded(e), out _);
                case "use":
                {
                    Element target = Referenced(e);
                    if (target == null || following.Contains(target) || following.Count >= MostUses) return Rect.Empty;
                    following.Add(target);
                    budget.In();   // a step deeper for each element inside another, as in drawing them
                    try
                    {
                        budget.One();   // measured, as drawn: once for each copy
                        Style ts = Styled(target, s);
                        Rect inner = Placed(target, ts, Bounds(target, ts));
                        return inner.IsEmpty ? inner : Rect.Transform(inner, new Matrix(1, 0, 0, 1, Len(e, s, "x", Axis.X, 0), Len(e, s, "y", Axis.Y, 0)));
                    }
                    finally
                    {
                        budget.Out();
                        following.Remove(target);
                    }
                }
                case "g":
                case "a":
                case "switch":
                case "svg":
                case "symbol":
                {
                    Rect all = Rect.Empty;
                    budget.In();
                    foreach (Element child in e.Children)
                    {
                        budget.Look();
                        if (child.Namespace != Xml.SvgSpace || !Drawn.Contains(child.Name) || !Passes(child)) continue;
                        budget.One();
                        Style cs = Styled(child, s);
                        if (!cs.Display) continue;
                        all.Union(Placed(child, cs, Bounds(child, cs)));
                    }
                    budget.Out();
                    return all;
                }
                default:
                    Figures f = Outline(e, s);
                    return f == null || f.IsEmpty ? Rect.Empty : f.ToGeometry(s.FillRule).Bounds;
            }
        }

        // a child's box in its parent's space: through its transform
        static Rect Placed(Element e, Style s, Rect box)
        {
            if (box.IsEmpty) return box;
            Matrix? m = s.Transform ?? Transforms.Parse(e["transform"]);
            return m.HasValue ? Rect.Transform(box, m.Value) : box;
        }

        // ---------- paint ----------

        static Brush Solid(Color c, double opacity)
        {
            if (c.A == 0 || opacity <= 0) return null;
            var b = new SolidColorBrush(c) { Opacity = Math.Min(1, opacity) };
            b.Freeze();
            return b;
        }

        // a fill or stroke's brush, or null for none
        Brush Brush(Paint p, double opacity, Rect box, Style s)
        {
            switch (p.Kind)
            {
                case Paint.Kinds.Colour:
                    return Solid(p.Colour, opacity);
                case Paint.Kinds.Current:
                    return Solid(s.Color, opacity);
                case Paint.Kinds.Url:
                    Element server = Find(p.Id, "linearGradient", "radialGradient");
                    if (server != null)
                    {
                        Brush gradient = Gradient(server, opacity, box, out bool fits);
                        if (fits) return gradient;
                    }
                    // no gradient of that name (or a pattern, which isn't drawn), or one in the units of a
                    // box with no width or height: the colour after it, if there is one, as in Chromium
                    if (p.Fallback == Paint.Kinds.Colour) return Solid(p.FallbackColour, opacity);
                    if (p.Fallback == Paint.Kinds.Current) return Solid(s.Color, opacity);
                    return null;
                default:
                    return null;
            }
        }

        // a gradient's brush (null when it has no stops); `fits` false when it can't go on this box
        Brush Gradient(Element server, double opacity, Rect box, out bool fits)
        {
            fits = true;
            // what it says, and what it takes from the gradients it names, as far as that goes: the
            // units, transform, spread and stops from either kind, the points only from its own kind
            bool radial = server.Name == "radialGradient";
            string units = null, transform = null, spread = null;
            var points = new Dictionary<string, string>();
            Element stopsFrom = null;
            var seen = new HashSet<Element>();
            for (Element g = server; g != null && g.Namespace == Xml.SvgSpace && (g.Name == "linearGradient" || g.Name == "radialGradient") && seen.Count < 32 && seen.Add(g); g = Referenced(g))
            {
                budget.Read(g.Size);   // read again for each brush
                units = units ?? g["gradientUnits"];
                transform = transform ?? g["gradientTransform"];
                spread = spread ?? g["spreadMethod"];
                if (g.Name == server.Name)
                    foreach (string name in radial ? new[] { "cx", "cy", "r", "fx", "fy", "fr" } : new[] { "x1", "y1", "x2", "y2" })
                        if (!points.ContainsKey(name) && g[name] != null) points[name] = g[name];
                if (stopsFrom == null && Stops(g).Count > 0) stopsFrom = g;
            }
            var stops = stopsFrom == null ? new List<GradientStop>() : new List<GradientStop>(Stops(stopsFrom));
            budget.Stops(stops.Count);
            if (stops.Count == 0) return null;
            if (stops.Count == 1) return Solid(stops[0].Color, opacity);
            Color end = stops[stops.Count - 1].Color;

            bool userSpace = (units ?? "").Trim() == "userSpaceOnUse";
            if (!userSpace && !(box.Width > 0 && box.Height > 0))
            {
                fits = false;   // a box with no width or height takes no gradient
                return null;
            }
            Style own = DocumentStyle(server);
            double V(string name, Axis axis, string fallback)
            {
                if (!points.TryGetValue(name, out string text) || !Length.TryParse(text, out Length l)) Length.TryParse(fallback, out l);
                if (userSpace) return Px(l, axis, own);
                return l.IsPercent ? l.Value / 100 : l.Value;
            }

            GradientBrush brush;
            if (radial)
            {
                double cx = V("cx", Axis.X, "50%"), cy = V("cy", Axis.Y, "50%"), r = V("r", Axis.Other, "50%");
                double fx = points.ContainsKey("fx") ? V("fx", Axis.X, "50%") : cx, fy = points.ContainsKey("fy") ? V("fy", Axis.Y, "50%") : cy;
                double fr = V("fr", Axis.Other, "0%");
                if (r < 0 || fr < 0) return null;
                if (r == 0) return Solid(end, opacity);
                // a focal point outside the circle goes onto its edge
                double fd = Math.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                if (fd > r * 0.999)
                {
                    fx = cx + (fx - cx) * r * 0.999 / fd;
                    fy = cy + (fy - cy) * r * 0.999 / fd;
                }
                // a focal radius around the centre: the stops start that far out
                if (fr > 0 && fd == 0 && fr < r)
                {
                    double start = fr / r;
                    for (int i = 0; i < stops.Count; i++) stops[i] = new GradientStop(stops[i].Color, start + stops[i].Offset * (1 - start));
                    stops.Insert(0, new GradientStop(stops[0].Color, 0));
                }
                brush = new RadialGradientBrush(new GradientStopCollection(stops)) { Center = new Point(cx, cy), GradientOrigin = new Point(fx, fy), RadiusX = r, RadiusY = r };
            }
            else
            {
                var p1 = new Point(V("x1", Axis.X, "0%"), V("y1", Axis.Y, "0%"));
                var p2 = new Point(V("x2", Axis.X, "100%"), V("y2", Axis.Y, "0%"));
                if (p1 == p2) return Solid(end, opacity);   // no length: the last stop's colour
                brush = new LinearGradientBrush(new GradientStopCollection(stops), p1, p2);
            }
            brush.MappingMode = BrushMappingMode.Absolute;
            brush.ColorInterpolationMode = ColorInterpolationMode.SRgbLinearInterpolation;
            string sm = (spread ?? "").Trim();
            brush.SpreadMethod = sm == "reflect" ? GradientSpreadMethod.Reflect : sm == "repeat" ? GradientSpreadMethod.Repeat : GradientSpreadMethod.Pad;
            Matrix full = (Transforms.Parse(transform) ?? Matrix.Identity) * (userSpace ? Matrix.Identity : new Matrix(box.Width, 0, 0, box.Height, box.X, box.Y));
            if (!full.HasInverse) return null;
            if (!full.IsIdentity) brush.Transform = new MatrixTransform(full);
            brush.Opacity = Math.Max(0, Math.Min(1, opacity));
            brush.Freeze();
            return brush;
        }

        // a gradient's own stops (none when it has no <stop>s), worked out once: each brush made from it
        // asks again
        List<GradientStop> Stops(Element gradient)
        {
            if (gradientStops.TryGetValue(gradient, out List<GradientStop> stops)) return stops;
            stops = new List<GradientStop>();
            Style around = null;
            double last = 0;
            foreach (Element stop in gradient.Children)
            {
                if (!stop.Is("stop")) continue;
                around = around ?? DocumentStyle(gradient);
                Style ss = Styled(stop, around);
                last = Math.Max(last, Offset(stop["offset"]));   // never before the stop before it
                Color c = ss.StopColor;
                var made = new GradientStop(Color.FromArgb((byte)Math.Round(c.A * ss.StopOpacity), c.R, c.G, c.B), last);
                made.Freeze();   // shared by the brushes
                stops.Add(made);
            }
            gradientStops[gradient] = stops;
            return stops;
        }

        readonly Dictionary<Element, List<GradientStop>> gradientStops = new Dictionary<Element, List<GradientStop>>();

        // a stop's offset: a number or a percentage, 0 to 1
        static double Offset(string text)
        {
            if (text == null || !Length.TryParse(text, out Length l) || !(l.Unit == "" || l.IsPercent)) return 0;
            double v = l.IsPercent ? l.Value / 100 : l.Value;
            return Math.Max(0, Math.Min(1, v));
        }

        // The stroke's pen, or null for none. A corner sharper than the miter limit is bevelled in SVG,
        // where WPF cuts the point off at the limit instead: then the whole outline is bevelled, and
        // `tips` holds the points of the corners that are within the limit, to be filled on top.
        Pen Pen(Style s, Rect box, Figures f, out Geometry tips)
        {
            tips = null;
            if (s.Stroke.Kind == Paint.Kinds.None) return null;
            double width = Px(s.StrokeWidth, Axis.Other, s);
            if (!(width > 0) || double.IsInfinity(width)) return null;
            Brush brush = Brush(s.Stroke, s.StrokeOpacity, box, s);
            if (brush == null) return null;
            var pen = new Pen(brush, width) { StartLineCap = s.LineCap, EndLineCap = s.LineCap, DashCap = s.LineCap, LineJoin = s.LineJoin, MiterLimit = Math.Min(s.MiterLimit, MostMiterLimit) };
            if (s.Dashes != null)
            {
                var dashes = new List<double>();
                foreach (Length d in s.Dashes) dashes.Add(Px(d, Axis.Other, s));
                if (dashes.Count % 2 == 1) dashes.AddRange(dashes.ToArray());   // an odd list is said twice
                double period = dashes.Sum();
                // all zeros is a plain line; so is a pattern too fine to draw along a long line
                if (period > 0 && !double.IsInfinity(period) && (f == null || f.RoughLength() / period * dashes.Count / 2 <= MostDashes))
                    pen.DashStyle = new DashStyle(dashes.Select(d => d / width), Px(s.DashOffset, Axis.Other, s) / width);
            }
            if (pen.LineJoin == PenLineJoin.Miter && f != null)
            {
                var within = new List<(Point, Vector, Vector)>();
                bool beyond = false;
                foreach (var join in f.Joins())
                {
                    Vector a = join.In, b = join.Out;
                    a.Normalize();
                    b.Normalize();
                    double sine = Math.Sqrt(Math.Max(0, (1 + Vector.Multiply(a, b)) / 2));   // sin of half the angle between the two lines
                    if (Math.Abs(Vector.CrossProduct(a, b)) < 1e-9 && Vector.Multiply(a, b) > 0) continue;   // straight on: no corner
                    if (sine < 1e-9 || 1 / sine > s.MiterLimit) beyond = true;
                    else within.Add((join.At, a, b));
                }
                if (beyond)
                {
                    pen.LineJoin = PenLineJoin.Bevel;
                    if (within.Count > 0 && pen.DashStyle == null) tips = Tips(within, width / 2);
                }
            }
            pen.Freeze();
            return pen;
        }

        // the triangles a mitre adds beyond a bevel, at each corner (`half` is half the line's width)
        static Geometry Tips(List<(Point At, Vector In, Vector Out)> corners, double half)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                foreach (var (p, a, b) in corners)
                {
                    // the normals on the outside of the corner, which points the way the lines come from
                    Vector na = new Vector(-a.Y, a.X), nb = new Vector(-b.Y, b.X);
                    if (Vector.Multiply(na, b) > 0) na = -na;
                    if (Vector.Multiply(nb, a) < 0) nb = -nb;
                    Vector u = na + nb;
                    if (u.LengthSquared < 1e-12) continue;
                    c.BeginFigure(p + na * half, true, true);
                    c.LineTo(p + u * (2 * half / u.LengthSquared), true, false);
                    c.LineTo(p + nb * half, true, false);
                    c.LineTo(p, true, false);
                }
            }
            g.Freeze();
            return g;
        }

        // ---------- clip paths ----------

        // A clip path pushed for an element; false when it leaves nothing to see (an empty one).
        bool PushClip(DrawingContext dc, Element clip, Element e, Style s, ref int pushes)
        {
            if (following.Contains(clip) || following.Count >= MostUses) return false;   // a clip path inside itself
            following.Add(clip);
            budget.In();
            try
            {
                budget.Read(clip.Size);   // read again for each element it clips
                Style clipStyle = DocumentStyle(clip);
                Matrix units = Matrix.Identity;
                if ((clip["clipPathUnits"] ?? "").Trim() == "objectBoundingBox")
                {
                    Rect box = Bounds(e, s);
                    if (!(box.Width > 0 && box.Height > 0)) return false;
                    units = new Matrix(box.Width, 0, 0, box.Height, box.X, box.Y);
                }
                Matrix whole = (clipStyle.Transform ?? Transforms.Parse(clip["transform"]) ?? Matrix.Identity) * units;
                var parts = new List<ClipPart>();
                foreach (Element child in clip.Children) Part(child, clipStyle, whole, parts);
                if (parts.Count == 0) return false;
                if (parts.Count == 1 && parts[0].ClipPath == null && parts[0].Covered == null)
                {
                    Geometry g = parts[0].Geometry.Clone();
                    g.Transform = new MatrixTransform(parts[0].Matrix);
                    g.Freeze();
                    dc.PushClip(g);
                    Pushed(ref pushes);
                }
                else
                {
                    // several shapes (or one with a clip path of its own, or text with emoji): their union, as a mask
                    var mask = new DrawingGroup();
                    using (DrawingContext m = mask.Open())
                    {
                        foreach (ClipPart part in parts)
                        {
                            int inner = 0;
                            m.PushTransform(new MatrixTransform(part.Matrix));
                            Pushed(ref inner);
                            if (part.ClipPath == null || PushClip(m, part.ClipPath, part.Element, part.Style, ref inner))
                            {
                                if (part.Geometry != null) m.DrawGeometry(Brushes.Black, null, part.Geometry);
                                if (part.Covered != null) m.DrawDrawing(part.Covered);
                            }
                            Pop(m, inner);
                        }
                    }
                    Rect bounds = mask.Bounds;
                    if (bounds.IsEmpty || !(bounds.Width > 0 && bounds.Height > 0)) return false;
                    var brush = new DrawingBrush(mask)
                    {
                        Stretch = Stretch.None, TileMode = TileMode.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                        ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds, ViewportUnits = BrushMappingMode.Absolute, Viewport = bounds,
                    };
                    brush.Freeze();
                    dc.PushClip(new RectangleGeometry(bounds));
                    Pushed(ref pushes);
                    dc.PushOpacityMask(brush);
                    Pushed(ref pushes, true);
                }
                // a clip path can have a clip path of its own: both apply
                if (clipStyle.ClipPath != null && Find(clipStyle.ClipPath, "clipPath") is Element outer && !PushClip(dc, outer, e, s, ref pushes)) return false;
                return true;
            }
            finally
            {
                budget.Out();
                following.Remove(clip);
            }
        }

        sealed class ClipPart
        {
            public Geometry Geometry;
            public Drawing Covered;   // what a text's emoji cover (Laid.Covered), or null
            public Matrix Matrix;
            public Element Element, ClipPath;
            public Style Style;
        }

        // one shape or text of a clip path, through its transform (a <use> of one too)
        void Part(Element child, Style clipStyle, Matrix whole, List<ClipPart> parts)
        {
            budget.Look();
            if (child.Namespace != Xml.SvgSpace || !Passes(child)) return;
            Style cs = Styled(child, clipStyle);
            if (!cs.Display) return;
            Matrix m = (cs.Transform ?? Transforms.Parse(child["transform"]) ?? Matrix.Identity) * whole;
            Element shape = child;
            Style ss = cs;
            if (child.Name == "use")
            {
                Element target = Referenced(child);
                if (target == null || target.Namespace != Xml.SvgSpace || !Passes(target)) return;
                ss = Styled(target, cs);
                if (!ss.Display) return;
                m = (ss.Transform ?? Transforms.Parse(target["transform"]) ?? Matrix.Identity) * new Matrix(1, 0, 0, 1, Len(child, cs, "x", Axis.X, 0), Len(child, cs, "y", Axis.Y, 0)) * m;
                shape = target;
            }
            if (!ss.Visible) return;
            budget.One();
            Geometry g;
            Drawing covered = null;
            if (shape.Name == "text")
            {
                Laid laid = Lay(shape, ss);
                g = laid.Outline();
                covered = laid.Covered();
            }
            else
            {
                Figures f = Outline(shape, ss);
                g = f == null || f.IsEmpty ? null : f.ToGeometry(ss.ClipRule);
            }
            if ((g == null || g.IsEmpty()) && covered == null) return;
            Element own = cs.ClipPath != null ? Find(cs.ClipPath, "clipPath") : null;
            parts.Add(new ClipPart { Geometry = g, Covered = covered, Matrix = m, Element = shape, Style = ss, ClipPath = own });
        }

        // ---------- pictures ----------

        // <image>: a PNG, JPEG, GIF, BMP or WebP picture, or an SVG one, from a data: URL
        void Picture(DrawingContext dc, Element e, Style s)
        {
            if (!s.Visible) return;
            object picture = Loaded(e);
            if (picture == null) return;
            Rect box = PictureBox(e, s, picture, out Size natural);
            if (!(box.Width > 0 && box.Height > 0)) return;
            Fit fit = Fit.Parse(e["preserveAspectRatio"]);
            if (picture is SvgImage svg)
            {
                // its place is a layer, and its own drawing goes on deeper from there (see Budget)
                if (fit.X == 2)
                {
                    // none: the picture at the size it would take in the box, stretched to the box
                    Size concrete = svg.Concrete(box.Size);
                    if (!(concrete.Width > 0 && concrete.Height > 0)) return;
                    int pushes = 0;
                    dc.PushTransform(new MatrixTransform(box.Width / concrete.Width, 0, 0, box.Height / concrete.Height, box.X, box.Y));
                    Pushed(ref pushes);
                    dc.DrawDrawing(svg.Render(concrete, budget));
                    Pop(dc, pushes);
                }
                else
                {
                    budget.In();
                    svg.Draw(dc, box, budget);   // the box is its viewport (see SvgImage)
                    budget.Out();
                }
                return;
            }
            var bitmap = (BitmapSource)picture;
            Rect at = Rect.Transform(new Rect(natural), fit.Transform(new Rect(natural), box));
            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, s.Pixelated ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
            using (DrawingContext g = group.Open())
            {
                if (fit.Slice) g.PushClip(new RectangleGeometry(box));
                g.DrawImage(bitmap, at);
                if (fit.Slice) g.Pop();
            }
            dc.DrawDrawing(group);
        }

        // where a picture goes: x, y, and width and height, which are its own size when they're auto
        Rect PictureBox(Element e, Style s, object picture, out Size natural)
        {
            natural = picture is BitmapSource b ? new Size(b.PixelWidth, b.PixelHeight) : picture is SvgImage svg ? svg.NaturalSize : Size.Empty;
            if (picture == null || natural.IsEmpty) return Rect.Empty;
            double? w = Optional(e, s, "width", Axis.X), h = Optional(e, s, "height", Axis.Y);
            double ratio = natural.Height > 0 ? natural.Width / natural.Height : 0;
            if (w == null && h == null)
            {
                w = natural.Width;
                h = natural.Height;
            }
            else if (w == null) w = ratio > 0 ? h * ratio : natural.Width;
            else if (h == null) h = ratio > 0 ? w / ratio : natural.Height;
            if (!(w > 0 && h > 0)) return Rect.Empty;
            return new Rect(Len(e, s, "x", Axis.X, 0), Len(e, s, "y", Axis.Y, 0), w.Value, h.Value);
        }

        // the picture an <image> names, read once: a BitmapSource, an SvgImage, or null
        object Loaded(Element e)
        {
            lock (image.Pictures)
            {
                if (image.Pictures.TryGetValue(e, out object kept)) return kept;
            }
            object made = null;
            if (SvgImage.DataUrl(Href(e), out byte[] bytes, out string type, out string charset))
            {
                if (type == "image/svg+xml")
                {
                    try
                    {
                        made = SvgImage.Read(bytes, charset, image.Depth + 1);
                    }
                    catch (SvgException)
                    {
                        // a broken SVG picture draws nothing
                    }
                }
                else made = Raster(bytes);
            }
            lock (image.Pictures) image.Pictures[e] = made;
            return made;
        }

        // the kinds of picture browsers draw, known by their first bytes whatever the address says
        BitmapSource Raster(byte[] bytes)
        {
            bool Starts(params int[] b) => bytes.Length >= b.Length && b.Select((v, i) => v < 0 || bytes[i] == v).All(x => x);
            bool known = Starts(0x89, 'P', 'N', 'G') || Starts(0xFF, 0xD8, 0xFF) || Starts('G', 'I', 'F', '8') || Starts('B', 'M') ||
                         Starts('R', 'I', 'F', 'F', -1, -1, -1, -1, 'W', 'E', 'B', 'P') || Starts(0, 0, 1, 0);
            if (!known) return null;
            try
            {
                BitmapFrame first = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
                long size = (long)first.PixelWidth * first.PixelHeight;
                lock (image.Pictures)
                {
                    if (image.PicturePixels + size > MostPixels) return null;   // too big to hold
                    image.PicturePixels += size;
                }
            }
            catch (Exception x) when (x is NotSupportedException || x is FileFormatException || x is ArgumentException || x is IOException ||
                                      x is InvalidOperationException || x is System.Runtime.InteropServices.ExternalException || x is OverflowException)
            {
                return null;
            }
            return Design.Images.FromBytes(bytes);
        }

        // ---------- text ----------

        sealed class Run
        {
            public readonly StringBuilder Text = new StringBuilder();
            public Style Style;
            public bool Emoji;          // emoji shown as their pictures (see Face)
            public int Characters;      // as Unicode's clusters have them (a letter with its marks, an emoji), for letter-spacing
            public double X, Y, Advance;
            public int Chunk;
            public Geometry Outline;
            public List<(GlyphRun Glyph, Color? Colour)> Layers;   // a colour font's glyphs, as their layers (see Glyphs), or null
            public Rect Cell;
        }

        sealed class Laid
        {
            public readonly List<Run> Runs = new List<Run>();
            public Rect Box = Rect.Empty;

            public Geometry Outline()
            {
                var all = new GeometryGroup { FillRule = FillRule.Nonzero };
                foreach (Run r in Runs)
                    if (r.Outline != null && r.Style.Visible) all.Children.Add(r.Outline);
                all.Freeze();
                return all;
            }

            // what its emoji cover, in black, for a clip path: all of each one's layers, as Chromium clips to the whole
            // picture (not its plain glyph's outline, which has holes); null when it has none
            public Drawing Covered()
            {
                DrawingGroup all = null;
                foreach (Run r in Runs)
                    if (r.Layers != null && r.Style.Visible)
                        foreach (var (glyph, _) in r.Layers)
                        {
                            all = all ?? new DrawingGroup();
                            all.Children.Add(new GlyphRunDrawing(Brushes.Black, glyph));
                        }
                all?.Freeze();
                return all;
            }
        }

        void Text(DrawingContext dc, Element e, Style s)
        {
            Laid laid = Lay(e, s);
            foreach (Run run in laid.Runs)
            {
                if (run.Outline == null || !run.Style.Visible) continue;
                // gradients on text span the whole <text>
                Brush fill = Brush(run.Style.Fill, run.Style.FillOpacity, laid.Box, run.Style);
                Pen pen = Pen(run.Style, laid.Box, null, out _);
                if (run.Layers == null)
                {
                    PaintShape(dc, run.Outline, fill, pen, run.Style, null);
                    continue;
                }
                bool strokeFirst = run.Style.StrokeFirst && fill != null && pen != null;
                if (!strokeFirst) PaintRun(dc, run, fill, null);
                PaintRun(dc, run, null, pen);
                if (strokeFirst) PaintRun(dc, run, fill, null);
            }
        }

        // A run of emoji filled, or stroked: its plain glyphs as any text's, and its colour glyphs as their layers, whole and
        // in their own colours (palette entry 0xFFFF in the paint itself), as see-through as the paint is, all at once (a
        // layer never shows through the one on it). Chromium draws them so in the fill and again in the stroke, whatever
        // the stroke's width or the paint's gradient: with no fill and a stroke they're drawn once, and with neither not at
        // all.
        void PaintRun(DrawingContext dc, Run run, Brush fill, Pen pen)
        {
            Brush paint = fill ?? pen?.Brush;
            if (paint == null) return;
            dc.DrawGeometry(fill, pen, run.Outline);
            double alpha = paint.Opacity * (paint is SolidColorBrush solid ? solid.Color.A / 255.0 : 1);
            if (alpha <= 0) return;
            Brush own = paint;   // the paint in its own colours, for 0xFFFF
            int pushes = 0;
            if (alpha < 1)
            {
                own = paint is SolidColorBrush s ? Solid(Color.FromRgb(s.Color.R, s.Color.G, s.Color.B), 1) : paint.CloneCurrentValue();
                if (!own.IsFrozen)
                {
                    own.Opacity = 1;
                    own.Freeze();
                }
                dc.PushOpacity(alpha);
                Pushed(ref pushes);
            }
            var brushes = new Dictionary<Color, Brush>();
            foreach (var (glyph, colour) in run.Layers)
            {
                Brush ink = own;
                if (colour.HasValue && !brushes.TryGetValue(colour.Value, out ink)) brushes[colour.Value] = ink = Solid(colour.Value, 1);
                if (ink != null) dc.DrawGlyphRun(ink, glyph);
            }
            Pop(dc, pushes);
        }

        // The characters of a <text> with its <tspan>s, laid out: white space dealt with as Chromium
        // does, x, y, dx and dy for each character, runs in their fonts, and text-anchor for each chunk.
        Laid Lay(Element text, Style s)
        {
            var chars = new StringBuilder();
            var styles = new List<Style>();
            var spans = new List<Span>();
            Collect(text, s, chars, styles, spans);
            // white space at the end goes too
            while (chars.Length > 0 && chars[chars.Length - 1] == ' ' && !styles[styles.Count - 1].PreserveSpace)
            {
                chars.Length--;
                styles.RemoveAt(styles.Count - 1);
            }
            string all = chars.ToString();
            MarksOnALetter(all);
            int n = chars.Length;
            // Unicode's clusters (a letter with its marks, an emoji's sequence, a character past U+FFFF), which Chromium
            // keeps whole, but where a character in one is placed; and which are emoji shown as their pictures
            var joined = new bool[n];
            var emoji = new bool[n];
            int at = 0;
            foreach (string cluster in Qr.Unicode.Clusters(all, MostMarks))
            {
                bool picture = Qr.Unicode.AsksForEmoji(cluster);
                for (int k = 0; k < cluster.Length && at + k < n; k++)
                {
                    joined[at + k] = k > 0;
                    emoji[at + k] = picture;
                }
                at += cluster.Length;
            }
            var ax = new double?[n];
            var ay = new double?[n];
            var dx = new double[n];
            var dy = new double[n];
            foreach (Span span in spans)
            {
                Positions(span, "x", Axis.X, all, (i, v) => ax[i] = v);
                Positions(span, "y", Axis.Y, all, (i, v) => ay[i] = v);
                Positions(span, "dx", Axis.X, all, (i, v) => dx[i] = v);
                Positions(span, "dy", Axis.Y, all, (i, v) => dy[i] = v);
            }

            var laid = new Laid();
            double penX = 0, penY = 0;
            int chunk = 0;
            Run run = null;
            for (int i = 0; i < n; i++)
            {
                Style cs = styles[i];
                bool spaced = cs.LetterSpacing.HasValue && cs.LetterSpacing.Value.Value != 0 || cs.WordSpacing.HasValue && cs.WordSpacing.Value.Value != 0;
                bool placed = ax[i].HasValue || ay[i].HasValue;
                // a new run where a character is placed or moved, and where the style, the spacing or the font changes,
                // which is never inside a cluster
                if (run == null || placed || dx[i] != 0 || dy[i] != 0 || !joined[i] && (cs != run.Style || spaced || emoji[i] != run.Emoji))
                {
                    penX += Measure(run);
                    if (placed && i > 0) chunk++;
                    if (ax[i].HasValue) penX = ax[i].Value;
                    if (ay[i].HasValue) penY = ay[i].Value;
                    penX += dx[i];
                    penY += dy[i];
                    run = new Run { Style = cs, Emoji = emoji[i], X = penX, Y = penY, Chunk = chunk };
                    laid.Runs.Add(run);
                }
                if (!joined[i]) run.Characters++;
                run.Text.Append(chars[i]);
            }
            Measure(run);

            // text-anchor: each chunk moved by its own length, as its first character says
            foreach (IGrouping<int, Run> c in laid.Runs.GroupBy(r => r.Chunk))
            {
                Run first = c.First(), last = c.Last();
                double length = last.X + last.Advance - first.X;
                double shift = first.Style.TextAnchor == "middle" ? -length / 2 : first.Style.TextAnchor == "end" ? -length : 0;
                foreach (Run r in c) r.X += shift;
            }
            foreach (Run r in laid.Runs)
            {
                if (r.Text.Length == 0 || !Sized(r.Style)) continue;
                var shaped = Shaped(r);
                double baseline = r.Y + BaselineShift(r.Style, shaped);
                r.Outline = Glyphs(r, shaped, new Point(r.X, baseline - shaped.Baseline));
                if (r.Outline.CanFreeze) r.Outline.Freeze();
                r.Cell = new Rect(r.X, baseline - shaped.Baseline, Math.Max(0, r.Advance), shaped.Height);
                laid.Box.Union(r.Cell);
            }
            return laid;
        }

        // A run's outline, as FormattedText.BuildGeometry makes it: the outlines of the runs of glyphs WPF lays it out
        // in. In a run of emoji, a colour font's glyphs are kept apart as their layers (Run.Layers), each counted as a
        // shape drawn (one emoji can have 120), to be painted in their colours.
        Geometry Glyphs(Run r, FormattedText shaped, Point at)
        {
            var drawn = new DrawingGroup();
            using (DrawingContext dc = drawn.Open()) dc.DrawText(shaped, at);
            var outline = new GeometryGroup { FillRule = FillRule.Nonzero };
            var layers = new List<(GlyphRun, Color?)>();
            void Walk(Drawing d)
            {
                if (d is DrawingGroup group)
                {
                    foreach (Drawing child in group.Children) Walk(child);
                    return;
                }
                if (!(d is GlyphRunDrawing glyphs)) return;
                int count = r.Emoji ? Qr.ColourGlyphs.Layers(glyphs.GlyphRun) : 0;
                if (count > 0)
                {
                    budget.Shapes(count);
                    if (Qr.ColourGlyphs.Split(glyphs.GlyphRun, outline, layers)) return;
                }
                Geometry g = glyphs.GlyphRun.BuildGeometry();
                if (!g.IsEmpty()) outline.Children.Add(g);
            }
            Walk(drawn);
            if (layers.Count > 0) r.Layers = layers;
            return outline.Children.Count == 0 ? Geometry.Empty : outline;
        }

        sealed class Span
        {
            public Element Element;
            public Style Style;
            public int Start, End;
        }

        // WPF's text layout ends the program (no exception, nothing to catch it) at a letter with about
        // 700 marks on it (accents, variation selectors, joiners): text with far fewer in a row is refused
        // first, counted as a caption counts them (Unicode.OverTheLimit: Unicode 17's marks, the joiners,
        // and what isn't a character yet, which a later Windows may know as a mark)
        const int MostMarks = 64;

        static void MarksOnALetter(string text)
        {
            if (Qr.Unicode.OverTheLimit(text, MostMarks)) throw new SvgException("its text has more than " + MostMarks + " accents or other marks on one letter");
        }

        // the characters inside `e`, white space collapsed unless it's kept (xml:space="preserve")
        void Collect(Element e, Style s, StringBuilder chars, List<Style> styles, List<Span> spans)
        {
            budget.In();   // a step deeper for each <tspan> inside another
            var span = new Span { Element = e, Style = s, Start = chars.Length };
            spans.Add(span);
            foreach (object o in e.Content)
            {
                if (o is string text)
                {
                    budget.Read(text.Length);   // all of it read, the white space that collapses too
                    foreach (char raw in text)
                    {
                        char c = raw == '\n' || raw == '\t' || raw == '\r' ? ' ' : raw;
                        if (!s.PreserveSpace && c == ' ' && (chars.Length == 0 || chars[chars.Length - 1] == ' ')) continue;
                        budget.Character();   // counted as it's read, so a huge text is stopped before it all is
                        chars.Append(c);
                        styles.Add(s);
                    }
                    continue;
                }
                var child = (Element)o;
                budget.Look();   // a <tspan> or not
                if (child.Namespace != Xml.SvgSpace || !(child.Name == "tspan" || child.Name == "a") || !Passes(child)) continue;
                budget.One();
                Style cs = Styled(child, s);
                if (!cs.Display) continue;
                Collect(child, cs, chars, styles, spans);
            }
            span.End = chars.Length;
            budget.Out();
        }

        // x, y, dx or dy on one element: a list of lengths, one for each of its characters in turn (read
        // only as far as it has characters, however long the list is), a character past U+FFFF taking one
        // as in Chromium, not one for each half
        void Positions(Span span, string name, Axis axis, string text, Action<int, double> set)
        {
            string list = span.Element[name];
            if (list == null) return;
            int i = span.Start, at = 0;
            while (i < span.End && i < text.Length)
            {
                if (i > 0 && char.IsLowSurrogate(text[i]) && char.IsHighSurrogate(text[i - 1]))
                {
                    i++;
                    continue;
                }
                while (at < list.Length && Gap(list[at])) at++;
                if (at == list.Length) return;
                int start = at;
                while (at < list.Length && !Gap(list[at])) at++;
                if (!Length.TryParse(list.Substring(start, at - start), out Length l)) return;   // a list with a mistake in it counts for nothing
                set(i++, Px(l, axis, span.Style));
            }
        }

        static bool Gap(char c) => c == ' ' || c == ',' || c == '\t' || c == '\n' || c == '\r';

        // a run's text in its font: its advance (with letter and word spacing), which moves the pen
        double Measure(Run run)
        {
            if (run == null || run.Text.Length == 0) return 0;
            Style s = run.Style;
            double advance = Sized(s) ? Shaped(run).WidthIncludingTrailingWhitespace : 0;
            if (s.LetterSpacing.HasValue) advance += run.Characters * Px(s.LetterSpacing.Value, Axis.X, s);
            if (s.WordSpacing.HasValue)
                foreach (char c in run.Text.ToString())
                    if (c == ' ') advance += Px(s.WordSpacing.Value, Axis.X, s);
            run.Advance = advance;
            return advance;
        }

        // Is text in this style drawn at all? Chromium draws a font size below a float's epsilon as none (font-size=
        // "1e-7" is 0 px in Edge 156, "1.2e-7" isn't; sizes in em of it are worked out from what it says, as here).
        // A size is never more than Style.MostFontSize, 10,000 px, which WPF lays out (it refuses one past 35,791 px,
        // or NaN).
        static bool Sized(Style s) => s.FontSize >= LeastFontSize;

        const double LeastFontSize = 1.1920929e-7;   // a float's epsilon

        static FormattedText Shaped(Run run)
        {
            Style s = run.Style;
            return new FormattedText(run.Text.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face(s, run.Emoji), s.FontSize, System.Windows.Media.Brushes.Black, 1.0);
        }

        // where the baseline goes for dominant-baseline (middle, central, hanging and the edges)
        static double BaselineShift(Style s, FormattedText shaped)
        {
            double ascent = shaped.Baseline, descent = shaped.Height - shaped.Baseline;
            switch (s.Baseline)
            {
                case "middle": return XHeight(s) / 2;
                case "central": return (ascent - descent) / 2;
                case "hanging": return ascent * 0.8;
                case "mathematical": return ascent / 2;
                case "text-before-edge":
                case "text-top": return ascent;
                case "text-after-edge":
                case "text-bottom":
                case "ideographic": return -descent;
                default: return 0;
            }
        }

        // ---------- fonts ----------

        static readonly object fontGate = new object();
        static HashSet<string> installed;
        static readonly Dictionary<string, string[]> resolved = new Dictionary<string, string[]>();

        // Chromium's fonts for CSS's generic families on Windows
        static string Generic(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "sans-serif": return "Arial";
                case "serif": return "Times New Roman";
                case "monospace": return "Consolas";
                case "cursive": return "Comic Sans MS";
                case "fantasy": return "Impact";
                case "system-ui": return "Segoe UI";
                default: return null;
            }
        }

        // The fonts a font-family list comes to on this PC, in order: the ones it names that are
        // installed (only those: a name is never taken as a place to load a font from), then
        // Chromium's default, Times New Roman.
        static string[] Families(string[] names)
        {
            string key = names == null ? "" : string.Join("\n", names);
            lock (fontGate)
            {
                if (resolved.TryGetValue(key, out string[] found)) return found;
                var list = new List<string>();
                foreach (string name in names ?? new string[0])
                {
                    string font = Generic(name) ?? (Installed(name) ? name : null);
                    if (font != null && !list.Contains(font, StringComparer.OrdinalIgnoreCase) && font.IndexOfAny(new[] { ',', '#', '/', '\\' }) < 0) list.Add(font);
                }
                if (!list.Contains("Times New Roman", StringComparer.OrdinalIgnoreCase)) list.Add("Times New Roman");
                found = list.ToArray();
                if (resolved.Count < 256) resolved[key] = found;
                return found;
            }
        }

        static bool Installed(string name)
        {
            lock (fontGate)
            {
                if (installed == null)
                {
                    var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (FontFamily f in Fonts.SystemFontFamilies) all.Add(f.Source);
                    installed = all;
                }
                return installed.Contains(name);
            }
        }

        const string EmojiFont = "Segoe UI Emoji";

        // A style's typeface, falling back from font to font for characters one doesn't have; for emoji shown as their
        // pictures (Unicode.AsksForEmoji: 😀, ⭐, ❤️, a family...) Segoe UI Emoji first, the font Chromium draws them
        // from, as a caption does (WPF's own choice is often another: Segoe UI Symbol's ⭐ in black, Arial's ☺ for ☺️)
        static Typeface Face(Style s, bool emoji = false)
        {
            string families = string.Join(", ", Families(s.FontFamily));
            if (emoji && Installed(EmojiFont)) families = EmojiFont + ", " + families;
            return new Typeface(new FontFamily(families), s.Italic ? FontStyles.Italic : FontStyles.Normal, FontWeight.FromOpenTypeWeight(Math.Max(1, Math.Min(999, s.FontWeight))), FontStretches.Normal);
        }

        // the x-height of a style's first font, in px (for ex units and dominant-baseline: middle)
        internal static double XHeight(Style s)
        {
            var face = new Typeface(new FontFamily(Families(s.FontFamily)[0]), s.Italic ? FontStyles.Italic : FontStyles.Normal,
                                    FontWeight.FromOpenTypeWeight(Math.Max(1, Math.Min(999, s.FontWeight))), FontStretches.Normal);
            return face.TryGetGlyphTypeface(out GlyphTypeface glyphs) ? glyphs.XHeight * s.FontSize : s.FontSize / 2;
        }
    }
}
