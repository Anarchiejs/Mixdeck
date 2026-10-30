using System.IO.Pipes;
using System.Text.Json;
using Mixdeck.Platform.Discord;

namespace Mixdeck.Platform.Tests;

public sealed class DiscordRpcProtocolTests
{
    [Fact]
    public async Task HandshakePingNonceAndVoiceDispatchUseTheRealFramedTransport()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var pipeName = "mixdeck-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync(ct);
        await pipe.ConnectAsync(ct);
        await accept;
        var receivedMute = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = DiscordRpcClient.RpcConnection.ConnectStreamAsync(pipe, "fake-id", mute => receivedMute.TrySetResult(mute), ct);
        var handshake = await DiscordRpcFrame.ReadAsync(server, ct);
        handshake.Opcode.Should().Be(0);
        using (var json = JsonDocument.Parse(handshake.Payload))
        {
            json.RootElement.GetProperty("v").GetInt32().Should().Be(1);
            json.RootElement.GetProperty("client_id").GetString().Should().Be("fake-id");
        }
        await SendAsync(server, new { cmd = "DISPATCH", evt = "READY", data = new { } }, ct);
        await using var client = await opening;
        await new DiscordRpcFrame(3, [1, 2, 3]).WriteAsync(server, ct);
        var pong = await DiscordRpcFrame.ReadAsync(server, ct);
        pong.Opcode.Should().Be(4);
        pong.Payload.Should().Equal([1, 2, 3]);

        var setting = client.CommandAsync("SET_VOICE_SETTINGS", new { mute = true }, ct);
        var command = await DiscordRpcFrame.ReadAsync(server, ct);
        using var commandJson = JsonDocument.Parse(command.Payload);
        var nonce = commandJson.RootElement.GetProperty("nonce").GetString();
        commandJson.RootElement.GetProperty("args").GetProperty("mute").GetBoolean().Should().BeTrue();
        // An unrelated nonce must not complete the request.
        await SendAsync(server, new { cmd = "SET_VOICE_SETTINGS", nonce = "unrelated", data = new { mute = false } }, ct);
        await SendAsync(server, new { cmd = "SET_VOICE_SETTINGS", nonce, data = new { mute = true } }, ct);
        (await setting).GetProperty("mute").GetBoolean().Should().BeTrue();
        receivedMute.Task.IsCompleted.Should().BeFalse("SET responses are not subscribed events");
        await SendAsync(server, new { cmd = "DISPATCH", evt = "VOICE_SETTINGS_UPDATE", data = new { mute = false } }, ct);
        (await receivedMute.Task.WaitAsync(ct)).Should().BeFalse();
    }

    [Fact]
    public async Task RpcErrorDoesNotExposeItsPayloadAndCloseFailsPendingCommands()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var pipeName = "mixdeck-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync(ct);
        await pipe.ConnectAsync(ct);
        await accept;
        var opening = DiscordRpcClient.RpcConnection.ConnectStreamAsync(pipe, "fake-id", null, ct);
        await DiscordRpcFrame.ReadAsync(server, ct);
        await SendAsync(server, new { evt = "READY", data = new { } }, ct);
        await using var client = await opening;
        var command = client.CommandAsync("AUTHENTICATE", new { access_token = "fake-token" }, ct);
        var request = await DiscordRpcFrame.ReadAsync(server, ct);
        using var json = JsonDocument.Parse(request.Payload);
        await SendAsync(server, new { evt = "ERROR", nonce = json.RootElement.GetProperty("nonce").GetString(),
            data = new { message = "fake-sensitive-content", code = 4009 } }, ct);
        var error = await Assert.ThrowsAsync<DiscordAuthorizationException>(() => command);
        error.ToString().Should().NotContain("fake-sensitive-content").And.NotContain("fake-token");
        var pending = client.CommandAsync("GET_VOICE_SETTINGS", new { }, ct);
        await DiscordRpcFrame.ReadAsync(server, ct);
        await new DiscordRpcFrame(2, JsonSerializer.SerializeToUtf8Bytes(new { code = 4003 })).WriteAsync(server, ct);
        await Assert.ThrowsAsync<IOException>(() => pending);
    }

    private static Task SendAsync(Stream stream, object payload, CancellationToken ct) =>
        new DiscordRpcFrame(1, JsonSerializer.SerializeToUtf8Bytes(payload)).WriteAsync(stream, ct);
}
