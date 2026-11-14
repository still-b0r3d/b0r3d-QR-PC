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

    // The website's "Data to encode" window as data: each template's fields in order, what's required,
    // the example it fills in, and the fields it fills from existing text. The rules are the website's;
    // the words (help lines, placeholders, examples and what a field that's wrong says) are b0r3d QR's own.
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
                    return "A bank transfer in euros, as an EPC QR code (also called GiroCode): banking apps that scan it fill in the payment for you.";
                default:
                    return null;
            }
        }

        public const string RequiredNote = "Fields with a * have to be filled in.";

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
                    return new List<TemplateField> { Address(F("url", "URL", "https://b0r3d.org")) };
                case TemplateKind.Email:
                    return new List<TemplateField>
                    {
                        Address(F("address", "Email Address", "name@example.com")),
                        Address(F("cc", "CC", "Copied in, if anyone")),
                        Address(F("bcc", "BCC", "Copied in unseen, if anyone")),
                        F("subject", "Subject", "What it's about"),
                        F("body", "Message", "What it says", FieldKind.Lines),
                    };
                case TemplateKind.Phone:
                    return new List<TemplateField> { F("phone", "Phone Number", "+1-202-555-0100") };
                case TemplateKind.Sms:
                    return new List<TemplateField> { F("phone", "Phone Number", "+1-202-555-0100"), F("message", "Message", "What it says", FieldKind.Lines) };
                case TemplateKind.Wifi:
                    return new List<TemplateField>
                    {
                        new TemplateField
                        {
                            Name = "encryption", Label = "Encryption", Kind = FieldKind.Choice,
                            Choices = new[] { ("nopass", "No encryption"), ("WEP", "WEP"), ("WPA", "WPA/WPA2/WPA3 (most networks)"), ("SAE", "WPA3 only") },
                        },
                        F("ssid", "Wireless SSID", "The network's name, as phones list it"),
                        F("password", "Password", "Capital letters count", FieldKind.Password),
                        F("hidden", "Hidden SSID", "", FieldKind.Check),
                    };
                case TemplateKind.VCard:
                    return new List<TemplateField>
                    {
                        new TemplateField { Name = "version", Label = "vCard Version", Kind = FieldKind.Choice, Wide = true, Choices = new[] { ("2", "vCard 2.1"), ("3", "vCard 3.0"), ("4", "vCard 4.0") } },
                        F("firstName", "First Name", "Ruckus"), F("lastName", "Last Name", "Cat"),
                        F("org", "Organization", "b0r3d.org"), F("position", "Position", "Chief napper"),
                        F("phoneWork", "Phone (Work)", "+1 202 555 0100"), F("phonePrivate", "Phone (Private)", "+1 202 555 0142"),
                        F("phoneMobile", "Phone (Mobile)", "+1 202 555 0199"), Address(F("email", "Email", "rukus@example.com")),
                        Address(F("website", "Website", "https://b0r3d.org")), F("street", "Street", "1 Loaf Lane"),
                        F("zipcode", "Zipcode", "12345"), F("city", "City", "Breadville"),
                        F("state", "State", "CA"), F("country", "Country", "USA"),
                    };
                case TemplateKind.Location:
                    // Greenwich's old observatory, on the prime meridian
                    return new List<TemplateField> { F("latitude", "Latitude", "51.4769", FieldKind.Number), F("longitude", "Longitude", "-0.0005", FieldKind.Number) };
                case TemplateKind.Event:
                    return new List<TemplateField>
                    {
                        F("title", "Event Title", "Nap time"), F("location", "Event Location", "The sunny windowsill"),
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
                        new TemplateField { Name = "version", Label = "EPC QR Version", Kind = FieldKind.Choice, Choices = new[] { ("002", "Version 2 (the BIC can be left out)"), ("001", "Version 1 (the BIC is needed)") } },
                        F("name", "Beneficiary Name", "Ruckus Cat", FieldKind.Line, 70),
                        // the example IBAN banks' own documents use, and the BIC of its bank
                        F("iban", "IBAN", "DE89 3704 0044 0532 0130 00", FieldKind.Line, 34),
                        F("bic", "BIC", "COBADEFFXXX", FieldKind.Line, 11),
                        F("amount", "Amount (EUR)", "12.50"),
                        F("purpose", "Purpose Code", "GDDS", FieldKind.Line, 4),
                        F("remittanceText", "Payment Reference", "Cat food", FieldKind.Line, 140),
                        // an ISO 11649 reference with real check digits
                        F("remittanceReference", "Structured Creditor Reference", "RF76B0R3D", FieldKind.Line, 35,
                          "A code holds one reference or the other: once this one is filled in, the payment reference is left out."),
                        F("originatorInfo", "Beneficiary Information", "", FieldKind.Line, 70),
                    };
                default:
                    return new List<TemplateField> { F("text", "Text", "A note, a quote, anything", FieldKind.Lines) };
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

        // What each template asks of its fields: whether a field is marked * (always, or only with a
        // choice made: a password for a network that has one, a BIC for an EPC version 1 code), whether
        // it's off (a password for an open network; the payment reference once there's a structured
        // one), and what Save says when it's missing or wrong. A field that isn't here asks nothing.
        sealed class Rule
        {
            public TemplateKind Kind;
            public string Field;
            public Func<IDictionary<string, string>, bool> Starred = Never, Off = Never;
            public Func<IDictionary<string, string>, string> Problem = v => null;
            public bool MessageStays;   // its message stays until the next Save, whatever's typed in it
        }

        static bool Never(IDictionary<string, string> v) => false;
        static bool Always(IDictionary<string, string> v) => true;
        static bool Secured(IDictionary<string, string> v) => Get(v, "encryption") != "nopass";
        static bool VersionOne(IDictionary<string, string> v) => Get(v, "version") == "001";
        static string Unspaced(string value) => Regex.Replace(value, Js.Space + "+", "");

        // a field marked * that Save asks for when it's empty
        static Rule Needed(TemplateKind kind, string field, string says) =>
            new Rule { Kind = kind, Field = field, Starred = Always, Problem = v => Get(v, field).Length == 0 ? says : null };

        // in the order Save lists what's wrong
        static readonly Rule[] Rules =
        {
            Needed(TemplateKind.Text, "text", "Type the text for the code."),
            Needed(TemplateKind.Url, "url", "Type the web address."),
            Needed(TemplateKind.Email, "address", "Type the email address."),
            Needed(TemplateKind.Phone, "phone", "Type the phone number."),
            Needed(TemplateKind.Sms, "phone", "Type the phone number."),
            Needed(TemplateKind.Wifi, "ssid", "Type the network's name."),
            new Rule
            {
                Kind = TemplateKind.Wifi, Field = "password", Starred = Secured, Off = v => !Secured(v), MessageStays = true,
                Problem = v => Secured(v) && Get(v, "password").Length == 0 ? "Type the password, or choose No encryption." : null,
            },
            // a coordinate that isn't a number (read from a geo: text) shows as nothing in the website's
            // number field, and its Save would write an empty code: asked for here
            new Rule { Kind = TemplateKind.Location, Field = "latitude", Starred = Always, Problem = v => double.IsNaN(Js.ParseFloat(Get(v, "latitude"))) ? "Type the latitude as a number." : null },
            new Rule { Kind = TemplateKind.Location, Field = "longitude", Starred = Always, Problem = v => double.IsNaN(Js.ParseFloat(Get(v, "longitude"))) ? "Type the longitude as a number." : null },
            Needed(TemplateKind.Event, "title", "Give the event a title."),
            Needed(TemplateKind.Event, "startTime", "Choose when it starts."),
            Needed(TemplateKind.Event, "endTime", "Choose when it ends."),
            new Rule { Kind = TemplateKind.Gs1DigitalLink, Field = "gtin", Starred = Always, Problem = v => Templates.GtinProblem(Get(v, "gtin")) },
            // a payment is checked as its lines are written: a name or BIC of spaces alone would make an
            // empty line, or no code at all, in the website's Save
            new Rule { Kind = TemplateKind.Epc, Field = "name", Starred = Always, Problem = v => Templates.EpcLine(Get(v, "name")).Length == 0 ? "Type the beneficiary's name." : null },
            new Rule
            {
                Kind = TemplateKind.Epc, Field = "iban", Starred = Always,
                Problem = v => Unspaced(Get(v, "iban")).Length == 0 ? "Type the IBAN." : Templates.ValidIban(Get(v, "iban")) ? null : IbanCheck,
            },
            new Rule
            {
                Kind = TemplateKind.Epc, Field = "bic", Starred = VersionOne,
                Problem = v => VersionOne(v) && Unspaced(Get(v, "bic")).Length == 0 ? "Version 1 needs the BIC: type it, or choose version 2." : null,
            },
            new Rule { Kind = TemplateKind.Epc, Field = "amount", Problem = v => AmountOk(Get(v, "amount")) ? null : AmountCheck },
            new Rule { Kind = TemplateKind.Epc, Field = "remittanceText", Off = v => Get(v, "remittanceReference").Length > 0 },
        };

        static Rule RuleFor(TemplateKind kind, string field) => Rules.FirstOrDefault(r => r.Kind == kind && r.Field == field);

        // a required field is marked with an asterisk
        public static bool Required(TemplateKind kind, string field, IDictionary<string, string> v) => RuleFor(kind, field)?.Starred(v) ?? false;

        public static bool Disabled(TemplateKind kind, string field, IDictionary<string, string> v) => RuleFor(kind, field)?.Off(v) ?? false;

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

        // What's missing or wrong when Save is clicked, field by field: what the website checks, in
        // b0r3d QR's words.
        public static List<KeyValuePair<string, string>> Problems(TemplateKind kind, IDictionary<string, string> v) =>
            Rules.Where(r => r.Kind == kind)
                 .Select(r => new KeyValuePair<string, string>(r.Field, r.Problem(v)))
                 .Where(p => p.Value != null)
                 .ToList();

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

        // whether a field's message goes once something is typed in it (all but the Wi-Fi password's)
        public static bool ClearsWhenFilled(TemplateKind kind, string field) => !(RuleFor(kind, field)?.MessageStays ?? false);

        // the checks of what's typed (an IBAN's check digits, an amount) go as soon as the field changes
        public static bool ClearsWhenChanged(string message) => message == IbanCheck || message == AmountCheck;

        // What "Use example" puts in the template on show, field by field in the form's own order. A
        // field given "" is emptied; one that isn't listed keeps what's in it (a contact's version, say).
        public static Dictionary<string, string> Example(TemplateKind kind, DateTime now) =>
            ExampleOf(kind, now).ToDictionary(pair => pair.Field, pair => pair.Value, StringComparer.Ordinal);

        static (string Field, string Value)[] ExampleOf(TemplateKind kind, DateTime now)
        {
            string Local(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
            switch (kind)
            {
                case TemplateKind.Url: return new[] { ("url", "https://b0r3d.org") };
                case TemplateKind.Email:
                    return new[]
                    {
                        ("address", "rukus@example.com"), ("cc", "staff@example.com"), ("bcc", "vet@example.com"),
                        ("subject", "My bowl"), ("body", "It's empty again. Please see to it."),
                    };
                case TemplateKind.Phone: return new[] { ("phone", "+1-202-555-0100") };
                case TemplateKind.Sms: return new[] { ("phone", "+1-202-555-0100"), ("message", "Dinner at six? Ruckus says yes.") };
                case TemplateKind.Wifi: return new[] { ("encryption", "WPA"), ("ssid", "b0r3d guest"), ("password", "naps-all-day"), ("hidden", "false") };
                case TemplateKind.VCard:
                    // the contact in the batch example (assets/batch/batch-example-contacts.csv), with whole phone numbers
                    return new[]
                    {
                        ("firstName", "Ruckus"), ("lastName", "Cat"), ("org", "b0r3d.org"), ("position", "Chief napper"),
                        ("phoneWork", "+1 202 555 0100"), ("phonePrivate", ""), ("phoneMobile", "+1 202 555 0199"),
                        ("email", "rukus@example.com"), ("website", "https://b0r3d.org"), ("street", "1 Loaf Lane"),
                        ("zipcode", "12345"), ("city", "Breadville"), ("state", "CA"), ("country", "USA"),
                    };
                case TemplateKind.Location: return new[] { ("latitude", "48.8584"), ("longitude", "2.2945") };   // the Eiffel Tower
                case TemplateKind.Event:
                    return new[] { ("title", "Nap time"), ("location", "The sunny windowsill"), ("startTime", Local(now)), ("endTime", Local(now.AddHours(1))) };
                case TemplateKind.Gs1DigitalLink:
                    // GS1's own example number, so the example can't point at a real product
                    return new[] { ("gtin", "09506000134352"), ("lot", "ABC123"), ("serial", ""), ("expiry", "2027-12-31"), ("domain", "") };
                case TemplateKind.Epc:
                    return new[]
                    {
                        ("version", "002"), ("name", "Ruckus Cat"), ("iban", "DE89 3704 0044 0532 0130 00"), ("bic", "COBADEFFXXX"),
                        ("amount", "12.50"), ("purpose", ""), ("remittanceText", "Cat food"), ("remittanceReference", ""), ("originatorInfo", ""),
                    };
                default: return new[] { ("text", "Ruckus was here.") };
            }
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
