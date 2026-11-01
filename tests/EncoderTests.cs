using System;
using System.Collections.Generic;
using System.Linq;
using B0r3dQR.Qr;

namespace B0r3dQR.Tests
{
    // The encoder against codes the website made itself (WebsiteMatrices.cs).
    static partial class TestMain
    {
        // the same version, the same parts and every module the same, for each of the website's codes
        static void SameModulesAsTheWebsite()
        {
            var masks = new HashSet<int>();
            foreach (var w in WebsiteMatrices)
            {
                var level = (QrLevel)Enum.Parse(typeof(QrLevel), w.Level);
                string label = "\"" + Short(w.Text.Replace("\n", "\\n").Replace("\r", "\\r")) + "\" at " + w.Level + (w.MinVersion > 0 ? " from version " + w.MinVersion : "");
                QrCode code = QrCode.Make(w.Text, level, w.MinVersion);
                Check(code.Version == w.Version, label + ": version " + code.Version + ", the website's " + w.Version);
                string parts = string.Join(" ", code.Segments.Select(s => s.Mode.ToString().Substring(0, 1) + s.Text.Length));
                Check(parts == w.Segments, label + ": parts " + parts + ", the website's " + w.Segments);
                byte[] packed = Convert.FromBase64String(w.Modules);
                int differ = 0;
                for (int row = 0, bit = 0; row < code.Size; row++)
                    for (int column = 0; column < code.Size; column++, bit++)
                        if (((packed[bit >> 3] >> (7 - (bit & 7))) & 1) == 1 != code[row, column]) differ++;
                Check(differ == 0, label + ": " + differ + " modules differ from the website's (b0r3d QR chose mask " + code.Mask + ")");
                masks.Add(code.Mask);
            }
            Check(masks.Count == 8, "the website's codes only try masks " + string.Join(", ", masks.OrderBy(m => m)) + ": add texts until all eight are covered");
        }

        // with the UTF-8 marker: the same version, mask and modules as zint made for the website
        // (WebsiteMarkedMatrices.cs)
        static void SameMarkedModulesAsTheWebsite()
        {
            var masks = new HashSet<int>();
            foreach (var w in WebsiteMarkedMatrices)
            {
                var level = (QrLevel)Enum.Parse(typeof(QrLevel), w.Level);
                string label = "marked \"" + Short(w.Text.Replace("\n", "\\n")) + "\" at " + w.Level + (w.MinVersion > 0 ? " in version " + w.MinVersion : "");
                QrCode code = QrCode.Make(w.Text, level, w.MinVersion, true);
                Check(code.Version == w.Version, label + ": version " + code.Version + ", the website's " + w.Version);
                if (code.Version != w.Version) continue;
                Check(code.Mask == w.Mask, label + ": mask " + code.Mask + ", the website's " + w.Mask);
                byte[] packed = Convert.FromBase64String(w.Modules);
                int differ = 0;
                for (int row = 0, bit = 0; row < code.Size; row++)
                    for (int column = 0; column < code.Size; column++, bit++)
                        if (((packed[bit >> 3] >> (7 - (bit & 7))) & 1) == 1 != code[row, column]) differ++;
                Check(differ == 0, label + ": " + differ + " modules differ from the website's (parts " +
                    string.Join(" ", code.Segments.Select(s => s.Mode.ToString().Substring(0, 1) + s.Text.Length)) + ")");
                masks.Add(code.Mask);
            }
            Check(masks.Count == 8, "the website's marked codes only use masks " + string.Join(", ", masks.OrderBy(m => m)) + ": add texts until all eight are covered");
            Check(QrCode.Make(new string('7', 40), QrLevel.Q, 0, true).Version == 2, "40 digits with the marker at Q should be version 2, as on the website");
        }

        // the split into parts follows the website's rules (its own test cases)
        static void SplitsLikeTheWebsite()
        {
            string Parts(string text, int group) => string.Join(" + ", QrEncoder.Split(text, group).Select(s => s.Mode + " " + s.Text));
            Check(Parts("abc123", 0) == "Byte abc123", "abc123: " + Parts("abc123", 0));
            Check(Parts("0123456789", 0) == "Numeric 0123456789", "digits: " + Parts("0123456789", 0));
            Check(Parts("id=12345678901234567890", 0) == "Byte id= + Numeric 12345678901234567890", "id=…: " + Parts("id=12345678901234567890", 0));
            Check(Parts("https://b0r3d.org/page", 0) == "Byte https://b0r3d.org/page", "a lower-case link: " + Parts("https://b0r3d.org/page", 0));
            Check(Parts("HTTPS://B0R3D.ORG/SOME/PAGE", 0) == "Alphanumeric HTTPS://B0R3D.ORG/SOME/PAGE", "capitals: " + Parts("HTTPS://B0R3D.ORG/SOME/PAGE", 0));
            Check(QrCode.Make(new string('7', 40), QrLevel.Q).Size == 25, "40 digits at Q should make a 25 × 25 code");
            Check(QrCode.Make("HTTPS://B0R3D.ORG/SOME/PAGE", QrLevel.Q).Size == 25, "the capitals link at Q should make a 25 × 25 code");
            QrCode grown = QrCode.Make("https://b0r3d.org/a/longer/address/than/version/one/holds", QrLevel.H, 1);
            Check(grown.Grown && grown.AskedVersion == 1 && grown.Version > 1, "asking for version 1 with too much text should grow it");
        }
    }
}
