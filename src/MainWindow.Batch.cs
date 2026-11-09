using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using B0r3dQR.Barcodes;
using B0r3dQR.Batch;

namespace B0r3dQR
{
    // File → Batch export…, and a CSV dropped on the window: the Batch export window for the code type showing.
    public partial class MainWindow
    {
        void OnBatch(object sender, ExecutedRoutedEventArgs e) => OpenBatch(null);

        // The Batch export window, with the Make tab's design (QR codes) or barcode type and settings as they are now,
        // the CSV at `csvPath` read into it if there is one; shown and waited for unless `show` is false (the tests).
        internal BatchWindow OpenBatch(string csvPath, bool show = true)
        {
            CatchUp();
            var kind = new BatchKind
            {
                Barcode = codeType,
                Design = design.Clone(),
                Print = MakingBarcode ? barcodePrint.Clone() : print.Clone(),
                WholePixels = wholePixels,
                Look = look.Clone(),
            };
            var window = new BatchWindow(kind, MakingBarcode ? BarcodeFormats.Get(codeType.Value).Label + " barcodes" : "QR codes", settings);
            if (csvPath != null) window.LoadCsv(csvPath);
            if (show)
            {
                window.Owner = this;
                window.ShowDialog();
                SaveSettings();
            }
            return window;
        }

        // one CSV file being dragged in (for a batch)
        static string CsvFileIn(IDataObject data)
        {
            try
            {
                if (data != null && data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths &&
                    paths.Length == 1 && string.Equals(Path.GetExtension(paths[0]), ".csv", StringComparison.OrdinalIgnoreCase))
                    return paths[0];
            }
            catch (COMException)
            {
            }
            return null;
        }
    }
}
