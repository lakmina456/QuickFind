using System.IO;
using QuickFind.Indexing;

namespace QuickFind.Services;

public sealed class Bookmark
{
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";
    public int Filter { get; set; }
}

public static class BookmarkStore
{
    public static List<Bookmark> Load()
    {
        try
        {
            if (!File.Exists(IndexCache.BookmarksPath)) return [];
            return File.ReadAllLines(IndexCache.BookmarksPath)
                .Select(l =>
                {
                    var parts = l.Split('\t', 3, StringSplitOptions.None);
                    if (parts.Length < 2) return null;
                    if (!int.TryParse(parts[0], out var filter)) return null;
                    return new Bookmark
                    {
                        Filter = filter,
                        Name = parts[1],
                        Query = parts.Length > 2 ? parts[2] : ""
                    };
                })
                .Where(b => b is not null)
                .Cast<Bookmark>()
                .ToList();
        }
        catch { return []; }
    }

    public static void Save(IReadOnlyList<Bookmark> list)
    {
        try
        {
            Directory.CreateDirectory(IndexCache.AppDataDir);
            var text = string.Concat(list.Select(b =>
                $"{b.Filter}\t{b.Name.Replace('\t', ' ')}\t{b.Query.Replace('\t', ' ').Replace('\n', ' ')}\n"));
            File.WriteAllText(IndexCache.BookmarksPath, text);
        }
        catch { }
    }

    public static void Add(List<Bookmark> list, string query, int filter, string filterName)
    {
        if (string.IsNullOrWhiteSpace(query) && filter == 0) return;
        if (list.Any(b => b.Query == query && b.Filter == filter)) return;
        var name = (string.IsNullOrWhiteSpace(query), filter) switch
        {
            (true, _) => filterName,
            (false, 0) => query.Trim(),
            _ => $"{query.Trim()} ({filterName})"
        };
        list.Add(new Bookmark { Name = name, Query = query, Filter = filter });
        Save(list);
    }
}
