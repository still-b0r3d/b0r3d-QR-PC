using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR.Design
{
    // Pictures in designs (the logo, a frame's background), which files keep as data: URLs. One
    // that's a web address or a path on the website can't be drawn: b0r3d QR never goes online.
    static class Images
    {
        sealed class Kept
        {
            public string Value, Problem;
            public BitmapSource Picture;
        }

        // the last few pictures read (a design's logo and its frame's picture among them), so a
        // change elsewhere in the design doesn't read them again
        static readonly object gate = new object();
        static readonly List<Kept> kept = new List<Kept>();
        const int Keep = 4;

        // The picture in a design's value, or null with `problem` saying why (null problem: no picture).
        public static BitmapSource Load(string value, out string problem)
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

        static BitmapSource Decode(string v, out string problem)
        {
            problem = null;
            if (Regex.IsMatch(v, "^https?://", RegexOptions.IgnoreCase))
            {
                string host = Uri.TryCreate(v, UriKind.Absolute, out Uri uri) ? uri.Host : "a web address";
                problem = "This picture is on the web (" + host + "), and b0r3d QR never goes online: save it, then choose the file.";
                return null;
            }
            if (!v.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                problem = "This picture is a file on the website (" + Short(v) + "), which b0r3d QR can't reach: choose the picture itself.";
                return null;
            }
            int comma = v.IndexOf(',');
            if (comma < 0)
            {
                problem = "This picture's data is broken.";
                return null;
            }
            string header = v.Substring(5, comma - 5).ToLowerInvariant();
            if (header.StartsWith("image/svg", StringComparison.Ordinal))
            {
                problem = "This picture is an SVG drawing: it goes into SVG files, but b0r3d QR can't draw it into pictures yet. For PNG, JPG and PDF files, use a PNG or JPEG of it.";
                return null;
            }
            byte[] bytes;
            try
            {
                bytes = header.EndsWith(";base64", StringComparison.Ordinal)
                    ? Convert.FromBase64String(v.Substring(comma + 1))
                    : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(v.Substring(comma + 1)));
            }
            catch (FormatException)
            {
                problem = "This picture's data is broken.";
                return null;
            }
            BitmapSource picture = FromBytes(bytes);
            if (picture == null) problem = "Windows can't read this picture.";
            return picture;
        }

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

        static string Short(string text) => text.Length <= 40 ? text : text.Substring(0, 37) + "...";
    }
}
