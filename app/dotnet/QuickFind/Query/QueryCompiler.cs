using System.Text.RegularExpressions;
using QuickFind.Core;

namespace QuickFind.Query;

public static class QueryCompiler
{
    static readonly string[] Modifiers =
    [
        "nocase:", "case:", "nopath:", "path:", "nowholeword:", "wholeword:", "noww:", "ww:", "noregex:", "regex:",
        "nodiacritics:", "diacritics:", "nowildcards:", "wildcards:", "wholefilename:", "wfn:", "startwith:", "endwith:",
        "files:", "file:", "folders:", "folder:", "ascii:", "utf8:", "nofile:", "nofolder:"
    ];

    public static readonly (string Name, ulong Lo, ulong Hi)[] SizeBands =
    [
        ("empty", 0, 0),
        ("tiny", 1, 10 * 1024),
        ("small", 10 * 1024 + 1, 100 * 1024),
        ("medium", 100 * 1024 + 1, 1024UL * 1024),
        ("large", 1024UL * 1024 + 1, 16 * 1024UL * 1024),
        ("huge", 16 * 1024UL * 1024 + 1, 128 * 1024UL * 1024),
        ("gigantic", 128 * 1024UL * 1024 + 1, ulong.MaxValue - 1)
    ];

    enum TokKind { Open, Close, Or, Not, Word }
    sealed record Tok(TokKind Kind, string? Word = null);

    struct Mods
    {
        public bool? Case, WholeWord, Path, Diacritics, Regex, Wildcards;
        public bool Wfn, Start, End, FilesOnly, FoldersOnly;

        public Mods Merge(Mods inner) => new()
        {
            Case = inner.Case ?? Case,
            WholeWord = inner.WholeWord ?? WholeWord,
            Path = inner.Path ?? Path,
            Diacritics = inner.Diacritics ?? Diacritics,
            Regex = inner.Regex ?? Regex,
            Wildcards = inner.Wildcards ?? Wildcards,
            Wfn = Wfn || inner.Wfn,
            Start = Start || inner.Start,
            End = End || inner.End,
            FilesOnly = FilesOnly || inner.FilesOnly,
            FoldersOnly = FoldersOnly || inner.FoldersOnly
        };

        public bool IsEmptyType => !FilesOnly && !FoldersOnly;
    }

    sealed class Parser
    {
        public List<Tok> Toks = [];
        public int Pos;
        public SearchOptions Opts = new();
        public int? Count;
        public bool Dupe;
        public string? Content;
        public string? Error;

        public Tok? Peek() => Pos < Toks.Count ? Toks[Pos] : null;
    }

    public static CompiledQuery Compile(string text, SearchOptions opts)
    {
        var toks = Tokenize(text);
        var isEmpty = toks.Count == 0;
        var p = new Parser { Toks = toks, Opts = opts };
        var root = ParseOr(p, default);
        if (p.Error is not null) throw new ArgumentException(p.Error);
        return new CompiledQuery { Root = root, Count = p.Count, Dupe = p.Dupe, IsEmpty = isEmpty, Content = p.Content };
    }

    static bool IsModifierOnly(string w)
    {
        if (w.Length == 0) return false;
        var rest = w;
        while (rest.Length > 0)
        {
            var lower = rest.ToLowerInvariant();
            var found = Modifiers.FirstOrDefault(m => lower.StartsWith(m, StringComparison.Ordinal));
            if (found is null) return false;
            rest = rest[found.Length..];
        }
        return true;
    }

    static List<Tok> Tokenize(string s)
    {
        var outList = new List<Tok>();
        var word = "";
        var quoted = false;
        void Push()
        {
            if (word.Length > 0) { outList.Add(new Tok(TokKind.Word, word)); word = ""; }
        }
        foreach (var c in s)
        {
            if (quoted)
            {
                if (c == '"') quoted = false;
                else word += c;
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; break;
                case var _ when char.IsWhiteSpace(c): Push(); break;
                case '|': Push(); outList.Add(new Tok(TokKind.Or)); break;
                case '!' when word.Length == 0: outList.Add(new Tok(TokKind.Not)); break;
                case '<' or '>':
                {
                    var last = word.Length > 0 ? word[^1] : '\0';
                    var comparison = last is ':' or '<' or '>' or '=';
                    if (comparison && !(c == '<' && IsModifierOnly(word)))
                        word += c;
                    else
                    {
                        Push();
                        outList.Add(new Tok(c == '<' ? TokKind.Open : TokKind.Close));
                    }
                    break;
                }
                default: word += c; break;
            }
        }
        Push();
        return outList;
    }

    static Node ParseOr(Parser p, Mods mods)
    {
        var alts = new List<Node> { ParseAnd(p, mods) };
        while (p.Peek()?.Kind == TokKind.Or)
        {
            p.Pos++;
            alts.Add(ParseAnd(p, mods));
        }
        return alts.Count == 1 ? alts[0] : new Node { Kind = NodeKind.Or, Children = alts };
    }

    static Node ParseAnd(Parser p, Mods mods)
    {
        var items = new List<Node>();
        var cur = mods;
        while (true)
        {
            var peek = p.Peek();
            if (peek is null || peek.Kind is TokKind.Or or TokKind.Close) break;
            if (peek.Kind == TokKind.Word && IsModifierOnly(peek.Word!))
            {
                p.Pos++;
                var (m, _) = StripModifiers(peek.Word!);
                var next = p.Peek();
                var nextIsTerm = next is not null && next.Kind is not (TokKind.Or or TokKind.Close);
                if (nextIsTerm) cur = cur.Merge(m);
                else if (!m.IsEmptyType) items.Add(Leaf(new Term { Kind = TermKind.True }, cur.Merge(m)));
            }
            else
            {
                var n = ParseUnary(p, cur);
                if (n is not null) items.Add(n);
            }
        }
        if (items.Count == 0) return Leaf(new Term { Kind = TermKind.True }, cur);
        return items.Count == 1 ? items[0] : new Node { Kind = NodeKind.And, Children = items };
    }

    static Node? ParseUnary(Parser p, Mods mods)
    {
        var peek = p.Peek();
        if (peek is null) return null;
        switch (peek.Kind)
        {
            case TokKind.Not:
                p.Pos++;
                var inner = ParseUnary(p, mods);
                return inner is null ? null : new Node { Kind = NodeKind.Not, Child = inner };
            case TokKind.Open:
                p.Pos++;
                var n = ParseOr(p, mods);
                if (p.Peek()?.Kind == TokKind.Close) p.Pos++;
                return n;
            case TokKind.Close or TokKind.Or:
                p.Pos++;
                return null;
            case TokKind.Word:
                p.Pos++;
                return Word(p, peek.Word!, mods);
            default:
                return null;
        }
    }

    static Node? Word(Parser p, string w, Mods outer)
    {
        var (m, rest) = StripModifiers(w);
        var mods = outer.Merge(m);
        var lower = rest.ToLowerInvariant();

        string? Func(string name) => lower.StartsWith(name, StringComparison.Ordinal) ? rest[name.Length..] : null;

        foreach (var (macroName, exts) in Filters.Macros)
        {
            var arg = Func(macroName);
            if (arg is null) continue;
            var nodes = new List<Node> { Leaf(new Term { Kind = TermKind.Ext, Exts = SplitExts(exts) }, mods) };
            if (arg.Length > 0)
            {
                var t = TextLeaf(p, arg, mods);
                if (t is not null) nodes.Add(t);
            }
            return nodes.Count == 1 ? nodes[0] : new Node { Kind = NodeKind.And, Children = nodes };
        }

        if (Func("ext:") is { } extArg)
            return Leaf(new Term { Kind = TermKind.Ext, Exts = SplitExts(extArg) }, mods);

        if (Func("size:") is { } sizeArg)
            return Leaf(ParseSize(sizeArg) ?? new Term { Kind = TermKind.True }, mods);

        foreach (var (name, created) in new[] { ("dm:", false), ("datemodified:", false), ("dc:", true), ("datecreated:", true) })
        {
            if (Func(name) is not { } darg) continue;
            if (DateParser.ParseRange(darg) is { } range)
                return Leaf(new Term { Kind = TermKind.Date, DateCreated = created, DateLo = range.Lo, DateHi = range.Hi }, mods);
            return Leaf(new Term { Kind = TermKind.True }, mods);
        }

        foreach (var name in new[] { "infolder:", "parent:" })
        {
            if (Func(name) is not { } iarg) continue;
            var folder = TextNorm.Fold(iarg.TrimEnd('\\'));
            return Leaf(new Term { Kind = TermKind.InFolder, InFolder = folder }, mods);
        }

        if (Func("len:") is { } lenArg)
        {
            var (lo, hi) = ParseCompare(lenArg, v =>
            {
                if (ulong.TryParse(v.Trim(), out var n)) return (n, n);
                return null;
            }) ?? (0UL, ulong.MaxValue);
            return Leaf(new Term { Kind = TermKind.Len, LenLo = lo, LenHi = hi }, mods);
        }

        if (Func("count:") is { } countArg)
        {
            if (int.TryParse(countArg.Trim(), out var c)) p.Count = c;
            return Leaf(new Term { Kind = TermKind.True }, mods);
        }

        if (Func("dupe:") is not null)
        {
            p.Dupe = true;
            return Leaf(new Term { Kind = TermKind.True }, mods);
        }

        if (Func("content:") is { } contentArg)
        {
            var needle = contentArg.Trim().Trim('"');
            if (needle.Length > 0) p.Content = needle;
            // Pre-filter to non-directories; content scan happens in SearchService.
            var fileMods = mods;
            fileMods.FilesOnly = true;
            return Leaf(new Term { Kind = TermKind.True }, fileMods);
        }

        if (Func("empty:") is not null)
            return Leaf(new Term { Kind = TermKind.Empty }, mods);

        foreach (var name in new[] { "attrib:", "attributes:" })
        {
            if (Func(name) is not { } aarg) continue;
            return Leaf(new Term { Kind = TermKind.Attrib, Attrib = ParseAttrib(aarg) }, mods);
        }

        return TextLeaf(p, rest, mods);
    }

    static Node? TextLeaf(Parser p, string text, Mods mods)
    {
        if (text.Length == 0) return Leaf(new Term { Kind = TermKind.True }, mods);
        var o = p.Opts;
        var caseSens = mods.Case ?? o.Case;
        var diacritics = mods.Diacritics ?? o.Diacritics;
        var regex = mods.Regex ?? o.Regex;
        var wholeWord = mods.WholeWord ?? o.WholeWord;
        var wildcards = mods.Wildcards ?? true;
        var path = (mods.Path ?? o.Path) || text.Contains('\\') || text.Contains('/');
        var textNorm = text.Replace('/', '\\');
        string Norm(string s) => TextNorm.Normalize(s, caseSens, diacritics);

        var tt = new TextTerm { Case = caseSens, Diacritics = diacritics, Path = path };
        if (regex)
        {
            var pat = caseSens ? text : "(?i)" + text;
            try { tt.Regex = new Regex(pat, RegexOptions.Compiled); tt.Kind = TextModeKind.Regex; }
            catch { p.Error = $"Invalid regex: {text}"; return null; }
        }
        else if (wildcards && (text.Contains('*') || text.Contains('?')))
        {
            tt.Kind = TextModeKind.Glob;
            tt.GlobPattern = System.Text.Encoding.UTF8.GetBytes(Norm(textNorm));
        }
        else if (mods.Wfn) { tt.Kind = TextModeKind.Equals; tt.Text = Norm(textNorm); }
        else if (mods.Start) { tt.Kind = TextModeKind.Starts; tt.Text = Norm(textNorm); }
        else if (mods.End) { tt.Kind = TextModeKind.Ends; tt.Text = Norm(textNorm); }
        else if (wholeWord) { tt.Kind = TextModeKind.Word; tt.Text = Norm(textNorm); }
        else { tt.Kind = TextModeKind.Contains; tt.Text = Norm(textNorm); }

        return Leaf(new Term { Kind = TermKind.Text, Text = tt }, mods);
    }

    static Node Leaf(Term term, Mods m) => new()
    {
        Kind = NodeKind.Leaf,
        Term = term,
        FilesOnly = m.FilesOnly,
        FoldersOnly = m.FoldersOnly
    };

    static (Mods, string) StripModifiers(string w)
    {
        var m = new Mods();
        var rest = w;
        while (true)
        {
            var lower = rest.ToLowerInvariant();
            var found = Modifiers.FirstOrDefault(p => lower.StartsWith(p, StringComparison.Ordinal));
            if (found is null) break;
            switch (found)
            {
                case "case:": m.Case = true; break;
                case "nocase:": m.Case = false; break;
                case "path:": m.Path = true; break;
                case "nopath:": m.Path = false; break;
                case "wholeword:" or "ww:": m.WholeWord = true; break;
                case "nowholeword:" or "noww:": m.WholeWord = false; break;
                case "regex:": m.Regex = true; break;
                case "noregex:": m.Regex = false; break;
                case "diacritics:": m.Diacritics = true; break;
                case "nodiacritics:": m.Diacritics = false; break;
                case "wildcards:": m.Wildcards = true; break;
                case "nowildcards:": m.Wildcards = false; break;
                case "wholefilename:" or "wfn:": m.Wfn = true; break;
                case "startwith:": m.Start = true; break;
                case "endwith:": m.End = true; break;
                case "file:" or "files:" or "nofolder:": m.FilesOnly = true; break;
                case "folder:" or "folders:" or "nofile:": m.FoldersOnly = true; break;
            }
            rest = rest[found.Length..];
            if (m.Regex == true) break;
        }
        return (m, rest);
    }

    static List<string> SplitExts(string s) =>
        s.Split(';').Select(e => e.Trim().TrimStart('.').ToLowerInvariant()).Where(e => e.Length > 0).ToList();

    static (ulong Lo, ulong Hi)? ParseCompare(string arg, Func<string, (ulong, ulong)?> value)
    {
        var a = arg.Trim();
        foreach (var (op, f) in new (string, int)[] { ("<=", 0), (">=", 1), ("<", 2), (">", 3), ("=", 4) })
        {
            if (!a.StartsWith(op, StringComparison.Ordinal)) continue;
            if (value(a[op.Length..]) is not { } r) return null;
            return f switch
            {
                0 => (0, r.Item2),
                1 => (r.Item1, ulong.MaxValue),
                2 => (0, r.Item1 == 0 ? 0 : r.Item1 - 1),
                3 => (r.Item2 == ulong.MaxValue ? ulong.MaxValue : r.Item2 + 1, ulong.MaxValue),
                _ => r
            };
        }
        var sep = a.IndexOf("..", StringComparison.Ordinal);
        if (sep < 0) sep = a.IndexOf('-');
        if (sep > 0)
        {
            var l = value(a[..sep]);
            var r = value(a[(sep + (a.Contains("..") ? 2 : 1))..]);
            if (l is null || r is null) return null;
            return (l.Value.Item1, r.Value.Item2);
        }
        return value(a);
    }

    static Term? ParseSize(string arg)
    {
        var a = arg.Trim().ToLowerInvariant();
        if (a == "unknown") return new Term { Kind = TermKind.SizeUnknown };
        if (ParseCompare(a, SizeValue) is not { } r) return null;
        return new Term { Kind = TermKind.Size, SizeLo = r.Lo, SizeHi = r.Hi };
    }

    static (ulong, ulong)? SizeValue(string v)
    {
        v = v.Trim();
        var band = SizeBands.FirstOrDefault(b => b.Name == v);
        if (band.Name is not null) return (band.Lo, band.Hi);
        var split = 0;
        while (split < v.Length && (char.IsAsciiDigit(v[split]) || v[split] == '.')) split++;
        if (split == 0) return null;
        if (!double.TryParse(v[..split], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num))
            return null;
        var unit = v[split..].Trim();
        const ulong kb = 1024;
        ulong mult = unit switch
        {
            "" or "b" or "bytes" => 0,
            "k" or "kb" => kb,
            "m" or "mb" => kb * kb,
            "g" or "gb" => kb * kb * kb,
            "t" or "tb" => kb * kb * kb * kb,
            "p" or "pb" => kb * kb * kb * kb * kb,
            "e" or "eb" => kb * kb * kb * kb * kb * kb,
            _ => ulong.MaxValue
        };
        if (mult == ulong.MaxValue) return null;
        if (mult == 0) return ((ulong)num, (ulong)num);
        var hi = ((ulong)(num * mult) + 0x3ff) & ~0x3ffUL;
        return (hi - 0x3ff, hi);
    }

    static uint ParseAttrib(string s)
    {
        uint a = 0;
        foreach (var c in s.ToUpperInvariant())
        {
            a |= c switch
            {
                'R' => 0x1u, 'H' => 0x2u, 'S' => 0x4u, 'D' => 0x10u, 'A' => 0x20u, 'N' => 0x80u, 'T' => 0x100u,
                'P' => 0x200u, 'L' => 0x400u, 'C' => 0x800u, 'O' => 0x1000u, 'I' => 0x2000u, 'E' => 0x4000u, _ => 0u
            };
        }
        return a;
    }
}
