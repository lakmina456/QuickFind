using System.IO.Pipes;
using System.Windows;

namespace QuickFind.Services;

public static class SingleInstance
{
    const string MutexName = "Local\\QuickFind.SingleInstance";
    const string PipeName = "QuickFind.ShowPipe";

    static Mutex? _mutex;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            try { _mutex.Dispose(); } catch { }
            _mutex = null;
            SignalShow();
            return false;
        }
        return true;
    }

    public static void StartListener(Action onShow, CancellationToken ct)
    {
        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct);
                    Application.Current?.Dispatcher.BeginInvoke(onShow);
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(500, ct); }
            }
        }, ct);
    }

    static void SignalShow()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(800);
            client.WriteByte(1);
        }
        catch { }
    }

    public static void Release()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
    }
}
