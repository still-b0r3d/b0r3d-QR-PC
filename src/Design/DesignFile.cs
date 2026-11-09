using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using B0r3dQR.Qr;

namespace B0r3dQR.Design
{
    // A design file the website refuses, and why, in the website's own words.
    sealed class DesignFileException : Exception
    {
        public DesignFileException(string message) : base(message) { }
    }

    // qr.b0r3d.org's design files (qr-code-config.json), both ways: what the website writes, in its
    // order and with every key it reads (it stops part-way through a file without them), and what
    // it reads, refusing what it refuses. Where the website is fragile, this is forgiving: a missing
    // part takes the website's default.
    static class DesignFile
    {
        public const string FileName = "qr-code-config.json";
        public const int Schema = 1;
        public const string Newer = "This file was saved by a newer version of b0r3d QR, so it can’t be opened here.";
        public const string NotADesign = "That file isn’t a b0r3d QR configuration.";

        // ---------- writing ----------

        // `withData` false leaves the text out (the design b0r3d QR keeps between runs)
        public static string Write(QrDesign d, bool withData = true)
        {
            var file = new Dictionary<string, object> { ["schemaVersion"] = Schema };
            if (d.Utf8Marker) file["useEci26"] = true;
            var props = new Dictionary<string, object>();
            if (withData) props["data"] = d.Data ?? "";
            props["image"] = d.Logo ?? "";
            props["width"] = d.Width;
            props["height"] = d.Height;
            props["margin"] = d.Margin;
            props["dotsOptions"] = Part(d.DotsColor, d.DotsType);
            props["cornersSquareOptions"] = Part(d.CornersSquareColor, d.CornersSquareType);
            props["cornersDotOptions"] = Part(d.CornersDotColor, d.CornersDotType);
            var image = new Dictionary<string, object> { ["padding"] = d.LogoSpace };
            if (d.LogoSize.HasValue) image["imageSize"] = d.LogoSize.Value;
            props["imageOptions"] = image;
            props["qrOptions"] = new Dictionary<string, object> { ["errorCorrectionLevel"] = d.Level.ToString(), ["typeNumber"] = d.Version };
            file["props"] = props;
            file["style"] = new Dictionary<string, object> { ["borderRadius"] = Json.Number(d.BorderRadius) + "px", ["background"] = d.Background ?? "#ffffff" };
            file["frame"] = d.Frame == null ? null : Frame(d.Frame);
            return Json.Write(file) + "\n";
        }

        static Dictionary<string, object> Part(string colour, string type) => new Dictionary<string, object> { ["color"] = colour ?? "#000000", ["type"] = type ?? "square" };

        // an emptied length field is written as what it draws, since the website refuses an empty one
        static Dictionary<string, object> Frame(QrFrame f)
        {
            string Length(string v, string drawn) => string.IsNullOrEmpty(v) ? drawn : v;
            var style = new Dictionary<string, object>
            {
                ["textColor"] = f.TextColor,
                ["backgroundColor"] = f.BackgroundColor,
                ["borderColor"] = f.BorderColor,
                ["borderWidth"] = Length(f.BorderWidth, "2px"),
                ["borderRadius"] = Length(f.BorderRadius, "8px"),
                ["padding"] = Length(f.Padding, "12px"),
            };
            if (!string.IsNullOrEmpty(f.FontFamily)) style["fontFamily"] = f.FontFamily;
            if (!string.IsNullOrEmpty(f.BackgroundImage)) style["backgroundImage"] = f.BackgroundImage;
            return new Dictionary<string, object> { ["text"] = f.Text ?? "", ["position"] = f.Position, ["style"] = style, ["captionWidth"] = f.CaptionWidth };
        }

        // ---------- reading ----------

        // The design in a file, on top of `current` (whose data stays when the file has none). Throws
        // DesignFileException with the website's message for a file it would refuse.
        public static QrDesign Read(string text, QrDesign current)
        {
            object root;
            try
            {
                root = Json.Parse(text);
            }
            catch (JsonException)
            {
                throw new DesignFileException(NotADesign);
            }
            if (!(root is Dictionary<string, object> file)) throw new DesignFileException(NotADesign);
            if (file.TryGetValue("schemaVersion", out object schema) && schema != null)
            {
                if (!(schema is double version)) throw new DesignFileException(NotADesign);
                if (version > Schema) throw new DesignFileException(Newer);
            }
            if (file.TryGetValue("useEci26", out object eci) && eci != null && !(eci is bool)) throw new DesignFileException(NotADesign);
            Dictionary<string, object> props = Json.Obj(file, "props"), style = Json.Obj(file, "style");
            if (props == null || style == null || !(Json.Has(style, "borderRadius") && style["borderRadius"] is string)) throw new DesignFileException(NotADesign);
            string logo = props.TryGetValue("image", out object image) && image != null ? image as string : "";
            if (logo == null || !SafeLogo(logo)) throw new DesignFileException(NotADesign);
            QrFrame frame = null;
            if (file.TryGetValue("frame", out object f) && f != null)
            {
                frame = ReadFrame(f as Dictionary<string, object>);
                if (frame == null) throw new DesignFileException(NotADesign);
            }

            QrDesign d = (current ?? new QrDesign()).Clone();
            string data = Json.Str(props, "data");
            if (!string.IsNullOrEmpty(data)) d.Data = data;
            d.Logo = logo;
            d.Width = Size(Json.Num(props, "width"));
            d.Height = Size(Json.Num(props, "height"));
            // an emptied number field is saved as "", which the website's sums take as 0
            double? margin = JsNumber(props, "margin");
            d.Margin = margin == null ? 4 : Finite(margin.Value) ? Math.Max(0, Math.Min(40, margin.Value)) : 0;
            ReadPart(Json.Obj(props, "dotsOptions"), "square", out d.DotsColor, out d.DotsType);
            ReadPart(Json.Obj(props, "cornersSquareOptions"), "extra-rounded", out d.CornersSquareColor, out d.CornersSquareType);
            ReadPart(Json.Obj(props, "cornersDotOptions"), "square", out d.CornersDotColor, out d.CornersDotType);
            Dictionary<string, object> logoOptions = Json.Obj(props, "imageOptions");
            double? space = JsNumber(logoOptions, "padding");
            d.LogoSpace = space == null ? 1 : Finite(space.Value) ? Math.Max(0, space.Value) : 0;
            double? size = JsNumber(logoOptions, "imageSize");
            d.LogoSize = size == null || double.IsNaN(size.Value) ? (double?)null : Math.Max(0, Math.Min(1, size.Value));
            Dictionary<string, object> qr = Json.Obj(props, "qrOptions");
            d.Level = Enum.TryParse(Json.Str(qr, "errorCorrectionLevel", ""), false, out QrLevel level) && Enum.IsDefined(typeof(QrLevel), level) ? level : QrLevel.Q;
            double? type = Json.Num(qr, "typeNumber");
            d.Version = type.HasValue && type.Value == Math.Floor(type.Value) && type.Value >= 0 && type.Value <= 40 ? (int)type.Value : 0;
            d.BorderRadius = Pixels((string)style["borderRadius"]);
            d.Background = Json.Str(style, "background", "#ffffff");
            d.Utf8Marker = eci is bool on && on;
            d.Frame = frame;
            return d;
        }

        static double Size(double? value) => value.HasValue && value.Value > 0 && !double.IsNaN(value.Value) ? Math.Min(value.Value, 100000) : 200;

        static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        // What JavaScript's Number() makes of a value, as the website's sums take it: a number as
        // itself, "" or spaces as 0, a number in a string as that number, true and false as 1 and 0,
        // anything else NaN. Null when the key is missing or null (the website's default applies).
        static double? JsNumber(Dictionary<string, object> map, string key)
        {
            if (map == null || !map.TryGetValue(key, out object v) || v == null) return null;
            switch (v)
            {
                case double n: return n;
                case bool b: return b ? 1 : 0;
                case string s:
                    s = s.Trim();
                    if (s.Length == 0) return 0;
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : double.NaN;
                default: return double.NaN;
            }
        }

        static void ReadPart(Dictionary<string, object> part, string fallbackType, out string colour, out string type)
        {
            colour = Json.Str(part, "color", "#000000");
            type = Json.Str(part, "type", fallbackType);
        }

        // "16px" as 16; "" or "px" as 0
        static double Pixels(string css)
        {
            string v = (css ?? "").Trim();
            if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 2);
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double px) && !double.IsNaN(px) ? Math.Max(0, px) : 0;
        }

        // A logo the website takes: none, a picture as a data: URL, a web address, or a path on the
        // website (no scheme). Any other scheme (javascript:, file:, data:text/...) refuses the file.
        public static bool SafeLogo(string logo)
        {
            // read the way a browser reads an address: tabs and line breaks anywhere, and control
            // characters and spaces at either end, don't count (so "\u0001javascript:" is still javascript:)
            var s = new System.Text.StringBuilder((logo ?? "").Length);
            foreach (char c in logo ?? "")
                if (c != '\t' && c != '\n' && c != '\r') s.Append(c);
            int start = 0, end = s.Length;
            while (start < end && s[start] <= 0x20) start++;
            while (end > start && s[end - 1] <= 0x20) end--;
            string v = s.ToString(start, end - start);
            if (v.Length == 0) return true;
            if (v.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return true;
            if (Regex.IsMatch(v, "^https?://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
            return !Regex.IsMatch(v, @"^[A-Za-z][A-Za-z0-9+.\-]*:");
        }

        static readonly string[] Positions = { "top", "bottom", "left", "right" };

        // a frame the website takes, or null
        static QrFrame ReadFrame(Dictionary<string, object> f)
        {
            if (f == null) return null;
            if (!(Json.Has(f, "text") && f["text"] is string text)) return null;
            string position = Json.Str(f, "position");
            if (!Positions.Contains(position)) return null;
            Dictionary<string, object> style = Json.Obj(f, "style");
            if (style == null) return null;
            string textColor = Json.Str(style, "textColor"), background = Json.Str(style, "backgroundColor"), border = Json.Str(style, "borderColor");
            if (Css.Colour(textColor) == null || Css.Colour(background) == null || Css.Colour(border) == null) return null;
            // the website refuses an empty length; it's taken here, and draws its default
            string width = Json.Str(style, "borderWidth"), radius = Json.Str(style, "borderRadius"), padding = Json.Str(style, "padding");
            bool Length(string v) => v != null && (v.Length == 0 || Css.IsLength(v));
            if (!Length(width) || !Length(radius) || !Length(padding)) return null;
            if (Json.Has(style, "fontFamily") && !(style["fontFamily"] is string)) return null;
            string picture = Json.Str(style, "backgroundImage");
            if (Json.Has(style, "backgroundImage") && (picture == null ||
                !(picture.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(picture, "^https?://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))) return null;
            // the website rounds the frame's width (caption + 200) and keeps it to 250-800
            double? caption = Json.Num(f, "captionWidth");
            double frameWidth = caption.HasValue && !double.IsNaN(caption.Value) ? Math.Round(caption.Value + 200, MidpointRounding.AwayFromZero) : 400;
            int captionWidth = (int)Math.Max(250, Math.Min(800, frameWidth)) - 200;
            return new QrFrame
            {
                Text = text.Length == 0 ? QrFrame.DefaultText : text,
                Position = position,
                TextColor = textColor,
                BackgroundColor = background,
                BorderColor = border,
                BorderWidth = width,
                BorderRadius = radius,
                Padding = padding,
                FontFamily = Json.Str(style, "fontFamily"),
                BackgroundImage = picture,
                CaptionWidth = captionWidth,
            };
        }
    }
}
