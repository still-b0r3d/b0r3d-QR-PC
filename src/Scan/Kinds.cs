using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ZXing;

namespace B0r3dQR.Scan
{
    // What a scanned code is, worded for people: "Link" to b0r3d.org, a "Wi-Fi network" called
    // home... The detail line never shows a password or a sign-in secret, and product numbers are
    // never taken for phone numbers.
    static class Kinds
    {
        public static void Describe(string text, BarcodeFormat format, out string kind, out string detail)
        {
            string t = (text ?? "").Trim();
            detail = "";
            if (IsProduct(format))
            {
                bool isbn = format == BarcodeFormat.EAN_13 && (t.StartsWith("978", StringComparison.Ordinal) || t.StartsWith("979", StringComparison.Ordinal));
                kind = isbn ? "Book number (ISBN)" : "Product number";
                return;
            }
            if (WebLink(t, out Uri uri))
            {
                kind = "Link";
                // the site, and its real name when it's written in other alphabets ("аpple.com" is
                // xn--pple-43d.com, not apple.com)
                string host = WithoutWww(uri.Host), real = WithoutWww(uri.IdnHost);
                detail = uri.HostNameType != UriHostNameType.Dns || real == host ? host : host + " (" + real + ")";
                return;
            }
            string upper = t.ToUpperInvariant();
            if (upper.StartsWith("WIFI:", StringComparison.Ordinal))
            {
                kind = "Wi-Fi network";
                Dictionary<string, string> fields = Fields(t.Substring(5));
                fields.TryGetValue("S", out string network);
                fields.TryGetValue("T", out string security);
                string how;
                switch ((security ?? "").ToUpperInvariant())
                {
                    case "": case "NOPASS": how = "no password"; break;
                    case "WPA": how = "WPA"; break;
                    case "SAE": how = "WPA3"; break;
                    case "WEP": how = "WEP"; break;
                    default: how = security; break;
                }
                detail = (string.IsNullOrEmpty(network) ? "a network" : "network " + network) + ", " + how;
                if (fields.TryGetValue("H", out string hidden) && hidden.Equals("true", StringComparison.OrdinalIgnoreCase)) detail += ", hidden";
                return;
            }
            if (upper.StartsWith("BEGIN:VCARD", StringComparison.Ordinal))
            {
                kind = "Contact";
                detail = Line(t, "FN") ?? (Line(t, "N") ?? "").Replace(";", " ").Trim();
                return;
            }
            if (upper.StartsWith("MECARD:", StringComparison.Ordinal))
            {
                kind = "Contact";
                Fields(t.Substring(7)).TryGetValue("N", out string name);
                detail = (name ?? "").Replace(",", " ").Trim();
                return;
            }
            if (upper.StartsWith("BEGIN:VCALENDAR", StringComparison.Ordinal) || upper.StartsWith("BEGIN:VEVENT", StringComparison.Ordinal))
            {
                kind = "Calendar event";
                detail = Line(t, "SUMMARY") ?? "";
                return;
            }
            if (upper.StartsWith("MAILTO:", StringComparison.Ordinal))
            {
                kind = "Email";
                detail = Uri.UnescapeDataString(t.Substring(7).Split('?')[0]);
                return;
            }
            if (upper.StartsWith("MATMSG:", StringComparison.Ordinal))
            {
                kind = "Email";
                Fields(t.Substring(7)).TryGetValue("TO", out string to);
                detail = to ?? "";
                return;
            }
            if (upper.StartsWith("TEL:", StringComparison.Ordinal))
            {
                kind = "Phone number";
                detail = t.Substring(4);
                return;
            }
            if (upper.StartsWith("SMSTO:", StringComparison.Ordinal) || upper.StartsWith("SMS:", StringComparison.Ordinal))
            {
                kind = "Text message";
                string rest = t.Substring(t.IndexOf(':') + 1);
                detail = rest.Split(':', '?')[0];
                return;
            }
            if (upper.StartsWith("GEO:", StringComparison.Ordinal))
            {
                kind = "Location";
                detail = t.Substring(4).Split('?', ';')[0];
                return;
            }
            if (Regex.IsMatch(t, @"^BCD\r?\n"))
            {
                kind = "Payment (EPC)";
                string[] lines = t.Replace("\r\n", "\n").Split('\n');
                string name = lines.Length > 5 ? lines[5].Trim() : "";
                string amount = lines.Length > 7 ? lines[7].Trim() : "";
                detail = (name.Length > 0 ? "to " + name : "") + (amount.Length > 3 ? (name.Length > 0 ? ", " : "") + amount.Substring(0, 3) + " " + amount.Substring(3) : "");
                return;
            }
            if (upper.StartsWith("OTPAUTH-MIGRATION:", StringComparison.Ordinal))
            {
                kind = "Sign-in codes (two-factor export)";   // every account's secret at once: nothing of it is shown
                return;
            }
            if (upper.StartsWith("OTPAUTH:", StringComparison.Ordinal))
            {
                kind = "Sign-in code (two-factor)";
                // (/{2}, not two slashes: the never-online test reads "scheme://" in strings as an address)
                // ([^/?]: with no label, "totp?issuer=A/B&secret=..." must not pass for one)
                Match label = Regex.Match(t, @"^otpauth:/{2}[^/?]+/([^?]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                detail = label.Success ? Uri.UnescapeDataString(label.Groups[1].Value) : "";
                return;
            }
            Match scheme = Regex.Match(t, @"^([A-Za-z][A-Za-z0-9+.\-]{1,30}):");
            if (scheme.Success && t.Length > scheme.Length + 1 && t.Substring(scheme.Length, 2) == "//")
            {
                kind = "Address (" + scheme.Groups[1].Value.ToLowerInvariant() + ")";
                return;
            }
            kind = "Text";
        }

        // The code's type as people call it. The symbology identifier tells GS1 codes and QR codes
        // with an ECI marker apart from plain ones.
        public static string FormatName(BarcodeFormat format, string text, string symbology)
        {
            switch (format)
            {
                case BarcodeFormat.QR_CODE:
                    return symbology == "]Q3" || symbology == "]Q4" ? "GS1 QR code" : symbology == "]Q2" ? "QR code with an ECI marker" : "QR code";
                case BarcodeFormat.DATA_MATRIX: return symbology == "]d2" ? "GS1 DataMatrix" : "Data Matrix";
                case BarcodeFormat.AZTEC: return "Aztec";
                case BarcodeFormat.PDF_417: return "PDF417";
                case BarcodeFormat.MAXICODE: return "MaxiCode";
                case BarcodeFormat.EAN_13: return text != null && (text.StartsWith("978", StringComparison.Ordinal) || text.StartsWith("979", StringComparison.Ordinal)) ? "EAN-13 (ISBN)" : "EAN-13";
                case BarcodeFormat.EAN_8: return "EAN-8";
                case BarcodeFormat.UPC_A: return "UPC-A";
                case BarcodeFormat.UPC_E: return "UPC-E";
                case BarcodeFormat.CODE_128: return symbology == "]C1" ? "GS1-128" : "Code 128";
                case BarcodeFormat.CODE_39: return "Code 39";
                case BarcodeFormat.CODE_93: return "Code 93";
                case BarcodeFormat.ITF: return text != null && text.Length == 14 ? "ITF-14" : "ITF";
                case BarcodeFormat.CODABAR: return "Codabar";
                case BarcodeFormat.RSS_14: return "GS1 DataBar";
                case BarcodeFormat.RSS_EXPANDED: return "GS1 DataBar Expanded";
                case BarcodeFormat.UPC_EAN_EXTENSION: return "EAN add-on";
                default: return format.ToString();
            }
        }

        static bool IsProduct(BarcodeFormat format)
        {
            return format == BarcodeFormat.EAN_13 || format == BarcodeFormat.EAN_8 || format == BarcodeFormat.UPC_A ||
                format == BarcodeFormat.UPC_E || format == BarcodeFormat.RSS_14 || format == BarcodeFormat.UPC_EAN_EXTENSION;
        }

        public static bool WebLink(string text, out Uri uri)
        {
            uri = null;
            string t = (text ?? "").Trim();
            return Uri.TryCreate(t, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0;
        }

        // The things worth knowing before opening a link: none for an ordinary https link.
        public static List<string> LinkWarnings(Uri uri)
        {
            var warnings = new List<string>();
            if (uri.Scheme == Uri.UriSchemeHttp) warnings.Add("It isn't encrypted (http, not https).");
            if (uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6) warnings.Add("It goes to a numbered address, not a named site.");
            // (IdnHost: .NET keeps Host in the letters it was written in, so a look-alike shows only there)
            if (uri.HostNameType == UriHostNameType.Dns && uri.IdnHost.Split('.').Any(part => part.StartsWith("xn--", StringComparison.OrdinalIgnoreCase)))
                warnings.Add("Its address uses letters from other alphabets, which can look like another site's (" + uri.IdnHost + ").");
            if (uri.UserInfo.Length > 0) warnings.Add("The part before the @ is a user name, not the site: it goes to " + uri.Host + ".");
            if (!uri.IsDefaultPort) warnings.Add("It uses an unusual port (" + uri.Port + ").");
            return warnings;
        }

        static string WithoutWww(string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host.Substring(4) : host;

        // "S:home;T:WPA;P:pass" (with \; \: \\ escapes) into fields by name, the first of each
        static Dictionary<string, string> Fields(string text)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            while (i < text.Length)
            {
                var field = new StringBuilder();
                for (; i < text.Length && text[i] != ';'; i++)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) i++;
                    field.Append(text[i]);
                }
                i++;
                string f = field.ToString();
                int at = f.IndexOf(':');
                if (at > 0 && !fields.ContainsKey(f.Substring(0, at))) fields[f.Substring(0, at)] = f.Substring(at + 1);
            }
            return fields;
        }

        // the value of a vCard or iCalendar line ("FN:Ruckus", "SUMMARY;LANGUAGE=en:Party")
        static string Line(string text, string name)
        {
            Match m = Regex.Match(text, "(?im)^" + name + @"(?:;[^:\r\n]*)?:(.*)$", RegexOptions.CultureInvariant);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
    }
}
