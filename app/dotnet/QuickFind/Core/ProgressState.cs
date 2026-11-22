namespace QuickFind.Core;

public sealed class ProgressState
{
    private long _found;
    private int _running;
    private string _current = "";
    private readonly object _gate = new();
    private long _lastNotifyTicks;

    /// <summary>Raised when indexing/enrich progress should refresh the UI (throttled).</summary>
    public event Action? Changed;

    public bool Running
    {
        get => Interlocked.CompareExchange(ref _running, 0, 0) != 0;
        set
        {
            var v = value ? 1 : 0;
            var prev = Interlocked.Exchange(ref _running, v);
            if (prev != v) Notify(force: true);
        }
    }

    public long Found => Interlocked.Read(ref _found);

    public void ResetFound()
    {
        Interlocked.Exchange(ref _found, 0);
        Notify(force: true);
    }

    public void AddFound(long n = 1)
    {
        Interlocked.Add(ref _found, n);
        Notify(force: false);
    }

    public string Current
    {
        get { lock (_gate) return _current; }
        set
        {
            lock (_gate)
            {
                if (_current == value) return;
                _current = value;
            }
            Notify(force: false);
        }
    }

    void Notify(bool force)
    {
        if (!force)
        {
            var now = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastNotifyTicks);
            if (now - last < 250) return;
            Interlocked.Exchange(ref _lastNotifyTicks, now);
        }
        else
        {
            Interlocked.Exchange(ref _lastNotifyTicks, Environment.TickCount64);
        }
        Changed?.Invoke();
    }
}
