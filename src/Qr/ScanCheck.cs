using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;

namespace B0r3dQR.Qr
{
    // Reading a picture of a code back with ZXing.Net, the way a phone camera reads the saved file.
    static class ScanCheck
    {
        // pixels per module for the check: enough for any reader, and quick to draw
        public const int PixelsPerModule = 4;

        // the quiet zone the QR standard asks for; scanners can struggle with less
        public const int StandardQuietZone = 4;

        public sealed class Reading
        {
            public string Text;
            public string Symbology;  // "]Q1" plain, "]Q2" with an ECI marker
        }

        // The picture's grey levels, for Read. Call it on the thread that made the picture.
        public static byte[] Grays(BitmapSource picture, out int width, out int height)
        {
            var gray = new FormatConvertedBitmap(picture, PixelFormats.Gray8, null, 0);
            width = gray.PixelWidth;
            height = gray.PixelHeight;
            var pixels = new byte[width * height];
            gray.CopyPixels(pixels, width, 0);
            return pixels;
        }

        // The QR code read from the grey levels, or null. Safe on any thread.
        public static Reading Read(byte[] grays, int width, int height)
        {
            var reader = new BarcodeReaderGeneric { AutoRotate = false };
            reader.Options.PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE };
            reader.Options.TryHarder = true;
            Result result = reader.Decode(new RGBLuminanceSource(grays, width, height, RGBLuminanceSource.BitmapFormat.Gray8));
            if (result == null) return null;
            object symbology = null;
            result.ResultMetadata?.TryGetValue(ResultMetadataType.SYMBOLOGY_IDENTIFIER, out symbology);
            return new Reading { Text = result.Text, Symbology = symbology as string };
        }
    }
}
