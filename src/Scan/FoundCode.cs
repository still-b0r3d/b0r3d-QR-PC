using System.Collections.Generic;
using System.Windows.Media.Imaging;
using B0r3dQR.Qr;

namespace B0r3dQR.Scan
{
    // One code a scan found, as the Scan tab shows it; or, in a batch of pictures, one that had
    // no code or couldn't be read, so the list names every picture that needs a look.
    public sealed class FoundCode
    {
        public string Text { get; internal set; }
        public string Kind { get; internal set; }        // "Link", "Wi-Fi network", "Product number"...
        public string Detail { get; internal set; }      // "b0r3d.org", "network home, WPA": never a password
        public string Format { get; internal set; }      // "QR code", "EAN-13"...
        public string Source { get; internal set; }      // "screen 2", "photo.jpg"
        public BitmapSource Thumbnail { get; internal set; }   // where it was found
        public bool IsWebLink { get; internal set; }     // an http or https link: it can be opened
        public bool CanMakeAgain { get; internal set; }  // a plain QR code: the Make tab can make it again
        public bool IsProblem { get; internal set; }     // a picture with no code, or one that couldn't be read

        public bool HasThumbnail => Thumbnail != null;
        public string ThumbnailTip => IsProblem ? "The picture" : "Where the code was found";
        public bool HasText => !string.IsNullOrEmpty(Text);
        public bool IsCode => !IsProblem;

        // "QR code · b0r3d.org · screen 2"
        public string Summary
        {
            get
            {
                var parts = new List<string>();
                foreach (string part in new[] { Format, Detail, Source })
                    if (!string.IsNullOrEmpty(part)) parts.Add(part);
                return string.Join(" · ", parts);
            }
        }

        // what a screen reader calls it: "Link, b0r3d.org" (no password, as in the detail line)
        public string Label => string.IsNullOrEmpty(Detail) ? Kind : Kind + ", " + Detail;

        public override string ToString() => string.IsNullOrEmpty(Source) ? Label : Label + ", " + Source;

        internal QrLevel? Level;   // a QR code's error correction, to make it again the same
        internal double X, Y;      // where it was in its picture, for putting them in reading order
        internal System.Windows.Int32Rect Box;   // the part of the picture it takes up, with some room

        // a picture in a batch that gave no code: "No code found", "Couldn't open"...
        internal static FoundCode Problem(string kind, string detail, string source, BitmapSource thumbnail = null)
        {
            return new FoundCode { Kind = kind, Detail = detail, Source = source, Format = "", Text = "", Thumbnail = thumbnail, IsProblem = true };
        }
    }
}
