using System;
using System.Linq;
using B0r3dQR.Qr;

namespace B0r3dQR.Tests
{
    static partial class TestMain
    {
        static readonly string[] Samples =
        {
            "https://b0r3d.org",
            "HELLO WORLD 0123456789",
            "0123456789012345678901234567890123456789",
            "héllo wörld \U0001F44B b0r3d",
            "日本語のテキスト",
            "Line one\nLine two\n\nLine four",
            "WIFI:T:WPA;S:b0r3d;P:p@ss;w0rd;;",
            "mailto:someone@example.com?subject=Hello%20there",
            new string('x', 500) + " end",
        };

        static readonly QrLevel[] Levels = { QrLevel.L, QrLevel.M, QrLevel.Q, QrLevel.H };

        // draws the code as a saved picture (4 pixels a module, like the check) and reads it back
        static ScanCheck.Reading DrawAndRead(QrCode code, int quietZone, int pixelsPerModule = ScanCheck.PixelsPerModule)
        {
            var picture = QrPicture.Render(QrShapes.For(code, quietZone), pixelsPerModule);
            byte[] grays = ScanCheck.Grays(picture, out int width, out int height);
            return ScanCheck.Read(grays, width, height);
        }

        static void EveryCodeReadsBack()
        {
            int tried = 0;
            foreach (string text in Samples)
                foreach (QrLevel level in Levels)
                    foreach (int quiet in new[] { 4, 8 })
                    {
                        QrCode code = QrCode.Make(text, level);
                        Check(code.Size == 17 + 4 * code.Version, "\"" + Short(text) + "\": " + code.Size + " modules isn't version " + code.Version);
                        ScanCheck.Reading reading = DrawAndRead(code, quiet);
                        Check(reading != null, "\"" + Short(text) + "\" at level " + level + ", quiet zone " + quiet + ": didn't read back");
                        Check(reading.Text == text, "\"" + Short(text) + "\" at level " + level + ": read back as \"" + Short(reading.Text) + "\"");
                        Check(reading.Symbology == "]Q1", "\"" + Short(text) + "\": read as " + reading.Symbology + ", not ]Q1 (an ECI marker crept in?)");
                        tried++;
                    }
            Check(tried == Samples.Length * Levels.Length * 2, "only " + tried + " codes were tried");
        }

        static void CompactAndUtf8()
        {
            // 41 digits fit version 1 at level L only as numbers (as bytes they'd need version 3)
            Check(QrCode.Make(new string('7', 41), QrLevel.L).Version == 1, "41 digits at level L should be version 1");
            // 23 capitals and spaces fit version 1 at level L only as capitals (bytes: version 2)
            Check(QrCode.Make("HELLO WORLD HELLO WORLD", QrLevel.L).Version == 1, "23 capitals at level L should be version 1");
            // a mix: version 3 compact, against version 4 all as bytes (checked when planning)
            Check(QrCode.Make("ORDER 0123456789012345678901234567890123 for b0r3d.org", QrLevel.M).Version == 3, "the mixed sample should be version 3 at level M");
            // any other character: one UTF-8 byte part with no ECI marker, read back exactly
            foreach (string text in new[] { "café 12345678901234567890", "Привет 123" })
            {
                QrCode code = QrCode.Make(text, QrLevel.M);
                Check(!code.IsPlainAscii, "\"" + text + "\" counted as plain ASCII");
                ScanCheck.Reading reading = DrawAndRead(code, 4);
                Check(reading != null && reading.Text == text, "\"" + text + "\" read back as \"" + Short(reading?.Text) + "\"");
                Check(reading.Symbology == "]Q1", "\"" + text + "\" has an ECI marker (" + reading.Symbology + ")");
            }
        }

        static void TooMuchText()
        {
            foreach (QrLevel level in Levels)
            {
                int most = QrCode.MaxBytes(level);
                QrCode biggest = QrCode.Make(new string('a', most), level);  // lower case: bytes
                Check(biggest.Version == 40, most + " bytes at level " + level + " should be version 40, not " + biggest.Version);
                try
                {
                    QrCode.Make(new string('a', most + 1), level);
                    Check(false, (most + 1) + " bytes at level " + level + " should be refused");
                }
                catch (QrException e)
                {
                    Check(e.Message.StartsWith("Too much", StringComparison.Ordinal), "the message: " + e.Message);
                }
            }
            QrCode big = QrCode.Make(new string('a', QrCode.MaxBytes(QrLevel.L)), QrLevel.L);
            ScanCheck.Reading reading = DrawAndRead(big, 4);
            Check(reading != null && reading.Text == big.Text, "the biggest code (version 40, level L) didn't read back");
            try
            {
                QrCode.Make("", QrLevel.Q);
                Check(false, "empty text should be refused");
            }
            catch (QrException)
            {
            }

            // a huge paste is refused at once instead of freezing the window
            foreach (string huge in new[] { new string('a', 300000), new string('7', QrCode.MaxDigits(QrLevel.L) + 1), new string('é', 2000) })
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    QrCode.Make(huge, QrLevel.Q);
                    Check(false, huge.Length + " characters should be refused");
                }
                catch (QrException)
                {
                }
                Check(watch.ElapsedMilliseconds < 100, huge.Length + " characters took " + watch.ElapsedMilliseconds + " ms to refuse");
            }
            // and the most digits that fit still fit
            Check(QrCode.Make(new string('7', QrCode.MaxDigits(QrLevel.L)), QrLevel.L).Version == 40, "the most digits at level L should fit version 40");
        }

        static void ShapesMatchTheCode()
        {
            QrCode code = QrCode.Make("https://b0r3d.org", QrLevel.Q);
            foreach (int quiet in new[] { 0, 4, 9 })
            {
                QrShapes shapes = QrShapes.For(code, quiet);
                Check(shapes.Width == code.Size + 2 * quiet, "quiet zone " + quiet + ": " + shapes.Width + " modules across");
                var grid = new bool[shapes.Width, shapes.Width];
                foreach (Run r in shapes.Runs)
                {
                    Check(r.Length > 0, "an empty run");
                    for (int x = r.X; x < r.X + r.Length; x++)
                    {
                        Check(!grid[x, r.Y], "two runs cover module " + x + "," + r.Y);
                        grid[x, r.Y] = true;
                    }
                }
                for (int y = 0; y < shapes.Width; y++)
                    for (int x = 0; x < shapes.Width; x++)
                    {
                        bool inside = x >= quiet && y >= quiet && x < quiet + code.Size && y < quiet + code.Size;
                        bool dark = inside && code[y - quiet, x - quiet];
                        Check(grid[x, y] == dark, "quiet zone " + quiet + ": module " + x + "," + y + " should be " + (dark ? "dark" : "light"));
                    }
            }
        }

        static void PictureSizes()
        {
            Check(QrPicture.PixelsPerModule(1000, 33) == 30, "1000 pixels for 33 modules should be 30 a module (990)");
            Check(QrPicture.PixelsPerModule(1000, 25) == 40, "1000 pixels for 25 modules should be 40 a module");
            Check(QrPicture.PixelsPerModule(50, 20) == 3, "2.5 pixels a module should round up to 3");
            Check(QrPicture.PixelsPerModule(100, 185) == 1, "never under one pixel a module");
            foreach (int wanted in new[] { 100, 333, 1000, 2500, QrPicture.MaxPixels })
                foreach (int modules in new[] { 21, 33, 57, 185 })
                {
                    int perModule = QrPicture.PixelsPerModule(wanted, modules);
                    Check(perModule == 1 || Math.Abs(perModule * modules - wanted) <= modules / 2.0,
                        wanted + " pixels for " + modules + " modules came out as " + perModule * modules);
                }
        }
    }
}
