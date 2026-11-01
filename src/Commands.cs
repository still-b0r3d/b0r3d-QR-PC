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
        public static readonly RoutedUICommand About = Make("About b0r3d QR", nameof(About), new KeyGesture(Key.F1));

        static RoutedUICommand Make(string text, string name, KeyGesture key)
        {
            return new RoutedUICommand(text, name, typeof(Commands), new InputGestureCollection { key });
        }
    }
}
