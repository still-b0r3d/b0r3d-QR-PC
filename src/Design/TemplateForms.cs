using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace B0r3dQR.Design
{
    enum FieldKind { Line, Lines, Password, Choice, Check, Number, Date, DateTime }

    // One field of a template's form, as the website's "Data to encode" window shows it.
    sealed class TemplateField
    {
        public string Name;                       // as Templates writes and reads it
        public string Label;
        public FieldKind Kind = FieldKind.Line;
        public string Placeholder = "";
        public string Hint;                       // a line under the field
        public int MaxLength;                     // 0: no limit
        public (string Value, string Label)[] Choices;
        public bool Wide;                         // across both columns of the contact form
        public bool Address;                      // a web or email address (the website's url and email fields)
    }

    // The website's "Data to encode" window as data (its src/components/DataTemplatesModal.vue): each
    // template's fields in order, what's required and its messages, the example it fills in, and
    // the fields it fills from existing text.
    static class TemplateForms
    {
        // the Data section's list, in the website's order
        public static readonly TemplateKind[] Menu =
        {
            TemplateKind.Text, TemplateKind.Url, TemplateKind.Wifi, TemplateKind.VCard, TemplateKind.Email, TemplateKind.Phone,
            TemplateKind.Sms, TemplateKind.Location, TemplateKind.Event, TemplateKind.Epc, TemplateKind.Gs1DigitalLink,
        };

        // the window's "Data type" list, in the website's order
        public static readonly TemplateKind[] Types =
        {
            TemplateKind.Text, TemplateKind.Url, TemplateKind.Email, TemplateKind.Phone, TemplateKind.Sms, TemplateKind.Wifi,
            TemplateKind.VCard, TemplateKind.Location, TemplateKind.Event, TemplateKind.Epc, TemplateKind.Gs1DigitalLink,
        };

        public static string Label(TemplateKind kind)
        {
            switch (kind)
            {
                case TemplateKind.Url: return "URL";
                case TemplateKind.Email: return "Email";
                case TemplateKind.Phone: return "Phone";
                case TemplateKind.Sms: return "SMS";
                case TemplateKind.Wifi: return "WiFi";
                case TemplateKind.VCard: return "vCard";
                case TemplateKind.Location: return "Location";
                case TemplateKind.Event: return "Event";
                case TemplateKind.Epc: return "EPC QR (SEPA Payment)";
                case TemplateKind.Gs1DigitalLink: return "Product (GS1 Digital Link)";
                default: return "Text";
            }
        }

        // the words above a template's fields, if it has any
        public static string Intro(TemplateKind kind)
        {
            switch (kind)
            {
                case TemplateKind.Gs1DigitalLink:
                    return "A web address for a product. Phones open it as a link; shop checkouts read the product number from it, the same as from an EAN or UPC barcode.";
                case TemplateKind.Epc:
                    return "Generates a SEPA Credit Transfer QR code (also known as GiroCode) that banking apps can scan to pre-fill a payment.";
                default:
                    return null;
            }
        }

        public const string RequiredNote = "Required fields are marked with an asterisk (*)";

        static TemplateField F(string name, string label, string placeholder = "", FieldKind kind = FieldKind.Line, int max = 0, string hint = null) =>
            new TemplateField { Name = name, Label = label, Placeholder = placeholder, Kind = kind, MaxLength = max, Hint = hint };

        static TemplateField Address(TemplateField f)
        {
            f.Address = true;
            return f;
        }

        public static List<TemplateField> Fields(TemplateKind kind)
        {
            switch (kind)
            {
                case TemplateKind.Url:
                    return new List<TemplateField> { Address(F("url", "URL", "https://example.com")) };
                case TemplateKind.Email:
                    return new List<TemplateField>
                    {
                        Address(F("address", "Email Address", "recipient@example.com")),
                        Address(F("cc", "CC", "Optional CC")),
                        Address(F("bcc", "BCC", "Optional BCC")),
                        F("subject", "Subject", "Optional subject line"),
                        F("body", "Message", "Optional email body", FieldKind.Lines),
                    };
                case TemplateKind.Phone:
                    return new List<TemplateField> { F("phone", "Phone Number", "+1234567890") };
                case TemplateKind.Sms:
                    return new List<TemplateField> { F("phone", "Phone Number", "+1234567890"), F("message", "Message", "Optional SMS message", FieldKind.Lines) };
                case TemplateKind.Wifi:
                    return new List<TemplateField>
                    {
                        new TemplateField
                        {
                            Name = "encryption", Label = "Encryption", Kind = FieldKind.Choice,
                            Choices = new[] { ("nopass", "No encryption"), ("WEP", "WEP"), ("WPA", "WPA/WPA2/WPA3 (most networks)"), ("SAE", "WPA3 only") },
                        },
                        F("ssid", "Wireless SSID", "Your network name"),
                        F("password", "Password", "Network password (case-sensitive)", FieldKind.Password),
                        F("hidden", "Hidden SSID", "", FieldKind.Check),
                    };
                case TemplateKind.VCard:
                    return new List<TemplateField>
                    {
                        new TemplateField { Name = "version", Label = "vCard Version", Kind = FieldKind.Choice, Wide = true, Choices = new[] { ("2", "vCard 2.1"), ("3", "vCard 3.0"), ("4", "vCard 4.0") } },
                        F("firstName", "First Name", "e.g., John"), F("lastName", "Last Name", "e.g., Doe"),
                        F("org", "Organization", "e.g., Acme Corp"), F("position", "Position", "e.g., Software Engineer"),
                        F("phoneWork", "Phone (Work)", "+1-555-123-4567"), F("phonePrivate", "Phone (Private)", "+1-555-987-6543"),
                        F("phoneMobile", "Phone (Mobile)", "+1-555-111-2222"), Address(F("email", "Email", "john.doe@acme.com")),
                        Address(F("website", "Website", "https://example.com")), F("street", "Street", "123 Main St"),
                        F("zipcode", "Zipcode", "90210"), F("city", "City", "Anytown"),
                        F("state", "State", "CA"), F("country", "Country", "USA"),
                    };
                case TemplateKind.Location:
                    return new List<TemplateField> { F("latitude", "Latitude", "e.g., 40.7128", FieldKind.Number), F("longitude", "Longitude", "e.g., -74.0060", FieldKind.Number) };
                case TemplateKind.Event:
                    return new List<TemplateField>
                    {
                        F("title", "Event Title", "e.g., Team Meeting"), F("location", "Event Location", "e.g., Conference Room A"),
                        F("startTime", "Start Time", "", FieldKind.DateTime), F("endTime", "End Time", "", FieldKind.DateTime),
                    };
                case TemplateKind.Gs1DigitalLink:
                    return new List<TemplateField>
                    {
                        F("gtin", "Product number (GTIN)", "09506000134352", FieldKind.Line, 18, "The number under the barcode: 8, 12, 13 or 14 digits."),
                        F("lot", "Batch or lot", "", FieldKind.Line, 20), F("serial", "Serial number", "", FieldKind.Line, 20),
                        F("expiry", "Expiry date", "", FieldKind.Date),
                        Address(F("domain", "Web address (resolver)", Templates.Gs1Domain, FieldKind.Line, 0,
                          "Your brand's own, if it has one set up; otherwise GS1's, which sends people to whatever the brand registered.")),
                    };
                case TemplateKind.Epc:
                    return new List<TemplateField>
                    {
                        new TemplateField { Name = "version", Label = "EPC QR Version", Kind = FieldKind.Choice, Choices = new[] { ("002", "Version 2 (BIC optional within SEPA)"), ("001", "Version 1 (BIC required)") } },
                        F("name", "Beneficiary Name", "e.g., Jane Smith", FieldKind.Line, 70),
                        F("iban", "IBAN", "DE89370400440532013000", FieldKind.Line, 34),
                        F("bic", "BIC", "DEUTDEFF", FieldKind.Line, 11),
                        F("amount", "Amount (EUR)", "25.00"),
                        F("purpose", "Purpose Code", "GDDS", FieldKind.Line, 4),
                        F("remittanceText", "Payment Reference", "e.g., Invoice 12345", FieldKind.Line, 140),
                        F("remittanceReference", "Structured Creditor Reference", "e.g., RF18539007547034", FieldKind.Line, 35,
                          "Only one of Payment Reference or Structured Creditor Reference is included; the reference takes priority when both are filled."),
                        F("originatorInfo", "Beneficiary Information", "", FieldKind.Line, 70),
                    };
                default:
                    return new List<TemplateField> { F("text", "Text", "Have a beautiful day!", FieldKind.Lines) };
            }
        }

        // what a form starts with before anything is typed in it
        public static Dictionary<string, string> Defaults(TemplateKind kind)
        {
            var v = Fields(kind).ToDictionary(f => f.Name, f => "", StringComparer.Ordinal);
            if (kind == TemplateKind.Wifi) { v["encryption"] = "nopass"; v["hidden"] = "false"; }
            if (kind == TemplateKind.VCard) v["version"] = "3";
            if (kind == TemplateKind.Epc) v["version"] = "002";
            return v;
        }

        static string Get(IDictionary<string, string> v, string name) => v != null && v.TryGetValue(name, out string s) && s != null ? s : "";

        // a required field is marked with an asterisk
        public static bool Required(TemplateKind kind, string field, IDictionary<string, string> v)
        {
            switch (kind)
            {
                case TemplateKind.Text: return field == "text";
                case TemplateKind.Url: return field == "url";
                case TemplateKind.Email: return field == "address";
                case TemplateKind.Phone:
                case TemplateKind.Sms: return field == "phone";
                case TemplateKind.Wifi: return field == "ssid" || (field == "password" && Get(v, "encryption").ToLowerInvariant() != "nopass");
                case TemplateKind.Location: return field == "latitude" || field == "longitude";
                case TemplateKind.Event: return field == "title" || field == "startTime" || field == "endTime";
                case TemplateKind.Gs1DigitalLink: return field == "gtin";
                case TemplateKind.Epc: return field == "name" || field == "iban" || (field == "bic" && Get(v, "version") == "001");
                default: return false;
            }
        }

        // a field that's off: the password when there's no encryption, the payment reference once
        // a structured reference is filled in
        public static bool Disabled(TemplateKind kind, string field, IDictionary<string, string> v) =>
            (kind == TemplateKind.Wifi && field == "password" && Get(v, "encryption").ToLowerInvariant() == "nopass") ||
            (kind == TemplateKind.Epc && field == "remittanceText" && Get(v, "remittanceReference").Length > 0);

        // the line under a field that changes with it: a WPA3-only warning, the check of a GTIN or an IBAN
        public static string LiveNote(TemplateKind kind, string field, IDictionary<string, string> v)
        {
            if (kind == TemplateKind.Wifi && field == "encryption" && Get(v, "encryption") == "SAE")
                return "Only for networks that accept nothing but WPA3. Some phones can't join from a WPA3-only code; WPA/WPA2/WPA3 works for mixed networks.";
            if (kind == TemplateKind.Gs1DigitalLink && field == "gtin" && Get(v, "gtin").Length > 0) return Templates.GtinProblem(Get(v, "gtin"));
            if (kind == TemplateKind.Epc && field == "iban")
            {
                string iban = Regex.Replace(Get(v, "iban"), Js.Space + "+", "");
                if (iban.Length >= 15 && !Templates.ValidIban(iban)) return IbanCheck;
            }
            return null;
        }

        public const string IbanCheck = "This IBAN's check digits don't match. Check it for a typo.";

        // What's missing or wrong when Save is clicked, field by field, in the website's words.
        public static List<KeyValuePair<string, string>> Problems(TemplateKind kind, IDictionary<string, string> v)
        {
            var problems = new List<KeyValuePair<string, string>>();
            void Flag(string field, string message) => problems.Add(new KeyValuePair<string, string>(field, message));
            void Need(string field, string message)
            {
                if (Get(v, field).Length == 0) Flag(field, message);
            }
            switch (kind)
            {
                case TemplateKind.Text: Need("text", "Text is required"); break;
                case TemplateKind.Url: Need("url", "URL is required"); break;
                case TemplateKind.Email: Need("address", "Email address is required"); break;
                case TemplateKind.Phone:
                case TemplateKind.Sms: Need("phone", "Phone number is required"); break;
                case TemplateKind.Wifi:
                    Need("ssid", "Wireless SSID is required");
                    if (Get(v, "encryption").ToLowerInvariant() != "nopass") Need("password", "Password is required when encryption is enabled");
                    break;
                case TemplateKind.Location:
                    // a coordinate that isn't a number (read from a geo: text) shows as nothing in the
                    // website's number field, and its Save would write an empty code: asked for here
                    foreach (string axis in new[] { "latitude", "longitude" })
                        if (double.IsNaN(Js.ParseFloat(Get(v, axis)))) Flag(axis, axis == "latitude" ? "Latitude is required" : "Longitude is required");
                    break;
                case TemplateKind.Event:
                    Need("title", "Event title is required");
                    Need("startTime", "Start time is required");
                    Need("endTime", "End time is required");
                    break;
                case TemplateKind.Gs1DigitalLink:
                    string gtin = Templates.GtinProblem(Get(v, "gtin"));
                    if (gtin != null) Flag("gtin", gtin);
                    break;
                case TemplateKind.Epc:
                    // checked as the payment's lines are written: a name or BIC of spaces alone
                    // would make an empty line, or no code at all, in the website's Save
                    if (Templates.EpcLine(Get(v, "name")).Length == 0) Flag("name", "Beneficiary name is required");
                    string iban = Regex.Replace(Get(v, "iban"), Js.Space + "+", "");
                    if (iban.Length == 0) Flag("iban", "IBAN is required");
                    else if (!Templates.ValidIban(iban)) Flag("iban", IbanCheck);
                    if (Get(v, "version") == "001" && Regex.Replace(Get(v, "bic"), Js.Space + "+", "").Length == 0) Flag("bic", "BIC is required for EPC QR version 1");
                    if (!AmountOk(Get(v, "amount"))) Flag("amount", AmountCheck);
                    break;
            }
            return problems;
        }

        public const string AmountCheck = "Enter the amount like 25.00 or 25,00, from 0.01 to 999999999.99, or leave it empty.";

        // An amount a payment code can carry, or none: euros, with up to two decimals after a point
        // or a comma, from 0.01 to 999999999.99. The website's number field drops anything else
        // without a word; here the field takes any text, and the website's way of reading it would
        // make "1.234,56" 1.23, so anything else is asked about instead.
        public static bool AmountOk(string amount)
        {
            string a = Js.Trim(amount ?? "");
            if (a.Length == 0) return true;
            Match m = Regex.Match(a, @"^([0-9]{0,9})(?:[.,]([0-9]{0,2}))?\z");
            return m.Success && m.Groups[1].Length + m.Groups[2].Length > 0 && Js.ParseFloat(a.Replace(',', '.')) >= 0.01;
        }

        // the fields whose message goes away once they're filled in again (the website's watchers)
        public static bool ClearsWhenFilled(TemplateKind kind, string field) =>
            !(kind == TemplateKind.Wifi && field == "password");

        // the checks of what's typed (an IBAN's check digits, an amount) go as soon as the field changes
        public static bool ClearsWhenChanged(string message) => message == IbanCheck || message == AmountCheck;

        // what "Use example" fills in, for the template on show
        public static Dictionary<string, string> Example(TemplateKind kind, DateTime now)
        {
            var v = new Dictionary<string, string>(StringComparer.Ordinal);
            string Local(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
            switch (kind)
            {
                case TemplateKind.Url: v["url"] = "https://b0r3d.org"; break;
                case TemplateKind.Email:
                    v["address"] = "test@example.com"; v["subject"] = "QR Code Test"; v["body"] = "This is a test email from b0r3d QR!";
                    v["cc"] = "cc@example.com"; v["bcc"] = "bcc@example.com";
                    break;
                case TemplateKind.Phone: v["phone"] = "+19876543210"; break;
                case TemplateKind.Sms: v["phone"] = "+19876543210"; v["message"] = "Hello from b0r3d QR!"; break;
                case TemplateKind.Wifi: v["ssid"] = "MyWiFiNetwork"; v["encryption"] = "WPA"; v["password"] = "Password123"; v["hidden"] = "false"; break;
                case TemplateKind.VCard:
                    v["firstName"] = "Jane"; v["lastName"] = "Smith"; v["org"] = "Example Inc."; v["position"] = "Developer";
                    v["phoneWork"] = "+1-555-000-1111"; v["phonePrivate"] = ""; v["phoneMobile"] = "+1-555-222-3333";
                    v["email"] = "jane.smith@example.com"; v["website"] = "https://example.com"; v["street"] = "456 Example Ave";
                    v["city"] = "Othertown"; v["state"] = "NY"; v["zipcode"] = "10001"; v["country"] = "USA";
                    break;
                case TemplateKind.Location: v["latitude"] = "37.7749"; v["longitude"] = "-122.4194"; break;
                case TemplateKind.Event:
                    v["title"] = "Project Sync"; v["location"] = "Online"; v["startTime"] = Local(now); v["endTime"] = Local(now.AddHours(1));
                    break;
                case TemplateKind.Gs1DigitalLink:
                    // GS1's own example number, so the example can't point at a real product
                    v["domain"] = ""; v["gtin"] = "09506000134352"; v["lot"] = "ABC123"; v["serial"] = ""; v["expiry"] = "2027-12-31";
                    break;
                case TemplateKind.Epc:
                    v["name"] = "Jane Smith"; v["iban"] = "DE89370400440532013000"; v["bic"] = "DEUTDEFF"; v["amount"] = "25.00"; v["purpose"] = "";
                    v["remittanceReference"] = ""; v["remittanceText"] = "Invoice 12345"; v["originatorInfo"] = ""; v["version"] = "002";
                    break;
                default: v["text"] = "Have a wonderful day!"; break;
            }
            return v;
        }

        // The form's fields filled from a text the website read as this template: what's missing
        // is empty, with the website's defaults for the choices (GS1's own resolver shows as none).
        public static Dictionary<string, string> FromText(TemplateKind kind, Dictionary<string, string> read)
        {
            Dictionary<string, string> v = Defaults(kind);
            foreach (string name in v.Keys.ToList()) v[name] = Get(read, name);
            switch (kind)
            {
                case TemplateKind.Wifi:
                    if (v["encryption"].Length == 0) v["encryption"] = "nopass";
                    v["hidden"] = Get(read, "hidden") == "true" ? "true" : "false";
                    break;
                case TemplateKind.VCard:
                    if (v["version"].Length == 0) v["version"] = "3";
                    break;
                case TemplateKind.Gs1DigitalLink:
                    if (v["domain"] == Templates.Gs1Domain) v["domain"] = "";
                    break;
                case TemplateKind.Epc:
                    v["version"] = Get(read, "version") == "001" ? "001" : "002";
                    break;
            }
            return v;
        }

        // What a browser's number field holds once something's typed in it: the number written the
        // JavaScript way ("40.70" is 40.7), or nothing when it isn't one.
        public static string TypedNumber(string typed)
        {
            string t = (typed ?? "").Trim();
            return Js.IsNumberFieldValue(t) ? Js.NumberToString(Js.ParseFloat(t)) : "";
        }

        // What a browser's field gives for what's typed in it: a box of lines its line breaks as \n
        // alone (Windows types \r\n); a one-line field no line breaks; a web or email address no
        // spaces at either end, as the website's url and email fields drop them.
        public static string AsTyped(TemplateField f, string text)
        {
            if (f.Kind == FieldKind.Lines) return Regex.Replace(text ?? "", "\r\n?", "\n");
            string line = (text ?? "").Replace("\r", "").Replace("\n", "");
            return f.Address ? line.Trim('\t', '\f', ' ') : line;
        }
    }
}
