using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using B0r3dQR.Printing;

namespace B0r3dQR
{
    // Printing: the code alone at its exact size, or a sheet of copies; or a batch's codes on sheets (or one a
    // page). A preview of each page, then Windows' Print dialog (the printer and its paper).
    public partial class PrintWindow : Window
    {
        readonly IList<PrintCode> codes;     // one code (copies of it) or a batch's
        readonly bool batch;
        readonly string unit;                // "mm" or "in", for saying the size
        readonly PrintOptions options = new PrintOptions();
        FixedDocument document;
        int page;
        bool showing = true, perPageSet;

        // for the tests: the paper, instead of the default printer's
        internal Size? PaperOverride;
        internal FixedDocument Document => document;

        // Windows' Print dialog; the tests stand in for it (as a print service that isn't answering) and hear what the
        // window would say in a message box
        internal Func<PrintDialog, bool?> AskPrinter = dialog => dialog.ShowDialog();
        internal Action<string> Warned;
        internal bool Shrunk { get; private set; }

        // `code` alone or as copies; `sizeNote` says what decides its size
        internal PrintWindow(PrintCode code, string sizeNote, string unit) : this(new List<PrintCode> { code }, false, sizeNote, unit) { }

        // a batch's codes
        internal PrintWindow(IList<PrintCode> codes, string sizeNote, string unit) : this(codes, true, sizeNote, unit) { }

        PrintWindow(IList<PrintCode> codes, bool batch, string sizeNote, string unit)
        {
            InitializeComponent();
            this.codes = codes;
            this.batch = batch;
            this.unit = unit;
            WhatLine.Text = !batch ? "The code from the Make tab, at its exact size." : codes.Count == 1 ? "The batch's one code, at its own size." : "The batch's " + codes.Count.ToString("N0") + " codes, each at its own size.";
            AloneText.Text = batch ? "_One a page, in the middle" : "_Alone, in the middle of the page";
            SheetText.Text = batch ? "On _sheets, as many to a page as fit" : "A _sheet of copies";
            if (batch) SheetChoice.IsChecked = true;
            SizeLine.Text = sizeNote;
            SourceInitialized += (s, e) => Theme.TitleBar(this);
            showing = false;
            Loaded += (s, e) => Lay();
        }

        // the paper of the default printer (A4, or Letter where the country uses it, when there's no printer; the
        // Print dialog gives a blank ticket itself when the print service isn't answering)
        Size Paper()
        {
            if (PaperOverride.HasValue) return PaperOverride.Value;
            try
            {
                Size? printer = PaperOf(new PrintDialog().PrintTicket);
                fallback = printer == null;
                return printer ?? Fallback;
            }
            catch (Exception x) when (PrinterTrouble(x))
            {
                fallback = true;
                return Fallback;
            }
        }

        // What Windows' printing throws when it can't go on: a printer's or the print service's trouble (the Print
        // Spooler stopped, say), a printer that's gone, an error from Windows
        static bool PrinterTrouble(Exception x) => x is PrintSystemException || x is InvalidOperationException || x is System.ComponentModel.Win32Exception;

        // why printing went wrong, in words: the print service not answering is explained; anything else is in Windows'
        // own words
        static string Why(Exception x) => x is PrintServerException
            ? "Windows' print service isn't answering. If the Print Spooler service is stopped, start it (or restart the PC), then try again."
            : x.Message;

        void Warn(string message)
        {
            if (Warned != null) Warned(message);
            else MessageBox.Show(this, message, Program.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        bool fallback;        // no printer said what paper it has

        static Size Fallback => RegionInfo.CurrentRegion.IsMetric ? PrintSheet.A4 : PrintSheet.Letter;

        // a print ticket's paper as it's turned, or null when the printer doesn't say
        static Size? PaperOf(PrintTicket ticket)
        {
            if (ticket?.PageMediaSize?.Width == null || ticket.PageMediaSize.Height == null) return null;
            double w = ticket.PageMediaSize.Width.Value, h = ticket.PageMediaSize.Height.Value;
            bool turned = ticket.PageOrientation == PageOrientation.Landscape || ticket.PageOrientation == PageOrientation.ReverseLandscape;
            return turned ? new Size(Math.Max(w, h), Math.Min(w, h)) : new Size(w, h);
        }

        void OnLayout(object sender, RoutedEventArgs e)
        {
            if (showing || codes == null) return;   // (XAML sets the first choice while the window is built)
            Lay();
        }

        void OnCopies(object sender, EventArgs e)
        {
            if (showing || codes == null) return;
            Lay();
        }

        // the pages, laid out again for the choices and the paper (`chosen`: the paper picked in the Print dialog)
        internal void Lay(Size? chosen = null)
        {
            if (chosen.HasValue)
            {
                paperSize = chosen;
                paperChosen = true;
            }
            options.Page = (paperSize ?? (paperSize = Paper())).Value;
            options.Sheet = SheetChoice.IsChecked == true;
            options.Captions = CaptionsCheck.IsChecked == true;
            options.CutMarks = MarksCheck.IsChecked == true;
            CopiesRow.Visibility = !batch && options.Sheet ? Visibility.Visible : Visibility.Collapsed;
            CaptionsCheck.Visibility = MarksCheck.Visibility = options.Sheet ? Visibility.Visible : Visibility.Collapsed;
            IList<PrintCode> these = codes;
            if (!batch)
            {
                int fit = PrintSheet.PerPage(codes[0].Size, options);
                if (!perPageSet)
                {
                    // a page full of copies, to start with
                    showing = true;
                    CopiesBox.Value = fit;
                    showing = false;
                    perPageSet = true;
                }
                CopiesNote.Text = "copies (" + fit.ToString("N0") + " fit on a page)";
                if (options.Sheet) these = Enumerable.Repeat(codes[0], Math.Max(1, (int)CopiesBox.Value)).ToList();
            }
            document = PrintSheet.Pages(these, options, out bool shrunk);
            Shrunk = shrunk;
            ShrunkLine.Text = "Bigger than the paper leaves room for, so it's printed smaller to fit: make it smaller on the Make tab for its exact size.";
            ShrunkLine.Visibility = shrunk ? Visibility.Visible : Visibility.Collapsed;
            PaperLine.Text = PrintSheet.Describe(options.Page, unit) +
                             (PaperOverride.HasValue || paperChosen ? "" : fallback ? " (no printer said what paper it has)" : ", the default printer's paper") +
                             ". Print… lets you choose the printer and its paper.";
            page = Math.Min(page, Math.Max(0, document.Pages.Count - 1));
            ShowPage();
        }

        Size? paperSize;      // the paper the pages are laid out for: the default printer's until one is chosen
        bool paperChosen;

        void OnPrevious(object sender, RoutedEventArgs e)
        {
            if (document == null) return;
            page = Math.Max(0, page - 1);
            ShowPage();
        }

        void OnNext(object sender, RoutedEventArgs e)
        {
            if (document == null) return;
            page = Math.Min(document.Pages.Count - 1, page + 1);
            ShowPage();
        }

        void OnStageSize(object sender, SizeChangedEventArgs e) => ShowPage();

        // the page drawn small enough to fit the room beside the choices
        void ShowPage()
        {
            if (document == null || document.Pages.Count == 0) return;
            PageLine.Text = "Page " + (page + 1).ToString("N0") + " of " + document.Pages.Count.ToString("N0");
            PreviousButton.IsEnabled = page > 0;
            NextButton.IsEnabled = page < document.Pages.Count - 1;
            double roomWidth = Math.Max(100, Stage.ActualWidth - 24), roomHeight = Math.Max(100, Stage.ActualHeight - 24);
            if (Stage.ActualWidth < 1) roomWidth = roomHeight = 420;   // not laid out yet
            double k = Math.Min(roomWidth / options.Page.Width, roomHeight / options.Page.Height);
            var fixedPage = (FixedPage)document.Pages[page].Child;
            fixedPage.Measure(options.Page);
            fixedPage.Arrange(new Rect(options.Page));
            fixedPage.UpdateLayout();
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var picture = new RenderTargetBitmap(Math.Max(1, (int)(options.Page.Width * k * scale)), Math.Max(1, (int)(options.Page.Height * k * scale)), 96 * k * scale, 96 * k * scale, PixelFormats.Pbgra32);
            picture.Render(fixedPage);
            picture.Freeze();
            PagePicture.Source = picture;
            PagePicture.Width = options.Page.Width * k;
            PagePicture.Height = options.Page.Height * k;
        }

        // Windows' Print dialog; then the pages again for the paper chosen, and off to the printer
        void OnPrint(object sender, RoutedEventArgs e)
        {
            var dialog = new PrintDialog { UserPageRangeEnabled = false };
            bool? chosen;
            try
            {
                chosen = AskPrinter(dialog);
            }
            catch (Exception x) when (PrinterTrouble(x))
            {
                // the print service not answering shows once Print is clicked in the dialog, as it reaches the printer
                Warn(x is PrintServerException ? "Couldn't print:\n\n" + Why(x) : "Windows' Print dialog didn't open:\n\n" + x.Message);
                return;
            }
            if (chosen != true) return;
            try
            {
                Lay(PaperOf(dialog.PrintTicket) ?? options.Page);
                dialog.PrintDocument(document.DocumentPaginator, batch ? "b0r3d QR batch" : "b0r3d QR code");
                StatusLine.Text = LongText.Cap("Sent to " + dialog.PrintQueue.FullName + ": " + (document.Pages.Count == 1 ? "1 page." : document.Pages.Count.ToString("N0") + " pages."));
                if (System.Windows.Automation.Peers.AutomationPeer.ListenerExists(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged))
                    System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(StatusLine)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
            }
            catch (Exception x) when (PrinterTrouble(x))   // a cancelled print job (PrintingCanceledException) too
            {
                Warn("Couldn't print:\n\n" + Why(x));
            }
        }

        void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
