using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace B0r3dQR.Qr
{
    // Shapes as path commands, in modules: the one description that becomes WPF geometry (for the
    // preview, PNG, JPG and PDF) and SVG path data (for the SVG file), so every export is drawn from
    // the same outlines. Holes are drawn the other way round, so they stay holes where shapes are
    // filled by winding (dots that touch simply join).
    sealed class Outline
    {
        enum Kind { Move, Line, Arc, Cubic, Close }

        struct Command
        {
            public Kind Kind;
            public double X, Y;            // where it ends
            public double R;               // an arc's radius
            public bool Clockwise, Large;  // an arc's direction (on screen, y down) and size
            public double X1, Y1, X2, Y2;  // a curve's control points
        }

        readonly List<Command> commands = new List<Command>();

        public bool IsEmpty => commands.Count == 0;

        public void Move(double x, double y) => commands.Add(new Command { Kind = Kind.Move, X = x, Y = y });

        public void Line(double x, double y) => commands.Add(new Command { Kind = Kind.Line, X = x, Y = y });

        // a circular arc from where the path is to (x, y)
        public void Arc(double x, double y, double radius, bool clockwise, bool large = false) =>
            commands.Add(new Command { Kind = Kind.Arc, X = x, Y = y, R = radius, Clockwise = clockwise, Large = large });

        public void Cubic(double x1, double y1, double x2, double y2, double x, double y) =>
            commands.Add(new Command { Kind = Kind.Cubic, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, X = x, Y = y });

        public void Close() => commands.Add(new Command { Kind = Kind.Close });

        public void Add(Outline other) => commands.AddRange(other.commands);

        // ---------- common shapes (clockwise on screen unless `hole`) ----------

        public void Rectangle(double x, double y, double w, double h, bool hole = false)
        {
            Move(x, y);
            if (!hole)
            {
                Line(x + w, y);
                Line(x + w, y + h);
                Line(x, y + h);
            }
            else
            {
                Line(x, y + h);
                Line(x + w, y + h);
                Line(x + w, y);
            }
            Close();
        }

        public void Circle(double cx, double cy, double r, bool hole = false)
        {
            Move(cx, cy - r);
            Arc(cx, cy + r, r, !hole);
            Arc(cx, cy - r, r, !hole);
            Close();
        }

        // a rectangle with each corner rounded by its own radius (top-left, top-right, bottom-right,
        // bottom-left); a radius of 0 is a square corner
        public void RoundedRectangle(double x, double y, double w, double h, double tl, double tr, double br, double bl, bool hole = false)
        {
            double most = Math.Min(w, h) / 2;
            tl = Math.Min(tl, most); tr = Math.Min(tr, most); br = Math.Min(br, most); bl = Math.Min(bl, most);
            if (!hole)
            {
                Move(x + tl, y);
                Line(x + w - tr, y);
                if (tr > 0) Arc(x + w, y + tr, tr, true);
                Line(x + w, y + h - br);
                if (br > 0) Arc(x + w - br, y + h, br, true);
                Line(x + bl, y + h);
                if (bl > 0) Arc(x, y + h - bl, bl, true);
                Line(x, y + tl);
                if (tl > 0) Arc(x + tl, y, tl, true);
            }
            else
            {
                Move(x + tl, y);
                if (tl > 0) Arc(x, y + tl, tl, false);
                Line(x, y + h - bl);
                if (bl > 0) Arc(x + bl, y + h, bl, false);
                Line(x + w - br, y + h);
                if (br > 0) Arc(x + w, y + h - br, br, false);
                Line(x + w, y + tr);
                if (tr > 0) Arc(x + w - tr, y, tr, false);
            }
            Close();
        }

        // ---------- as WPF geometry ----------

        // `scale` units to a module, moved by (dx, dy) units
        public Geometry ToGeometry(double scale, double dx = 0, double dy = 0)
        {
            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (StreamGeometryContext c = geometry.Open())
            {
                bool open = false;
                Point P(double x, double y) => new Point(dx + x * scale, dy + y * scale);
                foreach (Command k in commands)
                {
                    switch (k.Kind)
                    {
                        case Kind.Move:
                            c.BeginFigure(P(k.X, k.Y), true, true);
                            open = true;
                            break;
                        case Kind.Line:
                            if (open) c.LineTo(P(k.X, k.Y), false, false);
                            break;
                        case Kind.Arc:
                            if (open) c.ArcTo(P(k.X, k.Y), new Size(k.R * scale, k.R * scale), 0, k.Large, k.Clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, false, false);
                            break;
                        case Kind.Cubic:
                            if (open) c.BezierTo(P(k.X1, k.Y1), P(k.X2, k.Y2), P(k.X, k.Y), false, false);
                            break;
                        case Kind.Close:
                            open = false;
                            break;
                    }
                }
            }
            geometry.Freeze();
            return geometry;
        }

        // ---------- as SVG path data ----------

        // `scale` units to a module, moved by (dx, dy); numbers to 3 decimals at most
        public string ToSvgPath(double scale = 1, double dx = 0, double dy = 0)
        {
            var d = new StringBuilder(commands.Count * 12);
            foreach (Command k in commands)
            {
                switch (k.Kind)
                {
                    case Kind.Move:
                        d.Append('M').Append(N(dx + k.X * scale)).Append(' ').Append(N(dy + k.Y * scale));
                        break;
                    case Kind.Line:
                        d.Append('L').Append(N(dx + k.X * scale)).Append(' ').Append(N(dy + k.Y * scale));
                        break;
                    case Kind.Arc:
                        string r = N(k.R * scale);
                        d.Append('A').Append(r).Append(' ').Append(r).Append(" 0 ").Append(k.Large ? '1' : '0').Append(' ').Append(k.Clockwise ? '1' : '0')
                         .Append(' ').Append(N(dx + k.X * scale)).Append(' ').Append(N(dy + k.Y * scale));
                        break;
                    case Kind.Cubic:
                        d.Append('C').Append(N(dx + k.X1 * scale)).Append(' ').Append(N(dy + k.Y1 * scale)).Append(' ')
                         .Append(N(dx + k.X2 * scale)).Append(' ').Append(N(dy + k.Y2 * scale)).Append(' ')
                         .Append(N(dx + k.X * scale)).Append(' ').Append(N(dy + k.Y * scale));
                        break;
                    case Kind.Close:
                        d.Append('Z');
                        break;
                }
            }
            return d.ToString();
        }

        static string N(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
            double r = Math.Round(v, 3);
            return (r == 0 ? 0 : r).ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
