using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using B0r3dQR.Design;

namespace B0r3dQR.Barcodes
{
    // the barcode types the website makes besides QR, in its order
    enum BarcodeType { DataMatrix, Gs1DataMatrix, Aztec, Pdf417, Ean13, Ean8, UpcA, UpcE, Isbn, Code128, Gs1_128, Itf14, Code39 }

    // One of them as the website describes it (its src/lib/barcode/formats.ts): its name and group in the
    // Code type list, the hint under the data and the example it starts with, how it tidies what's
    // typed, its checks in its words, and what a scanner should read back.
    sealed class BarcodeFormat
    {
        public BarcodeType Type;
        public string Id, Label, Group, Hint, Example;
        public bool Linear;            // bars, which can have their text underneath
        public bool Gs1;               // GS1 data, written (01)09506000134352(10)ABC
        public bool KeepsQuietZones;   // its margins are part of it (ITF-14's box)
        public ZXing.BarcodeFormat ReadFormat;
        public Func<string, string> Prepare, Check, ExpectedText;
        public Func<string, string> NormalizeRead = text => text;
        public Func<string, string> EncodeText;   // what zint's given, when it isn't the tidied text
    }

    static class BarcodeFormats
    {
        public static readonly string[] Groups = { "2D codes", "Product barcodes", "Shipping and inventory" };

        public static BarcodeFormat Get(BarcodeType type) => All[(int)type];

        public static BarcodeFormat ById(string id) => Array.Find(All, f => f.Id == id);

        // the GS1 check digit for the digits before it (EAN, UPC, ITF-14, GTIN, SSCC, GLN)
        public static int CheckDigit(string body)
        {
            int sum = 0;
            for (int i = 0; i < body.Length; i++)
            {
                int digit = body[body.Length - 1 - i] - '0';
                sum += i % 2 == 0 ? digit * 3 : digit;
            }
            return (10 - sum % 10) % 10;
        }

        // UPC-E (6 digits, or 7 or 8 with the number system first) as the UPC-A body it stands for, without its check digit
        public static string UpcEToUpcA(string upce)
        {
            string s = upce.Length >= 7 ? upce.Substring(0, 1) : "0";
            string body = upce.Length >= 7 ? upce.Substring(1, 6) : upce.Substring(0, 6);
            char d6 = body[5];
            if (d6 == '0' || d6 == '1' || d6 == '2') return s + body.Substring(0, 2) + d6 + "0000" + body.Substring(2, 3);
            if (d6 == '3') return s + body.Substring(0, 3) + "00000" + body.Substring(3, 2);
            if (d6 == '4') return s + body.Substring(0, 4) + "00000" + body[4];
            return s + body.Substring(0, 5) + "0000" + d6;
        }

        public static int UpcECheckDigit(string upce) => CheckDigit(UpcEToUpcA(upce));

        // a 12- or 13-digit UPC-A back to 8-digit UPC-E when it can be, or null
        public static string UpcAToUpcE(string upca)
        {
            if (upca.Length == 13 && upca[0] == '0') upca = upca.Substring(1);
            if (upca.Length != 12) return null;
            char s = upca[0];
            if (s != '0' && s != '1') return null;
            string d = upca.Substring(1, 10);
            char check = upca[11];
            string e;
            if ((d[2] == '0' || d[2] == '1' || d[2] == '2') && d.Substring(3, 4) == "0000") e = d.Substring(0, 2) + d.Substring(7, 3) + d[2];
            else if (d.Substring(3, 5) == "00000") e = d.Substring(0, 3) + d.Substring(8, 2) + "3";
            else if (d.Substring(4, 5) == "00000") e = d.Substring(0, 4) + d[9] + "4";
            else if (d.Substring(5, 4) == "0000" && d[9] >= '5') e = d.Substring(0, 5) + d[9];
            else return null;
            return s + e + check;
        }

        // the ISBN-10 check character ('0' to '9' or 'X') for 9 digits
        public static string Isbn10CheckDigit(string body9)
        {
            int sum = 0;
            for (int i = 0; i < 9; i++) sum += (body9[i] - '0') * (10 - i);
            int rest = (11 - sum % 11) % 11;
            return rest == 10 ? "X" : rest.ToString();
        }

        // the AIs whose value ends in a check digit, and the value's length: SSCC, GTIN, content GTIN and the GLNs
        static int Gs1CheckedLength(string ai)
        {
            switch (ai)
            {
                case "00": return 18;
                case "01":
                case "02": return 14;
                case "410":
                case "411":
                case "412":
                case "413":
                case "414":
                case "415":
                case "416":
                case "417": return 13;
                default: return 0;
            }
        }

        // what zint doesn't report itself: the bracket syntax and check digits (zint checks each AI's length, not its check digit)
        public static string CheckGs1(string text)
        {
            if (text.Length == 0) return "Enter GS1 data, e.g. (01)09506000134352(10)ABC123.";
            if (!Regex.IsMatch(text, @"^(\([0-9]{2,4}\)[^()]+)+\z")) return "Write GS1 data as (AI)value pairs, e.g. (01)09506000134352(10)ABC123.";
            foreach (Match m in Regex.Matches(text, @"\(([0-9]{2,4})\)([^()]+)"))
            {
                string ai = m.Groups[1].Value, value = m.Groups[2].Value;
                int length = Gs1CheckedLength(ai);
                if (length > 0 && value.Length == length && Regex.IsMatch(value, @"^[0-9]+\z"))
                {
                    int expected = CheckDigit(value.Substring(0, length - 1));
                    if (value[length - 1] - '0' != expected) return "The check digit of (" + ai + ") should be " + expected + ", not " + value[length - 1] + ".";
                }
            }
            return null;
        }

        // spaces (JavaScript's) and hyphens out of numbers
        static string StripSeparators(string text) => Regex.Replace(text, Js.Space, "").Replace("-", "");

        static string StripSpaces(string text) => Regex.Replace(text, Js.Space, "");

        static bool AllDigits(string text) => Regex.IsMatch(text, @"^[0-9]+\z");

        // JavaScript's toUpperCase for what can pass Code 39's check: special casings that give capital
        // letters (ß, the ligatures ﬀ to ﬆ), then the one-to-one ones
        static string JsUpper(string text)
        {
            var upper = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                switch ((int)c)
                {
                    case 0xDF: upper.Append("SS"); break;
                    case 0xFB00: upper.Append("FF"); break;
                    case 0xFB01: upper.Append("FI"); break;
                    case 0xFB02: upper.Append("FL"); break;
                    case 0xFB03: upper.Append("FFI"); break;
                    case 0xFB04: upper.Append("FFL"); break;
                    case 0xFB05:
                    case 0xFB06: upper.Append("ST"); break;
                    case 0x131: upper.Append('I'); break;   // dotless i and long s, which .NET leaves as they are
                    case 0x17F: upper.Append('S'); break;
                    default: upper.Append(char.ToUpperInvariant(c)); break;
                }
            }
            return upper.ToString();
        }

        static string Same(string text) => text;

        static Func<string, string> AnyText(string label) => text => text.Length > 0 ? null : "Enter the text for the " + label + ".";

        // EAN-13, EAN-8, UPC-A and ITF-14: `length` digits, the check digit optional
        static BarcodeFormat NumberFormat(BarcodeType type, string id, string label, string group, string example, ZXing.BarcodeFormat read, int length) =>
            new BarcodeFormat
            {
                Type = type, Id = id, Label = label, Group = group, Example = example, ReadFormat = read, Linear = true,
                Hint = length + " digits; the check digit is added for you. With it, " + (length + 1) + ".",
                Prepare = StripSeparators,
                Check = text =>
                {
                    if (text.Length == 0) return "Enter " + length + " digits.";
                    if (!AllDigits(text)) return label + " can only hold digits.";
                    if (text.Length != length && text.Length != length + 1)
                        return label + " needs " + length + " digits (or " + (length + 1) + " with the check digit); this has " + text.Length + ".";
                    if (text.Length == length + 1)
                    {
                        int expected = CheckDigit(text.Substring(0, length));
                        if (text[length] - '0' != expected) return "The check digit (the last digit) should be " + expected + ". Leave it off and it's added for you.";
                    }
                    return null;
                },
                ExpectedText = text => text.Length == length ? text + CheckDigit(text) : text,
            };

        public static readonly BarcodeFormat[] All =
        {
            new BarcodeFormat
            {
                Type = BarcodeType.DataMatrix, Id = "datamatrix", Label = "Data Matrix", Group = "2D codes", ReadFormat = ZXing.BarcodeFormat.DATA_MATRIX,
                Hint = "Any text. Small and sturdy; common on parts, labels and packaging.", Example = "https://b0r3d.org",
                Prepare = Same, Check = AnyText("Data Matrix"), ExpectedText = Same,
            },
            new BarcodeFormat
            {
                Type = BarcodeType.Gs1DataMatrix, Id = "gs1-datamatrix", Label = "GS1 DataMatrix", Group = "2D codes", ReadFormat = ZXing.BarcodeFormat.DATA_MATRIX, Gs1 = true,
                Hint = "GS1 data as (AI)value pairs: product number (01), expiry date (17, YYMMDD), batch (10), serial (21)…",
                Example = "(01)09506000134352(17)271231(10)ABC123",
                Prepare = StripSpaces, Check = CheckGs1, ExpectedText = Same,
            },
            new BarcodeFormat
            {
                Type = BarcodeType.Aztec, Id = "aztec", Label = "Aztec", Group = "2D codes", ReadFormat = ZXing.BarcodeFormat.AZTEC,
                Hint = "Any text. Used on train and plane tickets; needs no blank border.", Example = "https://b0r3d.org",
                Prepare = Same, Check = AnyText("Aztec code"), ExpectedText = Same,
            },
            new BarcodeFormat
            {
                Type = BarcodeType.Pdf417, Id = "pdf417", Label = "PDF417", Group = "2D codes", ReadFormat = ZXing.BarcodeFormat.PDF_417,
                Hint = "Any text. A wide, stacked code used on boarding passes, ID cards and shipping labels.", Example = "b0r3d PDF417 example",
                Prepare = Same, Check = AnyText("PDF417 code"), ExpectedText = Same,
            },
            NumberFormat(BarcodeType.Ean13, "ean13", "EAN-13", "Product barcodes", "950600013435", ZXing.BarcodeFormat.EAN_13, 12),
            NumberFormat(BarcodeType.Ean8, "ean8", "EAN-8", "Product barcodes", "9638507", ZXing.BarcodeFormat.EAN_8, 7),
            UpcA(),
            new BarcodeFormat
            {
                Type = BarcodeType.UpcE, Id = "upce", Label = "UPC-E", Group = "Product barcodes", ReadFormat = ZXing.BarcodeFormat.UPC_E, Linear = true,
                Hint = "6 digits (or 7 with number system 0/1; 8 with check digit). Compact barcode for small packages.", Example = "01234565",
                Prepare = StripSeparators,
                Check = text =>
                {
                    if (text.Length == 0) return "Enter 6, 7 or 8 digits.";
                    if (!AllDigits(text)) return "UPC-E can only hold digits.";
                    if (text.Length != 6 && text.Length != 7 && text.Length != 8)
                        return "UPC-E needs 6 digits (or 7 with system digit, 8 with check digit); this has " + text.Length + ".";
                    if (text.Length >= 7 && text[0] != '0' && text[0] != '1') return "The first digit (number system) must be 0 or 1.";
                    if (text.Length == 8)
                    {
                        int expected = UpcECheckDigit(text.Substring(0, 7));
                        if (text[7] - '0' != expected) return "The check digit (the last digit) should be " + expected + ". Leave it off and it's added for you.";
                    }
                    return null;
                },
                ExpectedText = text =>
                {
                    string s = text.Length >= 7 ? text.Substring(0, 1) : "0";
                    string body = text.Length >= 7 ? text.Substring(1, 6) : text.Substring(0, 6);
                    string check = text.Length == 8 ? text.Substring(7, 1) : UpcECheckDigit(s + body).ToString();
                    return s + body + check;
                },
                NormalizeRead = text => UpcAToUpcE(text) ?? text,
            },
            new BarcodeFormat
            {
                Type = BarcodeType.Isbn, Id = "isbn", Label = "ISBN", Group = "Product barcodes", ReadFormat = ZXing.BarcodeFormat.EAN_13, Linear = true,
                Hint = "An ISBN-10 or ISBN-13; hyphens are fine. Leave off the check digit and it's added; type it and it's checked.",
                Example = "978-0-306-40615-7",
                Prepare = text => StripSeparators(text).ToUpperInvariant(),
                Check = text =>
                {
                    if (text.Length == 0) return "Enter a 10- or 13-digit ISBN.";
                    if (!Regex.IsMatch(text, @"^[0-9X]+\z")) return "ISBN can only hold digits (and X for ISBN-10 check digit).";
                    if (text.Length == 9 || text.Length == 10)
                    {
                        if (!Regex.IsMatch(text, @"^[0-9]{9}[0-9X]?\z")) return "ISBN-10 can only have an X as its final check character.";
                        if (text.Length == 10)
                        {
                            string expected = Isbn10CheckDigit(text.Substring(0, 9));
                            if (text.Substring(9, 1) != expected) return "The check digit (the last character) should be " + expected + ". Leave it off and it's added for you.";
                        }
                        return null;
                    }
                    if (text.Length == 12 || text.Length == 13)
                    {
                        if (!AllDigits(text)) return "ISBN-13 can only hold digits.";
                        if (!text.StartsWith("978", StringComparison.Ordinal) && !text.StartsWith("979", StringComparison.Ordinal)) return "ISBN-13 must start with 978 or 979.";
                        if (text.Length == 13)
                        {
                            int expected = CheckDigit(text.Substring(0, 12));
                            if (text[12] - '0' != expected) return "The check digit (the last digit) should be " + expected + ". Leave it off and it's added for you.";
                        }
                        return null;
                    }
                    return "ISBN needs 10 digits (ISBN-10) or 13 digits (ISBN-13); this has " + text.Length + ".";
                },
                ExpectedText = text =>
                {
                    if (text.Length == 9 || text.Length == 10)
                    {
                        string ean12 = "978" + text.Substring(0, 9);
                        return ean12 + CheckDigit(ean12);
                    }
                    return text.Length == 12 ? text + CheckDigit(text) : text;
                },
                // zint takes only whole ISBNs (and reads 9 digits as an old SBN)
                EncodeText = text => text.Length == 9 ? text + Isbn10CheckDigit(text) : text.Length == 12 ? text + CheckDigit(text) : text,
            },
            new BarcodeFormat
            {
                Type = BarcodeType.Code128, Id = "code128", Label = "Code 128", Group = "Shipping and inventory", ReadFormat = ZXing.BarcodeFormat.CODE_128, Linear = true,
                Hint = "Letters, digits and symbols. For IDs, tickets, asset tags and inventory.", Example = "B0R3D-128",
                Prepare = Same,
                Check = text =>
                {
                    if (text.Length == 0) return "Enter the text for the barcode.";
                    foreach (char c in text)
                        if (c > 0xFF) return "Code 128 can only hold Latin letters, digits and symbols. Use Data Matrix or QR for other text.";
                    return null;
                },
                ExpectedText = Same,
            },
            new BarcodeFormat
            {
                Type = BarcodeType.Gs1_128, Id = "gs1-128", Label = "GS1-128", Group = "Shipping and inventory", ReadFormat = ZXing.BarcodeFormat.CODE_128, Linear = true, Gs1 = true,
                Hint = "GS1 data as (AI)value pairs, e.g. shipping container code (00), product (01), batch (10).", Example = "(01)09506000134352(10)ABC123",
                Prepare = StripSpaces, Check = CheckGs1, ExpectedText = Same,
            },
            Itf14(),
            new BarcodeFormat
            {
                Type = BarcodeType.Code39, Id = "code39", Label = "Code 39", Group = "Shipping and inventory", ReadFormat = ZXing.BarcodeFormat.CODE_39, Linear = true,
                Hint = "Capital letters, digits, spaces and - . $ / + %. Common on ID badges and older systems.", Example = "B0R3D-39",
                Prepare = JsUpper,
                Check = text =>
                {
                    if (text.Length == 0) return "Enter the text for the barcode.";
                    if (!Regex.IsMatch(text, @"^[0-9A-Z .$/+%-]+\z")) return "Code 39 can only hold capital letters, digits, spaces and - . $ / + %.";
                    return null;
                },
                ExpectedText = Same,
            },
        };

        static BarcodeFormat UpcA()
        {
            BarcodeFormat f = NumberFormat(BarcodeType.UpcA, "upca", "UPC-A", "Product barcodes", "03600029145", ZXing.BarcodeFormat.UPC_A, 11);
            // scanners may give a UPC-A in its 13-digit EAN form, with a leading 0
            f.NormalizeRead = text => text.Length == 13 && text[0] == '0' ? text.Substring(1) : text;
            return f;
        }

        static BarcodeFormat Itf14()
        {
            BarcodeFormat f = NumberFormat(BarcodeType.Itf14, "itf14", "ITF-14", "Shipping and inventory", "1950600013435", ZXing.BarcodeFormat.ITF, 13);
            f.Hint = "13 digits; the check digit is added for you. With it, 14. Used on outer cartons.";
            f.KeepsQuietZones = true;   // its quiet zones sit inside the bearer box; without them it doesn't scan
            return f;
        }

        // the type a scanned code can be made again as, from the format the reader found and its text; null for a QR code
        // The types a scanned barcode could be made again as, the likeliest first, from what ZXing.Net
        // called it and read (`gs1` when its reader said the data is GS1's)
        public static IEnumerable<BarcodeType> ForScanned(ZXing.BarcodeFormat format, string text, bool gs1)
        {
            switch (format)
            {
                case ZXing.BarcodeFormat.DATA_MATRIX: yield return gs1 ? BarcodeType.Gs1DataMatrix : BarcodeType.DataMatrix; break;
                case ZXing.BarcodeFormat.AZTEC: yield return BarcodeType.Aztec; break;
                case ZXing.BarcodeFormat.PDF_417: yield return BarcodeType.Pdf417; break;
                case ZXing.BarcodeFormat.EAN_13:
                    // a book's number as its ISBN, else as the EAN-13 it also is
                    if (Regex.IsMatch(text, @"^97[89][0-9]{10}\z")) yield return BarcodeType.Isbn;
                    yield return BarcodeType.Ean13;
                    break;
                case ZXing.BarcodeFormat.EAN_8: yield return BarcodeType.Ean8; break;
                case ZXing.BarcodeFormat.UPC_A: yield return BarcodeType.UpcA; break;
                case ZXing.BarcodeFormat.UPC_E: yield return BarcodeType.UpcE; break;
                case ZXing.BarcodeFormat.CODE_128: yield return gs1 ? BarcodeType.Gs1_128 : BarcodeType.Code128; break;
                case ZXing.BarcodeFormat.ITF:
                    if (Regex.IsMatch(text, @"^[0-9]{14}\z")) yield return BarcodeType.Itf14;
                    break;
                case ZXing.BarcodeFormat.CODE_39: yield return BarcodeType.Code39; break;
            }
        }
    }
}
