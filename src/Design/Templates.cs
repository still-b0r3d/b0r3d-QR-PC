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

    // What the website's data templates write, and which template a text is, with the template's
    // fields filled from it so that writing it again gives back the same text. Fields go by the
    // website's own names ("ssid", "password"...); "hidden" is "true" or "false". Times in events
    // are local to `zone` (the PC's own time zone unless a test says).
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

        // vCard text (RFC 6350 3.4): a backslash before each \ , and ;
        static string VCardText(string value) => Regex.Replace(value, @"[\\,;]", @"\$&");

        // a Wi-Fi code's name or password: a backslash before each \ ; , : " and '
        static string WifiText(string value) => Regex.Replace(value, @"[\\;,:""']", @"\$&");

        // iCalendar text (RFC 5545 3.3.11): a backslash before each \ , and ;, and every line break
        // (CR LF, CR or LF) written as \n
        static string ICalText(string value) => Regex.Replace(value, "\r\n|[\r\n]|[\\\\,;]", m => m.Value[0] == '\r' || m.Value[0] == '\n' ? "\\n" : "\\" + m.Value);

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

        // a payment code's line: each line break (CR LF, CR or LF) a space, then no space at either end
        public static string EpcLine(string value) => Js.Trim(Regex.Replace(value ?? "", "\r\n|[\r\n]", " "));

        // ---------- writing ----------

        // The text a template writes from its fields; "" when what it needs is missing. An event
        // also gets the time it was made (DTSTAMP) and an id of its own (UID), so those two lines
        // differ every time, as on the website.
        public static string Make(TemplateKind kind, IDictionary<string, string> f, TimeZoneInfo zone = null, DateTime? utcNow = null, string uid = null)
        {
            switch (kind)
            {
                case TemplateKind.Url: return Link(Field(f, "url"));
                case TemplateKind.Email: return Mail(f);
                case TemplateKind.Phone: return Field(f, "phone").Length == 0 ? "" : "tel:" + Field(f, "phone");
                case TemplateKind.Sms: return Field(f, "phone").Length == 0 ? "" : "SMSTO:" + Field(f, "phone") + ":" + Field(f, "message");
                case TemplateKind.Wifi: return Wifi(f);
                case TemplateKind.VCard: return VCard(f);
                case TemplateKind.Location: return Geo(Field(f, "latitude"), Field(f, "longitude"));
                case TemplateKind.Event: return Event(f, zone ?? TimeZoneInfo.Local, utcNow ?? DateTime.UtcNow, uid ?? NewUid());
                case TemplateKind.Epc: return Epc(f);
                case TemplateKind.Gs1DigitalLink: return Gs1DigitalLink(f);
                default: return Field(f, "text");
            }
        }

        // a web address, made a secure one when it doesn't say http:// or https:// (in small letters)
        static string Link(string url) => url.Length == 0 ? "" : Regex.IsMatch(url, "^https?://") ? url : "https://" + url;

        // mailto:<address> (RFC 6068), with the subject, body, cc and bcc that are filled in, in that
        // order, each encoded as a URI component
        static string Mail(IDictionary<string, string> f)
        {
            string address = Field(f, "address");
            if (address.Length == 0) return "";
            string query = string.Join("&", new[] { "subject", "body", "cc", "bcc" }
                .Where(name => Field(f, name).Length > 0)
                .Select(name => name + "=" + Js.EncodeUriComponent(Field(f, name))));
            return "mailto:" + address + (query.Length == 0 ? "" : "?" + query);
        }

        // The Wi-Fi code phones join from (the format ZXing set down): WIFI:, then T:<security>,
        // S:<name>, P:<password> (not for an open network) and H:true for a hidden one, each ended
        // by a semicolon, and one more semicolon to close it.
        static string Wifi(IDictionary<string, string> f)
        {
            if (Field(f, "ssid").Length == 0) return "";
            string security = Field(f, "encryption");
            var parts = new List<string> { "T:" + security, "S:" + WifiText(Field(f, "ssid")) };
            if (security != "nopass") parts.Add("P:" + WifiText(Field(f, "password")));
            if (Field(f, "hidden") == "true") parts.Add("H:true");
            return "WIFI:" + string.Join(";", parts) + ";;";
        }

        // geo:<latitude>,<longitude> (RFC 5870), as they were typed; nothing unless both read as numbers
        static string Geo(string latitude, string longitude) =>
            double.IsNaN(Js.ParseFloat(latitude)) || double.IsNaN(Js.ParseFloat(longitude)) ? "" : "geo:" + latitude + "," + longitude;

        // vCard 2.1, 3.0 (the default) or 4.0, from "2", "3" or "4" or any way of writing them
        public static string VCardVersion(string version)
        {
            string v = Js.Trim(version ?? "");
            return v == "2" || v == "2.1" ? "2" : v == "4" || v == "4.0" ? "4" : "3";
        }

        // How each of a contact's properties is spelled in vCard 2.1, 3.0 and 4.0, the lines in the
        // order they're written. A 4.0 phone's value is a tel: URI.
        static readonly (string Field, string[] Spelled)[] ContactLines =
        {
            ("phoneWork", new[] { "TEL;WORK;VOICE", "TEL;TYPE=WORK,VOICE", "TEL;TYPE=work,voice;VALUE=uri" }),
            ("phonePrivate", new[] { "TEL;HOME;VOICE", "TEL;TYPE=HOME,VOICE", "TEL;TYPE=home,voice;VALUE=uri" }),
            ("phoneMobile", new[] { "TEL;CELL;VOICE", "TEL;TYPE=CELL,VOICE", "TEL;TYPE=cell,voice;VALUE=uri" }),
            ("email", new[] { "EMAIL;INTERNET", "EMAIL", "EMAIL;TYPE=work" }),
            ("website", new[] { "URL", "URL", "URL;TYPE=work" }),
        };

        static readonly string[] AdrSpelled = { "ADR;WORK", "ADR;TYPE=WORK", "ADR;TYPE=work" };

        // A contact card (vCard 2.1, RFC 2426's 3.0 or RFC 6350's 4.0), its lines joined by LF: the
        // name (N, and FN as first and last name; a card with only a company has FN from that), ORG,
        // TITLE, the phones, EMAIL, URL and ADR, each only when it has something in it, its value as
        // vCard text. A 2.1 line whose value isn't all printable ASCII says it's UTF-8.
        static string VCard(IDictionary<string, string> f)
        {
            string version = VCardVersion(Field(f, "version"));
            int spelling = version == "2" ? 0 : version == "3" ? 1 : 2;
            var card = new StringBuilder("BEGIN:VCARD\nVERSION:").Append(new[] { "2.1", "3.0", "4.0" }[spelling]);
            void Line(string property, string value)
            {
                card.Append('\n').Append(property);
                if (spelling == 0 && Regex.IsMatch(value, "[^\\x20-\\x7e]")) card.Append(";CHARSET=UTF-8");
                card.Append(':').Append(value);
            }

            string first = VCardText(Field(f, "firstName")), last = VCardText(Field(f, "lastName")), company = Field(f, "org");
            if (first.Length > 0 || last.Length > 0)
            {
                Line("N", last + ";" + first + ";;;");
                Line("FN", Js.Trim(first + " " + last));
            }
            else if (company.Length > 0) Line("FN", VCardText(company));
            if (company.Length > 0) Line("ORG", VCardText(company));
            if (Field(f, "position").Length > 0) Line("TITLE", VCardText(Field(f, "position")));
            foreach (var (field, spelled) in ContactLines)
            {
                string value = Field(f, field);
                if (value.Length == 0) continue;
                bool uri = spelling == 2 && field.StartsWith("phone", StringComparison.Ordinal);
                Line(spelled[spelling], (uri ? "tel:" : "") + VCardText(value));
            }
            string[] address = new[] { "street", "city", "state", "zipcode", "country" }.Select(name => VCardText(Field(f, name))).ToArray();
            if (address.Any(part => part.Length > 0)) Line(AdrSpelled[spelling], ";;" + string.Join(";", address));
            return card.Append("\nEND:VCARD").ToString();
        }

        // A calendar event (RFC 5545): one VEVENT in a VCALENDAR, with its title and place as
        // iCalendar text, its start and end in UTC (each only when it's a time), then the time it was
        // made and an id of its own, which calendars want in every event.
        static string Event(IDictionary<string, string> f, TimeZoneInfo zone, DateTime utcNow, string uid)
        {
            var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0", "BEGIN:VEVENT" };
            foreach (var (property, field) in new[] { ("SUMMARY", "title"), ("LOCATION", "location") })
                if (Field(f, field).Length > 0) lines.Add(property + ":" + ICalText(Field(f, field)));
            foreach (var (property, field) in new[] { ("DTSTART", "startTime"), ("DTEND", "endTime") })
            {
                string when = ICalTime(Field(f, field), zone);   // "" for nothing, or what isn't a time
                if (when.Length > 0) lines.Add(property + ":" + when);
            }
            lines.Add("DTSTAMP:" + UtcStamp(utcNow));
            lines.Add("UID:" + uid + "@qr.b0r3d.org");
            lines.Add("END:VEVENT");
            lines.Add("END:VCALENDAR");
            return string.Join("\n", lines);
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

        // A SEPA credit transfer as banks' apps read it (EPC069-12), one field a line: BCD, the
        // version (001, or 002 which may leave out the BIC), 1 for UTF-8, SCT, the BIC, the payee,
        // the IBAN, the amount, the purpose, then either the structured reference or the text, then
        // a note. The BIC and IBAN go without spaces and in capitals; the payee, purpose (also in
        // capitals), reference, text and note are each one line, cut to the length the standard
        // allows. Empty lines after the IBAN that nothing follows are left off. Nothing is written
        // without a payee and an IBAN, or for version 001 without a BIC.
        static string Epc(IDictionary<string, string> f)
        {
            bool versionOne = Field(f, "version") == "001";
            string bic = Js.ToUpperCase(Unspaced(Field(f, "bic")));
            if (Field(f, "name").Length == 0 || Field(f, "iban").Length == 0 || versionOne && bic.Length == 0) return "";
            string Part(string field, int most) => Upto(EpcLine(Field(f, field)), most);
            string reference = Part("remittanceReference", 35);
            string[] lines =
            {
                "BCD", versionOne ? "001" : "002", "1", "SCT", bic, Part("name", 70), Js.ToUpperCase(Unspaced(Field(f, "iban"))),
                EuroAmount(Field(f, "amount")), Js.ToUpperCase(Part("purpose", 4)), reference,
                reference.Length > 0 ? "" : Part("remittanceText", 140), Part("originatorInfo", 70),
            };
            int count = lines.Length;
            while (count > 7 && lines[count - 1].Length == 0) count--;
            return string.Join("\n", lines, 0, count);
        }

        // "EUR" and an amount above zero with two decimals, read with a decimal point or a comma
        // (the first comma is one); "" for none, or nothing that reads as one
        static string EuroAmount(string typed)
        {
            int comma = typed.IndexOf(',');
            double value = Js.ParseFloat(comma < 0 ? typed : typed.Remove(comma, 1).Insert(comma, "."));
            return !double.IsNaN(value) && value > 0 ? "EUR" + Js.ToFixed2(value) : "";
        }

        static string Unspaced(string value) => Regex.Replace(value, Js.Space + "+", "");

        static string Upto(string s, int most) => s.Length <= most ? s : s.Substring(0, most);

        // An IBAN with the right check digits (ISO 13616, mod 97); spaces and case don't matter.
        public static bool ValidIban(string value)
        {
            string iban = Js.ToUpperCase(Regex.Replace(value ?? "", Js.Space + "+", ""));
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
            string text = data ?? "";
            TemplateKind kind = KindOf(text);
            switch (kind)
            {
                case TemplateKind.Epc: ReadPayment(text, fields); break;
                case TemplateKind.VCard: fields = ReadVCard(text); break;
                case TemplateKind.Gs1DigitalLink: ReadGs1(text, fields); break;
                case TemplateKind.Url: fields["url"] = text; break;
                case TemplateKind.Email: ReadMail(text, fields); break;
                case TemplateKind.Phone: fields["phone"] = text.Substring("tel:".Length); break;
                case TemplateKind.Sms: ReadSms(text, fields); break;
                case TemplateKind.Wifi: ReadWifi(text, fields); break;
                case TemplateKind.Location: ReadGeo(text, fields); break;
                case TemplateKind.Event: ReadEvent(text, fields, zone ?? TimeZoneInfo.Local); break;
                default: fields["text"] = text; break;
            }
            return kind;
        }

        // How a text starts, in capitals or small letters, for each kind it can be, tried in this
        // order after a payment, a contact card and a GS1 Digital Link (see KindOf).
        static readonly (string Start, TemplateKind Kind)[] Starts =
        {
            ("^https?:/{2}", TemplateKind.Url), ("^mailto:", TemplateKind.Email), ("^tel:", TemplateKind.Phone),
            ("^(SMSTO|sms):", TemplateKind.Sms), ("^WIFI:", TemplateKind.Wifi), ("^geo:", TemplateKind.Location),
            ("^" + Js.Space + "*BEGIN:(VCALENDAR|VEVENT)", TemplateKind.Event),
        };

        // Which kind a text is, as the website decides it: a payment when it starts BCD and a line
        // break (in capitals), a contact card when it starts BEGIN:VCARD (after any white space), a
        // GS1 Digital Link when it's one this template writes with a right check digit, then by how
        // it starts (Starts); anything else, and nothing at all, is text.
        static TemplateKind KindOf(string text)
        {
            if (text.Length == 0) return TemplateKind.Text;
            if (Regex.IsMatch(text, "^BCD\r?\n")) return TemplateKind.Epc;
            if (Regex.IsMatch(text, "^" + Js.Space + "*BEGIN:VCARD", I)) return TemplateKind.VCard;
            Match gs1 = Gs1Link.Match(text);
            if (gs1.Success && GtinProblem(gs1.Groups[2].Value) == null) return TemplateKind.Gs1DigitalLink;
            foreach (var (start, kind) in Starts)
                if (Regex.IsMatch(text, start, I)) return kind;
            return TemplateKind.Text;
        }

        // A payment's fields from its lines (CRs left out): the version from the second line, then
        // from the fifth the BIC, the payee, the IBAN, the amount (what follows its EUR), the purpose,
        // the reference, the text and the note. A missing line is an empty field.
        static readonly string[] PaymentFields = { "bic", "name", "iban", "amount", "purpose", "remittanceReference", "remittanceText", "originatorInfo" };

        static void ReadPayment(string text, Dictionary<string, string> fields)
        {
            string[] lines = text.Replace("\r", "").Split('\n');
            string LineAt(int n) => n < lines.Length ? lines[n] : "";
            fields["version"] = LineAt(1) == "001" ? "001" : "002";
            for (int i = 0; i < PaymentFields.Length; i++) fields[PaymentFields[i]] = LineAt(4 + i);
            string amount = fields["amount"];
            fields["amount"] = amount.StartsWith("EUR", StringComparison.Ordinal) ? amount.Substring(3) : "";
        }

        static void ReadGs1(string text, Dictionary<string, string> fields)
        {
            Match gs1 = Gs1Link.Match(text);
            string Decode(Group g) => !g.Success ? "" : Js.DecodeUriComponent(g.Value) ?? g.Value;
            string date = gs1.Groups[5].Success ? gs1.Groups[5].Value : "";
            fields["domain"] = gs1.Groups[1].Value;
            fields["gtin"] = gs1.Groups[2].Value;
            fields["lot"] = Decode(gs1.Groups[3]);
            fields["serial"] = Decode(gs1.Groups[4]);
            fields["expiry"] = date.Length > 0 ? "20" + date.Substring(0, 2) + "-" + date.Substring(2, 2) + "-" + date.Substring(4) : "";
        }

        // mailto:<address>?<query>: the address is everything up to the first ?. The subject, body,
        // cc and bcc are read from what follows it, unless that's nothing or starts with another ?,
        // when none of them is set.
        static void ReadMail(string text, Dictionary<string, string> fields)
        {
            string rest = text.Substring("mailto:".Length);
            int question = rest.IndexOf('?');
            fields["address"] = question < 0 ? rest : rest.Substring(0, question);
            string query = question < 0 ? "" : rest.Substring(question + 1);
            if (query.Length == 0 || query[0] == '?') return;
            foreach (string name in new[] { "subject", "body", "cc", "bcc" }) fields[name] = Js.SearchParam(query, name) ?? "";
        }

        // geo:<latitude>,<longitude>, anything from a ; or ? on left off (RFC 5870's parameters), each
        // without spaces round it; with fewer than two, no fields at all
        static void ReadGeo(string text, Dictionary<string, string> fields)
        {
            string place = text.Substring("geo:".Length);
            int end = place.IndexOfAny(new[] { ';', '?' });
            string[] coordinates = (end < 0 ? place : place.Substring(0, end)).Split(',');
            if (coordinates.Length < 2) return;
            fields["latitude"] = Js.Trim(coordinates[0]);
            fields["longitude"] = Js.Trim(coordinates[1]);
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
                string key = Js.ToUpperCase(Js.Trim(field.Substring(0, colon)));
                string value = field.Substring(colon + 1);
                // a value that could pass for hex may be in double quotes
                if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal) && !value.EndsWith("\\\"", StringComparison.Ordinal))
                    value = value.Substring(1, value.Length - 2);
                if (!found.ContainsKey(key)) found[key] = UnescapeWifi(value);
            }
            string Get(string key) => found.TryGetValue(key, out string v) ? v : "";
            string type = Js.ToUpperCase(Get("T"));
            fields["ssid"] = Get("S");
            fields["encryption"] = type == "WEP" ? "WEP" : type == "SAE" ? "SAE" : type.StartsWith("WPA", StringComparison.Ordinal) ? "WPA" : "nopass";
            fields["password"] = Get("P");
            fields["hidden"] = Js.ToLowerCase(Get("H")) == "true" ? "true" : "false";
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
                    Name = Js.ToUpperCase(Js.Trim(head[0].Split('.').Last())),
                    Params = Js.ToUpperCase(string.Join(";", head.Skip(1))),
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
                // a company's card is named after the company: it isn't a person. A person's name
                // alone gives its first word as the first name and the rest, if any, as the last.
                if (org == null || string.Join(", ", Components(org)) != name)
                {
                    int space = name.IndexOf(' ');
                    parsed["firstName"] = space < 0 ? name : name.Substring(0, space);
                    if (space >= 0) parsed["lastName"] = name.Substring(space + 1);
                }
            }
            if (org != null) parsed["org"] = string.Join(", ", Components(org).Where(p => p.Length > 0));
            ContentLine title = First("TITLE");
            if (title != null) parsed["position"] = TextValue(title);
            // each phone goes in by its type (work, home, cell or mobile), the first of each; one with
            // no type is the mobile, but only when no phone has gone in before it
            string[] phones = { "phoneWork", "phonePrivate", "phoneMobile" };
            foreach (ContentLine line in lines.Where(l => l.Name == "TEL"))
            {
                string phone = Regex.Replace(TextValue(line), "^tel:", "", I);
                string key = Regex.IsMatch(line.Params, @"\bWORK\b", RegexOptions.ECMAScript) ? "phoneWork"
                    : Regex.IsMatch(line.Params, @"\bHOME\b", RegexOptions.ECMAScript) ? "phonePrivate"
                    : Regex.IsMatch(line.Params, @"\b(CELL|MOBILE)\b", RegexOptions.ECMAScript) ? "phoneMobile"
                    : phones.Any(p => Has(parsed, p)) ? null : "phoneMobile";
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
