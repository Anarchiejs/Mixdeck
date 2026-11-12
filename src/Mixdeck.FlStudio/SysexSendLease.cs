using System.Runtime.InteropServices;

namespace Mixdeck.FlStudio;

/// <summary>
/// Owns one MIDISysexSendRequest until CoreMIDI calls the completion proc.
/// Aborting a send only asks CoreMIDI to finish; the buffers stay valid until that callback.
/// </summary>
internal sealed class SysexSendLease
{
    public const int RequestBytes = 40;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _done = new(false);
    private readonly GCHandle _handle;
    private bool _released;
    private bool _waiterFinished;
    private int _releaseCount;

    private SysexSendLease(uint destination, byte[] message, IntPtr completion)
    {
        Request = Marshal.AllocHGlobal(RequestBytes);
        Data = Marshal.AllocHGlobal(Math.Max(message.Length, 1));
        _handle = GCHandle.Alloc(this);
        Marshal.Copy(message, 0, Data, message.Length);
        Marshal.WriteInt32(Request, 0, unchecked((int)destination));
        Marshal.WriteIntPtr(Request, 8, Data);
        Marshal.WriteInt32(Request, 16, message.Length);
        Marshal.WriteInt32(Request, 20, 0);
        Marshal.WriteIntPtr(Request, 24, completion);
        Marshal.WriteIntPtr(Request, 32, GCHandle.ToIntPtr(_handle));
    }

    public IntPtr Request { get; }
    public IntPtr Data { get; }
    public int ReleaseCount => Volatile.Read(ref _releaseCount);

    public static SysexSendLease Allocate(uint destination, byte[] message, IntPtr completion) =>
        new(destination, message, completion);

    public bool Wait(TimeSpan timeout) => _done.Wait(timeout);

    public void RequestAbort()
    {
        lock (_gate)
        {
            if (_released) return;
            Marshal.WriteByte(Request, 20, 1);
        }
    }

    public static void OnComplete(IntPtr request)
    {
        var pointer = Marshal.ReadIntPtr(request, 32);
        if (pointer == 0) return;
        var handle = GCHandle.FromIntPtr(pointer);
        if (handle.Target is SysexSendLease lease) lease.CompleteFromCallback();
    }

    public void CompleteFromCallback()
    {
        lock (_gate)
        {
            ReleaseNative();
            _done.Set();
            if (_waiterFinished) _done.Dispose();
        }
    }

    public void WaiterFinished()
    {
        lock (_gate)
        {
            if (_waiterFinished) return;
            _waiterFinished = true;
            if (_released) _done.Dispose();
        }
    }

    public void ReleaseBecauseNotSent()
    {
        lock (_gate)
        {
            ReleaseNative();
            if (_waiterFinished) return;
            _waiterFinished = true;
            _done.Dispose();
        }
    }

    private void ReleaseNative()
    {
        if (_released) return;
        _released = true;
        _releaseCount++;
        Marshal.FreeHGlobal(Data);
        Marshal.FreeHGlobal(Request);
        if (_handle.IsAllocated) _handle.Free();
    }
}
