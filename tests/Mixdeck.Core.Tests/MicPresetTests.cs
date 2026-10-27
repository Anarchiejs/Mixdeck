using System.Globalization;
using Mixdeck.Core.Audio;
using Mixdeck.Core.Settings;

namespace Mixdeck.Core.Tests;

public sealed class MicPresetTests
{
    [Fact]
    public void TableContainsEveryPresetOnceWithinBananaRanges()
    {
        MicPresets.All.Select(p => p.Preset).Should().BeEquivalentTo(Enum.GetValues<MicPreset>());
        MicPresets.All.Should().OnlyContain(p => p.Gate >= 0 && p.Gate <= 10 && p.Comp >= 0 && p.Comp <= 10
            && p.ColorX >= -0.5 && p.ColorX <= 0.5 && p.ColorY >= 0 && p.ColorY <= 1
            && p.Name.Length > 0 && p.Description.Length > 0);
        MicPresets.All.Single(p => p.Preset == MicPreset.Off).Should().Be(new MicPresetDefinition(MicPreset.Off,
            "Aucun", MicPresets.All[0].Description, 0, 0, 0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryScriptTargetsOnlyTheRequestedPhysicalStripAndResetsAllFourParameters(int strip)
    {
        foreach (var preset in MicPresets.All)
        {
            var commands = MicPresets.CreateScript(strip, preset.Preset).Split(';', StringSplitOptions.RemoveEmptyEntries);
            commands.Should().HaveCount(4).And.OnlyContain(c => c.StartsWith($"Strip[{strip}].", StringComparison.Ordinal));
            commands.Select(c => c.Split('=')[0]).Should().Equal($"Strip[{strip}].Gate", $"Strip[{strip}].Comp", $"Strip[{strip}].Color_x", $"Strip[{strip}].Color_y");
            commands.Select(c => float.Parse(c.Split('=')[1], CultureInfo.InvariantCulture)).Should().Equal(preset.Gate, preset.Comp, preset.ColorX, preset.ColorY);
        }
    }

    [Fact]
    public void FrenchCultureStillProducesRemoteDecimalPoints()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            MicPresets.CreateScript(2, MicPreset.Voice).Should().Be("Strip[2].Gate=2;Strip[2].Comp=3;Strip[2].Color_x=0.15;Strip[2].Color_y=0;");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(4)]
    public void VirtualAndInvalidMicrophonesAreRejected(int strip)
    {
        Action act = () => MicPresets.CreateScript(strip, MicPreset.Voice);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UnknownPresetIsRejected()
    {
        Action act = () => MicPresets.CreateScript(0, (MicPreset)99);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
