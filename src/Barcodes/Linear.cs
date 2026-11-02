using System.Text;

namespace B0r3dQR.Barcodes
{
    // The product and shipping barcodes as zint 2.16 makes them (upcean.c, 2of5inter.c,
    // 2of5inter_based.c and code.c; BSD-3-Clause, see THIRD-PARTY-NOTICES.txt): EAN-13, EAN-8, UPC-A,
    // UPC-E, ISBN, ITF-14 and Code 39, from data that has passed the website's own checks. zint's
    // refusals are kept, in its words, for what those checks don't catch.
    static class Linear
    {
        // the digits' bars and spaces (ISO/IEC 15420 number sets A and B) and the EAN/UPC parities
        static readonly string[] SetA = { "3211", "2221", "2122", "1411", "1132", "1231", "1114", "1312", "1213", "3112" };
        static readonly string[] SetB = { "1123", "1222", "2212", "1141", "2311", "1321", "4111", "2131", "3121", "2113" };
        static readonly string[] Ean13Parity = { "AAAAA", "ABABB", "ABBAB", "ABBBA", "BAABB", "BBAAB", "BBBAA", "BABAB", "BABBA", "BBABA" };
        static readonly string[] UpcEParity0 = { "BBBAAA", "BBABAA", "BBAABA", "BBAAAB", "BABBAA", "BAABBA", "BAAABB", "BABABA", "BABAAB", "BAABAB" };
        static readonly string[] UpcEParity1 = { "AAABBB", "AABABB", "AABBAB", "AABBBA", "ABAABB", "ABBAAB", "ABBBAA", "ABABAB", "ABABBA", "ABBABA" };

        // EAN-13 from 12 digits (its check digit added) or 13 (the check digit checked); ISBNs end up here too
        public static ZintSymbol Ean13(string digits, Symbology symbology, out string error)
        {
            string text = digits.Length == 12 ? digits + ZintSymbol.CheckDigit(digits) : digits;
            char check = ZintSymbol.CheckDigit(text.Substring(0, 12));
            if (text[12] != check) return Refuse(out error, "Invalid check digit '" + text[12] + "', expecting '" + check + "'");
            string parity = Ean13Parity[text[0] - '0'];
            var widths = new StringBuilder("111");
            for (int i = 1; i < 13; i++)
            {
                if (i == 7) widths.Append("11111");
                widths.Append(i > 1 && i < 7 && parity[i - 2] == 'B' ? SetB[text[i] - '0'] : SetA[text[i] - '0']);
            }
            widths.Append("111");
            error = null;
            return Made(symbology, widths.ToString(), text);
        }

        // EAN-8 from 7 digits or 8
        public static ZintSymbol Ean8(string digits, out string error)
        {
            string text = digits.Length == 7 ? digits + ZintSymbol.CheckDigit(digits) : digits;
            char check = ZintSymbol.CheckDigit(text.Substring(0, 7));
            if (text[7] != check) return Refuse(out error, "Invalid EAN-8 check digit '" + text[7] + "', expecting '" + check + "'");
            error = null;
            return Made(Symbology.Ean8, UpcAWidths(text), text);
        }

        // UPC-A from 11 digits or 12
        public static ZintSymbol UpcA(string digits, out string error)
        {
            string text = digits.Length == 11 ? digits + ZintSymbol.CheckDigit(digits) : digits;
            char check = ZintSymbol.CheckDigit(text.Substring(0, 11));
            if (text[11] != check) return Refuse(out error, "Invalid check digit '" + text[11] + "', expecting '" + check + "'");
            error = null;
            return Made(Symbology.UpcA, UpcAWidths(text), text);
        }

        // UPC-A and EAN-8: guards, the left half, the middle guard, the right half
        static string UpcAWidths(string text)
        {
            var widths = new StringBuilder("111");
            for (int i = 0; i < text.Length; i++)
            {
                if (i == text.Length / 2) widths.Append("11111");
                widths.Append(SetA[text[i] - '0']);
            }
            return widths.Append("111").ToString();
        }

        // UPC-E from 6 digits (number system 0), 7 (number system first) or 8 (and check digit last)
        public static ZintSymbol UpcE(string digits, out string error)
        {
            char given = '\0';
            if (digits.Length == 8)
            {
                given = digits[7];
                digits = digits.Substring(0, 7);
            }
            int system = digits.Length == 7 && digits[0] == '1' ? 1 : 0;
            string hrt = digits.Length == 7 ? (digits[0] == '0' || digits[0] == '1' ? digits : "0" + digits.Substring(1)) : "0" + digits;
            string s = hrt.Substring(1, 6);

            // the UPC-A it stands for, which gives the check digit
            char[] full = "00000000000".ToCharArray();
            if (system == 1) full[0] = hrt[0];
            full[1] = s[0];
            full[2] = s[1];
            switch (s[5])
            {
                case '0':
                case '1':
                case '2':
                    full[3] = s[5];
                    full[8] = s[2];
                    full[9] = s[3];
                    full[10] = s[4];
                    break;
                case '3':
                    full[3] = s[2];
                    full[9] = s[3];
                    full[10] = s[4];
                    if (s[2] <= '2') return Refuse(out error, "For this UPC-E zero suppression, 3rd character cannot be \"0\", \"1\" or \"2\" (" + s + ")");
                    break;
                case '4':
                    full[3] = s[2];
                    full[4] = s[3];
                    full[10] = s[4];
                    if (s[3] == '0') return Refuse(out error, "For this UPC-E zero suppression, 4th character cannot be \"0\" (" + s + ")");
                    break;
                default:
                    full[3] = s[2];
                    full[4] = s[3];
                    full[5] = s[4];
                    full[10] = s[5];
                    if (s[4] == '0') return Refuse(out error, "For this UPC-E zero suppression, 5th character cannot be \"0\" (" + s + ")");
                    break;
            }
            char check = ZintSymbol.CheckDigit(new string(full));
            if (given != '\0' && given != check) return Refuse(out error, "Invalid check digit '" + given + "', expecting '" + check + "'");
            string parity = (system == 1 ? UpcEParity1 : UpcEParity0)[check - '0'];
            var widths = new StringBuilder("111");
            for (int i = 0; i < 6; i++) widths.Append(parity[i] == 'A' ? SetA[s[i] - '0'] : SetB[s[i] - '0']);
            widths.Append("111111");
            error = null;
            return Made(Symbology.UpcE, widths.ToString(), hrt + check);
        }

        // An ISBN-10 or ISBN-13 (whole, with its check digit) as the EAN-13 for it
        public static ZintSymbol Isbn(string isbn, out string error)
        {
            if (isbn.Length != 9 && isbn.Length != 10 && isbn.Length != 13)
                return Refuse(out error, "Input length " + isbn.Length + " wrong (9, 10, or 13 characters required)");
            if (isbn.Length == 13)
            {
                if (!isbn.StartsWith("978", System.StringComparison.Ordinal) && !isbn.StartsWith("979", System.StringComparison.Ordinal))
                    return Refuse(out error, "Invalid ISBN (must begin with \"978\" or \"979\")");
                if (isbn.IndexOf('X') >= 0) return Refuse(out error, "Invalid character in input, \"X\" not allowed in ISBN-13");
                char check = ZintSymbol.CheckDigit(isbn.Substring(0, 12));
                if (isbn[12] != check) return Refuse(out error, "Invalid ISBN check digit '" + isbn[12] + "', expecting '" + check + "'");
                return Ean13(isbn.Substring(0, 12), Symbology.Isbn, out error);
            }
            string ten = isbn.Length == 9 ? "0" + isbn : isbn;
            if (ten.Substring(0, 9).IndexOf('X') >= 0) return Refuse(out error, "Invalid character in input, \"X\" allowed in last position only");
            int sum = 0;
            for (int i = 0; i < 9; i++) sum += (ten[i] - '0') * (i + 1);
            char isbnCheck = sum % 11 == 10 ? 'X' : (char)('0' + sum % 11);
            if (ten[9] != isbnCheck)
                return Refuse(out error, "Invalid " + (isbn.Length == 9 ? "SBN" : "ISBN") + " check digit '" + ten[9] + "', expecting '" + isbnCheck + "'");
            return Ean13("978" + ten.Substring(0, 9), Symbology.Isbn, out error);
        }

        // ITF-14 from 13 digits or 14: interleaved 2 of 5 (wide bars three times the narrow) in a box
        public static ZintSymbol Itf14(string digits, out string error)
        {
            char given = '\0';
            if (digits.Length == 14)
            {
                given = digits[13];
                digits = digits.Substring(0, 13);
            }
            string text = new string('0', 13 - digits.Length) + digits;
            char check = ZintSymbol.CheckDigit(text);
            if (given != '\0' && given != check) return Refuse(out error, "Invalid check digit '" + given + "', expecting '" + check + "'");
            text += check;
            var widths = new StringBuilder("1111");
            for (int i = 0; i < text.Length; i += 2)
            {
                string bars = Interleaved[text[i] - '0'], spaces = Interleaved[text[i + 1] - '0'];
                for (int j = 0; j < 5; j++) widths.Append(bars[j]).Append(spaces[j]);
            }
            widths.Append("311");
            ZintSymbol symbol = Made(Symbology.Itf14, widths.ToString(), text);
            symbol.Box = true;
            symbol.BorderWidth = 5;
            error = null;
            return symbol;
        }

        static readonly string[] Interleaved = { "11331", "31113", "13113", "33111", "11313", "31311", "13311", "11133", "31131", "13131" };

        // Code 39 from capitals, digits, space and - . $ / + %, between its start and stop stars (wide bars twice the narrow)
        public static ZintSymbol Code39(string text, out string error)
        {
            if (text.Length > 86) return Refuse(out error, "Input length " + text.Length + " too long (maximum 86)");
            var widths = new StringBuilder(Code39Table[43]);
            for (int i = 0; i < text.Length; i++)
            {
                int at = Code39Characters.IndexOf(text[i]);
                if (at < 0) return Refuse(out error, "Invalid character at position " + (i + 1) + " in input (alphanumerics, space and \"-.$/+%\" only)");
                widths.Append(Code39Table[at]);
            }
            widths.Append(Code39Table[43].Substring(0, 9));
            error = null;
            return Made(Symbology.Code39, widths.ToString(), "*" + text + "*");
        }

        const string Code39Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

        // ISO/IEC 16388 Table 1, then the start/stop character
        static readonly string[] Code39Table =
        {
            "1112212111", "2112111121", "1122111121", "2122111111", "1112211121", "2112211111", "1122211111", "1112112121",
            "2112112111", "1122112111", "2111121121", "1121121121", "2121121111", "1111221121", "2111221111", "1121221111",
            "1111122121", "2111122111", "1121122111", "1111222111", "2111111221", "1121111221", "2121111211", "1111211221",
            "2111211211", "1121211211", "1111112221", "2111112211", "1121112211", "1111212211", "2211111121", "1221111121",
            "2221111111", "1211211121", "2211211111", "1221211111", "1211112121", "2211112111", "1221112111", "1212121111",
            "1212111211", "1211121211", "1112121211", "1211212111",
        };

        // one row of bars from its widths, 50 modules high
        static ZintSymbol Made(Symbology symbology, string widths, string text)
        {
            var symbol = new ZintSymbol(symbology) { Text = text };
            symbol.Expand(widths);
            symbol.SetHeight(0, 50);
            return symbol;
        }

        static ZintSymbol Refuse(out string error, string message)
        {
            error = message;
            return null;
        }
    }
}
