namespace QuickFind.Core;

public static class TextNorm
{
    public static string Fold(string s) => Fold(s.AsSpan());

    public static string Fold(ReadOnlySpan<char> s)
    {
        Span<char> chars = s.Length <= 512 ? stackalloc char[s.Length * 2] : new char[s.Length * 2];
        var n = FoldInto(s, chars);
        return new string(chars[..n]);
    }

    /// <summary>Write folded form into dest. Returns written length. No heap alloc for ASCII.</summary>
    public static int FoldInto(ReadOnlySpan<char> s, Span<char> dest)
    {
        var n = 0;
        foreach (var c in s)
        {
            if (c < 128)
            {
                dest[n++] = (char)(c >= 'A' && c <= 'Z' ? c + 32 : c);
            }
            else
            {
                foreach (var l in char.ToLowerInvariant(c).ToString())
                    dest[n++] = StripAccent(l);
            }
        }
        return n;
    }

    /// <summary>Append folded name into pool. Returns (off,len). Reuses name slice when already folded.</summary>
    public static (uint Off, ushort Len) AppendFolded(NamePool names, uint nameOff, ushort nameLen, ReadOnlySpan<char> name)
    {
        // Fast ASCII path: detect if already lowercase-folded without alloc.
        var needsFold = false;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c >= 'A' && c <= 'Z') { needsFold = true; break; }
            if (c >= 128) { needsFold = true; break; }
        }
        if (!needsFold)
            return (nameOff, nameLen);

        Span<char> buf = name.Length <= 512 ? stackalloc char[name.Length * 2] : new char[name.Length * 2];
        var n = FoldInto(name, buf);
        if (n > ushort.MaxValue) n = ushort.MaxValue;
        var off = names.Append(buf[..n]);
        return (off, (ushort)n);
    }

    public static string StripAccents(string s)
    {
        return string.Create(s.Length, s, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                if (c < 128)
                {
                    span[i] = c;
                }
                else if (char.IsUpper(c))
                {
                    var lower = char.ToLowerInvariant(c);
                    var stripped = StripAccent(lower);
                    span[i] = char.ToUpperInvariant(stripped);
                }
                else
                {
                    span[i] = StripAccent(c);
                }
            }
        });
    }

    public static char StripAccent(char c) => c switch
    {
        'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' or 'ā' or 'ă' or 'ą' => 'a',
        'ç' or 'ć' or 'ĉ' or 'ċ' or 'č' => 'c',
        'ď' or 'đ' => 'd',
        'è' or 'é' or 'ê' or 'ë' or 'ē' or 'ĕ' or 'ė' or 'ę' or 'ě' => 'e',
        'ĝ' or 'ğ' or 'ġ' or 'ģ' => 'g',
        'ĥ' or 'ħ' => 'h',
        'ì' or 'í' or 'î' or 'ï' or 'ĩ' or 'ī' or 'ĭ' or 'į' or 'ı' => 'i',
        'ĵ' => 'j',
        'ķ' => 'k',
        'ĺ' or 'ļ' or 'ľ' or 'ŀ' or 'ł' => 'l',
        'ñ' or 'ń' or 'ņ' or 'ň' => 'n',
        'ò' or 'ó' or 'ô' or 'õ' or 'ö' or 'ø' or 'ō' or 'ŏ' or 'ő' => 'o',
        'ŕ' or 'ŗ' or 'ř' => 'r',
        'ś' or 'ŝ' or 'ş' or 'š' => 's',
        'ţ' or 'ť' or 'ŧ' => 't',
        'ù' or 'ú' or 'û' or 'ü' or 'ũ' or 'ū' or 'ŭ' or 'ů' or 'ű' or 'ų' => 'u',
        'ŵ' => 'w',
        'ý' or 'ÿ' or 'ŷ' => 'y',
        'ź' or 'ż' or 'ž' => 'z',
        _ => c
    };

    public static string Normalize(string s, bool caseSensitive, bool diacritics) => (caseSensitive, diacritics) switch
    {
        (false, false) => Fold(s),
        (true, false) => StripAccents(s),
        (false, true) => s.ToLowerInvariant(),
        (true, true) => s
    };

    public static string Join(string dir, string name)
    {
        if (string.IsNullOrEmpty(dir)) return name;
        return dir.EndsWith('\\') ? dir + name : dir + "\\" + name;
    }

    public static string ExtensionOf(string name)
    {
        var p = name.LastIndexOf('.');
        return p > 0 ? name[(p + 1)..] : "";
    }
}
