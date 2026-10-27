using System.Runtime.InteropServices;
using Mixdeck.Core.Abstractions;
using Mixdeck.Platform.Audio.AudioPolicy;

namespace Mixdeck.Platform.Tests;

public sealed class AppAudioRouterTests
{
    private const string Endpoint = "{0.0.0.00000000}.{b9e99855-e24d-40ec-a5ad-11c86117cd91}";
    private const string Packed = @"\\?\SWD#MMDEVAPI#" + Endpoint + "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    [Theory]
    [InlineData(17133, null)]
    [InlineData(17134, "2a59116d-6c4f-45e0-a74f-707e3fef9258")]
    [InlineData(19044, "2a59116d-6c4f-45e0-a74f-707e3fef9258")]
    [InlineData(21389, "2a59116d-6c4f-45e0-a74f-707e3fef9258")]
    [InlineData(21390, "ab3d4648-e242-459f-b02f-541c70306324")]
    [InlineData(22000, "ab3d4648-e242-459f-b02f-541c70306324")]
    [InlineData(22631, "ab3d4648-e242-459f-b02f-541c70306324")]
    [InlineData(26100, "ab3d4648-e242-459f-b02f-541c70306324")]
    public void SelectsDocumentedIidBoundary(int build, string? expected)
        => AudioPolicyIds.ForBuild(build).Should().Be(expected is null ? null : new Guid(expected));

    [Fact]
    public void EndpointIsWrappedExactlyOnceAndRoundTrips()
    {
        AudioPolicyIds.Pack(Endpoint).Should().Be(Packed);
        AudioPolicyIds.TryUnpack(Packed, out var unpacked).Should().BeTrue();
        unpacked.Should().Be(Endpoint);
        AudioPolicyIds.TryUnpack(Packed.ToUpperInvariant(), out var uppercase).Should().BeTrue();
        uppercase.Should().Be(Endpoint.ToUpperInvariant());
        Action doublePack = () => AudioPolicyIds.Pack(Packed);
        doublePack.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NativeDefaultDecodesToNull(string? value)
    {
        AudioPolicyIds.TryUnpack(value, out var id).Should().BeTrue();
        id.Should().BeNull();
        AudioPolicyIds.Pack(null).Should().BeNull();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(Endpoint)]
    [InlineData(@"\\?\SWD#MMDEVAPI#" + Endpoint)]
    [InlineData(Endpoint + "#{e6327cad-dcec-4949-ae8a-991e976a79d2}")]
    [InlineData(@"\\?\SWD#MMDEVAPI##{e6327cad-dcec-4949-ae8a-991e976a79d2}")]
    [InlineData(@"\\?\SWD#MMDEVAPI#" + Endpoint + "#{2eef81be-33fa-4800-9670-1cd474972c3f}")]
    [InlineData(Packed + "trailing")]
    [InlineData("\0")]
    public void UnknownOrCaptureFormatIsNotMistakenForDefault(string value)
        => AudioPolicyIds.TryUnpack(value, out _).Should().BeFalse();

    [Fact]
    public void WritesAndVerifiesBothRolesWithoutTouchingCommunications()
    {
        var native = new FakePolicy();
        var router = new AppAudioRouter(() => native);
        router.SetEndpoint(42, Endpoint).Should().BeTrue();
        native.Writes.Should().Equal((AudioPolicyRole.Multimedia, Packed), (AudioPolicyRole.Console, Packed));
        router.TryGetEndpoints(42, out var multimedia, out var console).Should().BeTrue();
        multimedia.Should().Be(Endpoint);
        console.Should().Be(Endpoint);
        native.Disposals.Should().Be(2);
    }

    [Fact]
    public void ReturningToDefaultWritesNullForOnlyThisProcess()
    {
        var native = new FakePolicy { Multimedia = Packed, Console = Packed };
        var router = new AppAudioRouter(() => native);
        router.SetEndpoint(42, null).Should().BeTrue();
        native.Writes.Should().Equal((AudioPolicyRole.Multimedia, null), (AudioPolicyRole.Console, null));
        native.ProcessIds.Should().OnlyContain(id => id == 42);
    }

    [Fact]
    public void PartialFailureRestoresDistinctOriginalRoles()
    {
        string original = AudioPolicyIds.Pack("another-endpoint")!;
        var native = new FakePolicy { Multimedia = original, FailWrite = 2 };
        var router = new AppAudioRouter(() => native);
        router.SetEndpoint(42, Endpoint).Should().BeFalse();
        native.Writes.Should().Equal((AudioPolicyRole.Multimedia, Packed), (AudioPolicyRole.Console, Packed),
            (AudioPolicyRole.Multimedia, original), (AudioPolicyRole.Console, null));
        native.Multimedia.Should().Be(original);
        native.Console.Should().BeNull();
    }

    [Fact]
    public void SuccessfulHresultWithoutPersistenceIsFailureAndRestores()
    {
        var native = new FakePolicy { IgnoreWrites = true };
        new AppAudioRouter(() => native).SetEndpoint(42, Endpoint).Should().BeFalse();
        native.Writes.Should().HaveCount(4);
        native.Writes.Skip(2).Should().OnlyContain(write => write.Value == null);
    }

    [Fact]
    public void ReadFailureDoesNotEraseAnUnknownPreference()
    {
        var native = new FakePolicy { FailRead = true };
        var router = new AppAudioRouter(() => native);
        router.SetEndpoint(42, Endpoint).Should().BeFalse();
        native.Writes.Should().BeEmpty();
        router.TryGetEndpoints(42, out _, out _).Should().BeFalse();
        native.Disposals.Should().Be(2);
    }

    [Fact]
    public void UnknownNativeFormatDisablesRoutingBeforeWriting()
    {
        var native = new FakePolicy { Multimedia = "new-unknown-format" };
        var router = new AppAudioRouter(() => native);
        router.SetEndpoint(42, Endpoint).Should().BeFalse();
        router.IsSupported.Should().BeFalse();
        native.Writes.Should().BeEmpty();
        native.Disposals.Should().Be(1);
    }

    [Theory]
    [InlineData(unchecked((int)0x80004002))]
    [InlineData(unchecked((int)0x80040154))]
    [InlineData(unchecked((int)0x80004001))]
    public void MissingInterfaceIsUnsupportedAndDoesNotRetry(int hr)
    {
        int attempts = 0;
        var router = new AppAudioRouter(() => { attempts++; throw new COMException("API absente", hr); });
        router.IsSupported.Should().BeFalse();
        router.GetEndpoint(42).Should().BeNull();
        router.SetEndpoint(42, Endpoint).Should().BeFalse();
        attempts.Should().Be(1);
    }

    [Fact]
    public void MissingEntryPointFailsSafely()
    {
        var router = new AppAudioRouter(() => throw new EntryPointNotFoundException());
        router.IsSupported.Should().BeFalse();
        router.SetEndpoint(42, Endpoint).Should().BeFalse();
    }

    [Fact]
    public void TransientDenialDoesNotPermanentlyDisableTheApi()
    {
        int attempts = 0;
        var native = new FakePolicy();
        var router = new AppAudioRouter(() => ++attempts == 1 ? throw new COMException("Refus", unchecked((int)0x80070005)) : native);
        router.IsSupported.Should().BeFalse();
        router.IsSupported.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, Endpoint)]
    [InlineData(-1, Endpoint)]
    [InlineData(42, "")]
    [InlineData(42, " ")]
    [InlineData(42, "bad\0id")]
    [InlineData(42, Packed)]
    public void InvalidInputsNeverOpenTheNativeApi(int processId, string endpoint)
    {
        int calls = 0;
        var router = new AppAudioRouter(() => { calls++; return new FakePolicy(); });
        router.SetEndpoint(processId, endpoint).Should().BeFalse();
        calls.Should().Be(0);
    }

    [Fact]
    public void EnumerationRemainsAvailableWhenPolicyIsMissing()
    {
        AudioEndpoint[] endpoints = [new(Endpoint, "Voicemeeter AUX Input")];
        var router = new AppAudioRouter(() => throw new COMException("Absent", PrivateApiNative.NoInterface), () => endpoints);
        router.IsSupported.Should().BeFalse();
        router.GetRenderEndpoints().Should().Equal(endpoints);
    }

    private sealed class FakePolicy : IAudioPolicySession
    {
        internal string? Multimedia;
        internal string? Console;
        internal int FailWrite;
        internal bool FailRead;
        internal bool IgnoreWrites;
        internal int Disposals;
        internal List<(AudioPolicyRole Role, string? Value)> Writes { get; } = [];
        internal List<int> ProcessIds { get; } = [];

        public string? Get(int processId, AudioPolicyRole role)
        {
            ProcessIds.Add(processId);
            if (FailRead) throw new COMException("Lecture refusée", unchecked((int)0x80070005));
            return role == AudioPolicyRole.Multimedia ? Multimedia : Console;
        }

        public void Set(int processId, AudioPolicyRole role, string? persistedId)
        {
            ProcessIds.Add(processId);
            Writes.Add((role, persistedId));
            if (Writes.Count == FailWrite) throw new COMException("Écriture refusée", unchecked((int)0x80070005));
            if (IgnoreWrites) return;
            if (role == AudioPolicyRole.Multimedia) Multimedia = persistedId;
            else Console = persistedId;
        }

        public void Dispose() => Disposals++;
    }
}
