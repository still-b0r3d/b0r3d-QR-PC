using System.Windows.Input;

namespace B0r3dQR
{
    // The menu commands with keyboard shortcuts. (Undo, Cut, Copy, Paste and Select all are
    // Windows' own, for the text box.)
    static class Commands
    {
        public static readonly RoutedUICommand SavePng = Make("Save PNG…", nameof(SavePng), new KeyGesture(Key.S, ModifierKeys.Control));
        public static readonly RoutedUICommand SaveSvg = Make("Save SVG…", nameof(SaveSvg), new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand CopyPicture = Make("Copy picture", nameof(CopyPicture), new KeyGesture(Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand SaveJpg = Make("Save JPG…", nameof(SaveJpg), null);
        public static readonly RoutedUICommand SavePdf = Make("Save PDF…", nameof(SavePdf), null);
        public static readonly RoutedUICommand SaveText = Make("Save as text…", nameof(SaveText), null);
        public static readonly RoutedUICommand CopySvg = Make("Copy as SVG", nameof(CopySvg), null);
        public static readonly RoutedUICommand CopyText = Make("Copy as text", nameof(CopyText), null);
        public static readonly RoutedUICommand OpenDesign = Make("Open design…", nameof(OpenDesign), null);
        public static readonly RoutedUICommand SaveDesign = Make("Save design…", nameof(SaveDesign), null);
        public static readonly RoutedUICommand Batch = Make("Batch export…", nameof(Batch), new KeyGesture(Key.B, ModifierKeys.Control));
        public static readonly RoutedUICommand Print = Make("Print…", nameof(Print), new KeyGesture(Key.P, ModifierKeys.Control));
        public static readonly RoutedUICommand ScanScreen = Make("Scan the screen", nameof(ScanScreen), new KeyGesture(Key.R, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand OpenPicture = Make("Open picture…", nameof(OpenPicture), new KeyGesture(Key.O, ModifierKeys.Control));
        public static readonly RoutedUICommand PastePicture = Make("Paste picture", nameof(PastePicture), new KeyGesture(Key.V, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand ScanFolder = Make("Scan a folder…", nameof(ScanFolder), new KeyGesture(Key.O, ModifierKeys.Control | ModifierKeys.Shift));
        public static readonly RoutedUICommand About = Make("About b0r3d QR", nameof(About), new KeyGesture(Key.F1));

        static RoutedUICommand Make(string text, string name, KeyGesture key)
        {
            var keys = new InputGestureCollection();
            if (key != null) keys.Add(key);
            return new RoutedUICommand(text, name, typeof(Commands), keys);
        }
    }
}
