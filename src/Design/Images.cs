using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Svg;

namespace B0r3dQR.Design
{
    // Pictures in designs (the logo, a frame's background), which files keep as data: URLs: PNG,
    // JPEG and GIF pictures, and SVG drawings, drawn as the website's browser draws them. One that's
    // a web address or a path on the website can't be drawn: b0r3d QR never goes online.
    static class Images
    {
        sealed class Kept
        {
            public string Value, Problem;
            public ImageSource Picture;
        }

        // the last few pictures read (a design's logo and its frame's picture among them), so a
        // change elsewhere in the design doesn't read them again
        static readonly object gate = new object();
        static readonly List<Kept> kept = new List<Kept>();
        const int Keep = 4;

        // the SVG drawing behind each DrawingImage that Load gives
        static readonly ConditionalWeakTable<ImageSource, SvgImage> drawings = new ConditionalWeakTable<ImageSource, SvgImage>();

        // The picture in a design's value, or null with `problem` saying why (null problem: no picture):
        // a BitmapSource, or for an SVG drawing a DrawingImage of it at its own size.
        public static ImageSource Load(string value, out string problem)
        {
            problem = null;
            string v = (value ?? "").Trim();
            if (v.Length == 0) return null;
            lock (gate)
            {
                Kept found = kept.Find(k => ReferenceEquals(k.Value, v)) ?? kept.Find(k => k.Value == v);
                if (found == null)
                {
                    found = new Kept { Value = v, Picture = Decode(v, out string why), Problem = why };
                    if (kept.Count == Keep) kept.RemoveAt(Keep - 1);
                }
                else kept.Remove(found);
                kept.Insert(0, found);
                problem = found.Problem;
                return found.Picture;
            }
        }

        static ImageSource Decode(string v, out string problem)
        {
            problem = null;
            if (Regex.IsMatch(v, "^https?://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                // (what a note quotes from the design file goes through Xml.Quote: cut short, and kept to one line)
                string host = Uri.TryCreate(v, UriKind.Absolute, out Uri uri) ? Xml.Quote(uri.Host) : "a web address";
                problem = "This picture is on the web (" + host + "), and b0r3d QR never goes online: save it, then choose the file.";
                return null;
            }
            if (!v.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                problem = "This picture is a file on the website (" + Xml.Quote(v) + "), which b0r3d QR can't reach: choose the picture itself.";
                return null;
            }
            // the data as a browser reads a data: URL (base64 with spaces or without its padding, or %-escaped)
            if (!SvgImage.DataUrl(v, out byte[] bytes, out string type, out string charset))
            {
                problem = "This picture's data is broken.";
                return null;
            }
            if (type == "image/svg+xml")
            {
                ImageSource drawing = Drawing(bytes, charset, out string why);
                if (drawing == null) problem = "b0r3d QR can't draw this SVG picture: " + why + ".";
                return drawing;
            }
            BitmapSource picture = FromBytes(bytes);
            if (picture == null) problem = "Windows can't read this picture.";
            return picture;
        }

        // An SVG drawing, read and drawn once to be sure it can be, or null with why not (`why`, as
        // a few words: "the file is empty").
        public static ImageSource Drawing(byte[] bytes, string charset, out string why)
        {
            why = null;
            try
            {
                SvgImage svg = SvgImage.Read(bytes, charset);
                Size size = svg.NaturalSize;
                if (!(size.Width > 0 && size.Height > 0) || size.Width > 1e6 || size.Height > 1e6) size = new Size(300, 150);
                var picture = new DrawingImage(svg.Render(size));
                picture.Freeze();
                drawings.Add(picture, svg);
                return picture;
            }
            catch (SvgException x)
            {
                why = x.Message;
                return null;
            }
            catch (Exception x)
            {
                why = Unexpected(x);
                return null;
            }
        }

        // The tests set this: a drawing that stops with anything but a refusal (SvgException) is a FAIL there, where
        // the program refuses the picture in plain words.
        internal static Action<Exception> OnUnexpected;

        // Whatever else stops an SVG drawing (a value WPF won't take, a mistake of b0r3d QR's) is a refusal too, in a
        // line, never the program's "Something went wrong" (a design file, template or Recent code holding the picture
        // would show that on every change): as a batch's row is (BatchWindow.MakeOne).
        static string Unexpected(Exception x)
        {
            OnUnexpected?.Invoke(x);
            if (x is OutOfMemoryException) return "there isn't enough memory to draw it";
            string message = (x.Message ?? "").Split('\r', '\n')[0].Trim();
            return "something in it stopped the drawing" + (message.Length > 0 ? " (" + Xml.Quote(message) + ")" : "");
        }

        // A picture in its box, as the website's <image> puts it there: a bitmap fitted inside it,
        // centred (xMidYMid meet, the logo), or covering it, centred and cropped (xMidYMid slice, a
        // frame's picture; the caller clips it). An SVG drawing takes the box as its viewport either
        // way, as Chromium draws one (see SvgImage). Drawn as vectors, so prints stay sharp.
        public static void Draw(DrawingContext dc, ImageSource picture, Rect box, bool slice)
        {
            if (picture == null || !(box.Width > 0 && box.Height > 0)) return;
            if (drawings.TryGetValue(picture, out SvgImage svg))
            {
                // drawn once already, at its own size (Drawing), so it should draw at any; one that doesn't is left
                // out, as a refused picture is (and `dc` is as it was: SvgImage.Draw)
                try
                {
                    svg.Draw(dc, box);
                }
                catch (Exception x)
                {
                    Unexpected(x);
                }
                return;
            }
            if (!(picture is BitmapSource bitmap) || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return;
            double w = bitmap.PixelWidth, h = bitmap.PixelHeight;
            double scale = slice ? Math.Max(box.Width / w, box.Height / h) : Math.Min(box.Width / w, box.Height / h);
            dc.DrawImage(bitmap, new Rect(box.X + (box.Width - w * scale) / 2, box.Y + (box.Height - h * scale) / 2, w * scale, h * scale));
        }

        // the SVG drawing behind a picture Load gave, or null for a bitmap
        public static SvgImage SvgOf(ImageSource picture) => picture != null && drawings.TryGetValue(picture, out SvgImage svg) ? svg : null;

        public static BitmapSource FromBytes(byte[] bytes)
        {
            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                BitmapSource frame = Upright(decoder.Frames[0]);
                frame.Freeze();
                return frame;
            }
            catch (Exception e) when (e is NotSupportedException || e is FileFormatException || e is ArgumentException || e is IOException ||
                                      e is InvalidOperationException || e is System.Runtime.InteropServices.ExternalException || e is OverflowException)
            {
                return null;
            }
        }

        // A photo the right way up. Phones save pictures sideways with a note of how to turn them
        // (EXIF's orientation), which browsers follow, so the website shows them upright.
        public static BitmapSource Upright(BitmapFrame frame)
        {
            int turn = 1;
            try
            {
                if (frame.Metadata is BitmapMetadata m && m.ContainsQuery("System.Photo.Orientation") && m.GetQuery("System.Photo.Orientation") is ushort o) turn = o;
            }
            catch (Exception e) when (e is NotSupportedException || e is InvalidOperationException || e is ArgumentException || e is System.Runtime.InteropServices.ExternalException)
            {
                // no metadata to read: it's the right way up
            }
            Transform Then(Transform first, Transform second) => new TransformGroup { Children = { first, second } };
            Transform t;
            switch (turn)
            {
                case 2: t = new ScaleTransform(-1, 1); break;
                case 3: t = new RotateTransform(180); break;
                case 4: t = new ScaleTransform(1, -1); break;
                case 5: t = Then(new RotateTransform(90), new ScaleTransform(-1, 1)); break;
                case 6: t = new RotateTransform(90); break;
                case 7: t = Then(new RotateTransform(270), new ScaleTransform(-1, 1)); break;
                case 8: t = new RotateTransform(270); break;
                default: return frame;
            }
            return new TransformedBitmap(frame, t);
        }
    }
}
