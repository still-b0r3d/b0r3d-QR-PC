using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace B0r3dQR.Design
{
    // One code in Recent codes, as the website keeps one: what the list shows, and the design
    // with its text, to open again.
    sealed class RecentCode
    {
        public int Id;
        public string Key;                            // the same text again replaces it
        public string Label, Title;                   // "URL", "WiFi"...; the text shortened, never a Wi-Fi password
        public bool Sensitive;                        // holds a Wi-Fi password, so the list shows no picture of it
        public List<string> Omitted = new List<string>();   // "logo", "background": too big to keep
        public long SavedAt;                          // milliseconds since 1970, UTC
        public string Design;                         // the design file's text, the code's text in it

        public string Name => Title.Length > 0 ? Label + ": " + Title : Label;

        public int Size => Design.Length;

        public DateTime SavedLocal => DateTimeOffset.FromUnixTimeMilliseconds(SavedAt).LocalDateTime;   // a time read from the file is checked first
    }

    // why Recent codes can't be used just now
    enum RecentTrouble
    {
        None,
        Busy,        // the file couldn't be opened: another program has it, or Windows said no
        Damaged,     // it isn't a Recent codes file this program can read
        Newer,       // a later b0r3d QR's, which this one leaves alone
        Unwritable,  // it couldn't be written
    }

    // Recent codes (the website's src/utils/recentCodes.ts and recentCodesDb.ts): the last 20
    // codes saved, copied or dragged out, newest first, kept in one file in the data folder,
    // never anywhere else. Whether codes are added, and whether the note about them has been
    // shown, live in the settings.
    static class RecentCodes
    {
        public const int Most = 20;
        public const string FileName = "b0r3dQR recent.json";
        public const int MostImageLength = 2 * 1024 * 1024;   // a logo or frame picture longer than this (as a data: URL) isn't kept
        const int TitleLength = 80;
        const int MostId = int.MaxValue / 2;                  // far more codes than anyone saves, with room to count on
        const long LatestTime = 253402214400000;              // 9999-12-31 UTC, the last day a date can show

        public static string Path => System.IO.Path.Combine(DataFolder.Path, FileName);

        // ---------- what the list says about a code ----------

        // The kind of content as the website's list names it ("URL", "WiFi", "Contact Card"...)
        public static string Label(string data)
        {
            string value = Js.Trim(data ?? "");
            Match gs1 = Regex.Match(value, @"^\([0-9]{2,4}\)");
            if (gs1.Success && gs1.Length < value.Length && !Js.IsSpace(value[gs1.Length])) return "GS1 data";   // "(01)09506000134352(10)ABC"
            switch (Templates.Detect(data ?? "", out _))
            {
                case TemplateKind.Url: return "URL";
                case TemplateKind.Gs1DigitalLink: return "Product link (GS1 Digital Link)";
                case TemplateKind.Email: return "Email";
                case TemplateKind.Phone: return "Phone Number";
                case TemplateKind.Sms: return "SMS";
                case TemplateKind.Wifi: return "WiFi";
                case TemplateKind.VCard: return "Contact Card";
                case TemplateKind.Event: return "Calendar Event";
                case TemplateKind.Location: return "Location";
                case TemplateKind.Epc: return "SEPA Payment";
            }
            if (Checks.BareDomain(value)) return "URL";
            if (Regex.IsMatch(value, @"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\z")) return "Email";
            int digits = value.Count(c => c >= '0' && c <= '9');
            bool international = value.Length > 1 && value[0] == '+' && value.Skip(1).All(c => (c >= '0' && c <= '9') || Js.IsSpace(c) || "().-".IndexOf(c) >= 0);
            if (international && digits >= 7 && digits <= 15) return "Phone Number";   // only numbers written the international way
            return "Text";
        }

        // the text, on one line and at most 80 characters
        static string Shorten(string text)
        {
            string line = Js.Trim(Regex.Replace(text ?? "", Js.Space + "+", " "));
            if (line.Length <= TitleLength) return line;
            string cut = line.Substring(0, TitleLength - 1);
            int end = cut.Length;
            while (end > 0 && Js.IsSpace(cut[end - 1])) end--;
            return cut.Substring(0, end) + "…";
        }

        // what the list shows for a code's text: its kind, a title (never a Wi-Fi password), and
        // whether it holds a secret
        public static void Describe(string data, out string label, out string title, out bool sensitive)
        {
            label = Label(data);
            sensitive = false;
            TemplateKind kind = Templates.Detect(data ?? "", out Dictionary<string, string> f);
            string Field(string name) => Js.Trim(f.TryGetValue(name, out string v) ? v : "");
            switch (kind)
            {
                case TemplateKind.Wifi:
                    title = Shorten(Field("ssid"));
                    sensitive = Field("password").Length > 0;
                    break;
                case TemplateKind.VCard:
                    string name = string.Join(" ", new[] { Field("firstName"), Field("lastName") }.Where(s => s.Length > 0));
                    title = Shorten(name.Length > 0 ? name : Field("org"));
                    break;
                case TemplateKind.Event:
                    title = Shorten(Field("title"));
                    break;
                case TemplateKind.Epc:
                    string amount = Field("amount");
                    title = Shorten(string.Join(", ", new[] { Field("name"), amount.Length > 0 ? "EUR " + amount : "" }.Where(s => s.Length > 0)));
                    break;
                default:
                    title = Shorten(data);
                    break;
            }
        }

        // A code to keep: the design with its text, without pictures too big to keep.
        public static RecentCode New(QrDesign design, long savedAt)
        {
            QrDesign kept = design.Clone();
            var omitted = new List<string>();
            if (TooBig(kept.Logo))
            {
                kept.Logo = "";
                omitted.Add("logo");
            }
            if (kept.Frame != null && TooBig(kept.Frame.BackgroundImage))
            {
                kept.Frame.BackgroundImage = null;
                omitted.Add("background");
            }
            Describe(kept.Data, out string label, out string title, out bool sensitive);
            return new RecentCode
            {
                Key = "qr:qr:" + (kept.Data ?? ""),
                Label = label,
                Title = title,
                Sensitive = sensitive,
                Omitted = omitted,
                SavedAt = savedAt,
                Design = DesignFile.Write(kept),
            };
        }

        static bool TooBig(string picture) => picture != null && picture.StartsWith("data:", StringComparison.Ordinal) && picture.Length > MostImageLength;

        public static string SizeText(long bytes) =>
            bytes < 1024 * 1024
                ? Math.Max(1, (long)Math.Round(bytes / 1024.0, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + " KB"
                : (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

        // ---------- the file ----------

        // The codes kept, newest first; null when the file is there but can't be used (`trouble`
        // says why), and then it's left as it is, never written over, until Clear all.
        public static List<RecentCode> Load() => Load(out _, out _);

        public static List<RecentCode> Load(out RecentTrouble trouble) => Load(out trouble, out _);

        // ...and the id the next code gets: the file counts on past codes deleted, so an id held
        // from before (the note's, another window's) never means a newer code
        static List<RecentCode> Load(out RecentTrouble trouble, out int next)
        {
            trouble = RecentTrouble.None;
            next = 1;
            try
            {
                if (!DataFolder.CanKeep || !File.Exists(Path)) return new List<RecentCode>();
                var file = Json.Parse(File.ReadAllText(Path, Encoding.UTF8)) as Dictionary<string, object>;
                if (file != null && Json.Num(file, "v") is double v && v > 1)
                {
                    trouble = RecentTrouble.Newer;
                    return null;
                }
                if (file == null || !(file.TryGetValue("codes", out object list) && list is List<object> codes))
                {
                    trouble = RecentTrouble.Damaged;
                    return null;
                }
                var result = new List<RecentCode>();
                foreach (object item in codes)
                {
                    // a code that isn't whole is left out (and goes when the file is next written)
                    if (!(item is Dictionary<string, object> c) || !(Json.Num(c, "id") is double id && Whole(id, 1, MostId)) || Json.Str(c, "key") == null ||
                        !(Json.Num(c, "savedAt") is double at && Whole(at, 0, LatestTime))) continue;
                    Dictionary<string, object> design = Json.Obj(c, "design");
                    if (design == null) continue;
                    result.Add(new RecentCode
                    {
                        Id = (int)id,
                        Key = Json.Str(c, "key"),
                        Label = Json.Str(c, "label", "Text"),
                        Title = Json.Str(c, "title", ""),
                        Sensitive = Json.Bool(c, "sensitive") == true,
                        Omitted = (c.TryGetValue("omitted", out object o) && o is List<object> names ? names.OfType<string>() : Enumerable.Empty<string>()).ToList(),
                        SavedAt = (long)at,
                        Design = Json.Write(design) + "\n",
                    });
                }
                int after = result.Count == 0 ? 1 : result.Max(c => c.Id) + 1;
                next = Json.Num(file, "next") is double n && Whole(n, 1, MostId) ? Math.Max((int)n, after) : after;
                return Newest(result);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                trouble = RecentTrouble.Busy;
                return null;
            }
            catch (JsonException)
            {
                trouble = RecentTrouble.Damaged;
                return null;
            }
        }

        static bool Whole(double x, double least, double most) => x >= least && x <= most && Math.Floor(x) == x;

        static List<RecentCode> Newest(IEnumerable<RecentCode> codes) => codes.OrderByDescending(c => c.SavedAt).ThenByDescending(c => c.Id).ToList();

        // writes the codes, all of them or none (false when they couldn't be kept)
        static bool Write(List<RecentCode> codes, int next)
        {
            var list = new List<object>();
            foreach (RecentCode c in Newest(codes))
            {
                list.Add(new Dictionary<string, object>
                {
                    ["id"] = c.Id,
                    ["key"] = c.Key,
                    ["label"] = c.Label,
                    ["title"] = c.Title,
                    ["sensitive"] = c.Sensitive,
                    ["omitted"] = c.Omitted.Cast<object>().ToList(),
                    ["savedAt"] = c.SavedAt,
                    ["design"] = Json.Parse(c.Design),
                });
            }
            return DataFolder.TryWrite(FileName, Json.Write(new Dictionary<string, object> { ["v"] = 1, ["next"] = next, ["codes"] = list }) + "\n");
        }

        // Adds a code: the same text again replaces the old one, and past 20 the oldest go. Its id,
        // or 0 when it couldn't be kept (`trouble` says why).
        public static int Add(RecentCode code) => Add(code, out _);

        public static int Add(RecentCode code, out RecentTrouble trouble)
        {
            List<RecentCode> codes = Load(out trouble, out int next);
            if (codes == null) return 0;
            codes.RemoveAll(c => c.Key == code.Key);
            codes = Newest(codes).Take(Most - 1).ToList();
            code.Id = next;
            codes.Add(code);
            if (Write(codes, next + 1)) return code.Id;
            trouble = RecentTrouble.Unwritable;
            return 0;
        }

        public static bool Delete(int id)
        {
            List<RecentCode> codes = Load(out _, out int next);
            if (codes == null) return false;
            codes.RemoveAll(c => c.Id == id);
            return Write(codes, next);
        }

        // every code gone, the count of ids going on (a file that couldn't be read is written over
        // too, and starts again)
        public static bool Clear()
        {
            Load(out _, out int next);
            return Write(new List<RecentCode>(), next);
        }

        // the design to open, checked as a design file is; null when it's gone or can't be used
        public static QrDesign Open(int id, QrDesign current)
        {
            RecentCode code = Load()?.FirstOrDefault(c => c.Id == id);
            if (code == null) return null;
            try
            {
                return DesignFile.Read(code.Design, current);
            }
            catch (DesignFileException)
            {
                return null;
            }
        }
    }
}
