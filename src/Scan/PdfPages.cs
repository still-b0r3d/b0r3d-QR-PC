using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace B0r3dQR.Scan
{
    // Reading PDF pages with Windows' own Windows.Data.Pdf reader (built into Windows 10 and 11). A
    // file's pages are drawn one at a time, each made grey and handed over before the next is drawn, and
    // every Windows object is let go of as soon as it's done with, none of it left for .NET to let go of
    // later.
    static class PdfPages
    {
        public const int MaxPages = 50;
        // a page is drawn at 200 DPI (300 for a small one), at most this many pixels, as any picture is (a
        // poster's page comes out at a lower DPI: an A0 at about 160); one page is drawn at a time, so this is
        // the most a PDF's pages take at once
        public const long MaxPagePixels = Pictures.MaxPixels;
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
                Release(new PdfPageRenderOptions());
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                // a Windows without its PDF reader: opening a PDF says so then
            }
        }

        // only one page is drawn at a time, whichever file it's in (a folder scan reads a few files at once),
        // so the memory a PDF takes stays that of one page
        static readonly SemaphoreSlim Drawing = new SemaphoreSlim(1, 1);

        // for the tests: the pages drawn so far (to see that each is handed over before the next is drawn),
        // the most drawn at once, and a page to fail as Windows failing to draw it would (its number, from 1)
        internal static int Drawn, MostAtOnce;
        internal static Func<int, bool> FailPage;
        static int drawingNow;

        // How long Windows' PDF reader may take over one piece of work (opening a file, drawing a page) before
        // it's given up on as not answering: a page drawn holds the one-page-at-a-time lock, so a reader that
        // never answered held up every scan after it, and Cancel couldn't free it. (The tests shorten it, and
        // StallPage stands in for a page whose drawing never ends.)
        internal static int AnswerMs = 60000;
        internal static Func<int, bool> StallPage;
        const string NotAnswering = "Windows' PDF reader stopped answering";

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

        // What reading a PDF came to: its pages, how many were drawn and handed over, the ones that
        // couldn't be drawn (and why), and how many past the first 50 weren't drawn at all.
        public sealed class Reading
        {
            public int Pages, Read;
            public readonly List<(int Page, string Why)> Failed = new List<(int Page, string Why)>();
            public int Skipped => Math.Max(0, Pages - MaxPages);
            public int GaveUpAt;   // the page Windows' PDF reader stopped answering at (the rest of the file left), or 0

            // what was left out, in words ("page 3 couldn't be read (…); pages 51 to 80 weren't read (…)"), or null
            public string LeftOut
            {
                get
                {
                    var parts = new List<string>();
                    if (Failed.Count == 1) parts.Add("page " + Failed[0].Page + " couldn't be read (" + Failed[0].Why + ")");
                    else if (Failed.Count > 1) parts.Add("pages " + Numbers(Failed.Select(f => f.Page).ToList()) + " couldn't be read (" + Failed[0].Why + ")");
                    int last = Math.Min(Pages, MaxPages);
                    if (GaveUpAt > 0 && GaveUpAt < last)
                        parts.Add((GaveUpAt + 1 == last ? "page " + last + " wasn't read" : "pages " + (GaveUpAt + 1) + " to " + last + " weren't read") + " (" + NotAnswering + ")");
                    if (Skipped == 1) parts.Add("page " + (MaxPages + 1) + " wasn't read (b0r3d QR reads a PDF's first " + MaxPages + " pages)");
                    else if (Skipped > 1) parts.Add("pages " + (MaxPages + 1) + " to " + Pages + " weren't read (b0r3d QR reads a PDF's first " + MaxPages + " pages)");
                    return parts.Count == 0 ? null : string.Join("; ", parts);
                }
            }

            // "3, 7 and 9"
            static string Numbers(List<int> pages) =>
                pages.Count == 1 ? pages[0].ToString() : string.Join(", ", pages.Take(pages.Count - 1)) + " and " + pages[pages.Count - 1];
        }

        // A PDF file's pages, one at a time: each is drawn, made grey and handed to `page` before the next
        // is drawn, up to the first 50. A page that can't be drawn is noted and the others are still read.
        // Throws PictureException when the file can't be opened as a PDF at all. Safe on any thread.
        public static Reading Read(string path, CancellationToken cancel, Action<Gray> page)
        {
            string name = Path.GetFileName(path);
            var reading = new Reading();
            FileStream file = null;
            IRandomAccessStream stream = null;
            PdfDocument document = null;
            try
            {
                file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream = AsRandomAccessStream(file);
                document = Open(stream, cancel);
                reading.Pages = (int)Math.Min(document.PageCount, int.MaxValue);
                if (reading.Pages == 0) throw new PictureException("it isn't a PDF, or it's damaged");
                for (int i = 0; i < Math.Min(reading.Pages, MaxPages); i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    Gray gray;
                    Drawing.Wait(cancel);
                    try
                    {
                        if (FailPage != null && FailPage(i + 1)) throw new COMException("a page the tests fail");
                        gray = Draw(document, i, reading.Pages > 1 ? name + ", page " + (i + 1) : name, 200, cancel, StallPage != null && StallPage(i + 1));
                    }
                    catch (TimeoutException)
                    {
                        // the reader stopped answering: this page and the rest of the file are left (each would wait
                        // as long), and the lock is let go of for the next file
                        reading.Failed.Add((i + 1, NotAnswering));
                        reading.GaveUpAt = i + 1;
                        break;
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        reading.Failed.Add((i + 1, Why(e)));
                        continue;
                    }
                    finally
                    {
                        Drawing.Release();
                    }
                    reading.Read++;
                    page(gray);
                }
                return reading;
            }
            catch (FileNotFoundException) { throw new PictureException("it isn't there any more"); }
            catch (DirectoryNotFoundException) { throw new PictureException("it isn't there any more"); }
            catch (UnauthorizedAccessException) { throw new PictureException("Windows won't let b0r3d QR open it"); }
            catch (IOException e) { throw new PictureException(e.Message.TrimEnd('.')); }
            catch (OutOfMemoryException) { throw new PictureException("it's too big to open"); }
            finally
            {
                Release(document);
                stream?.Dispose();
                file?.Dispose();
            }
        }

        // every page read (up to the first 50), for the tests and anything else that wants them all at once
        public static List<Gray> Load(string path, CancellationToken cancel = default)
        {
            var pages = new List<Gray>();
            Reading reading = Read(path, cancel, pages.Add);
            if (reading.Read == 0 && reading.Failed.Count > 0) throw new PictureException(reading.LeftOut);
            return pages;
        }

        // Renders the first page of a PDF in memory through Windows' PDF reader (for reading exported PDFs back);
        // null when it isn't a readable PDF.
        public static BitmapSource RenderFirstPage(byte[] pdf, double dpi = 300, int exactWidth = 0, int exactHeight = 0)
        {
            if (pdf == null || pdf.Length == 0) return null;
            var bytes = new MemoryStream(pdf);
            IRandomAccessStream stream = null;
            PdfDocument document = null;
            try
            {
                stream = AsRandomAccessStream(bytes);
                document = Open(stream);
                if (document.PageCount == 0) return null;
                PdfPage page = document.GetPage(0);
                try
                {
                    BitmapSource picture = null;
                    Render(page, dpi, exactWidth, exactHeight, read =>
                    {
                        BitmapFrame frame = BitmapDecoder.Create(read, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                        frame.Freeze();
                        picture = frame;
                    });
                    return picture;
                }
                finally
                {
                    Close(page);
                }
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return null;
            }
            finally
            {
                Release(document);
                stream?.Dispose();
                bytes.Dispose();
            }
        }

        static PdfDocument Open(IRandomAccessStream stream, CancellationToken cancel = default)
        {
            try
            {
                return Wait(PdfDocument.LoadFromStreamAsync(stream), cancel);
            }
            catch (TimeoutException)
            {
                throw new PictureException(NotAnswering);
            }
            catch (Exception e) when (e.HResult == WrongPasswordHResult)
            {
                throw new PictureException("it's protected by a password");
            }
            catch (Exception e) when (!(e is OutOfMemoryException) && !(e is OperationCanceledException))
            {
                throw new PictureException("it isn't a PDF, or it's damaged");
            }
        }

        // one page drawn and made grey: the picture Windows draws is read a band at a time into the grey
        // levels, never held whole beside them
        static Gray Draw(PdfDocument document, int index, string name, double dpi, CancellationToken cancel, bool stall)
        {
            PdfPage page = document.GetPage((uint)index);
            int now = Interlocked.Increment(ref drawingNow);
            try
            {
                if (now > MostAtOnce) MostAtOnce = now;
                Interlocked.Increment(ref Drawn);
                // small single-code pages are drawn at 300 DPI so fine modules stay crisp
                Windows.Foundation.Size size = page.Size;
                double pageDpi = Math.Max(size.Width, size.Height) < 500 && dpi < 300 ? 300 : dpi;
                Gray gray = null;
                Render(page, pageDpi, 0, 0, read =>
                {
                    BitmapFrame frame = BitmapDecoder.Create(read, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0];
                    gray = Pictures.Grays(frame, name);
                }, cancel, stall);
                return gray;
            }
            finally
            {
                Interlocked.Decrement(ref drawingNow);
                Close(page);
            }
        }

        // A page drawn on white by Windows' PDF reader into memory, then handed to `use` as a stream to read it
        // from (only while `use` runs); everything Windows made for it let go of before this returns. Waits at
        // most AnswerMs for the drawing (TimeoutException), and Cancel ends the wait at once.
        static void Render(PdfPage page, double dpi, int exactWidth, int exactHeight, Action<Stream> use, CancellationToken cancel = default, bool stall = false)
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
                Windows.Foundation.Size size = page.Size;
                double w = Math.Max(1, size.Width * dpi / 96.0);
                double h = Math.Max(1, size.Height * dpi / 96.0);
                if (w * h > MaxPagePixels)
                {
                    double scale = Math.Sqrt(MaxPagePixels / (w * h));
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
            var rendered = new InMemoryRandomAccessStream();
            try
            {
                Wait(page.RenderToStreamAsync(rendered, options), cancel, stall);
                rendered.Seek(0);
                using (Stream read = AsStreamForRead(rendered)) use(read);
            }
            catch (Exception e) when (!(e is OutOfMemoryException) && !(e is OperationCanceledException) && !(e is PictureException) && !(e is TimeoutException))
            {
                throw new PictureException("it isn't a PDF, or it's damaged");
            }
            finally
            {
                Close(rendered);
                Release(options);
            }
        }

        // why a page couldn't be drawn, in words
        static string Why(Exception e) =>
            e is PictureException ? e.Message : e is OutOfMemoryException ? "it's too big to draw" : "Windows' PDF reader couldn't draw it";

        // Windows' object closed (a page, a stream) and let go of
        static void Close(IDisposable closable)
        {
            if (closable == null) return;
            try
            {
                closable.Dispose();
            }
            catch (Exception e) when (e is COMException || e is ObjectDisposedException || e is InvalidOperationException)
            {
                // closed already
            }
            Release(closable);
        }

        // Windows' object let go of now, rather than whenever .NET gets round to it
        static void Release(object winrt)
        {
            if (winrt != null && Marshal.IsComObject(winrt)) Marshal.FinalReleaseComObject(winrt);
        }

        // waits for Windows' work (at most AnswerMs, and only until Cancel), then closes it and lets it go
        static T Wait<T>(IAsyncOperation<T> operation, CancellationToken cancel = default)
        {
            try
            {
                Finished(operation, handler => operation.Completed = (info, status) => handler(), cancel, false);
                return operation.GetResults();
            }
            finally
            {
                Done(operation);
            }
        }

        static void Wait(IAsyncAction action, CancellationToken cancel = default, bool stall = false)
        {
            try
            {
                Finished(action, handler => action.Completed = (info, status) => handler(), cancel, stall);
                action.GetResults();
            }
            finally
            {
                Done(action);
            }
        }

        // Waits for the work: OperationCanceledException at once on Cancel, TimeoutException after AnswerMs (each
        // asking Windows to stop the work too), Windows' error when it failed. `stall`: for the tests, as if it never
        // ended.
        static void Finished(IAsyncInfo work, Action<Action> whenDone, CancellationToken cancel, bool stall)
        {
            var done = new TaskCompletionSource<bool>();
            whenDone(() => done.TrySetResult(true));
            Task ended = stall ? new TaskCompletionSource<bool>().Task : done.Task;
            bool inTime;
            try
            {
                inTime = ended.Wait(AnswerMs, cancel);
            }
            catch (OperationCanceledException)
            {
                Stop(work);
                throw;
            }
            if (!inTime)
            {
                Stop(work);
                throw new TimeoutException(NotAnswering);
            }
            if (work.Status == AsyncStatus.Error) throw work.ErrorCode ?? new COMException("Windows' PDF reader failed");
            if (work.Status != AsyncStatus.Completed) throw new OperationCanceledException();
        }

        static void Stop(IAsyncInfo work)
        {
            try
            {
                work.Cancel();
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                // finished already, or past stopping
            }
        }

        // the work closed (Windows refuses to close work still going, which is then only let go of) and let go of
        static void Done(IAsyncInfo work)
        {
            try
            {
                work.Close();
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
            }
            Release(work);
        }
    }
}
