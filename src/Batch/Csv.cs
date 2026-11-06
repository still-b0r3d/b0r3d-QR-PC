using System.Collections.Generic;
using System.Text;
using B0r3dQR.Design;

namespace B0r3dQR.Batch
{
    // A CSV file as Csv.Parse reads it: the separator its values were cut at, and its records, each a list of
    // values (the records that are nothing but white space left out)
    sealed class CsvTable
    {
        public char Separator = ',';
        public readonly List<List<string>> Records = new List<List<string>>();
    }

    // Batch export's CSV files, read the way the website reads them, so that a file gives the same rows on both:
    // records and quotes much as RFC 4180 has them (see Reader for exactly how), with the website's own ways on top
    // (one separator for the file, chosen from its first record; values trimmed, and an apostrophe taken off each
    // end; its own way with CRs).
    static class Csv
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        static readonly Encoding Windows1252 = Encoding.GetEncoding(1252);

        // A file's text: its bytes as UTF-8 when they are UTF-8 (any broken sequence means they aren't), with one
        // byte order mark at the start dropped; else as Windows-1252, as Excel saves a CSV on a Western PC (all 256
        // bytes are characters there, and nothing is dropped)
        public static string Read(byte[] bytes)
        {
            string text;
            try
            {
                text = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Windows1252.GetString(bytes);
            }
            return text.Length > 0 && text[0] == (char)0xFEFF ? text.Substring(1) : text;
        }

        // A CSV's records and their separator, in one go through the text. The separator comes from the first
        // record that isn't blank, before any of it is cut into values: from its commas, semicolons and tabs
        // outside quotes (see Reader.Separator). The other two are characters like any other.
        public static CsvTable Parse(string text)
        {
            var table = new CsvTable();
            var reader = new Reader(text);
            if (!reader.Next()) return table;
            table.Separator = reader.Separator;
            do table.Records.Add(reader.Values(table.Separator));
            while (reader.Next());
            return table;
        }

        // Goes through a CSV's text a record at a time. A record runs to a line end (LF, or CR LF) outside quotes,
        // or to the end of the text; a CR on its own is a character of the record. A quote starts a quoted stretch
        // anywhere in a value and the next quote ends it, except that two quotes in a row inside one are a quote
        // in the value; a quote that's never closed runs to the end of the text.
        sealed class Reader
        {
            readonly string text;
            int next;                        // where the next record starts (past the end once the last is read)
            int start, end;                  // the record found: its characters, without its line end
            int dropped;                     // the CR it goes without (see Next), or -1
            int commas, semicolons, tabs;    // its commas, semicolons and tabs outside quotes

            public Reader(string text)
            {
                this.text = text;
            }

            // Finds the next record that isn't blank (blank: nothing but white space as it's written, so a quote or
            // a comma makes one that isn't, whatever its values come to); false when there are no more.
            public bool Next()
            {
                while (next <= text.Length)
                {
                    start = next;
                    int i = start, firstCr = -1;
                    bool quoted = false, blank = true, crlf = false;
                    commas = semicolons = tabs = 0;
                    for (; i < text.Length; i++)
                    {
                        char c = text[i];
                        if (!quoted && c == '\n') break;
                        if (!quoted && c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        {
                            crlf = true;
                            break;
                        }
                        if (c == '"') quoted = !quoted;   // (two in a row inside quotes, one quote in the value, leave it on)
                        else if (c == '\r' && firstCr < 0) firstCr = i;
                        else if (!quoted && c == ',') commas++;
                        else if (!quoted && c == ';') semicolons++;
                        else if (!quoted && c == '\t') tabs++;
                        blank &= Js.IsSpace(c);
                    }
                    end = i;
                    next = crlf ? i + 2 : i + 1;
                    // the website's way with CRs: a record that ends with a lone LF, or with the end of the text,
                    // goes without the first CR in it (in quotes or not), and the rest of the reading never sees
                    // that CR (so it can bring two quotes together); one that ends with CR LF keeps them all
                    dropped = crlf ? -1 : firstCr;
                    if (!blank) return true;
                }
                return false;
            }

            // the separator the record's counts choose: a semicolon when it has more of them than commas and at
            // least as many as tabs, else a tab when it has more of them than commas, else a comma (so a comma
            // wins any tie it's in, and a semicolon a tie with a tab)
            public char Separator => semicolons > commas && semicolons >= tabs ? ';' : tabs > commas ? '\t' : ',';

            // The record's values: cut at the separator where it's outside quotes, without the quotes, and each
            // trimmed (see Finish). A record with no separator is one value.
            public List<string> Values(char separator)
            {
                var values = new List<string>();
                var value = new StringBuilder();
                bool quoted = false;
                for (int i = start; i < end; i++)
                {
                    if (i == dropped) continue;
                    char c = text[i];
                    if (c == '"')
                    {
                        int after = i + 1 == dropped ? i + 2 : i + 1;   // the next character, past a dropped CR
                        if (quoted && after < end && text[after] == '"')
                        {
                            value.Append('"');
                            i = after;
                        }
                        else quoted = !quoted;
                    }
                    else if (c == separator && !quoted)
                    {
                        values.Add(Finish(value.ToString()));
                        value.Clear();
                    }
                    else value.Append(c);
                }
                values.Add(Finish(value.ToString()));
                return values;
            }

            // a value as the website uses it: trimmed of JavaScript's white space, then one apostrophe taken off
            // its start and one off its end where they are, and not trimmed again (so ' ' is a space)
            static string Finish(string raw)
            {
                string value = Js.Trim(raw);
                if (value.Length > 0 && value[0] == '\'') value = value.Substring(1);
                if (value.Length > 0 && value[value.Length - 1] == '\'') value = value.Substring(0, value.Length - 1);
                return value;
            }
        }
    }
}
