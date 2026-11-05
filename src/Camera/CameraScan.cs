using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using B0r3dQR.Scan;

namespace B0r3dQR.Camera
{
    // Reading codes off a camera, on a thread of its own, as the website's scanner does: its pictures
    // go to the window as they come, and a few times a second one of them is read for a code. The first
    // code found ends it, and so does Stop (the window closing). The camera is let go of on that same
    // thread whatever happens: after a code, after Stop, and as soon as it fails; Stop doesn't wait for
    // a picture the camera hasn't sent, which is cut short from the thread pool. Pictures live only in
    // memory, and only the newest few.
    sealed class CameraScan
    {
        public const int ReadEveryMs = 120;      // the website's pause between reads
        public const int MaxSide = 1280;         // longer pictures are shrunk to this side for reading, as on the website
        internal static int EarlierWindowsMs = 10000;   // the longest a window waits for an earlier window's camera (the tests shorten it)

        readonly ICameras cameras;
        readonly Dispatcher ui;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly object gate = new object();     // the window's Stop and Choose against the camera being read
        Thread thread;
        volatile bool stopping;
        volatile int wanted;                     // the camera to read (its place in the list)
        IFrameSource current;                    // the camera being read, to cut short from another thread (under `gate`)
        int currentAt;                           // its place in the list
        IFrameSource stoppedOnPurpose;           // the last one cut short by Stop or Choose, whose trouble is no failure

        // The cameras of earlier windows not let go of yet: one still starting when its window closed
        // (Windows can't cut that short) is let go of as soon as it has started. A window opened again
        // straight away waits for them, or Windows would say the camera is in use by another
        // application when it's b0r3d QR's own window, just closed.
        static readonly List<ManualResetEvent> notLetGo = new List<ManualResetEvent>();
        readonly ManualResetEvent letGo = new ManualResetEvent(false);
        ManualResetEvent[] earlier = new ManualResetEvent[0];

        // what the window hears, on its own thread
        public Action<List<CameraInfo>> Listed;   // the cameras Windows lists
        public Action<CameraInfo> Starting;       // a camera is being started
        public Action<CameraInfo> Started;        // its pictures are coming
        public Action<Frame> Picture;             // the newest picture, to copy before the call returns
        public Action<FoundCode> Found;           // the code read; the camera is already let go of
        public Action<CameraException> Failed;    // why the camera can't be read
        public Action Ended;                      // the thread is done and the camera let go of

        // for the tests: how many looks for a code there have been
        internal int Looks => Volatile.Read(ref looks);

        public CameraScan(ICameras cameras, Dispatcher ui)
        {
            this.cameras = cameras;
            this.ui = ui;
        }

        public bool Running => thread != null && thread.IsAlive;

        public void Start()
        {
            if (thread != null) return;
            lock (notLetGo)
            {
                earlier = notLetGo.ToArray();
                notLetGo.Add(letGo);
            }
            thread = new Thread(Run) { IsBackground = true, Name = "b0r3d QR camera" };
            thread.SetApartmentState(ApartmentState.MTA);   // Media Foundation's objects work from any thread of the MTA
            thread.Start();
        }

        // another camera from the list: the one being read is let go of first, at once (a picture it's
        // waiting for isn't waited for)
        public void Choose(int index)
        {
            IFrameSource other;
            lock (gate)
            {
                wanted = index;
                other = currentAt != index ? OnPurpose(current) : null;
            }
            wake.Set();
            CutShort(other);
        }

        // the window closing: the camera stops at once, even while it waits for a picture (one still
        // starting is let go of as soon as it has started)
        public void Stop()
        {
            IFrameSource other;
            lock (gate)
            {
                stopping = true;
                other = OnPurpose(current);
            }
            wake.Set();
            CutShort(other);
        }

        // the camera being read, to cut short on purpose, unless it's been cut short already (under `gate`)
        IFrameSource OnPurpose(IFrameSource camera)
        {
            if (camera == null || camera == stoppedOnPurpose) return null;
            stoppedOnPurpose = camera;
            return camera;
        }

        // Cuts short the camera's wait for a picture, from the thread pool: Media Foundation's objects
        // are the MTA's, as the camera's thread is, never the window's thread's. The camera's thread
        // lets go of the rest.
        static void CutShort(IFrameSource camera)
        {
            if (camera == null) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    camera.Interrupt();
                }
                catch (Exception)
                {
                    // stopped as far as Windows lets it be; the camera's thread lets go of the rest
                }
            });
        }

        void Post(Action call, DispatcherPriority priority = DispatcherPriority.Normal) => ui.BeginInvoke(priority, call);

        // Waits for the cameras of earlier windows to be let go of: not past Stop, and not for ever (a
        // camera that never finishes starting is tried anyway, and Windows says whether it's in use). One
        // still not let go of when the time is up may never be (its thread can wait for good: see
        // SourceFrames.Interrupt), so it's dropped from the list: no later window waits for it again.
        void WaitForEarlierWindows()
        {
            var waited = Stopwatch.StartNew();
            foreach (ManualResetEvent other in earlier)
                while (!stopping && !other.WaitOne(0))
                {
                    long left = EarlierWindowsMs - waited.ElapsedMilliseconds;
                    if (left <= 0)
                    {
                        lock (notLetGo) notLetGo.RemoveAll(e => Array.IndexOf(earlier, e) >= 0 && !e.WaitOne(0));
                        return;
                    }
                    WaitHandle.WaitAny(new WaitHandle[] { other, wake }, (int)left);
                }
        }

        void Run()
        {
            IFrameSource source = null;
            FoundCode code = null;
            try
            {
                List<CameraInfo> list = cameras.Find();
                Post(() => Listed?.Invoke(list));
                if (list.Count == 0) throw new CameraException(Trouble.NoCamera, null);
                WaitForEarlierWindows();
                int open = -1;
                bool started = false;
                var back = new Frame();    // the picture being filled
                var front = new Frame();   // the one the window is copying
                while (!stopping)
                {
                    int want = Math.Max(0, Math.Min(list.Count - 1, wanted));
                    if (want != open)
                    {
                        LetGo(ref source);
                        open = want;
                        started = false;
                        CameraInfo camera = list[want];
                        Post(() => Starting?.Invoke(camera));
                        try
                        {
                            source = cameras.Open(camera);   // (nothing cuts starting short: after Stop, it's let go of once started)
                            lock (gate)
                            {
                                current = source;
                                currentAt = want;
                            }
                        }
                        catch (Exception x)
                        {
                            CameraException why = Why(x);   // (another camera can still be chosen)
                            Post(() => Failed?.Invoke(why));
                        }
                        continue;
                    }
                    if (source == null)
                    {
                        wake.WaitOne();   // this one wouldn't start: wait for another choice, or Stop
                        continue;
                    }
                    bool got;
                    try
                    {
                        got = source.Next(back);
                    }
                    catch (Exception x)
                    {
                        bool meant;
                        lock (gate) meant = stoppedOnPurpose == source;
                        LetGo(ref source);
                        if (meant)
                        {
                            open = -1;   // stopped on purpose (the window closing, another camera chosen): no message, and the one wanted starts
                            continue;
                        }
                        CameraException why = Why(x);
                        Post(() => Failed?.Invoke(why));
                        continue;
                    }
                    if (!got || stopping || wanted != open) continue;
                    if (!started)
                    {
                        started = true;
                        CameraInfo camera = list[open];
                        Post(() => Started?.Invoke(camera));
                    }
                    code = Look(back, list.Count > 1 ? list[open].Name : "the camera");
                    if (code != null) break;
                    Show(ref back, ref front);
                }
            }
            catch (Exception x)
            {
                // whatever goes wrong, the camera still goes, and the window says so
                CameraException why = Why(x);
                Post(() => Failed?.Invoke(why));
            }
            finally
            {
                LetGo(ref source);
                letGo.Set();   // a window opened again can start the camera now
                lock (notLetGo) notLetGo.Remove(letGo);
                try
                {
                    cameras.Dispose();
                }
                catch (Exception)
                {
                    // Media Foundation stopping: nothing left to let go of
                }
                FoundCode read = code;
                Post(() =>
                {
                    if (read != null) Found?.Invoke(read);
                    Ended?.Invoke();
                });
            }
        }

        // what went wrong, in the website's words (anything Windows threw that isn't one of the
        // camera's own troubles: "Could not start QR code scanner", with what it said)
        static CameraException Why(Exception x) => x as CameraException ?? new CameraException(Trouble.Failed, x.Message);

        void LetGo(ref IFrameSource source)
        {
            if (source == null) return;
            lock (gate)
            {
                // nothing cuts it short from here on (a cut already on its way finds it let go of, and does nothing)
                if (current == source) current = null;
                if (stoppedOnPurpose == source) stoppedOnPurpose = null;
            }
            Interlocked.Increment(ref cameraGone);
            try
            {
                source.Dispose();
            }
            catch (Exception)
            {
                // it's let go of as far as Windows lets it be
            }
            source = null;
        }

        // ---------- reading ----------

        Task<FoundCode> reading;   // the reader's look at a picture, or null between looks
        readonly Stopwatch clock = Stopwatch.StartNew();   // for the pause between looks: Windows setting its time doesn't move it
        long nextLook;             // when the pause is over, in ms on `clock`
        int looks;
        byte[] grays;              // the picture being read, as grey levels (written only between looks)

        // The code from the last look, if it found one; and when the reader is free and the pause since
        // its last look is over, this picture goes to it.
        FoundCode Look(Frame frame, string source)
        {
            if (reading != null)
            {
                if (!reading.IsCompleted) return null;
                FoundCode found = reading.Status == TaskStatus.RanToCompletion ? reading.Result : null;
                reading = null;
                nextLook = clock.ElapsedMilliseconds + ReadEveryMs;
                if (found != null) return found;
            }
            if (clock.ElapsedMilliseconds < nextLook) return null;
            byte[] picture = Grays(frame, ref grays, out int width, out int height);
            int look = Volatile.Read(ref looks);
            reading = Task.Run(() =>
            {
                FoundCode found = Reader.ReadFrame(picture, width, height, source, look);
                Interlocked.Increment(ref looks);
                return found;
            });
            return null;
        }

        // A picture's grey levels for reading (the Scan tab's weights), at the size the website reads
        // its camera's pictures: 1280 pixels on the longer side when it's longer, the other side in
        // proportion, rounded. Each grey is the average of the pixels it covers (a 2 × 2 block from
        // 2560 × 1440; from 1920 × 1080, blocks of 1 or 2 pixels each way in turn).
        internal static byte[] Grays(Frame frame, ref byte[] into, out int width, out int height)
        {
            double scale = Math.Min(1, (double)MaxSide / Math.Max(frame.Width, frame.Height));
            width = Math.Max(1, (int)Math.Round(frame.Width * scale, MidpointRounding.AwayFromZero));
            height = Math.Max(1, (int)Math.Round(frame.Height * scale, MidpointRounding.AwayFromZero));
            if (into == null || into.Length != width * height) into = new byte[width * height];
            byte[] px = frame.Pixels;
            int stride = frame.Width * 4;
            for (int y = 0; y < height; y++)
            {
                int top = (int)((long)y * frame.Height / height), bottom = Math.Max(top + 1, (int)((long)(y + 1) * frame.Height / height));
                for (int x = 0; x < width; x++)
                {
                    int left = (int)((long)x * frame.Width / width), right = Math.Max(left + 1, (int)((long)(x + 1) * frame.Width / width));
                    int sum = 0;
                    for (int row = top; row < bottom; row++)
                        for (int at = row * stride + left * 4, end = row * stride + right * 4; at < end; at += 4)
                            sum += px[at + 2] * 299 + px[at + 1] * 587 + px[at] * 114;
                    into[y * width + x] = (byte)(sum / (1000 * (bottom - top) * (right - left)));
                }
            }
            return into;
        }

        // ---------- showing ----------

        int showing;      // 1 while the window copies a picture
        int cameraGone;   // how many cameras have been let go of

        // The picture to the window, unless it's still copying the last one (then this one is skipped).
        // The two pictures swap: the window copies one while the next is filled. A picture whose camera
        // has been let go of by the time the window gets to it isn't shown (pictures wait behind the
        // window's other work, so one can come after the camera's failure, or after another camera's start).
        void Show(ref Frame back, ref Frame front)
        {
            if (Interlocked.CompareExchange(ref showing, 1, 0) != 0) return;
            Frame shown = back;
            back = front;
            front = shown;
            int from = Volatile.Read(ref cameraGone);
            Post(() =>
            {
                try
                {
                    if (!stopping && from == Volatile.Read(ref cameraGone)) Picture?.Invoke(shown);
                }
                finally
                {
                    Volatile.Write(ref showing, 0);
                }
            }, DispatcherPriority.Background);
        }
    }
}
