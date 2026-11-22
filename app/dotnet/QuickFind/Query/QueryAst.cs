using System.Text.RegularExpressions;
using QuickFind.Core;

namespace QuickFind.Query;

enum TextModeKind { Contains, Equals, Starts, Ends, Word, Regex, Glob }

sealed class TextTerm
{
    public TextModeKind Kind;
    public string Text = "";
    public byte[]? GlobPattern;
    public Regex? Regex;
    public bool Case;
    public bool Diacritics;
    public bool Path;
}

enum TermKind { Text, Ext, Size, SizeUnknown, Date, InFolder, Len, Empty, Attrib, True }

sealed class Term
{
    public TermKind Kind;
    public TextTerm? Text;
    public List<string>? Exts;
    public ulong SizeLo, SizeHi;
    public bool DateCreated;
    public long DateLo, DateHi;
    public string? InFolder;
    public ulong LenLo, LenHi;
    public uint Attrib;
}

enum NodeKind { And, Or, Not, Leaf }

sealed class Node
{
    public NodeKind Kind;
    public List<Node>? Children;
    public Node? Child;
    public Term? Term;
    public bool FilesOnly;
    public bool FoldersOnly;
}

public sealed class CompiledQuery
{
    internal Node Root = new() { Kind = NodeKind.And, Children = [] };
    public int? Count;
    public bool Dupe;
    public bool IsEmpty;
    /// <summary>Optional on-demand content needle from content:term.</summary>
    public string? Content;

    public bool Matches(FileIndex idx, Entry e) => Eval(Root, idx, e);

    static bool Eval(Node n, FileIndex idx, Entry e) => n.Kind switch
    {
        NodeKind.And => n.Children!.TrueForAll(c => Eval(c, idx, e)),
        NodeKind.Or => n.Children!.Exists(c => Eval(c, idx, e)),
        NodeKind.Not => !Eval(n.Child!, idx, e),
        NodeKind.Leaf => MatchLeaf(n, idx, e),
        _ => false
    };

    static bool MatchLeaf(Node n, FileIndex idx, Entry e)
    {
        var dir = e.IsDir;
        if ((n.FilesOnly && dir) || (n.FoldersOnly && !dir)) return false;
        return TermMatch(n.Term!, idx, e);
    }

    static bool TermMatch(Term t, FileIndex idx, Entry e)
    {
        switch (t.Kind)
        {
            case TermKind.True:
                return true;
            case TermKind.Text:
                return TextMatch(t.Text!, idx, e);
            case TermKind.Ext:
            {
                var ext = idx.ExtensionSpan(e);
                if (ext.IsEmpty) return false;
                foreach (var x in t.Exts!)
                {
                    if (ext.Equals(x.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
            case TermKind.Size:
                if (e.Size == AppConstants.NoSize) return false;
                return e.Size >= t.SizeLo && e.Size <= t.SizeHi;
            case TermKind.SizeUnknown:
                return e.IsDir && (e.Size == AppConstants.NoSize || e.Size == 0);
            case TermKind.Date:
            {
                var ft = t.DateCreated ? e.Ctime : e.Mtime;
                if (!DateParser.IsValidFileTime(ft)) return false;
                var unix = DateParser.FileTimeToUnix(ft);
                return unix >= t.DateLo && unix < t.DateHi;
            }
            case TermKind.InFolder:
                return idx.DirFoldSpan(e).Equals(t.InFolder.AsSpan(), StringComparison.Ordinal);
            case TermKind.Len:
            {
                var n = (ulong)idx.NameSpan(e).Length;
                return n >= t.LenLo && n <= t.LenHi;
            }
            case TermKind.Empty:
                return e.IsDir && e.Children == 0 && (e.Attrs & AppConstants.AttrDirectory) != 0;
            case TermKind.Attrib:
                return t.Attrib != 0 && (e.Attrs & t.Attrib) == t.Attrib;
            default:
                return false;
        }
    }

    /// <summary>Rust text_match — default path uses fold pool slices with zero alloc.</summary>
    static bool TextMatch(TextTerm t, FileIndex idx, Entry e)
    {
        var defaultNorm = !t.Case && !t.Diacritics;
        var needle = t.Text.AsSpan();

        // Fast path: name-only, case/diacritic insensitive → FoldSpan (Rust idx.fold(e))
        if (!t.Path && defaultNorm)
            return MatchSpan(t.Kind, idx.FoldSpan(e), needle, t);

        // Path / alternate normalize paths allocate (rare relative to default search).
        string hay;
        if (t.Path)
        {
            hay = defaultNorm
                ? TextNorm.Join(idx.DirFold(e), idx.Fold(e))
                : TextNorm.Normalize(idx.FullPath(e), t.Case, t.Diacritics);
        }
        else
        {
            hay = TextNorm.Normalize(idx.Name(e), t.Case, t.Diacritics);
        }
        return MatchSpan(t.Kind, hay.AsSpan(), needle, t);
    }

    static bool MatchSpan(TextModeKind kind, ReadOnlySpan<char> hay, ReadOnlySpan<char> needle, TextTerm t) =>
        kind switch
        {
            TextModeKind.Contains => hay.Contains(needle, StringComparison.Ordinal),
            TextModeKind.Equals => hay.SequenceEqual(needle),
            TextModeKind.Starts => hay.StartsWith(needle, StringComparison.Ordinal),
            TextModeKind.Ends => hay.EndsWith(needle, StringComparison.Ordinal),
            TextModeKind.Word => WholeWord(hay, needle),
            TextModeKind.Regex => t.Regex!.IsMatch(hay),
            TextModeKind.Glob => GlobChars(t.Text.AsSpan(), hay),
            _ => false
        };

    static bool GlobChars(ReadOnlySpan<char> p, ReadOnlySpan<char> s)
    {
        var pi = 0;
        var si = 0;
        int? starPi = null, starSi = null;
        while (si < s.Length)
        {
            if (pi < p.Length && p[pi] == '?')
            {
                pi++;
                si++;
            }
            else if (pi < p.Length && p[pi] == '*')
            {
                starPi = pi;
                starSi = si;
                pi++;
            }
            else if (pi < p.Length && p[pi] == s[si])
            {
                pi++;
                si++;
            }
            else if (starPi is int sp && starSi is int ss)
            {
                starSi = ss + 1;
                pi = sp + 1;
                si = ss + 1;
            }
            else return false;
        }
        while (pi < p.Length && p[pi] == '*') pi++;
        return pi == p.Length;
    }

    static bool WholeWord(ReadOnlySpan<char> hay, ReadOnlySpan<char> needle)
    {
        if (needle.IsEmpty) return true;
        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
        var start = 0;
        while (true)
        {
            var p = hay[start..].IndexOf(needle, StringComparison.Ordinal);
            if (p < 0) return false;
            p += start;
            var end = p + needle.Length;
            var before = p == 0 || !IsWord(hay[p - 1]);
            var after = end >= hay.Length || !IsWord(hay[end]);
            if (before && after) return true;
            start = p + 1;
        }
    }
}
