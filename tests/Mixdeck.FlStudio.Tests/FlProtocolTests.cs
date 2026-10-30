using Mixdeck.Core.Model;
using Mixdeck.FlStudio;

namespace Mixdeck.FlStudio.Tests;

public sealed class FlProtocolTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(16383)]
    public void Commands_encode_big_endian_14_bits(int value)
    {
        byte[] n = [(byte)(value >> 7), (byte)(value & 127)];
        FlProtocol.Ping().Should().Equal(Frame(1));
        FlProtocol.GetCount().Should().Equal(Frame(9));
        FlProtocol.List().Should().Equal(Frame(10));
        FlProtocol.Select(value).Should().Equal(Frame(2, n));
        FlProtocol.GetTrack(value).Should().Equal(Frame(8, n));
        foreach (var enabled in new[] { false, true })
        {
            var bit = (byte)(enabled ? 1 : 0);
            FlProtocol.Mute(value, enabled).Should().Equal(Frame(3, [.. n, bit]));
            FlProtocol.SlotsEnabled(value, enabled).Should().Equal(Frame(7, [.. n, bit]));
            foreach (var slot in new[] { 0, 9 })
            {
                FlProtocol.Slot(value, slot, enabled).Should().Equal(Frame(5, [.. n, (byte)slot, bit]));
                FlProtocol.Preset(value, slot, enabled).Should().Equal(Frame(6, [.. n, (byte)slot, bit]));
            }
        }
        FlProtocol.Volume(value, 0).Should().Equal(Frame(4, [.. n, 0, 0]));
        FlProtocol.Volume(value, 1).Should().Equal(Frame(4, [.. n, 127, 127]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16383)]
    public void Replies_decode_each_message_and_limits(int value)
    {
        byte[] n = [(byte)(value >> 7), (byte)(value & 127)];
        FlProtocol.Decode(Frame(0x41, [.. n, 1])).Should().Be(new FlPong(value, 1));
        FlProtocol.Decode(Frame(0x41, n)).Should().Be(new FlPong(value, 0));
        FlProtocol.Decode(Frame(0x49, n)).Should().Be(new FlTrackCount(value));
        FlProtocol.Decode(Frame(0x4B, n)).Should().Be(new FlListEnd(value));
        FlProtocol.Decode(Frame(0x4C, n)).Should().Be(new FlSelectedTrack(value));
        foreach (var code in Enum.GetValues<FlErrorCode>())
            FlProtocol.Decode(Frame(0x7F, [5, (byte)code])).Should().Be(new FlError(5, code));
        var state = FlProtocol.Decode(Frame(0x48, [.. n, .. n, 1, 0, 4, 1, 4, 0, 4, 86, 111, 105, 120])) as FlTrackState;
        state.Should().NotBeNull();
        state!.Track.Should().BeEquivalentTo(new DawTrack(value, "Voix", value / 16383f, true, false,
            [new(0, true), new(9, false)]));
        // Protocole 2 : noms des plugins des slots occupés, dans l'ordre des slots.
        var named = FlProtocol.Decode(Frame(0x48, [.. n, .. n, 1, 0, 4, 1, 4, 0, 4, 86, 111, 105, 120, 2, 81, 50, 3, 77, 111, 100])) as FlTrackState;
        named!.Track.Slots.Should().BeEquivalentTo(new DawSlot[] { new(0, true, "Q2"), new(9, false, "Mod") });
    }

    [Theory]
    [InlineData(0x41, 2)]
    [InlineData(0x49, 2)]
    [InlineData(0x4B, 2)]
    [InlineData(0x4C, 2)]
    [InlineData(0x7F, 2)]
    [InlineData(0x48, 11)]
    public void Truncated_and_non_midi_payloads_are_rejected(int command, int size)
    {
        FlProtocol.Decode(Frame((byte)command, new byte[size - 1])).Should().BeNull();
        var frame = Frame((byte)command, new byte[size]);
        FlProtocol.Decode(frame[..^1]).Should().BeNull();
        frame[5] = 128;
        FlProtocol.Decode(frame).Should().BeNull();
        frame[0] = 0;
        FlProtocol.Decode(frame).Should().BeNull();
        FlProtocol.Decode([]).Should().BeNull();
        FlProtocol.Decode(Frame(0x40)).Should().BeNull();
        FlProtocol.Decode(Frame(0x48, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 65])).Should().BeNull();
    }

    [Fact]
    public void Invalid_command_arguments_are_rejected_and_volumes_bounded()
    {
        foreach (var track in new[] { -1, 16384 })
        {
            Action act = () => FlProtocol.Select(track);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
        foreach (var slot in new[] { -1, 10 })
        {
            Action act = () => FlProtocol.Slot(0, slot, true);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
        FlProtocol.Volume(0, float.NaN).Should().Equal(FlProtocol.Volume(0, 0));
        FlProtocol.Volume(0, -1).Should().Equal(FlProtocol.Volume(0, 0));
        FlProtocol.Volume(0, 2).Should().Equal(FlProtocol.Volume(0, 1));
    }

    internal static byte[] Frame(byte command, params byte[] data) => [0xF0, 0x7D, 0x4D, 0x58, command, .. data, 0xF7];
}
