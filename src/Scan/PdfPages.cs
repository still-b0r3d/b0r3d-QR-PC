using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace B0r3dQR.Scan
{
    // Reading PDF pages with Windows' own Windows.Data.Pdf reader (built into Windows 10 and 11).
    static class PdfPages
    {
        public const int MaxPages = 50;
        public const int WrongPasswordHResult = unchecked((int)0x8007052B);

        // Loads Windows' PDF reader (one of its objects made and let go of) and does nothing else; called
        // first thing, before WPF draws anything. Without a graphics card (a virtual machine, a remote
        // desktop), Windows draws in software (WARP), for WPF and for the PDF reader, which keeps its
        // renderer for as long as the program runs. As a program closes, Windows shuts its parts down
        // newest first: with the PDF reader loaded after WPF started WARP, the reader went first, and
        // letting go of its renderer then asked WARP for work after the program's threads were gone, so
        // every run that had read a PDF ended with 0xC000000D once its work was done. Loaded before WPF
        // draws, the reader is shut down after WARP, which ends cleanly.
        public static void LoadEarly()
        {
            try
            {
                var options = new PdfPageRenderOptions();
                if (Marshal.IsComObject(options)) Marshal.FinalReleaseComObject(options);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                // a Windows without its PDF reader: opening a PDF says so then
            }
        }

        // System.Runtime.WindowsRuntime's stream helpers were built against the Windows.winmd assembly
        // name, whereas System32\WinMetadata splits types by namespace. Binding once as delegates avoids
        // CS0012 at compile time while calling the framework helpers directly at runtime.
        static readonly Func<Stream, IRandomAccessStream> AsRandomAccessStream =
            (Func<Stream, IRandomAccessStream>)Delegate.CreateDelegate(
                typeof(Func<Stream, IRandomAccessStream>),
                typeof(WindowsRuntimeStreamExtensions).GetMethod("AsRandomAccessStream", new[] { typeof(Stream) }));

        static readonly Func<IInputStream, Stream> AsStreamForRead =
            (Func<IInputStream, Stream>)Delegate.CreateDelegate(
                typeof(Func<IInputStream, Stream>),
                typeof(WindowsRuntimeStreamExtensions).GetMethod("AsStreamForRead", new[] { typeof(IInputStream) }));

        // A PDF file's pages as grey levels (up to MaxPages), ready for the barcode and QR reader.
        // Safe on any thread.
        public static List<Gray> Load(string path, CancellationToken cancel = default)
        {
            string name = Path.GetFileName(path);
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    List<BitmapSource> pages = RenderStreamAsync(stream, 200, MaxPages, cancel).GetAwaiter().GetResult();
                    var grays = new List<Gray>(pages.Count);
                    for (int i = 0; i < pages.Count; i++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        string pageName = pages.Count > 1 ? name + ", page " + (i + 1) : name;
                        grays.Add(Pictures.Grays(pages[i], pageName));
                    }
                    return grays;
                }
            }
            catch (PictureException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (FileNotFoundException) { throw new PictureException("it isn't there any more"); }
            catch (DirectoryNotFoundException) { throw new PictureException("it isn't there any more"); }
            catch (UnauthorizedAccessException) { throw new PictureException("Windows won't let b0r3d QR open it"); }
            catch (IOException e) { throw new PictureException(e.Message.TrimEnd('.')); }
            catch (OutOfMemoryException) { throw new PictureException("it's too big to open"); }
        }

        // Renders the first page of a PDF in memory through Windows' PDF reader (for reading exported PDFs back);
        // null when it isn't a readable PDF.
        public static BitmapSource RenderFirstPage(byte[] pdf, double dpi = 300, int exactWidth = 0, int exactHeight = 0)
        {
            if (pdf == null || pdf.Length == 0) return null;
            try
            {
                using (var ms = new MemoryStream(pdf))
                using (IRandomAccessStream winrtStream = AsRandomAccessStream(ms))
                {
                    PdfDocument doc = PdfDocument.LoadFromStreamAsync(winrtStream).AsTask().GetAwaiter().GetResult();
                    if (doc.PageCount == 0) return null;
                    using (PdfPage page = doc.GetPage(0))
                        return RenderPageAsync(page, dpi, exactWidth, exactHeight).GetAwaiter().GetResult();
                }
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return null;
            }
        }

        static async Task<List<BitmapSource>> RenderStreamAsync(Stream stream, double dpi, int maxPages, CancellationToken cancel)
        {
            using (IRandomAccessStream winrtStream = AsRandomAccessStream(stream))
            {
                PdfDocument doc;
                try
                {
                    doc = await PdfDocument.LoadFromStreamAsync(winrtStream).AsTask().ConfigureAwait(false);
                }
                catch (Exception e) when (e.HResult == WrongPasswordHResult)
                {
                    throw new PictureException("it's protected by a password");
                }
                catch (Exception e) when (!(e is OutOfMemoryException) && !(e is OperationCanceledException))
                {
                    throw new PictureException("it isn't a PDF, or it's damaged");
                }

                if (doc.PageCount == 0)
                    throw new PictureException("it isn't a PDF, or it's damaged");

                int count = (int)Math.Min(doc.PageCount, (uint)maxPages);
                var pages = new List<BitmapSource>(count);
                for (int i = 0; i < count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    using (PdfPage page = doc.GetPage((uint)i))
                    {
                        // Small single-code pages render at 300 DPI so fine modules stay crisp
                        double pageDpi = Math.Max(page.Size.Width, page.Size.Height) < 500 && dpi < 300 ? 300 : dpi;
                        pages.Add(await RenderPageAsync(page, pageDpi).ConfigureAwait(false));
                    }
                }
                return pages;
            }
        }

        static async Task<BitmapSource> RenderPageAsync(PdfPage page, double dpi, int exactWidth = 0, int exactHeight = 0)
        {
            uint width, height;
            if (exactWidth > 0 && exactHeight > 0)
            {
                width = (uint)exactWidth;
                height = (uint)exactHeight;
            }
            else
            {
                // PdfPage.Size is in 96-DPI device-independent pixels and already accounts for page rotation
                double w = Math.Max(1, page.Size.Width * dpi / 96.0);
                double h = Math.Max(1, page.Size.Height * dpi / 96.0);
                if (w * h > Pictures.MaxPixels)
                {
                    double scale = Math.Sqrt(Pictures.MaxPixels / (w * h));
                    w *= scale;
                    h *= scale;
                }
                width = (uint)Math.Max(1, Math.Round(w));
                height = (uint)Math.Max(1, Math.Round(h));
            }

            var options = new PdfPageRenderOptions
            {
                DestinationWidth = width,
                DestinationHeight = height,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            };

            try
            {
                using (var rendered = new InMemoryRandomAccessStream())
                {
                    await page.RenderToStreamAsync(rendered, options).AsTask().ConfigureAwait(false);
                    rendered.Seek(0);
                    using (Stream readStream = AsStreamForRead(rendered))
                    {
                        BitmapFrame frame = BitmapDecoder.Create(
                            readStream,
                            BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad).Frames[0];
                        frame.Freeze();
                        return frame;
                    }
                }
            }
            catch (Exception e) when (!(e is OutOfMemoryException) && !(e is OperationCanceledException) && !(e is PictureException))
            {
                throw new PictureException("it isn't a PDF, or it's damaged");
            }
        }

        static Task<T> AsTask<T>(this IAsyncOperation<T> operation)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.Completed = (info, status) =>
            {
                if (status == AsyncStatus.Completed)
                {
                    try { tcs.TrySetResult(info.GetResults()); }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                }
                else if (status == AsyncStatus.Error)
                {
                    tcs.TrySetException(info.ErrorCode ?? new Exception("WinRT async error"));
                }
                else
                {
                    tcs.TrySetCanceled();
                }
            };
            return tcs.Task;
        }

        static Task AsTask(this IAsyncAction action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            action.Completed = (info, status) =>
            {
                if (status == AsyncStatus.Completed)
                {
                    try { info.GetResults(); tcs.TrySetResult(true); }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                }
                else if (status == AsyncStatus.Error)
                {
                    tcs.TrySetException(info.ErrorCode ?? new Exception("WinRT async error"));
                }
                else
                {
                    tcs.TrySetCanceled();
                }
            };
            return tcs.Task;
        }
    }
}
