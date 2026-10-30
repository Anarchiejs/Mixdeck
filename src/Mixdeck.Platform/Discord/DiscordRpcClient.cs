using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Settings;

namespace Mixdeck.Platform.Discord;

/// <summary>Local Discord IPC with bounded frames, correlated commands and a quiet reconnect loop.</summary>
public sealed class DiscordRpcClient : IDiscordVoice
{
    private readonly ISettingsStore settings;
    private readonly DiscordCredentialStore store;
    private readonly DiscordOAuth oauth;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly CancellationTokenSource shutdown = new();
    private int disposed;
    private CancellationTokenSource? lifetime;
    private CancellationTokenSource? sessionCancellation;
    private Task? worker;
    private RpcConnection? connection;
    private DiscordCredentials? credentials;
    private bool credentialReadFailed;
    private string configuredId;
    private bool configuredEnabled;
    internal Func<string, Action<bool>?, CancellationToken, Task<RpcConnection>> Connect { get; set; } = RpcConnection.OpenAsync;

    private readonly Microsoft.Extensions.Logging.ILogger? logger;

    // Code et message d'erreur Discord (ex. « 4006 Not authenticated or invalid scope ») : sans jeton ni secret.
    internal static string RpcError(System.Text.Json.JsonElement root)
    {
        var data = root.TryGetProperty("data", out var d) ? d : root;
        var code = data.TryGetProperty("code", out var c) ? c.ToString() : "?";
        var message = data.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        return code + " " + (message.Length > 120 ? message[..120] : message);
    }
    internal static string RpcError(byte[] payload)
    {
        try { using var json = System.Text.Json.JsonDocument.Parse(payload); return RpcError(json.RootElement); }
        catch (Exception) { return "?"; }
    }

    public DiscordRpcClient(ISettingsStore settings, DiscordCredentialStore store, DiscordOAuth oauth,
        Microsoft.Extensions.Logging.ILogger<DiscordRpcClient>? logger = null)
    {
        this.logger = logger;
        this.settings = settings;
        this.store = store;
        this.oauth = oauth;
        configuredId = settings.Current.Discord.ClientId;
        configuredEnabled = settings.Current.Discord.Enabled;
        try { credentials = store.Load(); }
        catch { credentialReadFailed = true; }
        settings.Changed += SettingsChanged;
    }

    public DiscordConnectionState State { get; private set; } = DiscordConnectionState.Disabled;
    public string? ErrorMessage { get; private set; }
    public bool HasCredentials => credentials is { ClientSecret.Length: > 0 } && credentials.ClientId == configuredId;
    public bool HasStoredSecret => HasCredentials;
    public event EventHandler<bool>? MuteChanged;
    public event EventHandler<DiscordConnectionState>? StateChanged;

    private void SettingsChanged(object? sender, AppSettings value) => Configure(value.Discord.Enabled, value.Discord.ClientId);

    public void Configure(bool enabled, string clientId)
    {
        if (configuredEnabled == enabled && configuredId == clientId) return;
        configuredEnabled = enabled;
        configuredId = clientId;
        try { sessionCancellation?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { StartWorker(); }
        finally { lifecycle.Release(); }
    }

    private void StartWorker()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        if (worker is { IsCompleted: false }) return;
        lifetime?.Dispose();
        lifetime = new CancellationTokenSource();
        worker = Task.Run(() => RunAsync(lifetime.Token));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopWorkerAsync().ConfigureAwait(false); }
        finally { lifecycle.Release(); }
    }

    private async Task StopWorkerAsync()
    {
        lifetime?.Cancel();
        if (worker is not null)
        {
            try { await worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        worker = null;
        SetState(DiscordConnectionState.Disabled);
    }

    public async Task AuthorizeAsync(string clientSecret, CancellationToken cancellationToken = default)
    {
        using var authorizationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
        var ct = authorizationCancellation.Token;
        await lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopWorkerAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(configuredId)) { SetState(DiscordConnectionState.AuthorizationRequired); return; }
            if (string.IsNullOrWhiteSpace(clientSecret)) clientSecret = HasCredentials ? credentials!.ClientSecret : "";
            if (string.IsNullOrWhiteSpace(clientSecret)) { SetState(DiscordConnectionState.AuthorizationRequired); return; }
            var clientId = configuredId;
            credentials = new DiscordCredentials { ClientId = clientId, ClientSecret = clientSecret };
            store.Save(credentials);
            credentialReadFailed = false;
            SetState(DiscordConnectionState.Connecting);
            var rpc = await Connect(clientId, null, ct).ConfigureAwait(false);
            await using var authorizationConnection = rpc.ConfigureAwait(false);
            var authorization = await rpc.CommandAsync("AUTHORIZE", new { client_id = clientId,
                // rpc.voice.read/write sont réservés aux partenaires approuvés par Discord (invalid_scope constaté) :
                // « rpc » seul, accordé au propriétaire de l'application, couvre les réglages de voix.
                scopes = new[] { "rpc" } }, ct,
                timeout: TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            var code = authorization.GetProperty("code").GetString();
            if (string.IsNullOrEmpty(code)) throw new DiscordAuthorizationException();
            var authorized = await oauth.ExchangeAsync(clientId, clientSecret, code, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (clientId != configuredId) return;
            store.Save(authorized);
            credentials = authorized;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DiscordUnavailableException) { SetState(DiscordConnectionState.Disconnected); }
        catch (DiscordAuthorizationException e)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "Autorisation Discord refusée : {Detail}", e.Detail ?? "?");
            SetState(DiscordConnectionState.AuthorizationRequired, "Discord authorization failed. Check RPC access and the application tester list.");
        }
        catch { SetState(DiscordConnectionState.Error, "Discord authorization could not be completed."); }
        finally { StartWorker(); lifecycle.Release(); }
    }

    public async Task ForgetAsync(CancellationToken cancellationToken = default)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopWorkerAsync().ConfigureAwait(false);
            store.Forget();
            credentials = null;
            credentialReadFailed = false;
        }
        finally { StartWorker(); lifecycle.Release(); }
    }

    public async Task SetMuteAsync(bool mute, CancellationToken cancellationToken = default)
    {
        var rpc = connection;
        if (State != DiscordConnectionState.Connected || rpc is null) return;
        try { await rpc.CommandAsync("SET_VOICE_SETTINGS", new { mute }, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            try { sessionCancellation?.Cancel(); } catch (ObjectDisposedException) { }
            throw new IOException("Discord mute could not be changed.");
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sessionCancellation = session;
            var clientId = configuredId;
            try
            {
                if (!configuredEnabled) SetState(DiscordConnectionState.Disabled);
                else if (credentialReadFailed) SetState(DiscordConnectionState.Error, "Discord credentials could not be read. Forget them and authorize again.");
                else if (string.IsNullOrWhiteSpace(clientId) || !HasCredentials || string.IsNullOrEmpty(credentials!.AccessToken))
                    SetState(DiscordConnectionState.AuthorizationRequired);
                else
                {
                    var auth = credentials!;
                    if (auth.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
                    {
                        try
                        {
                            auth = await oauth.RefreshAsync(auth, session.Token).ConfigureAwait(false);
                            session.Token.ThrowIfCancellationRequested();
                            if (clientId != configuredId) continue;
                            store.Save(auth);
                            credentials = auth;
                        }
                        catch (DiscordAuthorizationException)
                        {
                            auth.AccessToken = "";
                            auth.RefreshToken = "";
                            store.Save(auth);
                            throw;
                        }
                    }
                    SetState(DiscordConnectionState.Connecting);
                    var rpc = await Connect(clientId,
                        mute => { if (State == DiscordConnectionState.Connected) MuteChanged?.Invoke(this, mute); }, session.Token).ConfigureAwait(false);
                    await using var activeConnection = rpc.ConfigureAwait(false);
                    await rpc.CommandAsync("AUTHENTICATE", new { access_token = auth.AccessToken }, session.Token).ConfigureAwait(false);
                    // Read initial state without publishing it; Mixdeck wins when Connected is raised.
                    await rpc.CommandAsync("GET_VOICE_SETTINGS", new { }, session.Token).ConfigureAwait(false);
                    await rpc.CommandAsync("SUBSCRIBE", new { }, session.Token, "VOICE_SETTINGS_UPDATE").ConfigureAwait(false);
                    connection = rpc;
                    SetState(DiscordConnectionState.Connected);
                    var refreshIn = auth.ExpiresAt - DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30);
                    var renewal = Task.Delay(refreshIn > TimeSpan.Zero ? refreshIn : TimeSpan.FromSeconds(1), session.Token);
                    await Task.WhenAny(rpc.Completion, renewal).ConfigureAwait(false);
                    session.Token.ThrowIfCancellationRequested();
                    if (rpc.Completion.IsCompleted) await rpc.Completion.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            catch (DiscordAuthorizationException e)
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "Connexion Discord refusée : {Detail}", e.Detail ?? "?");
                SetState(DiscordConnectionState.AuthorizationRequired, "Discord authorization is required. Check RPC access and the application tester list.");
            }
            catch (DiscordUnavailableException) { SetState(DiscordConnectionState.Disconnected); }
            catch (IOException) { SetState(DiscordConnectionState.Disconnected); }
            catch { SetState(DiscordConnectionState.Error, "Discord connection failed. It will retry automatically."); }
            finally { connection = null; }
            if (ct.IsCancellationRequested) break;
            if (State == DiscordConnectionState.Connected) SetState(DiscordConnectionState.Disconnected);
            // Cancellation/configuration wakes the connection immediately; normal retries stay quiet.
            try { await Task.Delay(TimeSpan.FromSeconds(5), session.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            sessionCancellation = null;
            session.Cancel();
        }
    }

    private void SetState(DiscordConnectionState state, string? error = null)
    {
        if (State == state && ErrorMessage == error) return;
        State = state;
        ErrorMessage = error;
        StateChanged?.Invoke(this, state);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        shutdown.Cancel();
        settings.Changed -= SettingsChanged;
        await StopAsync().ConfigureAwait(false);
        lifetime?.Dispose();
        shutdown.Dispose();
    }

    internal sealed class RpcConnection : IAsyncDisposable
    {
        private readonly Stream stream;
        private readonly SemaphoreSlim writes = new(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
        private readonly CancellationTokenSource closed;
        private readonly Action<bool>? muteChanged;
        public Task Completion { get; private set; } = Task.CompletedTask;

        private RpcConnection(Stream stream, Action<bool>? muteChanged, CancellationToken ct)
        {
            this.stream = stream;
            this.muteChanged = muteChanged;
            closed = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        internal static async Task<RpcConnection> OpenAsync(string clientId, Action<bool>? muteChanged, CancellationToken ct)
        {
            NamedPipeClientStream? pipe = null;
            for (var index = 0; index < 10; index++)
            {
                var candidate = new NamedPipeClientStream(".", "discord-ipc-" + index, PipeDirection.InOut, PipeOptions.Asynchronous);
                try { await candidate.ConnectAsync(150, ct).ConfigureAwait(false); pipe = candidate; break; }
                catch (Exception ex) when (ex is TimeoutException or IOException) { candidate.Dispose(); }
                catch { candidate.Dispose(); throw; }
            }
            if (pipe is null) throw new DiscordUnavailableException();
            return await ConnectStreamAsync(pipe, clientId, muteChanged, ct).ConfigureAwait(false);
        }

        internal static async Task<RpcConnection> ConnectStreamAsync(Stream pipe, string clientId, Action<bool>? muteChanged, CancellationToken ct)
        {
            var rpc = new RpcConnection(pipe, muteChanged, ct);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await rpc.WriteAsync(new(0, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, client_id = clientId })), timeout.Token).ConfigureAwait(false);
                while (true)
                {
                    var ready = await DiscordRpcFrame.ReadAsync(pipe, timeout.Token).ConfigureAwait(false);
                    if (ready.Opcode == 3) { await rpc.WriteAsync(new(4, ready.Payload), timeout.Token).ConfigureAwait(false); continue; }
                    if (ready.Opcode != 1) throw new DiscordAuthorizationException("RPC poignée de main " + RpcError(ready.Payload));
                    using var json = JsonDocument.Parse(ready.Payload);
                    if (json.RootElement.TryGetProperty("evt", out var evt) && evt.GetString() == "READY") break;
                    throw new DiscordAuthorizationException();
                }
                rpc.Completion = rpc.ReadLoopAsync();
                return rpc;
            }
            catch { await rpc.DisposeAsync().ConfigureAwait(false); throw; }
        }

        public async Task<JsonElement> CommandAsync(string command, object args, CancellationToken ct, string? evt = null, TimeSpan? timeout = null)
        {
            var nonce = Guid.NewGuid().ToString("N");
            var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[nonce] = response;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, closed.Token);
            deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
            try
            {
                var payload = JsonSerializer.SerializeToUtf8Bytes(new { cmd = command, args, nonce, evt });
                await WriteAsync(new(1, payload), deadline.Token).ConfigureAwait(false);
                return await response.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            finally { pending.TryRemove(nonce, out _); }
        }

        private async Task WriteAsync(DiscordRpcFrame frame, CancellationToken ct)
        {
            await writes.WaitAsync(ct).ConfigureAwait(false);
            try { await frame.WriteAsync(stream, ct).ConfigureAwait(false); }
            finally { writes.Release(); }
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!closed.IsCancellationRequested)
                {
                    var frame = await DiscordRpcFrame.ReadAsync(stream, closed.Token).ConfigureAwait(false);
                    if (frame.Opcode == 2) throw new DiscordAuthorizationException("RPC fermeture " + RpcError(frame.Payload));
                    if (frame.Opcode == 3) { await WriteAsync(new(4, frame.Payload), closed.Token).ConfigureAwait(false); continue; }
                    if (frame.Opcode != 1) continue;
                    using var document = JsonDocument.Parse(frame.Payload);
                    var root = document.RootElement;
                    var evt = root.TryGetProperty("evt", out var eventValue) ? eventValue.GetString() : null;
                    if (root.TryGetProperty("nonce", out var nonce) && nonce.ValueKind == JsonValueKind.String &&
                        pending.TryRemove(nonce.GetString()!, out var completion))
                    {
                        if (evt == "ERROR") completion.TrySetException(new DiscordAuthorizationException("RPC " + RpcError(root)));
                        else if (root.TryGetProperty("data", out var result)) completion.TrySetResult(result.Clone());
                        else completion.TrySetException(new InvalidDataException("Invalid Discord response."));
                    }
                    // Only dispatch actual subscriptions; SET responses must not echo back into Mixdeck.
                    else if (evt == "VOICE_SETTINGS_UPDATE" && root.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("mute", out var mute) && mute.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        muteChanged?.Invoke(mute.GetBoolean());
                }
            }
            finally
            {
                foreach (var entry in pending) entry.Value.TrySetException(new IOException("Discord disconnected."));
            }
        }

        public async ValueTask DisposeAsync()
        {
            closed.Cancel();
            stream.Dispose();
            try { await Completion.ConfigureAwait(false); } catch { }
            closed.Dispose();
        }
    }

    private sealed class DiscordUnavailableException : IOException;
}
