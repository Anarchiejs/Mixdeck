namespace Mixdeck.FlStudio;

/// <summary>Recolle les tampons MIDI jusqu'à F7, sans confondre horloge et données SysEx.</summary>
internal sealed class SysexAssembler
{
    private readonly List<byte> _pending = [];

    public IEnumerable<byte[]> Push(byte[] data)
    {
        foreach (var b in data)
        {
            if (b >= 0xF8) continue;
            if (b == 0xF0) { _pending.Clear(); _pending.Add(b); }
            else if (_pending.Count > 0)
            {
                _pending.Add(b);
                if (b == 0xF7)
                {
                    var message = _pending.ToArray();
                    _pending.Clear();
                    yield return message;
                }
                else if (b > 0x7F || _pending.Count > 1024 * 1024) _pending.Clear();
            }
        }
    }
}
