using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using B0r3dQR.Design;

namespace B0r3dQR.Tests
{
    // The data templates against the website's own (WebsiteTemplates.cs, made in Central time).
    static partial class TestMain
    {
        static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");

        static TemplateKind KindOf(string id) => Enum.GetValues(typeof(TemplateKind)).Cast<TemplateKind>().First(k => Templates.Id(k) == id);

        static Dictionary<string, string> FieldsOf(string json) =>
            ((Dictionary<string, object>)Json.Parse(json)).ToDictionary(p => p.Key, p => (string)p.Value);

        static string Masked(string text) => Regex.Replace(Regex.Replace(text, "(?m)^DTSTAMP:.*$", "DTSTAMP:*"), "(?m)^UID:.*$", "UID:*");

        // a text with its line breaks shown, for a failure message
        static string Show(string text) => "\"" + (text ?? "(nothing)").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

        // every template writes the website's text from the same fields, byte for byte
        static void TemplatesWriteLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (var w in WebsiteTemplateTexts)
            {
                string made = Masked(Templates.Make(KindOf(w.Kind), FieldsOf(w.Fields), Central));
                if (made != w.Text) wrong.Add(w.Kind + " " + w.Fields + ": " + Show(made) + " (the website: " + Show(w.Text) + ")");
            }
            Check(wrong.Count == 0, wrong.Count + " of " + WebsiteTemplateTexts.Length + " differ from the website: " + string.Join(" | ", wrong.Take(4)));
            string ev = Templates.Make(TemplateKind.Event, new Dictionary<string, string> { ["title"] = "x" }, Central, new DateTime(2026, 11, 1, 6, 5, 4, DateTimeKind.Utc), "abcdefghijklmnopqr");
            Check(ev.Contains("\nDTSTAMP:20261101T060504Z\nUID:abcdefghijklmnopqr@qr.b0r3d.org\n"), "an event's stamp and id: " + Show(ev));
            Check(Regex.IsMatch(Templates.NewUid(), "^[0-9a-z]{18}$") && Templates.NewUid() != Templates.NewUid(), "an event's id should be 18 random base-36 characters, as the website makes them");
        }

        // texts are read back into the same template and fields as on the website
        static void TemplatesReadLikeTheWebsite()
        {
            var wrong = new List<string>();
            foreach (var r in WebsiteTemplateReads)
            {
                TemplateKind kind = Templates.Detect(r.Text, out Dictionary<string, string> fields, Central);
                Dictionary<string, string> expected = FieldsOf(r.Fields);
                bool same = Templates.Id(kind) == r.Kind && fields.Count == expected.Count && expected.All(p => fields.TryGetValue(p.Key, out string v) && v == p.Value);
                if (!same) wrong.Add(Show(r.Text) + ": " + Templates.Id(kind) + " " + Json.Write(fields.ToDictionary(p => p.Key, p => (object)p.Value)).Replace("\n", "") + " (the website: " + r.Kind + " " + r.Fields + ")");
            }
            Check(wrong.Count == 0, wrong.Count + " of " + WebsiteTemplateReads.Length + " read differently from the website: " + string.Join(" | ", wrong.Take(3)));
        }

        // the forms' rules: what's required, the messages, opening a text, number fields, the examples
        static void TemplateFormsLikeTheWebsite()
        {
            List<string> Says(TemplateKind kind, Dictionary<string, string> v) => TemplateForms.Problems(kind, v).Select(p => p.Key + ": " + p.Value).ToList();
            var wifi = new Dictionary<string, string> { ["encryption"] = "WPA" };
            Check(Says(TemplateKind.Wifi, wifi).SequenceEqual(new[] { "ssid: Type the network's name.", "password: Type the password, or choose No encryption." }), "Wi-Fi: " + string.Join(" | ", Says(TemplateKind.Wifi, wifi)));
            wifi["encryption"] = "nopass";
            Check(Says(TemplateKind.Wifi, wifi).Count == 1 && !TemplateForms.Required(TemplateKind.Wifi, "password", wifi) && TemplateForms.Disabled(TemplateKind.Wifi, "password", wifi), "an open network needs no password, and its box is off");
            var epc = new Dictionary<string, string> { ["iban"] = "DE89370400440532013001", ["version"] = "001" };
            Check(Says(TemplateKind.Epc, epc).SequenceEqual(new[] { "name: Type the beneficiary's name.", "iban: " + TemplateForms.IbanCheck, "bic: Version 1 needs the BIC: type it, or choose version 2." }), "EPC: " + string.Join(" | ", Says(TemplateKind.Epc, epc)));
            Check(TemplateForms.LiveNote(TemplateKind.Epc, "iban", new Dictionary<string, string> { ["iban"] = "DE89 3704 0044 053" }) == TemplateForms.IbanCheck &&
                  TemplateForms.LiveNote(TemplateKind.Epc, "iban", new Dictionary<string, string> { ["iban"] = "DE89 3704" }) == null, "an IBAN is checked as it's typed, from 15 characters");
            // the IBANs the website's form takes (WebsiteTemplates.cs), in capitals as toUpperCase makes them
            List<string> ibans = WebsiteIbanChecks.Where(c => Templates.ValidIban(c.Iban) != c.Valid).Select(c => Show(c.Iban) + (c.Valid ? " (the website takes it)" : " (the website doesn't)")).ToList();
            Check(ibans.Count == 0, ibans.Count + " of " + WebsiteIbanChecks.Length + " IBANs are checked differently from the website: " + string.Join(" | ", ibans));
            Check(Says(TemplateKind.Gs1DigitalLink, new Dictionary<string, string>()).SequenceEqual(new[] { "gtin: Enter the product number (GTIN)." }), "GS1 with no number");
            Check(Says(TemplateKind.Event, new Dictionary<string, string>()).Count == 3 && Says(TemplateKind.VCard, new Dictionary<string, string>()).Count == 0, "an event needs its title and times; a contact needs nothing");

            // a payment: what its lines are written from, and an amount that's asked about rather than guessed
            var pay = new Dictionary<string, string> { ["name"] = " ", ["iban"] = "DE89370400440532013000", ["version"] = "001", ["bic"] = "  " };
            Check(Says(TemplateKind.Epc, pay).SequenceEqual(new[] { "name: Type the beneficiary's name.", "bic: Version 1 needs the BIC: type it, or choose version 2." }),
                "a name or BIC of spaces alone is asked for, so Save can't write a broken code or none: " + string.Join(" | ", Says(TemplateKind.Epc, pay)));
            foreach (string bad in new[] { "1.234,56", "1,234.56", "1 000", "€25", "0", "0.001", "1000000000", ".", "-5", "1e3" })
                Check(!TemplateForms.AmountOk(bad), "the amount " + Show(bad) + " should be asked about, not guessed");
            foreach (string good in new[] { "", "25", "25.00", "12,50", ".5", "25.", "999999999.99", " 7 " })
                Check(TemplateForms.AmountOk(good), "the amount " + Show(good) + " should do");
            pay["name"] = "Rukus Cat";
            pay["bic"] = "DEUTDEFF";
            pay["amount"] = "1.234,56";
            Check(Says(TemplateKind.Epc, pay).SequenceEqual(new[] { "amount: " + TemplateForms.AmountCheck }), "an amount it can't read: " + string.Join(" | ", Says(TemplateKind.Epc, pay)));
            pay["amount"] = "12,50";
            Check(Says(TemplateKind.Epc, pay).Count == 0 && Templates.Make(TemplateKind.Epc, pay).Contains("\nEUR12.50"), "a comma for the cents works, as on the website");

            TemplateField Field(TemplateKind kind, string name) => TemplateForms.Fields(kind).First(f => f.Name == name);
            Check(TemplateForms.AsTyped(Field(TemplateKind.Url, "url"), " \thttps://b0r3d.org\r\n ") == "https://b0r3d.org" &&
                  TemplateForms.AsTyped(Field(TemplateKind.VCard, "email"), " rukus@example.com ") == "rukus@example.com" &&
                  TemplateForms.AsTyped(Field(TemplateKind.Gs1DigitalLink, "domain"), "https://id.example.com ") == "https://id.example.com" &&
                  TemplateForms.AsTyped(Field(TemplateKind.Email, "subject"), " a\r\nb ") == " ab " &&
                  TemplateForms.AsTyped(Field(TemplateKind.Email, "body"), "a\r\nb\rc\nd") == "a\nb\nc\nd", "typed text as a browser's fields give it");

            Templates.Detect("WIFI:S:home;T:WPA2;P:pw;H:true;;", out Dictionary<string, string> read);
            Dictionary<string, string> form = TemplateForms.FromText(TemplateKind.Wifi, read);
            Check(form["ssid"] == "home" && form["encryption"] == "WPA" && form["password"] == "pw" && form["hidden"] == "true", "a Wi-Fi text fills the form");
            Templates.Detect("https://id.gs1.org/01/09506000134352", out read);
            Check(TemplateForms.FromText(TemplateKind.Gs1DigitalLink, read)["domain"] == "", "GS1's own resolver shows as none, as on the website");
            Templates.Detect("geo:abc,1", out read);
            Check(TemplateForms.Problems(TemplateKind.Location, TemplateForms.FromText(TemplateKind.Location, read)).Select(p => p.Key).SequenceEqual(new[] { "latitude" }),
                "a coordinate that isn't a number is asked for, so Save can't write an empty code over the text");

            Check(TemplateForms.TypedNumber("40.70") == "40.7" && TemplateForms.TypedNumber("1e3") == "1000" && TemplateForms.TypedNumber("abc") == "" &&
                  TemplateForms.TypedNumber("5.") == "" && TemplateForms.TypedNumber(".5") == "0.5" && TemplateForms.TypedNumber("-0") == "0", "number fields as a browser's give their value");
            Check(Js.NumberToString(0.00001) == "0.00001" && Js.NumberToString(1e-7) == "1e-7" && Js.NumberToString(1e21) == "1e+21" && Js.NumberToString(123.456) == "123.456" &&
                  Js.NumberToString(-1.5) == "-1.5" && Js.NumberToString(1e20) == "100000000000000000000" && Js.NumberToString(0.7999999999999999) == "0.7999999999999999" &&
                  Js.NumberToString(0.1 + 0.2) == "0.30000000000000004" && Js.NumberToString(123e-20) == "1.23e-18", "numbers written the JavaScript way");

            var now = new DateTime(2026, 11, 1, 14, 30, 0);
            Check(Templates.Make(TemplateKind.Wifi, TemplateForms.Example(TemplateKind.Wifi, now)) == "WIFI:T:WPA;S:b0r3d guest;P:naps-all-day;;" &&
                  Templates.Make(TemplateKind.Gs1DigitalLink, TemplateForms.Example(TemplateKind.Gs1DigitalLink, now)) == "https://id.gs1.org/01/09506000134352/10/ABC123?17=271231" &&
                  TemplateForms.Example(TemplateKind.Event, now)["endTime"] == "2026-11-01T15:30", "the examples");
            foreach (TemplateKind kind in TemplateForms.Types)
                Check(TemplateForms.Problems(kind, TemplateForms.Example(kind, now)).Count == 0, TemplateForms.Label(kind) + "'s example should be complete");
            Check(TemplateForms.Menu.Select(TemplateForms.Label).SequenceEqual(new[] { "Text", "URL", "WiFi", "vCard", "Email", "Phone", "SMS", "Location", "Event", "EPC QR (SEPA Payment)", "Product (GS1 Digital Link)" }),
                "the Data section's list in the website's order");
        }

        // the window: filled from the text, its messages, Save writing the code's text, what's typed kept
        static void TemplatesInTheWindow()
        {
            MainWindow window = NewMain();
            Check(window.TemplateBox.SelectedIndex == -1 && window.TemplateHint.Visibility == Visibility.Visible, "with no text, no template is chosen");
            Type(window, "WIFI:T:WPA;S:home;P:pw;;");
            Check((string)window.TemplateBox.SelectedItem == "WiFi" && window.TemplateHint.Visibility == Visibility.Collapsed, "the list shows the template the text is");

            TemplateWindow form = window.TemplateFor(TemplateKind.Wifi);
            LayOut(form.Root, 600, 680);
            Check(((TextBox)form.Input("ssid")).Text == "home" && ((PasswordBox)form.Input("password")).Password == "pw" && ((ComboBox)form.Input("encryption")).SelectedIndex == 2,
                "the Wi-Fi form should be filled from the text");
            Check(form.HasAsterisk("ssid") && form.HasAsterisk("password"), "the required fields have their asterisks");
            ((TextBox)form.Input("ssid")).Text = "office";
            Check(form.Save() && form.Result == "WIFI:T:WPA;S:office;P:pw;;", "Save should write the website's text: " + Show(form.Result));
            window.UseTemplateText(form.Result);
            Check(window.Code?.Text == "WIFI:T:WPA;S:office;P:pw;;", "the written text becomes the code");

            TemplateWindow url = window.TemplateFor(TemplateKind.Url);
            LayOut(url.Root, 600, 680);
            Check(((TextBox)url.Input("url")).Text == "" && !url.Save() && url.ProblemText("url") == "Type the web address.", "an empty URL can't be saved, and says why");
            ((TextBox)url.Input("url")).Text = "b0r3d.org";
            Check(url.ProblemText("url") == "", "the message goes once the field is filled in");
            Check(url.Save() && url.Result == "https://b0r3d.org", "a URL without https:// gets it, as on the website");
            TemplateWindow again = window.TemplateFor(TemplateKind.Url);
            Check(((TextBox)again.Input("url")).Text == "b0r3d.org", "what was typed in a form is kept for next time, as on the website");
            TemplateWindow spaced = window.TemplateFor(TemplateKind.Url);
            ((TextBox)spaced.Input("url")).Text = " https://b0r3d.org/x\r\n";
            Check(spaced.Save() && spaced.Result == "https://b0r3d.org/x", "a link pasted with spaces round it goes in without them, as from a browser's url field: " + Show(spaced.Result));

            TemplateWindow mail = window.TemplateFor(TemplateKind.Email);
            ((TextBox)mail.Input("address")).Text = "rukus@example.com ";
            ((TextBox)mail.Input("body")).Text = "line 1\r\nline 2";   // as Enter types it
            Check(mail.Save() && mail.Result == "mailto:rukus@example.com?body=line%201%0Aline%202", "a message's line breaks are written as the website writes them: " + Show(mail.Result));

            TemplateWindow meeting = window.TemplateFor(TemplateKind.Event);
            Check(!meeting.Save() && meeting.ProblemText("title") == "Give the event a title.", "an empty event can't be saved");
            meeting.ExampleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(meeting.ProblemText("title") == "" && meeting.ProblemText("startTime") == "" && meeting.ProblemText("endTime") == "", "Use example takes the messages off the fields it fills in");

            TemplateWindow card = window.TemplateFor(TemplateKind.VCard);
            UIElement Cell(string name)
            {
                DependencyObject e = card.Input(name);
                while (e != null && !(LogicalTreeHelper.GetParent(e) is Grid g && g.ColumnDefinitions.Count == 3)) e = LogicalTreeHelper.GetParent(e);
                return (UIElement)e;
            }
            Check(Grid.GetColumnSpan(Cell("version")) == 3 && Grid.GetRow(Cell("firstName")) == Grid.GetRow(Cell("lastName")) && Grid.GetColumn(Cell("lastName")) == 2 &&
                  Grid.GetRow(Cell("org")) == Grid.GetRow(Cell("firstName")) + 1 && Grid.GetColumn(Cell("org")) == 0, "a contact's fields go two to a row");

            TemplateWindow place = window.TemplateFor(TemplateKind.Location);
            ((TextBox)place.Input("latitude")).Text = "51.4770";
            ((TextBox)place.Input("longitude")).Text = "-0.0050";
            Check(place.Save() && place.Result == "geo:51.477,-0.005", "typed numbers are written the way the website's number fields give them: " + Show(place.Result));

            TemplateWindow example = window.TemplateFor(TemplateKind.Epc);
            example.ExampleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(((TextBox)example.Input("name")).Text == "Rukus Cat" && example.Save() && example.Result.StartsWith("BCD\n002\n1\nSCT\nCOBADEFFXXX\nRukus Cat\nDE89370400440532013000\nEUR12.50", StringComparison.Ordinal),
                "Use example fills in the example: " + Show(example.Result));
            Check(((TextBox)example.Input("remittanceText")).IsEnabled, "the payment reference is on while there's no structured one");
            ((TextBox)example.Input("remittanceReference")).Text = "RF18";
            Check(!((TextBox)example.Input("remittanceText")).IsEnabled, "a structured reference turns the payment reference off");

            TemplateWindow amount = window.TemplateFor(TemplateKind.Epc);
            ((TextBox)amount.Input("amount")).Text = "1.234,56";
            Check(!amount.Save() && amount.ProblemText("amount") == TemplateForms.AmountCheck, "an amount it can't read is asked about, not guessed");
            ((TextBox)amount.Input("amount")).Text = "1234,56";
            Check(amount.ProblemText("amount") == "" && amount.Save() && amount.Result.Contains("\nEUR1234.56\n"), "the message goes once the amount changes: " + Show(amount.Result));

            // the list of templates: looking through it closed opens nothing; Edit… opens the one it's on
            var asked = new List<TemplateKind>();
            window.AskTemplate = k =>
            {
                asked.Add(k);
                return null;
            };
            string before = window.DataBox.Text;
            window.TemplateBox.SelectedIndex = Array.IndexOf(TemplateForms.Menu, TemplateKind.VCard);   // Down on the closed list
            Pump(() => false, 100);
            Check(asked.Count == 0 && window.DataBox.Text == before, "moving through the closed list opens no form");
            window.EditTemplateButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(asked.SequenceEqual(new[] { TemplateKind.VCard }) && (string)window.TemplateBox.SelectedItem == "WiFi", "Edit… opens the template the list is on, and cancelled, the list shows the text's again");
            window.UseTemplateText("");
            Check(window.DataBox.Text == before, "an empty text never goes over the data");
            window.AskTemplate = null;
        }

        // the JavaScript pieces behave as a browser's do
        static void JavaScriptPiecesLikeABrowser()
        {
            Check(Js.EncodeUriComponent("a b!*'()~ü/&") == "a%20b!*'()~%C3%BC%2F%26", "encodeURIComponent: " + Js.EncodeUriComponent("a b!*'()~ü/&"));
            Check(Js.DecodeUriComponent("%E2%82%AC+x") == "€+x" && Js.DecodeUriComponent("%E2%82") == null && Js.DecodeUriComponent("100%") == null, "decodeURIComponent");
            Check(Js.SearchParam("?a=1&b=x+y%20z&a=2", "a") == "1" && Js.SearchParam("a=1&b=x+y%20z", "b") == "x y z" && Js.SearchParam("a", "a") == "" && Js.SearchParam("a=1", "c") == null &&
                  Js.SearchParam("q=%ZZ", "q") == "%ZZ" && Js.SearchParam("?a=%C2%C2", "a") == "\uFFFD\uFFFD" && Js.SearchParam("?a=%E0%A0x", "a") == "\uFFFDx", "URLSearchParams");
            Templates.Detect("BEGIN:VCARD\nVERSION:2.1\nN;ENCODING=QUOTED-PRINTABLE:=C2=C2;x\nEND:VCARD", out Dictionary<string, string> qp);
            Check(qp["lastName"] == "\uFFFD\uFFFD", "quoted-printable UTF-8 replaces each broken part as TextDecoder does: " + Show(qp["lastName"]));
            Check(Js.ParseFloat(" 12abc") == 12 && double.IsNaN(Js.ParseFloat("abc")) && Js.ParseFloat("1e3") == 1000 && Js.ParseFloat("-.5") == -0.5 && Js.ParseFloat("1e") == 1 &&
                  double.IsPositiveInfinity(Js.ParseFloat("Infinity")) && double.IsPositiveInfinity(Js.ParseFloat("1e400")), "parseFloat");
            Check(Js.ToFixed2(1.005) == "1.00" && Js.ToFixed2(0.125) == "0.13" && Js.ToFixed2(12.5) == "12.50" && Js.ToFixed2(1e3) == "1000.00" && Js.ToFixed2(0.004) == "0.00", "toFixed(2)");
            Check(Js.Trim((char)0xFEFF + "" + (char)0x00A0 + " x " + (char)0x2028) == "x" && Js.Trim((char)0x0085 + "x") == (char)0x0085 + "x", "trim takes JavaScript's spaces, not .NET's");
            Check(Templates.ValidIban("DE89 3704 0044 0532 0130 00") && !Templates.ValidIban("DE89 3704 0044 0532 0130 01"), "IBAN check digits");
            Check(Templates.GtinProblem("9506000134352") == null && Templates.GtinProblem("9506000134353") == "The check digit (the last digit) should be 2." &&
                  Templates.GtinProblem("12a") == "A GTIN has digits only." && Templates.GtinProblem("") == "Enter the product number (GTIN).", "GTIN checks in the website's words");
        }
    }
}
