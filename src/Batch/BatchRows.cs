using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using B0r3dQR.Design;

namespace B0r3dQR.Batch
{
    // a row of a batch: what goes in its code, and what the CSV says about it
    sealed class BatchRow
    {
        public int Row;                 // its row in the file, counting a header row, as a spreadsheet numbers them
        public string Data;             // the code's text (for a file of contacts, the vCard written from the row)
        public string FrameText = "";   // QR: the frame's caption for this row ("" for the design's own)
        public string FileName = "";    // the file name the CSV gives it ("" for none)
        public string Font = "";        // QR: the caption's font for this row ("" for the design's own)
        public Dictionary<string, string> Contact;   // QR: a contact's fields (the vCard template's names), for showing the row
    }

    static class BatchRows
    {
        // ---------- reading ----------

        // the vCard template's fields for a contact, and the column each one is read from
        static readonly (string Field, string Column)[] ContactColumns =
        {
            ("firstName", "firstname"), ("lastName", "lastname"), ("org", "org"), ("position", "position"),
            ("phoneWork", "phonework"), ("phonePrivate", "phoneprivate"), ("phoneMobile", "phonemobile"), ("email", "email"),
            ("website", "website"), ("street", "street"), ("zipcode", "zipcode"), ("city", "city"), ("state", "state"),
            ("country", "country"),
        };

        // The QR page's batch, as the website reads it: a header row naming the columns, then a code a row; null
        // where the website says "Invalid CSV" (see IsUsable). A file whose header has firstName and lastName columns
        // is a file of contacts, each row made into a vCard. Any other is a file of links or texts, read by place
        // whatever its header says: the code's text, then the caption, then the file name. In both, a row's value in
        // a frameFontFamily column is its caption's font, and a caption in any row puts the frame on.
        public static List<BatchRow> ReadQr(string content, out bool hasFrameText)
        {
            hasFrameText = false;
            List<List<string>> records = Csv.Parse(content).Records;
            if (records.Count == 0) return null;
            var columns = new Columns(records[0]);
            bool contacts = columns.Has("firstname") && columns.Has("lastname");
            if (!IsUsable(records, columns, contacts)) return null;
            var rows = new List<BatchRow>();
            for (int i = 1; i < records.Count; i++)
            {
                List<string> values = records[i];
                var row = new BatchRow { Row = i + 1, Font = columns.Get(values, "framefontfamily") };
                if (contacts) ReadContact(row, values, columns);
                else
                {
                    row.Data = At(values, 0);
                    row.FrameText = At(values, 1);
                    row.FileName = At(values, 2);
                }
                rows.Add(row);
            }
            hasFrameText = rows.Any(row => Js.Trim(row.FrameText).Length > 0);
            return rows;
        }

        // Whether the website takes a QR batch's records: it needs a row after the header, and every row needs its
        // code's text (in a file of contacts, a first name and a last name). One row without makes it all invalid.
        static bool IsUsable(List<List<string>> records, Columns columns, bool contacts)
        {
            if (records.Count < 2) return false;
            for (int i = 1; i < records.Count; i++)
            {
                List<string> values = records[i];
                bool usable = contacts ? columns.Get(values, "firstname").Length > 0 && columns.Get(values, "lastname").Length > 0
                                       : Js.Trim(At(values, 0)).Length > 0;
                if (!usable) return false;
            }
            return true;
        }

        // a contact's row: the vCard the vCard template writes from its columns (version 3.0 unless it says), and
        // its caption and file name from the frameText and fileName columns
        static void ReadContact(BatchRow row, List<string> values, Columns columns)
        {
            var fields = new Dictionary<string, string>();
            foreach (var (field, column) in ContactColumns) fields[field] = columns.Get(values, column);
            string version = columns.Get(values, "version");
            fields["version"] = version.Length > 0 ? version : "3.0";
            row.Data = Templates.Make(TemplateKind.VCard, fields);
            row.FrameText = columns.Get(values, "frametext");
            row.FileName = columns.Get(values, "filename");
            row.Contact = fields;
        }

        // a row's value at a place, "" past its end
        static string At(List<string> values, int place) => place < values.Count ? values[place] : "";

        // A header's columns, looked up by name: each of its values in small letters (JavaScript's, so the Kelvin
        // sign is a k), at the first place it comes.
        sealed class Columns
        {
            readonly Dictionary<string, int> places = new Dictionary<string, int>();

            public Columns(List<string> header)
            {
                for (int i = 0; i < header.Count; i++)
                {
                    string name = Js.ToLowerCase(header[i]);
                    if (!places.ContainsKey(name)) places.Add(name, i);
                }
            }

            public bool Has(string name) => places.ContainsKey(name);

            // a row's value in the column of that name: "" when there's no such column, or the row stops before it
            public string Get(List<string> values, string name) => places.TryGetValue(name, out int place) ? At(values, place) : "";
        }

        static readonly string[] DataColumns = { "data", "code", "barcode", "value", "text", "url" };
        static readonly string[] FileNameColumns = { "filename", "file name", "file", "name" };

        // The barcode page's batch (parseBarcodeBatchCsv): one code a row. A header row is optional and known by its
        // names (data, code, barcode, value, text or url for the code; fileName, file name, file or name for the
        // file), so a QR batch file works too; without one, the first column is the code and the second the file
        // name. Rows with no code are left out.
        public static List<BatchRow> ReadBarcodes(string content)
        {
            string text = content.Length > 0 && content[0] == (char)0xFEFF ? content.Substring(1) : content;
            List<List<string>> records = Csv.Parse(text).Records;
            var rows = new List<BatchRow>();
            if (records.Count == 0) return rows;
            List<string> first = records[0].Select(Js.ToLowerCase).ToList();
            int dataColumn = first.FindIndex(cell => DataColumns.Contains(cell));
            int fileColumn = first.FindIndex(cell => FileNameColumns.Contains(cell));
            bool hasHeader = dataColumn >= 0 || fileColumn >= 0;
            int dataAt = dataColumn >= 0 ? dataColumn : fileColumn == 0 ? 1 : 0;
            int fileAt = hasHeader ? fileColumn : 1;
            for (int i = hasHeader ? 1 : 0; i < records.Count; i++)
            {
                List<string> values = records[i];
                string data = dataAt < values.Count ? values[dataAt] : "";
                if (Js.Trim(data).Length == 0) continue;
                rows.Add(new BatchRow { Row = i + 1, Data = data, FileName = fileAt >= 0 && fileAt < values.Count ? values[fileAt] : "" });
            }
            return rows;
        }

        // ---------- the files' names ----------

        // A name made safe for a file: every character other than A-Z, a-z, 0-9, _ and - made _ (each UTF-16 unit, so
        // an emoji, which is two, becomes __)
        public static string Safe(string name)
        {
            var safe = new StringBuilder(name.Length);
            foreach (char c in name) safe.Append(IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return safe.ToString();
        }

        static bool IsLetterOrDigit(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

        // b0r3d QR's own step for a safe name that Windows keeps for a device (CON, PRN, AUX, NUL, COM1 to COM9, LPT1
        // to LPT9, in any capitals): it gets _ on the end, as Windows 10 makes no file called CON.png. Into a zip too,
        // so the zip unpacks on Windows. (The website keeps the name.)
        static string NotADevice(string safe) => Exports.IsReserved(safe) ? safe + "_" : safe;

        // The QR page's file names, one a row in order, as the website names them. A row's name is its file name, else
        // its caption, else one from its code's text (NameFromText); when an earlier row came to the same name, the
        // row's index (from 0) goes on the end ("same-1"); then it's made safe. Last, b0r3d QR's own steps: a device's
        // name gets _ (NotADevice), and a safe name an earlier row was given already gets the index too, then -3, -4
        // and so on until it's new (the website's zip keeps only one of two files of the same name).
        public static List<string> QrNames(IList<BatchRow> rows)
        {
            var named = new HashSet<string>();   // the names the rows came to, before they were made safe
            var given = new HashSet<string>();   // the names given
            var names = new List<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                BatchRow row = rows[i];
                string name = !string.IsNullOrEmpty(row.FileName) ? row.FileName
                    : !string.IsNullOrEmpty(row.FrameText) ? row.FrameText
                    : NameFromText(row.Data ?? "", i);
                if (named.Contains(name)) name += "-" + i;
                named.Add(name);
                // the website's name made safe; then b0r3d QR's own steps, for a device's name and for a safe name
                // that's been given already
                string safe = NotADevice(Safe(name)), file = safe;
                if (given.Contains(file)) file = safe + "-" + i;
                for (int n = 3; given.Contains(file); n++) file = safe + "-" + i + "-" + n;
                given.Add(file);
                names.Add(file);
            }
            return names;
        }

        // A name from a code's text, for a row with no file name or caption. A vCard is named after the name on it
        // (VCardName). Text with more than one / in it is named after its last part, if that's a word (A-Z, a-z, 0-9
        // and _ only), else the part before it if that is, else its site (SiteName), else "qr_code_" and the row's
        // index. Any other text is its own name, trimmed.
        static string NameFromText(string text, int index)
        {
            if (text.StartsWith("BEGIN:VCARD", StringComparison.Ordinal)) return VCardName(text);
            string[] parts = text.Split('/');
            if (parts.Length <= 2) return Js.Trim(text);
            if (IsWord(parts[parts.Length - 1])) return parts[parts.Length - 1];
            if (IsWord(parts[parts.Length - 2])) return parts[parts.Length - 2];
            return SiteName(text) ?? "qr_code_" + index;
        }

        static bool IsWord(string part) => part.Length > 0 && part.All(c => IsLetterOrDigit(c) || c == '_');

        // A vCard's name: what follows its first "FN:" that has something other than a line end after it, up to the
        // end of that line, trimmed, with every run of white space in it made one _. "" when there's none (as in a
        // vCard 2.1 whose name is written FN;CHARSET=UTF-8:).
        static string VCardName(string card)
        {
            for (int at = card.IndexOf("FN:", StringComparison.Ordinal); at >= 0; at = card.IndexOf("FN:", at + 1, StringComparison.Ordinal))
            {
                int from = at + 3, to = from;
                while (to < card.Length && card[to] != '\r' && card[to] != '\n') to++;
                if (to > from) return Regex.Replace(Js.Trim(card.Substring(from, to - from)), Js.Space + "+", "_");
            }
            return "";
        }

        // A link's site, as a name: what follows the first http:// or https:// that has anything but a / after it, up
        // to the next / or the end, with every character other than A-Z, a-z and 0-9 made _; null when there's none.
        static string SiteName(string text)
        {
            Match site = Regex.Match(text, "https?://([^/]+)");
            return site.Success ? Regex.Replace(site.Groups[1].Value, "[^A-Za-z0-9]", "_") : null;
        }

        // The barcode page's file names: the CSV's file name, else the code's text, made safe (and not a device's),
        // with _2, _3 and so on for a name already taken
        public static List<string> BarcodeNames(IList<BatchRow> rows)
        {
            var used = new HashSet<string>();
            var names = new List<string>();
            foreach (BatchRow row in rows)
            {
                string safe = NotADevice(Safe(string.IsNullOrEmpty(row.FileName) ? row.Data : row.FileName)), name = safe;
                for (int n = 2; used.Contains(name); n++) name = safe + "_" + n;
                used.Add(name);
                names.Add(name);
            }
            return names;
        }

        // The text export's file names, for the rows it makes, in order (places[k] being rows[k]'s place among all
        // the batch's rows, from 0): the CSV's file name, else "qr-" and the place, made safe (and not a device's). A
        // name given already gets _1, or if that's taken too _2, and so on.
        public static List<string> TextNames(IList<BatchRow> rows, IList<int> places)
        {
            var names = new List<string>();
            var given = new HashSet<string>();
            for (int k = 0; k < rows.Count; k++)
            {
                string wanted = NotADevice(Safe(string.IsNullOrEmpty(rows[k].FileName) ? "qr-" + places[k] : rows[k].FileName)), name = wanted;
                for (int n = 1; !given.Add(name); n++) name = wanted + "_" + n;
                names.Add(name);
            }
            return names;
        }
    }
}
