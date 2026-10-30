using System.Buffers.Binary;
using System.Text;
using Mixdeck.Platform.Discord;

namespace Mixdeck.Platform.Tests;

public sealed class DiscordFrameTests
{
    [Fact]
    public async Task FramesUseLittleEndianHeaderAndUtf8Payload()
    {
        var payload = Encoding.UTF8.GetBytes("{\"message\":\"coupé\"}");
        using var stream = new MemoryStream();
        await new DiscordRpcFrame(1, payload).WriteAsync(stream, default);
        var bytes = stream.ToArray();
        BinaryPrimitives.ReadInt32LittleEndian(bytes).Should().Be(1);
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)).Should().Be(payload.Length);
        bytes.AsSpan(8).ToArray().Should().Equal(payload);
        stream.Position = 0;
        var decoded = await DiscordRpcFrame.ReadAsync(stream, default);
        decoded.Opcode.Should().Be(1);
        decoded.Payload.Should().Equal(payload);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1048577)]
    public async Task RejectsUntrustedPayloadLengthsBeforeAllocating(int length)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), length);
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => DiscordRpcFrame.ReadAsync(stream, default));
    }

    [Fact]
    public async Task HandlesPartialReadsAndRejectsTruncation()
    {
        using var wire = new MemoryStream();
        await new DiscordRpcFrame(3, [1, 2, 3, 4]).WriteAsync(wire, default);
        using var fragmented = new FragmentedStream(wire.ToArray());
        (await DiscordRpcFrame.ReadAsync(fragmented, default)).Payload.Should().Equal([1, 2, 3, 4]);
        using var truncated = new MemoryStream(wire.ToArray()[..^1]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => DiscordRpcFrame.ReadAsync(truncated, default));
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
