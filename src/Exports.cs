using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace B0r3dQR
{
    // Getting a code out of the program: files, the clipboard and drag and drop.
    static class Exports
    {
        // A file name for a code, without the extension. Nothing private goes into it: a link
        // gives its site ("b0r3d.org", never a name and password in it), a Wi-Fi code its network
        // ("wifi-home", never the password), other kinds of data just their kind ("contact",
        // "otpauth"), and plain text its first words. Otherwise "qr-code".
        public static string SuggestedName(string text)
        {
            string t = WithoutInvisibleEnds(text ?? "");
            string kind = Kind(t);
            if (kind != null) t = kind;
            var name = new StringBuilder();
            bool gap = false;
            foreach (char c in t)
            {
                if (name.Length >= 40) break;
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_')
                {
                    name.Append(c);
                    gap = false;
                }
                else if (!gap && name.Length > 0)
                {
                    name.Append('-');
                    gap = true;
                }
            }
            string result = name.ToString().Trim('-', '.');
            return result.Length == 0 || IsReserved(result) ? "qr-code" : result;
        }

        // what a code is, when it's more than plain words: null for plain text
        static string Kind(string t)
        {
            if (t.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase)) return "wifi " + WifiNetwork(t);
            if (t.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase) || t.StartsWith("MECARD:", StringComparison.OrdinalIgnoreCase)) return "contact";
            if (t.StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase) || t.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) return "event";
            if (Regex.IsMatch(t, @"^BCD\r?\n")) return "payment";  // an EPC (SEPA) payment
            Match scheme = Regex.Match(t, @"^([A-Za-z][A-Za-z0-9+.\-]{1,30}):");
            if (!scheme.Success) return null;
            switch (scheme.Groups[1].Value.ToLowerInvariant())
            {
                case "http":
                case "https":
                    return Uri.TryCreate(t, UriKind.Absolute, out Uri uri) && uri.Host.Length > 0
                        ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host.Substring(4) : uri.Host)
                        : "link";
                case "mailto": return "email";
                case "tel": return "phone";
                case "sms":
                case "smsto": return "sms";
                case "geo": return "location";
                default: return scheme.Groups[1].Value.ToLowerInvariant();
            }
        }

        // Spaces, and characters you can't see (a byte-order mark or zero-width space at the start
        // of pasted text), at either end don't count.
        static string WithoutInvisibleEnds(string text)
        {
            int start = 0, end = text.Length;
            while (start < end && Invisible(text[start])) start++;
            while (end > start && Invisible(text[end - 1])) end--;
            return text.Substring(start, end - start);
        }

        static bool Invisible(char c) =>
            char.IsWhiteSpace(c) || char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format;

        // the S: (network name) part of "WIFI:T:WPA;S:home;P:secret;;", with \; \: \\ unescaped
        static string WifiNetwork(string wifi)
        {
            int i = wifi.IndexOf(':') + 1;
            while (i < wifi.Length)
            {
                var field = new StringBuilder();
                for (; i < wifi.Length && wifi[i] != ';'; i++)
                {
                    if (wifi[i] == '\\' && i + 1 < wifi.Length) i++;
                    field.Append(wifi[i]);
                }
                i++;
                if (field.Length > 2 && (field[0] == 'S' || field[0] == 's') && field[1] == ':') return field.ToString(2, field.Length - 2);
            }
            return "";
        }

        // names Windows keeps for devices, even with an extension (CON.png is no file on Windows 10)
        internal static bool IsReserved(string name)
        {
            string stem = name.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL") return true;
            return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3]) && stem[3] != '0';
        }

        // Writes the whole file or nothing: into a temporary file of its own beside it first (a
        // name nobody else uses), then into place.
        public static void Write(string path, byte[] bytes)
        {
            string full = Path.GetFullPath(path);
            string tmp = Path.Combine(Path.GetDirectoryName(full), "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                File.WriteAllBytes(tmp, bytes);
                IntoPlace(tmp, full);
            }
            catch
            {
                try { File.Delete(tmp); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                throw;
            }
        }

        // A whole temporary file into its place (a batch's zip goes in this way too): moved there, or
        // swapped for the file there already. Some USB sticks' file systems can't swap files; there the
        // old file is moved to a name of its own, the new one into its place, and then the old one
        // deleted. Moves on one drive need no room of their own, so a nearly full stick can't lose
        // either file, and if the new one can't go in, the old one goes back. A read-only file is
        // left as it is, as Windows leaves it when it can swap.
        internal static void IntoPlace(string temporary, string full)
        {
            if (!File.Exists(full))
            {
                Move(temporary, full);
                return;
            }
            try
            {
                Swap(temporary, full);
                return;
            }
            catch (IOException)
            {
            }
            if (!File.Exists(full))
            {
                Move(temporary, full);   // the swap failed half-way, with the old file gone
                return;
            }
            if ((File.GetAttributes(full) & FileAttributes.ReadOnly) != 0) throw new UnauthorizedAccessException("Access to the path '" + full + "' is denied.");
            string old = Path.Combine(Path.GetDirectoryName(full), "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".old");
            Move(full, old);
            try
            {
                Move(temporary, full);
            }
            catch
            {
                Move(old, full);
                throw;
            }
            try { File.Delete(old); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }   // the new file is in; the old one stays beside it
        }

        // Windows swapping a file for another, and moving one; the tests make them fail, as swapping
        // does on some USB sticks
        internal static Action<string, string> Swap = (temporary, full) => File.Replace(temporary, full, null);
        internal static Action<string, string> Move = File.Move;

        // What Copy and drag hand to other programs: the picture (for Word, Paint and email), the
        // PNG file's bytes (for programs that take PNG), and when dragging, a PNG file to drop into
        // folders. It's never sent to Windows' cloud clipboard (it's a picture of what was typed).
        public static DataObject DataFor(BitmapSource picture, byte[] png, string fileName, bool withFile)
        {
            var data = new DataObject();
            data.SetImage(picture);
            data.SetData("PNG", new MemoryStream(png), false);
            KeepOffTheCloud(data);
            if (withFile)
            {
                string file = DragFile(png, fileName);
                if (file != null) data.SetFileDropList(new StringCollection { file });
            }
            return data;
        }

        // What Copy text hands over: the text, kept out of Windows' cloud clipboard (a scanned code
        // can hold a Wi-Fi password or a sign-in secret)
        public static DataObject TextData(string text)
        {
            var data = new DataObject();
            data.SetText(text, TextDataFormat.UnicodeText);
            KeepOffTheCloud(data);
            return data;
        }

        // Windows' cloud clipboard leaves alone what says it can't be uploaded. (Text copied with
        // Ctrl+C from any of b0r3d QR's text boxes gets this too: App's OnTextCopying.)
        public static void KeepOffTheCloud(IDataObject data)
        {
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)), false);
        }

        // Dragged PNGs wait in a folder of b0r3d QR's own inside the data folder until it closes.
        public const string DragFolderName = "b0r3dQR drag";

        public static string DragFolder => Path.Combine(DataFolder.Path, DragFolderName);

        static string DragFile(byte[] png, string fileName)
        {
            if (!DataFolder.CanKeep) return null;
            try
            {
                Directory.CreateDirectory(DragFolder);
                string path = Path.Combine(DragFolder, fileName + ".png");
                File.WriteAllBytes(path, png);
                return path;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
            {
                return null;
            }
        }

        // Empties the drag folder (on start and on close): only the PNG files in it, then the
        // folder itself once it's empty. Never anything else, never a whole tree.
        public static void ClearDragFolder()
        {
            try
            {
                if (!Directory.Exists(DragFolder)) return;
                foreach (string file in Directory.GetFiles(DragFolder).Where(f => string.Equals(Path.GetExtension(f), ".png", StringComparison.OrdinalIgnoreCase)))
                {
                    try { File.Delete(file); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }  // still being dropped somewhere; next time
                }
                if (!Directory.EnumerateFileSystemEntries(DragFolder).Any()) Directory.Delete(DragFolder, false);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
