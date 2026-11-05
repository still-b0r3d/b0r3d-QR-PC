using System.Windows.Input;
using B0r3dQR.Camera;

namespace B0r3dQR
{
    // Scan → Scan with the camera: the camera's window; the first code it reads becomes the Scan tab's
    // list, as the website's scan page shows the code its camera read.
    public partial class MainWindow
    {
        void OnScanCamera(object sender, ExecutedRoutedEventArgs e)
        {
            if (Scanning) return;
            var window = new CameraWindow(new Webcams()) { Owner = this };
            window.ShowDialog();
            ShowCameraCode(window);
        }

        // after the camera's window has closed: its code as the list, or the list as it was
        internal void ShowCameraCode(CameraWindow window)
        {
            if (window.Code == null) return;   // cancelled, or no camera could be read: nothing changes here
            Tabs.SelectedItem = ScanTab;
            found.Clear();
            found.Add(window.Code);
            ShowSummary("1 code from " + window.Code.Source, "Text");
        }
    }
}
