using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ZXing.QrCode.Internal;

namespace B0r3dQR.Qr
{
    // How much of a code can be damaged or covered and still read: about 7, 15, 25 or 30 %.
    enum QrLevel { L, M, Q, H }

    // What went wrong making a code, worded for the person typing.
    sealed class QrException : Exception
    {
        public QrException(string message) : base(message) { }
    }

    // A QR code's grid of modules (true = dark) for some text, made the way qr.b0r3d.org makes it
    // (QrEncoder), so the same text, level and size give the same code on both.
    sealed class QrCode
    {
        public readonly string Text;
        public readonly QrLevel Level;          // the level it was made at
        public readonly int Version;            // 1-40
        public readonly int Size;               // modules across: 17 + 4 * Version
        public readonly int AskedVersion;       // the smallest version asked for, or 0 for "as small as it goes"
        public readonly bool Utf8Marker;        // starts with ECI 26: "this text is UTF-8"
        public bool MarkerLeftOut { get; private set; }   // asked for, but there was no room for it
        public readonly IReadOnlyList<QrSegment> Segments;
        public readonly int Mask;
        readonly bool[] dark;                   // row by row

        QrCode(string text, QrLevel level, int askedVersion, bool marker, QrEncoder.Result made)
        {
            Text = text;
            Level = level;
            AskedVersion = askedVersion;
            Utf8Marker = marker;
            Version = made.Version;
            Size = made.Size;
            Segments = made.Segments;
            Mask = made.Mask;
            dark = made.Dark;
        }

        public bool this[int row, int column] => dark[row * Size + column];

        public int ByteCount => Encoding.UTF8.GetByteCount(Text);

        // characters as the website counts them: Unicode code points (an emoji is one)
        public int CharacterCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Text.Length; i++)
                {
                    if (char.IsHighSurrogate(Text[i]) && i + 1 < Text.Length && char.IsLowSurrogate(Text[i + 1])) i++;
                    count++;
                }
                return count;
            }
        }

        public bool IsPlainAscii => PlainAscii(Text);

        // the size asked for was too small for the text, so a bigger one is used
        public bool Grown => AskedVersion > 0 && Version != AskedVersion;

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

        // `version` 1-40 asks for that size at least (a bigger one when the text doesn't fit), 0
        // for the smallest that fits; `utf8Marker` starts the code with ECI 26
        public static QrCode Make(string text, QrLevel level, int version = 0, bool utf8Marker = false)
        {
            if (string.IsNullOrEmpty(text)) throw new QrException("Type something to make a code.");
            bool ascii = PlainAscii(text);
            // a huge paste is refused at once, before any work on it
            if (ascii ? text.Length > MaxDigits(level) : Encoding.UTF8.GetByteCount(text) > MaxBytes(level))
                throw new QrException(TooMuch(text, level));
            int asked = Math.Max(0, Math.Min(40, version));
            QrEncoder.Result made = QrEncoder.Encode(text, Zxing(level), asked, utf8Marker);
            bool leftOut = false;
            if (made == null && utf8Marker)
            {
                // the text fits, but not with the marker: the website then makes it without one, and says so
                made = QrEncoder.Encode(text, Zxing(level), asked, false);
                leftOut = made != null;
            }
            if (made == null) throw new QrException(TooMuch(text, level));
            return new QrCode(text, level, asked, utf8Marker && !leftOut, made) { MarkerLeftOut = leftOut };
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

        internal static ErrorCorrectionLevel Zxing(QrLevel level)
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
