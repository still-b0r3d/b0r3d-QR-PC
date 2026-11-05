using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace B0r3dQR.Svg
{
    // An outline of figures (lines and curves, each figure open or closed), made from SVG path data
    // or a basic shape, ready to become WPF geometry. Arcs become curves here, the way SVG's own
    // notes work them out.
    sealed class Figures
    {
        enum Kind { Line, Quad, Cubic }

        struct Segment
        {
            public Kind Kind;
            public Point A, B, To;   // control points (as many as the kind has), then the end
        }

        sealed class Figure
        {
            public Point Start;
            public bool Closed;
            public readonly List<Segment> Segments = new List<Segment>();
        }

        readonly List<Figure> figures = new List<Figure>();
        Figure open;
        Point current;
        int count;

        public bool IsEmpty => figures.Count == 0;
        public Point Current => current;

        // how many points it has: each figure's start and each segment's end
        public int Count => count;

        public void Move(Point p)
        {
            open = new Figure { Start = p };
            figures.Add(open);
            current = p;
            count++;
        }

        // a figure to add to: after a close, a new one starts where the last one started
        Figure Open()
        {
            if (open == null) Move(current);
            return open;
        }

        public void Line(Point p)
        {
            Open().Segments.Add(new Segment { Kind = Kind.Line, To = p });
            current = p;
            count++;
        }

        public void Quad(Point c, Point p)
        {
            Open().Segments.Add(new Segment { Kind = Kind.Quad, A = c, To = p });
            current = p;
            count++;
        }

        public void Cubic(Point c1, Point c2, Point p)
        {
            Open().Segments.Add(new Segment { Kind = Kind.Cubic, A = c1, B = c2, To = p });
            current = p;
            count++;
        }

        public void Close()
        {
            if (open == null) return;
            open.Closed = true;
            current = open.Start;
            open = null;
        }

        // An elliptical arc to `to` (SVG's A command): radii too small to reach are grown, a zero
        // radius is a straight line, and an arc to where it starts is no arc at all.
        public void Arc(double rx, double ry, double degrees, bool large, bool sweep, Point to)
        {
            Point from = current;
            if (from == to) return;
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx == 0 || ry == 0)
            {
                Line(to);
                return;
            }
            double phi = degrees * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (from.X - to.X) / 2, dy = (from.Y - to.Y) / 2;
            double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
            double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
            if (lambda > 1)
            {
                rx *= Math.Sqrt(lambda);
                ry *= Math.Sqrt(lambda);
            }
            double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1, den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            double coef = (large == sweep ? -1 : 1) * Math.Sqrt(Math.Max(0, num / den));
            double cxp = coef * rx * y1 / ry, cyp = -coef * ry * x1 / rx;
            double cx = cos * cxp - sin * cyp + (from.X + to.X) / 2, cy = sin * cxp + cos * cyp + (from.Y + to.Y) / 2;
            double start = Math.Atan2((y1 - cyp) / ry, (x1 - cxp) / rx);
            double end = Math.Atan2((-y1 - cyp) / ry, (-x1 - cxp) / rx);
            double sweepAngle = end - start;
            if (!sweep && sweepAngle > 0) sweepAngle -= 2 * Math.PI;
            else if (sweep && sweepAngle < 0) sweepAngle += 2 * Math.PI;
            int pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepAngle) / (Math.PI / 2) - 1e-9));
            double step = sweepAngle / pieces, k = 4.0 / 3 * Math.Tan(step / 4);
            Point At(double t) => new Point(cx + rx * Math.Cos(t) * cos - ry * Math.Sin(t) * sin, cy + rx * Math.Cos(t) * sin + ry * Math.Sin(t) * cos);
            Vector Slope(double t) => new Vector(-rx * Math.Sin(t) * cos - ry * Math.Cos(t) * sin, -rx * Math.Sin(t) * sin + ry * Math.Cos(t) * cos);
            double t1 = start;
            for (int i = 0; i < pieces; i++)
            {
                double t2 = t1 + step;
                Point p1 = At(t1), p2 = i == pieces - 1 ? to : At(t2);
                Cubic(p1 + k * Slope(t1), p2 - k * Slope(t2), p2);
                t1 = t2;
            }
        }

        // ---------- as WPF geometry ----------

        public Geometry ToGeometry(FillRule rule)
        {
            if (figures.Count == 0) return null;
            var g = new StreamGeometry { FillRule = rule };
            using (StreamGeometryContext c = g.Open())
            {
                foreach (Figure f in figures)
                {
                    c.BeginFigure(f.Start, true, f.Closed);
                    foreach (Segment s in f.Segments)
                    {
                        switch (s.Kind)
                        {
                            case Kind.Line: c.LineTo(s.To, true, false); break;
                            case Kind.Quad: c.QuadraticBezierTo(s.A, s.To, true, false); break;
                            default: c.BezierTo(s.A, s.B, s.To, true, false); break;
                        }
                    }
                }
            }
            g.Freeze();
            return g;
        }

        // Where the figures of no length are (a closed figure or segments that go nowhere): round and
        // square line caps draw a dot or a square there, as in browsers.
        public List<Point> ZeroLength()
        {
            var found = new List<Point>();
            foreach (Figure f in figures)
            {
                if (f.Segments.Count == 0 && !f.Closed) continue;
                bool nowhere = true;
                foreach (Segment s in f.Segments)
                    if (s.To != f.Start || s.Kind != Kind.Line && (s.A != f.Start || s.Kind == Kind.Cubic && s.B != f.Start)) nowhere = false;
                if (nowhere) found.Add(f.Start);
            }
            return found;
        }

        // About how long the figures are, never less (curves are measured along their control points):
        // enough to know a dash pattern would make far too many dashes.
        public double RoughLength()
        {
            double length = 0;
            foreach (Figure f in figures)
            {
                Point at = f.Start;
                foreach (Segment s in f.Segments)
                {
                    if (s.Kind == Kind.Line) length += (s.To - at).Length;
                    else if (s.Kind == Kind.Quad) length += (s.A - at).Length + (s.To - s.A).Length;
                    else length += (s.A - at).Length + (s.B - s.A).Length + (s.To - s.B).Length;
                    at = s.To;
                }
                if (f.Closed) length += (f.Start - at).Length;
            }
            return length;
        }

        // The corners where one segment meets the next (and in a closed figure, the last the first):
        // where, and the directions coming in and going out. Segments of no length don't count.
        public List<(Point At, Vector In, Vector Out)> Joins()
        {
            var joins = new List<(Point, Vector, Vector)>();
            foreach (Figure f in figures)
            {
                var ends = new List<(Point From, Point To, Vector Start, Vector End)>();
                Point at = f.Start;
                foreach (Segment s in f.Segments)
                {
                    Vector start = First(s.Kind == Kind.Line ? s.To - at : s.A - at, s.Kind == Kind.Cubic ? s.B - at : s.To - at, s.To - at);
                    Vector end = First(s.Kind == Kind.Line ? s.To - at : s.Kind == Kind.Cubic ? s.To - s.B : s.To - s.A, s.Kind == Kind.Cubic ? s.To - s.A : s.To - at, s.To - at);
                    if (start.Length > 0) ends.Add((at, s.To, start, end));
                    at = s.To;
                }
                if (f.Closed && (f.Start - at).Length > 0) ends.Add((at, f.Start, f.Start - at, f.Start - at));
                for (int i = 1; i < ends.Count; i++) joins.Add((ends[i].From, ends[i - 1].End, ends[i].Start));
                if (f.Closed && ends.Count > 1) joins.Add((ends[0].From, ends[ends.Count - 1].End, ends[0].Start));
            }
            return joins;
        }

        static Vector First(Vector a, Vector b, Vector c) => a.Length > 0 ? a : b.Length > 0 ? b : c;

        // ---------- SVG path data ----------

        // The figures in SVG path data, as Chromium reads it: up to the first mistake, which leaves
        // out the segment it's in and everything after it. Past `most` points the rest isn't read
        // (more than a picture draws: the caller refuses it).
        public static Figures FromPathData(string d, int most = int.MaxValue)
        {
            var f = new Figures();
            if (string.IsNullOrEmpty(d)) return f;
            var n = new Numbers(d);
            char previous = '\0';
            Point start = new Point(), control = new Point();   // the figure's start; the last curve's control point
            char lastCurve = '\0';                                 // 'C' or 'Q' when the last segment was that kind of curve
            n.SkipSpaces();
            while (!n.AtEnd && f.count <= most)
            {
                char c = n.Next, command;
                if ("MmZzLlHhVvCcSsQqTtAa".IndexOf(c) >= 0)
                {
                    if (previous == '\0' && c != 'M' && c != 'm') break;
                    command = c;
                    n.At++;
                }
                else
                {
                    // numbers after a command repeat it (after a move, as lines); a close takes none
                    if (previous == '\0' || previous == 'Z' || previous == 'z' || !Numbers.NumberStart(c)) break;
                    command = previous == 'M' ? 'L' : previous == 'm' ? 'l' : previous;
                }
                bool relative = char.IsLower(command);
                Point here = f.current;
                double Dx(double v) => relative ? here.X + v : v;
                double Dy(double v) => relative ? here.Y + v : v;
                var a = new double[7];
                int need;
                switch (char.ToUpperInvariant(command))
                {
                    case 'Z': need = 0; break;
                    case 'H':
                    case 'V': need = 1; break;
                    case 'M':
                    case 'L':
                    case 'T': need = 2; break;
                    case 'S':
                    case 'Q': need = 4; break;
                    case 'C': need = 6; break;
                    default: need = 7; break;   // A
                }
                n.SkipSpaces();
                bool ok = true;
                bool large = false, sweep = false;
                for (int i = 0; i < need && ok; i++)
                {
                    if (need == 7 && (i == 3 || i == 4))
                    {
                        ok = n.Flag(out bool flag);
                        if (i == 3) large = flag;
                        else sweep = flag;
                    }
                    else ok = n.NumberThenSeparator(out a[i]);
                }
                if (!ok) break;
                char curve = '\0';
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        f.Move(new Point(Dx(a[0]), Dy(a[1])));
                        start = f.current;
                        break;
                    case 'Z':
                        f.Close();
                        break;
                    case 'L':
                        f.Line(new Point(Dx(a[0]), Dy(a[1])));
                        break;
                    case 'H':
                        f.Line(new Point(Dx(a[0]), here.Y));
                        break;
                    case 'V':
                        f.Line(new Point(here.X, Dy(a[0])));
                        break;
                    case 'C':
                        control = new Point(Dx(a[2]), Dy(a[3]));
                        f.Cubic(new Point(Dx(a[0]), Dy(a[1])), control, new Point(Dx(a[4]), Dy(a[5])));
                        curve = 'C';
                        break;
                    case 'S':
                    {
                        Point first = lastCurve == 'C' ? new Point(2 * here.X - control.X, 2 * here.Y - control.Y) : here;
                        control = new Point(Dx(a[0]), Dy(a[1]));
                        f.Cubic(first, control, new Point(Dx(a[2]), Dy(a[3])));
                        curve = 'C';
                        break;
                    }
                    case 'Q':
                        control = new Point(Dx(a[0]), Dy(a[1]));
                        f.Quad(control, new Point(Dx(a[2]), Dy(a[3])));
                        curve = 'Q';
                        break;
                    case 'T':
                        control = lastCurve == 'Q' ? new Point(2 * here.X - control.X, 2 * here.Y - control.Y) : here;
                        f.Quad(control, new Point(Dx(a[0]), Dy(a[1])));
                        curve = 'Q';
                        break;
                    default:   // A
                        f.Arc(a[0], a[1], a[2], large, sweep, new Point(Dx(a[5]), Dy(a[6])));
                        break;
                }
                lastCurve = curve;
                previous = command;
                n.SkipSpaces();
            }
            return f;
        }

        // ---------- the basic shapes, drawn the way SVG describes them (which sets where dashes start) ----------

        public static Figures Rectangle(double x, double y, double w, double h, double rx, double ry)
        {
            var f = new Figures();
            if (rx <= 0 || ry <= 0)
            {
                f.Move(new Point(x, y));
                f.Line(new Point(x + w, y));
                f.Line(new Point(x + w, y + h));
                f.Line(new Point(x, y + h));
                f.Close();
                return f;
            }
            f.Move(new Point(x + rx, y));
            f.Line(new Point(x + w - rx, y));
            f.Arc(rx, ry, 0, false, true, new Point(x + w, y + ry));
            f.Line(new Point(x + w, y + h - ry));
            f.Arc(rx, ry, 0, false, true, new Point(x + w - rx, y + h));
            f.Line(new Point(x + rx, y + h));
            f.Arc(rx, ry, 0, false, true, new Point(x, y + h - ry));
            f.Line(new Point(x, y + ry));
            f.Arc(rx, ry, 0, false, true, new Point(x + rx, y));
            f.Close();
            return f;
        }

        // an ellipse from its right-hand point, clockwise on screen
        public static Figures Ellipse(double cx, double cy, double rx, double ry)
        {
            var f = new Figures();
            f.Move(new Point(cx + rx, cy));
            f.Arc(rx, ry, 0, false, true, new Point(cx, cy + ry));
            f.Arc(rx, ry, 0, false, true, new Point(cx - rx, cy));
            f.Arc(rx, ry, 0, false, true, new Point(cx, cy - ry));
            f.Arc(rx, ry, 0, false, true, new Point(cx + rx, cy));
            f.Close();
            return f;
        }

        // polyline and polygon: pairs of numbers, up to the first mistake (a lone number left over is
        // one); past `most` points the rest isn't read, as for path data
        public static Figures Points(string points, bool closed, int most = int.MaxValue)
        {
            var f = new Figures();
            var n = new Numbers(points);
            n.SkipSpaces();
            while (!n.AtEnd && f.count <= most)
            {
                if (!n.NumberThenSeparator(out double x) || !n.NumberThenSeparator(out double y)) break;
                var p = new Point(x, y);
                if (f.IsEmpty) f.Move(p);
                else f.Line(p);
            }
            if (closed && !f.IsEmpty) f.Close();
            return f;
        }
    }
}
