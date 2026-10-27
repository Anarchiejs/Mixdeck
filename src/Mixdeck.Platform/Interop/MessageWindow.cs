using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Mixdeck.Platform.Interop;

internal sealed class MessageWindow : IDisposable
{
    private const uint InvokeMessage = 0x8001;
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly object _gate = new();
    private readonly Thread _thread;
    private readonly Native.WindowProc _procedure;
    private readonly Func<uint, nuint, nint, nint?> _handler;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;
    internal nint Handle { get; private set; }

    internal MessageWindow(string name, Func<uint, nuint, nint, nint?> handler, bool receiveBroadcasts = false)
    {
        _handler = handler;
        _procedure = Procedure;
        _thread = new Thread(() => Run(receiveBroadcasts)) { IsBackground = true, Name = name };
        _thread.Start();
        _ready.Task.GetAwaiter().GetResult();
    }

    internal void Invoke(Action action)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Thread.CurrentThread == _thread) { action(); return; }
            _commands.Enqueue(() => { try { action(); completion.SetResult(); } catch (Exception ex) { completion.SetException(ex); } });
            if (!Native.PostMessage(Handle, InvokeMessage, 0, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        completion.Task.GetAwaiter().GetResult();
    }

    private nint Procedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == InvokeMessage)
        {
            while (_commands.TryDequeue(out var command)) command();
            return 0;
        }
        // Une exception ne doit jamais traverser un rappel natif.
        try { return _handler(message, wParam, lParam) ?? Native.DefWindowProc(window, message, wParam, lParam); }
        catch { return Native.DefWindowProc(window, message, wParam, lParam); }
    }

    private void Run(bool receiveBroadcasts)
    {
        string className = "Mixdeck." + Guid.NewGuid().ToString("N");
        nint classPointer = Marshal.StringToHGlobalUni(className);
        nint broadcastWindow = 0;
        var module = Native.GetModuleHandle(null);
        try
        {
            Native.WindowClass windowClass = new() { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = Marshal.GetFunctionPointerForDelegate(_procedure), Instance = module, ClassName = classPointer };
            if (Native.RegisterClassEx(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            Handle = Native.CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, new nint(-3), 0, module, 0);
            if (Handle == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            // Les diffusions Windows n'atteignent pas les fenêtres message-only.
            if (receiveBroadcasts)
            {
                broadcastWindow = Native.CreateWindowEx(0x80, className, "", 0, 0, 0, 0, 0, 0, 0, module, 0);
                if (broadcastWindow == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            _ready.SetResult();
            while (Native.GetMessage(out var message, 0, 0, 0) > 0)
            {
                Native.TranslateMessage(in message);
                Native.DispatchMessage(in message);
            }
        }
        catch (Exception ex) { _ready.TrySetException(ex); }
        finally
        {
            if (broadcastWindow != 0) Native.DestroyWindow(broadcastWindow);
            if (Handle != 0) Native.DestroyWindow(Handle);
            Native.UnregisterClass(className, module);
            Marshal.FreeHGlobal(classPointer);
            GC.KeepAlive(_procedure);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _commands.Enqueue(() => Native.PostQuitMessage(0));
            Native.PostMessage(Handle, InvokeMessage, 0, 0);
        }
        if (Thread.CurrentThread != _thread) _thread.Join();
    }
}
