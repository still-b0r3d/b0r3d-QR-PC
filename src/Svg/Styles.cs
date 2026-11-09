using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace B0r3dQR.Svg
{
    // One CSS declaration: a property, its value, and whether it's !important.
    struct Declaration
    {
        public string Property, Value;
        public bool Important;
    }

    // The CSS in an SVG file's <style> elements, as browsers apply it to the drawing: rules with
    // type, class, id, attribute and * selectors, descendant, child and sibling combinators, a few
    // pseudo-classes (:root, :first-child, :last-child, :only-child, :nth-child(), :not()), comma
    // lists, specificity and source order, !important, and @media for all or screen. A rule a browser
    // would drop (a selector it can't read) is dropped here too.
    sealed class StyleSheet
    {
        sealed class Compound
        {
            public string Type;                  // null: any
            public string Id;
            public readonly List<string> Classes = new List<string>();
            public readonly List<string[]> Attributes = new List<string[]>();   // name, operator ("" when only present), value
            public readonly List<Func<Element, bool>> Tests = new List<Func<Element, bool>>();
            public bool Never;                   // :hover and the like, ::before: never in a still picture
            public char Combinator;              // how the compound to its left relates: ' ', '>', '+', '~' ('\0' at the far left)
        }

        sealed class Rule
        {
            public List<Compound> Parts;         // right to left
            public int Specificity, Order;
            public List<Declaration> Declarations;
        }

        const int MaxRules = 20000;
        // Steps in one drawing: matching a selector (it can ask for a lot of ancestors, and each name
        // and attribute is compared), and applying each declaration to each element it reaches (copies
        // included), a long value counting for more since reading it takes as long. 20 million take
        // about a second and a half.
        const long MaxSteps = 20000000;
        const int ApplyStep = 32;         // the work applying one declaration takes, apart from reading its value
        // Selectors and @media blocks are read and matched one level inside another, each level a step
        // deeper into the program's stack, so how many levels there can be is limited: far beyond any
        // real file, and far short of what the stack holds.
        const int MaxParts = 32;          // compounds in one selector (rect.a > g ~ ...), those inside :not() too
        const int MaxNesting = 16;        // @media blocks inside one another

        readonly Dictionary<string, List<Rule>> byId = new Dictionary<string, List<Rule>>(), byClass = new Dictionary<string, List<Rule>>(), byType = new Dictionary<string, List<Rule>>();
        readonly List<Rule> universal = new List<Rule>();
        int count;
        [ThreadStatic] static long steps;

        // starts counting the steps of a new drawing on this thread
        public static void Fresh() => steps = 0;

        // the steps taken since (for the tests)
        internal static long Steps => steps;

        // `n` more steps in this drawing
        internal static void Charge(long n)
        {
            steps += n;
            if (steps > MaxSteps) throw new SvgException("its styles take too long to work out");
        }

        // the steps applying `declarations` to one element takes (charged before they're applied, so a
        // declaration with a huge value, or a great many, is stopped before the slow work, drawing after
        // drawing through <use>)
        internal static void ChargeApply(List<Declaration> declarations)
        {
            long n = 0;
            foreach (Declaration d in declarations) n += ApplyStep + d.Value.Length;
            Charge(n);
        }

        public bool IsEmpty => count == 0;

        // ---------- reading ----------

        public void Add(string css)
        {
            string text = WithoutComments(css ?? "");
            int at = 0;
            Rules(text, ref at, text.Length);
        }

        static string WithoutComments(string css)
        {
            if (css.IndexOf("/*", StringComparison.Ordinal) < 0) return css;
            var s = new StringBuilder(css.Length);
            for (int i = 0; i < css.Length; i++)
            {
                if (css[i] == '/' && i + 1 < css.Length && css[i + 1] == '*')
                {
                    int end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) break;
                    i = end + 1;
                    s.Append(' ');
                }
                else s.Append(css[i]);
            }
            return s.ToString();
        }

        void Rules(string text, ref int at, int end, int depth = 0)
        {
            while (at < end)
            {
                while (at < end && char.IsWhiteSpace(text[at])) at++;
                if (at >= end) return;
                if (text[at] == '}' || text.IndexOf("<!--", at, Math.Min(4, end - at), StringComparison.Ordinal) == at || text.IndexOf("-->", at, Math.Min(3, end - at), StringComparison.Ordinal) == at)
                {
                    at += text[at] == '}' ? 1 : text[at] == '<' ? 4 : 3;   // stray ones, and the old <!-- --> around styles
                    continue;
                }
                int open = Find(text, at, end, '{', ';');
                if (open < 0) return;
                string prelude = text.Substring(at, open - at).Trim();
                if (text[open] == ';')
                {
                    at = open + 1;   // @import, @charset, @namespace: nothing to use
                    continue;
                }
                int close = Matching(text, open, end);
                if (prelude.StartsWith("@", StringComparison.Ordinal))
                {
                    if (MediaApplies(prelude))
                    {
                        if (depth >= MaxNesting) throw new SvgException("its styles have @media blocks nested more than " + MaxNesting + " deep");
                        int inner = open + 1;
                        Rules(text, ref inner, close, depth + 1);
                    }
                }
                else AddRule(prelude, text.Substring(open + 1, close - open - 1));
                at = Math.Min(end, close + 1);
            }
        }

        // the first of `a` or `b` outside quotes and brackets, or -1
        static int Find(string text, int at, int end, char a, char b)
        {
            int depth = 0;
            for (int i = at; i < end; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                {
                    int q = text.IndexOf(c, i + 1);
                    i = q < 0 || q >= end ? end : q;
                }
                else if (c == '(' || c == '[') depth++;
                else if ((c == ')' || c == ']') && depth > 0) depth--;
                else if (depth == 0 && (c == a || c == b)) return i;
            }
            return -1;
        }

        // the } that closes the { at `open` (or the end)
        static int Matching(string text, int open, int end)
        {
            int depth = 0;
            for (int i = open; i < end; i++)
            {
                char c = text[i];
                if (c == '"' || c == '\'')
                {
                    int q = text.IndexOf(c, i + 1);
                    i = q < 0 || q >= end ? end : q;
                }
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return i;
            }
            return end;
        }

        // @media all, @media screen (with no conditions after them); a still picture has no other media
        static bool MediaApplies(string prelude)
        {
            if (!prelude.StartsWith("@media", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (string query in prelude.Substring(6).Split(','))
            {
                string q = query.Trim().ToLowerInvariant();
                if (q == "all" || q == "screen" || q == "only screen" || q == "only all") return true;
            }
            return false;
        }

        void AddRule(string selectors, string block)
        {
            var parsed = new List<Rule>();
            foreach (string one in Split(selectors, ','))
            {
                List<Compound> parts = Selector(one.Trim(), out int specificity);
                if (parts == null) return;   // one selector a browser can't read drops the whole rule
                parsed.Add(new Rule { Parts = parts, Specificity = specificity });
            }
            List<Declaration> declarations = Declarations(block);
            if (declarations.Count == 0) return;
            foreach (Rule r in parsed)
            {
                if (count >= MaxRules) throw new SvgException("its styles have more than " + MaxRules + " rules");
                r.Order = count++;
                r.Declarations = declarations;
                Compound right = r.Parts[0];
                if (right.Id != null) Bucket(byId, right.Id).Add(r);
                else if (right.Classes.Count > 0) Bucket(byClass, right.Classes[0]).Add(r);
                else if (right.Type != null) Bucket(byType, right.Type).Add(r);
                else universal.Add(r);
            }
        }

        static List<Rule> Bucket(Dictionary<string, List<Rule>> d, string key)
        {
            if (!d.TryGetValue(key, out List<Rule> list)) d[key] = list = new List<Rule>();
            return list;
        }

        // text split at `on` outside quotes and brackets
        static List<string> Split(string text, char on)
        {
            var parts = new List<string>();
            int start = 0;
            while (true)
            {
                int i = Find(text, start, text.Length, on, on);
                if (i < 0)
                {
                    parts.Add(text.Substring(start));
                    return parts;
                }
                parts.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }

        // A declaration block: "fill: red; stroke: blue !important". Properties are lower-cased; ones
        // with nothing after them, custom properties and var() are left out.
        public static List<Declaration> Declarations(string block)
        {
            var found = new List<Declaration>();
            if (string.IsNullOrWhiteSpace(block)) return found;
            foreach (string part in Split(WithoutComments(block), ';'))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;
                string name = part.Substring(0, colon).Trim().ToLowerInvariant();
                string value = part.Substring(colon + 1).Trim();
                bool important = false;
                int bang = value.LastIndexOf('!');
                if (bang >= 0 && value.Substring(bang + 1).Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
                {
                    important = true;
                    value = value.Substring(0, bang).Trim();
                }
                if (name.Length == 0 || value.Length == 0 || name.StartsWith("--", StringComparison.Ordinal) || value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                found.Add(new Declaration { Property = name, Value = value, Important = important });
            }
            return found;
        }

        // ---------- selectors ----------

        static bool IdentStart(char c) => char.IsLetter(c) || c == '_' || c == '-' || c >= 0x80 || c == '\\';
        static bool IdentChar(char c) => IdentStart(c) || char.IsDigit(c);

        static string Ident(string s, ref int at)
        {
            var id = new StringBuilder();
            while (at < s.Length && IdentChar(s[at]))
            {
                if (s[at] == '\\' && at + 1 < s.Length)
                {
                    at++;
                    int hex = 0;
                    while (hex < 6 && at + hex < s.Length && "0123456789abcdefABCDEF".IndexOf(s[at + hex]) >= 0) hex++;
                    if (hex > 0)
                    {
                        id.Append(char.ConvertFromUtf32(Math.Min(0x10FFFF, int.Parse(s.Substring(at, hex), NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
                        at += hex;
                        if (at < s.Length && s[at] == ' ') at++;
                        continue;
                    }
                }
                id.Append(s[at++]);
            }
            return id.ToString();
        }

        // a selector's compounds, right to left, or null when a browser wouldn't read it
        static List<Compound> Selector(string text, out int specificity)
        {
            specificity = 0;
            if (text.Length == 0) return null;
            var parts = new List<Compound>();
            int at = 0, ids = 0, classes = 0, types = 0, read = 0;
            char combinator = '\0';
            while (true)
            {
                Compound c = ReadCompound(text, ref at, ref ids, ref classes, ref types, ref read);
                if (c == null) return null;
                c.Combinator = combinator;
                parts.Add(c);
                int before = at;
                while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
                if (at >= text.Length) break;
                if (text[at] == '>' || text[at] == '+' || text[at] == '~')
                {
                    combinator = text[at++];
                    while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
                }
                else if (at > before) combinator = ' ';
                else return null;
            }
            // each compound keeps the combinator written before it, which is how the compound to
            // its left relates to it: read right to left, that's the step to the next one
            parts.Reverse();
            specificity = Math.Min(ids, 99) * 10000 + Math.Min(classes, 99) * 100 + Math.Min(types, 99);
            return parts;
        }

        // one compound (rect.a:first-child); `read` counts the compounds read for the selector so far
        static Compound ReadCompound(string s, ref int at, ref int ids, ref int classes, ref int types, ref int read)
        {
            if (++read > MaxParts) throw new SvgException("its styles have a selector of more than " + MaxParts + " parts");
            var c = new Compound();
            int start = at;
            if (at < s.Length && s[at] == '*') at++;
            else if (at < s.Length && IdentStart(s[at]))
            {
                c.Type = Ident(s, ref at);
                types++;
            }
            if (at < s.Length && s[at] == '|') return null;   // namespaces in selectors
            while (at < s.Length)
            {
                char ch = s[at];
                if (ch == '#')
                {
                    at++;
                    string id = Ident(s, ref at);
                    if (id.Length == 0) return null;
                    if (c.Id != null && c.Id != id) c.Never = true;
                    c.Id = id;
                    ids++;
                }
                else if (ch == '.')
                {
                    at++;
                    string cls = Ident(s, ref at);
                    if (cls.Length == 0) return null;
                    c.Classes.Add(cls);
                    classes++;
                }
                else if (ch == '[')
                {
                    int close = s.IndexOf(']', at);
                    if (close < 0) return null;
                    string inside = s.Substring(at + 1, close - at - 1).Trim();
                    at = close + 1;
                    var m = System.Text.RegularExpressions.Regex.Match(inside, @"^([A-Za-z_][\w\-:]*)\s*(?:([~|^$*]?=)\s*(?:""([^""]*)""|'([^']*)'|([^\s""']+))\s*(i|s)?)?$");
                    if (!m.Success) return null;
                    c.Attributes.Add(new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Value });
                    classes++;
                }
                else if (ch == ':')
                {
                    at++;
                    bool element = at < s.Length && s[at] == ':';
                    if (element) at++;
                    string name = Ident(s, ref at).ToLowerInvariant();
                    string argument = null;
                    if (at < s.Length && s[at] == '(')
                    {
                        int close = Closing(s, at);
                        if (close >= s.Length) return null;
                        argument = s.Substring(at + 1, close - at - 1).Trim();
                        at = close + 1;
                    }
                    if (element || name == "before" || name == "after" || name == "first-line" || name == "first-letter")
                    {
                        c.Never = true;
                        types++;
                        continue;
                    }
                    if (!Pseudo(c, name, argument, ref ids, ref classes, ref types, ref read)) return null;
                }
                else break;
            }
            return at > start ? c : null;
        }

        // the ) that closes the ( at `open` (or the end), past quotes and the brackets inside
        static int Closing(string s, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\'')
                {
                    int q = s.IndexOf(c, i + 1);
                    i = q < 0 ? s.Length : q;
                }
                else if (c == '(') depth++;
                else if (c == ')' && --depth == 0) return i;
            }
            return s.Length;
        }

        static bool Pseudo(Compound c, string name, string argument, ref int ids, ref int classes, ref int types, ref int read)
        {
            switch (argument == null ? name : name + "()")
            {
                case "root": c.Tests.Add(e => e.Parent == null); break;
                case "first-child": c.Tests.Add(e => e.Index == 0); break;
                case "last-child": c.Tests.Add(e => e.Parent == null || e.Index == e.Parent.Children.Count - 1); break;
                case "only-child": c.Tests.Add(e => e.Parent == null || e.Parent.Children.Count == 1); break;
                case "empty": c.Tests.Add(e => e.Content.Count == 0); break;
                case "hover":
                case "active":
                case "focus":
                case "focus-visible":
                case "focus-within":
                case "visited":
                case "target":
                case "checked":
                case "disabled":
                    c.Never = true;
                    break;
                case "link":
                case "any-link":
                case "enabled":
                    c.Never = true;
                    break;
                case "nth-child()":
                case "nth-last-child()":
                {
                    if (!NthParse(argument, out int a, out int b)) return false;
                    bool last = name == "nth-last-child";
                    c.Tests.Add(e =>
                    {
                        int position = last ? (e.Parent == null ? 1 : e.Parent.Children.Count - e.Index) : e.Index + 1;
                        return a == 0 ? position == b : (position - b) % a == 0 && (position - b) / a >= 0;
                    });
                    break;
                }
                case "not()":
                {
                    int at = 0, i2 = 0, c2 = 0, t2 = 0;
                    Compound inner = ReadCompound(argument, ref at, ref i2, ref c2, ref t2, ref read);
                    if (inner == null || at != argument.Length) return false;
                    ids += i2;
                    classes += c2;
                    types += t2;
                    c.Tests.Add(e => !Simple(inner, e));
                    return true;
                }
                default:
                    return false;
            }
            classes++;
            return true;
        }

        static bool NthParse(string text, out int a, out int b)
        {
            a = 0;
            b = 0;
            string t = text.Replace(" ", "").ToLowerInvariant();
            if (t == "odd") { a = 2; b = 1; return true; }
            if (t == "even") { a = 2; b = 0; return true; }
            var m = System.Text.RegularExpressions.Regex.Match(t, @"^(?:([+-]?\d*)n)?([+-]?\d+)?$");
            if (!m.Success || t.Length == 0) return false;
            if (m.Groups[1].Success) a = m.Groups[1].Value == "" || m.Groups[1].Value == "+" ? 1 : m.Groups[1].Value == "-" ? -1 : int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (m.Groups[2].Success) b = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return true;
        }

        // ---------- matching ----------

        // The declarations of the rules that match an element, lowest priority first (specificity,
        // then the order they're written in).
        public List<Declaration> Matching(Element e)
        {
            var found = new List<Rule>();
            if (count == 0) return new List<Declaration>();
            string id = e["id"];
            if (id != null && byId.TryGetValue(id, out List<Rule> r1)) Try(r1, e, found);
            // each rule is in one list, and each list is tried once (each class name is there once): no
            // rule is found twice
            string[] classes = e.ClassNames;
            Charge(classes.Length);
            foreach (string c in classes)
                if (byClass.TryGetValue(c, out List<Rule> r2)) Try(r2, e, found);
            if (byType.TryGetValue(e.Name, out List<Rule> r3)) Try(r3, e, found);
            Try(universal, e, found);
            Charge(found.Count);   // the work sorting them (the comparisons of a merge sort, near enough)
            found.Sort((x, y) => x.Specificity != y.Specificity ? x.Specificity.CompareTo(y.Specificity) : x.Order.CompareTo(y.Order));
            var result = new List<Declaration>();
            foreach (Rule r in found) result.AddRange(r.Declarations);
            return result;
        }

        static string[] ClassList(string value) => value.Split(new[] { ' ', '\t', '\n', '\r', '\f' }, StringSplitOptions.RemoveEmptyEntries);

        void Try(List<Rule> rules, Element e, List<Rule> found)
        {
            foreach (Rule r in rules)
                if (Matches(r.Parts, 0, e)) found.Add(r);
        }

        bool Matches(List<Compound> parts, int i, Element e)
        {
            Charge(1);
            if (!Simple(parts[i], e)) return false;
            if (i == parts.Count - 1) return true;
            switch (parts[i].Combinator)
            {
                case '>':
                    return e.Parent != null && Matches(parts, i + 1, e.Parent);
                case '+':
                    return e.Parent != null && e.Index > 0 && Matches(parts, i + 1, e.Parent.Children[e.Index - 1]);
                case '~':
                    for (int k = e.Index - 1; e.Parent != null && k >= 0; k--)
                        if (Matches(parts, i + 1, e.Parent.Children[k])) return true;
                    return false;
                default:
                    for (Element p = e.Parent; p != null; p = p.Parent)
                        if (Matches(parts, i + 1, p)) return true;
                    return false;
            }
        }

        // one compound against an element; what it compares (names, attributes, the text of values)
        // counts as steps
        static bool Simple(Compound c, Element e)
        {
            if (c.Never) return false;
            if (c.Type != null && c.Type != e.Name) return false;
            if (c.Id != null)
            {
                Charge(e.Attributes.Count);
                if (e["id"] != c.Id) return false;
            }
            if (c.Classes.Count > 0)
            {
                string[] list = e.ClassNames;
                Charge((long)c.Classes.Count * list.Length);
                foreach (string want in c.Classes)
                    if (Array.IndexOf(list, want) < 0) return false;
            }
            foreach (string[] a in c.Attributes)
            {
                Charge(e.Attributes.Count);
                string v = null;
                foreach (Attr at in e.Attributes)
                    if (at.Name == a[0]) v = at.Value;
                if (v == null) return false;
                string w = a[2];
                Charge(a[1] == "*=" ? (long)v.Length * w.Length / 64 : v.Length / 16);   // looking for one text in another tries it at each place
                switch (a[1])
                {
                    case "": break;
                    case "=": if (v != w) return false; break;
                    case "~=": if (Array.IndexOf(ClassList(v), w) < 0) return false; break;
                    case "|=": if (v != w && !v.StartsWith(w + "-", StringComparison.Ordinal)) return false; break;
                    case "^=": if (w.Length == 0 || !v.StartsWith(w, StringComparison.Ordinal)) return false; break;
                    case "$=": if (w.Length == 0 || !v.EndsWith(w, StringComparison.Ordinal)) return false; break;
                    case "*=": if (w.Length == 0 || v.IndexOf(w, StringComparison.Ordinal) < 0) return false; break;
                }
            }
            foreach (Func<Element, bool> test in c.Tests)
                if (!test(e)) return false;
            return true;
        }
    }

    // A fill or a stroke: none, a colour, the current colour, or a gradient (by its id) with what to
    // use when there's no such gradient.
    struct Paint
    {
        public enum Kinds : byte { None, Colour, Current, Url }

        public Kinds Kind;
        public Color Colour;
        public string Id;                 // a gradient's id; null for an address outside the file
        public Kinds Fallback;            // for a url: what to use without it (None also when none was given)
        public Color FallbackColour;

        public static readonly Paint None = new Paint { Kind = Kinds.None };
        public static Paint Of(Color c) => new Paint { Kind = Kinds.Colour, Colour = c };

        public static bool TryParse(string value, out Paint paint)
        {
            paint = None;
            string v = value.Trim();
            if (v.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                int close = v.IndexOf(')');
                if (close < 0) return false;
                paint.Kind = Kinds.Url;
                paint.Id = Url(v.Substring(0, close + 1));
                string rest = v.Substring(close + 1).Trim();
                if (rest.Length == 0) return true;
                if (!Simple(rest, out Paint fallback) ) return false;
                paint.Fallback = fallback.Kind;
                paint.FallbackColour = fallback.Colour;
                return true;
            }
            return Simple(v, out paint);
        }

        static bool Simple(string v, out Paint paint)
        {
            paint = None;
            if (v.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
            if (v.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
            {
                paint.Kind = Kinds.Current;
                return true;
            }
            Color? c = Design.Css.Colour(v);
            if (c == null) return false;
            paint = Of(c.Value);
            return true;
        }

        // the id in url(#id), url("#id") or url('#id'); null for anything outside the file
        public static string Url(string text)
        {
            string t = text.Trim();
            if (!t.StartsWith("url(", StringComparison.OrdinalIgnoreCase) || !t.EndsWith(")", StringComparison.Ordinal)) return null;
            string inside = t.Substring(4, t.Length - 5).Trim().Trim('"', '\'').Trim();
            return inside.StartsWith("#", StringComparison.Ordinal) && inside.Length > 1 ? inside.Substring(1) : null;
        }
    }

    // An element's style once the cascade is done: what's inherited from its parent, its
    // presentation attributes, the style sheet's rules and its style attribute, in that order.
    sealed class Style
    {
        const int MaxDashes = 10000;   // values in a stroke-dasharray (real ones have a handful)

        // Chromium's biggest font size, 10,000 px: a size past it is that size (font-size="50000", "1e9" and "1e400",
        // and nine groups of 3e38em inside one another, are all 10,000 px in Edge 156, as are the em units of the text
        // in them), so a size is never infinite, and WPF lays out every one (it refuses one past 35,791 px)
        public const double MostFontSize = 10000;

        // inherited
        public Paint Fill = Paint.Of(Colors.Black), Stroke = Paint.None;
        public double FillOpacity = 1, StrokeOpacity = 1;
        public FillRule FillRule = FillRule.Nonzero, ClipRule = FillRule.Nonzero;
        public Length StrokeWidth = new Length(1);
        public PenLineCap LineCap = PenLineCap.Flat;
        public PenLineJoin LineJoin = PenLineJoin.Miter;
        public double MiterLimit = 4;
        public Length[] Dashes;
        public Length DashOffset;
        public Color Color = Colors.Black;
        public bool Visible = true;
        public string[] FontFamily;
        public double FontSize = 16;
        public int FontWeight = 400;
        public bool Italic;
        public string TextAnchor = "start", Baseline = "auto";
        public Length? LetterSpacing, WordSpacing;
        public bool PreserveSpace, StrokeFirst, Pixelated;

        // not inherited
        public bool Display = true;
        public double Opacity = 1;
        public string ClipPath, Mask, Filter;
        public Color StopColor = Colors.Black;
        public double StopOpacity = 1;
        public string Overflow;               // null: as the element has it (hidden for inner <svg> and <symbol>)
        public Matrix? Transform;             // CSS transform in a style
        public Dictionary<string, string> Geometry;   // x, y, width, height, cx, cy, r, rx and ry set by CSS, which beat the attributes

        public static readonly Style Initial = new Style();

        static readonly HashSet<string> PresentationAttributes = new HashSet<string>
        {
            "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit",
            "stroke-dasharray", "stroke-dashoffset", "color", "visibility", "display", "opacity", "clip-path", "clip-rule", "mask", "filter",
            "font-family", "font-size", "font-weight", "font-style", "text-anchor", "dominant-baseline", "letter-spacing", "word-spacing",
            "stop-color", "stop-opacity", "overflow", "paint-order", "image-rendering",
        };

        // the style of `e` inside a parent with style `parent`
        public static Style Of(Element e, Style parent, StyleSheet sheet)
        {
            var s = (Style)parent.MemberwiseClone();
            s.Display = true;
            s.Opacity = 1;
            s.ClipPath = s.Mask = s.Filter = null;
            s.StopColor = Colors.Black;
            s.StopOpacity = 1;
            s.stopCurrent = false;
            s.Overflow = null;
            s.Transform = null;
            s.Geometry = null;
            string space = e["space", Xml.XmlSpace];
            if (space == "preserve") s.PreserveSpace = true;
            else if (space == "default") s.PreserveSpace = false;

            var all = new List<Declaration>();
            foreach (Attr a in e.Attributes)
                if (a.Namespace.Length == 0 && PresentationAttributes.Contains(a.Name)) all.Add(new Declaration { Property = a.Name, Value = a.Value });
            if (sheet != null && !sheet.IsEmpty) all.AddRange(sheet.Matching(e));
            string inline = e["style"];
            if (inline != null) all.AddRange(StyleSheet.Declarations(inline));
            StyleSheet.ChargeApply(all);   // before applying them, so a huge lot (a file leaning on <use>) is stopped first
            foreach (Declaration d in all)
                if (!d.Important) s.Apply(d.Property, d.Value, parent);
            foreach (Declaration d in all)
                if (d.Important) s.Apply(d.Property, d.Value, parent);
            if (s.stopCurrent) s.StopColor = s.Color;   // the element's own colour, whatever order they came in
            return s;
        }

        bool stopCurrent;

        static readonly Dictionary<string, double> FontSizes = new Dictionary<string, double>
        {
            ["xx-small"] = 9, ["x-small"] = 10, ["small"] = 13, ["medium"] = 16, ["large"] = 18, ["x-large"] = 24, ["xx-large"] = 32, ["xxx-large"] = 48,
        };

        // one declaration; a value this property can't take is left out, as CSS does
        void Apply(string property, string value, Style parent)
        {
            string v = value.Trim();
            string k = v.ToLowerInvariant();
            if (k == "inherit" || k == "unset" && Inherits(property))
            {
                Copy(property, parent);
                return;
            }
            if (k == "initial" || k == "unset")
            {
                Copy(property, Initial);
                return;
            }
            switch (property)
            {
                case "fill":
                    if (Paint.TryParse(v, out Paint fill)) Fill = fill;
                    break;
                case "stroke":
                    if (Paint.TryParse(v, out Paint stroke)) Stroke = stroke;
                    break;
                case "fill-opacity": Fraction(v, ref FillOpacity); break;
                case "stroke-opacity": Fraction(v, ref StrokeOpacity); break;
                case "opacity": Fraction(v, ref Opacity); break;
                case "stop-opacity": Fraction(v, ref StopOpacity); break;
                case "fill-rule":
                case "clip-rule":
                    if (k == "nonzero" || k == "evenodd")
                    {
                        FillRule rule = k == "evenodd" ? FillRule.EvenOdd : FillRule.Nonzero;
                        if (property == "fill-rule") FillRule = rule;
                        else ClipRule = rule;
                    }
                    break;
                case "stroke-width":
                    if (Length.TryParse(v, out Length width) && width.Value >= 0) StrokeWidth = width;
                    break;
                case "stroke-linecap":
                    if (k == "butt") LineCap = PenLineCap.Flat;
                    else if (k == "round") LineCap = PenLineCap.Round;
                    else if (k == "square") LineCap = PenLineCap.Square;
                    break;
                case "stroke-linejoin":
                    if (k == "miter") LineJoin = PenLineJoin.Miter;
                    else if (k == "round") LineJoin = PenLineJoin.Round;
                    else if (k == "bevel") LineJoin = PenLineJoin.Bevel;
                    break;
                case "stroke-miterlimit":
                    if (CssNumber(v, false, out double miter, out _) && miter >= 1) MiterLimit = miter;
                    break;
                case "stroke-dasharray":
                    if (k == "none") Dashes = null;
                    else
                    {
                        // read the values one at a time (not all split out first): a dash array with
                        // more than MaxDashes of them is refused before the whole huge value is read
                        var dashes = new List<Length>();
                        bool ok = true;
                        int at = 0;
                        while (at < v.Length)
                        {
                            while (at < v.Length && (v[at] == ',' || v[at] == ' ' || v[at] == '\t' || v[at] == '\n' || v[at] == '\r')) at++;
                            if (at == v.Length) break;
                            int start = at;
                            while (at < v.Length && !(v[at] == ',' || v[at] == ' ' || v[at] == '\t' || v[at] == '\n' || v[at] == '\r')) at++;
                            if (dashes.Count >= MaxDashes) throw new SvgException("its stroke-dasharray has more than " + MaxDashes + " values");
                            if (!Length.TryParse(v.Substring(start, at - start), out Length dash) || dash.Value < 0) { ok = false; break; }
                            dashes.Add(dash);
                        }
                        if (ok && dashes.Count > 0) Dashes = dashes.ToArray();
                    }
                    break;
                case "stroke-dashoffset":
                    if (Length.TryParse(v, out Length offset)) DashOffset = offset;
                    break;
                case "color":
                    if (k == "currentcolor") Color = parent.Color;
                    else if (Design.Css.Colour(v) is Color colour) Color = colour;
                    break;
                case "stop-color":
                    if (k == "currentcolor") stopCurrent = true;
                    else if (Design.Css.Colour(v) is Color stop)
                    {
                        StopColor = stop;
                        stopCurrent = false;
                    }
                    break;
                case "visibility":
                    if (k == "visible") Visible = true;
                    else if (k == "hidden" || k == "collapse") Visible = false;
                    break;
                case "display":
                    Display = k != "none";
                    break;
                case "clip-path":
                    if (k == "none") ClipPath = null;
                    else if (k.StartsWith("url(", StringComparison.Ordinal)) ClipPath = Paint.Url(v) ?? "";
                    break;
                case "mask":
                    Mask = k == "none" ? null : Paint.Url(v) ?? "";
                    break;
                case "filter":
                    Filter = k == "none" ? null : v;
                    break;
                case "overflow":
                    Overflow = k;
                    break;
                case "font-family":
                    string[] families = Families(v);
                    if (families != null) FontFamily = families;
                    break;
                case "font-size":
                    if (FontSizes.TryGetValue(k, out double named)) FontSize = named;
                    else if (k == "smaller") FontSize = parent.FontSize / 1.2;
                    else if (k == "larger") FontSize = Math.Min(MostFontSize, parent.FontSize * 1.2);
                    else if (Length.TryParse(v, out Length size, true) && size.Value >= 0)
                    {
                        // (of a parent's size, which is never past the cap, a size is never NaN; were it one, the
                        // parent's would be kept)
                        double px = size.Px(parent.FontSize, parent.FontSize);
                        if (!double.IsNaN(px)) FontSize = Math.Min(MostFontSize, px);
                    }
                    break;
                case "font-weight":
                    if (k == "normal") FontWeight = 400;
                    else if (k == "bold") FontWeight = 700;
                    else if (k == "bolder") FontWeight = parent.FontWeight < 350 ? 400 : parent.FontWeight < 550 ? 700 : 900;
                    else if (k == "lighter") FontWeight = parent.FontWeight < 550 ? 100 : parent.FontWeight < 750 ? 400 : 700;
                    else if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double weight) && weight >= 1 && weight <= 1000) FontWeight = (int)Math.Round(weight);
                    break;
                case "font-style":
                    if (k == "normal") Italic = false;
                    else if (k == "italic" || k.StartsWith("oblique", StringComparison.Ordinal)) Italic = true;
                    break;
                case "font":
                    Font(v, parent);
                    break;
                case "text-anchor":
                    if (k == "start" || k == "middle" || k == "end") TextAnchor = k;
                    break;
                case "dominant-baseline":
                    Baseline = k;
                    break;
                case "letter-spacing":
                case "word-spacing":
                    Length? spacing = null;
                    if (k == "normal") spacing = null;
                    else if (Length.TryParse(v, out Length l)) spacing = l;
                    else break;
                    if (property == "letter-spacing") LetterSpacing = spacing;
                    else WordSpacing = spacing;
                    break;
                case "white-space":
                    if (k == "pre" || k == "pre-wrap" || k == "break-spaces") PreserveSpace = true;
                    else if (k == "normal" || k == "nowrap" || k == "pre-line") PreserveSpace = false;
                    break;
                case "paint-order":
                    if (k == "normal") StrokeFirst = false;
                    else
                    {
                        int f = k.IndexOf("fill", StringComparison.Ordinal), st = k.IndexOf("stroke", StringComparison.Ordinal);
                        StrokeFirst = st >= 0 && (f < 0 || st < f);
                    }
                    break;
                case "image-rendering":
                    Pixelated = k == "pixelated" || k == "crisp-edges" || k == "optimizespeed";
                    break;
                case "transform":
                    Matrix? m = Transforms.ParseCss(v);
                    if (m != null) Transform = m;
                    break;
                case "x":
                case "y":
                case "width":
                case "height":
                case "cx":
                case "cy":
                case "r":
                case "rx":
                case "ry":
                    if (Length.TryParse(v, out _) || k == "auto")
                    {
                        if (Geometry == null) Geometry = new Dictionary<string, string>();
                        Geometry[property] = v;
                    }
                    break;
            }
        }

        static bool Inherits(string property) =>
            !(property == "opacity" || property == "display" || property == "clip-path" || property == "mask" || property == "filter" || property == "stop-color" ||
              property == "stop-opacity" || property == "overflow" || property == "transform");

        // `property` as `from` has it (for inherit, initial and unset)
        void Copy(string property, Style from)
        {
            switch (property)
            {
                case "fill": Fill = from.Fill; break;
                case "stroke": Stroke = from.Stroke; break;
                case "fill-opacity": FillOpacity = from.FillOpacity; break;
                case "stroke-opacity": StrokeOpacity = from.StrokeOpacity; break;
                case "opacity": Opacity = from.Opacity; break;
                case "stop-opacity": StopOpacity = from.StopOpacity; break;
                case "fill-rule": FillRule = from.FillRule; break;
                case "clip-rule": ClipRule = from.ClipRule; break;
                case "stroke-width": StrokeWidth = from.StrokeWidth; break;
                case "stroke-linecap": LineCap = from.LineCap; break;
                case "stroke-linejoin": LineJoin = from.LineJoin; break;
                case "stroke-miterlimit": MiterLimit = from.MiterLimit; break;
                case "stroke-dasharray": Dashes = from.Dashes; break;
                case "stroke-dashoffset": DashOffset = from.DashOffset; break;
                case "color": Color = from.Color; break;
                case "stop-color": StopColor = from.StopColor; break;
                case "visibility": Visible = from.Visible; break;
                case "display": Display = from.Display; break;
                case "clip-path": ClipPath = from.ClipPath; break;
                case "mask": Mask = from.Mask; break;
                case "filter": Filter = from.Filter; break;
                case "overflow": Overflow = from.Overflow; break;
                case "font-family": FontFamily = from.FontFamily; break;
                case "font-size": FontSize = from.FontSize; break;
                case "font-weight": FontWeight = from.FontWeight; break;
                case "font-style": Italic = from.Italic; break;
                case "text-anchor": TextAnchor = from.TextAnchor; break;
                case "dominant-baseline": Baseline = from.Baseline; break;
                case "letter-spacing": LetterSpacing = from.LetterSpacing; break;
                case "word-spacing": WordSpacing = from.WordSpacing; break;
                case "paint-order": StrokeFirst = from.StrokeFirst; break;
                case "image-rendering": Pixelated = from.Pixelated; break;
                case "transform": Transform = from.Transform; break;
            }
        }

        // an opacity: a number or a percentage, 0 to 1 (one past a float's range is as big as a float goes, as CSS
        // has it: fill-opacity="1e400" is 1 in Chromium)
        static void Fraction(string v, ref double into)
        {
            if (!CssNumber(v, true, out double x, out bool percent)) return;
            if (percent) x /= 100;
            into = Math.Max(0, Math.Min(1, x));
        }

        // A number as CSS writes one, or (with `opacity`) a number or a percentage, a number past a float's range
        // being as big as a float goes; false for anything else. CSS has no NaN or Infinity, which .NET reads:
        // Chromium leaves fill-opacity="NaN" and stroke-miterlimit="Infinity" out (the parent's, or the value before,
        // stays), where WPF took them for no opacity at all and drew no stroke.
        static bool CssNumber(string v, bool opacity, out double x, out bool percent)
        {
            percent = false;
            var n = new Numbers(v);
            n.SkipSpaces();
            if (!n.Number(out x, opacity)) return false;
            if (opacity) percent = n.Take('%');
            n.SkipSpaces();
            return n.AtEnd;
        }

        // a font-family list: quoted names, or words joined by single spaces; null if it's broken
        public static string[] Families(string value)
        {
            var found = new List<string>();
            foreach (string part in value.Split(','))
            {
                string p = part.Trim();
                if (p.Length == 0) return null;
                if (p[0] == '"' || p[0] == '\'')
                {
                    if (p.Length < 2 || p[p.Length - 1] != p[0]) return null;
                    found.Add(p.Substring(1, p.Length - 2));
                }
                else found.Add(string.Join(" ", p.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries)));
            }
            return found.Count > 0 ? found.ToArray() : null;
        }

        // the font shorthand: [style] [weight] size[/line-height] family
        void Font(string v, Style parent)
        {
            var m = System.Text.RegularExpressions.Regex.Match(v, @"^\s*((?:(?:italic|oblique|normal|bold|bolder|lighter|[1-9]00|small-caps)\s+)*)([^\s/]+)(?:\s*/\s*\S+)?\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!m.Success) return;
            string size = m.Groups[2].Value;
            if (!FontSizes.ContainsKey(size.ToLowerInvariant()) && !Length.TryParse(size, out _, true)) return;
            string[] families = Families(m.Groups[3].Value);
            if (families == null) return;
            Italic = false;
            FontWeight = 400;
            foreach (string word in m.Groups[1].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Equals("normal", StringComparison.OrdinalIgnoreCase) || word.Equals("small-caps", StringComparison.OrdinalIgnoreCase)) continue;
                if (word.Equals("italic", StringComparison.OrdinalIgnoreCase) || word.Equals("oblique", StringComparison.OrdinalIgnoreCase)) Italic = true;
                else Apply("font-weight", word, parent);
            }
            Apply("font-size", size, parent);
            FontFamily = families;
        }
    }
}
