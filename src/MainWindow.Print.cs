using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using B0r3dQR.Design;
using B0r3dQR.Printing;

namespace B0r3dQR
{
    // File → Print…: the code showing, alone, as a sheet of copies, or on Avery label sheets, at the size its PDF has.
    public partial class MainWindow
    {
        void OnPrintCode(object sender, ExecutedRoutedEventArgs e)
        {
            CatchUp();
            PrintWindow window = PrintWindowFor();
            if (window == null) return;
            window.Owner = this;
            window.ShowDialog();
            SaveSettings();
        }

        // the Print window for the code showing (null when there's none); its caption on a sheet is what Recent
        // codes calls it (never a Wi-Fi password)
        internal PrintWindow PrintWindowFor()
        {
            if (!HasCode) return null;
            if (MakingBarcode)
            {
                Size size = PrintSheet.BarcodeSize(barcodeArt, look, barcodePrint);
                var barcode = new PrintCode
                {
                    Drawing = barcodeArt.ToDrawing(Css.Paint(look.Color), look.Transparent ? (Color?)null : Css.Paint(look.Background)),
                    Canvas = new Rect(0, 0, barcodeArt.W, barcodeArt.H),
                    Size = size,
                    Caption = barcodeExpected,
                };
                return new PrintWindow(barcode, PrintSheet.SizeNote(size, barcodePrint, false), barcodePrint.Unit, settings);
            }
            Size qrSize = PrintSheet.QrSize(art, print, wholePixels);
            RecentCodes.Describe(design.Data, out string label, out string title, out _);
            var qr = new PrintCode { Drawing = art.ToDrawing(), Canvas = new Rect(0, 0, art.W, art.H), Size = qrSize, Caption = title.Length > 0 ? title : label };
            return new PrintWindow(qr, PrintSheet.SizeNote(qrSize, print, false), print.Unit, settings);
        }
    }
}
