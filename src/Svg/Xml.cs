using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace B0r3dQR.Svg
{
    // What's wrong with an SVG file, worded for people.
    sealed class SvgException : Exception
    {
        public SvgException(string message) : base(message) { }
    }

    // An attribute: its local name, its namespace ("" for none) and its value.
    struct Attr
    {
        public string Name, Namespace, Value;
    }

    // An element of an XML file: its local name and namespace, its attributes, and what's inside it
    // (elements and text, in order).
    sealed class Element
    {
        public string Name = "", Namespace = "", Prefix = "";
        public Element Parent;
        public int Index;                                       // among its parent's elements
        public readonly List<Attr> Attributes = new List<Attr>();
        public readonly List<object> Content = new List<object>();   // Element and string
        public readonly List<Element> Children = new List<Element>();
        internal Dictionary<string, string> Scope;              // the namespace prefixes in force inside it
        // How much there is to read in its attributes (their names and values, but for href's values: the
        // pictures and elements they name are read once), which drawing reads again for each copy
        internal int Size;
        string[] classNames;

        // the names in its class attribute, once each (style sheets look them up again and again)
        internal string[] ClassNames => classNames ?? (classNames = this["class"]?.Split(new[] { ' ', '\t', '\n', '\r', '\f' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray() ?? new string[0]);

        // an attribute's value, or null when it isn't there
        public string this[string name, string space = ""]
        {
            get
            {
                foreach (Attr a in Attributes)
                    if (a.Name == name && a.Namespace == space) return a.Value;
                return null;
            }
        }

        public bool Is(string name) => Name == name && Namespace == Xml.SvgSpace;
    }

    // A small XML reader of b0r3d QR's own (.NET's can fetch files named in a document, which this
    // program never does). It reads what browsers read in an SVG file: elements, attributes,
    // namespaces, text, CDATA sections, the five standard entities, numbered characters, and
    // entities declared inside the file's DOCTYPE (old Illustrator files name their namespaces
    // that way); comments and processing instructions are skipped. A file a browser would refuse
    // (mismatched tags, an unknown entity, two attributes of the same name) is refused here too,
    // and hostile ones (entities that grow without end, nesting a mile deep) are stopped early.
    static class Xml
    {
        public const string SvgSpace = "http://www.w3.org/2000/svg";
        public const string XLinkSpace = "http://www.w3.org/1999/xlink";
        // the xml: prefix's namespace, which XML fixes; this is only ever compared, so a short name does
        public const string XmlSpace = "xml:";

        public const int MaxDepth = 256;            // libxml2's own limit, which Chromium keeps
        public const int MaxElements = 200000;
        const int MaxEntities = 1000;
        const int MaxEntityDepth = 8;

        // The text of an XML file's bytes, as a browser decodes it: a byte order mark first, then
        // the charset a data: URL names, then the file's own encoding="", else UTF-8.
        public static string Decode(byte[] bytes, string charset)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Strict(new UTF8Encoding(false, true), bytes, 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Strict(new UnicodeEncoding(false, false, true), bytes, 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Strict(new UnicodeEncoding(true, false, true), bytes, 2);
            string label = (charset ?? Declared(bytes) ?? "utf-8").Trim().ToLowerInvariant();
            switch (label)
            {
                case "utf-8":
                case "utf8":
                case "unicode-1-1-utf-8":
                    return Strict(new UTF8Encoding(false, true), bytes, 0);
                case "utf-16":
                case "utf-16le":
                    return Strict(new UnicodeEncoding(false, false, true), bytes, 0);
                case "utf-16be":
                    return Strict(new UnicodeEncoding(true, false, true), bytes, 0);
                case "iso-8859-1":
                case "iso8859-1":
                case "latin1":
                case "l1":
                case "us-ascii":
                case "ascii":
                case "windows-1252":
                case "cp1252":
                    return Encoding.GetEncoding(1252).GetString(bytes);   // browsers read all of these as Windows-1252
            }
            try
            {
                return Encoding.GetEncoding(label).GetString(bytes);
            }
            catch (ArgumentException)
            {
                return Strict(new UTF8Encoding(false, true), bytes, 0);
            }
        }

        static string Strict(Encoding encoding, byte[] bytes, int start)
        {
            try
            {
                return encoding.GetString(bytes, start, bytes.Length - start);
            }
            catch (DecoderFallbackException)
            {
                throw new SvgException("its text isn't in the characters it says it's in (UTF-8 unless it says otherwise)");
            }
        }

        // encoding="..." in an XML declaration at the very start, read as plain ASCII
        static string Declared(byte[] bytes)
        {
            string start = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 200));
            if (!start.StartsWith("<?xml", StringComparison.Ordinal)) return null;
            int end = start.IndexOf("?>", StringComparison.Ordinal);
            if (end < 0) return null;
            var m = System.Text.RegularExpressions.Regex.Match(start.Substring(0, end), "encoding\\s*=\\s*[\"']([A-Za-z0-9._:-]+)[\"']");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static Element Parse(string text) => new Parser(text).Document();

        // A name or value from the file as a refusal quotes it: by the one rule for outside text that's shown (no
        // letter with more marks than a caption keeps: about 700 end the program where the message is shown), then
        // cut to its first 37 characters and "..." (never half of a surrogate pair), its line breaks, tabs and other
        // control characters as spaces, so the message stays a line. WPF breaks a line at CR, LF, U+000B, U+000C,
        // U+0085 (NEL), U+2028 and U+2029 and no other character (each one tried, between two letters, in a TextBlock
        // and in FormattedText); an entity's name, a numbered character's digits and the character where a name was
        // expected can hold any of them. Only its start can show, and a letter keeps its first 100 marks, so capping its
        // first 4,096 characters gives what capping all of it would: a name, or a design's value, of millions of
        // characters costs no more than a short one.
        internal static string Quote(string text)
        {
            int start = Math.Min(text.Length, 4096);
            if (start < text.Length && char.IsHighSurrogate(text[start - 1])) start--;
            string s = Qr.Unicode.Capped(start < text.Length ? text.Substring(0, start) : text, Qr.CaptionFont.MostMarks);
            if (s.Length > 40) s = s.Substring(0, char.IsHighSurrogate(s[36]) ? 36 : 37) + "...";
            var quoted = new StringBuilder(s.Length);
            foreach (char c in s) quoted.Append(c < 0x20 || c >= 0x7F && c <= 0x9F || c == 0x2028 || c == 0x2029 ? ' ' : c);
            return quoted.ToString();
        }

        sealed class Parser
        {
            readonly string s;
            int at;
            int elements;
            long grown;                         // characters added by expanding entities
            readonly long mostGrowth;
            readonly Dictionary<string, string> entities = new Dictionary<string, string>();
            readonly StringBuilder text = new StringBuilder();

            public Parser(string source)
            {
                // XML reads every line ending as a line feed
                s = source.IndexOf('\r') < 0 ? source : source.Replace("\r\n", "\n").Replace('\r', '\n');
                mostGrowth = Math.Max(1 << 20, 2L * s.Length);
            }

            SvgException Problem(string what)
            {
                int line = 1;
                for (int i = 0; i < at && i < s.Length; i++)
                    if (s[i] == '\n') line++;
                return new SvgException(what + " (line " + line + ")");
            }

            bool Starts(string word) => string.CompareOrdinal(s, at, word, 0, word.Length) == 0;

            static bool Space(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r';

            void SkipSpaces()
            {
                while (at < s.Length && Space(s[at])) at++;
            }

            static bool NameStart(char c) => char.IsLetter(c) || c == '_' || c == ':' || c >= 0xC0 && !char.IsWhiteSpace(c);

            static bool NameChar(char c) => NameStart(c) || char.IsDigit(c) || c == '.' || c == '-' || c == 0xB7 || c >= 0x80 && !char.IsWhiteSpace(c) && c != 0xFEFF;

            string Name()
            {
                int start = at;
                if (at >= s.Length || !NameStart(s[at])) throw Problem(at >= s.Length ? "it ends too soon" : "a name was expected but found '" + Quote(s.Substring(at, char.IsSurrogatePair(s, at) ? 2 : 1)) + "'");
                at++;
                while (at < s.Length && NameChar(s[at])) at++;
                return s.Substring(start, at - start);
            }

            void Expect(string word, string what)
            {
                if (!Starts(word)) throw Problem(at >= s.Length ? "it ends too soon" : what);
                at += word.Length;
            }

            public Element Document()
            {
                if (s.Length == 0) throw new SvgException("the file is empty");
                if (Starts("<?xml") && at + 5 < s.Length && Space(s[at + 5])) SkipUntil("?>");
                Misc();
                if (Starts("<!DOCTYPE"))
                {
                    Doctype();
                    Misc();
                }
                if (at >= s.Length) throw Problem("there's no element in it");
                Element root = Elements();
                Misc();
                if (at < s.Length) throw Problem("something follows the end of the drawing");
                return root;
            }

            void SkipUntil(string end)
            {
                int found = s.IndexOf(end, at, StringComparison.Ordinal);
                if (found < 0) throw Problem("it ends too soon");
                at = found + end.Length;
            }

            // white space, comments and processing instructions, outside the elements
            void Misc()
            {
                while (true)
                {
                    SkipSpaces();
                    if (Starts("<!--")) SkipUntil("-->");
                    else if (Starts("<?xml") && at + 5 < s.Length && Space(s[at + 5])) throw Problem("the XML declaration has to come first");
                    else if (Starts("<?")) SkipUntil("?>");
                    else if (at < s.Length && s[at] != '<') throw Problem("there's text outside the drawing");
                    else return;
                }
            }

            // <!DOCTYPE name [external id] [internal subset]>: only the entities declared inside it count
            void Doctype()
            {
                at += "<!DOCTYPE".Length;
                SkipSpaces();
                Name();
                while (true)
                {
                    SkipSpaces();
                    if (at >= s.Length) throw Problem("it ends too soon");
                    char c = s[at];
                    if (c == '>')
                    {
                        at++;
                        return;
                    }
                    if (c == '"' || c == '\'') Quoted();
                    else if (c == '[') InternalSubset();
                    else Name();   // SYSTEM, PUBLIC
                }
            }

            string Quoted()
            {
                char quote = s[at];
                int end = s.IndexOf(quote, at + 1);
                if (end < 0) throw Problem("it ends too soon");
                string value = s.Substring(at + 1, end - at - 1);
                at = end + 1;
                return value;
            }

            void InternalSubset()
            {
                at++;
                while (true)
                {
                    SkipSpaces();
                    if (at >= s.Length) throw Problem("it ends too soon");
                    if (s[at] == ']')
                    {
                        at++;
                        return;
                    }
                    if (Starts("<!--")) SkipUntil("-->");
                    else if (Starts("<?")) SkipUntil("?>");
                    else if (Starts("<!ENTITY")) Entity();
                    else if (Starts("<!")) Declaration();
                    else if (s[at] == '%')
                    {
                        at++;
                        Name();
                        Expect(";", "a parameter entity isn't closed with ;");
                    }
                    else throw Problem("the DOCTYPE has something it shouldn't");
                }
            }

            // <!ELEMENT ...>, <!ATTLIST ...>, <!NOTATION ...>: skipped, quotes and all
            void Declaration()
            {
                at += 2;
                ToEnd();
            }

            // the rest of a declaration, up to and past its >
            void ToEnd()
            {
                while (at < s.Length && s[at] != '>')
                {
                    if (s[at] == '"' || s[at] == '\'') Quoted();
                    else at++;
                }
                Expect(">", "it ends too soon");
            }

            void Entity()
            {
                at += "<!ENTITY".Length;
                SkipSpaces();
                bool parameter = false;
                if (at < s.Length && s[at] == '%')
                {
                    parameter = true;
                    at++;
                    SkipSpaces();
                }
                string name = Name();
                SkipSpaces();
                string value = null;
                if (at < s.Length && (s[at] == '"' || s[at] == '\''))
                {
                    // numbered characters are put in now; other entities when the entity is used
                    string raw = Quoted();
                    var v = new StringBuilder(raw.Length);
                    for (int i = 0; i < raw.Length; i++)
                    {
                        if (raw[i] == '&' && i + 1 < raw.Length && raw[i + 1] == '#')
                        {
                            int end = raw.IndexOf(';', i);
                            if (end < 0) throw Problem("a numbered character isn't closed with ;");
                            v.Append(CharacterNumber(raw.Substring(i + 2, end - i - 2)));
                            i = end;
                        }
                        else v.Append(raw[i]);
                    }
                    value = v.ToString();
                }
                ToEnd();   // whatever's left (SYSTEM "...", NDATA ...) up to the >
                if (parameter) return;
                if (entities.Count >= MaxEntities) throw Problem("it declares too many entities");
                if (!entities.ContainsKey(name)) entities[name] = value;   // the first declaration counts; null: one from outside the file
            }

            string CharacterNumber(string digits)
            {
                bool hex = digits.StartsWith("x", StringComparison.Ordinal);
                if (!int.TryParse(hex ? digits.Substring(1) : digits, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out int code) ||
                    !(code == 9 || code == 10 || code == 13 || code >= 0x20 && code <= 0xD7FF || code >= 0xE000 && code <= 0xFFFD || code >= 0x10000 && code <= 0x10FFFF))
                    throw Problem("&#" + Quote(digits) + "; isn't a character XML allows");
                return char.ConvertFromUtf32(code);
            }

            // &...; at `at`, added to `into` (in an attribute, white space in an entity becomes spaces)
            void Reference(StringBuilder into, bool attribute, int depth = 0)
            {
                int end = s.IndexOf(';', at);
                if (end < 0 || end - at > 64) throw Problem("an & isn't the start of an entity (write &amp; for an &)");
                string name = s.Substring(at + 1, end - at - 1);
                at = end + 1;
                Expand(name, into, attribute, depth);
            }

            void Expand(string name, StringBuilder into, bool attribute, int depth)
            {
                if (name.StartsWith("#", StringComparison.Ordinal))
                {
                    into.Append(CharacterNumber(name.Substring(1)));
                    return;
                }
                switch (name)
                {
                    case "lt": into.Append('<'); return;
                    case "gt": into.Append('>'); return;
                    case "amp": into.Append('&'); return;
                    case "quot": into.Append('"'); return;
                    case "apos": into.Append('\''); return;
                }
                if (!entities.TryGetValue(name, out string value)) throw Problem("the entity &" + Quote(name) + "; isn't defined");
                if (value == null) throw Problem("the entity &" + Quote(name) + "; is in another file, which b0r3d QR doesn't read");
                if (depth >= MaxEntityDepth) throw Problem("its entities are nested too deeply");
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c == '<') throw Problem("the entity &" + Quote(name) + "; holds markup, which b0r3d QR doesn't read");
                    if (c == '&')
                    {
                        int end = value.IndexOf(';', i);
                        if (end < 0) throw Problem("the entity &" + Quote(name) + "; has an & that isn't an entity");
                        Expand(value.Substring(i + 1, end - i - 1), into, attribute, depth + 1);
                        i = end;
                        continue;
                    }
                    into.Append(attribute && Space(c) ? ' ' : c);
                    if (++grown > mostGrowth) throw Problem("its entities grow far bigger than the file");
                }
            }

            // the root element and everything in it
            Element Elements()
            {
                var open = new List<Element>();
                Element root = null;
                var rootScope = new Dictionary<string, string> { ["xml"] = XmlSpace };
                while (true)
                {
                    if (at >= s.Length) throw Problem("it ends too soon: " + (open.Count > 0 ? "<" + Quote(Qualified(open[open.Count - 1])) + "> isn't closed" : "no element"));
                    Element parent = open.Count > 0 ? open[open.Count - 1] : null;
                    char c = s[at];
                    if (c != '<')
                    {
                        if (parent == null) throw Problem("there's text outside the drawing");
                        if (c == '&') Reference(text, false);
                        else
                        {
                            if (c < 0x20 && c != '\t' && c != '\n' || c == 0xFFFE || c == 0xFFFF) throw Problem("it has a character XML doesn't allow");
                            text.Append(c);
                            at++;
                        }
                        continue;
                    }
                    if (Starts("<!--"))
                    {
                        SkipUntil("-->");
                        continue;
                    }
                    if (Starts("<![CDATA["))
                    {
                        if (parent == null) throw Problem("there's text outside the drawing");
                        int end = s.IndexOf("]]>", at, StringComparison.Ordinal);
                        if (end < 0) throw Problem("a CDATA section isn't closed");
                        text.Append(s, at + 9, end - at - 9);
                        at = end + 3;
                        continue;
                    }
                    if (Starts("<?"))
                    {
                        SkipUntil("?>");
                        continue;
                    }
                    if (Starts("<!")) throw Problem("it has a <! that isn't a comment or CDATA");
                    Flush(parent);
                    if (Starts("</"))
                    {
                        at += 2;
                        string name = Name();
                        SkipSpaces();
                        Expect(">", "an end tag isn't closed with >");
                        if (parent == null || name != Qualified(parent)) throw Problem("</" + Quote(name) + "> doesn't match " + (parent == null ? "anything" : "<" + Quote(Qualified(parent)) + ">"));
                        open.RemoveAt(open.Count - 1);
                        if (open.Count == 0) return root;
                        continue;
                    }
                    at++;
                    Element e = StartTag(parent, parent?.Scope ?? rootScope, out bool empty);
                    if (++elements > MaxElements) throw Problem("it has more than " + MaxElements + " elements");
                    if (parent == null)
                    {
                        if (root != null) throw Problem("it has a second drawing after the first");
                        root = e;
                    }
                    else
                    {
                        e.Index = parent.Children.Count;
                        parent.Children.Add(e);
                        parent.Content.Add(e);
                    }
                    if (empty)
                    {
                        if (open.Count == 0) return root;
                        continue;
                    }
                    if (open.Count >= MaxDepth) throw Problem("its elements are nested more than " + MaxDepth + " deep");
                    open.Add(e);
                }
            }

            void Flush(Element parent)
            {
                if (text.Length == 0) return;
                parent?.Content.Add(text.ToString());
                text.Clear();
            }

            static string Qualified(Element e) => e.Prefix.Length > 0 ? e.Prefix + ":" + e.Name : e.Name;

            Element StartTag(Element parent, Dictionary<string, string> scope, out bool empty)
            {
                var e = new Element { Parent = parent };
                string qualified = Name();
                var raw = new List<KeyValuePair<string, string>>();
                var names = new HashSet<string>();
                while (true)
                {
                    bool spaced = at < s.Length && Space(s[at]);
                    SkipSpaces();
                    if (Starts("/>"))
                    {
                        at += 2;
                        empty = true;
                        break;
                    }
                    if (Starts(">"))
                    {
                        at++;
                        empty = false;
                        break;
                    }
                    if (!spaced) throw Problem(at >= s.Length ? "it ends too soon" : "attributes need a space between them");
                    string name = Name();
                    SkipSpaces();
                    Expect("=", "the attribute " + Quote(name) + " has no =");
                    SkipSpaces();
                    if (at >= s.Length || s[at] != '"' && s[at] != '\'') throw Problem("the attribute " + Quote(name) + "'s value isn't in quotes");
                    string value = AttributeValue();
                    if (!names.Add(name)) throw Problem("the attribute " + Quote(name) + " is there twice");
                    raw.Add(new KeyValuePair<string, string>(name, value));
                }
                // namespaces declared on this element hold for it and everything inside it
                bool copied = false;
                foreach (var a in raw)
                {
                    if (a.Key == "xmlns" || a.Key.StartsWith("xmlns:", StringComparison.Ordinal))
                    {
                        if (!copied) scope = new Dictionary<string, string>(scope);
                        copied = true;
                        scope[a.Key == "xmlns" ? "" : a.Key.Substring(6)] = a.Value;
                    }
                }
                e.Scope = scope;
                int colon = qualified.IndexOf(':');
                e.Prefix = colon > 0 ? qualified.Substring(0, colon) : "";
                e.Name = colon > 0 ? qualified.Substring(colon + 1) : qualified;
                e.Namespace = scope.TryGetValue(e.Prefix, out string space) ? space : (e.Prefix.Length > 0 ? "?" + e.Prefix : "");
                foreach (var a in raw)
                {
                    if (a.Key == "xmlns" || a.Key.StartsWith("xmlns:", StringComparison.Ordinal)) continue;
                    int c = a.Key.IndexOf(':');
                    string prefix = c > 0 ? a.Key.Substring(0, c) : "";
                    string ns = prefix.Length == 0 ? "" : scope.TryGetValue(prefix, out string found) ? found : "?" + prefix;
                    var attr = new Attr { Name = c > 0 ? a.Key.Substring(c + 1) : a.Key, Namespace = ns, Value = a.Value };
                    e.Attributes.Add(attr);
                    e.Size += a.Key.Length + (attr.Name == "href" ? 0 : attr.Value.Length);
                }
                return e;
            }

            // an attribute's value in its quotes: entities expanded, white space as spaces
            string AttributeValue()
            {
                char quote = s[at++];
                var v = new StringBuilder();
                while (true)
                {
                    if (at >= s.Length) throw Problem("an attribute's value isn't closed");
                    char c = s[at];
                    if (c == quote)
                    {
                        at++;
                        return v.ToString();
                    }
                    if (c == '<') throw Problem("an attribute's value has a < in it (write &lt;)");
                    if (c == '&')
                    {
                        Reference(v, true);
                        continue;
                    }
                    if (c < 0x20 && c != '\t' && c != '\n' || c == 0xFFFE || c == 0xFFFF) throw Problem("it has a character XML doesn't allow");
                    v.Append(Space(c) ? ' ' : c);
                    at++;
                }
            }
        }
    }
}
