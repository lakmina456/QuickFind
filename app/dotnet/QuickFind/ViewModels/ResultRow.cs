using System.Windows.Media;
using QuickFind.Core;
using QuickFind.Services;

namespace QuickFind.ViewModels;

public sealed class ResultRow
{
    public uint Id { get; init; }
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string Size { get; init; } = "";
    public string Extension { get; init; } = "";
    public string Modified { get; init; } = "";
    public string Created { get; init; } = "";
    public string Attributes { get; init; } = "";
    public bool IsDirectory { get; init; }
    public FileKind Kind { get; init; }

    /// <summary>Windows shell icon (Explorer), cached by extension.</summary>
    public ImageSource Icon => ShellIconCache.GetForPath(FullPath, IsDirectory, Extension, Kind);

    /// <summary>Badge category key used by styles: pictures, documents, audio, video, archives, apps, folders, other.</summary>
    public string BadgeCategory
    {
        get
        {
            if (IsDirectory) return "folders";
            return Kind switch
            {
                FileKind.Picture => "pictures",
                FileKind.Document => "documents",
                FileKind.Audio => "audio",
                FileKind.Video => "video",
                FileKind.Archive => "archives",
                FileKind.Exe => "apps",
                _ => "other"
            };
        }
    }

    /// <summary>Short extension badge for the name column (matches mockup chips).</summary>
    public string BadgeText
    {
        get
        {
            if (IsDirectory) return "";
            if (string.IsNullOrWhiteSpace(Extension)) return "FILE";
            var e = Extension.TrimStart('.').ToUpperInvariant();
            return e.Length <= 4 ? e : e[..4];
        }
    }

    public bool IsFolderBadge => IsDirectory;
}
