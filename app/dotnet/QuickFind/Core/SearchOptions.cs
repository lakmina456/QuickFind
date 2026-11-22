namespace QuickFind.Core;

public sealed class SearchOptions
{
    public bool Case { get; set; }
    public bool WholeWord { get; set; }
    public bool Path { get; set; }
    public bool Diacritics { get; set; }
    public bool Regex { get; set; }

    public SearchOptions Clone() => new()
    {
        Case = Case,
        WholeWord = WholeWord,
        Path = Path,
        Diacritics = Diacritics,
        Regex = Regex
    };
}
