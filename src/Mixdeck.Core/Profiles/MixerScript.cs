using System.Globalization;
using System.Text;
namespace Mixdeck.Core.Profiles;
public sealed class MixerScript
{
    private readonly StringBuilder text = new();
    public MixerScript Set(string parameter, float value)
    {
        text.Append(parameter).Append('=').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        return this;
    }
    public MixerScript Set(string parameter, bool value) => Set(parameter, value ? 1f : 0f);
    public override string ToString() => text.ToString();
}
