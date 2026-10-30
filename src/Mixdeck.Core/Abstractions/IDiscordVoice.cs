namespace Mixdeck.Core.Abstractions;

public enum DiscordConnectionState
{
    Disabled,
    Disconnected,
    AuthorizationRequired,
    Connecting,
    Connected,
    Error,
}

/// <summary>Local Discord voice RPC. Events may arrive on a background thread; errors never contain credentials.</summary>
public interface IDiscordVoice : IAsyncDisposable
{
    DiscordConnectionState State { get; }
    string? ErrorMessage { get; }
    /// <summary>Whether an application secret has been saved, even before authorization.</summary>
    bool HasCredentials { get; }
    event EventHandler<DiscordConnectionState>? StateChanged;
    event EventHandler<bool>? MuteChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task SetMuteAsync(bool muted, CancellationToken cancellationToken = default);
    Task AuthorizeAsync(string clientSecret, CancellationToken cancellationToken = default);
    Task ForgetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Demo implementation: never opens Discord or stores credentials.</summary>
public sealed class NullDiscordVoice : IDiscordVoice
{
    public DiscordConnectionState State => DiscordConnectionState.Disabled;
    public string? ErrorMessage => null;
    public bool HasCredentials => false;
    public event EventHandler<DiscordConnectionState>? StateChanged { add { } remove { } }
    public event EventHandler<bool>? MuteChanged { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetMuteAsync(bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task AuthorizeAsync(string clientSecret, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ForgetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
