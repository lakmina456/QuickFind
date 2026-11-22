using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using QuickFind.Core;
using QuickFind.Indexing;

namespace QuickFind.ViewModels;

/// <summary>
/// DataGrid-friendly list that keeps entry ids and builds rows on demand.
/// Rows are cached per index so WPF virtualization/recycling keeps stable binding sources
/// (returning a new ResultRow on every indexer hit blanks the Name column).
/// </summary>
public sealed class VirtualResultList : IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    readonly FileIndexStore _store;
    List<uint> _ids = [];
    ResultRow?[] _cache = [];
    ulong _epoch;

    public VirtualResultList(FileIndexStore store) => _store = store;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Count => _ids.Count;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public int TotalCount { get; private set; }

    public void Reset(List<uint> ids, ulong epoch, int? totalCount = null)
    {
        _ids = ids;
        _epoch = epoch;
        _cache = new ResultRow?[ids.Count];
        TotalCount = totalCount ?? ids.Count;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalCount)));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void Clear()
    {
        if (_ids.Count == 0 && TotalCount == 0) return;
        _ids = [];
        _cache = [];
        TotalCount = 0;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalCount)));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public uint IdAt(int index) => _ids[index];

    public ResultRow? RowAt(int index)
    {
        if (index < 0 || index >= _ids.Count) return null;
        if (_cache[index] is { } cached) return cached;

        var id = _ids[index];
        var row = _store.Read(idx =>
        {
            if (idx.Epoch != _epoch || id >= idx.Entries.Count) return null;
            var e = idx.Entries[(int)id];
            if (e.Deleted) return null;
            var name = idx.Name(e);
            var ext = idx.Extension(e);
            return new ResultRow
            {
                Id = id,
                Name = name,
                Path = idx.DirPath(e),
                FullPath = idx.FullPath(e),
                Size = FormatUtil.SizeText(e.Size, e.IsDir),
                Extension = ext,
                Modified = DateParser.FormatFileTime(e.Mtime),
                Created = DateParser.FormatFileTime(e.Ctime),
                Attributes = FormatUtil.AttribText(e.Attrs),
                IsDirectory = e.IsDir,
                Kind = e.IsDir ? FileKind.Other : Filters.KindOf(ext)
            };
        });
        _cache[index] = row;
        return row;
    }

    public object? this[int index]
    {
        get => RowAt(index);
        set => throw new NotSupportedException();
    }

    public int Add(object? value) => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void ClearList() => Clear();
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public int IndexOf(object? value)
    {
        if (value is not ResultRow row) return -1;
        for (var i = 0; i < _ids.Count; i++)
            if (_ids[i] == row.Id) return i;
        return -1;
    }
    public void CopyTo(Array array, int index)
    {
        for (var i = 0; i < _ids.Count; i++)
            array.SetValue(this[i], index + i);
    }
    public IEnumerator GetEnumerator()
    {
        for (var i = 0; i < _ids.Count; i++)
            yield return this[i];
    }
}
