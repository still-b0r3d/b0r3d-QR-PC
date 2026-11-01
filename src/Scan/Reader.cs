using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Qr;
using ZXing;
using ZXing.Common;

namespace B0r3dQR.Scan
{
    // Finding every code in a picture with ZXing.Net: QR codes, Data Matrix, Aztec and the common
    // barcodes, wherever they are in it, light on dark as well as dark on light, and side-on
    // barcodes too. Safe on any thread.
    static class Reader
    {
        // a big photo of a blurry or very big code reads better at half size
        const long TryHalfAbove = 3000000;

        public static List<FoundCode> ReadAll(byte[] grays, int width, int height, string source, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            List<FoundCode> found = Read(grays, width, height, source, cancel);
            if (found.Count == 0 && (long)width * height > TryHalfAbove)
            {
                cancel.ThrowIfCancellationRequested();
                byte[] half = Half(grays, width, height, out int halfWidth, out int halfHeight);
                found = Read(half, halfWidth, halfHeight, source, cancel);
            }
            return found;
        }

        static List<FoundCode> Read(byte[] grays, int width, int height, string source, CancellationToken cancel)
        {
            var found = new List<FoundCode>();
            Decode(grays, grays, width, height, source, true, found);
            // ZXing's search for several codes looks around each one it finds and can miss some:
            // blank out the codes found so far and look again (light on dark is tried just once)
            byte[] rest = (byte[])grays.Clone();
            for (int round = 0; round < 4 && found.Count > 0; round++)
            {
                cancel.ThrowIfCancellationRequested();
                foreach (FoundCode code in found) Blank(rest, width, code.Box);
                if (Decode(rest, grays, width, height, source, false, found) == 0) break;
            }
            foreach (FoundCode code in found) Blank(rest, width, code.Box);
            ReadPatches(rest, grays, width, height, source, found, cancel);
            // reading order: rows from the top, then left to right
            return found.OrderBy(f => Math.Round(f.Y / 64)).ThenBy(f => f.X).ToList();
        }

        // Every kind ZXing reads but MaxiCode: it only reads one that fills the whole picture, and
        // its reader trips over tiny pictures (a 1x1 GIF threw IndexOutOfRangeException).
        static readonly List<BarcodeFormat> Everything = new List<BarcodeFormat>
        {
            BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC, BarcodeFormat.PDF_417,
            BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E, BarcodeFormat.CODE_128,
            BarcodeFormat.CODE_39, BarcodeFormat.CODE_93, BarcodeFormat.ITF, BarcodeFormat.CODABAR, BarcodeFormat.RSS_14, BarcodeFormat.RSS_EXPANDED,
        };
        static readonly List<BarcodeFormat> FoundFromTheMiddle = new List<BarcodeFormat> { BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC };
        const int MaxPatches = 150;

        // Data Matrix and Aztec finders look outward from the middle of what they're given, so
        // they only see a code that covers the middle of the picture. Each patch that could be one
        // of those codes gets a look of its own, in a piece of the picture with the patch in the
        // middle: the likeliest first, for at most twice the picture's pixels in all.
        static void ReadPatches(byte[] look, byte[] original, int width, int height, string source, List<FoundCode> found, CancellationToken cancel)
        {
            BitMatrix dark = Safely(() => new HybridBinarizer(new RGBLuminanceSource(look, width, height, RGBLuminanceSource.BitmapFormat.Gray8)).BlackMatrix);
            if (dark == null) return;
            long budget = 2L * width * height;
            int tries = 0;
            foreach (Int32Rect patch in Patches(dark))
            {
                cancel.ThrowIfCancellationRequested();
                int cx = patch.X + patch.Width / 2, cy = patch.Y + patch.Height / 2;
                if (found.Any(f => cx >= f.Box.X && cx < f.Box.X + f.Box.Width && cy >= f.Box.Y && cy < f.Box.Y + f.Box.Height)) continue;
                // as much room again around the patch as it's big, keeping it in the middle
                int rx = Math.Min(patch.Width + 16, Math.Min(cx, width - cx)), ry = Math.Min(patch.Height + 16, Math.Min(cy, height - cy));
                budget -= 4L * rx * ry;
                if (budget < 0 || ++tries > MaxPatches) break;
                DecodePiece(look, original, width, height, cx - rx, cy - ry, 2 * rx, 2 * ry, source, found);
            }
        }

        // Patches of touching dark pixels (corners too) that could be a code: 16 pixels or more
        // each way, at most 6 times as long as they're wide, a fifth to most of them dark, and busy,
        // with two or more dark runs a row on average, as a code has and a letter mostly hasn't.
        // The busiest first.
        static List<Int32Rect> Patches(BitMatrix dark)
        {
            int width = dark.Width, height = dark.Height;
            var parent = new List<int>();
            var patches = new List<Patch>();
            var above = new List<Run>();
            var here = new List<Run>();
            var row = new BitArray(width);
            for (int y = 0; y < height; y++)
            {
                row = dark.getRow(y, row);
                here.Clear();
                int a = 0;
                for (int x = row.getNextSet(0); x < width; )
                {
                    int end = row.getNextUnset(x);   // the run is x to end - 1
                    while (a < above.Count && above[a].End < x) a++;
                    int id = -1;
                    for (int b = a; b < above.Count && above[b].Start <= end; b++)
                        id = id < 0 ? Root(parent, above[b].Id) : Join(parent, patches, id, above[b].Id);
                    if (id < 0)
                    {
                        id = parent.Count;
                        parent.Add(id);
                        patches.Add(new Patch { MinX = x, MaxX = end - 1, MinY = y, MaxY = y });
                    }
                    patches[id].Add(x, end, y);
                    here.Add(new Run { Start = x, End = end, Id = id });
                    x = end < width ? row.getNextSet(end) : width;
                }
                List<Run> swap = above;
                above = here;
                here = swap;
            }

            var likely = new List<KeyValuePair<Int32Rect, double>>();
            for (int i = 0; i < parent.Count; i++)
            {
                if (parent[i] != i) continue;
                Patch p = patches[i];
                int w = p.MaxX - p.MinX + 1, h = p.MaxY - p.MinY + 1;
                if (w < 16 || h < 16 || Math.Max(w, h) > 6 * Math.Min(w, h)) continue;
                double share = p.Pixels / ((double)w * h), busy = p.Runs / (double)h;
                if (share < 0.2 || share > 0.85 || busy < 2) continue;
                likely.Add(new KeyValuePair<Int32Rect, double>(new Int32Rect(p.MinX, p.MinY, w, h), busy));
            }
            return likely.OrderByDescending(l => l.Value).Select(l => l.Key).ToList();
        }

        struct Run { public int Start, End, Id; }

        sealed class Patch
        {
            public int MinX, MaxX, MinY, MaxY, Runs;
            public long Pixels;

            public void Add(int start, int end, int y)
            {
                MinX = Math.Min(MinX, start);
                MaxX = Math.Max(MaxX, end - 1);
                MinY = Math.Min(MinY, y);
                MaxY = Math.Max(MaxY, y);
                Runs++;
                Pixels += end - start;
            }

            public void Take(Patch other)
            {
                MinX = Math.Min(MinX, other.MinX);
                MaxX = Math.Max(MaxX, other.MaxX);
                MinY = Math.Min(MinY, other.MinY);
                MaxY = Math.Max(MaxY, other.MaxY);
                Runs += other.Runs;
                Pixels += other.Pixels;
            }
        }

        static int Root(List<int> parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        // one patch from two that touch; which one it is now
        static int Join(List<int> parent, List<Patch> patches, int a, int b)
        {
            a = Root(parent, a);
            b = Root(parent, b);
            if (a == b) return a;
            patches[a].Take(patches[b]);
            parent[b] = a;
            return a;
        }

        // adds the codes in `look` that aren't in `found` yet (thumbnails from `original`); how many
        static int Decode(byte[] look, byte[] original, int width, int height, string source, bool tryInverted, List<FoundCode> found)
        {
            var reader = new BarcodeReaderGeneric { AutoRotate = false };
            reader.Options.PossibleFormats = Everything;
            reader.Options.TryHarder = true;           // also tries 1D barcodes turned on their side
            reader.Options.TryInverted = tryInverted;  // light codes on dark backgrounds
            Result[] results = Safely(() => reader.DecodeMultiple(new RGBLuminanceSource(look, width, height, RGBLuminanceSource.BitmapFormat.Gray8)));
            return Add(results, 0, 0, original, width, height, source, found);
        }

        // ZXing slipping on an odd picture (an index past the end, deep inside one of its readers)
        // means nothing was found there, not that the scan has to stop
        static T Safely<T>(Func<T> read) where T : class
        {
            try
            {
                return read();
            }
            catch (Exception e) when (!(e is OutOfMemoryException) && !(e is OperationCanceledException))
            {
                return null;
            }
        }

        // a piece of the picture, for a Data Matrix or Aztec code in its middle
        static void DecodePiece(byte[] look, byte[] original, int width, int height, int left, int top, int pieceWidth, int pieceHeight, string source, List<FoundCode> found)
        {
            var piece = new byte[pieceWidth * pieceHeight];
            for (int y = 0; y < pieceHeight; y++) Buffer.BlockCopy(look, (top + y) * width + left, piece, y * pieceWidth, pieceWidth);
            var reader = new BarcodeReaderGeneric { AutoRotate = false };
            reader.Options.PossibleFormats = FoundFromTheMiddle;
            Result result = Safely(() => reader.Decode(new RGBLuminanceSource(piece, pieceWidth, pieceHeight, RGBLuminanceSource.BitmapFormat.Gray8)));
            if (result != null) Add(new[] { result }, left, top, original, width, height, source, found);
        }

        // the results not found already, their points moved from a piece back onto the whole picture
        static int Add(Result[] results, int left, int top, byte[] original, int width, int height, string source, List<FoundCode> found)
        {
            int added = 0;
            foreach (Result result in results ?? new Result[0])
            {
                if (string.IsNullOrEmpty(result.Text)) continue;
                ResultPoint[] points = (result.ResultPoints ?? new ResultPoint[0]).Where(p => p != null).Select(p => new ResultPoint(p.X + left, p.Y + top)).ToArray();
                FoundCode code = Make(result, points, original, width, height, source);
                if (found.Any(f => SameCode(f, code))) continue;
                found.Add(code);
                added++;
            }
            return added;
        }

        // The same code read twice: the same text and type, in the same place. (A barcode can be
        // read along any line across its bars, so two reads of one barcode can be far apart.)
        // Two identical stickers in different places stay two codes.
        static bool SameCode(FoundCode a, FoundCode b)
        {
            if (a.Text != b.Text || a.Format != b.Format) return false;
            if (a.Box.IsEmpty || b.Box.IsEmpty) return Math.Abs(a.X - b.X) < 24 && Math.Abs(a.Y - b.Y) < 24;
            return a.Box.X < b.Box.X + b.Box.Width && b.Box.X < a.Box.X + a.Box.Width && a.Box.Y < b.Box.Y + b.Box.Height && b.Box.Y < a.Box.Y + a.Box.Height;
        }

        static void Blank(byte[] grays, int width, Int32Rect box)
        {
            for (int y = box.Y; y < box.Y + box.Height; y++)
                for (int x = box.X; x < box.X + box.Width; x++)
                    grays[y * width + x] = 255;
        }

        static FoundCode Make(Result result, ResultPoint[] points, byte[] grays, int width, int height, string source)
        {
            string symbology = Metadata(result, ResultMetadataType.SYMBOLOGY_IDENTIFIER);
            Kinds.Describe(result.Text, result.BarcodeFormat, out string kind, out string detail);
            var code = new FoundCode
            {
                Text = result.Text,
                Kind = kind,
                Detail = detail,
                Format = Kinds.FormatName(result.BarcodeFormat, result.Text, symbology),
                Source = source,
                IsWebLink = Kinds.WebLink(result.Text, out Uri link),
                // plain QR codes only: a GS1 one would come out as plain text (other kinds of code: stage 5)
                CanMakeAgain = result.BarcodeFormat == BarcodeFormat.QR_CODE && (symbology == null || symbology == "]Q1" || symbology == "]Q2"),
                Thumbnail = Crop(points, grays, width, height, out double x, out double y, out Int32Rect box),
            };
            code.X = x;
            code.Y = y;
            code.Box = box;
            if (Enum.TryParse(Metadata(result, ResultMetadataType.ERROR_CORRECTION_LEVEL), true, out QrLevel level) && Enum.IsDefined(typeof(QrLevel), level))
                code.Level = level;
            return code;
        }

        static string Metadata(Result result, ResultMetadataType type)
        {
            return result.ResultMetadata != null && result.ResultMetadata.TryGetValue(type, out object value) ? value?.ToString() : null;
        }

        // A thumbnail of where the code was: the points ZXing found (a QR code's finder pattern
        // centres, a barcode's scan line) and some room around them, at most 192 pixels across.
        static BitmapSource Crop(ResultPoint[] points, byte[] grays, int width, int height, out double centreX, out double centreY, out Int32Rect box)
        {
            centreX = centreY = 0;
            box = Int32Rect.Empty;
            ResultPoint[] seen = (points ?? new ResultPoint[0]).Where(p => p != null).ToArray();
            if (seen.Length == 0) return null;
            float minX = seen.Min(p => p.X), maxX = seen.Max(p => p.X), minY = seen.Min(p => p.Y), maxY = seen.Max(p => p.Y);
            centreX = (minX + maxX) / 2;
            centreY = (minY + maxY) / 2;
            float span = Math.Max(Math.Max(maxX - minX, maxY - minY), 8);
            bool line = maxY - minY < span * 0.2f || maxX - minX < span * 0.2f;  // a barcode's scan line: open it up across
            float padAlong = Math.Max(span * 0.3f, 12), padAcross = line ? Math.Max(span * 0.35f, 16) : padAlong;
            float padX = maxX - minX >= maxY - minY ? padAlong : padAcross, padY = maxX - minX >= maxY - minY ? padAcross : padAlong;
            int x0 = Clamp((int)(minX - padX), 0, width - 1), y0 = Clamp((int)(minY - padY), 0, height - 1);
            int x1 = Clamp((int)Math.Ceiling(maxX + padX), x0 + 1, width), y1 = Clamp((int)Math.Ceiling(maxY + padY), y0 + 1, height);
            int cropWidth = x1 - x0, cropHeight = y1 - y0;
            box = new Int32Rect(x0, y0, cropWidth, cropHeight);
            int step = Math.Max(1, (int)Math.Ceiling(Math.Max(cropWidth, cropHeight) / 192.0));
            int w = Math.Max(1, cropWidth / step), h = Math.Max(1, cropHeight / step);
            var pixels = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = grays[(y0 + y * step) * width + x0 + x * step];
            BitmapSource thumbnail = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, pixels, w);
            thumbnail.Freeze();
            return thumbnail;
        }

        static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        // every 2x2 block of pixels averaged into one
        static byte[] Half(byte[] grays, int width, int height, out int halfWidth, out int halfHeight)
        {
            halfWidth = width / 2;
            halfHeight = height / 2;
            var half = new byte[halfWidth * halfHeight];
            for (int y = 0; y < halfHeight; y++)
            {
                int top = 2 * y * width, bottom = top + width;
                for (int x = 0; x < halfWidth; x++)
                    half[y * halfWidth + x] = (byte)((grays[top + 2 * x] + grays[top + 2 * x + 1] + grays[bottom + 2 * x] + grays[bottom + 2 * x + 1] + 2) / 4);
            }
            return half;
        }
    }
}
