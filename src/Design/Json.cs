using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace B0r3dQR.Design
{
    // What was wrong with a JSON file, worded for people.
    sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    // JSON for design files, both ways. Objects come out as Dictionary<string, object> (keys in the
    // file's order), arrays as List<object>, and the rest as string, double, bool or null. Written
    // like the website writes its files: two spaces to a level. (.NET's own JSON reader is part of
    // System.Web, which this program never uses.)
    static class Json
    {
        const int MaxDepth = 64;   // nested deeper than this, a file is broken or hostile

        public static object Parse(string text)
        {
            var parser = new Parser(text ?? "");
            parser.Skip();
            object value = parser.Value(0);
            parser.Skip();
            if (!parser.AtEnd) throw parser.Problem("something follows the end of the data");
            return value;
        }

        sealed class Parser
        {
            readonly string text;
            int at;

            public Parser(string text)
            {
                this.text = text;
            }

            public bool AtEnd => at >= text.Length;

            public JsonException Problem(string what)
            {
                int line = 1, column = 1;
                for (int i = 0; i < at && i < text.Length; i++)
                {
                    if (text[i] == '\n') { line++; column = 1; }
                    else column++;
                }
                return new JsonException("It isn't a design file: " + what + " (line " + line + ", column " + column + ").");
            }

            public void Skip()
            {
                while (at < text.Length && (text[at] == ' ' || text[at] == '\t' || text[at] == '\r' || text[at] == '\n' || text[at] == '﻿')) at++;
            }

            public object Value(int depth)
            {
                if (depth > MaxDepth) throw Problem("it's nested too deeply");
                if (AtEnd) throw Problem("it ends too soon");
                char c = text[at];
                switch (c)
                {
                    case '{': return Object(depth);
                    case '[': return Array(depth);
                    case '"': return String();
                    case 't': Word("true"); return true;
                    case 'f': Word("false"); return false;
                    case 'n': Word("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return Number();
                        throw Problem("unexpected '" + c + "'");
                }
            }

            Dictionary<string, object> Object(int depth)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                at++;
                Skip();
                if (!AtEnd && text[at] == '}') { at++; return result; }
                while (true)
                {
                    Skip();
                    if (AtEnd || text[at] != '"') throw Problem("a name in quotes was expected");
                    string key = String();
                    Skip();
                    if (AtEnd || text[at] != ':') throw Problem("':' was expected");
                    at++;
                    Skip();
                    result[key] = Value(depth + 1);
                    Skip();
                    if (AtEnd) throw Problem("it ends inside { }");
                    if (text[at] == ',') { at++; continue; }
                    if (text[at] == '}') { at++; return result; }
                    throw Problem("',' or '}' was expected");
                }
            }

            List<object> Array(int depth)
            {
                var result = new List<object>();
                at++;
                Skip();
                if (!AtEnd && text[at] == ']') { at++; return result; }
                while (true)
                {
                    Skip();
                    result.Add(Value(depth + 1));
                    Skip();
                    if (AtEnd) throw Problem("it ends inside [ ]");
                    if (text[at] == ',') { at++; continue; }
                    if (text[at] == ']') { at++; return result; }
                    throw Problem("',' or ']' was expected");
                }
            }

            string String()
            {
                at++;  // the opening quote
                var s = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Problem("a text in quotes doesn't end");
                    char c = text[at++];
                    if (c == '"') return s.ToString();
                    if (c < ' ') throw Problem("a text has a raw control character");
                    if (c != '\\') { s.Append(c); continue; }
                    if (AtEnd) throw Problem("a text ends in the middle of an escape");
                    char e = text[at++];
                    switch (e)
                    {
                        case '"': s.Append('"'); break;
                        case '\\': s.Append('\\'); break;
                        case '/': s.Append('/'); break;
                        case 'b': s.Append('\b'); break;
                        case 'f': s.Append('\f'); break;
                        case 'n': s.Append('\n'); break;
                        case 'r': s.Append('\r'); break;
                        case 't': s.Append('\t'); break;
                        case 'u':
                            if (at + 4 > text.Length || !int.TryParse(text.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw Problem("a \\u escape needs four hex digits");
                            s.Append((char)code);
                            at += 4;
                            break;
                        default: throw Problem("unknown escape \\" + e);
                    }
                }
            }

            double Number()
            {
                int start = at;
                if (text[at] == '-') at++;
                while (!AtEnd && (char.IsDigit(text[at]) || text[at] == '.' || text[at] == 'e' || text[at] == 'E' || text[at] == '+' || text[at] == '-')) at++;
                string n = text.Substring(start, at - start);
                if (!double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsInfinity(value))
                {
                    at = start;
                    throw Problem("'" + n + "' isn't a number");
                }
                return value;
            }

            void Word(string word)
            {
                if (string.CompareOrdinal(text, at, word, 0, word.Length) != 0) throw Problem("unexpected '" + text[at] + "'");
                at += word.Length;
            }
        }

        // ---------- writing ----------

        public static string Write(object value)
        {
            var s = new StringBuilder();
            Write(s, value, 0);
            return s.ToString();
        }

        static void Write(StringBuilder s, object value, int indent)
        {
            switch (value)
            {
                case null: s.Append("null"); break;
                case bool b: s.Append(b ? "true" : "false"); break;
                case string text: WriteString(s, text); break;
                case int i: s.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: s.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case double d: s.Append(Number(d)); break;
                case float f: s.Append(Number(f)); break;
                case IDictionary<string, object> map:
                    if (map.Count == 0) { s.Append("{}"); break; }
                    s.Append("{\n");
                    int n = 0;
                    foreach (KeyValuePair<string, object> pair in map)
                    {
                        s.Append(' ', indent + 2);
                        WriteString(s, pair.Key);
                        s.Append(": ");
                        Write(s, pair.Value, indent + 2);
                        if (++n < map.Count) s.Append(',');
                        s.Append('\n');
                    }
                    s.Append(' ', indent).Append('}');
                    break;
                case IList<object> list:
                    if (list.Count == 0) { s.Append("[]"); break; }
                    s.Append("[\n");
                    for (int k = 0; k < list.Count; k++)
                    {
                        s.Append(' ', indent + 2);
                        Write(s, list[k], indent + 2);
                        if (k + 1 < list.Count) s.Append(',');
                        s.Append('\n');
                    }
                    s.Append(' ', indent).Append(']');
                    break;
                default:
                    throw new ArgumentException("JSON can't hold a " + value.GetType().Name);
            }
        }

        // whole numbers without a point, others as short as they round-trip
        public static string Number(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        static void WriteString(StringBuilder s, string text)
        {
            s.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': s.Append("\\\""); break;
                    case '\\': s.Append("\\\\"); break;
                    case '\n': s.Append("\\n"); break;
                    case '\r': s.Append("\\r"); break;
                    case '\t': s.Append("\\t"); break;
                    case '\b': s.Append("\\b"); break;
                    case '\f': s.Append("\\f"); break;
                    default:
                        if (c < ' ' || c == (char)0x2028 || c == (char)0x2029) s.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else s.Append(c);
                        break;
                }
            }
            s.Append('"');
        }

        // ---------- reading values out of parsed objects, forgiving of missing or odd ones ----------

        public static Dictionary<string, object> Obj(object map, string key) =>
            map is Dictionary<string, object> d && d.TryGetValue(key, out object v) ? v as Dictionary<string, object> : null;

        public static string Str(object map, string key, string fallback = null) =>
            map is Dictionary<string, object> d && d.TryGetValue(key, out object v) && v is string s ? s : fallback;

        public static double? Num(object map, string key)
        {
            if (!(map is Dictionary<string, object> d) || !d.TryGetValue(key, out object v)) return null;
            if (v is double n) return n;
            if (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
                !double.IsNaN(parsed) && !double.IsInfinity(parsed)) return parsed;   // "NaN" and "Infinity" aren't numbers to keep
            return null;
        }

        public static bool? Bool(object map, string key) =>
            map is Dictionary<string, object> d && d.TryGetValue(key, out object v) && v is bool b ? b : (bool?)null;

        public static bool Has(object map, string key) => map is Dictionary<string, object> d && d.ContainsKey(key);
    }
}
