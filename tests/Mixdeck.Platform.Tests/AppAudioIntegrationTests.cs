using System.Runtime.InteropServices;
using Mixdeck.Platform.Audio;
using Xunit.Abstractions;

namespace Mixdeck.Platform.Tests;

public sealed class AppAudioIntegrationTests(ITestOutputHelper output)
{
    [AppAudioOutputFact]
    public async Task EnumeratesRealSessionsWhileSoundPlaysAndReleasesNotifications()
    {
        var path = Path.Combine(Path.GetTempPath(), "mixdeck-audio-" + Guid.NewGuid() + ".wav");
        WriteTone(path);
        try
        {
            // Son bref et faible, limité au processus de test ; aucun volume existant n'est modifié.
            await using var service = new AppAudioSessions();
            service.Start();
            service.Start();
            await service.Initialization.WaitAsync(TimeSpan.FromSeconds(10));
            service.HasDefaultEndpoint.Should().BeTrue();
            var playing = PlaySound(path, 0, 0x0002000B); // FILENAME | ASYNC | LOOP | NODEFAULT
            output.WriteLine("Lecture du son de vérification : " + playing);
            await Task.Delay(650); // Laisse arriver les notifications, sans sondage.
            output.WriteLine("Sortie multimédia par défaut : " + service.HasDefaultEndpoint);
            foreach (var session in service.Sessions)
                output.WriteLine($"{session.DisplayName} | {session.ProcessName} | PID {session.ProcessId} | {session.Volume:P0} | muet={session.Mute} | système={session.IsSystemSounds}");
            service.Sessions.Should().OnlyContain(session => !string.IsNullOrWhiteSpace(session.Id)
                && !string.IsNullOrWhiteSpace(session.DisplayName) && session.Volume >= 0 && session.Volume <= 1);
        }
        finally
        {
            PlaySound(null, 0, 0);
            File.Delete(path);
        }
    }

    private static void WriteTone(string path)
    {
        const int rate = 16000;
        const int count = rate;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
        writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(count * 2);
        for (var i = 0; i < count; i++)
        {
            var envelope = Math.Min(1, Math.Min(i, count - 1 - i) / 320d);
            writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * 500 * envelope));
        }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string? sound, nint module, uint flags);
}

public sealed class AppAudioOutputFactAttribute : FactAttribute
{
    public AppAudioOutputFactAttribute()
    {
        // La découverte ne doit pas laisser de RCW sur son thread ; la sonde possède son MTA.
        var available = Task.Run(() =>
        {
            var initialized = CoreAudioInterop.CoInitializeEx(0, 0) >= 0;
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                var result = enumerator.GetDefaultAudioEndpoint(0, 1, out device);
                if (result == unchecked((int)0x80070490)) return false;
                CoreAudioInterop.Check(result);
                return true;
            }
            finally
            {
                CoreAudioInterop.Release(device);
                CoreAudioInterop.Release(enumerator);
                if (initialized) CoreAudioInterop.CoUninitialize();
            }
        }).GetAwaiter().GetResult();
        if (!available) Skip = "Aucune sortie audio multimédia par défaut sur cette machine.";
    }
}
