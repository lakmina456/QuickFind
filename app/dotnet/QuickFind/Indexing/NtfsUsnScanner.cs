using System.Runtime.InteropServices;
using System.Text;
using QuickFind.Core;

namespace QuickFind.Indexing;

/// <summary>
/// Fast volume enumeration via FSCTL_ENUM_USN_DATA (NTFS file table).
/// Names go straight into the index NamePool — no per-file string dictionary.
/// </summary>
public static class NtfsUsnScanner
{
    const uint GenericRead = 0x80000000;
    const uint FileShareRead = 0x1;
    const uint FileShareWrite = 0x2;
    const uint OpenExisting = 3;
    const uint FileFlagBackupSemantics = 0x02000000;
    const uint FsctlEnumUsnData = 0x000900b3;
    const uint FileAttributeDirectory = 0x10;
    const uint FileAttributeReparse = 0x400;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern nint CreateFileW(string name, uint access, uint share, nint sec, uint disp, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(nint h);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(
        nint h, uint code,
        ref MftEnumData input, int inputSize,
        byte[] output, int outputSize,
        out int bytesReturned, nint overlapped);

    [StructLayout(LayoutKind.Sequential)]
    struct MftEnumData
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    /// <summary>Compact MFT node — name lives in FileIndex.Names, not a heap string.</summary>
    struct Node
    {
        public ulong Parent;
        public uint NameOff;
        public ushort NameLen;
        public uint Attrs;
        public ulong TimeStamp; // USN_RECORD TimeStamp (FILETIME)
    }

    public static bool TryScanVolume(string root, FileIndex idx, ProgressState progress, out string? error)
    {
        error = null;
        var volumePath = @"\\.\" + root.TrimEnd('\\');
        var handle = CreateFileW(volumePath, GenericRead, FileShareRead | FileShareWrite, 0, OpenExisting, FileFlagBackupSemantics, 0);
        if (handle == nint.Zero || handle == new nint(-1))
        {
            error = $"Cannot open {root} (admin required for NTFS index)";
            return false;
        }

        try
        {
            var nodes = new Dictionary<ulong, Node>(256_000);
            var enumData = new MftEnumData { StartFileReferenceNumber = 0, LowUsn = 0, HighUsn = long.MaxValue };
            var buffer = new byte[1 << 20]; // 1 MB
            var nameBuf = new char[512];

            while (true)
            {
                if (!DeviceIoControl(handle, FsctlEnumUsnData, ref enumData, Marshal.SizeOf<MftEnumData>(),
                        buffer, buffer.Length, out var returned, 0))
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err == 38 /* ERROR_HANDLE_EOF */) break;
                    error = $"USN enum failed on {root} (error {err})";
                    return false;
                }
                if (returned <= 8) break;

                enumData.StartFileReferenceNumber = BitConverter.ToUInt64(buffer, 0);
                var offset = 8;
                while (offset + 60 <= returned)
                {
                    var recordLength = BitConverter.ToUInt32(buffer, offset);
                    if (recordLength < 60 || offset + (int)recordLength > returned) break;

                    var frn = BitConverter.ToUInt64(buffer, offset + 8);
                    var parent = BitConverter.ToUInt64(buffer, offset + 16);
                    // USN_RECORD_V2.TimeStamp at +32 (FILETIME) — used for Modified when walk metadata is missing.
                    var timeStamp = BitConverter.ToUInt64(buffer, offset + 32);
                    var attrs = BitConverter.ToUInt32(buffer, offset + 52);
                    var nameLenBytes = BitConverter.ToUInt16(buffer, offset + 56);
                    var nameOff = BitConverter.ToUInt16(buffer, offset + 58);
                    if (nameLenBytes == 0 || offset + nameOff + nameLenBytes > returned)
                    {
                        offset += (int)recordLength;
                        continue;
                    }

                    var charCount = nameLenBytes / 2;
                    if (charCount == 0)
                    {
                        offset += (int)recordLength;
                        continue;
                    }
                    if (charCount > nameBuf.Length)
                        nameBuf = new char[charCount];
                    Encoding.Unicode.GetChars(buffer, offset + nameOff, nameLenBytes, nameBuf, 0);
                    var nameSpan = nameBuf.AsSpan(0, charCount);
                    if (nameSpan is "." or "..")
                    {
                        offset += (int)recordLength;
                        continue;
                    }

                    var poolOff = idx.Names.Append(nameSpan);
                    nodes[frn] = new Node
                    {
                        Parent = parent,
                        NameOff = poolOff,
                        NameLen = (ushort)Math.Min(charCount, ushort.MaxValue),
                        Attrs = attrs,
                        TimeStamp = timeStamp
                    };
                    progress.AddFound(1);
                    offset += (int)recordLength;
                }

                if (returned < buffer.Length) break;
            }

            BuildIndex(root, nodes, idx, progress);
            return true;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    static void BuildIndex(string root, Dictionary<ulong, Node> nodes, FileIndex idx, ProgressState progress)
    {
        progress.Current = root;
        // Only directories get cached paths — not every file FRN.
        var dirPaths = new Dictionary<ulong, string>(Math.Max(1024, nodes.Count / 8));
        var dirIds = new Dictionary<ulong, uint>(Math.Max(1024, nodes.Count / 8));

        var rootDirId = (uint)idx.Dirs.Count;
        idx.SetDir(rootDirId, root);
        idx.Entries.Add(Entry.Create(idx.Names, root, AppConstants.None, rootDirId, 0, 0, 0, AppConstants.AttrDirectory));
        dirIds[0] = rootDirId;
        dirPaths[0] = root;

        string ResolveDirPath(ulong frn)
        {
            if (dirPaths.TryGetValue(frn, out var cached)) return cached;
            if (!nodes.TryGetValue(frn, out var node))
            {
                dirPaths[frn] = root;
                return root;
            }
            var name = idx.Names.GetString(node.NameOff, node.NameLen);
            if (!nodes.ContainsKey(node.Parent) || node.Parent == frn)
            {
                var p = TextNorm.Join(root, name);
                dirPaths[frn] = p;
                return p;
            }
            var parentPath = ResolveDirPath(node.Parent);
            var full = TextNorm.Join(parentPath, name);
            dirPaths[frn] = full;
            return full;
        }

        uint EnsureDir(ulong frn, string path)
        {
            if (dirIds.TryGetValue(frn, out var id)) return id;
            id = (uint)idx.Dirs.Count;
            idx.SetDir(id, path);
            dirIds[frn] = id;
            dirPaths[frn] = path;
            return id;
        }

        foreach (var (frn, node) in nodes)
        {
            var isDir = (node.Attrs & FileAttributeDirectory) != 0;
            string parentPath;
            uint parentDirId;

            if (!nodes.ContainsKey(node.Parent) || node.Parent == frn)
            {
                parentPath = root;
                parentDirId = rootDirId;
            }
            else
            {
                parentPath = ResolveDirPath(node.Parent);
                parentDirId = EnsureDir(node.Parent, parentPath);
            }

            var nameSpan = idx.Names.Slice(node.NameOff, node.NameLen);
            var selfDir = AppConstants.None;
            if (isDir)
            {
                var full = TextNorm.Join(parentPath, idx.Names.GetString(node.NameOff, node.NameLen));
                selfDir = EnsureDir(frn, full);
            }

            var attrs = node.Attrs;
            if (isDir) attrs |= AppConstants.AttrDirectory;
            if ((node.Attrs & FileAttributeReparse) != 0) attrs |= AppConstants.AttrReparse;

            // Reuse the name already in the pool (avoid a second Append + fold copy when possible).
            var (foldOff, foldLen) = TextNorm.AppendFolded(idx.Names, node.NameOff, node.NameLen, nameSpan);
            var ts = DateParser.IsValidFileTime(node.TimeStamp) ? node.TimeStamp : 0UL;
            idx.Entries.Add(new Entry
            {
                NameOff = node.NameOff,
                NameLen = node.NameLen,
                FoldOff = foldOff,
                FoldLen = foldLen,
                Dir = parentDirId,
                SelfDir = selfDir,
                Size = isDir ? 0UL : AppConstants.NoSize,
                Mtime = ts,
                Ctime = ts,
                Attrs = attrs
            });
        }

        dirPaths.Clear();
    }

    public static bool IsAdmin()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(id);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
