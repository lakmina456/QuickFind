using System.Runtime.InteropServices;
using QuickFind.Core;

namespace QuickFind.Indexing;

/// <summary>
/// Fills missing file sizes/dates on an existing index without building a second FileIndex.
/// Processes in batches so path strings never all live at once.
/// </summary>
public static class SizeEnricher
{
    const int BatchSize = 25_000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetFileAttributesExW(string name, int infoLevel, out Win32FileAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    struct Win32FileAttributeData
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint FileSizeHigh;
        public uint FileSizeLow;
    }

    public static int EnrichInPlace(FileIndexStore store, ProgressState progress)
    {
        progress.Running = true;
        progress.Current = "Enriching sizes…";

        var total = store.Read(idx => idx.Entries.Count);
        var applied = 0;
        var cursor = 0;

        while (cursor < total)
        {
            var batch = new List<(uint Id, string Path)>(BatchSize);
            store.Read(idx =>
            {
                var n = idx.Entries.Count;
                total = n;
                for (; cursor < n && batch.Count < BatchSize; cursor++)
                {
                    var e = idx.Entries[cursor];
                    if (e.Deleted) continue;
                    // Files: fill missing size/mtime. Folders: fill missing mtime only (size is rolled up).
                    if (e.IsDir)
                    {
                        if (e.Mtime != 0) continue;
                    }
                    else if (e.Size != AppConstants.NoSize && e.Mtime != 0)
                    {
                        continue;
                    }
                    batch.Add(((uint)cursor, idx.FullPath(e)));
                }
            });

            if (batch.Count == 0)
            {
                if (cursor >= total) break;
                continue;
            }

            var updates = new (uint Id, ulong Size, ulong Mtime, ulong Ctime)[batch.Count];
            var updateCount = 0;
            Parallel.For(0, batch.Count, i =>
            {
                var (id, path) = batch[i];
                if (!TryStat(path, out var size, out var mtime, out var ctime)) return;
                var slot = Interlocked.Increment(ref updateCount) - 1;
                updates[slot] = (id, size, mtime, ctime);
            });

            // Drop path strings before write lock.
            batch.Clear();

            // Update sizes without bumping Generation each batch — avoids constant UI re-search.
            store.Write(idx =>
            {
                for (var i = 0; i < updateCount; i++)
                {
                    var (id, size, mtime, ctime) = updates[i];
                    if (id >= idx.Entries.Count) continue;
                    var e = idx.Entries[(int)id];
                    if (e.Deleted) continue;
                    if (!e.IsDir) e.Size = size;
                    if (mtime != 0) e.Mtime = mtime;
                    if (ctime != 0) e.Ctime = ctime;
                    idx.Entries[(int)id] = e;
                    applied++;
                }
            });

            progress.Current = $"Enriching sizes… {Math.Min(cursor, total):N0}/{total:N0}";
        }

        // One generation bump at the end so the UI re-searches once.
        store.Write(idx =>
        {
            idx.RollupFolderSizes();
            idx.Generation++;
            idx.Compact();
        });

        progress.Running = false;
        return applied;
    }

    static bool TryStat(string path, out ulong size, out ulong mtime, out ulong ctime)
    {
        size = 0; mtime = 0; ctime = 0;
        try
        {
            if (!GetFileAttributesExW(path, 0, out var data)) return false;
            var isDir = (data.FileAttributes & 0x10) != 0;
            size = isDir ? 0UL : (((ulong)data.FileSizeHigh << 32) | data.FileSizeLow);
            mtime = ((ulong)data.LastWriteTimeHigh << 32) | data.LastWriteTimeLow;
            ctime = ((ulong)data.CreationTimeHigh << 32) | data.CreationTimeLow;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
