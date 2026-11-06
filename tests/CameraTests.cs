using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Camera;
using B0r3dQR.Scan;
using ZXing;
using ZXing.Common;
using Frame = B0r3dQR.Camera.Frame;
using Reader = B0r3dQR.Scan.Reader;

namespace B0r3dQR.Tests
{
    // The camera. No test ever starts a real camera (this PC's webcam is the owner's to turn on): Windows'
    // cameras are only listed; Media Foundation's reader is tried on video files written here, which go
    // through the same code a camera's pictures do; and the camera window reads pretend cameras through
    // its real read loop: a code turning up, none, each kind of trouble, two cameras to switch between.
    static partial class TestMain
    {
        // ---------- pretend cameras ----------

        // A camera's picture (blue, green, red, spare): a grey room with some noise, as cameras see,
        // and, with `code`, a QR code on a white card in the middle
        static Frame CameraPicture(int width, int height, string code, int seed = 1)
        {
            var random = new Random(seed);
            byte[] gray = Canvas(width, height);
            for (int i = 0; i < gray.Length; i++) gray[i] = (byte)(90 + (i / width) * 60 / height + random.Next(24));
            if (code != null)
            {
                int k = Math.Max(2, height / 90);
                QrCodeSize(code, out int modules);
                // a code with many characters (a big version) is drawn smaller so it still fits the frame
                k = Math.Max(1, Math.Min(k, Math.Min(width, height) / (modules + 8)));
                int side = (modules + 8) * k, left = (width - side) / 2, top = (height - side) / 2;
                PutQr(gray, width, code, left, top, k);
            }
            var frame = new Frame();
            frame.Size(width, height);
            for (int i = 0; i < gray.Length; i++)
            {
                frame.Pixels[4 * i] = frame.Pixels[4 * i + 1] = frame.Pixels[4 * i + 2] = gray[i];
                frame.Pixels[4 * i + 3] = 255;
            }
            return frame;
        }

        static void QrCodeSize(string text, out int modules) => modules = Qr.QrCode.Make(text, Qr.QrLevel.Q).Size;

        // a pretend camera: `Blanks` pictures with no code, then `Code` (none: blank for ever); trouble
        // when it's started, or after `FailAfter` pictures. Like a real one it's one window's at a time
        // (another is told it's in use), it can take a while to start (`StartMs`, which nothing cuts
        // short), and after `StallAfter` pictures it can stop sending for `StallMs` without saying so.
        // `Deaf`: a stop doesn't cut that wait short either, only `Freed` does (as when Windows' reader
        // never hears the stop, and the camera's thread waits for good).
        sealed class FakeCamera
        {
            public string Name;
            public string Code;
            public int Blanks;
            public int Width = 640, Height = 480;
            public Exception OpenTrouble, ReadTrouble;
            public int FailAfter;
            public int StartMs, StallAfter = -1, StallMs;
            public bool Deaf;
            public readonly ManualResetEvent Freed = new ManualResetEvent(false);
            public int Opened, Shown, Closed;   // counted on the camera's thread
            public int Holding;                 // started and not let go of
            public volatile bool Stalled;       // waiting for a picture that hasn't come
            public readonly List<long> HeldAfterStart = new List<long>();   // ms from its start to its let go, each time
            public readonly List<(int Thread, ApartmentState Apartment)> Interrupts = new List<(int, ApartmentState)>();   // who cut its wait short
        }

        // the pretend cameras: what they're asked, in order, and on which threads
        sealed class FakeCameras : ICameras
        {
            public readonly FakeCamera[] All;
            public CameraException FindTrouble;
            public bool Disposed;
            public readonly List<string> Calls = new List<string>();
            public readonly HashSet<int> Threads = new HashSet<int>();

            public FakeCameras(params FakeCamera[] cameras)
            {
                All = cameras;
            }

            void Note(string call)
            {
                lock (Calls)
                {
                    if (call != null) Calls.Add(call);
                    Threads.Add(Thread.CurrentThread.ManagedThreadId);
                }
            }

            public string Said
            {
                get { lock (Calls) return string.Join(", ", Calls); }
            }

            public List<CameraInfo> Find()
            {
                Note("find");
                if (FindTrouble != null) throw FindTrouble;
                return All.Select((c, i) => new CameraInfo { Name = c.Name, Id = "fake " + i }).ToList();
            }

            public IFrameSource Open(CameraInfo camera)
            {
                FakeCamera fake = All[int.Parse(camera.Id.Substring(5))];
                Note("open " + fake.Name);
                if (fake.OpenTrouble != null) throw fake.OpenTrouble;
                int starting = fake.StartMs;
                if (Interlocked.Increment(ref fake.Holding) > 1)
                {
                    // another window has it (as Windows says of a camera another program has)
                    Interlocked.Decrement(ref fake.Holding);
                    throw Webcams.Problem(unchecked((int)0xC00D3704), "Windows didn't start " + fake.Name);
                }
                Thread.Sleep(starting);
                fake.Opened++;
                return new Source(this, fake);
            }

            public void Dispose()
            {
                Note("dispose");
                Disposed = true;
            }

            sealed class Source : IFrameSource
            {
                readonly FakeCameras cameras;
                readonly FakeCamera fake;
                readonly Frame blank, withCode;
                readonly ManualResetEvent stopped = new ManualResetEvent(false);
                readonly System.Diagnostics.Stopwatch started = System.Diagnostics.Stopwatch.StartNew();
                bool done;

                public Source(FakeCameras cameras, FakeCamera fake)
                {
                    this.cameras = cameras;
                    this.fake = fake;
                    blank = CameraPicture(fake.Width, fake.Height, null);
                    withCode = fake.Code == null ? blank : CameraPicture(fake.Width, fake.Height, fake.Code);
                }

                // a picture every 15 ms or so, or none for StallMs once it stalls; once it's been stopped,
                // what Media Foundation says of a camera that's been shut down
                public bool Next(Frame into)
                {
                    cameras.Note(null);
                    bool stall = fake.StallAfter >= 0 && fake.Shown >= fake.StallAfter;
                    fake.Stalled = stall;
                    if ((fake.Deaf ? fake.Freed : stopped).WaitOne(stall ? fake.StallMs : 15)) throw Webcams.Problem(unchecked((int)0xC00D3E85), "the camera stopped sending pictures");   // MF_E_SHUTDOWN
                    fake.Stalled = false;
                    if (fake.ReadTrouble != null && fake.Shown >= fake.FailAfter) throw fake.ReadTrouble;
                    Frame picture = fake.Shown < fake.Blanks ? blank : withCode;
                    fake.Shown++;
                    into.Size(picture.Width, picture.Height);
                    Buffer.BlockCopy(picture.Pixels, 0, into.Pixels, 0, picture.Pixels.Length);
                    return true;
                }

                // from another thread: the wait for a picture ends at once
                public void Interrupt()
                {
                    lock (fake.Interrupts) fake.Interrupts.Add((Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.GetApartmentState()));
                    stopped.Set();
                }

                public void Dispose()
                {
                    if (done) return;
                    done = true;
                    fake.Closed++;
                    lock (fake.HeldAfterStart) fake.HeldAfterStart.Add(started.ElapsedMilliseconds);
                    Interlocked.Decrement(ref fake.Holding);
                    cameras.Note("close " + fake.Name);
                }
            }
        }

        static CameraWindow StartCamera(ICameras cameras)
        {
            // the first window made is the program's main one, and closing that ends it: so that these
            // tests can run on their own (--only), a camera window is never the first
            if (Application.Current.MainWindow == null) NewMain();
            var window = new CameraWindow(cameras);
            LayOut(window.Root, 720, 620);
            window.Begin();
            return window;
        }

        static void WaitForCamera(CameraWindow window, Func<bool> done, int ms = 10000)
        {
            Pump(done, ms);
            LayOut(window.Root, 720, 620);
        }

        // ---------- Windows' cameras, listed ----------

        // Windows' cameras listed, none started: each with a name and a device path. GitHub's machines
        // have none, and Windows without its media features says so as the camera window would.
        static void CamerasListedNotStarted()
        {
            var webcams = new Webcams();
            List<CameraInfo> list;
            try
            {
                list = webcams.Find();
            }
            catch (CameraException x) when (x.Message.Contains("media features"))
            {
                Check(x.Trouble == Trouble.Failed && x.Words == "The camera didn't start.", "no Media Foundation: " + x.Trouble + " / " + x.Words);
                throw new SkipException("Windows' media features (Media Foundation) aren't on this machine");
            }
            finally
            {
                webcams.Dispose();
            }
            Check(list.All(c => c.Name.Trim().Length > 0 && c.Id.Length > 0), "a camera without a name or a device path: " + string.Join(", ", list.Select(c => "\"" + c.Name + "\"")));
            Check(list.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == list.Count, "two cameras with the same name in the list");
            Console.WriteLine("      (Windows lists " + (list.Count == 1 ? "1 camera" : list.Count + " cameras") + "; none was started)");

            // the names to choose by, and which are shown mirrored (the website's rule: front or user)
            var named = new List<CameraInfo> { new CameraInfo { Name = "USB Camera" }, new CameraInfo { Name = "usb camera " }, new CameraInfo { Name = " " }, new CameraInfo { Name = null } };
            Webcams.Name(named);
            Check(named.Select(c => c.Name).SequenceEqual(new[] { "USB Camera", "usb camera (2)", "Camera 3", "Camera 4" }), "the names: " + string.Join(", ", named.Select(c => c.Name)));
            Check(new CameraInfo { Name = "Surface Camera Front" }.Mirrored && new CameraInfo { Name = "User-facing camera" }.Mirrored, "a front camera should be shown mirrored");
            Check(!new CameraInfo { Name = "Integrated Webcam" }.Mirrored && !new CameraInfo { Name = "Surface Camera Rear" }.Mirrored, "only a front camera is mirrored");
        }

        // what each of Windows' answers says, and the kind of picture asked of a camera
        static void CameraTroubleWords()
        {
            var words = new Dictionary<Trouble, string>
            {
                { Trouble.NoCamera, "b0r3d QR can't find a camera on this PC." },
                { Trouble.InUse, "Another app is using the camera. Close it, then try again." },
                { Trouble.Failed, "The camera didn't start." },
            };
            foreach (var pair in words) Check(new CameraException(pair.Key, null).Words == pair.Value, pair.Key + " says \"" + new CameraException(pair.Key, null).Words + "\"");
            string denied = new CameraException(Trouble.Denied, null).Words;
            Check(denied.StartsWith("Windows isn't letting b0r3d QR use the camera.", StringComparison.Ordinal) && denied.Contains("Settings > Privacy & security > Camera") && denied.Contains("Let desktop apps access your camera") &&
                  denied.Contains("Settings > Privacy > Camera") && denied.Contains("Allow desktop apps to access your camera"),
                "access denied should say where Windows allows it: " + denied);

            var codes = new Dictionary<uint, Trouble>
            {
                { 0x80070005, Trouble.Denied },    // E_ACCESSDENIED
                { 0xC00D3704, Trouble.InUse },     // MF_E_HW_MFT_FAILED_START_STREAMING
                { 0xC00D3EA3, Trouble.InUse },     // MF_E_VIDEO_RECORDING_DEVICE_PREEMPTED
                { 0x80070020, Trouble.InUse },     // ERROR_SHARING_VIOLATION
                { 0x800700AA, Trouble.InUse },     // ERROR_BUSY
                { 0x80070964, Trouble.InUse },     // ERROR_DEVICE_IN_USE
                { 0xC00D3EA2, Trouble.NoCamera },  // MF_E_VIDEO_RECORDING_DEVICE_INVALIDATED
                { 0xC00DABE0, Trouble.NoCamera },  // MF_E_NO_CAPTURE_DEVICES_AVAILABLE
                { 0x8007048F, Trouble.NoCamera },  // ERROR_DEVICE_NOT_CONNECTED
                { 0xC00D36B4, Trouble.Failed },    // MF_E_INVALIDMEDIATYPE
                { 0x80004005, Trouble.Failed },    // E_FAIL
            };
            foreach (var pair in codes)
            {
                CameraException made = Webcams.Problem(unchecked((int)pair.Key), "Windows didn't start USB Camera");
                Check(made.Trouble == pair.Value, "0x" + pair.Key.ToString("X8") + " is " + made.Trouble + ", not " + pair.Value);
                Check(made.Message == "Windows didn't start USB Camera (error 0x" + pair.Key.ToString("X8") + ")", "the detail: " + made.Message);
            }

            // the camera's own kind of picture: 15 a second or more, nearest 1280 x 720, then nearest 30 a
            // second (Chrome's choice for the website, which asks for no rate), the faster of two as near
            SourceFrames.Kind K(int index, int w, int h, double rate) => new SourceFrames.Kind { Index = index, Width = w, Height = h, Rate = rate };
            int[] Order(params SourceFrames.Kind[] kinds) => SourceFrames.InOrder(kinds).Select(k => k.Index).ToArray();
            Check(Order(K(0, 640, 480, 30), K(1, 1280, 720, 10), K(2, 1280, 720, 30), K(3, 1920, 1080, 30), K(4, 160, 120, 30))[0] == 2, "1280 x 720 at 30 a second should come first");
            Check(Order(K(0, 1280, 720, 5), K(1, 640, 480, 30))[0] == 1, "a picture every fifth of a second shouldn't beat 640 x 480 at 30");
            Check(Order(K(0, 1920, 1080, 30), K(1, 640, 480, 30))[0] == 0, "1920 x 1080 is nearer 1280 x 720 than 640 x 480 is");
            Check(Order(K(0, 1280, 720, 15), K(1, 1280, 720, 30)).SequenceEqual(new[] { 1, 0 }), "30 a second before 15");
            Check(Order(K(0, 1280, 720, 60), K(1, 1280, 720, 30)).SequenceEqual(new[] { 1, 0 }), "1280 x 720 at 30 a second should come before 60 (twice the decoding for nothing)");
            Check(Order(K(0, 1280, 720, 60), K(1, 1280, 720, 15), K(2, 1280, 720, 24)).SequenceEqual(new[] { 2, 1, 0 }), "the nearest to 30 a second first: " + string.Join(", ", Order(K(0, 1280, 720, 60), K(1, 1280, 720, 15), K(2, 1280, 720, 24))));
            Check(Order(K(0, 1280, 720, 25), K(1, 1280, 720, 35)).SequenceEqual(new[] { 1, 0 }), "of two as near 30 a second, the faster");
            Check(Order(K(0, 1280, 720, 60), K(1, 640, 480, 30))[0] == 0 && Order(K(0, 1280, 720, 60))[0] == 0, "1280 x 720 at 60 a second is used when it's the only 1280 x 720");
            Check(Order(K(0, 0, 0, 0)).Length == 1, "a kind with no size still gets a try");
        }

        // The pictures read for a code, at the website's size: no side longer than 1280 px (a bigger
        // picture shrunk to fit, both sides by the same factor and rounded, a smaller one as it is).
        // Each grey is the average of the pixels it covers.
        static void CameraPicturesShrunkForReading()
        {
            foreach (var (w, h, rw, rh) in new[] { (640, 480, 640, 480), (1280, 720, 1280, 720), (1920, 1080, 1280, 720), (2560, 1440, 1280, 720), (3840, 2160, 1280, 720),
                                                   (720, 1280, 720, 1280), (1600, 1200, 1280, 960), (1281, 721, 1280, 720), (2592, 1944, 1280, 960), (1080, 1920, 720, 1280) })
            {
                var frame = new Frame();
                frame.Size(w, h);
                for (int i = 0; i < w * h; i++)
                {
                    int x = i % w, y = i / w;
                    byte shade = (byte)((x * 7 + y * 13) % 251);
                    frame.Pixels[4 * i] = frame.Pixels[4 * i + 1] = frame.Pixels[4 * i + 2] = shade;
                }
                byte[] into = null;
                byte[] grays = CameraScan.Grays(frame, ref into, out int gw, out int gh);
                Check(gw == rw && gh == rh && grays.Length == rw * rh, w + " x " + h + " was read at " + gw + " x " + gh + ", not " + rw + " x " + rh);
                // a few greys against the frame's own pixels that each one covers
                foreach (var (gx, gy) in new[] { (0, 0), (1, 1), (gw / 2, gh / 3), (gw - 1, gh - 1) })
                {
                    int left = gx * w / gw, right = Math.Max(left + 1, (gx + 1) * w / gw), top = gy * h / gh, bottom = Math.Max(top + 1, (gy + 1) * h / gh);
                    int sum = 0;
                    for (int y = top; y < bottom; y++) for (int x = left; x < right; x++) sum += frame.Pixels[(y * w + x) * 4];
                    int expected = sum / ((right - left) * (bottom - top));
                    Check(Math.Abs(grays[gy * gw + gx] - expected) <= 1 && right - left <= (w + gw - 1) / gw && bottom - top <= (h + gh - 1) / gh,
                        w + " x " + h + ": the grey at " + gx + "," + gy + " is " + grays[gy * gw + gx] + ", not " + expected + " (the average of " + (right - left) + " x " + (bottom - top) + " pixels)");
                }
            }
            var colour = new Frame();
            colour.Size(2, 1);
            colour.Pixels[0] = 255;            // blue
            colour.Pixels[4 + 1] = 255;        // green
            byte[] none = null;
            byte[] two = CameraScan.Grays(colour, ref none, out _, out _);
            Check(two[0] == 29 && two[1] == 149, "blue and green as greys: " + two[0] + ", " + two[1] + " (the Scan tab's weights give 29 and 149)");
        }

        // ---------- the window, reading pretend cameras ----------

        // A code turning up after some pictures: read (what it is, where, from the camera), the camera
        // let go of, the window closed, and every call on the camera's own thread; then the Scan tab's card
        static void CameraReadsTheFirstCode()
        {
            var desk = new FakeCamera { Name = "Desk camera", Blanks = 6, Code = "https://b0r3d.org/camera", Width = 1920, Height = 1080 };
            var cameras = new FakeCameras(desk);
            CameraWindow window = StartCamera(cameras);
            Check(window.StartingText.Visibility == Visibility.Visible && window.StartingText.Text == "Starting the camera…", "the window should start with \"Starting the camera…\"");
            WaitForCamera(window, () => window.IsClosed && window.CameraLetGo);
            Check(window.IsClosed, "the window is still open after " + desk.Shown + " pictures, " + window.Looks + " looks: " + cameras.Said);
            FoundCode code = window.Code;
            Check(code != null && code.Text == "https://b0r3d.org/camera", "the code read: " + Show(code?.Text));
            Check(code.Kind == "Link" && code.Format == "QR code" && code.Source == "the camera" && code.IsWebLink && code.CanMakeAgain && code.HasThumbnail && code.Level == Qr.QrLevel.Q,
                "the code: " + code.Kind + " / " + code.Summary + " / " + code.Level);
            Check(desk.Shown > desk.Blanks, "a code was read before it was there (" + desk.Shown + " pictures)");
            Check(desk.Opened == 1 && desk.Closed == 1 && cameras.Disposed && window.CameraLetGo, "the camera wasn't let go of: " + cameras.Said);
            Check(cameras.Said == "find, open Desk camera, close Desk camera, dispose", "the camera was asked: " + cameras.Said);
            Check(cameras.Threads.Count == 1 && !cameras.Threads.Contains(Thread.CurrentThread.ManagedThreadId), "the camera should only ever be used on its own thread, not " + cameras.Threads.Count + " threads");
            Check(window.Pictures > 0 && window.Looks >= 1, "nothing was shown (" + window.Pictures + " pictures) or read (" + window.Looks + " looks)");
            Check(window.CameraRow.Visibility == Visibility.Collapsed, "one camera needs no list");

            // the Scan tab: the code becomes the list, as on the website's scan page
            MainWindow main = NewMain();
            main.Found.Add(FoundCode.Problem("No code found", "", "old.png"));
            main.Tabs.SelectedItem = main.MakeTab;
            main.ShowCameraCode(window);
            Check(main.Tabs.SelectedItem == main.ScanTab && main.Found.Count == 1 && main.Found[0] == code && main.ScanStatus.Text == "1 code from the camera",
                "the Scan tab after the camera: " + main.Found.Count + " codes, \"" + main.ScanStatus.Text + "\"");
            LayOut(main.Root, MainWidth, MainHeight);
            Check(main.FoundList.Items.Count == 1 && main.FoundHint.Visibility == Visibility.Collapsed, "the card doesn't show");
        }

        // No code: the pictures keep coming and are shown, a few looks a second (not one a picture);
        // closing the window lets the camera go and changes nothing on the Scan tab
        static void CameraWithoutACode()
        {
            var desk = new FakeCamera { Name = "Desk camera", Blanks = int.MaxValue };
            var cameras = new FakeCameras(desk);
            CameraWindow window = StartCamera(cameras);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            WaitForCamera(window, () => false, 1500);
            long ms = watch.ElapsedMilliseconds;
            int looks = window.Looks, shown = desk.Shown;
            Check(!window.IsClosed && window.Code == null && window.Problem == null, "no code: the window " + (window.IsClosed ? "closed" : "has a problem: " + window.Problem?.Words));
            Check(window.StartingText.Visibility == Visibility.Collapsed && window.Viewfinder.Visibility == Visibility.Visible && window.PictureView.Source != null,
                "reading: the picture and its frame should show, not \"Starting the camera…\"");
            Check(shown >= 30 && window.Pictures >= 10, "only " + shown + " pictures came and " + window.Pictures + " were shown in " + ms + " ms");
            Check(looks >= 3 && looks <= ms / CameraScan.ReadEveryMs + 1, looks + " looks in " + ms + " ms at " + shown + " pictures: it should be a few a second");
            Check(desk.Closed == 0, "the camera was let go of while reading");

            window.Close();
            Pump(() => window.CameraLetGo, 3000);
            Check(window.CameraLetGo && desk.Closed == 1 && cameras.Disposed, "closing the window didn't let the camera go: " + cameras.Said);
            int after = desk.Shown;
            Pump(() => false, 200);
            Check(desk.Shown == after, "pictures kept coming after the window closed");

            MainWindow main = NewMain();
            main.Found.Add(FoundCode.Problem("No code found", "", "old.png"));
            main.ShowCameraCode(window);
            Check(main.Found.Count == 1 && main.Found[0].Source == "old.png", "cancelling the camera changed the Scan tab's list");

            // a front camera is shown mirrored, as on the website
            var front = new FakeCameras(new FakeCamera { Name = "Surface Camera Front", Blanks = int.MaxValue });
            CameraWindow selfie = StartCamera(front);
            WaitForCamera(selfie, () => selfie.Pictures > 0, 5000);
            Check(selfie.PictureView.RenderTransform is ScaleTransform flip && flip.ScaleX == -1 && flip.ScaleY == 1, "a front camera's view isn't mirrored");
            selfie.Close();
            Pump(() => selfie.CameraLetGo, 3000);
            Check(front.Disposed && front.All[0].Closed == 1, "the front camera wasn't let go of");
        }

        // each kind of trouble: its message (with Windows' side under it), no picture, the
        // camera let go of at once; and the window stays open until it's closed
        static void CameraTroublesShowTheirMessage()
        {
            CameraException Trouble(Trouble kind, string detail) => new CameraException(kind, detail);
            var cases = new List<(string What, FakeCameras Cameras, Trouble Kind, bool Detail)>
            {
                ("no camera", new FakeCameras(), Camera.Trouble.NoCamera, false),
                ("no media features", new FakeCameras(new FakeCamera { Name = "x" }) { FindTrouble = Trouble(Camera.Trouble.Failed, "Windows' media features aren't on this PC") }, Camera.Trouble.Failed, true),
                ("access denied", new FakeCameras(new FakeCamera { Name = "Desk camera", OpenTrouble = Webcams.Problem(unchecked((int)0x80070005), "Windows didn't start Desk camera") }), Camera.Trouble.Denied, true),
                ("in use", new FakeCameras(new FakeCamera { Name = "Desk camera", OpenTrouble = Webcams.Problem(unchecked((int)0xC00D3704), "Windows didn't start Desk camera") }), Camera.Trouble.InUse, true),
                ("unplugged", new FakeCameras(new FakeCamera { Name = "Desk camera", Blanks = int.MaxValue, FailAfter = 3, ReadTrouble = Webcams.Problem(unchecked((int)0xC00D3EA2), "the camera stopped sending pictures") }), Camera.Trouble.NoCamera, true),
                ("stopped", new FakeCameras(new FakeCamera { Name = "Desk camera", Blanks = int.MaxValue, FailAfter = 3, ReadTrouble = Trouble(Camera.Trouble.Failed, "the camera stopped sending pictures") }), Camera.Trouble.Failed, true),
            };
            foreach (var (what, cameras, kind, detail) in cases)
            {
                CameraWindow window = StartCamera(cameras);
                WaitForCamera(window, () => window.Problem != null, 5000);
                CameraException problem = window.Problem;
                Check(problem != null && problem.Trouble == kind, what + ": the window shows " + (problem == null ? "no problem" : problem.Trouble.ToString()) + ", not " + kind + " (" + cameras.Said + ")");
                Check(window.ProblemPanel.Visibility == Visibility.Visible && window.ProblemText.Text == problem.Words, what + ": the message isn't the website's: " + window.ProblemText.Text);
                Check((window.DetailText.Visibility == Visibility.Visible) == detail && (!detail || window.DetailText.Text.EndsWith(").") || window.DetailText.Text.EndsWith(".")),
                    what + ": Windows' side: \"" + window.DetailText.Text + "\"");
                Check(window.PictureView.Source == null && window.Viewfinder.Visibility == Visibility.Collapsed && window.StartingText.Visibility == Visibility.Collapsed,
                    what + ": a picture, its frame or \"Starting the camera…\" still shows");
                Check(!window.IsClosed && window.Code == null, what + ": the window should stay open with its message, and read nothing");
                Pump(() => cameras.All.All(c => c.Opened == c.Closed), 2000);
                Check(cameras.All.All(c => c.Opened == c.Closed), what + ": the camera wasn't let go of when it failed: " + cameras.Said);
                window.Close();
                Pump(() => window.CameraLetGo, 3000);
                Check(window.CameraLetGo && cameras.Disposed, what + ": closing didn't let the cameras go: " + cameras.Said);
            }

            // A camera that fails just after its first picture while the window is busy: that picture waits
            // behind the message (pictures come after the window's other work) and isn't shown after it
            var hasty = new FakeCamera { Name = "Desk camera", Blanks = int.MaxValue, FailAfter = 1, ReadTrouble = Trouble(Camera.Trouble.Failed, "the camera stopped sending pictures") };
            var busy = new FakeCameras(hasty);
            CameraWindow late = StartCamera(busy);
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (hasty.Closed == 0 && waited.ElapsedMilliseconds < 5000) Thread.Sleep(10);   // the window does nothing meanwhile
            Thread.Sleep(50);
            WaitForCamera(late, () => late.Problem != null, 5000);
            Pump(() => false, 100);
            Check(hasty.Shown == 1 && late.Problem != null && late.Pictures == 0 && late.PictureView.Source == null,
                "a picture sent before the camera failed was shown after its message (" + hasty.Shown + " sent, " + late.Pictures + " shown)");
            late.Close();
            Pump(() => late.CameraLetGo, 3000);
            Check(late.CameraLetGo && busy.Disposed, "closing didn't let the camera go: " + busy.Said);
        }

        // Two cameras: a list of them; choosing the other lets the first go before the second starts,
        // and its code is read, named for the camera it came from. A camera in use, then another chosen.
        static void CameraSwitching()
        {
            var front = new FakeCamera { Name = "Front camera", Blanks = int.MaxValue };
            var back = new FakeCamera { Name = "Back camera", Blanks = 2, Code = "BACK-1234" };
            var cameras = new FakeCameras(front, back);
            CameraWindow window = StartCamera(cameras);
            WaitForCamera(window, () => window.Pictures > 3 && window.Looks > 1, 5000);
            Check(window.CameraRow.Visibility == Visibility.Visible && window.CameraBox.Items.Cast<string>().SequenceEqual(new[] { "Front camera", "Back camera" }) && window.CameraBox.SelectedIndex == 0,
                "the list of cameras: " + string.Join(", ", window.CameraBox.Items.Cast<object>()));
            Check(window.Showing?.Name == "Front camera" && window.PictureView.RenderTransform is ScaleTransform, "the front camera should be showing, mirrored");
            Check(window.CancelButton.IsCancel && (string)window.CameraBox.GetValue(System.Windows.Automation.AutomationProperties.NameProperty) == "Camera", "Esc should cancel, and the list should be named for screen readers");
            window.CameraBox.SelectedIndex = 1;
            WaitForCamera(window, () => window.IsClosed && window.CameraLetGo);
            Check(window.Code?.Text == "BACK-1234" && window.Code.Source == "Back camera", "the second camera's code: " + Show(window.Code?.Text) + " from " + window.Code?.Source);
            Check(cameras.Said == "find, open Front camera, close Front camera, open Back camera, close Back camera, dispose", "switching: " + cameras.Said);
            Check(front.Closed == 1 && back.Closed == 1 && cameras.Disposed, "a camera wasn't let go of: " + cameras.Said);

            // one in use (say, by a video call), then the other
            var busy = new FakeCamera { Name = "Desk camera", OpenTrouble = Webcams.Problem(unchecked((int)0xC00D3704), "Windows didn't start Desk camera") };
            var spare = new FakeCamera { Name = "USB camera", Blanks = 1, Code = "SPARE" };
            var two = new FakeCameras(busy, spare);
            CameraWindow other = StartCamera(two);
            WaitForCamera(other, () => other.Problem != null, 5000);
            Check(other.Problem?.Trouble == Camera.Trouble.InUse && other.ProblemText.Text == "Another app is using the camera. Close it, then try again.", "the camera in use: " + other.ProblemText.Text);
            other.CameraBox.SelectedIndex = 1;
            WaitForCamera(other, () => other.IsClosed && other.CameraLetGo);
            Check(other.Code?.Text == "SPARE" && other.Code.Source == "USB camera", "after the camera in use, the other one's code: " + Show(other.Code?.Text));
            Check(two.Said == "find, open Desk camera, open USB camera, close USB camera, dispose" && spare.Closed == 1, "the cameras were asked: " + two.Said);

            // one that trips over something Windows threw that isn't a known trouble: said as a camera that
            // didn't start, with what Windows said, and the other camera can still be chosen
            var odd = new FakeCamera { Name = "Odd camera", OpenTrouble = new COMException("The device is not ready.", unchecked((int)0x80070015)) };
            var fine = new FakeCamera { Name = "USB camera", Blanks = 1, Code = "FINE" };
            var three = new FakeCameras(odd, fine);
            CameraWindow third = StartCamera(three);
            WaitForCamera(third, () => third.Problem != null, 5000);
            Check(third.ProblemText.Text == "The camera didn't start." && third.DetailText.Text == "The device is not ready.", "the odd camera: " + third.ProblemText.Text + " / " + third.DetailText.Text);
            third.CameraBox.SelectedIndex = 1;
            WaitForCamera(third, () => third.IsClosed && third.CameraLetGo);
            Check(third.Code?.Text == "FINE" && three.Said == "find, open Odd camera, open USB camera, close USB camera, dispose", "after the odd camera, the other one's code: " + Show(third.Code?.Text) + " (" + three.Said + ")");
        }

        // A camera that stops sending pictures without saying so (or takes seconds over one): closing the
        // window stops it at once, its wait cut short from the thread pool (Media Foundation's apartment,
        // never the window's thread), and lets it go without a message. Choosing another camera does the same.
        static void CameraStoppedWhileWaiting()
        {
            var stuck = new FakeCamera { Name = "Desk camera", Blanks = int.MaxValue, StallAfter = 3, StallMs = 8000 };
            var cameras = new FakeCameras(stuck);
            CameraWindow window = StartCamera(cameras);
            try
            {
                WaitForCamera(window, () => stuck.Stalled, 5000);
                Check(stuck.Stalled, "the camera never got to waiting for a picture: " + cameras.Said);
                Pump(() => false, 200);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                window.Close();
                Pump(() => stuck.Closed == 1, 3000);
                long ms = watch.ElapsedMilliseconds;
                Check(stuck.Closed == 1 && ms < 1000, "closing the window let the camera go " + (stuck.Closed == 1 ? "after " + ms + " ms" : "not within 3 s") + ": it waited for its picture first");
                Pump(() => window.CameraLetGo, 3000);
                Check(window.CameraLetGo && cameras.Disposed && cameras.Said == "find, open Desk camera, close Desk camera, dispose", "the camera wasn't let go of: " + cameras.Said);
                int ui = Thread.CurrentThread.ManagedThreadId;
                Check(stuck.Interrupts.Count == 1 && stuck.Interrupts[0].Apartment == ApartmentState.MTA && stuck.Interrupts[0].Thread != ui,
                    "the camera's wait should be cut short once, from the thread pool (MTA), not the window's thread: " + string.Join(", ", stuck.Interrupts.Select(i => i.Apartment + (i.Thread == ui ? " (the window's)" : ""))));
            }
            finally
            {
                if (!window.IsClosed) window.Close();
            }

            // two cameras, the first waiting for a picture: choosing the second stops it at once, with no message
            var frozen = new FakeCamera { Name = "Front camera", Blanks = int.MaxValue, StallAfter = 2, StallMs = 8000 };
            var back = new FakeCamera { Name = "Back camera", Blanks = 2, Code = "BACK-5678" };
            var two = new FakeCameras(frozen, back);
            CameraWindow other = StartCamera(two);
            try
            {
                WaitForCamera(other, () => frozen.Stalled, 5000);
                Check(frozen.Stalled, "the first camera never got to waiting for a picture: " + two.Said);
                Pump(() => false, 200);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                other.CameraBox.SelectedIndex = 1;
                Pump(() => frozen.Closed == 1, 3000);
                long ms = watch.ElapsedMilliseconds;
                Check(frozen.Closed == 1 && ms < 1000, "choosing the other camera let the first go " + (frozen.Closed == 1 ? "after " + ms + " ms" : "not within 3 s") + ": it waited for its picture first");
                WaitForCamera(other, () => other.IsClosed && other.CameraLetGo);
                Check(other.Code?.Text == "BACK-5678" && other.ProblemText.Text == "", "after the first camera was stopped: " + Show(other.Code?.Text) + ", \"" + other.ProblemText.Text + "\"");
                Check(two.Said == "find, open Front camera, close Front camera, open Back camera, close Back camera, dispose" && frozen.Interrupts.Count == 1, "switching: " + two.Said);
            }
            finally
            {
                if (!other.IsClosed) other.Close();
            }
        }

        // A camera still starting when its window closed (Windows can't cut that short) is let go of as soon
        // as it has started; and a window opened again straight away waits for it, rather than saying the
        // camera is in use by another application when that's b0r3d QR's own window, just closed
        static void CameraStillStartingWhenClosed()
        {
            var slow = new FakeCamera { Name = "Desk camera", Blanks = int.MaxValue, StartMs = 1500 };
            var first = new FakeCameras(slow);
            CameraWindow one = StartCamera(first);
            CameraWindow two = null;
            try
            {
                Pump(() => Volatile.Read(ref slow.Holding) == 1, 3000);
                slow.StartMs = 0;   // (the next start is at once: it's the first that has to be waited for)
                Pump(() => false, 100);
                one.Close();
                var again = new FakeCameras(slow);
                two = StartCamera(again);
                WaitForCamera(two, () => two.Pictures > 2 || two.Problem != null, 8000);
                Check(two.Problem == null, "the window opened again says \"" + two.Problem?.Words + "\" (" + two.Problem?.Message + "): b0r3d QR's own closed window still had the camera");
                Check(two.Pictures > 2 && one.CameraLetGo && first.Disposed, "the window opened again shows " + two.Pictures + " pictures");
                Check(first.Said == "find, open Desk camera, close Desk camera, dispose" && slow.HeldAfterStart.Count >= 1 && slow.HeldAfterStart[0] < 300,
                    "the closed window's camera should be let go of as soon as it had started, not " + slow.HeldAfterStart.FirstOrDefault() + " ms after (" + first.Said + ")");
                two.Close();
                Pump(() => two.CameraLetGo, 3000);
                Check(two.CameraLetGo && slow.Opened == 2 && slow.Closed == 2 && slow.Holding == 0 && again.Disposed, "the camera wasn't let go of: " + again.Said);
            }
            finally
            {
                if (!one.IsClosed) one.Close();
                if (two != null && !two.IsClosed) two.Close();
            }
        }

        // A camera whose thread never lets it go (a stop can't cut its wait for a picture short: Windows'
        // trouble). Closing its window doesn't wait for it, nor does closing a window that's waiting for it.
        // The next window waits for it (EarlierWindowsMs at most), then starts its own camera; and no window
        // after that waits for it again.
        static void CameraNeverLetGoOf()
        {
            int was = CameraScan.EarlierWindowsMs;
            var lost = new FakeCamera { Name = "Desk camera", Blanks = int.MaxValue, StallAfter = 2, StallMs = 60000, Deaf = true };
            var usb = new FakeCamera { Name = "USB camera", Blanks = int.MaxValue };
            CameraWindow one = null, hasty = null, two = null, three = null;
            try
            {
                one = StartCamera(new FakeCameras(lost));
                WaitForCamera(one, () => lost.Stalled, 5000);
                Check(lost.Stalled, "the camera never got to waiting for a picture");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                one.Close();
                long closing = watch.ElapsedMilliseconds;
                Pump(() => false, 200);
                Check(!one.CameraLetGo && lost.Closed == 0, "the camera a stop can't cut short was let go of");
                Check(closing < 1000, "closing the window took " + closing + " ms: it waited for a camera that's never let go of");

                // a window closed while it waits for that camera: its wait ends at once
                CameraScan.EarlierWindowsMs = 60000;
                var waiting = new FakeCameras(usb);
                hasty = StartCamera(waiting);
                Pump(() => waiting.Said == "find", 3000);
                Pump(() => false, 200);
                Check(usb.Opened == 0 && waiting.Said == "find", "a window started its camera while the earlier window's was still held: " + waiting.Said);
                watch.Restart();
                hasty.Close();
                closing = watch.ElapsedMilliseconds;
                Pump(() => hasty.CameraLetGo, 3000);
                Check(closing < 1000 && hasty.CameraLetGo && waiting.Said == "find, dispose",
                    "closing a window that waits for an earlier window's camera took " + closing + " ms, and its thread " + (hasty.CameraLetGo ? "ended" : "was still waiting 3 s later") + " (" + waiting.Said + ")");

                // the next window waits for it, then starts its camera all the same
                CameraScan.EarlierWindowsMs = 400;
                watch.Restart();
                two = StartCamera(new FakeCameras(usb));
                WaitForCamera(two, () => two.Pictures > 0 || two.Problem != null, 5000);
                long started = watch.ElapsedMilliseconds;
                Check(two.Pictures > 0 && started >= 350, "the window after it " + (two.Pictures > 0 ? "showed its camera after " + started + " ms, not after waiting 400 ms for the earlier one" : "showed no picture in 5 s: " + two.Problem?.Words));
                two.Close();
                Pump(() => two.CameraLetGo, 3000);

                // and no window after that waits for it again, however long it would wait
                CameraScan.EarlierWindowsMs = 60000;
                watch.Restart();
                three = StartCamera(new FakeCameras(usb));
                WaitForCamera(three, () => three.Pictures > 0 || three.Problem != null, 5000);
                Check(three.Pictures > 0, "the window after that waited for the camera never let go of again: no picture after " + watch.ElapsedMilliseconds + " ms" + (three.Problem != null ? " (" + three.Problem.Words + ")" : ""));
                three.Close();
                Pump(() => three.CameraLetGo, 3000);
                Check(three.CameraLetGo && usb.Opened == 2 && usb.Closed == 2, "the USB camera wasn't let go of: opened " + usb.Opened + " times, let go of " + usb.Closed);
            }
            finally
            {
                CameraScan.EarlierWindowsMs = was;
                lost.Freed.Set();   // its thread ends at last
                foreach (CameraWindow window in new[] { one, hasty, two, three })
                    if (window != null && !window.IsClosed) window.Close();
                if (one != null) Pump(() => one.CameraLetGo, 3000);
            }
        }

        // the Scan tab's part and the menu line: Scan with the camera (its own access key and shortcut),
        // off while the screen is being scanned
        static void CameraOnTheScanTab()
        {
            MainWindow window = NewMain();
            window.Tabs.SelectedItem = window.ScanTab;
            LayOut(window.Root, MainWidth, MainHeight);
            Check(window.ScanCameraButton.Command == Commands.ScanCamera && (string)window.ScanCameraButton.Content == "Scan _with the camera", "the Scan tab's camera button");
            MenuItem scan = window.MenuBar.Items.OfType<MenuItem>().Single(m => (string)m.Header == "Sca_n");
            Check(scan.Items.OfType<MenuItem>().Any(m => m.Command == Commands.ScanCamera && (string)m.Header == "Scan _with the camera"), "Scan with the camera isn't in the Scan menu");
            var key = (KeyGesture)Commands.ScanCamera.InputGestures[0];
            Check(key.Key == Key.A && key.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key.GetDisplayStringForCulture(System.Globalization.CultureInfo.InvariantCulture) == "Ctrl+Shift+A", "the shortcut: " + key.DisplayString);
            var every = typeof(Commands).GetFields().Select(f => f.GetValue(null)).OfType<RoutedUICommand>().SelectMany(c => c.InputGestures.OfType<KeyGesture>().Select(g => g.Key + "+" + g.Modifiers)).ToList();
            Check(every.Count(g => g == key.Key + "+" + key.Modifiers) == 1, "Ctrl+Shift+A belongs to two commands");
            Pump(() => window.ScanCameraButton.IsEnabled, 2000);
            Check(window.ScanCameraButton.IsEnabled, "Scan with the camera should be there when nothing is being scanned");
            BitmapSource shot = GrayPicture(Canvas(300, 200), 300, 200);
            var screen = new Screens.Screen { Bounds = new Int32Rect(0, 0, 300, 200), Scale = 1, Main = true, Number = 1 };
            var overlays = new List<SnipWindow> { new SnipWindow(screen, shot, true) };
            window.WatchSnips(overlays);
            CommandManager.InvalidateRequerySuggested();
            Pump(() => !window.ScanCameraButton.IsEnabled, 2000);
            Check(!window.ScanCameraButton.IsEnabled, "Scan with the camera should be off while the screen is being scanned");
            overlays[0].Close();
            Pump(() => !window.Scanning, 3000);
        }

        // ---------- Media Foundation, on video files ----------

        [DllImport("mfplat.dll")] static extern int MFCreateFile(int access, int open, int flags, [MarshalAs(UnmanagedType.LPWStr)] string path, out IntPtr stream);
        [DllImport("mfplat.dll")] static extern int MFCreateSourceResolver(out IMFSourceResolver resolver);
        [DllImport("mfplat.dll")] static extern int MFCreateSample(out IMFSample sample);
        [DllImport("mfplat.dll")] static extern int MFCreate2DMediaBuffer(int width, int height, int fourCC, int bottomUp, out IMFMediaBuffer buffer);
        [DllImport("mfplat.dll")] static extern int MFCreateMemoryBuffer(int length, out IMFMediaBuffer buffer);

        // Windows' way from a file to a media source (only the tests open files with Media Foundation)
        [ComImport, Guid("fbe5a32d-a497-4b61-bb85-97b1a848a6e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSourceResolver
        {
            [PreserveSig] int CreateObjectFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, int flags, IntPtr props, out int objectType, [MarshalAs(UnmanagedType.IUnknown)] out object made);
            [PreserveSig] int CreateObjectFromByteStream(IntPtr stream, [MarshalAs(UnmanagedType.LPWStr)] string url, int flags, IntPtr props, out int objectType, [MarshalAs(UnmanagedType.IUnknown)] out object made);
        }

        const int Shutdown = unchecked((int)0xC00D3E85);   // MF_E_SHUTDOWN: a source that has been let go of

        // Media Foundation here, started (the caller stops it); false on Windows without its media features
        // (all three of the camera's DLLs: a Windows Server can have some of them)
        static bool StartMediaFoundation()
        {
            if (!new[] { "mfplat.dll", "mf.dll", "mfreadwrite.dll" }.All(dll => File.Exists(Path.Combine(Environment.SystemDirectory, dll)))) return false;
            try
            {
                return MF.Start() == 0;
            }
            catch (Exception x) when (x is DllNotFoundException || x is EntryPointNotFoundException)
            {
                return false;
            }
        }

        [DllImport("mfplat.dll")] static extern int MFTEnumEx(Guid category, int flags, TypeInfo input, TypeInfo output, out IntPtr activates, out int count);

        // a kind of media, as MFTEnumEx takes it (MFT_REGISTER_TYPE_INFO)
        [StructLayout(LayoutKind.Sequential)]
        sealed class TypeInfo
        {
            public Guid Major, Sub;
        }

        static readonly Guid VideoDecoders = new Guid("d6c02d4b-6833-45b4-971a-05a4b04bab91");   // MFT_CATEGORY_VIDEO_DECODER

        // What Windows here lacks to read a kind of video as a camera's pictures are read, or null when
        // nothing. The reader turns YUY2, NV12 and RGB32 into RGB32 itself; any other kind (MJPEG) needs
        // a decoder Windows lists, and a Windows can be without one (Media Foundation's own MJPEG decoder
        // is a DLL apart, mfmjpegdec.dll), as GitHub's machines may be. Asked of Windows, not of b0r3d QR,
        // so a Windows that has it still runs the test, and fails it if b0r3d QR's reading is wrong.
        static string Lacks(string kind)
        {
            if (kind == "YUY2" || kind == "NV12" || kind == "RGB32") return null;
            var compressed = new TypeInfo { Major = MF.Video, Sub = new Guid(BitConverter.ToInt32(Encoding.ASCII.GetBytes(kind), 0), 0, 0x10, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71) };
            int hr = MFTEnumEx(VideoDecoders, 0, compressed, null, out IntPtr found, out int count);   // (0: the software decoders the reader uses)
            for (int i = 0; i < count; i++) Marshal.Release(Marshal.ReadIntPtr(found, i * IntPtr.Size));
            if (found != IntPtr.Zero) Marshal.FreeCoTaskMem(found);
            return hr == 0 && count > 0 ? null : "Windows here has no " + kind + " decoder for Media Foundation";
        }

        // A video file's media source, as a started camera's would be (the file's own kind of picture)
        static IMFMediaSource OpenVideo(string file)
        {
            Check(MFCreateFile(1, 0, 0, file, out IntPtr stream) == 0, "Media Foundation didn't open " + file);   // MF_ACCESSMODE_READ, MF_OPENMODE_FAIL_IF_NOT_EXIST
            Check(MFCreateSourceResolver(out IMFSourceResolver resolver) == 0, "no source resolver");
            int hr = resolver.CreateObjectFromByteStream(stream, file, 1, IntPtr.Zero, out _, out object made);   // MF_RESOLUTION_MEDIASOURCE
            Marshal.Release(stream);
            MF.Release(resolver);
            if (hr == unchecked((int)0xC00D36C4)) throw new SkipException("Media Foundation here can't read AVI files (MF_E_UNSUPPORTED_BYTESTREAM_TYPE)");
            Check(hr == 0 && made is IMFMediaSource, "Media Foundation didn't make a source of " + Path.GetFileName(file) + " (0x" + hr.ToString("X8") + ")");
            return (IMFMediaSource)made;
        }

        // A picture to tell up from down and left from right: red along the top, blue along the bottom,
        // green down the left, and a QR code (or none) in the middle
        static byte[] Marked(int w, int h, string code)
        {
            var bgrx = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    byte b = 200, g = 200, r = 200;
                    if (y < h / 10) { b = 0; g = 0; r = 255; }
                    else if (y >= h - h / 10) { b = 255; g = 0; r = 0; }
                    else if (x < w / 10) { b = 0; g = 255; r = 0; }
                    bgrx[i] = b;
                    bgrx[i + 1] = g;
                    bgrx[i + 2] = r;
                    bgrx[i + 3] = 255;
                }
            if (code != null)
            {
                BitMatrix m = new ZXing.QrCode.QRCodeWriter().encode(code, BarcodeFormat.QR_CODE, 0, 0);
                int k = Math.Max(2, h / 2 / m.Height), left = w / 2 - m.Width * k / 2, top = h / 2 - m.Height * k / 2;
                for (int my = 0; my < m.Height; my++)
                    for (int mx = 0; mx < m.Width; mx++)
                        for (int py = 0; py < k; py++)
                            for (int px = 0; px < k; px++)
                            {
                                int i = ((top + my * k + py) * w + left + mx * k + px) * 4;
                                bgrx[i] = bgrx[i + 1] = bgrx[i + 2] = m[mx, my] ? (byte)0 : (byte)255;
                            }
            }
            return bgrx;
        }

        static byte Clamp(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v)));

        // An AVI file of the pictures as a webcam sends them, YUY2, NV12 or MJPEG, or as RGB32 kept
        // bottom row first (Windows' DIB order), 15 a second
        static byte[] Avi(int w, int h, string kind, List<byte[]> pictures)
        {
            var frames = new List<byte[]>();
            foreach (byte[] bgrx in pictures)
            {
                double Y(int i) => 16 + 0.257 * bgrx[i + 2] + 0.504 * bgrx[i + 1] + 0.098 * bgrx[i];
                double U(int i) => 128 - 0.148 * bgrx[i + 2] - 0.291 * bgrx[i + 1] + 0.439 * bgrx[i];
                double V(int i) => 128 + 0.439 * bgrx[i + 2] - 0.368 * bgrx[i + 1] - 0.071 * bgrx[i];
                if (kind == "YUY2")
                {
                    var yuy2 = new byte[w * h * 2];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x += 2)
                        {
                            int a = (y * w + x) * 4, o = (y * w + x) * 2;
                            yuy2[o] = Clamp(Y(a));
                            yuy2[o + 1] = Clamp(U(a));
                            yuy2[o + 2] = Clamp(Y(a + 4));
                            yuy2[o + 3] = Clamp(V(a));
                        }
                    frames.Add(yuy2);
                }
                else if (kind == "NV12")
                {
                    var nv12 = new byte[w * h * 3 / 2];
                    for (int i = 0; i < w * h; i++) nv12[i] = Clamp(Y(4 * i));
                    for (int y = 0; y < h / 2; y++)
                        for (int x = 0; x < w / 2; x++)
                        {
                            int a = (2 * y * w + 2 * x) * 4, o = w * h + y * w + 2 * x;
                            nv12[o] = Clamp(U(a));
                            nv12[o + 1] = Clamp(V(a));
                        }
                    frames.Add(nv12);
                }
                else if (kind == "RGB32")
                {
                    var dib = new byte[w * h * 4];
                    for (int y = 0; y < h; y++) Buffer.BlockCopy(bgrx, y * w * 4, dib, (h - 1 - y) * w * 4, w * 4);
                    frames.Add(dib);
                }
                else
                {
                    var jpeg = new JpegBitmapEncoder { QualityLevel = 90 };
                    jpeg.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, bgrx, w * 4)));
                    var bytes = new MemoryStream();
                    jpeg.Save(bytes);
                    frames.Add(bytes.ToArray());
                }
            }
            int biggest = frames.Max(f => f.Length), bits = kind == "YUY2" ? 16 : kind == "NV12" ? 12 : kind == "RGB32" ? 32 : 24;
            uint compression = kind == "RGB32" ? 0u : BitConverter.ToUInt32(Encoding.ASCII.GetBytes(kind), 0);
            string chunk = kind == "RGB32" ? "00db" : "00dc";

            var file = new MemoryStream();
            var write = new BinaryWriter(file);
            void Four(string s) => write.Write(Encoding.ASCII.GetBytes(s));
            long Open(string id) { Four(id); write.Write(0); return file.Position; }
            void Close(long start)
            {
                long end = file.Position;
                file.Position = start - 4;
                write.Write((int)(end - start));
                file.Position = end;
                if ((end - start) % 2 == 1) write.Write((byte)0);
            }
            long riff = Open("RIFF");
            Four("AVI ");
            long hdrl = Open("LIST");
            Four("hdrl");
            long avih = Open("avih");   // the main header: 15 pictures a second, an index
            foreach (int v in new[] { 1000000 / 15, biggest * 15, 0, 0x10, frames.Count, 0, 1, biggest, w, h, 0, 0, 0, 0 }) write.Write(v);
            Close(avih);
            long strl = Open("LIST");
            Four("strl");
            long strh = Open("strh");   // the video stream's header
            Four("vids");
            Four(kind == "RGB32" ? "DIB " : kind);
            foreach (int v in new[] { 0, 0, 0, 1, 15, 0, frames.Count, biggest, -1, 0 }) write.Write(v);
            foreach (short v in new short[] { 0, 0, (short)w, (short)h }) write.Write(v);
            Close(strh);
            long strf = Open("strf");   // BITMAPINFOHEADER
            write.Write(40);
            write.Write(w);
            write.Write(h);
            write.Write((short)1);
            write.Write((short)bits);
            write.Write(compression);
            write.Write(kind == "MJPG" ? w * h * 3 : frames[0].Length);
            foreach (int v in new[] { 0, 0, 0, 0 }) write.Write(v);
            Close(strf);
            Close(strl);
            Close(hdrl);
            long movi = Open("LIST");
            long moviStart = file.Position;
            Four("movi");
            var index = new List<(int Offset, int Length)>();
            foreach (byte[] frame in frames)
            {
                index.Add(((int)(file.Position - moviStart), frame.Length));
                long c = Open(chunk);
                write.Write(frame);
                Close(c);
            }
            Close(movi);
            long idx1 = Open("idx1");
            foreach (var (offset, length) in index)
            {
                Four(chunk);
                write.Write(0x10);   // a key frame
                write.Write(offset);
                write.Write(length);
            }
            Close(idx1);
            Close(riff);
            return file.ToArray();
        }

        // Media Foundation's reader, on video files as a webcam sends them (YUY2, NV12, MJPEG) and RGB32
        // kept bottom row first, at 640 × 480 and 1280 × 720: through the camera's own code (the reader,
        // the kind of picture, the copy) every picture comes out the right way up and the right size, its
        // code reads, the end is said, and the source is let go of. And the copy itself, from buffers kept
        // top row first and bottom row first. A kind Windows here can't decode isn't tried, and the test
        // says so as a skip once the others have passed.
        static void MediaFoundationReadsVideo()
        {
            if (!StartMediaFoundation()) throw new SkipException("Windows' media features (Media Foundation) aren't on this machine");
            try
            {
                foreach (bool twoD in new[] { true, false })
                    foreach (bool bottomUp in new[] { false, true })
                        CopiesTheRightWayUp(twoD, bottomUp);

                Check(Lacks("B0R3") != null, "Windows says it has a decoder for a kind of video nobody makes, so its answers can't be trusted to skip by");
                var lacking = new List<string>();
                string folder = Path.Combine(Work, "camera");
                Fresh(folder);
                foreach (string kind in new[] { "YUY2", "NV12", "MJPG", "RGB32" })
                    foreach (var (w, h) in new[] { (640, 480), (1280, 720) })
                    {
                        string lacks = Lacks(kind);
                        if (lacks != null)
                        {
                            if (!lacking.Contains(lacks)) lacking.Add(lacks);
                            continue;
                        }
                        string file = Path.Combine(folder, kind + "-" + w + ".avi");
                        File.WriteAllBytes(file, Avi(w, h, kind, new List<byte[]> { Marked(w, h, null), Marked(w, h, null), Marked(w, h, kind + " 2"), Marked(w, h, kind + " 3") }));
                        IMFMediaSource source = OpenVideo(file);
                        IntPtr kept = Marshal.GetIUnknownForObject(source);   // to ask it afterwards whether it was let go of
                        try
                        {
                            var pictures = new SourceFrames(source, null);
                            var read = new List<string>();
                            var frame = new Frame();
                            string ended = null;
                            try
                            {
                                Check(pictures.Width == w && pictures.Height == h, kind + " " + w + ": read at " + pictures.Width + " x " + pictures.Height);
                                for (int i = 0; i < 10 && ended == null; i++)
                                {
                                    try
                                    {
                                        if (!pictures.Next(frame)) continue;
                                    }
                                    catch (CameraException x)
                                    {
                                        ended = x.Trouble + ": " + x.Message;
                                        continue;
                                    }
                                    Check(frame.Width == w && frame.Height == h && frame.Pixels.Length == w * h * 4, kind + " " + w + ": a picture came out " + frame.Width + " x " + frame.Height);
                                    // red at the top, blue at the bottom, green at the left: not upside down, not mirrored
                                    byte[] p = frame.Pixels;
                                    int top = (3 * w + w / 2) * 4, bottom = ((h - 3) * w + w / 2) * 4, left = (h / 2 * w + 3) * 4, right = (h / 2 * w + w - 4) * 4;
                                    Check(p[top + 2] > 200 && p[top] < 60 && p[bottom] > 200 && p[bottom + 2] < 60 && p[left + 1] > 200 && p[left + 2] < 60 && p[right + 1] < 230 && p[right + 1] > 160,
                                        kind + " " + w + ": the picture isn't the right way up (top " + p[top + 2] + "/" + p[top + 1] + "/" + p[top] + ", bottom " + p[bottom + 2] + "/" + p[bottom + 1] + "/" + p[bottom] + ", left " + p[left + 2] + "/" + p[left + 1] + "/" + p[left] + ")");
                                    byte[] grays = null;
                                    grays = CameraScan.Grays(frame, ref grays, out int gw, out int gh);
                                    FoundCode code = Reader.ReadFrame(grays, gw, gh, "the camera", 0);
                                    if (code != null) read.Add(code.Text);
                                }
                            }
                            finally
                            {
                                pictures.Dispose();
                            }
                            Check(read.SequenceEqual(new[] { kind + " 2", kind + " 3" }), kind + " " + w + ": the codes read were " + string.Join(", ", read));
                            Check(ended == "Failed: the camera stopped sending pictures", kind + " " + w + ": the end of the pictures: " + ended);
                            var again = (IMFMediaSource)Marshal.GetObjectForIUnknown(kept);
                            int after = again.GetCharacteristics(out _);
                            MF.Release(again);
                            Check(after == Shutdown, kind + " " + w + ": the source wasn't shut down when it was let go of (0x" + after.ToString("X8") + ")");
                        }
                        finally
                        {
                            Marshal.Release(kept);
                        }
                    }
                if (lacking.Count > 0) throw new SkipException(string.Join("; ", lacking) + ", so those videos weren't tried (the others passed)");
            }
            finally
            {
                MF.Stop();
            }
        }

        // Media Foundation's reader stopped from the thread pool, as closing the window stops it, with every
        // core kept busy twice over (as on a PC busy with other work), so that Media Foundation's own threads,
        // which hear the stop for the reader, wait their turn. Waiting for a picture that never comes (a camera
        // of the tests' own that starts and then sends nothing): ReadSample returns at once with an error, Next
        // goes on throwing, and only then is the camera shut down, on the camera's thread (shut down first, it
        // would lose the error with the rest of its events, and ReadSample would wait for good); stopped once,
        // twice, before its first read, and with the error reaching the reader late. One whose reader never
        // hears it is shut down all the same, SourceFrames.ShutDownAfterMs after the stop. Then reading a long
        // video: Next throws at once instead of reading on, the source is shut down, Dispose then lets go of
        // the rest (each wrapper once, nothing used after), and a stop after Dispose does nothing; ten times,
        // the stop landing at different moments, some while ReadSample is under way.
        static void MediaFoundationReaderStoppedMidRead()
        {
            if (!StartMediaFoundation()) throw new SkipException("Windows' media features (Media Foundation) aren't on this machine");
            bool stuck = false;
            BusyCores busy = null;
            try
            {
                busy = new BusyCores();
                var shutAfter = new List<long>();   // ms from each stop to the camera shut down

                // A silent camera on a thread of its own, as the camera's, its reader waiting for a picture (or,
                // `early`, about to ask for its first), stopped from the thread pool (`twice`: two stops at once)
                void Silent(string what, int lateMs = 0, bool deaf = false, bool early = false, bool twice = false)
                {
                    SilentCamera silent = null;
                    SourceFrames waiting = null;
                    Exception said = null, wrong = null;
                    bool after = false;
                    long stop = 0, returned = 0;
                    var made = new ManualResetEvent(false);
                    var go = new ManualResetEvent(!early);   // early: the first read waits for the stop
                    var reader = new Thread(() =>
                    {
                        try
                        {
                            silent = new SilentCamera { LateMs = lateMs, Deaf = deaf };
                            waiting = new SourceFrames(silent, null);
                            made.Set();
                            go.WaitOne(5000);
                            var frame = new Frame();
                            try
                            {
                                waiting.Next(frame);
                            }
                            catch (CameraException x)
                            {
                                said = x;
                            }
                            returned = System.Diagnostics.Stopwatch.GetTimestamp();
                            try
                            {
                                waiting.Next(frame);
                            }
                            catch (CameraException)
                            {
                                after = true;   // and from then on
                            }
                            waiting.Dispose();
                            busy.Off();
                        }
                        catch (Exception x)
                        {
                            wrong = x;
                        }
                        finally
                        {
                            made.Set();
                        }
                    }) { IsBackground = true, Name = "pretend camera thread" };
                    reader.SetApartmentState(ApartmentState.MTA);
                    reader.Start();
                    Check(made.WaitOne(10000) && wrong == null && waiting != null, what + ": Media Foundation's reader didn't start on the silent camera: " + wrong);
                    if (!early)
                    {
                        Pump(() => silent.Asked > 0, 5000);
                        Pump(() => false, 100);
                        Check(silent.Asked > 0 && reader.IsAlive, what + ": the reader didn't wait for a picture from the silent camera: " + (wrong?.ToString() ?? said?.Message));
                    }
                    busy.On();   // from the stop until the camera is shut down
                    stop = System.Diagnostics.Stopwatch.GetTimestamp();
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        waiting.Interrupt();
                        if (twice) waiting.Interrupt();
                        go.Set();
                    });
                    stuck = !reader.Join(5000);
                    busy.Off();
                    long Ms(long at) => at == 0 ? -1 : (at - stop) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                    long ended = Ms(returned), shut = Ms(silent.ShutDownAt);
                    Check(!stuck, what + ": the reader was still waiting for its picture 5 s after it was stopped" + (shut >= 0 ? ", the camera shut down " + shut + " ms after the stop" : ""));
                    Check(wrong == null && said != null && said.Message.EndsWith("(error 0xC00D3E85)") && after,
                        what + ": " + (wrong?.ToString() ?? said?.Message) + (after ? "" : "; the next read didn't throw"));
                    if (deaf)
                    {
                        Check(shut >= SourceFrames.ShutDownAfterMs - 50 && shut < SourceFrames.ShutDownAfterMs + 3000,
                            what + ": the camera was shut down " + (shut < 0 ? "never" : shut + " ms after the stop") + ", not " + SourceFrames.ShutDownAfterMs + " ms after (before then, the reader could still hear the stop)");
                        return;
                    }
                    Check(ended < lateMs + 2000, what + ": ReadSample returned " + ended + " ms after the stop");
                    Check(shut >= ended, what + ": the camera was shut down " + (shut < 0 ? "never" : shut + " ms after the stop, before ReadSample returned (" + ended + " ms): its error could still have been on its way to the reader"));
                    if (lateMs == 0) shutAfter.Add(shut);
                }

                Silent("waiting for a picture");
                Silent("waiting for a picture, stopped twice", twice: true);
                Silent("stopped before its first read", early: true);
                Silent("its error reaching the reader 200 ms late", lateMs: 200);
                int giveUp = SourceFrames.ShutDownAfterMs;
                SourceFrames.ShutDownAfterMs = 300;
                try
                {
                    Silent("a reader that never hears the stop", deaf: true);
                }
                finally
                {
                    SourceFrames.ShutDownAfterMs = giveUp;
                }

                const int length = 400;
                string file = Path.Combine(Work, "camera-long.avi");
                Directory.CreateDirectory(Work);
                File.WriteAllBytes(file, Avi(64, 48, "RGB32", Enumerable.Repeat(Marked(64, 48, null), length).ToList()));
                long videoWorst = 0;   // ms from a stop to the video let go of, at worst
                for (int round = 0; round < 10; round++)
                {
                    int stopAfter = 2 + 2 * round, read = 0, readAfter = 0, shutDown = 0;
                    bool paced = round % 2 == 0;   // a picture every 2 ms, or as fast as they come
                    Exception ended = null, trouble = null;
                    SourceFrames frames = null;
                    long stop = 0, letGo = 0;
                    var going = new ManualResetEvent(false);
                    var stopped = new ManualResetEvent(false);
                    var camera = new Thread(() =>
                    {
                        try
                        {
                            IMFMediaSource source = OpenVideo(file);
                            IntPtr kept = Marshal.GetIUnknownForObject(source);   // to ask it afterwards whether it was shut down
                            try
                            {
                                frames = new SourceFrames(source, null);
                                var frame = new Frame();
                                try
                                {
                                    while (true)
                                    {
                                        if (!frames.Next(frame)) continue;
                                        if (stopped.WaitOne(0)) readAfter++;
                                        if (++read == stopAfter) going.Set();
                                        if (paced) Thread.Sleep(2);
                                    }
                                }
                                catch (CameraException x)
                                {
                                    ended = x;
                                }
                                finally
                                {
                                    frames.Dispose();
                                    letGo = System.Diagnostics.Stopwatch.GetTimestamp();
                                    busy.Off();
                                }
                                var again = (IMFMediaSource)Marshal.GetObjectForIUnknown(kept);
                                shutDown = again.GetCharacteristics(out _);
                                MF.Release(again);
                            }
                            finally
                            {
                                Marshal.Release(kept);
                            }
                        }
                        catch (Exception x)
                        {
                            trouble = x;
                        }
                        finally
                        {
                            going.Set();
                        }
                    }) { IsBackground = true, Name = "pretend camera thread" };
                    camera.SetApartmentState(ApartmentState.MTA);
                    camera.Start();
                    Check(going.WaitOne(5000), "round " + round + ": the video didn't start");
                    if (trouble is SkipException) throw trouble;
                    Check(trouble == null && frames != null, "round " + round + ": " + trouble);
                    busy.On();   // from the stop until the video is shut down
                    stop = System.Diagnostics.Stopwatch.GetTimestamp();
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        frames.Interrupt();
                        stopped.Set();
                    });
                    stuck = !camera.Join(5000);
                    busy.Off();
                    Check(!stuck, "round " + round + ": the reader went on waiting after it was stopped");
                    Check(trouble == null, "round " + round + ": " + trouble);
                    Check(ended != null && read < length && readAfter <= 1, "round " + round + ": " + read + " of " + length + " pictures read, " + readAfter + " of them after the stop, then " + (ended?.Message ?? "nothing"));
                    Check(shutDown == Shutdown, "round " + round + ": the video wasn't shut down (0x" + shutDown.ToString("X8") + ")");
                    videoWorst = Math.Max(videoWorst, (letGo - stop) * 1000 / System.Diagnostics.Stopwatch.Frequency);
                    frames.Interrupt();   // after Dispose: nothing to stop, nothing used
                }
                Console.WriteLine("      (with " + busy.Threads + " threads busy: the silent camera shut down " + shutAfter.Min() + " to " + shutAfter.Max() + " ms after its stop, the video let go of within " + videoWorst + " ms)");
            }
            finally
            {
                busy?.Dispose();
                if (!stuck) MF.Stop();   // (not under a reader that's still waiting)
            }
        }

        // Every core kept busy twice over from On to Off, as on a PC busy with other work: the threads the
        // tests watch, Media Foundation's and the thread pool's then wait their turn, as they do there. Its
        // threads end when it's disposed.
        sealed class BusyCores : IDisposable
        {
            public readonly int Threads = 2 * Environment.ProcessorCount;
            readonly List<Thread> spinning = new List<Thread>();
            readonly ManualResetEvent on = new ManualResetEvent(false);
            volatile bool done;
            double sum;

            public BusyCores()
            {
                for (int i = 0; i < Threads; i++)
                {
                    var spin = new Thread(() =>
                    {
                        double x = 1;
                        while (on.WaitOne() && !done)
                            for (int k = 0; k < 100000; k++) x = Math.Sqrt(x + k);
                        sum += x;   // (so the sums aren't left out as unused)
                    }) { IsBackground = true, Name = "busy core" };
                    spin.Start();
                    spinning.Add(spin);
                }
            }

            public void On() => on.Set();
            public void Off() => on.Reset();

            public void Dispose()
            {
                done = true;
                on.Set();
                foreach (Thread spin in spinning) spin.Join();
            }
        }

        [DllImport("mfplat.dll")] static extern int MFCreateEventQueue(out IMFMediaEventQueue queue);
        [DllImport("mfplat.dll")] static extern int MFCreateStreamDescriptor(int id, int count, IntPtr[] types, out IMFStreamDescriptor descriptor);
        [DllImport("mfplat.dll")] static extern int MFCreatePresentationDescriptor(int count, IntPtr[] streams, out IMFPresentationDescriptor descriptor);

        // A camera that starts and then never sends a picture, as a real one can stop sending without
        // saying so: a Media Foundation source of the tests' own, one stream of 640 × 480 RGB32, its events
        // kept in Media Foundation's own queues as a camera's are. It answers the reader's every request
        // for a picture with nothing.
        sealed class SilentCamera : IMFMediaSource, IMFMediaEventGenerator
        {
            readonly IMFMediaEventQueue events;
            readonly IMFPresentationDescriptor offer;
            readonly SilentStream stream;
            public volatile bool ShutDown;
            public long ShutDownAt;   // when it was first shut down (Stopwatch.GetTimestamp), or 0
            public int Asked;   // pictures the reader has asked for

            // LateMs: an error put in its events from outside (as a stop puts one) reaches the reader that
            // long after, as when a busy PC keeps Media Foundation's threads waiting; shut down before then,
            // the camera loses it with the rest of its events, as Media Foundation's own queues do. Deaf: it
            // never reaches the reader while the camera runs (Windows' trouble); once the camera is shut down
            // it does, and the events close at the next shutdown (Media Foundation would leave the reader
            // waiting for good: this lets the test end)
            public int LateMs;
            public bool Deaf;
            Timer late;
            bool held;
            int heldType, heldStatus;
            Guid heldKind;

            public SilentCamera()
            {
                Check(MFCreateEventQueue(out events) == 0, "no event queue");
                Check(MF.MFCreateMediaType(out IMFMediaType type) == 0, "no media type");
                Guid major = MF.MajorType, video = MF.Video, sub = MF.Subtype, rgb32 = MF.Rgb32, size = MF.FrameSize, rate = MF.FrameRate, stride = MF.DefaultStride;
                Check(type.SetGUID(ref major, ref video) == 0 && type.SetGUID(ref sub, ref rgb32) == 0 && type.SetUINT64(ref size, (640L << 32) | 480) == 0 &&
                      type.SetUINT64(ref rate, (30L << 32) | 1) == 0 && type.SetUINT32(ref stride, 640 * 4) == 0, "the silent camera's kind of picture");
                IntPtr kind = Marshal.GetComInterfaceForObject(type, typeof(IMFMediaType));
                try
                {
                    Check(MFCreateStreamDescriptor(0, 1, new[] { kind }, out IMFStreamDescriptor one) == 0, "the silent camera's stream");
                    Check(one.GetMediaTypeHandler(out IMFMediaTypeHandler handler) == 0 && handler.SetCurrentMediaType(kind) == 0, "the silent camera's kind of picture");
                    IntPtr described = Marshal.GetComInterfaceForObject(one, typeof(IMFStreamDescriptor));
                    Check(MFCreatePresentationDescriptor(1, new[] { described }, out offer) == 0 && offer.SelectStream(0) == 0, "the silent camera's streams");
                    stream = new SilentStream(this, described);
                    MF.Release(handler);
                    MF.Release(one);
                }
                finally
                {
                    Marshal.Release(kind);
                    MF.Release(type);
                }
            }

            public int GetEvent(int flags, out IntPtr mediaEvent) => events.GetEvent(flags, out mediaEvent);
            public int BeginGetEvent(IntPtr callback, IntPtr state) => events.BeginGetEvent(callback, state);
            public int EndGetEvent(IntPtr result, out IntPtr mediaEvent) => events.EndGetEvent(result, out mediaEvent);

            // only ever from outside: the stop's error, with no value
            public int QueueEvent(int type, ref Guid extendedType, int status, IntPtr value)
            {
                if (Deaf || LateMs > 0)
                {
                    if (ShutDown) return Shutdown;
                    heldType = type;
                    heldKind = extendedType;
                    heldStatus = status;
                    held = Deaf;
                    if (LateMs > 0)
                        late = new Timer(_ =>
                        {
                            try
                            {
                                events.QueueEventParamVar(heldType, ref heldKind, heldStatus, IntPtr.Zero);   // MF_E_SHUTDOWN once shut down: lost
                            }
                            catch (Exception)
                            {
                            }
                        }, null, LateMs, Timeout.Infinite);
                    return 0;
                }
                return events.QueueEventParamVar(type, ref extendedType, status, value);
            }

            public int GetCharacteristics(out int characteristics)
            {
                characteristics = 1;   // MFMEDIASOURCE_IS_LIVE, as a camera is
                return ShutDown ? Shutdown : 0;
            }

            public int CreatePresentationDescriptor(out IntPtr descriptor)
            {
                descriptor = IntPtr.Zero;
                return ShutDown ? Shutdown : offer.Clone(out descriptor);
            }

            // started: the stream is new and started, and so is the camera (MENewStream, MEStreamStarted, MESourceStarted)
            public int Start(IntPtr descriptor, IntPtr timeFormat, IntPtr startPosition)
            {
                if (ShutDown) return Shutdown;
                Guid none = Guid.Empty;
                IntPtr unknown = Marshal.GetComInterfaceForObject(stream, typeof(IMFMediaStream));
                int hr = events.QueueEventParamUnk(205, ref none, 0, unknown);
                Marshal.Release(unknown);
                IntPtr time = Marshal.AllocCoTaskMem(24);   // a PROPVARIANT: VT_I8, 0
                for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(time, i, 0);
                Marshal.WriteInt16(time, 20);
                if (hr == 0) hr = stream.Events.QueueEventParamVar(202, ref none, 0, time);
                if (hr == 0) hr = events.QueueEventParamVar(201, ref none, 0, time);
                Marshal.FreeCoTaskMem(time);
                return hr;
            }

            public int Stop() => ShutDown ? Shutdown : 0;
            public int Pause() => ShutDown ? Shutdown : 0;

            int IMFMediaSource.Shutdown()
            {
                Interlocked.CompareExchange(ref ShutDownAt, System.Diagnostics.Stopwatch.GetTimestamp(), 0);
                ShutDown = true;
                if (held)
                {
                    held = false;   // deaf: the stop's error reaches the reader now; the events close next time
                    return events.QueueEventParamVar(heldType, ref heldKind, heldStatus, IntPtr.Zero);
                }
                stream.Events.Shutdown();
                events.Shutdown();
                return 0;
            }

            sealed class SilentStream : IMFMediaStream, IMFMediaEventGenerator
            {
                public readonly IMFMediaEventQueue Events;
                readonly SilentCamera camera;
                readonly IntPtr descriptor;

                public SilentStream(SilentCamera camera, IntPtr descriptor)
                {
                    this.camera = camera;
                    this.descriptor = descriptor;
                    Check(MFCreateEventQueue(out Events) == 0, "no event queue");
                }

                public int GetEvent(int flags, out IntPtr mediaEvent) => Events.GetEvent(flags, out mediaEvent);
                public int BeginGetEvent(IntPtr callback, IntPtr state) => Events.BeginGetEvent(callback, state);
                public int EndGetEvent(IntPtr result, out IntPtr mediaEvent) => Events.EndGetEvent(result, out mediaEvent);
                public int QueueEvent(int type, ref Guid extendedType, int status, IntPtr value) => Events.QueueEventParamVar(type, ref extendedType, status, value);

                public int GetMediaSource(out IntPtr source)
                {
                    source = Marshal.GetComInterfaceForObject(camera, typeof(IMFMediaSource));
                    return 0;
                }

                public int GetStreamDescriptor(out IntPtr described)
                {
                    Marshal.AddRef(descriptor);
                    described = descriptor;
                    return 0;
                }

                // a picture asked for, and never sent
                public int RequestSample(IntPtr token)
                {
                    if (camera.ShutDown) return Shutdown;
                    Interlocked.Increment(ref camera.Asked);
                    return 0;
                }
            }
        }

        // what has events (a camera, its streams), which Media Foundation's reader listens to
        [ComImport, Guid("2cd0bd52-bcd5-4b89-b62c-eadc0c031e7d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaEventGenerator
        {
            [PreserveSig] int GetEvent(int flags, out IntPtr mediaEvent);
            [PreserveSig] int BeginGetEvent(IntPtr callback, IntPtr state);
            [PreserveSig] int EndGetEvent(IntPtr result, out IntPtr mediaEvent);
            [PreserveSig] int QueueEvent(int type, [In] ref Guid extendedType, int status, IntPtr value);
        }

        // a camera's stream of pictures (IMFMediaEventGenerator's 4 methods first)
        [ComImport, Guid("d182108f-4ec6-443f-aa42-a71106ec825f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaStream
        {
            [PreserveSig] int GetEvent(int flags, out IntPtr mediaEvent);
            [PreserveSig] int BeginGetEvent(IntPtr callback, IntPtr state);
            [PreserveSig] int EndGetEvent(IntPtr result, out IntPtr mediaEvent);
            [PreserveSig] int QueueEvent(int type, [In] ref Guid extendedType, int status, IntPtr value);
            [PreserveSig] int GetMediaSource(out IntPtr source);
            [PreserveSig] int GetStreamDescriptor(out IntPtr descriptor);
            [PreserveSig] int RequestSample(IntPtr token);
        }

        // Media Foundation's own queue of events, as a camera keeps one for itself and each stream
        [ComImport, Guid("36f846fc-2256-48b6-b58e-e2b638316581"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaEventQueue
        {
            [PreserveSig] int GetEvent(int flags, out IntPtr mediaEvent);
            [PreserveSig] int BeginGetEvent(IntPtr callback, IntPtr state);
            [PreserveSig] int EndGetEvent(IntPtr result, out IntPtr mediaEvent);
            [PreserveSig] int QueueEvent(IntPtr mediaEvent);
            [PreserveSig] int QueueEventParamVar(int type, [In] ref Guid extendedType, int status, IntPtr value);
            [PreserveSig] int QueueEventParamUnk(int type, [In] ref Guid extendedType, int status, IntPtr unknown);
            [PreserveSig] int Shutdown();
        }

        // what a camera offers: its streams (IMFAttributes' 30 methods first, none of them called here)
        [ComImport, Guid("03cb2711-24d7-4db6-a17f-f3a7a479a536"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFPresentationDescriptor
        {
            [PreserveSig] int A00(); [PreserveSig] int A01(); [PreserveSig] int A02(); [PreserveSig] int A03(); [PreserveSig] int A04(); [PreserveSig] int A05();
            [PreserveSig] int A06(); [PreserveSig] int A07(); [PreserveSig] int A08(); [PreserveSig] int A09(); [PreserveSig] int A10(); [PreserveSig] int A11();
            [PreserveSig] int A12(); [PreserveSig] int A13(); [PreserveSig] int A14(); [PreserveSig] int A15(); [PreserveSig] int A16(); [PreserveSig] int A17();
            [PreserveSig] int A18(); [PreserveSig] int A19(); [PreserveSig] int A20(); [PreserveSig] int A21(); [PreserveSig] int A22(); [PreserveSig] int A23();
            [PreserveSig] int A24(); [PreserveSig] int A25(); [PreserveSig] int A26(); [PreserveSig] int A27(); [PreserveSig] int A28(); [PreserveSig] int A29();
            [PreserveSig] int GetStreamDescriptorCount(out int count);
            [PreserveSig] int GetStreamDescriptorByIndex(int index, out int selected, out IntPtr descriptor);
            [PreserveSig] int SelectStream(int index);
            [PreserveSig] int DeselectStream(int index);
            [PreserveSig] int Clone(out IntPtr copy);
        }

        // one stream as offered (IMFAttributes' 30 methods first, none of them called here)
        [ComImport, Guid("56c03d9c-9dbb-45f5-ab4b-d80f47c05938"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFStreamDescriptor
        {
            [PreserveSig] int A00(); [PreserveSig] int A01(); [PreserveSig] int A02(); [PreserveSig] int A03(); [PreserveSig] int A04(); [PreserveSig] int A05();
            [PreserveSig] int A06(); [PreserveSig] int A07(); [PreserveSig] int A08(); [PreserveSig] int A09(); [PreserveSig] int A10(); [PreserveSig] int A11();
            [PreserveSig] int A12(); [PreserveSig] int A13(); [PreserveSig] int A14(); [PreserveSig] int A15(); [PreserveSig] int A16(); [PreserveSig] int A17();
            [PreserveSig] int A18(); [PreserveSig] int A19(); [PreserveSig] int A20(); [PreserveSig] int A21(); [PreserveSig] int A22(); [PreserveSig] int A23();
            [PreserveSig] int A24(); [PreserveSig] int A25(); [PreserveSig] int A26(); [PreserveSig] int A27(); [PreserveSig] int A28(); [PreserveSig] int A29();
            [PreserveSig] int GetStreamIdentifier(out int id);
            [PreserveSig] int GetMediaTypeHandler(out IMFMediaTypeHandler handler);
        }

        // a stream's kinds of picture, and the one it sends
        [ComImport, Guid("e93dcf6c-4b07-4e1e-8123-aa16ed6eadf5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaTypeHandler
        {
            [PreserveSig] int IsMediaTypeSupported(IntPtr type, out IntPtr closest);
            [PreserveSig] int GetMediaTypeCount(out int count);
            [PreserveSig] int GetMediaTypeByIndex(int index, out IntPtr type);
            [PreserveSig] int SetCurrentMediaType(IntPtr type);
            [PreserveSig] int GetCurrentMediaType(out IntPtr type);
            [PreserveSig] int GetMajorType(out Guid major);
        }

        // a 6 × 4 picture written into a buffer (rows top first or bottom first), copied out top row first
        static void CopiesTheRightWayUp(bool twoD, bool bottomUp)
        {
            const int w = 6, h = 4;
            string what = (twoD ? "a 2D buffer" : "a plain buffer") + (bottomUp ? ", bottom row first" : ", top row first");
            IMFMediaBuffer buffer;
            if (twoD) Check(MFCreate2DMediaBuffer(w, h, 22, bottomUp ? 1 : 0, out buffer) == 0, "no 2D buffer");   // D3DFMT_X8R8G8B8, as RGB32
            else Check(MFCreateMemoryBuffer(w * h * 4, out buffer) == 0, "no buffer");
            int stride = (bottomUp ? -1 : 1) * w * 4;
            IMFSample sample = null;
            try
            {
                if (twoD)
                {
                    var rows = (IMF2DBuffer)buffer;
                    Check(rows.Lock2D(out IntPtr top, out int pitch) == 0, what + ": Lock2D");
                    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) Marshal.WriteInt32(top + y * pitch + x * 4, y * 16 + x);
                    rows.Unlock2D();
                }
                else
                {
                    Check(buffer.Lock(out IntPtr start, out _, out _) == 0, what + ": Lock");
                    IntPtr top = stride < 0 ? start + -stride * (h - 1) : start;
                    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) Marshal.WriteInt32(top + y * stride + x * 4, y * 16 + x);
                    buffer.Unlock();
                    buffer.SetCurrentLength(w * h * 4);
                }
                Check(MFCreateSample(out sample) == 0 && sample.AddBuffer(buffer) == 0, "no sample");
                var frame = new Frame();
                SourceFrames.Copy(sample, w, h, stride, frame);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        Check(BitConverter.ToInt32(frame.Pixels, (y * w + x) * 4) == y * 16 + x, what + ": the pixel at " + x + "," + y + " came out from somewhere else");
                buffer.SetCurrentLength(w * 4);
                try
                {
                    if (!twoD) SourceFrames.Copy(sample, w, h, stride, frame);
                    Check(twoD, what + ": a picture cut short was copied");
                }
                catch (CameraException x)
                {
                    Check(x.Message.Contains("cut short"), what + ": a picture cut short: " + x.Message);
                }
            }
            finally
            {
                MF.Release(sample);
                MF.Release(buffer);
            }
        }

        // The whole way, as a webcam goes: the camera window's read loop starting Media Foundation's reader
        // on its own thread (on an MJPEG video instead of a camera), reading its code, letting it go
        static void CameraWindowReadsAVideo()
        {
            if (!StartMediaFoundation()) throw new SkipException("Windows' media features (Media Foundation) aren't on this machine");
            string lacks = Lacks("MJPG");
            MF.Stop();
            if (lacks != null) throw new SkipException(lacks + ", and the video is MJPEG as webcams send it");
            string file = Path.Combine(Work, "camera-video.avi");
            Directory.CreateDirectory(Work);
            var pictures = new List<byte[]> { Marked(640, 480, null), Marked(640, 480, null) };
            pictures.AddRange(Enumerable.Repeat(Marked(640, 480, "https://b0r3d.org/video"), 40));
            File.WriteAllBytes(file, Avi(640, 480, "MJPG", pictures));
            var video = new VideoCamera(file);
            CameraWindow window = StartCamera(video);
            WaitForCamera(window, () => window.IsClosed && window.CameraLetGo);
            Check(window.Code?.Text == "https://b0r3d.org/video", "the video's code: " + Show(window.Code?.Text) + (window.Problem != null ? " (" + window.Problem.Words + ": " + window.Problem.Message + ")" : ""));
            Check(video.ShutDown == Shutdown && video.Threads.Count == 1 && !video.Threads.Contains(Thread.CurrentThread.ManagedThreadId),
                "the video wasn't let go of on the camera's own thread (0x" + video.ShutDown.ToString("X8") + ", " + video.Threads.Count + " threads)");
        }

        // The window's read loop on Media Foundation's reader, stopped as the window stops it, with every core
        // busy twice over from each stop until the camera stopped is shut down: a camera that starts and never
        // sends is left for a video by choosing the video, the video for that camera again, and the window is
        // closed while it waits for a picture. Each time, the camera's read ends at once, the camera is shut
        // down on its own thread, and the next one starts (shut down by the stop itself, as before, the
        // camera could lose the stop and leave its thread waiting for good, the window stuck on it).
        static void CameraWindowStopsMediaFoundationsReader()
        {
            if (!StartMediaFoundation()) throw new SkipException("Windows' media features (Media Foundation) aren't on this machine");
            MF.Stop();   // (the window's cameras start their own)
            string file = Path.Combine(Work, "camera-chosen.avi");
            Directory.CreateDirectory(Work);
            File.WriteAllBytes(file, Avi(64, 48, "RGB32", Enumerable.Repeat(Marked(64, 48, null), 200).ToList()));
            var cameras = new VideoCamera(file, silent: true);
            var busy = new BusyCores();
            CameraWindow window = null;
            try
            {
                window = StartCamera(cameras);
                // the silent camera started `count` times, the last of them waiting for a picture
                bool Waiting(int count)
                {
                    lock (cameras.Silent) return cameras.Silent.Count == count && cameras.Silent[count - 1].Asked > 0;
                }
                long Ms(long from, long to) => to == 0 ? -1 : (to - from) * 1000 / System.Diagnostics.Stopwatch.Frequency;

                WaitForCamera(window, () => Waiting(1), 5000);
                Check(Waiting(1) && window.Problem == null, "the silent camera's reader didn't wait for a picture: " + window.Problem?.Message);
                Pump(() => false, 100);
                busy.On();
                long chosen = System.Diagnostics.Stopwatch.GetTimestamp();
                window.CameraBox.SelectedIndex = 1;
                Pump(() => cameras.Silent[0].ShutDown, 5000);
                busy.Off();
                WaitForCamera(window, () => window.Pictures > 0 || window.Problem != null, 5000);
                Check(window.Showing?.Name == "the video" && window.Pictures > 0 && window.Problem == null,
                    "choosing the video while the silent camera waited: " + (window.Problem?.Message ?? window.Pictures + " pictures from " + window.Showing?.Name + " after 5 s"));
                long left = Ms(chosen, cameras.Silent[0].ShutDownAt);

                busy.On();
                window.CameraBox.SelectedIndex = 0;
                Pump(() => Waiting(2), 5000);
                busy.Off();
                Check(Waiting(2) && window.Problem == null, "choosing the silent camera again while the video was read: " + (window.Problem?.Message ?? "it didn't start in 5 s"));
                Pump(() => false, 100);

                busy.On();
                long closed = System.Diagnostics.Stopwatch.GetTimestamp();
                window.Close();
                Pump(() => window.CameraLetGo, 5000);
                busy.Off();
                Check(window.CameraLetGo, "the window closed while the silent camera waited, and its thread was still waiting 5 s later");
                Check(cameras.Silent.All(s => s.ShutDown) && cameras.ShutDown == Shutdown && window.Problem == null,
                    "a camera wasn't shut down (the video: 0x" + cameras.ShutDown.ToString("X8") + "), or a message was shown: " + window.Problem?.Message);
                Console.WriteLine("      (with " + busy.Threads + " threads busy: the silent camera shut down " + left + " ms after the video was chosen, " + Ms(closed, cameras.Silent[1].ShutDownAt) + " ms after the window closed)");
            }
            finally
            {
                busy.Dispose();
                if (window != null && !window.IsClosed) window.Close();
            }
        }

        // a video file as the only camera, a picture every 20 ms as a camera sends them; with `silent`, a
        // camera that starts and never sends (SilentCamera) listed before it, started anew each time it's chosen
        sealed class VideoCamera : ICameras
        {
            readonly string file;
            readonly bool silent;
            IntPtr kept;
            bool started;
            public int ShutDown;
            public readonly HashSet<int> Threads = new HashSet<int>();
            public readonly List<SilentCamera> Silent = new List<SilentCamera>();   // each one started, in turn

            public VideoCamera(string file, bool silent = false)
            {
                this.file = file;
                this.silent = silent;
            }

            public List<CameraInfo> Find()
            {
                Threads.Add(Thread.CurrentThread.ManagedThreadId);
                started = MF.Start() == 0;
                var list = new List<CameraInfo> { new CameraInfo { Name = "the video", Id = file } };
                if (silent) list.Insert(0, new CameraInfo { Name = "the silent camera", Id = "silent" });
                return list;
            }

            public IFrameSource Open(CameraInfo camera)
            {
                Threads.Add(Thread.CurrentThread.ManagedThreadId);
                if (camera.Id == "silent")
                {
                    var quiet = new SilentCamera();
                    lock (Silent) Silent.Add(quiet);
                    return new SourceFrames(quiet, null);
                }
                IMFMediaSource source = OpenVideo(file);
                kept = Marshal.GetIUnknownForObject(source);
                return new Paced(new SourceFrames(source, null), Threads);
            }

            public void Dispose()
            {
                Threads.Add(Thread.CurrentThread.ManagedThreadId);
                if (kept != IntPtr.Zero)
                {
                    var again = (IMFMediaSource)Marshal.GetObjectForIUnknown(kept);
                    ShutDown = again.GetCharacteristics(out _);
                    MF.Release(again);
                    Marshal.Release(kept);
                    kept = IntPtr.Zero;
                }
                if (started) MF.Stop();
            }

            sealed class Paced : IFrameSource
            {
                readonly IFrameSource inner;
                readonly HashSet<int> threads;

                public Paced(IFrameSource inner, HashSet<int> threads)
                {
                    this.inner = inner;
                    this.threads = threads;
                }

                public bool Next(Frame into)
                {
                    threads.Add(Thread.CurrentThread.ManagedThreadId);
                    Thread.Sleep(20);
                    return inner.Next(into);
                }

                public void Interrupt() => inner.Interrupt();

                public void Dispose() => inner.Dispose();
            }
        }
    }
}
