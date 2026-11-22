using System.IO;
using System.Text;
using QuickFind.Core;

namespace QuickFind.Services;

/// <summary>On-demand file-content scan (not indexed). Hard limits keep it snappy.</summary>
public static class ContentSearch
{
    const long MaxFileBytes = 2 * 1024 * 1024; // 2 MB
    const int MaxScanFiles = 2_000;
    static readonly HashSet<string> AllowExt = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "cs", "js", "ts", "tsx", "jsx", "json", "xml", "html", "htm", "css", "scss",
        "py", "rb", "go", "rs", "java", "kt", "swift", "c", "cpp", "h", "hpp", "sql", "yml", "yaml",
        "toml", "ini", "cfg", "conf", "log", "csv", "tsv", "bat", "ps1", "sh", "gitignore", "env"
    };

    public static List<uint> Filter(FileIndex idx, List<uint> ids, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle)) return ids;
        var n = needle.Trim();
        var result = new List<uint>();
        var scanned = 0;
        foreach (var id in ids)
        {
            if (scanned >= MaxScanFiles) break;
            if (id >= idx.Entries.Count) continue;
            var e = idx.Entries[(int)id];
            if (e.Deleted || e.IsDir) continue;
            var ext = idx.Extension(e);
            if (ext.Length > 0 && !AllowExt.Contains(ext)) continue;
            if (e.Size > (ulong)MaxFileBytes && e.Size != AppConstants.NoSize) continue;

            var path = idx.FullPath(e);
            scanned++;
            if (FileContains(path, n))
                result.Add(id);
        }
        return result;
    }

    static bool FileContains(string path, string needle)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);
            var buf = new char[64 * 1024];
            var carry = "";
            int read;
            while ((read = reader.Read(buf, 0, buf.Length)) > 0)
            {
                var chunk = carry + new string(buf, 0, read);
                if (chunk.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
                carry = chunk.Length > needle.Length
                    ? chunk[(chunk.Length - needle.Length)..]
                    : chunk;
            }
        }
        catch { }
        return false;
    }
}
