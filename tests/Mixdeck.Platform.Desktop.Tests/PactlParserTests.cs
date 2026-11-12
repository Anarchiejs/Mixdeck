using Mixdeck.Platform.Desktop.Audio;

namespace Mixdeck.Platform.Desktop.Tests;

public sealed class PactlParserTests
{
    [Fact]
    public void Json_array_reads_devices_and_clamps_volume()
    {
        const string json = """
            [
              {
                "index": 0,
                "name": "alsa_output.speakers",
                "description": "Speakers",
                "mute": false,
                "volume": { "value": 32768 }
              },
              {
                "index": 1,
                "name": "alsa_output.loud",
                "description": "Loud",
                "mute": "yes",
                "volume": { "front-left": { "value": 131072 }, "front-right": { "value": 131072 } }
              }
            ]
            """;

        var devices = PactlParser.ParseDevices(json);
        devices.Should().HaveCount(2);
        devices[0].Id.Should().Be("alsa_output.speakers");
        devices[0].Name.Should().Be("Speakers");
        devices[0].Volume.Should().BeApproximately(0.5f, 0.001f);
        devices[0].Mute.Should().BeFalse();
        devices[1].Volume.Should().Be(1f);
        devices[1].Mute.Should().BeTrue();
    }

    [Fact]
    public void Json_object_map_reads_sessions()
    {
        const string json = """
            {
              "12": {
                "index": 12,
                "mute": false,
                "sink": "alsa_output.speakers",
                "volume": { "value_percent": "80%" },
                "properties": {
                  "application.name": "Firefox",
                  "application.process.binary": "firefox",
                  "application.process.id": "4242"
                }
              }
            }
            """;

        var sessions = PactlParser.ParseSessions(json);
        sessions.Should().ContainSingle();
        sessions[0].Id.Should().Be("12");
        sessions[0].ProcessId.Should().Be(4242);
        sessions[0].ProcessName.Should().Be("firefox");
        sessions[0].DisplayName.Should().Be("Firefox");
        sessions[0].Volume.Should().BeApproximately(0.8f, 0.001f);
        sessions[0].Sink.Should().Be("alsa_output.speakers");
    }

    [Fact]
    public void Text_list_reads_devices_sessions_and_defaults()
    {
        const string devices = """
            Sink #2
                Name: alsa_output.headphones
                Description: Headphones
                Mute: no
                Volume: front-left: 65536 / 100% / 0.00 dB,   front-right: 32768 / 50% / -6.00 dB
            Source #3
                Name: alsa_input.mic
                Description: Microphone
                Mute: yes
                Volume: mono: 65536 / 25,5% / 0.00 dB
            """;
        const string sessions = """
            Sink Input #7
                Sink: 2
                Mute: no
                Volume: front-left: 65536 / 40% / 0.00 dB
                application.name = "VLC"
                application.process.binary = "vlc"
                application.process.id = "99"
            """;
        const string info = """
            Default Sink: alsa_output.headphones
            Default Source: alsa_input.mic
            """;

        var parsed = PactlParser.ParseDevices(devices);
        parsed.Should().HaveCount(2);
        parsed[0].Name.Should().Be("Headphones");
        parsed[0].Volume.Should().BeApproximately(0.75f, 0.001f);
        parsed[1].Mute.Should().BeTrue();
        parsed[1].Volume.Should().BeApproximately(0.255f, 0.001f);

        var apps = PactlParser.ParseSessions(sessions);
        apps.Should().ContainSingle();
        apps[0].DisplayName.Should().Be("VLC");
        apps[0].ProcessName.Should().Be("vlc");
        apps[0].ProcessId.Should().Be(99);
        apps[0].Volume.Should().BeApproximately(0.4f, 0.001f);

        PactlParser.ParseDefaults(info).Should().Be(("alsa_output.headphones", "alsa_input.mic"));
    }

    [Fact]
    public void Json_defaults_use_pulse_field_names()
    {
        const string json = """
            { "default_sink_name": "speakers", "default_source_name": "mic" }
            """;
        PactlParser.ParseDefaults(json).Should().Be(("speakers", "mic"));
    }
}
