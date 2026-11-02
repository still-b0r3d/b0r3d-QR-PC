using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using B0r3dQR.Design;

namespace B0r3dQR.Batch
{
    // Reading CSV files as the website's src/utils/csv.ts reads them: commas, or the semicolons or tabs that
    // spreadsheets write in much of Europe; quotes round values that hold a separator, a line break or ""; rows
    // of nothing but spaces left out; values trimmed (and one ' taken off each end).
    static class Csv
    {
        // readCSVFile: UTF-8 when the bytes are UTF-8 (a byte order mark at the start dropped, as a browser's
        // decoder drops it), else Windows-1252, as spreadsheets in much of Europe save "CSV"
        public static string Read(byte[] bytes)
        {
            try
            {
                string text = new UTF8Encoding(false, true).GetString(bytes);
                return text.Length > 0 && text[0] == (char)0xFEFF ? text.Substring(1) : text;
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(1252).GetString(bytes);
            }
        }

        // detectDelimiter: whichever of , ; and tab the header row has most of outside quotes (commas when tied)
        public static char Delimiter(string header)
        {
            int commas = 0, semicolons = 0, tabs = 0;
            bool insideQuotes = false;
            foreach (char c in header)
            {
                if (c == '"') insideQuotes = !insideQuotes;
                else if (!insideQuotes && c == ',') commas++;
                else if (!insideQuotes && c == ';') semicolons++;
                else if (!insideQuotes && c == '\t') tabs++;
            }
            if (semicolons > commas && semicolons >= tabs) return ';';
            if (tabs > commas) return '\t';
            return ',';
        }

        // splitCSVLine: one row's values, trimmed; a separator inside quotes is kept, and "" inside quotes is a quote
        public static List<string> SplitLine(string line, char delimiter = ',')
        {
            var values = new List<string>();
            var value = new StringBuilder();
            bool insideQuotes = false;
            for (int j = 0; j < line.Length; j++)
            {
                char c = line[j];
                if (c == '"')
                {
                    if (insideQuotes && j + 1 < line.Length && line[j + 1] == '"')
                    {
                        value.Append('"');
                        j++;
                    }
                    else insideQuotes = !insideQuotes;
                }
                else if (c == delimiter && !insideQuotes)
                {
                    values.Add(Finish(value.ToString()));
                    value.Clear();
                }
                else value.Append(c);
            }
            values.Add(Finish(value.ToString()));
            return values;
        }

        static string Finish(string value) => Regex.Replace(Js.Trim(value), "^'|'\\z", "");

        // splitCSVRows: the rows, a line break inside quotes kept in its row; rows of nothing but spaces left out.
        // (The website takes the first \r out of a row that ends in a lone \n, and leaves the rest.)
        public static List<string> SplitRows(string content)
        {
            var rows = new List<string>();
            var row = new StringBuilder();
            bool insideQuotes = false;
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '"')
                {
                    insideQuotes = !insideQuotes;
                    row.Append(c);
                }
                else if (c == '\n' && !insideQuotes)
                {
                    Keep(rows, row.ToString(), true);
                    row.Clear();
                }
                else if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n' && !insideQuotes)
                {
                    Keep(rows, row.ToString(), false);
                    row.Clear();
                    i++;
                }
                else row.Append(c);
            }
            Keep(rows, row.ToString(), true);
            return rows;
        }

        static void Keep(List<string> rows, string row, bool dropFirstCr)
        {
            if (Js.Trim(row).Length == 0) return;
            int cr = dropFirstCr ? row.IndexOf('\r') : -1;
            rows.Add(cr >= 0 ? row.Remove(cr, 1) : row);
        }

        // isVCardStructure: a header row with firstName and lastName is a file of contacts
        public static bool IsContacts(string header, char delimiter)
        {
            List<string> headers = SplitLine(header.ToLowerInvariant(), delimiter);
            return headers.Contains("firstname") && headers.Contains("lastname");
        }
    }
}
