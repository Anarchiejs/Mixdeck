using System.Buffers.Binary;

namespace Mixdeck.Platform.Discord;

internal readonly record struct DiscordRpcFrame(int Opcode, byte[] Payload)
{
    internal const int MaximumPayload = 1024 * 1024;

    internal async Task WriteAsync(Stream stream, CancellationToken ct)
    {
        if (Payload.Length > MaximumPayload) throw new InvalidDataException("Discord frame too large.");
        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, Opcode);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), Payload.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(Payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    internal static async Task<DiscordRpcFrame> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        var opcode = BinaryPrimitives.ReadInt32LittleEndian(header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (opcode is < 0 or > 4 || length < 0 || length > MaximumPayload)
            throw new InvalidDataException("Invalid Discord frame.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        return new DiscordRpcFrame(opcode, payload);
    }
}
