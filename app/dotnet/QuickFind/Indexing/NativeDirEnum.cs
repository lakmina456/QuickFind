using System.IO;
using System.Runtime.InteropServices;
using QuickFind.Core;

namespace QuickFind.Indexing;

/// <summary>
/// Win32 FindFirstFileEx/FindNextFile — same style of OS enumeration Rust's read_dir uses.
/// Avoids DirectoryInfo.GetFileSystemInfos() which allocates managed objects per entry.
/// </summary>
static class NativeDirEnum
{
    const int FindFirstExLargeFetch = 2;
    const int FindExInfoBasic = 1;
    const int FindExSearchNameMatch = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Win32FindData
    {
        public uint DwFileAttributes;
        public long FtCreationTime;
        public long FtLastAccessTime;
        public long FtLastWriteTime;
        public uint NFileSizeHigh;
        public uint NFileSizeLow;
        public uint DwReserved0;
        public uint DwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string CFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string CAlternateFileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern nint FindFirstFileExW(
        string lpFileName,
        int fInfoLevelId,
        out Win32FindData lpFindFileData,
        int fSearchOp,
        nint lpSearchFilter,
        int dwAdditionalFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool FindNextFileW(nint hFindFile, out Win32FindData lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FindClose(nint hFindFile);

    public readonly struct Item
    {
        public readonly string Name;
        public readonly uint Attrs;
        public readonly ulong Size;
        public readonly ulong Mtime;
        public readonly ulong Ctime;
        public readonly bool IsDir;
        public readonly bool IsReparse;

        public Item(string name, uint attrs, ulong size, ulong mtime, ulong ctime, bool isDir, bool isReparse)
        {
            Name = name;
            Attrs = attrs;
            Size = size;
            Mtime = mtime;
            Ctime = ctime;
            IsDir = isDir;
            IsReparse = isReparse;
        }
    }

    public static List<Item> Read(string path)
    {
        var pattern = path.EndsWith(':') || path.EndsWith('\\')
            ? path.TrimEnd('\\') + "\\*"
            : path + "\\*";

        var handle = FindFirstFileExW(
            pattern,
            FindExInfoBasic,
            out var data,
            FindExSearchNameMatch,
            0,
            FindFirstExLargeFetch);

        if (handle == nint.Zero || handle == -1)
            return [];

        var items = new List<Item>(64);
        try
        {
            do
            {
                var name = data.CFileName;
                if (name is "." or ".." or null) continue;

                var attrs = data.DwFileAttributes;
                var isDir = (attrs & (uint)FileAttributes.Directory) != 0;
                var isReparse = (attrs & (uint)FileAttributes.ReparsePoint) != 0;
                var size = isDir
                    ? AppConstants.NoSize
                    : ((ulong)data.NFileSizeHigh << 32) | data.NFileSizeLow;
                var mtime = unchecked((ulong)data.FtLastWriteTime);
                var ctime = unchecked((ulong)data.FtCreationTime);
                items.Add(new Item(name, attrs, size, mtime, ctime, isDir, isReparse));
            }
            while (FindNextFileW(handle, out data));
        }
        finally
        {
            FindClose(handle);
        }
        return items;
    }
}
