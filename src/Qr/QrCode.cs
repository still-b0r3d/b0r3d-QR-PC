using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ZXing;
using ZXing.QrCode.Internal;
using Encoder = ZXing.QrCode.Internal.Encoder;

namespace B0r3dQR.Qr
{
    // How much of a code can be damaged or covered and still read: about 7, 15, 25 or 30 %.
    enum QrLevel { L, M, Q, H }

    // What went wrong making a code, worded for the person typing.
    sealed class QrException : Exception
    {
        public QrException(string message) : base(message) { }
    }

    // A QR code's grid of modules (true = dark) for some text, made by ZXing.Net. Plain ASCII text is
    // split into number, capital-letter and byte parts, whichever is shortest (ZXing's compact mode),
    // so numbers and capitals take fewer modules. Text with any other character is one UTF-8 byte
    // part with no ECI marker, which phone cameras read as UTF-8; split into parts, a reader could
    // guess a different alphabet for each short part. (qr.b0r3d.org makes its codes the same way.)
    sealed class QrCode
    {
        public readonly string Text;
        public readonly QrLevel Level;
        public readonly int Version;   // 1-40
        public readonly int Size;      // modules across: 17 + 4 * Version
        readonly bool[] dark;          // row by row

        QrCode(string text, QrLevel level, int version, int size, bool[] dark)
        {
            Text = text;
            Level = level;
            Version = version;
            Size = size;
            this.dark = dark;
        }

        public bool this[int row, int column] => dark[row * Size + column];

        public int ByteCount => Encoding.UTF8.GetByteCount(Text);

        public int CharacterCount => new StringInfo(Text).LengthInTextElements;

        public bool IsPlainAscii => PlainAscii(Text);

        // The most bytes of text one code holds at each level (version 40, all in byte mode).
        public static int MaxBytes(QrLevel level)
        {
            switch (level)
            {
                case QrLevel.L: return 2953;
                case QrLevel.M: return 2331;
                case QrLevel.Q: return 1663;
                default: return 1273;
            }
        }

        // The most characters one code holds at each level, all digits (version 40). Plain ASCII
        // text longer than this can't fit whatever it is.
        public static int MaxDigits(QrLevel level)
        {
            switch (level)
            {
                case QrLevel.L: return 7089;
                case QrLevel.M: return 5596;
                case QrLevel.Q: return 3993;
                default: return 3057;
            }
        }

        public static QrCode Make(string text, QrLevel level)
        {
            if (string.IsNullOrEmpty(text)) throw new QrException("Type something to make a code.");
            bool ascii = PlainAscii(text);
            // a huge paste is refused at once: ZXing would take a second working that out
            if (ascii ? text.Length > MaxDigits(level) : Encoding.UTF8.GetByteCount(text) > MaxBytes(level))
                throw new QrException(TooMuch(text, level));
            var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.DISABLE_ECI] = true };
            if (ascii) hints[EncodeHintType.QR_COMPACT] = true;
            else hints[EncodeHintType.CHARACTER_SET] = "UTF-8";

            ZXing.QrCode.Internal.QRCode qr;
            try { qr = Encoder.encode(text, Zxing(level), hints); }
            catch (WriterException) { throw new QrException(TooMuch(text, level)); }

            var grid = qr.Matrix;
            int size = grid.Width;
            var dark = new bool[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    dark[y * size + x] = grid[x, y] == 1;
            return new QrCode(text, level, qr.Version.VersionNumber, size, dark);
        }

        static bool PlainAscii(string text)
        {
            foreach (char c in text)
                if (c > 0x7F) return false;
            return true;
        }

        static string TooMuch(string text, QrLevel level)
        {
            int bytes = Encoding.UTF8.GetByteCount(text);
            string most = MaxBytes(level).ToString("N0", CultureInfo.InvariantCulture);
            string advice = level == QrLevel.L ? "so it needs to be shorter." : "so shorten it or pick a lower error correction level.";
            return "Too much for one QR code: " + bytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes. At level " + level +
                " a code holds up to " + most + " bytes of text (more if it's only numbers or capitals), " + advice;
        }

        static ErrorCorrectionLevel Zxing(QrLevel level)
        {
            switch (level)
            {
                case QrLevel.L: return ErrorCorrectionLevel.L;
                case QrLevel.M: return ErrorCorrectionLevel.M;
                case QrLevel.Q: return ErrorCorrectionLevel.Q;
                default: return ErrorCorrectionLevel.H;
            }
        }
    }
}
