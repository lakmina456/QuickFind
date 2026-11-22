using System.IO;
using System.Text.Json;
using QuickFind.Indexing;

namespace QuickFind.Services;

public sealed class HistoryEntry
{
    public string Query { get; set; } = "";
    public int Filter { get; set; }
    public string? Location { get; set; }
    public string Display { get; set; } = "";
    public long UtcTicks { get; set; }
}

public static class SearchHistoryStore
{
    const int MaxEntries = 50;
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    public static string Path => System.IO.Path.Combine(IndexCache.AppDataDir, "history.json");

    public static List<HistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(Path)) return [];
            return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(Path), JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IReadOnlyList<HistoryEntry> list)
    {
        try
        {
            Directory.CreateDirectory(IndexCache.AppDataDir);
            File.WriteAllText(Path, JsonSerializer.Serialize(list, JsonOpts));
        }
        catch { }
    }

    public static void Record(List<HistoryEntry> list, string query, int filter, string? location, string filterLabel)
    {
        var q = query?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(q) && filter == 0 && string.IsNullOrEmpty(location))
            return;

        list.RemoveAll(e =>
            string.Equals(e.Query, q, StringComparison.OrdinalIgnoreCase) &&
            e.Filter == filter &&
            string.Equals(e.Location ?? "", location ?? "", StringComparison.OrdinalIgnoreCase));

        var display = BuildDisplay(q, filter, location, filterLabel);
        list.Insert(0, new HistoryEntry
        {
            Query = q,
            Filter = filter,
            Location = location,
            Display = display,
            UtcTicks = DateTime.UtcNow.Ticks
        });

        while (list.Count > MaxEntries)
            list.RemoveAt(list.Count - 1);

        Save(list);
    }

    static string BuildDisplay(string query, int filter, string? location, string filterLabel)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query)) parts.Add(query.Trim());
        if (filter != 0) parts.Add(filterLabel);
        if (!string.IsNullOrEmpty(location)) parts.Add($"in {location}");
        return parts.Count == 0 ? "All files" : string.Join(" · ", parts);
    }
}
