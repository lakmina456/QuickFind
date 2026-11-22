using QuickFind.Core;
using QuickFind.Services;

namespace QuickFind.Indexing;

/// <summary>
/// Prefers NTFS USN file-table enumeration when allowed/admin; falls back to directory walk.
/// Walk enrich updates sizes in place — never builds a second full FileIndex.
/// </summary>
public sealed class HybridIndexSource : IIndexSource, IDisposable
{
    IndexWatcher? _watcher;
    readonly object _scanGate = new();
    CancellationTokenSource? _loopCts;
    IndexOptions _opts;
    int _enriching;

    public string LastMode { get; private set; } = "Walk";

    public HybridIndexSource(IndexOptions opts) => _opts = opts;

    public void UpdateOptions(IndexOptions opts) => _opts = opts;

    public void Start(FileIndexStore store, ProgressState progress, CancellationToken ct)
    {
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _loopCts.Token;
        Task.Run(() => StartupLoop(store, progress, token), token);
    }

    public void Rebuild(FileIndexStore store, ProgressState progress, CancellationToken ct)
    {
        if (progress.Running) return;
        Task.Run(() =>
        {
            var roots = Roots();
            Refresh(store, progress, roots);
            RestartWatcher(store, roots);
            TrySave(store);
        }, ct);
    }

    /// <summary>Re-scan with current drive flags and reattach watchers (USB/network toggles).</summary>
    public void RefreshRoots(FileIndexStore store, ProgressState progress, CancellationToken ct)
    {
        if (progress.Running) return;
        Task.Run(() =>
        {
            var roots = Roots();
            if (roots.Count == 0)
                roots = DriveEnumerator.SelectedRoots(true, false, false);
            Refresh(store, progress, roots);
            RestartWatcher(store, roots);
            TrySave(store);
            store.Read(idx => idx.Compact());
        }, ct);
    }

    void RestartWatcher(FileIndexStore store, List<string> roots)
    {
        _watcher?.Dispose();
        _watcher = new IndexWatcher();
        _watcher.Start(store, roots);
    }

    List<string> Roots() =>
        DriveEnumerator.SelectedRoots(_opts.IndexFixed, _opts.IndexRemovable, _opts.IndexNetwork);

    void StartupLoop(FileIndexStore store, ProgressState progress, CancellationToken ct)
    {
        var roots = Roots();
        if (roots.Count == 0)
            roots = DriveEnumerator.SelectedRoots(true, false, false);

        var loaded = IndexCache.Load();
        if (loaded is not null && loaded.Entries.Count > 0)
        {
            // Keep the cached index — do NOT rebuild a second full catalog on every launch.
            loaded.Compact();
            store.Replace(loaded);
            LastMode = "Cache";
            if (NeedsSizeEnrich(store))
                ScheduleWalkEnrich(store, progress);
        }
        else
        {
            ColdStart(store, progress, roots);
        }

        TrySave(store);
        store.Read(idx => idx.Compact());

        _watcher?.Dispose();
        _watcher = new IndexWatcher();
        _watcher.Start(store, roots);

        var saved = store.Read(i => i.Generation);
        while (!ct.IsCancellationRequested)
        {
            try { Task.Delay(TimeSpan.FromSeconds(600), ct).Wait(ct); }
            catch (OperationCanceledException) { break; }

            if (progress.Running) continue;
            store.Read(idx =>
            {
                if (idx.Generation != saved)
                {
                    try { IndexCache.Save(idx); saved = idx.Generation; } catch { }
                }
            });
            // Periodic tombstone compaction when soft-deletes pile up.
            store.Write(idx =>
            {
                if (CompactTombstones(idx))
                {
                    idx.Compact();
                    idx.Generation++;
                }
            });
        }
    }

    void ColdStart(FileIndexStore store, ProgressState progress, List<string> roots)
    {
        if (_opts.PreferNtfs && TryNtfsIntoLive(store, progress, roots))
        {
            ScheduleWalkEnrich(store, progress);
            return;
        }

        LastMode = "Walk";
        DriveScanner.ScanIntoLive(store, roots, progress);
        store.Read(idx => idx.Compact());
    }

    void Refresh(FileIndexStore store, ProgressState progress, IReadOnlyList<string> roots)
    {
        var fresh = new FileIndex();
        if (_opts.PreferNtfs && TryNtfsInto(fresh, progress, roots))
        {
            fresh.Compact();
            store.Replace(fresh);
            ScheduleWalkEnrich(store, progress);
            return;
        }

        LastMode = "Walk";
        DriveScanner.ScanInto(fresh, roots, progress, _scanGate);
        fresh.Compact();
        store.Replace(fresh);
    }

    void ScheduleWalkEnrich(FileIndexStore store, ProgressState progress)
    {
        if (Interlocked.CompareExchange(ref _enriching, 1, 0) != 0) return;
        Task.Run(() =>
        {
            try
            {
                var n = SizeEnricher.EnrichInPlace(store, progress);
                TrySave(store);
                LastMode = n > 0 ? "NTFS+Enrich" : LastMode;
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _enriching, 0);
            }
        });
    }

    static bool NeedsSizeEnrich(FileIndexStore store)
    {
        var missing = 0;
        store.Read(idx =>
        {
            var n = Math.Min(idx.Entries.Count, 50_000);
            for (var i = 0; i < n; i++)
            {
                var e = idx.Entries[i];
                if (!e.Deleted && !e.IsDir && e.Size == AppConstants.NoSize)
                {
                    missing++;
                    if (missing > 100) return;
                }
            }
        });
        return missing > 100;
    }

    /// <summary>Strip soft-deleted entries when they exceed 8% of the list.</summary>
    static bool CompactTombstones(FileIndex idx)
    {
        var dead = 0;
        foreach (var e in idx.Entries)
            if (e.Deleted) dead++;
        if (dead < 10_000 || dead * 12 < idx.Entries.Count) return false;

        var live = new List<Entry>(idx.Entries.Count - dead);
        foreach (var e in idx.Entries)
            if (!e.Deleted) live.Add(e);
        idx.Entries.Clear();
        idx.Entries.AddRange(live);
        idx.FinalizeIndex();
        return true;
    }

    bool TryNtfsIntoLive(FileIndexStore store, ProgressState progress, IReadOnlyList<string> roots)
    {
        var fresh = new FileIndex();
        if (!TryNtfsInto(fresh, progress, roots))
        {
            LastMode = NtfsUsnScanner.IsAdmin() ? "Walk" : "Walk (no admin)";
            return false;
        }
        fresh.Compact();
        store.Replace(fresh);
        return true;
    }

    bool TryNtfsInto(FileIndex target, ProgressState progress, IReadOnlyList<string> roots)
    {
        if (!NtfsUsnScanner.IsAdmin()) return false;

        progress.Running = true;
        progress.ResetFound();
        var okAny = false;
        lock (_scanGate)
        {
            foreach (var root in roots)
            {
                progress.Current = root;
                if (NtfsUsnScanner.TryScanVolume(root, target, progress, out _))
                    okAny = true;
            }
            if (okAny)
            {
                target.FinalizeIndex();
                target.Ready = true;
                target.Generation++;
                target.Compact();
            }
        }
        progress.Running = false;
        if (okAny) LastMode = "NTFS";
        return okAny;
    }

    static void TrySave(FileIndexStore store) =>
        store.Read(idx => { try { IndexCache.Save(idx); } catch { } });

    public void Dispose()
    {
        _loopCts?.Cancel();
        _watcher?.Dispose();
    }
}
