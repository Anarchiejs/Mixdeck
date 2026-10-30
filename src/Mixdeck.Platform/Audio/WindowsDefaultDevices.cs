using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Mixdeck.Platform.Audio.AudioPolicy;

namespace Mixdeck.Platform.Audio;

/// <summary>Owns native objects on one MTA thread, including registration and teardown of notifications.</summary>
public sealed class WindowsDefaultDevices : IWindowsDefaultDevices
{
    private readonly BlockingCollection<Action> work = new();
    private readonly Thread worker;
    private int disposed;
    private Exception? startupError;
    public event EventHandler? Changed;

    public WindowsDefaultDevices()
    {
        worker = new Thread(Run) { IsBackground = true, Name = "Mixdeck Windows defaults" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
    }

    public Task<WindowsDefaultDevicesSnapshot> ReadAsync() => Enqueue(() => new WindowsDefaultDevicesSnapshot(
        RenderEndpoints.Read(null), RenderEndpoints.Read(null, 1), ReadDefault(0), ReadDefault(1)));

    public Task SetDefaultAsync(WindowsAudioFlow flow, string id) => Enqueue(() =>
    {
        if (flow is not (WindowsAudioFlow.Output or WindowsAudioFlow.Input)) throw new ArgumentOutOfRangeException(nameof(flow));
        // Revalidate immediately before writing: a headset may have been unplugged since the last refresh.
        if (!RenderEndpoints.Read(null, (int)flow).Any(d => d.Id == id))
            throw new InvalidOperationException("Endpoint is no longer active.");
        IDefaultDevicePolicy? policy = null;
        try
        {
            policy = (IDefaultDevicePolicy)new DefaultDevicePolicyClient();
            DefaultDeviceRoles.Apply(id, policy.SetDefaultEndpoint);
        }
        finally { CoreAudioInterop.Release(policy); }
        return true;
    });

    private static string? ReadDefault(int flow)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            var hr = enumerator.GetDefaultAudioEndpoint(flow, 0, out device);
            if (hr == unchecked((int)0x80070490)) return null; // No device, e.g. no microphone attached.
            CoreAudioInterop.Check(hr);
            CoreAudioInterop.Check(device.GetId(out var id));
            return id;
        }
        finally { CoreAudioInterop.Release(device); CoreAudioInterop.Release(enumerator); }
    }

    private Task<T> Enqueue<T>(Func<T> operation)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Volatile.Read(ref disposed) != 0) return Task.FromException<T>(new ObjectDisposedException(nameof(WindowsDefaultDevices)));
        try
        {
            work.Add(() =>
            {
                try
                {
                    if (startupError is not null) throw new InvalidOperationException("Windows audio notifications unavailable.", startupError);
                    result.SetResult(operation());
                }
                catch (Exception ex) { result.SetException(ex); }
            });
        }
        catch (InvalidOperationException) { result.TrySetException(new ObjectDisposedException(nameof(WindowsDefaultDevices))); }
        return result.Task;
    }

    private void Run()
    {
        IMMDeviceEnumerator? enumerator = null;
        var callback = new Notifications(() => Changed?.Invoke(this, EventArgs.Empty));
        bool registered = false;
        var hr = CoreAudioInterop.CoInitializeEx(0, 0);
        try
        {
            try
            {
                CoreAudioInterop.Check(hr);
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                CoreAudioInterop.Check(enumerator.RegisterEndpointNotificationCallback(callback));
                registered = true;
            }
            catch (Exception ex) { startupError = ex; }
            foreach (var operation in work.GetConsumingEnumerable())
            {
                // Registration failure must be visible, rather than silently offering a stale list.
                operation();
            }
        }
        finally
        {
            if (registered) enumerator!.UnregisterEndpointNotificationCallback(callback);
            CoreAudioInterop.Release(enumerator);
            if (hr >= 0) CoreAudioInterop.CoUninitialize();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        work.CompleteAdding();
        // No UI calls or synchronous callback marshaling in the worker.
        worker.Join();
        work.Dispose();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class Notifications(Action changed) : IMMNotificationClient
    {
        private int Notify() { try { changed(); } catch (Exception) { /* Never cross a native callback boundary. */ } return 0; }
        public int OnDeviceStateChanged(string id, uint state) => Notify();
        public int OnDeviceAdded(string id) => Notify();
        public int OnDeviceRemoved(string id) => Notify();
        public int OnDefaultDeviceChanged(int flow, int role, string? id) => Notify();
        public int OnPropertyValueChanged(string id, PropertyKey key) => Notify();
    }
}
