using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace B0r3dQR.Design
{
    // The website's data templates, in the order its list shows them.
    enum TemplateKind { Text, Url, Email, Phone, Sms, Wifi, VCard, Location, Event, Epc, Gs1DigitalLink }

    // What the website's data templates write (its src/utils/dataEncoding.ts), and which template a
    // text is, with the template's fields filled from it so that writing it again gives back the
    // same text. Fields go by the website's own names ("ssid", "password"...); "hidden" is "true" or
    // "false". Times in events are local to `zone` (the PC's own time zone unless a test says).
    static class Templates
    {
        // the website's names for them, as its files and tests use
        public static string Id(TemplateKind kind)
        {
            switch (kind)
            {
                case TemplateKind.Gs1DigitalLink: return "gs1dl";
                default: return kind.ToString().ToLowerInvariant();
            }
        }

        const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        static string Field(IDictionary<string, string> f, string name) => f != null && f.TryGetValue(name, out string v) && v != null ? v : "";

        // ---------- escaping ----------

        static string Escape(string v, string chars)
        {
            if (string.IsNullOrEmpty(v)) return "";
            var s = new StringBuilder(v.Length + 8);
            foreach (char c in v)
            {
                if (chars.IndexOf(c) >= 0) s.Append('\\');
                s.Append(c);
            }
            return s.ToString();
        }

        public static string EscapeVCard(string v) => Escape(v, "\\,;");

        public static string EscapeWifi(string v) => Escape(v, "\\;,:\"'");

        public static string EscapeICal(string v) => Regex.Replace(Escape(v, "\\,;"), "\r\n|\r|\n", "\\n");

        // \, \; \: \\ as the character, \n or \N as a line break
        public static string UnescapeText(string v) => Regex.Replace(v, @"\\([\\,;:nN])", m => m.Groups[1].Value == "n" || m.Groups[1].Value == "N" ? "\n" : m.Groups[1].Value);

        public static string UnescapeWifi(string v) => Regex.Replace(v, @"\\([\s\S])", "$1");

        // the parts between every separator that isn't escaped, escapes kept
        public static List<string> SplitUnescaped(string text, char separator)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    current.Append(c).Append(text[i + 1]);
                    i++;
                }
                else if (c == separator)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(c);
            }
            parts.Add(current.ToString());
            return parts;
        }

        // one line of a payment code: line breaks become spaces, and the ends are trimmed
        public static string EpcLine(string v) => string.IsNullOrEmpty(v) ? "" : Js.Trim(Regex.Replace(v, "\r\n|\r|\n", " "));

        // ---------- writing ----------

        // The text a template writes from its fields; "" when what it needs is missing. An event
        // also gets the time it was made (DTSTAMP) and an id of its own (UID), so those two lines
        // differ every time, as on the website.
        public static string Make(TemplateKind kind, IDictionary<string, string> f, TimeZoneInfo zone = null, DateTime? utcNow = null, string uid = null)
        {
            switch (kind)
            {
                case TemplateKind.Url:
                {
                    string url = Field(f, "url");
                    if (url.Length == 0) return "";
                    return url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal) ? url : "https://" + url;
                }
                case TemplateKind.Email:
                {
                    string address = Field(f, "address");
                    if (address.Length == 0) return "";
                    var parts = new List<string>();
                    foreach (string name in new[] { "subject", "body", "cc", "bcc" })
                        if (Field(f, name).Length > 0) parts.Add(name + "=" + Js.EncodeUriComponent(Field(f, name)));
                    return "mailto:" + address + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
                }
                case TemplateKind.Phone:
                    return Field(f, "phone").Length > 0 ? "tel:" + Field(f, "phone") : "";
                case TemplateKind.Sms:
                    return Field(f, "phone").Length > 0 ? "SMSTO:" + Field(f, "phone") + ":" + Field(f, "message") : "";
                case TemplateKind.Wifi:
                {
                    if (Field(f, "ssid").Length == 0) return "";
                    string ssid = EscapeWifi(Field(f, "ssid")), encryption = Field(f, "encryption");
                    string hidden = Field(f, "hidden") == "true" ? "H:true;" : "";
                    return encryption == "nopass"
                        ? "WIFI:T:nopass;S:" + ssid + ";" + hidden + ";"
                        : "WIFI:T:" + encryption + ";S:" + ssid + ";P:" + EscapeWifi(Field(f, "password")) + ";" + hidden + ";";
                }
                case TemplateKind.VCard:
                    return VCard(f);
                case TemplateKind.Location:
                {
                    string latitude = Field(f, "latitude"), longitude = Field(f, "longitude");
                    if (double.IsNaN(Js.ParseFloat(latitude)) || double.IsNaN(Js.ParseFloat(longitude))) return "";
                    return "geo:" + latitude + "," + longitude;
                }
                case TemplateKind.Event:
                    return Event(f, zone ?? TimeZoneInfo.Local, utcNow ?? DateTime.UtcNow, uid ?? NewUid());
                case TemplateKind.Epc:
                    return Epc(f);
                case TemplateKind.Gs1DigitalLink:
                    return Gs1DigitalLink(f);
                default:
                    return Field(f, "text");
            }
        }

        // vCard 2.1, 3.0 (the default) or 4.0, from "2", "3" or "4" or any way of writing them
        public static string VCardVersion(string version)
        {
            string v = Js.Trim(version ?? "");
            return v == "2" || v == "2.1" ? "2" : v == "4" || v == "4.0" ? "4" : "3";
        }

        static string VCard(IDictionary<string, string> f)
        {
            var lines = new List<string> { "BEGIN:VCARD" };
            string version = VCardVersion(Field(f, "version"));
            lines.Add("VERSION:" + (version == "2" ? "2.1" : version == "4" ? "4.0" : "3.0"));
            // vCard 2.1 reads text as ASCII unless a line says otherwise
            void Push(string property, string value) =>
                lines.Add(property + (version == "2" && Regex.IsMatch(value, "[^\\x20-\\x7e]") ? ";CHARSET=UTF-8" : "") + ":" + value);
            void Tel(string value, string v2, string v4, string v3)
            {
                if (value.Length == 0) return;
                if (version == "2") Push(v2, EscapeVCard(value));
                else if (version == "4") lines.Add(v4 + EscapeVCard(value));
                else lines.Add(v3 + EscapeVCard(value));
            }

            string first = EscapeVCard(Field(f, "firstName")), last = EscapeVCard(Field(f, "lastName")), org = Field(f, "org");
            if (first.Length > 0 || last.Length > 0)
            {
                Push("N", last + ";" + first + ";;;");
                Push("FN", Js.Trim(first + " " + last));
            }
            else if (org.Length > 0) Push("FN", EscapeVCard(org));   // a company's card is named after the company
            if (org.Length > 0) Push("ORG", EscapeVCard(org));
            if (Field(f, "position").Length > 0) Push("TITLE", EscapeVCard(Field(f, "position")));
            Tel(Field(f, "phoneWork"), "TEL;WORK;VOICE", "TEL;TYPE=work,voice;VALUE=uri:tel:", "TEL;TYPE=WORK,VOICE:");
            Tel(Field(f, "phonePrivate"), "TEL;HOME;VOICE", "TEL;TYPE=home,voice;VALUE=uri:tel:", "TEL;TYPE=HOME,VOICE:");
            Tel(Field(f, "phoneMobile"), "TEL;CELL;VOICE", "TEL;TYPE=cell,voice;VALUE=uri:tel:", "TEL;TYPE=CELL,VOICE:");
            string email = Field(f, "email");
            if (email.Length > 0)
            {
                if (version == "2") Push("EMAIL;INTERNET", EscapeVCard(email));
                else if (version == "4") lines.Add("EMAIL;TYPE=work:" + EscapeVCard(email));
                else lines.Add("EMAIL:" + EscapeVCard(email));
            }
            string website = Field(f, "website");
            if (website.Length > 0)
            {
                if (version == "4") lines.Add("URL;TYPE=work:" + EscapeVCard(website));
                else Push("URL", EscapeVCard(website));
            }
            string[] address = { "street", "city", "state", "zipcode", "country" };
            string[] parts = address.Select(name => EscapeVCard(Field(f, name))).ToArray();
            if (parts.Any(p => p.Length > 0))
            {
                string adr = string.Join(";", parts);
                if (version == "2") Push("ADR;WORK", ";;" + adr);
                else if (version == "4") lines.Add("ADR;TYPE=work:;;" + adr);
                else lines.Add("ADR;TYPE=WORK:;;" + adr);
            }
            lines.Add("END:VCARD");
            return string.Join("\n", lines);
        }

        static string Event(IDictionary<string, string> f, TimeZoneInfo zone, DateTime utcNow, string uid)
        {
            var lines = new List<string> { "BEGIN:VEVENT" };
            if (Field(f, "title").Length > 0) lines.Add("SUMMARY:" + EscapeICal(Field(f, "title")));
            if (Field(f, "location").Length > 0) lines.Add("LOCATION:" + EscapeICal(Field(f, "location")));
            string start = Field(f, "startTime").Length > 0 ? ICalTime(Field(f, "startTime"), zone) : "";
            string end = Field(f, "endTime").Length > 0 ? ICalTime(Field(f, "endTime"), zone) : "";
            if (start.Length > 0) lines.Add("DTSTART:" + start);
            if (end.Length > 0) lines.Add("DTEND:" + end);
            lines.Add("DTSTAMP:" + UtcStamp(utcNow));   // calendars need both of these in every event
            lines.Add("UID:" + uid + "@qr.b0r3d.org");
            lines.Add("END:VEVENT");
            return "BEGIN:VCALENDAR\nVERSION:2.0\n" + string.Join("\n", lines) + "\nEND:VCALENDAR";
        }

        // 18 characters: nine random bytes, each in base 36 and two long, as the website makes them
        public static string NewUid()
        {
            var bytes = new byte[9];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var s = new StringBuilder(18);
            foreach (byte b in bytes) s.Append(Base36(b).PadLeft(2, '0'));
            return s.ToString();
        }

        static string Base36(int n)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            return n < 36 ? digits[n].ToString() : digits[n / 36].ToString() + digits[n % 36];
        }

        static string UtcStamp(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        // A time the way the website's browser reads it (new Date(text)), written in UTC: a date
        // with a time and no zone is local, a date alone is midnight UTC; "" when it isn't one.
        static string ICalTime(string text, TimeZoneInfo zone)
        {
            Match m = Regex.Match(text, @"^([0-9]{4})-([0-9]{2})-([0-9]{2})(?:T([0-9]{2}):([0-9]{2})(?::([0-9]{2})(?:\.([0-9]{1,3})[0-9]*)?)?(Z|[+-][0-9]{2}:[0-9]{2})?)?\z");
            if (!m.Success) return "";
            int Part(int g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture) : 0;
            int year = Part(1), month = Part(2), day = Part(3), hour = Part(4), minute = Part(5), second = Part(6);
            if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(Math.Max(1, year), month) || hour > 23 || minute > 59 || second > 59 || year < 1) return "";
            var wall = new DateTime(year, month, day, hour, minute, second, m.Groups[7].Success ? int.Parse(m.Groups[7].Value.PadRight(3, '0'), CultureInfo.InvariantCulture) : 0);
            DateTime utc;
            if (!m.Groups[4].Success) utc = wall;                                     // a date alone: midnight UTC
            else if (m.Groups[8].Success && m.Groups[8].Value == "Z") utc = wall;
            else if (m.Groups[8].Success)
            {
                string o = m.Groups[8].Value;
                var offset = new TimeSpan(int.Parse(o.Substring(1, 2), CultureInfo.InvariantCulture), int.Parse(o.Substring(4, 2), CultureInfo.InvariantCulture), 0);
                utc = o[0] == '+' ? wall - offset : wall + offset;
            }
            else utc = LocalToUtc(wall, zone);
            return UtcStamp(utc);
        }

        // A local time in UTC, as JavaScript does it: a time skipped when the clocks go forward
        // takes the offset from before; one that happens twice when they go back, the first.
        public static DateTime LocalToUtc(DateTime local, TimeZoneInfo zone)
        {
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) return DateTime.SpecifyKind(local - zone.GetUtcOffset(local.AddDays(-1)), DateTimeKind.Utc);
            if (zone.IsAmbiguousTime(local)) return DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }

        static string Epc(IDictionary<string, string> f)
        {
            if (Field(f, "name").Length == 0 || Field(f, "iban").Length == 0) return "";
            string version = Field(f, "version") == "001" ? "001" : "002";
            string bic = Regex.Replace(Field(f, "bic"), Js.Space + "+", "").ToUpperInvariant();
            if (version == "001" && bic.Length == 0) return "";   // version 001 needs the BIC
            string name = Cut(EpcLine(Field(f, "name")), 70);
            string iban = Regex.Replace(Field(f, "iban"), Js.Space + "+", "").ToUpperInvariant();
            string amount = "";
            string typed = Field(f, "amount");
            if (typed.Length > 0)
            {
                int comma = typed.IndexOf(',');
                double value = Js.ParseFloat(comma < 0 ? typed : typed.Substring(0, comma) + "." + typed.Substring(comma + 1));
                if (!double.IsNaN(value) && value > 0) amount = "EUR" + Js.ToFixed2(value);
            }
            string purpose = Cut(EpcLine(Field(f, "purpose")), 4).ToUpperInvariant();
            string reference = Cut(EpcLine(Field(f, "remittanceReference")), 35);
            string text = reference.Length > 0 ? "" : Cut(EpcLine(Field(f, "remittanceText")), 140);   // one or the other
            string originator = Cut(EpcLine(Field(f, "originatorInfo")), 70);
            var lines = new List<string> { "BCD", version, "1", "SCT", bic, name, iban, amount, purpose, reference, text, originator };
            while (lines.Count > 7 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);   // the IBAN line stays
            return string.Join("\n", lines);
        }

        static string Cut(string s, int length) => s.Length <= length ? s : s.Substring(0, length);

        // An IBAN with the right check digits (ISO 13616, mod 97); spaces and case don't matter.
        public static bool ValidIban(string value)
        {
            string iban = Regex.Replace(value ?? "", Js.Space + "+", "").ToUpperInvariant();
            if (!Regex.IsMatch(iban, @"^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}\z")) return false;
            string rearranged = iban.Substring(4) + iban.Substring(0, 4);
            int remainder = 0;
            foreach (char c in rearranged)
            {
                string digits = c >= 'A' ? (c - 55).ToString(CultureInfo.InvariantCulture) : c.ToString();
                foreach (char d in digits) remainder = (remainder * 10 + (d - '0')) % 97;
            }
            return remainder == 1;
        }

        public const string Gs1Domain = "https://id.gs1.org";

        // What's wrong with a GTIN (8, 12, 13 or 14 digits, check digit included), or null.
        public static string GtinProblem(string value)
        {
            string gtin = Regex.Replace(value ?? "", "(" + Js.Space + "|-)", "");
            if (gtin.Length == 0) return "Enter the product number (GTIN).";
            if (!Regex.IsMatch(gtin, @"^[0-9]+\z")) return "A GTIN has digits only.";
            if (!(gtin.Length == 8 || gtin.Length == 12 || gtin.Length == 13 || gtin.Length == 14)) return "A GTIN has 8, 12, 13 or 14 digits, check digit included.";
            int expected = Gs1CheckDigit(gtin.Substring(0, gtin.Length - 1));
            return gtin[gtin.Length - 1] - '0' != expected ? "The check digit (the last digit) should be " + expected + "." : null;
        }

        // GS1's check digit: weights 3 and 1 from the right
        public static int Gs1CheckDigit(string body)
        {
            int sum = 0;
            for (int i = 0; i < body.Length; i++) sum += (body[body.Length - 1 - i] - '0') * (i % 2 == 0 ? 3 : 1);
            return (10 - sum % 10) % 10;
        }

        static string Gs1DigitalLink(IDictionary<string, string> f)
        {
            string gtin = Regex.Replace(Field(f, "gtin"), "(" + Js.Space + "|-)", "");
            if (GtinProblem(gtin) != null) return "";
            string domain = Js.Trim(Field(f, "domain"));
            domain = Regex.Replace(domain.Length > 0 ? domain : Gs1Domain, "/+\\z", "");
            string link = Regex.IsMatch(domain, "^https?:/{2}", I) ? domain : "https:/" + "/" + domain;
            link += "/01/" + gtin.PadLeft(14, '0');   // the key, then its qualifiers, in this order
            if (Field(f, "lot").Length > 0) link += "/10/" + Js.EncodeUriComponent(Field(f, "lot"));
            if (Field(f, "serial").Length > 0) link += "/21/" + Js.EncodeUriComponent(Field(f, "serial"));
            Match expiry = Regex.Match(Field(f, "expiry"), @"^[0-9]{2}([0-9]{2})-([0-9]{2})-([0-9]{2})\z");
            if (expiry.Success) link += "?17=" + expiry.Groups[1].Value + expiry.Groups[2].Value + expiry.Groups[3].Value;
            return link;
        }

        // Only links this template can write back unchanged count as GS1 Digital Links; anything
        // with other qualifiers or attributes stays a plain web address.
        static readonly Regex Gs1Link = new Regex(@"^(https?:/{2}[^?#]*?)/01/([0-9]{14})(?:/10/([^/?#]+))?(?:/21/([^/?#]+))?/?(?:\?17=([0-9]{6}))?\z", I);

        // ---------- reading ----------

        // Which template a text is, and its fields, as the website reads it ("text" for anything
        // it doesn't know). Writing those fields again gives the same text back.
        public static TemplateKind Detect(string data, out Dictionary<string, string> fields, TimeZoneInfo zone = null)
        {
            fields = new Dictionary<string, string>(StringComparer.Ordinal);
            zone = zone ?? TimeZoneInfo.Local;
            if (string.IsNullOrEmpty(data))
            {
                fields["text"] = data ?? "";
                return TemplateKind.Text;
            }
            if (Regex.IsMatch(data, "^BCD\r?\n"))
            {
                string[] lines = data.Replace("\r", "").Split('\n');
                string Line(int n) => n < lines.Length ? lines[n] : "";
                fields["version"] = Line(1) == "001" ? "001" : "002";
                fields["bic"] = Line(4);
                fields["name"] = Line(5);
                fields["iban"] = Line(6);
                fields["amount"] = Line(7).StartsWith("EUR", StringComparison.Ordinal) ? Line(7).Substring(3) : "";
                fields["purpose"] = Line(8);
                fields["remittanceReference"] = Line(9);
                fields["remittanceText"] = Line(10);
                fields["originatorInfo"] = Line(11);
                return TemplateKind.Epc;
            }
            if (Regex.IsMatch(data, "^" + Js.Space + "*BEGIN:VCARD", I))
            {
                fields = ReadVCard(data);
                return TemplateKind.VCard;
            }
            Match gs1 = Gs1Link.Match(data);
            if (gs1.Success && GtinProblem(gs1.Groups[2].Value) == null)
            {
                string Decode(Group g) => !g.Success ? "" : Js.DecodeUriComponent(g.Value) ?? g.Value;
                string date = gs1.Groups[5].Success ? gs1.Groups[5].Value : "";
                fields["domain"] = gs1.Groups[1].Value;
                fields["gtin"] = gs1.Groups[2].Value;
                fields["lot"] = Decode(gs1.Groups[3]);
                fields["serial"] = Decode(gs1.Groups[4]);
                fields["expiry"] = date.Length > 0 ? "20" + date.Substring(0, 2) + "-" + date.Substring(2, 2) + "-" + date.Substring(4) : "";
                return TemplateKind.Gs1DigitalLink;
            }
            if (Regex.IsMatch(data, "^https?:/{2}", I))
            {
                fields["url"] = data;
                return TemplateKind.Url;
            }
            if (Regex.IsMatch(data, "^mailto:", I))
            {
                string[] parts = data.Substring(7).Split('?');
                fields["address"] = parts[0];
                if (parts.Length > 1 && parts[1].Length > 0)
                {
                    string query = string.Join("?", parts.Skip(1));
                    foreach (string name in new[] { "subject", "body", "cc", "bcc" }) fields[name] = Js.SearchParam(query, name) ?? "";
                }
                return TemplateKind.Email;
            }
            if (Regex.IsMatch(data, "^tel:", I))
            {
                fields["phone"] = data.Substring(4);
                return TemplateKind.Phone;
            }
            if (Regex.IsMatch(data, "^(SMSTO|sms):", I))
            {
                ReadSms(data, fields);
                return TemplateKind.Sms;
            }
            if (Regex.IsMatch(data, "^WIFI:", I))
            {
                ReadWifi(data, fields);
                return TemplateKind.Wifi;
            }
            if (Regex.IsMatch(data, "^geo:", I))
            {
                string[] coords = Regex.Split(data.Substring(4), "[;?]")[0].Split(',');
                if (coords.Length >= 2)
                {
                    fields["latitude"] = Js.Trim(coords[0]);
                    fields["longitude"] = Js.Trim(coords[1]);
                }
                return TemplateKind.Location;
            }
            if (Regex.IsMatch(data, "^" + Js.Space + "*BEGIN:(VCALENDAR|VEVENT)", I))
            {
                ReadEvent(data, fields, zone);
                return TemplateKind.Event;
            }
            fields["text"] = data;
            return TemplateKind.Text;
        }

        static void ReadSms(string data, Dictionary<string, string> fields)
        {
            Match smsto = Regex.Match(data, @"^SMSTO:([^:]*)(?::([\s\S]*))?\z", I);
            if (smsto.Success)
            {
                fields["phone"] = Js.Trim(smsto.Groups[1].Value);
                fields["message"] = smsto.Groups[2].Success ? smsto.Groups[2].Value : "";
                return;
            }
            Match uri = Regex.Match(data, @"^sms:([^?]*)(?:\?([\s\S]*))?\z", I);   // sms:+123?body=Hello (RFC 5724)
            if (!uri.Success)
            {
                fields["phone"] = "";
                fields["message"] = "";
                return;
            }
            string phone = Js.DecodeUriComponent(uri.Groups[1].Value) ?? uri.Groups[1].Value;
            fields["phone"] = Js.Trim(phone);
            fields["message"] = Js.SearchParam(uri.Groups[2].Success ? uri.Groups[2].Value : "", "body") ?? "";
        }

        static void ReadWifi(string data, Dictionary<string, string> fields)
        {
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string field in SplitUnescaped(Regex.Replace(data, "^WIFI:", "", I), ';'))
            {
                int colon = field.IndexOf(':');
                if (colon <= 0) continue;
                string key = Js.Trim(field.Substring(0, colon)).ToUpperInvariant();
                string value = field.Substring(colon + 1);
                // a value that could pass for hex may be in double quotes
                if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal) && !value.EndsWith("\\\"", StringComparison.Ordinal))
                    value = value.Substring(1, value.Length - 2);
                if (!found.ContainsKey(key)) found[key] = UnescapeWifi(value);
            }
            string Get(string key) => found.TryGetValue(key, out string v) ? v : "";
            string type = Get("T").ToUpperInvariant();
            fields["ssid"] = Get("S");
            fields["encryption"] = type == "WEP" ? "WEP" : type == "SAE" ? "SAE" : type.StartsWith("WPA", StringComparison.Ordinal) ? "WPA" : "nopass";
            fields["password"] = Get("P");
            fields["hidden"] = Get("H").ToLowerInvariant() == "true" ? "true" : "false";
        }

        // ---------- vCard and iCalendar lines ----------

        sealed class ContentLine
        {
            public string Name, Params, Value;   // name and parameters in capitals; the value still escaped
        }

        // the lines of a vCard or iCalendar text: folded lines joined, each split into its name,
        // parameters and value (a group prefix, "item1.", left off the name)
        static List<ContentLine> ContentLines(string data)
        {
            string[] raw = Regex.Replace(Regex.Replace(data, "\r\n?", "\n"), "\n[ \t]", "").Split('\n');
            var result = new List<ContentLine>();
            for (int i = 0; i < raw.Length; i++)
            {
                string line = raw[i];
                // vCard 2.1's quoted-printable values go on after a trailing "=" without a leading space
                if (Regex.IsMatch(line, "^[^:]*QUOTED-PRINTABLE[^:]*:", I))
                    while (line.EndsWith("=", StringComparison.Ordinal) && i + 1 < raw.Length) line = line.Substring(0, line.Length - 1) + raw[++i];
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string[] head = line.Substring(0, colon).Split(';');
                result.Add(new ContentLine
                {
                    Name = Js.Trim(head[0].Split('.').Last()).ToUpperInvariant(),
                    Params = string.Join(";", head.Skip(1)).ToUpperInvariant(),
                    Value = line.Substring(colon + 1),
                });
            }
            return result;
        }

        // vCard 2.1's quoted-printable ("=C3=BC" for ü) as UTF-8, as a browser's TextDecoder reads it
        static string QuotedPrintable(string value)
        {
            var bytes = new List<byte>();
            for (int i = 0; i < value.Length; i++)
            {
                string hex = value[i] == '=' ? value.Substring(i + 1, Math.Min(2, value.Length - i - 1)) : "";
                if (Regex.IsMatch(hex, "^[0-9A-Fa-f]{2}\\z"))
                {
                    bytes.Add(Convert.ToByte(hex, 16));
                    i += 2;
                }
                else bytes.AddRange(char.IsSurrogate(value[i]) ? new byte[] { 0xEF, 0xBF, 0xBD } : Encoding.UTF8.GetBytes(value[i].ToString()));
            }
            byte[] all = bytes.ToArray();
            int skip = all.Length >= 3 && all[0] == 0xEF && all[1] == 0xBB && all[2] == 0xBF ? 3 : 0;   // TextDecoder drops a byte-order mark
            return Encoding.UTF8.GetString(all, skip, all.Length - skip);
        }

        static string RawValue(ContentLine line) => line.Params.Contains("QUOTED-PRINTABLE") ? QuotedPrintable(line.Value) : line.Value;

        static string TextValue(ContentLine line) => Js.Trim(UnescapeText(RawValue(line)));

        static List<string> Components(ContentLine line) => SplitUnescaped(RawValue(line), ';').Select(p => Js.Trim(UnescapeText(p))).ToList();

        static Dictionary<string, string> ReadVCard(string data)
        {
            var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
            List<ContentLine> lines = ContentLines(data);
            ContentLine First(string name) => lines.FirstOrDefault(l => l.Name == name);

            ContentLine version = First("VERSION");
            parsed["version"] = version != null ? VCardVersion(version.Value) : "3";
            ContentLine n = First("N");
            if (n != null)
            {
                List<string> parts = Components(n);
                string last = parts.Count > 0 ? parts[0] : "", first = parts.Count > 1 ? parts[1] : "";
                if (last.Length > 0 || first.Length > 0)
                {
                    parsed["lastName"] = last;
                    parsed["firstName"] = first;
                }
            }
            ContentLine fn = First("FN"), org = First("ORG");
            if (!Has(parsed, "firstName") && !Has(parsed, "lastName") && fn != null)
            {
                string name = TextValue(fn);
                // a company's card is named after the company: it isn't a person
                if (org == null || string.Join(", ", Components(org)) != name)
                {
                    string[] parts = name.Split(' ');
                    parsed["firstName"] = parts[0];
                    if (parts.Length > 1) parsed["lastName"] = string.Join(" ", parts.Skip(1));
                }
            }
            if (org != null) parsed["org"] = string.Join(", ", Components(org).Where(p => p.Length > 0));
            ContentLine title = First("TITLE");
            if (title != null) parsed["position"] = TextValue(title);
            foreach (ContentLine line in lines.Where(l => l.Name == "TEL"))
            {
                string phone = Regex.Replace(TextValue(line), "^tel:", "", I);
                string key = Regex.IsMatch(line.Params, @"\bWORK\b", RegexOptions.ECMAScript) ? "phoneWork"
                    : Regex.IsMatch(line.Params, @"\bHOME\b", RegexOptions.ECMAScript) ? "phonePrivate"
                    : Regex.IsMatch(line.Params, @"\b(CELL|MOBILE)\b", RegexOptions.ECMAScript) ? "phoneMobile"
                    : !Has(parsed, "phoneWork") && !Has(parsed, "phonePrivate") && !Has(parsed, "phoneMobile") ? "phoneMobile"
                    : null;
                if (key != null && !Has(parsed, key)) parsed[key] = phone;
            }
            ContentLine email = First("EMAIL"), url = First("URL"), adr = First("ADR");
            if (email != null) parsed["email"] = TextValue(email);
            if (url != null) parsed["website"] = TextValue(url);
            if (adr != null)
            {
                List<string> parts = Components(adr);
                string Part(int i) => i < parts.Count ? parts[i] : "";
                parsed["street"] = Part(2);
                parsed["city"] = Part(3);
                parsed["state"] = Part(4);
                parsed["zipcode"] = Part(5);
                parsed["country"] = Part(6);
            }
            return parsed;
        }

        // set and not empty, as JavaScript's `if (parsed.x)` sees it
        static bool Has(Dictionary<string, string> d, string key) => d.TryGetValue(key, out string v) && v.Length > 0;

        static void ReadEvent(string data, Dictionary<string, string> fields, TimeZoneInfo zone)
        {
            List<ContentLine> lines = ContentLines(data);
            ContentLine First(string name) => lines.FirstOrDefault(l => l.Name == name);
            ContentLine summary = First("SUMMARY"), location = First("LOCATION"), start = First("DTSTART"), end = First("DTEND");
            if (summary != null) fields["title"] = TextValue(summary);
            if (location != null) fields["location"] = TextValue(location);
            if (start != null) fields["startTime"] = LocalInput(start.Value, zone);
            if (end != null) fields["endTime"] = LocalInput(end.Value, zone);
        }

        // An iCalendar date or time as a form's "2026-11-01T14:30": a UTC time (ending in Z) in
        // local time, a floating time or a date as written; "" when it isn't one.
        public static string LocalInput(string value, TimeZoneInfo zone)
        {
            Match m = Regex.Match(Js.Trim(value), @"^([0-9]{4})([0-9]{2})([0-9]{2})(?:T([0-9]{2})([0-9]{2})([0-9]{2})?(Z)?)?\z");
            if (!m.Success) return "";
            string G(int g, string empty) => m.Groups[g].Success ? m.Groups[g].Value : empty;
            if (!m.Groups[7].Success) return G(1, "") + "-" + G(2, "") + "-" + G(3, "") + "T" + G(4, "00") + ":" + G(5, "00");
            int N(int g) => int.Parse(G(g, "0"), CultureInfo.InvariantCulture);
            try
            {
                // as Date.UTC does: years 0-99 are 1900-1999, and a month or day past the end
                // carries into the next
                DateTime utc = new DateTime(N(1) <= 99 ? 1900 + N(1) : N(1), 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(N(2) - 1).AddDays(N(3) - 1).AddHours(N(4)).AddMinutes(N(5)).AddSeconds(N(6));
                DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
                return local.Year.ToString(CultureInfo.InvariantCulture) + local.ToString("-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return "";
            }
        }
    }
}
