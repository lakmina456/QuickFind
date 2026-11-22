using QuickFind.Core;

namespace QuickFind.Indexing;

/// <summary>Thread-safe holder for the live <see cref="FileIndex"/>.</summary>
public sealed class FileIndexStore
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private FileIndex _index = new();

    /// <summary>Raised after a write/replace that changes Epoch or Generation (any thread).</summary>
    public event Action? Changed;

    public void Read(Action<FileIndex> action)
    {
        _lock.EnterReadLock();
        try { action(_index); }
        finally { _lock.ExitReadLock(); }
    }

    public T Read<T>(Func<FileIndex, T> action)
    {
        _lock.EnterReadLock();
        try { return action(_index); }
        finally { _lock.ExitReadLock(); }
    }

    public void Write(Action<FileIndex> action)
    {
        ulong beforeEpoch, beforeGen;
        _lock.EnterWriteLock();
        try
        {
            beforeEpoch = _index.Epoch;
            beforeGen = _index.Generation;
            action(_index);
            if (_index.Epoch != beforeEpoch || _index.Generation != beforeGen)
                Changed?.Invoke();
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void Replace(FileIndex next)
    {
        _lock.EnterWriteLock();
        try
        {
            next.Epoch = _index.Epoch + 1;
            next.Generation = _index.Generation + 1;
            _index = next;
            Changed?.Invoke();
        }
        finally { _lock.ExitWriteLock(); }
    }

    public (ulong Epoch, ulong Generation, bool Ready, long Count) SnapshotMeta()
    {
        _lock.EnterReadLock();
        try { return (_index.Epoch, _index.Generation, _index.Ready, _index.Entries.Count); }
        finally { _lock.ExitReadLock(); }
    }
}
