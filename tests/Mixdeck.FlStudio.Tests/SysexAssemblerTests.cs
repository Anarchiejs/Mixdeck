namespace Mixdeck.FlStudio.Tests;

public sealed class SysexAssemblerTests
{
    [Fact]
    public void Reassembles_more_than_two_4K_buffers_and_multiple_messages_in_order()
    {
        var assembler = new SysexAssembler();
        byte[] message = [0xF0, .. Enumerable.Repeat((byte)42, 9000), 0xF7];
        assembler.Push(message[..4096]).Should().BeEmpty();
        assembler.Push(message[4096..8192]).Should().BeEmpty();
        var tail = assembler.Push([.. message[8192..], 0xF0, 1, 0xF7]).ToArray();
        tail.Should().HaveCount(2);
        tail[0].Should().Equal(message);
        tail[1].Should().Equal(0xF0, 1, 0xF7);
    }

    [Fact]
    public void Resynchronizes_after_garbage_and_ignores_realtime_bytes()
    {
        var assembler = new SysexAssembler();
        assembler.Push([1, 2, 0xF7, 0xF0, 1, 0xF8, 0xFE]).Should().BeEmpty();
        assembler.Push([2, 0xF7]).Single().Should().Equal(0xF0, 1, 2, 0xF7);
        assembler.Push([0xF0, 1, 0xF0, 2, 0xF7]).Single().Should().Equal(0xF0, 2, 0xF7);
        assembler.Push([0xF0, 0x90, 1, 0xF7]).Should().BeEmpty();
    }
}
