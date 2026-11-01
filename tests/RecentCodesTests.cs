using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using B0r3dQR.Design;

namespace B0r3dQR.Tests
{
    // Recent codes, by the website's rules, in a data folder of the tests' own.
    static partial class TestMain
    {
        // runs `test` with the data folder in a fresh folder of its own, and puts it back after
        static void InFreshDataFolder(string name, Action test)
        {
            string was = DataFolder.Path;
            string folder = Path.Combine(Work, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            DataFolder.Reset(folder);
            try
            {
                test();
            }
            finally
            {
                DataFolder.Reset(was);
                RecentWindow.LastFailed = false;
            }
        }

        static QrDesign Coded(string data)
        {
            QrDesign d = Presets.Apply("Plain", new QrDesign());
            d.Data = data;
            return d;
        }

        // what the list says about a code, and which codes are kept
        static void RecentCodesLikeTheWebsite()
        {
            void Says(string data, string label, string title, bool sensitive)
            {
                RecentCodes.Describe(data, out string l, out string t, out bool s);
                Check(l == label && t == title && s == sensitive, Show(data) + ": " + l + " / " + t + " / " + s + " (the website: " + label + " / " + title + " / " + sensitive + ")");
            }
            Says("https://b0r3d.org", "URL", "https://b0r3d.org", false);
            Says("WIFI:T:WPA;S:home;P:secret;;", "WiFi", "home", true);
            Says("WIFI:T:nopass;S:open;;", "WiFi", "open", false);
            Says("BEGIN:VCARD\nVERSION:3.0\nN:Cat;Rukus;;;\nFN:Rukus Cat\nEND:VCARD", "Contact Card", "Rukus Cat", false);
            Says("BEGIN:VCARD\nFN:Acme\nORG:Acme\nEND:VCARD", "Contact Card", "Acme", false);
            Says("BCD\n002\n1\nSCT\n\nRukus Cat\nDE89370400440532013000\nEUR12.50", "SEPA Payment", "Rukus Cat, EUR 12.50", false);
            Says("b0r3d.org", "URL", "b0r3d.org", false);
            Says("someone@example.com", "Email", "someone@example.com", false);
            Says("+44 20 7946 0958", "Phone Number", "+44 20 7946 0958", false);
            Says("(01)09506000134352(10)ABC", "GS1 data", "(01)09506000134352(10)ABC", false);
            Says("https://id.gs1.org/01/09506000134352", "Product link (GS1 Digital Link)", "https://id.gs1.org/01/09506000134352", false);
            Says("hello\n  world", "Text", "hello world", false);
            string longText = string.Join(" ", Enumerable.Repeat("words", 30));
            RecentCodes.Describe(longText, out _, out string cut, out _);
            Check(cut.Length == 80 && cut.EndsWith(" w…", StringComparison.Ordinal), "a long text is cut to 80 characters with an ellipsis, as the website cuts it: " + cut);
            Check(RecentCodes.SizeText(500) == "1 KB" && RecentCodes.SizeText(1536) == "2 KB" && RecentCodes.SizeText(2 * 1024 * 1024) == "2.0 MB", "sizes as the website writes them");

            InFreshDataFolder("recent", () =>
            {
                for (int i = 1; i <= 21; i++) Check(RecentCodes.Add(RecentCodes.New(Coded("code " + i), 1000 + i)) > 0, "code " + i + " wasn't kept");
                List<RecentCode> kept = RecentCodes.Load();
                Check(kept.Count == 20 && kept[0].Title == "code 21" && kept.Last().Title == "code 2", "20 codes kept, newest first, the oldest gone: " + kept.Count + ", " + kept[0].Title + " ... " + kept.Last().Title);
                RecentCodes.Add(RecentCodes.New(Coded("code 5"), 5000));
                kept = RecentCodes.Load();
                Check(kept.Count == 20 && kept[0].Title == "code 5" && kept.Count(c => c.Title == "code 5") == 1, "the same text again should replace its old entry and go to the top");
                QrDesign opened = RecentCodes.Open(kept[0].Id, new QrDesign());
                Check(opened != null && opened.Data == "code 5" && RecentCodes.Open(99999, new QrDesign()) == null, "a code opens with its text; a gone one doesn't");
                Check(RecentCodes.Delete(kept[0].Id) && RecentCodes.Load().Count == 19, "Delete takes one out");

                QrDesign big = Coded("big logo");
                big.Logo = "data:image/png;base64," + new string('A', RecentCodes.MostImageLength + 10);
                big.Frame = Presets.ApplyFrame("Default Frame", null);
                big.Frame.BackgroundImage = "data:image/png;base64," + new string('B', RecentCodes.MostImageLength + 10);
                RecentCode bigCode = RecentCodes.New(big, 9000);
                Check(bigCode.Omitted.SequenceEqual(new[] { "logo", "background" }) && !bigCode.Design.Contains("AAAA") && !bigCode.Design.Contains("BBBB"), "pictures too big to keep should be left out, and said");

                Check(RecentCodes.Clear() && RecentCodes.Load().Count == 0, "Clear all empties it");

                // ids never come round again, so one held from before never means a newer code
                int first = RecentCodes.Add(RecentCodes.New(Coded("first"), 100));
                RecentCodes.Delete(first);
                int second = RecentCodes.Add(RecentCodes.New(Coded("second"), 200));
                RecentCodes.Clear();
                int third = RecentCodes.Add(RecentCodes.New(Coded("third"), 300));
                Check(first > 0 && second > first && third > second, "an id is never used twice, after Delete or Clear all: " + first + ", " + second + ", " + third);

                // a code with a time no date can show (a file edited by hand) is left out, and the list still opens
                File.WriteAllText(RecentCodes.Path, "{\"v\":1,\"codes\":[{\"id\":1,\"key\":\"qr:qr:a\",\"savedAt\":1e15,\"design\":{}},{\"id\":2.5,\"key\":\"qr:qr:b\",\"savedAt\":5,\"design\":{}}," +
                                                    "{\"id\":3,\"key\":\"qr:qr:c\",\"savedAt\":5,\"design\":{}}]}");
                Check(RecentCodes.Load().Select(c => c.Id).SequenceEqual(new[] { 3 }), "codes with an impossible time or id are left out");
                Check(new RecentWindow(new Settings(), id => true).Codes.Count == 1, "...and the list opens");

                File.WriteAllText(RecentCodes.Path, "{\"v\":2,\"codes\":[]}");
                Check(RecentCodes.Load(out RecentTrouble newer) == null && newer == RecentTrouble.Newer && RecentCodes.Add(RecentCodes.New(Coded("y"), 1)) == 0 &&
                      File.ReadAllText(RecentCodes.Path) == "{\"v\":2,\"codes\":[]}", "a newer b0r3d QR's file is left alone");

                File.WriteAllText(RecentCodes.Path, "{\"v\":1,\"codes\":[]}");
                using (new FileStream(RecentCodes.Path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Check(RecentCodes.Load(out RecentTrouble busy) == null && busy == RecentTrouble.Busy, "a file another program has open is busy, not damaged");
                    var busyList = new RecentWindow(new Settings(), id => true);
                    Check(busyList.ProblemLine.Text.Contains("couldn't be opened just now") && busyList.ClearButton.Visibility == Visibility.Collapsed,
                        "a busy file isn't offered to Clear all: " + busyList.ProblemLine.Text);
                }

                File.WriteAllText(RecentCodes.Path, "{ not json");
                Check(RecentCodes.Load(out RecentTrouble damaged) == null && damaged == RecentTrouble.Damaged && RecentCodes.Add(RecentCodes.New(Coded("x"), 1)) == 0 &&
                      File.ReadAllText(RecentCodes.Path) == "{ not json", "a file that can't be read should be left as it is, not written over");
                var damagedList = new RecentWindow(new Settings(), id => true);
                damagedList.ClearAll();
                Check(damagedList.ClearButton.Visibility == Visibility.Visible && (string)damagedList.ClearButton.Content == "Empty the file", "a damaged file can be started again: " + damagedList.ClearButton.Content);
                Check(RecentCodes.Clear() && RecentCodes.Load().Count == 0, "Clear all starts a damaged file again");
            });
        }

        // the codes saved or copied land in the list, with the note the first time; the list opens and deletes them
        static void RecentCodesInTheWindow()
        {
            InFreshDataFolder("recent-window", () =>
            {
                var settings = new Settings();
                var window = new MainWindow(settings);
                LayOut(window.Root, MainWidth, MainHeight);
                Type(window, "https://b0r3d.org/recent");
                window.Remember(window.CurrentDesign);
                Check(window.RecentNote.Visibility == Visibility.Visible && settings.RecentNoteShown, "the first code kept should bring the note about Recent codes");
                Check(Settings.Load(Path.Combine(DataFolder.Path, Settings.FileName)).RecentNoteShown, "the note should be shown only once, ever");
                window.RecentNoteOk.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(window.RecentNote.Visibility == Visibility.Collapsed, "OK puts the note away");

                Type(window, "WIFI:T:WPA;S:home;P:secret;;");
                window.Remember(window.CurrentDesign);
                Check(window.RecentNote.Visibility == Visibility.Collapsed, "the note shows only the first time");
                var list = new RecentWindow(settings, window.OpenRecent);
                LayOut(list.Root, 600, 660);
                Check(list.Codes.Count == 2 && list.Codes[0].Sensitive && list.Codes[0].Name == "WiFi: home",
                    "the list should have both, newest first, the Wi-Fi one by its network's name only");
                Check(list.CountLine.Text.StartsWith("2 of 20 codes, about ", StringComparison.Ordinal), "the count: " + list.CountLine.Text);
                var wifiRow = (DockPanel)list.CodesList.Children[0];
                Check(((Border)wifiRow.Children[0]).Child is System.Windows.Shapes.Path && ((Border)((DockPanel)list.CodesList.Children[1]).Children[0]).Child is Image,
                    "a code with a password shows a padlock, the others a picture of the code");
                object Picture() => ((Image)((Border)((DockPanel)list.CodesList.Children[1]).Children[0]).Child).Source;
                object drawn = Picture();
                list.Refresh();
                Check(ReferenceEquals(drawn, Picture()), "each code is drawn once while the list is open");

                list.Open(list.Codes[1]);
                Check(window.DataBox.Text == "https://b0r3d.org/recent", "Open should bring the code back into the window, its text and all");

                settings.RecentCodes = false;
                Type(window, "https://b0r3d.org/not-kept");
                window.Remember(window.CurrentDesign);
                Check(RecentCodes.Load().Count == 2, "with Recent codes off, nothing is added");
                settings.RecentCodes = true;

                list = new RecentWindow(settings, window.OpenRecent);
                list.Delete(list.Codes[0]);
                Check(list.Codes.Count == 1 && list.StatusLine.Text == "Deleted WiFi: home.", "Delete: " + list.StatusLine.Text);
                list.ClearAll();
                Check(list.Codes.Count == 1 && list.CancelClearButton.Visibility == Visibility.Visible && (string)list.ClearButton.Content == "Delete all 1 codes", "Clear all should ask first");
                list.ClearAll();
                Check(list.Codes.Count == 1, "a second click straight away (a double-click) shouldn't delete them");
                list.ClearAll(true);
                Check(list.Codes.Count == 0 && list.StatusLine.Text == "All recent codes deleted." && list.EmptyLine.Visibility == Visibility.Visible, "Clear all, confirmed: " + list.StatusLine.Text);

                // "Don't keep codes" on the note: no more are added, and that code goes
                var fresh = new Settings();
                var second = new MainWindow(fresh);
                LayOut(second.Root, MainWidth, MainHeight);
                Type(second, "https://b0r3d.org/note");
                second.Remember(second.CurrentDesign);
                Check(second.RecentNote.Visibility == Visibility.Visible, "a fresh start shows the note");
                second.RecentNoteStop.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(!fresh.RecentCodes && RecentCodes.Load().Count == 0 && second.RecentNote.Visibility == Visibility.Collapsed, "Don't keep codes turns it off and deletes that code");
                Check(!Settings.Load(Path.Combine(DataFolder.Path, Settings.FileName)).RecentCodes, "turning it off is kept at once");
                foreach (string off in new[] { "off", "false", "No", "0" })
                {
                    File.WriteAllText(Path.Combine(DataFolder.Path, Settings.FileName), "recent_codes=" + off + "\r\n");
                    Check(!Settings.Load(Path.Combine(DataFolder.Path, Settings.FileName)).RecentCodes, "recent_codes=" + off + " should be off");
                }

                // a file it can't read: the code's still saved or copied, and the line says so after that
                File.WriteAllText(RecentCodes.Path, "{ not json");
                settings.RecentCodes = true;
                Type(window, "https://b0r3d.org/damaged");
                window.Remember(window.CurrentDesign);
                Check(window.Note.Text.Contains("Not added to Recent codes: its file in the data folder can't be read") && !RecentWindow.LastFailed, "a damaged file: " + window.Note.Text);
            });
        }
    }
}
