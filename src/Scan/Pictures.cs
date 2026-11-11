using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace B0r3dQR.Scan
{
    // What went wrong opening a picture, worded for people.
    sealed class PictureException : Exception
    {
        public PictureException(string message) : base(message) { }
    }

    // A picture's grey levels, ready for the reader.
    sealed class Gray
    {
        public byte[] Pixels;
        public int Width, Height;
        public string Name;   // "photo.jpg", "scan.tif, page 2"
    }

    // Pictures and PDFs to read codes from: files (through Windows' own picture and PDF readers),
    // folders of them, and pictures handed over by the clipboard or a drop.
    static class Pictures
    {
        // bigger pictures are shrunk to this many pixels first, which is plenty for any code in them
        public const long MaxPixels = 40000000;

        static readonly string[] Extensions =
        {
            ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico",
            ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp", ".hdp", ".pdf",
        };

        public static bool LooksLikePicture(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        // the pictures and PDFs in a folder (not in the folders inside it), by name
        public static List<string> InFolder(string folder)
        {
            return Directory.EnumerateFiles(folder).Where(LooksLikePicture).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static bool IsPdf(string path) => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

        // Each picture in a file, handed to `each` in turn: a PDF's pages one at a time (a page is drawn
        // only once the one before has been handed over, so a long PDF takes the memory of one page),
        // else the pictures Load gives. Returns what was left out of a PDF, in words (pages that couldn't
        // be read, pages past the first 50), or null. Safe on any thread.
        public static string Read(string path, CancellationToken cancel, Action<Gray> each)
        {
            if (IsPdf(path))
            {
                PdfPages.Reading reading = PdfPages.Read(path, cancel, each);
                if (reading.Read == 0 && reading.Failed.Count > 0) throw new PictureException(reading.LeftOut);
                return reading.LeftOut;
            }
            foreach (Gray picture in Load(path, cancel)) each(picture);
            return null;
        }

        // A picture or PDF file's grey levels: every page of a PDF (up to 50) or a TIFF (up to 20),
        // the biggest picture in an icon, otherwise the first (a GIF's first frame). Safe on any thread.
        public static List<Gray> Load(string path, CancellationToken cancel = default)
        {
            string name = Path.GetFileName(path);
            if (IsPdf(path))
                return PdfPages.Load(path, cancel);
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frames = new List<BitmapFrame>();
                    if (decoder is TiffBitmapDecoder) frames.AddRange(decoder.Frames.Take(20));
                    else if (decoder is IconBitmapDecoder) frames.Add(decoder.Frames.OrderByDescending(f => (long)f.PixelWidth * f.PixelHeight).First());
                    else frames.Add(decoder.Frames[0]);
                    return frames.Select((frame, i) => Grays(frame, frames.Count > 1 ? name + ", page " + (i + 1) : name)).ToList();
                }
            }
            catch (FileNotFoundException) { throw new PictureException("it isn't there any more"); }
            catch (DirectoryNotFoundException) { throw new PictureException("it isn't there any more"); }
            catch (UnauthorizedAccessException) { throw new PictureException("Windows won't let b0r3d QR open it"); }
            catch (NotSupportedException) { throw new PictureException("Windows can't open this kind of picture"); }
            catch (FileFormatException) { throw new PictureException("it isn't a picture, or it's damaged"); }
            catch (ArgumentException) { throw new PictureException("it isn't a picture, or it's damaged"); }
            catch (IOException e) { throw new PictureException(e.Message.TrimEnd('.')); }
            catch (OutOfMemoryException) { throw new PictureException("it's too big to open"); }
            // Windows' picture readers can fail in other ways too (a HEIF file without the HEVC
            // extension, a broken TIFF): still just this picture's problem
            catch (Exception e) when (e is System.Runtime.InteropServices.ExternalException || e is InvalidOperationException || e is OverflowException)
            {
                throw new PictureException("Windows couldn't read it (" + e.Message.TrimEnd('.') + ")");
            }
        }

        // Online-only files (OneDrive and the like keep just a placeholder until one is opened):
        // a folder scan leaves them alone rather than downloading every one
        public static bool OnlyOnline(string path)
        {
            const int Offline = 0x1000, RecallOnOpen = 0x40000, RecallOnDataAccess = 0x400000;
            try
            {
                return ((int)File.GetAttributes(path) & (Offline | RecallOnOpen | RecallOnDataAccess)) != 0;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;  // reading it will say what's wrong
            }
        }

        // a small copy of a picture (at most 192 pixels across), for a picture that had no code
        public static BitmapSource Thumbnail(Gray picture)
        {
            int step = Math.Max(1, (int)Math.Ceiling(Math.Max(picture.Width, picture.Height) / 192.0));
            int w = Math.Max(1, picture.Width / step), h = Math.Max(1, picture.Height / step);
            var pixels = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = picture.Pixels[(long)y * step * picture.Width + x * step];
            BitmapSource thumbnail = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, pixels, w);
            thumbnail.Freeze();
            return thumbnail;
        }

        // A picture's grey levels, shrunk first if it's huge. See-through parts count as white,
        // the way a code on a transparent background looks on a page. Safe on any thread for a
        // frozen picture.
        public static Gray Grays(BitmapSource picture, string name)
        {
            BitmapSource source = picture;
            long pixels = (long)picture.PixelWidth * picture.PixelHeight;
            if (pixels > MaxPixels)
            {
                double scale = Math.Sqrt(MaxPixels / (double)pixels);
                source = new TransformedBitmap(picture, new ScaleTransform(scale, scale));
            }
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth, height = bgra.PixelHeight, stride = width * 4;
            var grays = new byte[(long)width * height];
            int band = Math.Max(1, (4 << 20) / stride);  // rows at a time, about 4 MB of colour
            var buffer = new byte[stride * Math.Min(band, height)];
            for (int top = 0; top < height; top += band)
            {
                int rows = Math.Min(band, height - top);
                bgra.CopyPixels(new Int32Rect(0, top, width, rows), buffer, stride, 0);
                for (int i = 0, at = top * width; i < rows * width; i++, at++)
                {
                    int b = buffer[4 * i], g = buffer[4 * i + 1], r = buffer[4 * i + 2], a = buffer[4 * i + 3];
                    int lightness = (r * 299 + g * 587 + b * 114) / 1000;
                    grays[at] = (byte)((lightness * a + 255 * (255 - a)) / 255);
                }
            }
            return new Gray { Pixels = grays, Width = width, Height = height, Name = name };
        }

        // Is there something to read in what was copied or dropped: picture files, a folder, or a picture?
        public static bool CanRead(IDataObject data)
        {
            if (data == null) return false;
            try
            {
                return data.GetDataPresent(DataFormats.FileDrop) || data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.Dib);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return false;
            }
        }

        // What was copied or dropped: files and folders, or a picture (a PNG if there is one: it
        // keeps see-through parts, which plain bitmaps lose)
        public static void From(IDataObject data, out string[] files, out BitmapSource picture)
        {
            files = null;
            picture = null;
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                files = paths;
                return;
            }
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
            {
                try
                {
                    BitmapFrame frame = BitmapDecoder.Create(new MemoryStream(png.ToArray()), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
                    frame.Freeze();
                    picture = frame;
                    return;
                }
                catch (Exception e) when (e is NotSupportedException || e is FileFormatException || e is ArgumentException || e is IOException)
                {
                    // not a usable PNG after all: try the plain bitmap
                }
            }
            if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                if (bitmap.CanFreeze && !bitmap.IsFrozen) bitmap.Freeze();
                picture = bitmap;
            }
        }
    }
}
