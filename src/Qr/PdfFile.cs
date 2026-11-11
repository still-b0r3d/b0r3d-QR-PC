using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR.Qr
{
    // A PDF file of one page, placed at an exact size in points (72 to an inch). QR codes, barcodes,
    // frames, SVG logos and captions (their letters as outlines, colour emoji as their layers) are
    // drawn as vector shapes; any raster picture (a PNG or JPG logo or frame background) is kept
    // without loss, with a soft mask for see-through parts. Written by hand: it's a few objects.
    static class PdfFile
    {
        public const double PointsPerInch = 72;

        sealed class EmbeddedImage
        {
            public string Name;
            public BitmapSource Source;
            public int Width, Height;
            public byte[] Rgb, Alpha;
        }

        // A form XObject: a group's shapes drawn together then made see-through as one (a transparency group),
        // or the grey shading a gradient's soft mask is made from
        sealed class Form
        {
            public string Content, Group;
            public Rect Box;
        }

        // `place` is where `canvas` goes on the page, in points from the page's top-left corner
        public static byte[] Make(Drawing drawing, Rect canvas, double pageWidth, double pageHeight, Rect place, string title)
        {
            var content = new StringBuilder(4096);
            var gsKeys = new Dictionary<string, string>();
            var gsEntries = new List<string>();
            var shadings = new List<string>();
            var images = new List<EmbeddedImage>();
            var forms = new List<Form>();

            // a form, by its number: its object is written "@F3@" until the file's objects are numbered, and its name
            // in the resources (for Do) is "Fm4"
            int AddForm(string formContent, Rect box, string group)
            {
                forms.Add(new Form { Content = formContent, Box = box, Group = group });
                return forms.Count - 1;
            }
            string FormObject(int form) => "@F" + form.ToString(CultureInfo.InvariantCulture) + "@";
            string FormName(int form) => "Fm" + (form + 1).ToString(CultureInfo.InvariantCulture);

            string AlphaState(double fillAlpha, double strokeAlpha)
            {
                string fa = C(fillAlpha), sa = C(strokeAlpha);
                string key = fa + ":" + sa;
                if (!gsKeys.TryGetValue(key, out string name))
                {
                    name = "GS" + (gsEntries.Count + 1).ToString(CultureInfo.InvariantCulture);
                    gsKeys[key] = name;
                    gsEntries.Add("/" + name + " << /Type /ExtGState /ca " + fa + " /CA " + sa + " >>");
                }
                return name;
            }

            string AddImage(BitmapSource picture)
            {
                foreach (EmbeddedImage existing in images)
                    if (ReferenceEquals(existing.Source, picture)) return existing.Name;
                var bgra = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                var pixels = new byte[(long)w * h * 4];
                bgra.CopyPixels(pixels, w * 4, 0);
                var rgb = new byte[(long)w * h * 3];
                var alpha = new byte[(long)w * h];
                bool seeThrough = false;
                for (long i = 0, j = 0; i < (long)w * h; i++, j += 4)
                {
                    byte a = pixels[j + 3];
                    alpha[i] = a;
                    if (a != 255) seeThrough = true;
                    // Bgra32 isn't premultiplied: the colours are as they are
                    rgb[i * 3] = pixels[j + 2];
                    rgb[i * 3 + 1] = pixels[j + 1];
                    rgb[i * 3 + 2] = pixels[j];
                }
                string name = "Im" + (images.Count + 1).ToString(CultureInfo.InvariantCulture);
                images.Add(new EmbeddedImage
                {
                    Name = name, Source = picture, Width = w, Height = h,
                    Rgb = Zlib(rgb), Alpha = seeThrough ? Zlib(alpha) : null,
                });
                return name;
            }

            double unitScale = canvas.Width > 0 ? place.Width / PdfFile.PointsPerInch * 300 / canvas.Width : 4;

            void DrawBitmap(BitmapSource bmp, Rect rect, double opacity)
            {
                if (bmp == null || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0 || !(rect.Width > 0 && rect.Height > 0) || opacity <= 0) return;
                string name = AddImage(bmp);
                content.Append("q\n");
                if (opacity < 0.9999)
                {
                    string gs = AlphaState(opacity, opacity);
                    content.Append('/').Append(gs).Append(" gs\n");
                }
                // the outer canvas transform flips Y (-sy), so -rect.Height flips the image's Y back upright
                content.Append(N(rect.Width)).Append(" 0 0 ").Append(N(-rect.Height)).Append(' ')
                       .Append(N(rect.Left)).Append(' ').Append(N(rect.Bottom)).Append(" cm /").Append(name).Append(" Do Q\n");
            }

            void Rasterize(Drawing d, double opacity)
            {
                Rect b = d.Bounds;
                if (b.IsEmpty || !(b.Width > 0 && b.Height > 0) || opacity <= 0) return;
                double s = Math.Max(1, Math.Min(16, unitScale));
                int pw = Math.Max(1, Math.Min(4096, (int)Math.Ceiling(b.Width * s)));
                int ph = Math.Max(1, Math.Min(4096, (int)Math.Ceiling(b.Height * s)));
                var visual = new DrawingVisual();
                using (DrawingContext dc = visual.RenderOpen())
                {
                    dc.PushTransform(new MatrixTransform(pw / b.Width, 0, 0, ph / b.Height, -b.X * pw / b.Width, -b.Y * ph / b.Height));
                    dc.DrawDrawing(d);
                    dc.Pop();
                }
                var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();
                DrawBitmap(rtb, b, opacity);
            }

            bool PaintGradient(Geometry geometry, GradientBrush gb, bool forceNonzero, double opacity)
            {
                if (gb.SpreadMethod != GradientSpreadMethod.Pad || gb.GradientStops == null || gb.GradientStops.Count == 0) return false;
                Matrix gm = gb.Transform != null ? gb.Transform.Value : Matrix.Identity;
                if (!gm.HasInverse) return false;
                if (gb.MappingMode == BrushMappingMode.RelativeToBoundingBox)
                {
                    Rect b = geometry.Bounds;
                    if (b.IsEmpty || !(b.Width > 0 && b.Height > 0)) return false;
                    gm = gm * new Matrix(b.Width, 0, 0, b.Height, b.X, b.Y);
                }
                string coords;
                int type;
                if (gb is LinearGradientBrush lgb)
                {
                    type = 2;
                    coords = N(lgb.StartPoint.X) + " " + N(lgb.StartPoint.Y) + " " + N(lgb.EndPoint.X) + " " + N(lgb.EndPoint.Y);
                }
                else if (gb is RadialGradientBrush rgb && Math.Abs(rgb.RadiusX - rgb.RadiusY) < 1e-4 && rgb.RadiusX > 0)
                {
                    type = 3;
                    coords = N(rgb.GradientOrigin.X) + " " + N(rgb.GradientOrigin.Y) + " 0 " + N(rgb.Center.X) + " " + N(rgb.Center.Y) + " " + N(rgb.RadiusX);
                }
                else return false;

                var raw = gb.GradientStops.OrderBy(s => s.Offset).Select(s => (Offset: Clamp01(s.Offset), Color: s.Color)).ToList();
                if (raw[0].Offset > 0) raw.Insert(0, (0, raw[0].Color));
                if (raw[raw.Count - 1].Offset < 1) raw.Add((1, raw[raw.Count - 1].Color));
                var stops = new List<(double Offset, Color Color)> { raw[0] };
                for (int i = 1; i < raw.Count; i++)
                {
                    double t = Math.Max(stops[stops.Count - 1].Offset + (i == raw.Count - 1 ? 0 : 1e-4), raw[i].Offset);
                    if (t > 1) t = 1;
                    if (t > stops[stops.Count - 1].Offset) stops.Add((t, raw[i].Color));
                    else stops[stops.Count - 1] = (stops[stops.Count - 1].Offset, raw[i].Color);
                }
                if (stops.Count < 2) stops.Add((1, stops[0].Color));

                string Rgb(Color c) => C(c.R / 255.0) + " " + C(c.G / 255.0) + " " + C(c.B / 255.0);
                string Grey(Color c) => C(c.A / 255.0);
                // the stops as a PDF function of the gradient's 0 to 1: their colours, or their opacities as greys
                string Function(Func<Color, string> value)
                {
                    string Seg(Color a, Color b) => "<< /FunctionType 2 /Domain [0 1] /C0 [" + value(a) + "] /C1 [" + value(b) + "] /N 1 >>";
                    if (stops.Count == 2) return Seg(stops[0].Color, stops[1].Color);
                    var funcs = new List<string>();
                    var bounds = new List<string>();
                    var encode = new List<string>();
                    for (int i = 0; i < stops.Count - 1; i++)
                    {
                        funcs.Add(Seg(stops[i].Color, stops[i + 1].Color));
                        if (i > 0) bounds.Add(C(stops[i].Offset));
                        encode.Add("0 1");
                    }
                    return "<< /FunctionType 3 /Domain [0 1] /Functions [" + string.Join(" ", funcs) + "] /Bounds [" + string.Join(" ", bounds) + "] /Encode [" + string.Join(" ", encode) + "] >>";
                }

                var path = new StringBuilder();
                int figures = 0;
                AppendGeometry(path, geometry, Matrix.Identity, ref figures);
                if (figures == 0) return true;

                string Shading(string colourSpace, string function)
                {
                    string name = "Sh" + (shadings.Count + 1).ToString(CultureInfo.InvariantCulture);
                    shadings.Add("/" + name + " << /ShadingType " + type + " /ColorSpace " + colourSpace + " /Coords [" + coords + "] /Extend [true true] /Function " + function + " >>");
                    return name;
                }
                string sh = Shading("/DeviceRGB", Function(Rgb));
                string place = gm.IsIdentity ? "" : N(gm.M11) + " " + N(gm.M12) + " " + N(gm.M21) + " " + N(gm.M22) + " " + N(gm.OffsetX) + " " + N(gm.OffsetY) + " cm\n";

                double alpha = opacity * Clamp01(gb.Opacity);
                content.Append("q\n");
                if (stops.Any(s => s.Color.A < 255))
                {
                    // Stops that can be seen through: their opacities as the same gradient in greys, a soft mask over
                    // the colours (a fade to clear is clear at its end, not half see-through all over)
                    int mask = AddForm(place + "/" + Shading("/DeviceGray", Function(Grey)) + " sh\n", geometry.Bounds, "/S /Transparency /CS /DeviceGray");
                    string gs = "GS" + (gsEntries.Count + 1).ToString(CultureInfo.InvariantCulture);
                    gsEntries.Add("/" + gs + " << /Type /ExtGState /ca " + C(alpha) + " /CA " + C(alpha) + " /SMask << /Type /Mask /S /Luminosity /G " + FormObject(mask) + " >> >>");
                    content.Append('/').Append(gs).Append(" gs\n");
                }
                else if (alpha < 0.9999)
                {
                    string gs = AlphaState(alpha, alpha);
                    content.Append('/').Append(gs).Append(" gs\n");
                }
                bool evenOdd = !forceNonzero && IsEvenOdd(geometry);
                content.Append(path).Append(evenOdd ? "W* n\n" : "W n\n");
                content.Append(place);
                content.Append('/').Append(sh).Append(" sh\nQ\n");
                return true;
            }

            void PaintGeometry(Geometry geometry, Brush fill, Pen pen, bool forceNonzero, double opacity)
            {
                if (geometry == null || geometry.IsEmpty() || opacity <= 0) return;
                bool hasPen = pen != null && pen.Brush != null && pen.Thickness > 0 && !double.IsInfinity(pen.Thickness);
                if (fill == null && !hasPen) return;
                if (fill is GradientBrush gb && !hasPen && PaintGradient(geometry, gb, forceNonzero, opacity)) return;
                if ((fill != null && !(fill is SolidColorBrush)) || (hasPen && !(pen.Brush is SolidColorBrush)))
                {
                    Rasterize(new GeometryDrawing(fill, pen, geometry), opacity);
                    return;
                }
                var fb = fill as SolidColorBrush;
                var sb = hasPen ? pen.Brush as SolidColorBrush : null;
                double fillAlpha = fb != null ? (fb.Color.A / 255.0) * Clamp01(fb.Opacity) * opacity : 0;
                double strokeAlpha = sb != null ? (sb.Color.A / 255.0) * Clamp01(sb.Opacity) * opacity : 0;
                bool doFill = fillAlpha > 0;
                bool doStroke = strokeAlpha > 0;
                if (!doFill && !doStroke) return;

                var path = new StringBuilder();
                int figures = 0;
                AppendGeometry(path, geometry, Matrix.Identity, ref figures);
                if (figures == 0) return;

                content.Append("q\n");
                if ((doFill && fillAlpha < 0.9999) || (doStroke && strokeAlpha < 0.9999))
                {
                    string gs = AlphaState(doFill ? fillAlpha : 1, doStroke ? strokeAlpha : 1);
                    content.Append('/').Append(gs).Append(" gs\n");
                }
                if (doFill)
                {
                    content.Append(C(fb.Color.R / 255.0)).Append(' ')
                           .Append(C(fb.Color.G / 255.0)).Append(' ')
                           .Append(C(fb.Color.B / 255.0)).Append(" rg\n");
                }
                if (doStroke)
                {
                    content.Append(C(sb.Color.R / 255.0)).Append(' ')
                           .Append(C(sb.Color.G / 255.0)).Append(' ')
                           .Append(C(sb.Color.B / 255.0)).Append(" RG\n");
                    content.Append(N(pen.Thickness)).Append(" w\n");
                    int cap = pen.StartLineCap == PenLineCap.Round ? 1 : pen.StartLineCap == PenLineCap.Square ? 2 : 0;
                    if (cap != 0) content.Append(cap).Append(" J\n");
                    int join = pen.LineJoin == PenLineJoin.Round ? 1 : pen.LineJoin == PenLineJoin.Bevel ? 2 : 0;
                    if (join != 0) content.Append(join).Append(" j\n");
                    else if (pen.MiterLimit > 0 && Math.Abs(pen.MiterLimit - 10) > 0.01)
                        content.Append(N(Math.Max(1, Math.Min(1e6, pen.MiterLimit)))).Append(" M\n");
                    if (pen.DashStyle != null && pen.DashStyle.Dashes != null && pen.DashStyle.Dashes.Count > 0)
                    {
                        content.Append('[');
                        for (int i = 0; i < pen.DashStyle.Dashes.Count; i++)
                        {
                            if (i > 0) content.Append(' ');
                            content.Append(N(Math.Max(0, pen.DashStyle.Dashes[i] * pen.Thickness)));
                        }
                        content.Append("] ").Append(N(pen.DashStyle.Offset * pen.Thickness)).Append(" d\n");
                    }
                }
                content.Append(path);
                bool evenOdd = !forceNonzero && IsEvenOdd(geometry);
                if (doFill && doStroke) content.Append(evenOdd ? "B*\n" : "B\n");
                else if (doFill) content.Append(evenOdd ? "f*\n" : "f\n");
                else content.Append("S\n");
                content.Append("Q\n");
            }

            // A group's children. A group with an opacity of its own has them drawn together, into a transparency
            // group that's then made see-through as one, as WPF does: shapes that overlap in it aren't darker where
            // they meet (each child had the group's opacity, so overlaps were). Without one, one by one.
            void Children(DrawingGroup group, double own, double opacity)
            {
                if (own >= 0.9999)
                {
                    foreach (Drawing child in group.Children) Walk(child, opacity);
                    return;
                }
                StringBuilder around = content;
                content = new StringBuilder();
                foreach (Drawing child in group.Children) Walk(child, 1);
                string inside = content.ToString();
                content = around;
                if (inside.Length == 0) return;
                Rect box = Rect.Empty;
                foreach (Drawing child in group.Children) box.Union(child.Bounds);
                int form = AddForm(inside, box, "/S /Transparency /I true");
                string gs = AlphaState(opacity * own, opacity * own);
                content.Append("q /").Append(gs).Append(" gs /").Append(FormName(form)).Append(" Do Q\n");
            }

            void Walk(Drawing d, double opacity)
            {
                if (d == null || opacity <= 0) return;
                if (d is DrawingGroup group)
                {
                    double own = Clamp01(group.Opacity);
                    if (opacity * own <= 0 || group.Children.Count == 0) return;
                    if (group.OpacityMask != null)
                    {
                        // drawn as a picture, which has the group's own opacity and mask in it already: only the
                        // opacity around the group goes on top (it had the group's own opacity twice)
                        Rasterize(group, opacity);
                        return;
                    }
                    bool hasTransform = group.Transform != null && !group.Transform.Value.IsIdentity;
                    if (hasTransform && !group.Transform.Value.HasInverse) return;
                    bool hasClip = group.ClipGeometry != null;
                    if (hasTransform || hasClip)
                    {
                        content.Append("q\n");
                        if (hasTransform)
                        {
                            Matrix m = group.Transform.Value;
                            content.Append(N(m.M11)).Append(' ').Append(N(m.M12)).Append(' ')
                                   .Append(N(m.M21)).Append(' ').Append(N(m.M22)).Append(' ')
                                   .Append(N(m.OffsetX)).Append(' ').Append(N(m.OffsetY)).Append(" cm\n");
                        }
                        if (hasClip)
                        {
                            var clipPath = new StringBuilder();
                            int figures = 0;
                            AppendGeometry(clipPath, group.ClipGeometry, Matrix.Identity, ref figures);
                            if (figures == 0)
                            {
                                content.Append("Q\n");
                                return;
                            }
                            content.Append(clipPath).Append(IsEvenOdd(group.ClipGeometry) ? "W* n\n" : "W n\n");
                        }
                        Children(group, own, opacity);
                        content.Append("Q\n");
                    }
                    else
                    {
                        Children(group, own, opacity);
                    }
                }
                else if (d is GlyphRunDrawing glyphs)
                {
                    if (glyphs.GlyphRun == null || glyphs.ForegroundBrush == null) return;
                    PaintGeometry(glyphs.GlyphRun.BuildGeometry(), glyphs.ForegroundBrush, null, true, opacity);
                }
                else if (d is GeometryDrawing gd)
                {
                    PaintGeometry(gd.Geometry, gd.Brush, gd.Pen, false, opacity);
                }
                else if (d is ImageDrawing img && img.ImageSource is BitmapSource bmp)
                {
                    DrawBitmap(bmp, img.Rect, opacity);
                }
            }

            if (canvas.Width > 0 && canvas.Height > 0 && place.Width > 0 && place.Height > 0)
            {
                double sx = place.Width / canvas.Width, sy = place.Height / canvas.Height;
                double tx = place.Left - canvas.X * sx, ty = pageHeight - place.Top + canvas.Y * sy;
                content.Append("q ").Append(N(sx)).Append(" 0 0 ").Append(N(-sy)).Append(' ')
                       .Append(N(tx)).Append(' ').Append(N(ty)).Append(" cm ")
                       .Append(N(canvas.X)).Append(' ').Append(N(canvas.Y)).Append(' ')
                       .Append(N(canvas.Width)).Append(' ').Append(N(canvas.Height)).Append(" re W n\n");
                Walk(drawing, 1);
                content.Append("Q\n");
            }

            var pdf = new MemoryStream();
            var offsets = new List<long>();
            void Text(string s) { byte[] b = Encoding.ASCII.GetBytes(s); pdf.Write(b, 0, b.Length); }
            void Start(int number) { offsets.Add(pdf.Position); Text(number + " 0 obj\n"); }

            Text("%PDF-1.4\n%");
            pdf.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3 }, 0, 4);   // bytes over 127 mark the file as binary for programs that look
            Text("\n");
            Start(1);
            Text("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            Start(2);
            Text("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

            // Objects: 1 Catalog, 2 Pages, 3 Page, 4 Contents, 5 Info, 6 Resources (the page's and every form's),
            // 7+ Images & SMasks, then forms
            int nextObj = 7;
            var imageObjects = new List<(EmbeddedImage Img, int Obj, int MaskObj)>();
            foreach (EmbeddedImage img in images)
            {
                int obj = nextObj++;
                int maskObj = img.Alpha != null ? nextObj++ : 0;
                imageObjects.Add((img, obj, maskObj));
            }
            var formObjects = new int[forms.Count];
            for (int i = 0; i < forms.Count; i++) formObjects[i] = nextObj++;
            string Numbered(string text) => Regex.Replace(text, "@F([0-9]+)@", m => formObjects[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] + " 0 R");

            var resources = new StringBuilder("<<");
            if (gsEntries.Count > 0)
                resources.Append(" /ExtGState << ").Append(Numbered(string.Join(" ", gsEntries))).Append(" >>");
            if (shadings.Count > 0)
                resources.Append(" /Shading << ").Append(string.Join(" ", shadings)).Append(" >>");
            if (imageObjects.Count > 0 || forms.Count > 0)
            {
                resources.Append(" /XObject <<");
                foreach (var (img, obj, _) in imageObjects)
                    resources.Append(" /").Append(img.Name).Append(' ').Append(obj).Append(" 0 R");
                for (int i = 0; i < forms.Count; i++)
                    resources.Append(" /").Append(FormName(i)).Append(' ').Append(formObjects[i]).Append(" 0 R");
                resources.Append(" >>");
            }
            resources.Append(" >>");

            Start(3);
            Text("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(pageWidth) + " " + N(pageHeight) + "] /Resources 6 0 R /Contents 4 0 R >>\nendobj\n");

            byte[] contentBytes = Encoding.ASCII.GetBytes(content.ToString());
            Start(4);
            Text("<< /Length " + contentBytes.Length + " >>\nstream\n");
            pdf.Write(contentBytes, 0, contentBytes.Length);
            Text("endstream\nendobj\n");

            Start(5);
            Text("<< /Title " + PdfString(title) + " /Producer " + PdfString(Program.Name + " " + Program.Version) + " >>\nendobj\n");

            Start(6);
            Text(resources + "\nendobj\n");

            foreach (var (img, obj, maskObj) in imageObjects)
            {
                Start(obj);
                Text("<< /Type /XObject /Subtype /Image /Width " + img.Width + " /Height " + img.Height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode" +
                    (maskObj > 0 ? " /SMask " + maskObj + " 0 R" : "") + " /Length " + img.Rgb.Length + " >>\nstream\n");
                pdf.Write(img.Rgb, 0, img.Rgb.Length);
                Text("\nendstream\nendobj\n");
                if (maskObj > 0)
                {
                    Start(maskObj);
                    Text("<< /Type /XObject /Subtype /Image /Width " + img.Width + " /Height " + img.Height + " /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length " + img.Alpha.Length + " >>\nstream\n");
                    pdf.Write(img.Alpha, 0, img.Alpha.Length);
                    Text("\nendstream\nendobj\n");
                }
            }

            for (int i = 0; i < forms.Count; i++)
            {
                byte[] formBytes = Encoding.ASCII.GetBytes(forms[i].Content);
                Start(formObjects[i]);
                Text("<< /Type /XObject /Subtype /Form /BBox [" + Box(forms[i].Box) + "] /Group << " + forms[i].Group + " >> /Resources 6 0 R /Length " + formBytes.Length + " >>\nstream\n");
                pdf.Write(formBytes, 0, formBytes.Length);
                Text("endstream\nendobj\n");
            }

            long xref = pdf.Position;
            Text("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
            foreach (long o in offsets) Text(o.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
            Text("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R /Info 5 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            return pdf.ToArray();
        }

        public static byte[] Make(BitmapSource picture, double pageWidth, double pageHeight, Rect place, string title) =>
            Make(new ImageDrawing(picture, new Rect(0, 0, picture.PixelWidth, picture.PixelHeight)),
                 new Rect(0, 0, picture.PixelWidth, picture.PixelHeight), pageWidth, pageHeight, place, title);

        static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(1, v));

        static bool IsEvenOdd(Geometry geometry)
        {
            if (geometry is StreamGeometry sg) return sg.FillRule == FillRule.EvenOdd;
            if (geometry is PathGeometry pg) return pg.FillRule == FillRule.EvenOdd;
            if (geometry is GeometryGroup gg) return gg.FillRule == FillRule.EvenOdd;
            return false;
        }

        static void AppendGeometry(StringBuilder sb, Geometry geometry, Matrix parentMatrix, ref int figures)
        {
            if (geometry == null || geometry.IsEmpty()) return;
            Matrix m = (geometry.Transform != null ? geometry.Transform.Value : Matrix.Identity) * parentMatrix;
            if (!m.HasInverse) return;
            if (geometry is RectangleGeometry rg && m.IsIdentity && rg.RadiusX <= 0 && rg.RadiusY <= 0)
            {
                Rect r = rg.Rect;
                if (r.IsEmpty || !(r.Width > 0 && r.Height > 0)) return;
                sb.Append(N(r.X)).Append(' ').Append(N(r.Y)).Append(' ').Append(N(r.Width)).Append(' ').Append(N(r.Height)).Append(" re\n");
                figures++;
                return;
            }
            if (geometry is LineGeometry lg && m.IsIdentity)
            {
                sb.Append(N(lg.StartPoint.X)).Append(' ').Append(N(lg.StartPoint.Y)).Append(" m ")
                  .Append(N(lg.EndPoint.X)).Append(' ').Append(N(lg.EndPoint.Y)).Append(" l\n");
                figures++;
                return;
            }
            if (geometry is GeometryGroup gg)
            {
                foreach (Geometry child in gg.Children) AppendGeometry(sb, child, m, ref figures);
                return;
            }
            // Transform is applied via `m` after converting arcs to Beziers in local coordinates
            Geometry untrimmed = geometry;
            if (geometry.Transform != null && !geometry.Transform.Value.IsIdentity)
            {
                untrimmed = geometry.Clone();
                untrimmed.Transform = Transform.Identity;
            }
            PathGeometry path = untrimmed as PathGeometry ?? PathGeometry.CreateFromGeometry(untrimmed);
            if (path == null) return;
            foreach (PathFigure fig in path.Figures)
            {
                Point cur = fig.StartPoint;
                Point start = m.Transform(cur);
                sb.Append(N(start.X)).Append(' ').Append(N(start.Y)).Append(" m\n");
                figures++;
                void Line(Point to)
                {
                    Point p = m.Transform(to);
                    sb.Append(N(p.X)).Append(' ').Append(N(p.Y)).Append(" l\n");
                    cur = to;
                }
                void Cubic(Point c1, Point c2, Point to)
                {
                    Point p1 = m.Transform(c1), p2 = m.Transform(c2), p = m.Transform(to);
                    sb.Append(N(p1.X)).Append(' ').Append(N(p1.Y)).Append(' ')
                      .Append(N(p2.X)).Append(' ').Append(N(p2.Y)).Append(' ')
                      .Append(N(p.X)).Append(' ').Append(N(p.Y)).Append(" c\n");
                    cur = to;
                }
                void Quad(Point q, Point to)
                {
                    var c1 = new Point(cur.X + 2.0 / 3 * (q.X - cur.X), cur.Y + 2.0 / 3 * (q.Y - cur.Y));
                    var c2 = new Point(to.X + 2.0 / 3 * (q.X - to.X), to.Y + 2.0 / 3 * (q.Y - to.Y));
                    Cubic(c1, c2, to);
                }
                foreach (PathSegment seg in fig.Segments)
                {
                    if (seg is LineSegment ls) Line(ls.Point);
                    else if (seg is PolyLineSegment pls)
                    {
                        foreach (Point p in pls.Points) Line(p);
                    }
                    else if (seg is BezierSegment bs) Cubic(bs.Point1, bs.Point2, bs.Point3);
                    else if (seg is PolyBezierSegment pbs)
                    {
                        for (int i = 0; i + 2 < pbs.Points.Count; i += 3)
                            Cubic(pbs.Points[i], pbs.Points[i + 1], pbs.Points[i + 2]);
                    }
                    else if (seg is QuadraticBezierSegment qbs) Quad(qbs.Point1, qbs.Point2);
                    else if (seg is PolyQuadraticBezierSegment pqbs)
                    {
                        for (int i = 0; i + 1 < pqbs.Points.Count; i += 2)
                            Quad(pqbs.Points[i], pqbs.Points[i + 1]);
                    }
                    else if (seg is ArcSegment arc)
                    {
                        ArcToBeziers(cur, arc.Size.Width, arc.Size.Height, arc.RotationAngle, arc.IsLargeArc,
                                     arc.SweepDirection == SweepDirection.Clockwise, arc.Point, Cubic, Line);
                    }
                }
                if (fig.IsClosed) sb.Append("h\n");
            }
        }

        // An elliptical arc to `to`, turned into up to four cubic Beziers (the same sums as Svg.Figures.Arc)
        static void ArcToBeziers(Point from, double rx, double ry, double degrees, bool large, bool sweep, Point to,
                                 Action<Point, Point, Point> cubic, Action<Point> line)
        {
            if (from == to) return;
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx == 0 || ry == 0)
            {
                line(to);
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
                cubic(p1 + k * Slope(t1), p2 - k * Slope(t2), p2);
                t1 = t2;
            }
        }

        static string N(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            double r = Math.Round(d, 4);
            return (r == 0 ? 0 : r).ToString("0.####", CultureInfo.InvariantCulture);
        }

        // a form's box: what's drawn in it and a little more (anti-aliasing), or very big when there's nothing to go by
        static string Box(Rect r)
        {
            if (r.IsEmpty || double.IsNaN(r.X) || double.IsNaN(r.Y) || double.IsInfinity(r.Width) || double.IsInfinity(r.Height))
                return "-100000 -100000 100000 100000";
            r.Inflate(2, 2);
            return N(r.Left) + " " + N(r.Top) + " " + N(r.Right) + " " + N(r.Bottom);
        }

        static string C(double d)
        {
            double r = Math.Round(Clamp01(d), 4);
            return (r == 0 ? 0 : r).ToString("0.####", CultureInfo.InvariantCulture);
        }

        // a PDF text string: plain ASCII as is, anything else as UTF-16 with its byte-order mark
        static string PdfString(string text)
        {
            bool ascii = true;
            foreach (char c in text) if (c < ' ' || c > '~') { ascii = false; break; }
            if (ascii) return "(" + text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")";
            var hex = new StringBuilder("<FEFF");
            foreach (char c in text) hex.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            return hex.Append('>').ToString();
        }

        // The first page of a PDF, drawn on white by Windows' PDF reader (for reading a file back); null when
        // Windows can't read it. When the page's canvas clip is in whole units (such as a barcode's modules),
        // it's drawn at whole pixels per unit near 300 DPI so every bar edge falls on a whole pixel.
        public static BitmapSource ReadPicture(byte[] pdf)
        {
            if (pdf == null || pdf.Length == 0) return null;
            int exactW = 0, exactH = 0;
            string head = Encoding.ASCII.GetString(pdf, 0, Math.Min(pdf.Length, 2048));
            Match media = Regex.Match(head, @"/MediaBox \[0 0 ([0-9.]+) ([0-9.]+)\]");
            Match clip = Regex.Match(head, @"cm 0 0 ([0-9]+) ([0-9]+) re W n");
            if (media.Success && clip.Success)
            {
                double pw = double.Parse(media.Groups[1].Value, CultureInfo.InvariantCulture);
                double ph = double.Parse(media.Groups[2].Value, CultureInfo.InvariantCulture);
                int cw = int.Parse(clip.Groups[1].Value, CultureInfo.InvariantCulture);
                int ch = int.Parse(clip.Groups[2].Value, CultureInfo.InvariantCulture);
                if (cw > 0 && ch > 0 && cw <= 500 && ch <= 500 && Math.Abs(pw * ch - ph * cw) < 0.2 * cw)
                {
                    int scale = Math.Max(1, Math.Min(4096 / Math.Max(cw, ch), (int)Math.Round(pw / PointsPerInch * 300 / cw)));
                    exactW = cw * scale;
                    exactH = ch * scale;
                }
            }
            return Scan.PdfPages.RenderFirstPage(pdf, 300, exactW, exactH);
        }

        // Flate as PDF wants it: zlib's two-byte header, the deflated bytes, then the Adler-32 sum
        static byte[] Zlib(byte[] data)
        {
            var output = new MemoryStream();
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true)) deflate.Write(data, 0, data.Length);
            uint a = 1, b = 0;
            for (int i = 0; i < data.Length; )
            {
                int end = Math.Min(data.Length, i + 5552);  // the most bytes before the sums can overflow
                for (; i < end; i++)
                {
                    a += data[i];
                    b += a;
                }
                a %= 65521;
                b %= 65521;
            }
            uint sum = (b << 16) | a;
            output.WriteByte((byte)(sum >> 24));
            output.WriteByte((byte)(sum >> 16));
            output.WriteByte((byte)(sum >> 8));
            output.WriteByte((byte)sum);
            return output.ToArray();
        }
    }
}
