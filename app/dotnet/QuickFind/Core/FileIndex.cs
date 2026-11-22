namespace QuickFind.Core;

/// <summary>In-memory index — Rust Index layout: entries + shared name pool + dirs.</summary>
public sealed class FileIndex
{
    public List<Entry> Entries { get; } = [];
    public NamePool Names { get; } = new();
    public List<Dir> Dirs { get; } = [];
    public ulong Generation;
    public ulong Epoch;
    public bool Ready;

    public ReadOnlySpan<char> NameSpan(in Entry e) => Names.Slice(e.NameOff, e.NameLen);
    public ReadOnlySpan<char> FoldSpan(in Entry e) => Names.Slice(e.FoldOff, e.FoldLen);

    public string Name(in Entry e) => Names.GetString(e.NameOff, e.NameLen);
    public string Fold(in Entry e) => Names.GetString(e.FoldOff, e.FoldLen);

    public ReadOnlySpan<char> ExtensionSpan(in Entry e)
    {
        if (e.IsDir) return ReadOnlySpan<char>.Empty;
        var name = NameSpan(e);
        var p = name.LastIndexOf('.');
        if (p <= 0) return ReadOnlySpan<char>.Empty;
        return name[(p + 1)..];
    }

    public string Extension(in Entry e) => ExtensionSpan(e).ToString();

    public string DirPath(in Entry e)
    {
        if (e.Dir == AppConstants.None) return "";
        return e.Dir < Dirs.Count ? Dirs[(int)e.Dir].Path : "";
    }

    public string DirFold(in Entry e)
    {
        if (e.Dir == AppConstants.None) return "";
        return e.Dir < Dirs.Count ? Dirs[(int)e.Dir].Fold : "";
    }

    public ReadOnlySpan<char> DirFoldSpan(in Entry e)
    {
        if (e.Dir == AppConstants.None) return ReadOnlySpan<char>.Empty;
        return e.Dir < Dirs.Count ? Dirs[(int)e.Dir].Fold.AsSpan() : ReadOnlySpan<char>.Empty;
    }

    public string FullPath(in Entry e) => TextNorm.Join(DirPath(e), Name(e));

    public void SetDir(uint id, string path)
    {
        var i = (int)id;
        while (Dirs.Count <= i)
            Dirs.Add(new Dir());
        Dirs[i].Path = path;
        // Reuse the same string when already lowercase ASCII — halves dir string heap.
        var fold = TextNorm.Fold(path);
        Dirs[i].Fold = ReferenceEquals(path, fold) || string.Equals(path, fold, StringComparison.Ordinal)
            ? path
            : fold;
    }

    public void Rename(int i, string name)
    {
        var tmp = Entry.Create(Names, name, 0, 0, 0, 0, 0, 0);
        var e = Entries[i];
        e.NameOff = tmp.NameOff;
        e.NameLen = tmp.NameLen;
        e.FoldOff = tmp.FoldOff;
        e.FoldLen = tmp.FoldLen;
        Entries[i] = e;
    }

    public void FinalizeIndex()
    {
        foreach (var d in Dirs)
            d.Entry = AppConstants.None;

        for (var i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            if (e.SelfDir != AppConstants.None && e.SelfDir < Dirs.Count)
                Dirs[(int)e.SelfDir].Entry = (uint)i;
        }

        var counts = new uint[Dirs.Count];
        for (var i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            if (!e.Deleted && e.Dir != AppConstants.None && e.Dir < counts.Length)
                counts[e.Dir]++;
        }

        for (var i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            if (e.SelfDir != AppConstants.None)
            {
                e.Children = e.SelfDir < counts.Length ? counts[e.SelfDir] : 0;
                Entries[i] = e;
            }
        }

        RollupFolderSizes();
        Generation++;
    }

    /// <summary>Sum file sizes into ancestor folder entries (bottom-up).</summary>
    public void RollupFolderSizes()
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            if (!e.IsDir) continue;
            e.Size = 0;
            Entries[i] = e;
        }

        for (var i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            if (e.Deleted || e.IsDir) continue;
            if (e.Size == 0 || e.Size == AppConstants.NoSize) continue;
            AddSizeToAncestors(e.Dir, e.Size);
        }
    }

    public void AddSizeToAncestors(uint dirId, ulong delta)
    {
        if (delta == 0 || delta == AppConstants.NoSize) return;
        var guard = 0;
        while (dirId != AppConstants.None && dirId < Dirs.Count && guard++ < 512)
        {
            var ent = Dirs[(int)dirId].Entry;
            if (ent < Entries.Count)
            {
                var fe = Entries[(int)ent];
                if (fe.IsDir)
                {
                    var next = fe.Size == AppConstants.NoSize ? 0UL : fe.Size;
                    fe.Size = next + delta;
                    Entries[(int)ent] = fe;
                }
                dirId = fe.Dir;
            }
            else break;
        }
    }

    public void AdjustFolderSizeDelta(uint dirId, long delta)
    {
        if (delta == 0) return;
        if (delta > 0) AddSizeToAncestors(dirId, (ulong)delta);
        else SubtractSizeFromAncestors(dirId, (ulong)(-delta));
    }

    void SubtractSizeFromAncestors(uint dirId, ulong delta)
    {
        if (delta == 0) return;
        var guard = 0;
        while (dirId != AppConstants.None && dirId < Dirs.Count && guard++ < 512)
        {
            var ent = Dirs[(int)dirId].Entry;
            if (ent < Entries.Count)
            {
                var fe = Entries[(int)ent];
                if (fe.IsDir)
                {
                    var cur = fe.Size == AppConstants.NoSize ? 0UL : fe.Size;
                    fe.Size = cur > delta ? cur - delta : 0;
                    Entries[(int)ent] = fe;
                }
                dirId = fe.Dir;
            }
            else break;
        }
    }

    public void Clear()
    {
        Entries.Clear();
        Names.Clear();
        Dirs.Clear();
        Ready = false;
    }

    /// <summary>Shrink collections after a full scan to cut unused capacity.</summary>
    public void Compact()
    {
        Entries.Capacity = Entries.Count;
        Dirs.Capacity = Dirs.Count;
        Names.TrimExcess();
    }
}
