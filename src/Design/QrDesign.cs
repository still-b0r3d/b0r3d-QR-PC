using System.Text.RegularExpressions;
using B0r3dQR.Qr;

namespace B0r3dQR.Design
{
    // A code's design, as qr.b0r3d.org's design files hold it: the data, the size and quiet zone,
    // the shapes and colours of the dots and the corners, the background, a logo, the error
    // correction and the size of the code, and a frame with a caption. Values are kept the way the
    // file has them (a dot type the website doesn't know stays as it is, and is drawn the way the
    // website draws it), so opening and saving a design changes nothing that wasn't changed.
    sealed class QrDesign
    {
        public const double DefaultLogoSize = 0.4;

        public string Data = "";
        public string Logo = "";                 // "" none, a data: URL, a web address, or a path on the website
        public double Width = 200, Height = 200; // the export's size in px; the code is square, the smaller of the two
        public double Margin = 4;                // the quiet zone, in modules
        public string DotsType = "square", DotsColor = "#000000";
        public string CornersSquareType = "square", CornersSquareColor = "#000000";
        public string CornersDotType = "square", CornersDotColor = "#000000";
        public double LogoSpace = 1;             // modules between the logo and the dots, taken out of the logo
        public double? LogoSize;                 // the logo's share of what error correction allows; null: 0.4
        public QrLevel Level = QrLevel.Q;
        public int Version;                      // 1-40 at least, or 0 for the smallest that fits
        public double BorderRadius;              // rounded corners for the whole code, in px at its size
        public string Background = "#ffffff";    // a colour, or "transparent"
        public bool Utf8Marker;                  // starts the code with ECI 26
        public QrFrame Frame;                    // null: no frame

        public bool HasLogo => !string.IsNullOrWhiteSpace(Logo);
        public bool Transparent => Css.IsTransparent(Background);

        // A logo the website draws without going online (a data: URL, or a path on the website):
        // the code clears room for it and raises L or M to Q, even where b0r3d QR can't draw the
        // picture itself (an SVG). One on the web only counts once the website has fetched it, so
        // here, where nothing is ever fetched, it counts as none, as on the website when it can't.
        public bool LogoCounts => HasLogo && !Regex.IsMatch(Logo.Trim(), "^https?://", RegexOptions.IgnoreCase);

        // the level the code is made at: a logo needs at least Q (as on the website)
        public QrLevel LevelUsed => LogoCounts && (Level == QrLevel.L || Level == QrLevel.M) ? QrLevel.Q : Level;

        public QrDesign Clone()
        {
            var copy = (QrDesign)MemberwiseClone();
            copy.Frame = Frame?.Clone();
            return copy;
        }
    }

    // A frame around the code with a caption, as the website's design files hold it. The lengths
    // are CSS strings ("16px"), the way the website's fields take them.
    sealed class QrFrame
    {
        public const string DefaultText = "Scan for more info";

        public string Text = DefaultText;
        public string Position = "bottom";       // top, bottom, left or right
        public string TextColor = "#000000", BackgroundColor = "#ffffff", BorderColor = "#000000";
        public string BorderWidth = "1px", BorderRadius = "8px", Padding = "16px";
        public string FontFamily;                // a CSS family list, or null for the default
        public string BackgroundImage;           // a data: URL (or a web address in a website file), or null
        public int CaptionWidth = 200;           // the caption's column beside the code, left or right

        public bool Beside => Position == "left" || Position == "right";

        public QrFrame Clone() => (QrFrame)MemberwiseClone();
    }
}
