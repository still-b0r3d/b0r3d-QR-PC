using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32;

namespace B0r3dQR.Tests
{
    // a test that can't run here (and says why) rather than one that failed
    sealed class SkipException : Exception
    {
        public SkipException(string why) : base(why) { }
    }

    sealed class CheckException : Exception
    {
        public CheckException(string why) : base(why) { }
    }

    // Runs every test and prints a PASS, FAIL or SKIP line for each. It never shows a dialog:
    // whatever goes wrong ends as a line, and a watchdog stops the run after 90 seconds.
    //   b0r3dQRTests.exe                  run the tests
    //   b0r3dQRTests.exe --shots <folder> draw the windows to PNG files (see Shots.cs)
    //   b0r3dQRTests.exe --only <words>   run only the tests whose names have all of those words in them
    static partial class TestMain
    {
        static int passed, failed, skipped;
        static string only;

        // this exe is in bin\tests\; the one-file exe is in bin\
        static readonly string TestsFolder = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        static readonly string BinFolder = Path.GetDirectoryName(TestsFolder);
        static readonly string RepoFolder = Path.GetDirectoryName(BinFolder);
        static string OneExe => Path.Combine(BinFolder, "b0r3dQR.exe");

        // the tests' own scratch folder, next to this exe (never Temp: a test checks nothing lands there)
        static readonly string Work = Path.Combine(TestsFolder, "work");

        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Console.WriteLine("FAIL  crashed: " + e.ExceptionObject); Environment.Exit(3); };
            using (new Timer(_ => { Console.WriteLine("FAIL  still running after 90 seconds, so stopped (watchdog)"); Environment.Exit(4); }, null, 90000, Timeout.Infinite))
            {
                // the program's resources and styles, as when it runs; a window that throws is a FAIL line
                var app = new App();
                app.InitializeComponent();
                App.OnProblem = e => { Console.WriteLine("FAIL  a window threw: " + e); Environment.Exit(3); };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Fresh(Work);
                DataFolder.Reset(Path.Combine(Work, "data"));

                if (args.Length == 2 && args[0] == "--shots")
                {
                    int shot = TakeShots(args[1]);
                    TryDelete(Work);
                    return shot;
                }
                if (args.Length == 2 && args[0] == "--only") only = args[1];
                else if (args.Length > 0)
                {
                    Console.WriteLine("b0r3dQRTests.exe [--shots <folder>] [--only <words from the tests' names>]");
                    return 2;
                }

                List<string> footprint = Footprint();

                Run("version: the one-file exe carries the version and the build", ExeVersion);
                Run("one file: the exe runs on its own, with ZXing inside, per-monitor DPI on, and its windows load", ExeRunsOnItsOwn);
                Run("never goes online: no networking, web, XML, media, native or COM way out in the exe", ExeNeverGoesOnline);
                Run("safety flags: the one-file exe keeps Windows' address randomisation and DEP", ExeSafetyFlags);
                Run("file dialogs: Save, Open and folder are told not to add to Recent items", FileDialogsStayOutOfRecent);
                Run("icon: the cup at every size, small ones as bitmaps, all of them pictures", ExeIcon);
                Run("icon check: tells static from a picture", StaticIsCaught);
                Run("build.cmd: plain ASCII with CRLF line endings", BuildCmdIsPlain);

                Run("codes: every sample reads back exactly, at each level and quiet zone", EveryCodeReadsBack);
                Run("codes: plain ASCII is packed small; other text is one UTF-8 part, no ECI marker", CompactAndUtf8);
                Run("codes: too much text is refused with a plain message, and a huge paste at once", TooMuchText);
                Run("codes: the same modules as the website's own encoder, for 42 of its codes and all 8 masks", SameModulesAsTheWebsite);
                Run("codes: with the UTF-8 marker, the same modules as zint made for the website, for 58 codes", SameMarkedModulesAsTheWebsite);
                Run("codes: text split into digit, capital and byte parts by the website's rules", SplitsLikeTheWebsite);
                Run("barcodes: the website's SVG, character for character, or its message, for 624 barcodes of all 13 types", BarcodesLikeTheWebsite);
                Run("barcodes: Data Matrix, Aztec and PDF417 modules as zint made them for the website, for 252 codes", BarcodeMatricesLikeTheWebsite);
                Run("barcodes: every type scans back as itself and is made again into the same barcode", BarcodesScanAndMakeAgain);
                Run("barcodes: PNG, SVG and PDF in the website's colours and sizes, read back", BarcodeFilesLikeTheWebsite);
                Run("templates: every template writes the website's text, byte for byte (68 of its own)", TemplatesWriteLikeTheWebsite);
                Run("templates: texts read back into the website's template and fields (48 of its own)", TemplatesReadLikeTheWebsite);
                Run("templates: encodeURIComponent, URLSearchParams, parseFloat, toFixed and trim as in a browser", JavaScriptPiecesLikeABrowser);
                Run("templates: the forms' fields, messages, examples and number fields as on the website", TemplateFormsLikeTheWebsite);
                Run("recent codes: what the list says, 20 kept, the same text replaced, big pictures left out, a damaged file left alone", RecentCodesLikeTheWebsite);
                Run("design: all 72 dot, ring and centre shape combinations read back", EveryShapeReadsBack);
                Run("design: the website's shapes (circles, its radii, its fallbacks for unknown types)", ShapesLikeTheWebsite);
                Run("design: the logo's room by the website's sums; L raised to Q; a web logo left out with a note", LogoRoomLikeTheWebsite);
                Run("design: frames laid out as the website exports them (234 × 271.6, 239 × 278 px)", FrameLikeTheWebsite);
                Run("design: print size, module size and scan distance as the website works them out", PrintSizeLikeTheWebsite);
                Run("design: PNG, JPG, PDF and text files at the website's sizes, each read back", ExportsLikeTheWebsite);
                Run("design: the website's design files open, ours write all its keys, refusals in its words", DesignFilesLikeTheWebsite);
                Run("design: colour, quiet-zone and data checks with the website's results", ChecksLikeTheWebsite);
                Run("designer: a preset from the list, a frame on, off and back, Randomize", PresetsAndFrameInTheWindow);
                Run("designer: a logo pasted, kept as the website keeps uploads, and removed", LogoInTheWindow);
                Run("designer: a design file opens into the window with its text, exactly as saved", DesignFilesInTheWindow);
                Run("designer: the design is kept for next time, never its text", DesignKeptWithoutText);
                Run("designer: the data checks' fixes, one after another", DataChecksInTheWindow);
                Run("designer: print size in the window, and inches kept as the same length", PrintSizeInTheWindow);
                Run("designer: a number typed with a point means the same in any language", NumbersInAnyLanguage);
                Run("designer: data templates in the window, filled from the text, with their messages, Save and examples", TemplatesInTheWindow);
                Run("designer: Recent codes: kept on save or copy, the note once, open, delete, Clear all asks, off", RecentCodesInTheWindow);
                Run("designer: barcodes: every type from the list, checked, its own options, the website's messages, QR code again", BarcodesInTheWindow);
                Run("designer: barcode settings kept for next time; a recent barcode opens with its type, text and look", BarcodeSettingsKept);
                Run("shapes: exactly the dark modules, with the quiet zone around them", ShapesMatchTheCode);
                Run("picture size: whole pixels per module, as near the size asked for as can be", PictureSizes);

                Run("PNG: every module is exactly its pixels, and it reads back from the file", PngIsExact);
                Run("SVG: valid, the right size, its path is exactly the code, and it reads back", SvgIsExact);
                Run("saving: the whole file or nothing, never a temporary file left behind or someone's .tmp lost", SavingIsSafe);
                Run("copy and drag: a picture, a PNG, never the cloud clipboard, and for drag a file for folders", CopyAndDragData);
                Run("file names: a link's site, a Wi-Fi network, a kind of data; never a password or secret", FileNames);

                Run("data folder: next to the exe, or where --data says; nothing kept in Temp or AppData", DataFolderPlaces);
                Run("data folder: --data into a folder of other things touches only b0r3d QR's own files", OtherFilesUntouched);
                Run("settings: saved and read back; garbled lines fall back to the defaults", SettingsRoundTrip);
                Run("settings: a folder that can't be written to is caught at start and said", SettingsWhereTheyCantBeKept);
                Run("settings: a save that fails once (a held file) doesn't stop the next", SaveFailsOnlyOnce);
                Run("command line: --data, --selftest, and unknown options", CommandLines);

                Run("reading: every code in a picture, each with what it is, its type and where it was", ReadsEveryCode);
                Run("reading: light codes on dark backgrounds", ReadsLightOnDark);
                Run("reading: a small code on a 4K screen, in good time", ReadsSmallCodeOnABigScreen);
                Run("reading: Aztec and Data Matrix codes away from the middle, among letters", ReadsCodesAwayFromTheMiddle);
                Run("reading: a barcode on its side", ReadsSidewaysBarcode);
                Run("reading: noise and blank pictures give nothing, not made-up codes", NothingThereSaysSo);
                Run("reading: tiny and flat pictures are just pictures with no code", OddPicturesHaveNoCode);
                Run("reading: a code on a see-through background reads as on white", SeeThroughCountsAsWhite);
                Run("pictures: PNG, JPEG, BMP, GIF and TIFF pages; a broken or missing file says so", ReadsPictureFiles);
                Run("folders: a line for every picture (codes, no code, couldn't open), a summary; Cancel stops it", ReadsFolders);
                Run("clipboard and drops: a PNG, a bitmap or files; Ctrl+V leaves text to the text boxes", ReadsCopiedAndDropped);
                Run("screens: every screen in real pixels, numbered left to right, and a picture of one", ScreensInRealPixels);
                Run("snip: a box drawn at 150 % lands on the right real pixels; a click reads the screen", SnipBoxesInRealPixels);
                Run("snip: closing an overlay or switching programs always brings b0r3d QR back", OverlaysAlwaysComeBack);
                Run("kinds: what each code is, never a password or secret, product numbers never phones", KindsInWords);
                Run("links: only web links open, after a question, and odd ones come with warnings", LinksAskFirst);
                Run("scan tab: a dropped picture is read and listed, named for screen readers; Make this code again", ScanTabShowsAndMakesAgain);
                Run("make again: the scanned text exactly; GS1 data in round brackets; no GS1 QR codes", MakeAgainKeepsTheText);
                Run("make again: a scanned barcode as its own type with its text exactly, then QR codes again", BarcodeMadeAgain);

                Run("look: the three colour sets match, and every colour used is in them", ThemesMatch);
                Run("window: builds in light and dark with no binding errors", WindowWithoutBindingErrors);
                Run("window: the tabs' typewriter font stays on the tabs", TabsKeepTheirLookToThemselves);
                Run("window: typing makes the code, and the check says it reads back", WindowMakesTheCode);
                Run("window: empty and too-long text explain themselves, with nothing to save", WindowExplainsProblems);
                Run("window: error correction, quiet zone and picture size change the code", WindowOptions);
                Run("window: the preview is pixel for pixel at 150 %", WindowSharpAt150);
                Run("window: the options and the look are kept, what was typed never is", WindowKeepsSettings);
                Run("window: Save, Copy and drag first catch up with typing and half-typed numbers", ExportsCatchUp);
                Run("keyboard: no two things share an access key, and every menu line has one", AccessKeysAreUnique);
                Run("screen readers: the number boxes have their names", NumberBoxesHaveNames);
                Run("look: the text boxes' right-click menu is b0r3d QR's own, so it's themed", TextMenusAreThemed);
                Run("about: the version, the build, the settings folder and every licence", AboutShowsItAll);

                Run("footprint: nothing written to AppData, Temp or the registry", () => FootprintUnchanged(footprint));

                Console.WriteLine();
                if (only != null && passed + failed + skipped == 0)
                {
                    Console.WriteLine("No test has all of the words \"" + only + "\" in its name.");
                    TryDelete(Work);
                    return 2;
                }
                Console.WriteLine(passed + " passed, " + failed + " failed, " + skipped + " skipped");
                TryDelete(Work);
                return failed == 0 ? 0 : 1;
            }
        }

        static void Run(string name, Action test)
        {
            if (only != null && !only.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(word => name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)) return;
            try
            {
                test();
                passed++;
                Console.WriteLine("PASS  " + name);
            }
            catch (SkipException e)
            {
                skipped++;
                Console.WriteLine("SKIP  " + name + ": " + e.Message);
            }
            catch (CheckException e)
            {
                failed++;
                Console.WriteLine("FAIL  " + name + ": " + e.Message);
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine("FAIL  " + name + ": " + e);
            }
        }

        static void Check(bool ok, string why)
        {
            if (!ok) throw new CheckException(why);
        }

        // lets the window's timers and queued work run, until `done` or `ms` milliseconds pass
        static void Pump(Func<bool> done, int ms)
        {
            var watch = Stopwatch.StartNew();
            while (!done() && watch.ElapsedMilliseconds < ms)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(5);
            }
        }

        static void Fresh(string folder)
        {
            TryDelete(folder);
            Directory.CreateDirectory(folder);
        }

        static void TryDelete(string folder)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // still in use; the next run clears it
            }
        }

        static string Short(string text) => text == null ? "(nothing)" : text.Length <= 40 ? text : text.Substring(0, 37) + "...";

        // Anything named after b0r3d (b0r3dQR, b0r3d QR, b0r3d.org: .NET would name settings after
        // the company) two levels into AppData and Temp, and in the registry's Software keys: the
        // program must add none of it (its folder is the only place it writes). What Windows itself
        // records (the Save dialog's last folder) isn't named after us, and the tests never open the
        // dialog; SaveDialogStaysOutOfRecent checks its Recent-items option instead.
        static List<string> Footprint()
        {
            var seen = new List<string>();
            foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.GetTempPath() })
                FindOurs(folder, 2, seen);
            foreach (string key in new[] { "Software", @"Software\Classes" })
                using (RegistryKey software = Registry.CurrentUser.OpenSubKey(key))
                    if (software != null) seen.AddRange(software.GetSubKeyNames().Where(Ours).Select(k => @"HKCU\" + key + @"\" + k));
            seen.Sort(StringComparer.OrdinalIgnoreCase);
            return seen.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        static void FindOurs(string folder, int depth, List<string> seen)
        {
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    if (Ours(Path.GetFileName(entry))) seen.Add(entry);
                    else if (depth > 1 && Directory.Exists(entry) && (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0) FindOurs(entry, depth - 1, seen);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
            }
        }

        static bool Ours(string name) => name.IndexOf("b0r3d", StringComparison.OrdinalIgnoreCase) >= 0;

        static void FootprintUnchanged(List<string> before)
        {
            List<string> added = Footprint().Except(before, StringComparer.OrdinalIgnoreCase).ToList();
            Check(added.Count == 0, "new: " + string.Join(", ", added));
        }
    }
}
