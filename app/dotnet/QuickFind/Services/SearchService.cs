using System.Collections.Concurrent;
using QuickFind.Core;
using QuickFind.Indexing;
using QuickFind.Query;

namespace QuickFind.Services;

public sealed class SearchRequest
{
    public string Text { get; set; } = "";
    public SearchOptions Options { get; set; } = new();
    public SortKey Sort { get; set; }
    public bool Ascending { get; set; } = true;
    public ulong Generation { get; set; }
    public ulong Epoch { get; set; }
    public ulong Serial { get; set; }
}

public sealed class SearchReply
{
    public ulong Serial { get; set; }
    public ulong Epoch { get; set; }
    public List<uint> Ids { get; set; } = [];
    public int MatchTotal { get; set; }
    public string? Error { get; set; }
    public long Millis { get; set; }
}

/// <summary>Filter-first search so typed queries stay fast.</summary>
public sealed class SearchService : IDisposable
{
    readonly FileIndexStore _store;
    readonly BlockingCollection<SearchRequest> _queue = new();
    readonly Thread _thread;
    public event Action<SearchReply>? ReplyReady;

    public SearchService(FileIndexStore store)
    {
        _store = store;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "QuickFind.Search",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    public void Enqueue(SearchRequest req)
    {
        try { _queue.Add(req); } catch (InvalidOperationException) { }
    }

    void Loop()
    {
        foreach (var first in _queue.GetConsumingEnumerable())
        {
            var req = first;
            while (_queue.TryTake(out var newer)) req = newer;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            SearchReply reply;
            try { reply = Run(req, sw); }
            catch (Exception ex) { reply = new SearchReply { Serial = req.Serial, Error = ex.Message }; }
            ReplyReady?.Invoke(reply);
        }
    }

    SearchReply Run(SearchRequest req, System.Diagnostics.Stopwatch sw)
    {
        return _store.Read(idx =>
        {
            CompiledQuery q;
            try { q = QueryCompiler.Compile(req.Text, req.Options); }
            catch (ArgumentException ex)
            {
                return new SearchReply { Serial = req.Serial, Epoch = idx.Epoch, Error = ex.Message };
            }

            var n = idx.Entries.Count;
            if (n == 0)
            {
                return new SearchReply { Serial = req.Serial, Epoch = idx.Epoch, Ids = [], MatchTotal = 0, Millis = sw.ElapsedMilliseconds };
            }

            var hit = new bool[n];
            if (q.IsEmpty)
            {
                Parallel.For(0, n, i => { hit[i] = !idx.Entries[i].Deleted; });
            }
            else
            {
                Parallel.For(0, n, i =>
                {
                    var e = idx.Entries[i];
                    hit[i] = !e.Deleted && q.Matches(idx, e);
                });
            }

            var count = 0;
            for (var i = 0; i < n; i++) if (hit[i]) count++;
            var ids = new List<uint>(count);
            for (var i = 0; i < n; i++)
                if (hit[i]) ids.Add((uint)i);

            if (q.Dupe)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var i in ids)
                {
                    var f = idx.Fold(idx.Entries[(int)i]);
                    counts[f] = counts.GetValueOrDefault(f) + 1;
                }
                ids.RemoveAll(i => counts[idx.Fold(idx.Entries[(int)i])] <= 1);
            }

            if (!string.IsNullOrEmpty(q.Content))
                ids = ContentSearch.Filter(idx, ids, q.Content);

            var matchTotal = ids.Count;
            // Empty / huge result sets: skip sorting millions so typed queries are not blocked.
            // Cap the id window early — UI only shows UiRowCap rows; holding 20k+ ids still burns RAM.
            const int IdWindowCap = 5_000;
            if (ids.Count > 25_000)
            {
                if (!req.Ascending) ids.Reverse();
                ids = ids.GetRange(0, IdWindowCap);
            }
            else
            {
                SortHits(idx, ids, req.Sort, req.Ascending);
                if (ids.Count > IdWindowCap)
                    ids = ids.GetRange(0, IdWindowCap);
            }
            if (q.Count is int c && c >= 0 && ids.Count > c)
                ids.RemoveRange(c, ids.Count - c);

            return new SearchReply
            {
                Serial = req.Serial,
                Epoch = idx.Epoch,
                Ids = ids,
                MatchTotal = matchTotal,
                Millis = sw.ElapsedMilliseconds
            };
        });
    }

    static void SortHits(FileIndex idx, List<uint> ids, SortKey key, bool ascending)
    {
        if (ids.Count <= 1) return;
        Entry E(uint i) => idx.Entries[(int)i];
        switch (key)
        {
            case SortKey.Name:
                ids.Sort((a, b) =>
                {
                    var cmp = idx.FoldSpan(E(a)).SequenceCompareTo(idx.FoldSpan(E(b)));
                    return cmp != 0 ? cmp : idx.NameSpan(E(a)).SequenceCompareTo(idx.NameSpan(E(b)));
                });
                break;
            case SortKey.Path:
                ids.Sort((a, b) =>
                {
                    var cmp = idx.DirFoldSpan(E(a)).SequenceCompareTo(idx.DirFoldSpan(E(b)));
                    if (cmp != 0) return cmp;
                    cmp = idx.FoldSpan(E(a)).SequenceCompareTo(idx.FoldSpan(E(b)));
                    return cmp != 0 ? cmp : idx.NameSpan(E(a)).SequenceCompareTo(idx.NameSpan(E(b)));
                });
                break;
            case SortKey.Size:
                ids.Sort((a, b) =>
                {
                    ulong Sa(Entry x)
                    {
                        if (x.Size == AppConstants.NoSize) return 0;
                        return x.Size + 1;
                    }
                    return Sa(E(a)).CompareTo(Sa(E(b)));
                });
                break;
            case SortKey.Extension:
                ids.Sort((a, b) =>
                    idx.ExtensionSpan(E(a)).CompareTo(idx.ExtensionSpan(E(b)), StringComparison.OrdinalIgnoreCase));
                break;
            case SortKey.Modified:
                ids.Sort((a, b) => E(a).Mtime.CompareTo(E(b).Mtime));
                break;
            case SortKey.Created:
                ids.Sort((a, b) => E(a).Ctime.CompareTo(E(b).Ctime));
                break;
            case SortKey.Attributes:
                ids.Sort((a, b) => E(a).Attrs.CompareTo(E(b).Attrs));
                break;
        }
        if (!ascending) ids.Reverse();
    }

    public void Dispose() => _queue.CompleteAdding();
}