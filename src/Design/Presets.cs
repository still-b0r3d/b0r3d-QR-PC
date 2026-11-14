using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using B0r3dQR.Qr;

namespace B0r3dQR.Design
{
    // The website's presets: Plain, Rounded, Dots, b0r3d Cup and Ruckus for the code, three for the
    // frame, and Randomize. A preset never touches the data.
    static class Presets
    {
        public static readonly string[] Names = { "Plain", "Rounded", "Dots", "b0r3d Cup", "Ruckus" };

        // The type names, in the order the website offers them.
        public static readonly string[] DotTypes = { "dots", "rounded", "classy", "classy-rounded", "square", "extra-rounded" };
        public static readonly string[] CornerSquareTypes = { "dot", "square", "rounded", "extra-rounded" };
        public static readonly string[] CornerDotTypes = { "dot", "square", "rounded" };

        // `design` with a preset's look: every value the website's preset sets, the frame off
        public static QrDesign Apply(string name, QrDesign design)
        {
            QrDesign d = design.Clone();
            d.Width = 200;
            d.Height = 200;
            d.Margin = 4;
            d.LogoSpace = 1;
            d.LogoSize = null;
            d.Version = 0;
            d.Background = "#ffffff";
            d.Frame = null;
            d.Logo = "";
            d.Level = QrLevel.Q;
            d.BorderRadius = 16;
            switch (name)
            {
                case "Rounded":
                    Look(d, "rounded", "#000000", "extra-rounded", "#000000", "dot", "#000000");
                    break;
                case "Dots":
                    Look(d, "dots", "#000000", "dot", "#000000", "dot", "#000000");
                    break;
                case "b0r3d Cup":
                    Look(d, "rounded", "#131313", "extra-rounded", "#131313", "dot", "#c000c0");
                    d.Level = QrLevel.H;
                    d.Logo = LogoUrl("b0r3d-cup.png");
                    break;
                case "Ruckus":
                    Look(d, "rounded", "#2b2b2b", "extra-rounded", "#2b2b2b", "dot", "#c0396b");
                    d.Level = QrLevel.H;
                    d.Logo = LogoUrl("rukus.png");
                    break;
                default:   // Plain
                    Look(d, "square", "#000000", "square", "#000000", "square", "#000000");
                    d.BorderRadius = 0;
                    break;
            }
            return d;
        }

        static void Look(QrDesign d, string dots, string dotsColour, string squares, string squaresColour, string centres, string centresColour)
        {
            d.DotsType = dots;
            d.DotsColor = dotsColour;
            d.CornersSquareType = squares;
            d.CornersSquareColor = squaresColour;
            d.CornersDotType = centres;
            d.CornersDotColor = centresColour;
        }

        // the preset's logo as the website's preset holds it: its PNG as a data: URL
        static readonly Dictionary<string, string> logos = new Dictionary<string, string>();

        public static string LogoUrl(string file)
        {
            lock (logos)
            {
                if (logos.TryGetValue(file, out string url)) return url;
                using (Stream png = Application.GetResourceStream(new Uri("/b0r3dQR;component/assets/presets/" + file, UriKind.Relative)).Stream)
                using (var bytes = new MemoryStream())
                {
                    png.CopyTo(bytes);
                    url = "data:image/png;base64," + Convert.ToBase64String(bytes.ToArray());
                }
                logos[file] = url;
                return url;
            }
        }

        // the preset a design's look matches (its shapes, colours, logo, background and corners;
        // a frame, the size or the error correction changed since don't count), or null
        public static string Matching(QrDesign d)
        {
            foreach (string name in Names)
            {
                QrDesign p = Apply(name, d);
                if (p.DotsType == d.DotsType && p.DotsColor == d.DotsColor && p.CornersSquareType == d.CornersSquareType && p.CornersSquareColor == d.CornersSquareColor &&
                    p.CornersDotType == d.CornersDotType && p.CornersDotColor == d.CornersDotColor && p.Logo == d.Logo && p.Background == d.Background &&
                    p.BorderRadius == d.BorderRadius)
                    return name;
            }
            return null;
        }

        // ---------- the frame ----------

        public static readonly string[] FrameNames = { "Default Frame", "Dark Frame", "Borderless Frame" };

        // a frame preset: its colours and lengths, keeping the caption and its place; the default font, no picture
        public static QrFrame ApplyFrame(string name, QrFrame current)
        {
            QrFrame f = current?.Clone() ?? new QrFrame();
            f.FontFamily = null;
            f.BackgroundImage = null;
            f.BorderWidth = "1px";
            f.BorderRadius = "8px";
            f.Padding = "16px";
            switch (name)
            {
                case "Dark Frame":
                    f.TextColor = "#ffffff";
                    f.BackgroundColor = "#000000";
                    f.BorderColor = "#ffffff";
                    break;
                case "Borderless Frame":
                    f.TextColor = "#000000";
                    f.BackgroundColor = "#ffffff";
                    f.BorderColor = "#ffffff";
                    f.BorderWidth = "0px";
                    f.BorderRadius = "0px";
                    break;
                default:
                    f.TextColor = "#000000";
                    f.BackgroundColor = "#ffffff";
                    f.BorderColor = "#000000";
                    break;
            }
            return f;
        }

        // ---------- Randomize ----------

        // random shapes and colours, as the website's Randomize (its colours always six digits);
        // the logo, quiet zone, error correction, size and frame stay
        public static QrDesign Randomize(QrDesign design, Random random)
        {
            QrDesign d = design.Clone();
            d.DotsType = DotTypes[random.Next(DotTypes.Length)];
            d.CornersSquareType = CornerSquareTypes[random.Next(CornerSquareTypes.Length)];
            d.CornersDotType = CornerDotTypes[random.Next(CornerDotTypes.Length)];
            string Colour() => "#" + random.Next(0x1000000).ToString("x6");
            d.DotsColor = Colour();
            d.CornersSquareColor = Colour();
            d.CornersDotColor = Colour();
            d.Background = Colour();
            return d;
        }
    }
}
