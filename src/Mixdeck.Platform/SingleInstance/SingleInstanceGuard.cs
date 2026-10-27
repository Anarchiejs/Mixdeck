namespace Mixdeck.Platform.SingleInstance;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private readonly EventWaitHandle _stop = new(false, EventResetMode.ManualReset);
    private readonly Thread _thread;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(false, @"Local\Mixdeck-6f1c2b7e");
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Mixdeck-6f1c2b7e-activation");
        _thread = new Thread(Listen) { IsBackground = true, Name = "Mixdeck activation" };
        _thread.Start();
        IsFirstInstance = _ready.Task.GetAwaiter().GetResult();
    }

    public bool IsFirstInstance { get; }
    public event EventHandler? ActivationRequested;

    public void SignalFirstInstance()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _activation.Set();
    }

    private void Listen()
    {
        WaitHandle[] handles = [_stop, _activation];
        bool ownsMutex = false;
        try
        {
            try { ownsMutex = _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            _ready.SetResult(ownsMutex);
            if (!ownsMutex) { _stop.WaitOne(); return; }
            while (WaitHandle.WaitAny(handles) == 1)
            {
                try { ActivationRequested?.Invoke(this, EventArgs.Empty); }
                catch { /* Un abonné ne doit pas arrêter l'écoute d'activation. */ }
            }
        }
        catch (Exception ex) { _ready.TrySetException(ex); }
        finally
        {
            if (ownsMutex) _mutex.ReleaseMutex();
            _mutex.Dispose();
            _activation.Dispose();
            _stop.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Set();
        if (Thread.CurrentThread != _thread) _thread.Join();
    }
}
