using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickFind.Views;

namespace QuickFind.Services;

/// <summary>Windows shell icons (Explorer) cached by extension / directory / path.</summary>
public static class ShellIconCache
{
    static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    const uint FileAttributeDirectory = 0x10;
    const uint FileAttributeNormal = 0x80;
    const uint ShgfiIcon = 0x000000100;
    const uint ShgfiSmallIcon = 0x000000001;
    const uint ShgfiLargeIcon = 0x000000000;
    const uint ShgfiUseFileAttributes = 0x000000010;
    const uint ShgfiSysIconIndex = 0x000004000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShFileInfo
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern nint SHGetFileInfoW(
        string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(nint hIcon);

    /// <summary>Fast path for result rows — Windows shell icon by extension (or folder).</summary>
    public static ImageSource Get(string extension, bool isDirectory, Core.FileKind kind = Core.FileKind.Other)
    {
        var key = isDirectory
            ? "<dir>"
            : string.IsNullOrWhiteSpace(extension)
                ? "<file>"
                : "." + extension.TrimStart('.').ToLowerInvariant();

        return Cache.GetOrAdd(key, _ =>
            LoadByAttributes(key, isDirectory) ?? FileIcons.Get(isDirectory, kind));
    }

    /// <summary>Real path icon (exe / lnk / custom). Falls back to extension icon.</summary>
    public static ImageSource GetForPath(string fullPath, bool isDirectory, string extension, Core.FileKind kind)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return Get(extension, isDirectory, kind);

        // Path-specific icons for executables and shortcuts only (keeps cache small).
        var ext = extension.TrimStart('.').ToLowerInvariant();
        if (ext is "exe" or "lnk" or "ico" or "url" or "msc" or "cpl")
        {
            var pathKey = "<path>:" + fullPath.ToLowerInvariant();
            if (Cache.TryGetValue(pathKey, out var hit)) return hit;
            var fromPath = LoadFromPath(fullPath, isDirectory);
            if (fromPath is not null)
            {
                Cache[pathKey] = fromPath;
                return fromPath;
            }
        }

        return Get(extension, isDirectory, kind);
    }

    static ImageSource? LoadByAttributes(string key, bool isDirectory)
    {
        try
        {
            // Dummy path — SHGFI_USEFILEATTRIBUTES asks the shell for the type icon.
            string probe;
            uint attrs;
            if (isDirectory)
            {
                probe = "folder";
                attrs = FileAttributeDirectory;
            }
            else if (key is "<file>")
            {
                probe = "file";
                attrs = FileAttributeNormal;
            }
            else
            {
                probe = "file" + key; // e.g. file.pdf
                attrs = FileAttributeNormal;
            }

            return ExtractIcon(probe, attrs, ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes | ShgfiSysIconIndex);
        }
        catch
        {
            return null;
        }
    }

    static ImageSource? LoadFromPath(string path, bool isDirectory)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return null;
            var attrs = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
            return ExtractIcon(path, attrs, ShgfiIcon | ShgfiSmallIcon);
        }
        catch
        {
            return null;
        }
    }

    static ImageSource? ExtractIcon(string path, uint attrs, uint flags)
    {
        var info = new ShFileInfo();
        var r = SHGetFileInfoW(path, attrs, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags);
        if (r == 0 || info.hIcon == nint.Zero) return null;

        try
        {
            var bmp = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}
