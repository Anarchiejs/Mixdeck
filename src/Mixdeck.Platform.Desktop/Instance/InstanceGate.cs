using System.Net.Sockets;
using System.Text;

namespace Mixdeck.Platform.Desktop.Instance;

/// <summary>Une seule instance par utilisateur : le socket Unix est le verrou. La seconde demande d'afficher la fenêtre.</summary>
public sealed class InstanceGate : IDisposable
{
    private readonly string _path;
    private readonly Socket? _listener;
    private readonly CancellationTokenSource? _lifetime;
    private int _disposed;

    private InstanceGate(string path, Socket? listener)
    {
        _path = path;
        _listener = listener;
        IsFirst = listener is not null;
        if (listener is null) return;
        _lifetime = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoop(_lifetime.Token));
    }

    public bool IsFirst { get; }
    public event Action? ShowRequested;

    public static InstanceGate Acquire(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = SocketPath(directory);
        var endpoint = new UnixDomainSocketEndPoint(path);
        if (TryConnect(endpoint)) return new InstanceGate(path, null);
        TryDelete(path);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(endpoint);
            listener.Listen(4);
            return new InstanceGate(path, listener);
        }
        catch (SocketException)
        {
            listener.Dispose();
            return new InstanceGate(path, null);
        }
    }

    public void SignalShow()
    {
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(_path));
            var bytes = "show\n"u8.ToArray();
            client.Send(bytes);
        }
        catch (SocketException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime?.Cancel();
        try { _listener?.Dispose(); } catch (SocketException) { }
        _lifetime?.Dispose();
        if (IsFirst) TryDelete(_path);
    }

    private async Task AcceptLoop(CancellationToken cancellationToken)
    {
        if (_listener is null) return;
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket client;
            try { client = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException or OperationCanceledException) { return; }
            using (client)
            {
                var buffer = new byte[16];
                try
                {
                    var read = client.Receive(buffer);
                    if (read > 0 && Encoding.ASCII.GetString(buffer, 0, read).Contains("show", StringComparison.Ordinal))
                    {
                        try { ShowRequested?.Invoke(); }
                        catch { /* L'abonné ne doit pas fermer le verrou. */ }
                    }
                }
                catch (SocketException) { }
            }
        }
    }

    private static bool TryConnect(UnixDomainSocketEndPoint endpoint)
    {
        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            probe.Connect(endpoint);
            return true;
        }
        catch (SocketException) { return false; }
        catch (IOException) { return false; }
    }

    private static string SocketPath(string directory)
    {
        var path = Path.Combine(directory, "instance.sock");
        if (path.Length < 100) return path;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];
        return Path.Combine(Path.GetTempPath(), "mixdeck-" + hash + ".sock");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }
}
