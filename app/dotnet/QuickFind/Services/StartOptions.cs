using QuickFind.Core;

namespace QuickFind.Services;

public sealed class StartOptions
{
    public string? Search { get; set; }
    public string? Filter { get; set; }
    public string? Bookmark { get; set; }
    public bool? Case { get; set; }
    public bool? Diacritics { get; set; }
    public bool? Path { get; set; }
    public bool? WholeWord { get; set; }
    public bool? Regex { get; set; }
    public SortKey? Sort { get; set; }
    public bool? Ascending { get; set; }

    public static StartOptions FromArgs(string[] args)
    {
        var o = new StartOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i].ToLowerInvariant();
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (a)
            {
                case "-s" or "-search": o.Search = Next(); break;
                case "-filter": o.Filter = Next(); break;
                case "-bookmark": o.Bookmark = Next(); break;
                case "-sort":
                    o.Sort = (Next()?.ToLowerInvariant().Replace("_", "").Replace(" ", "")) switch
                    {
                        "name" => SortKey.Name,
                        "path" => SortKey.Path,
                        "size" => SortKey.Size,
                        "extension" or "ext" or "type" => SortKey.Extension,
                        "datemodified" or "dm" => SortKey.Modified,
                        "datecreated" or "dc" => SortKey.Created,
                        "attributes" => SortKey.Attributes,
                        _ => null
                    };
                    break;
                case "-sort-ascending": o.Ascending = true; break;
                case "-sort-descending": o.Ascending = false; break;
                case "-case": o.Case = true; break;
                case "-nocase": o.Case = false; break;
                case "-diacritics": o.Diacritics = true; break;
                case "-nodiacritics": o.Diacritics = false; break;
                case "-matchpath": o.Path = true; break;
                case "-nomatchpath": o.Path = false; break;
                case "-ww" or "-wholeword": o.WholeWord = true; break;
                case "-noww" or "-nowholeword": o.WholeWord = false; break;
                case "-regex": o.Regex = true; break;
                case "-noregex": o.Regex = false; break;
            }
        }
        return o;
    }
}
