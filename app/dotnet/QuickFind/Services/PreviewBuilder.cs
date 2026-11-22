using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickFind.Core;
using QuickFind.Indexing;

namespace QuickFind.Services;

public enum PreviewBodyKind
{
    None,
    Image,
    Text,
    Note
}

public sealed class PreviewInfo
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string KindLabel { get; init; } = "";
    public List<(string Label, string Value)> Rows { get; init; } = [];
    public PreviewBodyKind BodyKind { get; init; }
    public string? Note { get; init; }
    public string? Text { get; init; }
    public ImageSource? Image { get; init; }
}

public static class PreviewBuilder
{
    static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { "png", "jpg", "jpeg", "gif", "bmp", "webp" };
    const ulong MaxImage = 30 * 1024 * 1024;

    public static PreviewInfo? Build(FileIndexStore store, uint id)
    {
        return store.Read(idx =>
        {
            if (id >= idx.Entries.Count) return null;
            var e = idx.Entries[(int)id];
            if (e.Deleted) return null;
            var path = idx.FullPath(e);
            var ext = idx.Extension(e);
            var isDir = e.IsDir;
            var kindLabel = isDir
                ? "File folder"
                : string.IsNullOrEmpty(ext) ? "File" : $"{ext.ToUpperInvariant()} file";
            // Inspector meta matches mockup: Size · Modified · Type · Location
            var rows = new List<(string, string)>();
            if (isDir)
                rows.Add(("Size", $"{e.Children} items"));
            else
                rows.Add(("Size", $"{FormatUtil.SizeText(e.Size, false)} ({e.Size:N0} bytes)"));
            rows.Add(("Modified", DateParser.FormatFileTime(e.Mtime)));
            rows.Add(("Type", kindLabel));
            rows.Add(("Location", idx.DirPath(e)));

            PreviewBodyKind kind;
            string? note = null;
            string? text = null;
            ImageSource? image = null;

            if (isDir)
            {
                kind = PreviewBodyKind.None;
            }
            else if (ImageExts.Contains(ext))
            {
                if (e.Size <= MaxImage && File.Exists(path))
                {
                    image = TryLoadImage(path);
                    kind = image is not null ? PreviewBodyKind.Image : PreviewBodyKind.Note;
                    if (image is null) note = "Could not load picture";
                }
                else
                {
                    kind = PreviewBodyKind.Note;
                    note = "Picture is too large to preview";
                }
            }
            else
            {
                var fk = Filters.KindOf(ext);
                if (fk is FileKind.Audio or FileKind.Video or FileKind.Archive or FileKind.Exe)
                {
                    kind = PreviewBodyKind.Note;
                    note = "No preview for this kind of file";
                }
                else
                {
                    text = SniffText(path);
                    if (text is not null)
                    {
                        kind = PreviewBodyKind.Text;
                    }
                    else
                    {
                        kind = PreviewBodyKind.Note;
                        note = "No preview for this kind of file";
                    }
                }
            }

            return new PreviewInfo
            {
                Name = idx.Name(e),
                Path = path,
                KindLabel = kindLabel,
                Rows = rows,
                BodyKind = kind,
                Note = note,
                Text = text,
                Image = image
            };
        });
    }

    static ImageSource? TryLoadImage(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 480;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    static string? SniffText(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            var buf = new byte[6000];
            var n = fs.Read(buf, 0, buf.Length);
            if (n == 0) return "";
            if (buf.AsSpan(0, n).Contains((byte)0)) return null;
            var s = System.Text.Encoding.UTF8.GetString(buf, 0, n);
            var total = s.Length;
            var bad = s.Count(c => c == '\uFFFD');
            if (total > 0 && bad * 20 > total) return null;
            var lines = s.Replace("\r\n", "\n").Split('\n');
            var take = Math.Min(lines.Length, 60);
            var outText = string.Join('\n', lines.Take(take));
            if (outText.Length > 2500) outText = outText[..2500];
            return outText;
        }
        catch
        {
            return null;
        }
    }
}
