using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace B0r3dQR
{
    public partial class App : Application
    {
        // The tests set this: a window that throws becomes a FAIL line instead of a message box.
        internal static Action<Exception> OnProblem;

        // Ctrl+C, Cut and Copy in any text box of any window (the data box, a code's text, a template's
        // fields) keep the text out of Windows' cloud clipboard, like Copy text does: it can be a Wi-Fi
        // password, an IBAN or someone's contact details
        static App()
        {
            EventManager.RegisterClassHandler(typeof(TextBoxBase), DataObject.CopyingEvent, new DataObjectCopyingEventHandler(OnTextCopying));
            // every text box caps the marks on each letter before it's laid out, so no scanned code,
            // opened file, paste or drop can end the program in WPF's text layout (Unicode.Capped)
            LongText.Guard();
        }

        static void OnTextCopying(object sender, DataObjectCopyingEventArgs e)
        {
            if (!e.IsDragDrop) Exports.KeepOffTheCloud(e.DataObject);
        }

        public App()
        {
            DispatcherUnhandledException += OnUnhandled;
        }

        void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            if (OnProblem != null)
            {
                OnProblem(e.Exception);
                return;
            }
            MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
