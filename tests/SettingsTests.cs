using System;
using System.IO;
using B0r3dQR.Qr;

namespace B0r3dQR.Tests
{
    static partial class TestMain
    {
        static void DataFolderPlaces()
        {
            string data = DataFolder.Path;
            try
            {
                const string exe = @"C:\Portable\b0r3dQR";
                DataFolder.Choose(null, exe);
                Check(DataFolder.Path == exe + @"\b0r3dQR data" && DataFolder.CanKeep, "the default is " + DataFolder.Path);
                DataFolder.Choose(@"D:\PortableApps\b0r3dQRPortable\Data\settings", exe);
                Check(DataFolder.Path == @"D:\PortableApps\b0r3dQRPortable\Data\settings" && DataFolder.CanKeep, "--data D:\\... gave " + DataFolder.Path);
                DataFolder.Choose("my settings", exe);
                Check(DataFolder.Path == exe + @"\my settings", "a relative --data is from the exe's folder, not " + DataFolder.Path);
                DataFolder.Choose("\"quoted\"", exe);
                Check(DataFolder.Path == exe + @"\quoted", "quotes should go: " + DataFolder.Path);
                Environment.SetEnvironmentVariable("B0R3DQR_TEST_DIR", @"E:\here");
                DataFolder.Choose(@"%B0R3DQR_TEST_DIR%\x", exe);
                Check(DataFolder.Path == @"E:\here\x", "environment variables should expand: " + DataFolder.Path);
                DataFolder.Choose("bad|name", exe);
                Check(!DataFolder.CanKeep, "an impossible folder name should mean settings aren't kept");

                // the inside of a zip opened in Explorer runs from Temp: keep nothing there
                DataFolder.Choose(null, Path.Combine(Path.GetTempPath(), "Temp1_b0r3dQR.zip"));
                Check(!DataFolder.CanKeep && DataFolder.WhyNotKept.Contains("temporary"), "running from Temp should keep nothing");
                Check(!new Settings().Save(), "settings were saved into Temp");
                // nor in AppData, where email and chat programs keep attachments
                foreach (Environment.SpecialFolder appData in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
                {
                    DataFolder.Choose(null, Path.Combine(Environment.GetFolderPath(appData), @"Microsoft\Windows\INetCache\Content.Outlook\ABC123"));
                    Check(!DataFolder.CanKeep, "running from " + appData + " should keep nothing");
                }
                // but --data may point anywhere: that's the user's own choice
                DataFolder.Choose(Path.Combine(Path.GetTempPath(), "chosen"), exe);
                Check(DataFolder.CanKeep, "a folder given with --data should be used as given");
            }
            finally
            {
                Environment.SetEnvironmentVariable("B0R3DQR_TEST_DIR", null);
                DataFolder.Reset(data);
            }
        }

        static void SettingsRoundTrip()
        {
            string data = DataFolder.Path;
            string folder = Path.Combine(Work, "settings-round-trip");
            DataFolder.Reset(folder);
            try
            {
                var s = new Settings { Look = Look.Dark, Level = QrLevel.H, QuietZone = 7, PictureSize = 2400, WindowWidth = 1234, WindowHeight = 777, Maximized = true };
                Check(s.Save(), "couldn't save: " + DataFolder.WhyNotKept);
                Settings back = Settings.Load();
                Check(back.Look == Look.Dark && back.Level == QrLevel.H && back.QuietZone == 7 && back.PictureSize == 2400, "read back: " + back.ToText());
                Check(back.WindowWidth == 1234 && back.WindowHeight == 777 && back.Maximized, "the window read back as " + back.ToText());
                string file = Path.Combine(folder, Settings.FileName);
                Check(!File.Exists(file + ".tmp"), "a .tmp was left behind");

                File.WriteAllText(file, "look=purple\nlevel=Z\nquiet_zone=999\npicture_size=-5\nwindow=abc\nnonsense\n=x\n");
                Settings odd = Settings.Load();
                Check(odd.Look == Look.Windows && odd.Level == QrLevel.Q, "garbled look and level should be the defaults: " + odd.ToText());
                Check(odd.QuietZone == Settings.MaxQuietZone && odd.PictureSize == Settings.MinPictureSize, "numbers out of range should be brought in: " + odd.ToText());
                Check(odd.WindowWidth == 1000 && odd.WindowHeight == 660 && !odd.Maximized, "a garbled window size should be the default: " + odd.ToText());

                File.WriteAllText(file, "look=2\nlevel=3\n");
                Settings numbers = Settings.Load();
                Check(numbers.Look == Look.Windows && numbers.Level == QrLevel.Q, "numbers aren't names: " + numbers.ToText());
            }
            finally
            {
                DataFolder.Reset(data);
            }
        }

        static void SettingsWhereTheyCantBeKept()
        {
            string data = DataFolder.Path;
            string notAFolder = Path.Combine(Work, "not-a-folder");
            File.WriteAllText(notAFolder, "x");
            DataFolder.Reset(Path.Combine(notAFolder, "data"));
            try
            {
                // at start: found at once, and said
                DataFolder.CheckWritable();
                Check(!DataFolder.CanKeep && DataFolder.WhyNotKept.Contains("can't be kept"), "a folder that can't be made should be caught at start: " + DataFolder.WhyNotKept);
                Check(!new Settings().Save(), "saving there should fail");
                Check(Settings.Load().Level == QrLevel.Q, "loading from there should give the defaults");

                // a save that fails later says so, without stopping the ones after it
                DataFolder.Reset(Path.Combine(notAFolder, "data"));
                Check(!new Settings().Save(), "saving inside a file should fail");
                Check(DataFolder.CanKeep && DataFolder.LastProblem != null && DataFolder.LastProblem.Contains("couldn't be saved"), "a failed save should say why: " + DataFolder.LastProblem);
            }
            finally
            {
                DataFolder.Reset(data);
            }
        }

        // a file held for a moment (a sync app, antivirus) fails that save, not the next one
        static void SaveFailsOnlyOnce()
        {
            string data = DataFolder.Path;
            string folder = Path.Combine(Work, "held");
            Fresh(folder);
            DataFolder.Reset(folder);
            try
            {
                var settings = new Settings { QuietZone = 9 };
                Check(settings.Save(), "the first save failed: " + DataFolder.LastProblem);
                string file = Path.Combine(folder, Settings.FileName);
                using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    settings.QuietZone = 3;
                    Check(!settings.Save(), "a save over a held file should fail");
                    Check(DataFolder.CanKeep && DataFolder.LastProblem != null, "a held file should give a note, not stop saving");
                }
                Check(settings.Save() && DataFolder.LastProblem == null, "the save after the file was let go failed: " + DataFolder.LastProblem);
                Check(Settings.Load().QuietZone == 3, "the later save didn't land");
            }
            finally
            {
                DataFolder.Reset(data);
            }
        }

        // --data can point at a folder full of other things: b0r3d QR touches only its own names
        static void OtherFilesUntouched()
        {
            string data = DataFolder.Path;
            string shared = Path.Combine(Work, "shared");
            Fresh(shared);
            Directory.CreateDirectory(Path.Combine(shared, "temp", "photos"));
            File.WriteAllText(Path.Combine(shared, "temp", "photos", "keep.txt"), "mine");
            File.WriteAllText(Path.Combine(shared, "settings.ini"), "someone=else");
            Directory.CreateDirectory(Path.Combine(shared, "b0r3dQR drag"));
            File.WriteAllText(Path.Combine(shared, "b0r3dQR drag", "note.txt"), "not a picture");
            File.WriteAllBytes(Path.Combine(shared, "b0r3dQR drag", "old.png"), new byte[] { 1 });
            DataFolder.Reset(shared);
            try
            {
                MainWindow window = NewMain();  // clears the drag folder, as on start
                window.SaveSettings();
                Exports.ClearDragFolder();      // as on close
                Check(File.Exists(Path.Combine(shared, "temp", "photos", "keep.txt")), "a file in the folder's own temp folder was deleted");
                Check(File.ReadAllText(Path.Combine(shared, "settings.ini")) == "someone=else", "another program's settings.ini was changed");
                Check(File.Exists(Path.Combine(shared, "b0r3dQR drag", "note.txt")), "something other than a PNG was deleted from the drag folder");
                Check(!File.Exists(Path.Combine(shared, "b0r3dQR drag", "old.png")), "an old dragged PNG was left behind");
                Check(File.Exists(Path.Combine(shared, Settings.FileName)), "b0r3d QR's own settings weren't saved");
            }
            finally
            {
                DataFolder.Reset(data);
            }
        }

        static void CommandLines()
        {
            CommandLine none = CommandLine.Parse(new string[0]);
            Check(none.Problem == null && none.DataFolder == null && none.SelfTestReport == null, "no arguments");
            Check(CommandLine.Parse(new[] { "--data", @"D:\x y" }).DataFolder == @"D:\x y", "--data <folder>");
            Check(CommandLine.Parse(new[] { @"--data=D:\z" }).DataFolder == @"D:\z", "--data=<folder>");
            Check(CommandLine.Parse(new[] { "--DATA", "f" }).DataFolder == "f", "options aren't case-sensitive");
            Check(CommandLine.Parse(new[] { "--selftest", "r.txt" }).SelfTestReport == "r.txt", "--selftest <file>");
            Check(CommandLine.Parse(new[] { "--data" }).Problem != null, "--data with nothing after it");
            Check(CommandLine.Parse(new[] { "--frobnicate" }).Problem != null, "an unknown option");
            Check(CommandLine.Parse(new[] { "/?" }).Problem != null, "/?");
            Check(CommandLine.Parse(new[] { @"C:\some\file.png" }).Problem == null, "a file dropped on the exe is left alone");
        }
    }
}
