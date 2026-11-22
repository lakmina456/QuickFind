using System.Collections.Concurrent;
using System.IO;
using QuickFind.Core;

namespace QuickFind.Indexing;

/// <summary>
/// Parallel directory walk with mid-scan flushes (Rust scan.rs parity).
/// Entries appear in the live index while scanning so search works immediately.
/// </summary>
public static class DriveScanner
{
    const int FlushAt = 20_000;

    sealed class Shared
    {
        public readonly ConcurrentStack<(string Path, uint Id)> Queue = new();
        public int Pending;
        public int NextDir;
        public readonly Action<Action<FileIndex>> Write;

        public Shared(Action<Action<FileIndex>> write) => Write = write;
        public uint NewDirId() => (uint)Interlocked.Increment(ref NextDir) - 1;
    }

    /// <summary>Scan into a private FileIndex (background refresh / rebuild).</summary>
    public static void ScanInto(FileIndex target, IReadOnlyList<string> roots, ProgressState progress, object writeGate)
    {
        ScanCore(roots, progress, action => { lock (writeGate) action(target); }, seedRoots: true);
    }

    /// <summary>
    /// Scan into the live store with 20k flushes — Rust scan_into(&shared_index) behavior.
    /// Search can return results while indexing is still running.
    /// </summary>
    public static void ScanIntoLive(FileIndexStore store, IReadOnlyList<string> roots, ProgressState progress)
    {
        // Clear live index and seed roots under write lock.
        store.Write(idx =>
        {
            idx.Clear();
            idx.Epoch++; // invalidate old result ids
        });
        ScanCore(roots, progress, store.Write, seedRoots: true);
    }

    static void ScanCore(
        IReadOnlyList<string> roots,
        ProgressState progress,
        Action<Action<FileIndex>> write,
        bool seedRoots)
    {
        progress.Running = true;
        progress.ResetFound();
        var shared = new Shared(write);

        if (seedRoots)
        {
            write(idx =>
            {
                foreach (var r in roots)
                {
                    var id = shared.NewDirId();
                    AppendRoot(idx, r, id);
                    shared.Queue.Push((r, id));
                    Interlocked.Increment(ref shared.Pending);
                }
                idx.Generation++;
                idx.Ready = false;
            });
        }

        var threads = Math.Clamp(Environment.ProcessorCount, 2, 8);
        var workers = new Task[threads];
        for (var t = 0; t < threads; t++)
            workers[t] = Task.Run(() => Worker(shared, progress));
        Task.WaitAll(workers);

        write(idx =>
        {
            idx.FinalizeIndex();
            idx.Ready = true;
        });
        progress.Running = false;
    }

    static void AppendRoot(FileIndex idx, string name, uint selfDir) =>
        idx.Entries.Add(Entry.Create(idx.Names, name, AppConstants.None, selfDir, AppConstants.NoSize, 0, 0, AppConstants.AttrDirectory));

    static void Worker(Shared shared, ProgressState progress)
    {
        var local = new LocalBatch();
        while (true)
        {
            if (!shared.Queue.TryPop(out var item))
            {
                if (Volatile.Read(ref shared.Pending) == 0) break;
                Flush(shared, local);
                Thread.Sleep(1);
                continue;
            }

            local.Dirs.Add((item.Id, item.Path));
            var subdirs = ReadOne(item.Path, item.Id, local, () => shared.NewDirId(), progress);
            if (subdirs.Count > 0)
            {
                Interlocked.Add(ref shared.Pending, subdirs.Count);
                foreach (var s in subdirs) shared.Queue.Push(s);
            }
            Interlocked.Decrement(ref shared.Pending);

            if (local.Entries.Count >= FlushAt)
            {
                progress.Current = item.Path;
                Flush(shared, local);
            }
        }
        Flush(shared, local);
    }

    static void Flush(Shared shared, LocalBatch local)
    {
        if (local.Entries.Count == 0 && local.Dirs.Count == 0) return;
        shared.Write(idx =>
        {
            local.MoveInto(idx);
            idx.Generation++;
        });
    }

    public static List<(string Path, uint Id)> ReadOne(
        string path, uint id, LocalBatch local, Func<uint> newDirId, ProgressState progress)
    {
        var subdirs = new List<(string, uint)>();
        List<NativeDirEnum.Item> items;
        try { items = NativeDirEnum.Read(path); }
        catch { return subdirs; }

        foreach (var info in items)
        {
            var selfDir = AppConstants.None;
            if (info.IsDir)
            {
                selfDir = newDirId();
                var child = TextNorm.Join(path, info.Name);
                if (info.IsReparse)
                    local.Dirs.Add((selfDir, child));
                else
                    subdirs.Add((child, selfDir));
            }

            progress.AddFound();
            var attrs = info.Attrs | (info.IsDir ? AppConstants.AttrDirectory : 0u);
            local.AddEntry(info.Name, id, selfDir, info.Size, info.Mtime, info.Ctime, attrs);
        }
        return subdirs;
    }

    public static (uint Attrs, ulong Mtime, ulong Ctime) MetaTimes(FileSystemInfo info)
    {
        var attrs = (uint)info.Attributes;
        ulong mtime = 0, ctime = 0;
        try { mtime = (ulong)info.LastWriteTimeUtc.ToFileTimeUtc(); } catch { }
        try { ctime = (ulong)info.CreationTimeUtc.ToFileTimeUtc(); } catch { }
        return (attrs, mtime, ctime);
    }

    public sealed class LocalBatch
    {
        public List<Entry> Entries { get; } = [];
        public readonly NamePool Names = new();
        public List<(uint Id, string Path)> Dirs { get; } = [];

        public void AddEntry(string name, uint dir, uint selfDir, ulong size, ulong mtime, ulong ctime, uint attrs) =>
            Entries.Add(Entry.Create(Names, name, dir, selfDir, size, mtime, ctime, attrs));

        public void MoveInto(FileIndex idx)
        {
            foreach (var (id, path) in Dirs)
                idx.SetDir(id, path);
            Dirs.Clear();
            var baseOff = (uint)idx.Names.Length;
            idx.Names.AppendPool(Names);
            Names.Clear();
            for (var i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                e.Rebase(baseOff);
                idx.Entries.Add(e);
            }
            Entries.Clear();
        }
    }
}
