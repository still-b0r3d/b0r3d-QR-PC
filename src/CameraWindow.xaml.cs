using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Camera;
using B0r3dQR.Scan;
using Frame = B0r3dQR.Camera.Frame;   // (not WPF's Frame, which shows web pages)

namespace B0r3dQR
{
    // Scan → Scan with the camera, as the website's camera scanner: "Starting the camera…", then what
    // it sees with a frame to aim with, read a few times a second until a code turns up. That code goes
    // to the Scan tab, the camera stops and the window closes. A list of the cameras when there's more
    // than one; the website's messages when a camera can't be read; Cancel (Esc) stops it all.
    public partial class CameraWindow : Window
    {
        readonly CameraScan scan;
        WriteableBitmap picture;
        bool begun, closed, quiet;

        // the code read, once the window has closed (null: cancelled, or nothing could be read)
        internal FoundCode Code { get; private set; }
        // the camera being read, the cameras listed, and why none can be read (for the tests)
        internal CameraInfo Showing { get; private set; }
        internal List<CameraInfo> Cameras { get; private set; } = new List<CameraInfo>();
        internal CameraException Problem { get; private set; }
        internal bool IsClosed => closed;
        internal bool CameraLetGo { get; private set; }   // the camera's thread has ended
        internal int Pictures { get; private set; }       // pictures shown
        internal int Looks => scan.Looks;

        internal CameraWindow(ICameras cameras)
        {
            InitializeComponent();
            scan = new CameraScan(cameras, Dispatcher)
            {
                Listed = OnListed,
                Starting = OnStarting,
                Started = OnStarted,
                Picture = OnPicture,
                Found = OnFound,
                Failed = OnFailed,
                Ended = () => CameraLetGo = true,
            };
            SourceInitialized += (s, e) => Theme.TitleBar(this);
            Loaded += (s, e) => Begin();
            Closed += (s, e) =>
            {
                closed = true;
                scan.Stop();
            };
        }

        // starts the camera (once the window is on screen; the tests start it themselves)
        internal void Begin()
        {
            if (begun || closed) return;
            begun = true;
            scan.Start();
        }

        void OnListed(List<CameraInfo> cameras)
        {
            if (closed) return;
            Cameras = cameras;
            quiet = true;
            CameraBox.ItemsSource = cameras.Select(c => LongText.Cap(c.Name)).ToList();   // a camera calls itself anything
            CameraBox.SelectedIndex = cameras.Count > 0 ? 0 : -1;
            quiet = false;
            CameraRow.Visibility = cameras.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnCamera(object sender, SelectionChangedEventArgs e)
        {
            if (quiet || closed || CameraBox.SelectedIndex < 0) return;
            scan.Choose(CameraBox.SelectedIndex);
        }

        void OnStarting(CameraInfo camera)
        {
            if (closed) return;
            Showing = camera;
            Problem = null;
            PictureView.Source = picture = null;
            PictureView.RenderTransform = camera.Mirrored ? new ScaleTransform(-1, 1) : Transform.Identity;
            ShowState(starting: true);
        }

        void OnStarted(CameraInfo camera)
        {
            if (closed) return;
            ShowState(starting: false);
        }

        void OnPicture(Frame frame)
        {
            if (closed) return;
            if (picture == null || picture.PixelWidth != frame.Width || picture.PixelHeight != frame.Height)
            {
                picture = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
                PictureView.Source = picture;
            }
            picture.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);
            Pictures++;
        }

        // the first code: to the Scan tab, as the website hands it to its scan page (the camera's already off)
        void OnFound(FoundCode code)
        {
            if (closed) return;
            Code = code;
            Close();
        }

        void OnFailed(CameraException problem)
        {
            if (closed) return;
            Problem = problem;
            PictureView.Source = picture = null;
            ProblemText.Text = problem.Words;
            // Windows' side of it, smaller: "Windows didn't start USB Camera (error 0x80070005)." (with the camera's name, capped)
            string detail = LongText.Cap(problem.Message.Trim().TrimEnd('.'));
            DetailText.Text = detail.Length > 0 ? char.ToUpperInvariant(detail[0]) + detail.Substring(1) + "." : "";
            DetailText.Visibility = DetailText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowState(starting: false);
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.CreatePeerForElement(ProblemText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        // "Starting the camera…", the picture with its frame, or why there's none
        void ShowState(bool starting)
        {
            bool problem = Problem != null;
            StartingText.Visibility = starting && !problem ? Visibility.Visible : Visibility.Collapsed;
            ProblemPanel.Visibility = problem ? Visibility.Visible : Visibility.Collapsed;
            Veil.Visibility = Viewfinder.Visibility = !starting && !problem ? Visibility.Visible : Visibility.Collapsed;
            if (problem) Stage.SetResourceReference(Border.BackgroundProperty, "Stage");
            else Stage.Background = Brushes.Black;
            if (starting && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                UIElementAutomationPeer.CreatePeerForElement(StartingText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        // the frame to aim with, 15 % in from the top and bottom and 10 % from the sides, the rest dimmed
        void OnViewSize(object sender, SizeChangedEventArgs e)
        {
            double w = View.ActualWidth, h = View.ActualHeight;
            var inner = new Rect(w * 0.1, h * 0.15, w * 0.8, h * 0.7);
            Viewfinder.Margin = new Thickness(inner.Left, inner.Top, w - inner.Right, h - inner.Bottom);
            var veil = new GeometryGroup { FillRule = FillRule.EvenOdd };
            veil.Children.Add(new RectangleGeometry(new Rect(0, 0, w, h)));
            veil.Children.Add(new RectangleGeometry(inner, 12, 12));
            Veil.Data = veil;
        }

        void OnCancel(object sender, RoutedEventArgs e) => Close();

        // for the screenshots: the window as it is while reading `frame`, from one of `cameras`, with
        // no camera started
        internal void ShowForShots(Frame frame, List<CameraInfo> cameras)
        {
            OnListed(cameras);
            OnStarting(cameras[0]);
            OnStarted(cameras[0]);
            OnPicture(frame);
        }
    }
}
