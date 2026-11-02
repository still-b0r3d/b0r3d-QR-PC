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
        public Dictionary<string, string> Contact;   // QR: a contact's fields, for showing the row
    }

    // What the website makes of a batch CSV (src/utils/csv.ts and csvBatchProcessing.ts) and the file names it gives
    // the codes in the batch's zip (QRCodeCreate.vue, BarcodeCreate.vue and TextExportModal.vue).
    static class BatchRows
    {
        static readonly string[] ContactFields =
        {
            "org", "position", "phonework", "phoneprivate", "phonemobile", "email", "website", "street", "zipcode",
            "city", "state", "country", "version", "frameText",
        };

        // The QR page's batch (parseCSV, validateCSVData, processCsvDataForBatch): the first row is always the
        // header. A header with firstName and lastName makes a file of contacts, each written as a vCard (3.0 unless
        // a version column says otherwise); otherwise each row is the text, then a frame caption, then a file name,
        // by position, and a frameFontFamily column is found by its name. Null when the website says "Invalid CSV":
        // no rows, or a row without its text (or a contact without both names).
        public static List<BatchRow> ReadQr(string content, out bool hasFrameText)
        {
            hasFrameText = false;
            List<string> lines = Csv.SplitRows(content);
            if (lines.Count == 0) return null;
            string header = lines[0].ToLowerInvariant();
            char delimiter = Csv.Delimiter(header);
            List<string> headers = Csv.SplitLine(header, delimiter).Select(h => h.ToLowerInvariant()).ToList();
            bool contacts = Csv.IsContacts(header, delimiter);
            int fontAt = headers.IndexOf("framefontfamily");
            var rows = new List<BatchRow>();
            for (int i = 1; i < lines.Count; i++)
            {
                List<string> values = Csv.SplitLine(lines[i], delimiter);
                string At(int index) => index >= 0 && index < values.Count ? values[index] : null;
                var row = new BatchRow { Row = i + 1 };
                if (contacts)
                {
                    string first = At(headers.IndexOf("firstname")) ?? "", last = At(headers.IndexOf("lastname")) ?? "";
                    if (first.Length == 0 || last.Length == 0) return null;
                    var fields = new Dictionary<string, string> { ["firstName"] = first, ["lastName"] = last };
                    foreach (string field in ContactFields)
                    {
                        string value = At(headers.IndexOf(field.ToLowerInvariant()));
                        if (!string.IsNullOrEmpty(value)) fields[field] = value;
                    }
                    row.Contact = fields;
                    row.Data = Templates.Make(TemplateKind.VCard, new Dictionary<string, string>
                    {
                        ["firstName"] = first, ["lastName"] = last,
                        ["org"] = Get(fields, "org"), ["position"] = Get(fields, "position"),
                        ["phoneWork"] = Get(fields, "phonework"), ["phonePrivate"] = Get(fields, "phoneprivate"), ["phoneMobile"] = Get(fields, "phonemobile"),
                        ["email"] = Get(fields, "email"), ["website"] = Get(fields, "website"),
                        ["street"] = Get(fields, "street"), ["zipcode"] = Get(fields, "zipcode"), ["city"] = Get(fields, "city"),
                        ["state"] = Get(fields, "state"), ["country"] = Get(fields, "country"),
                        ["version"] = Get(fields, "version").Length > 0 ? Get(fields, "version") : "3.0",
                    });
                    row.FrameText = Get(fields, "frameText");
                    row.FileName = At(headers.IndexOf("filename")) ?? "";
                }
                else
                {
                    row.Data = At(0) ?? "";
                    if (Js.Trim(row.Data).Length == 0) return null;
                    row.FrameText = At(1) ?? "";
                    row.FileName = At(2) ?? "";
                }
                row.Font = At(fontAt) ?? "";
                rows.Add(row);
            }
            if (rows.Count == 0) return null;
            hasFrameText = rows.Any(r => Js.Trim(r.FrameText).Length > 0);
            return rows;
        }

        static string Get(Dictionary<string, string> fields, string name) => fields.TryGetValue(name, out string value) ? value : "";

        static readonly string[] DataColumns = { "data", "code", "barcode", "value", "text", "url" };
        static readonly string[] FileNameColumns = { "filename", "file name", "file", "name" };

        // The barcode page's batch (parseBarcodeBatchCsv): one code a row. A header row is optional and known by its
        // names (data, code, barcode, value, text or url for the code; fileName, file name, file or name for the
        // file), so a QR batch file works too; without one, the first column is the code and the second the file
        // name. Rows with no code are left out.
        public static List<BatchRow> ReadBarcodes(string content)
        {
            string text = content.Length > 0 && content[0] == (char)0xFEFF ? content.Substring(1) : content;
            List<string> lines = Csv.SplitRows(text);
            var rows = new List<BatchRow>();
            if (lines.Count == 0) return rows;
            char delimiter = Csv.Delimiter(lines[0]);
            List<string> first = Csv.SplitLine(lines[0], delimiter).Select(cell => cell.ToLowerInvariant()).ToList();
            int dataColumn = first.FindIndex(cell => DataColumns.Contains(cell));
            int fileColumn = first.FindIndex(cell => FileNameColumns.Contains(cell));
            bool hasHeader = dataColumn >= 0 || fileColumn >= 0;
            int dataAt = dataColumn >= 0 ? dataColumn : fileColumn == 0 ? 1 : 0;
            int fileAt = hasHeader ? fileColumn : 1;
            for (int i = hasHeader ? 1 : 0; i < lines.Count; i++)
            {
                List<string> values = Csv.SplitLine(lines[i], delimiter);
                string data = dataAt < values.Count ? values[dataAt] : "";
                if (Js.Trim(data).Length == 0) continue;
                rows.Add(new BatchRow { Row = i + 1, Data = data, FileName = fileAt >= 0 && fileAt < values.Count ? values[fileAt] : "" });
            }
            return rows;
        }

        // the characters the website keeps in a file name; any other becomes _
        public static string Safe(string name) => Regex.Replace(name, "[^a-zA-Z0-9_-]", "_");

        // The QR page's file names (generateBatchExportFilename, then made safe): the CSV's file name, else the frame
        // caption, else one from the text (a contact's name; a link's last word-like part, else its site; or the text
        // itself), with "-<its place>" when the name was taken. The website's zip keeps one of two files whose names
        // only became the same when made safe; here the second gets "-<its place>" too, and so on.
        public static List<string> QrNames(IList<BatchRow> rows)
        {
            var used = new HashSet<string>();
            var safeUsed = new HashSet<string>();
            var names = new List<string>();
            for (int index = 0; index < rows.Count; index++)
            {
                string name = Name(rows[index].Data, rows[index].FrameText, rows[index].FileName, index);
                if (used.Contains(name)) name = name + "-" + index;
                used.Add(name);
                string safe = Safe(name), unique = safe;
                for (int n = 2; safeUsed.Contains(unique); n++) unique = safe + "-" + index + (n > 2 ? "-" + n : "");
                safeUsed.Add(unique);
                names.Add(unique);
            }
            return names;
        }

        static readonly Regex WordLike = new Regex("^[a-zA-Z0-9_]+\\z");

        static string Name(string data, string frameText, string fileName, int index)
        {
            if (!string.IsNullOrEmpty(fileName)) return fileName;
            if (!string.IsNullOrEmpty(frameText)) return frameText;
            if (data.StartsWith("BEGIN:VCARD", System.StringComparison.Ordinal))
            {
                Match fn = Regex.Match(data, "FN:([^\r\n]+)");
                return fn.Success ? SpacesTo(Js.Trim(fn.Groups[1].Value), "_") : "";
            }
            string[] parts = data.Split('/');
            if (parts.Length <= 2) return Js.Trim(data);
            string last = parts[parts.Length - 1], secondLast = parts[parts.Length - 2];
            if (WordLike.IsMatch(last)) return last;
            if (secondLast.Length > 0 && WordLike.IsMatch(secondLast)) return secondLast;
            Match site = Regex.Match(data, "https?://([^/]+)");
            return site.Success ? Regex.Replace(site.Groups[1].Value, "[^a-zA-Z0-9]", "_") : "qr_code_" + index;
        }

        // every run of JavaScript's white space as one `with`
        static string SpacesTo(string text, string with)
        {
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (!Js.IsSpace(text[i])) result.Append(text[i]);
                else if (i == 0 || !Js.IsSpace(text[i - 1])) result.Append(with);
            }
            return result.ToString();
        }

        // The barcode page's file names: the CSV's file name, else the code's text, made safe, with _2, _3 and so on
        // for a name already taken
        public static List<string> BarcodeNames(IList<BatchRow> rows)
        {
            var used = new HashSet<string>();
            var names = new List<string>();
            foreach (BatchRow row in rows)
            {
                string safe = Safe(string.IsNullOrEmpty(row.FileName) ? row.Data : row.FileName), name = safe;
                for (int n = 2; used.Contains(name); n++) name = safe + "_" + n;
                used.Add(name);
                names.Add(name);
            }
            return names;
        }

        // The text export's file names (TextExportModal.vue) for the rows it makes, at `places` among all the rows: the
        // CSV's file name, else qr-<its place from 0>, made safe, with _1, _2 and so on for a name already taken
        public static List<string> TextNames(IList<BatchRow> rows, IList<int> places, string extension)
        {
            var used = new HashSet<string>();
            var names = new List<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                string safe = Safe(string.IsNullOrEmpty(rows[i].FileName) ? "qr-" + places[i] : rows[i].FileName), name = safe;
                for (int n = 1; used.Contains(name + extension); n++) name = safe + "_" + n;
                used.Add(name + extension);
                names.Add(name);
            }
            return names;
        }
    }
}
