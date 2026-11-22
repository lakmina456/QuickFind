using System.Collections.Concurrent;
using System.IO;
using QuickFind.Core;

namespace QuickFind.Indexing;

/// <summary>FileSystemWatcher-based live updates (Rust notify equivalent).</summary>
public sealed class IndexWatcher : IDisposable
{
    readonly List<FileSystemWatcher> _watchers = [];
    readonly ConcurrentQueue<string> _queue = new();
    readonly CancellationTokenSource _cts = new();
    Thread? _thread;

    public void Start(FileIndexStore store, IReadOnlyList<string> roots)
    {
        var cacheFold = TextNorm.Fold(IndexCache.AppDataDir);
        foreach (var r in roots)
        {
            try
            {
                var w = new FileSystemWatcher(r + "\\")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.CreationTime,
                    InternalBufferSize = 64 * 1024
                };
                void Enqueue(object? _, FileSystemEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.FullPath)) _queue.Enqueue(e.FullPath);
                }
                void Renamed(object? _, RenamedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.OldFullPath)) _queue.Enqueue(e.OldFullPath);
                    if (!string.IsNullOrEmpty(e.FullPath)) _queue.Enqueue(e.FullPath);
                }
                w.Created += Enqueue;
                w.Changed += Enqueue;
                w.Deleted += Enqueue;
                w.Renamed += Renamed;
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch { /* skip unwatchable roots */ }
        }

        _thread = new Thread(() => Loop(store, cacheFold)) { IsBackground = true, Name = "QuickFind.Watcher" };
        _thread.Start();
    }

    void Loop(FileIndexStore store, string cacheFold)
    {
        while (!_cts.IsCancellationRequested)
        {
            if (!_queue.TryDequeue(out var first))
            {
                Thread.Sleep(50);
                continue;
            }

            var batch = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first };
            var deadline = DateTime.UtcNow.AddMilliseconds(400);
            while (DateTime.UtcNow < deadline && batch.Count < 50_000)
            {
                if (_queue.TryDequeue(out var p))
                {
                    batch.Add(p);
                    deadline = DateTime.UtcNow.AddMilliseconds(400);
                }
                else Thread.Sleep(10);
            }

            batch.RemoveWhere(p =>
            {
                var f = TextNorm.Fold(p);
                return !string.IsNullOrEmpty(cacheFold) && f.StartsWith(cacheFold, StringComparison.Ordinal);
            });
            if (batch.Count == 0) continue;

            store.Write(idx =>
            {
                if (!idx.Ready) return;
                Apply(idx, batch);
                idx.Generation++;
            });
        }
    }

    static void Apply(FileIndex idx, HashSet<string> batch)
    {
        var lookup = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (var i = 0; i < idx.Dirs.Count; i++)
        {
            if (!string.IsNullOrEmpty(idx.Dirs[i].Fold))
                lookup[idx.Dirs[i].Fold] = (uint)i;
        }

        var paths = batch.OrderBy(p => p.Length).ToList();
        var targets = new Dictionary<(uint, string), string>();
        foreach (var p in paths)
        {
            if (!Split(p, out var parent, out var name)) continue;
            if (lookup.TryGetValue(TextNorm.Fold(parent), out var pid))
                targets[(pid, TextNorm.Fold(name))] = p;
        }
        if (targets.Count == 0) return;

        var parents = targets.Keys.Select(k => k.Item1).ToHashSet();
        var found = new Dictionary<(uint, string), int>();
        for (var i = 0; i < idx.Entries.Count; i++)
        {
            var e = idx.Entries[i];
            if (e.Deleted || !parents.Contains(e.Dir)) continue;
            var key = (e.Dir, idx.Fold(e));
            if (targets.ContainsKey(key)) found[key] = i;
        }

        foreach (var key in targets.Keys.OrderBy(k => targets[k].Length))
        {
            var path = targets[key];
            found.TryGetValue(key, out var existing);
            var hasExisting = found.ContainsKey(key);
            try
            {
                var info = new FileInfo(path);
                FileSystemInfo fsi = (info.Attributes & FileAttributes.Directory) != 0
                    ? new DirectoryInfo(path)
                    : info;
                // Refresh attributes via GetAttributes for paths that may be dirs
                var attrsFlags = File.GetAttributes(path);
                var isDir = (attrsFlags & FileAttributes.Directory) != 0;
                fsi = isDir ? new DirectoryInfo(path) : new FileInfo(path);
                var (attrs, mtime, ctime) = DriveScanner.MetaTimes(fsi);
                var size = isDir ? AppConstants.NoSize : (ulong)((fsi as FileInfo)?.Length ?? 0);

                if (hasExisting)
                {
                    var e = idx.Entries[existing];
                    var oldSize = e.Size;
                    e.Size = size;
                    e.Mtime = mtime;
                    e.Ctime = ctime;
                    e.Attrs = attrs | (isDir ? AppConstants.AttrDirectory : 0u);
                    idx.Entries[existing] = e;
                    if (!isDir)
                    {
                        var old = oldSize == AppConstants.NoSize ? 0UL : oldSize;
                        var neu = size == AppConstants.NoSize ? 0UL : size;
                        if (neu != old)
                            idx.AdjustFolderSizeDelta(e.Dir, (long)neu - (long)old);
                    }
                    if (Split(path, out _, out var nm) && !idx.NameSpan(e).SequenceEqual(nm.AsSpan()))
                        idx.Rename(existing, nm);
                }
                else
                {
                    AddNew(idx, lookup, key.Item1, path, isDir, size, attrs, mtime, ctime);
                }
            }
            catch
            {
                if (hasExisting) Remove(idx, lookup, existing);
            }
        }
    }

    static void AddNew(FileIndex idx, Dictionary<string, uint> lookup, uint pid, string path, bool isDir, ulong size, uint attrs, ulong mtime, ulong ctime)
    {
        if (!Split(path, out _, out var name)) return;
        var selfDir = AppConstants.None;
        if (isDir)
        {
            selfDir = (uint)idx.Dirs.Count;
            idx.SetDir(selfDir, path);
            lookup[TextNorm.Fold(path)] = selfDir;
        }
        size = isDir ? 0UL : size;
        attrs |= isDir ? AppConstants.AttrDirectory : 0u;
        idx.Entries.Add(Entry.Create(idx.Names, name, pid, selfDir, size, mtime, ctime, attrs));
        BumpChildren(idx, pid, 1);
        if (!isDir && size > 0 && size != AppConstants.NoSize)
            idx.AddSizeToAncestors(pid, size);

        if (!isDir) return;

        idx.Dirs[(int)selfDir].Entry = (uint)(idx.Entries.Count - 1);
        var nextDir = new IntRef { Value = idx.Dirs.Count };
        var progress = new ProgressState();
        var stack = new Stack<(string, uint)>();
        stack.Push((path, selfDir));
        var local = new DriveScanner.LocalBatch();
        while (stack.Count > 0)
        {
            var (p, id) = stack.Pop();
            foreach (var s in DriveScanner.ReadOne(p, id, local, () => (uint)Interlocked.Increment(ref nextDir.Value) - 1, progress))
                stack.Push(s);
            local.Dirs.Add((id, p));
            if (local.Entries.Count > 200_000) break;
        }
        local.Dirs.RemoveAll(d => d.Id == selfDir);
        foreach (var (id, p) in local.Dirs)
            lookup[TextNorm.Fold(p)] = id;
        var start = idx.Entries.Count;
        local.MoveInto(idx);
        for (var i = start; i < idx.Entries.Count; i++)
        {
            var sd = idx.Entries[i].SelfDir;
            if (sd != AppConstants.None && sd < idx.Dirs.Count)
                idx.Dirs[(int)sd].Entry = (uint)i;
        }
        for (var i = start; i < idx.Entries.Count; i++)
            BumpChildren(idx, idx.Entries[i].Dir, 1);
    }

    static void Remove(FileIndex idx, Dictionary<string, uint> lookup, int i)
    {
        var removed = idx.Entries[i];
        removed.Deleted = true;
        idx.Entries[i] = removed;
        var pid = removed.Dir;
        BumpChildren(idx, pid, -1);
        if (!removed.IsDir)
        {
            var sz = removed.Size == AppConstants.NoSize ? 0UL : removed.Size;
            if (sz > 0) idx.AdjustFolderSizeDelta(pid, -(long)sz);
        }
        else
        {
            var folderSize = removed.Size == AppConstants.NoSize ? 0UL : removed.Size;
            if (folderSize > 0) idx.AdjustFolderSizeDelta(pid, -(long)folderSize);
        }
        var sd = removed.SelfDir;
        if (sd == AppConstants.None) return;
        var prefix = idx.Dirs[(int)sd].Fold + "\\";
        var dead = new HashSet<uint> { sd };
        for (var d = 0; d < idx.Dirs.Count; d++)
        {
            if (idx.Dirs[d].Fold.StartsWith(prefix, StringComparison.Ordinal))
                dead.Add((uint)d);
        }
        foreach (var d in dead)
            lookup.Remove(idx.Dirs[(int)d].Fold);
        for (var j = 0; j < idx.Entries.Count; j++)
        {
            var e = idx.Entries[j];
            if (dead.Contains(e.Dir))
            {
                e.Deleted = true;
                idx.Entries[j] = e;
            }
        }
    }

    static void BumpChildren(FileIndex idx, uint dir, int delta)
    {
        if (dir == AppConstants.None) return;
        var ent = dir < idx.Dirs.Count ? idx.Dirs[(int)dir].Entry : AppConstants.None;
        if (ent < idx.Entries.Count)
        {
            var e = idx.Entries[(int)ent];
            e.Children = (uint)Math.Max(0, (int)e.Children + delta);
            idx.Entries[(int)ent] = e;
        }
    }

    static bool Split(string path, out string parent, out string name)
    {
        var p = path.TrimEnd('\\');
        var i = p.LastIndexOf('\\');
        if (i < 0) { parent = ""; name = ""; return false; }
        parent = p[..i];
        name = p[(i + 1)..];
        return true;
    }

    sealed class IntRef { public int Value; }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}
