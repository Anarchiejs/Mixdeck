using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mixdeck.Core.Model;

namespace Mixdeck.Voicemeeter.Tests;

public sealed class DeviceAndLevelRequestTests
{
    private static VoicemeeterMixer Create(SimulatedVoicemeeterApi api, TimeProvider time) =>
        new(api, NullLogger<VoicemeeterMixer>.Instance, time);

    [Fact]
    public async Task LevelRequestsAccumulateWithTheSwitchAndDisposeOnlyOnceAcrossThreads()
    {
        var api = new SimulatedVoicemeeterApi(42);
        var time = new FakeTimeProvider();
        await using var mixer = Create(api, time);
        mixer.Tick();
        var first = mixer.RequestLevels();
        var second = mixer.RequestLevels();
        mixer.Tick();
        api.GetLevelCallCount.Should().Be(20);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(first.Dispose)));
        time.Advance(TimeSpan.FromMilliseconds(16)); mixer.Tick();
        api.GetLevelCallCount.Should().Be(40);
        mixer.LevelsEnabled = true;
        second.Dispose();
        time.Advance(TimeSpan.FromMilliseconds(16)); mixer.Tick();
        api.GetLevelCallCount.Should().Be(60);
        mixer.LevelsEnabled = false;
        first.Dispose(); second.Dispose();
        time.Advance(TimeSpan.FromSeconds(1)); mixer.Tick();
        api.GetLevelCallCount.Should().Be(60);
        using var third = mixer.RequestLevels();
        mixer.Tick();
        api.GetLevelCallCount.Should().Be(80);
    }

    [Fact]
    public async Task DeferredReadsPreserveWriteOrderAndSupportUnicodeAndDeviceRemoval()
    {
        var api = new SimulatedVoicemeeterApi();
        await using var mixer = Create(api, new FakeTimeProvider());
        mixer.Tick();
        mixer.SetString(VmParam.StripDevice(0, AudioDeviceDriver.Wdm), "Micro été 🎙");
        var read = mixer.GetStringAsync(VmParam.StripDeviceName(0));
        var inputs = mixer.GetDevicesAsync(AudioDeviceDirection.Input);
        var outputs = mixer.GetDevicesAsync(AudioDeviceDirection.Output);
        read.IsCompleted.Should().BeFalse(); inputs.IsCompleted.Should().BeFalse();
        api.Calls.Should().NotContain(c => c.Method == nameof(api.InputGetDeviceNumber));
        mixer.Tick();
        (await read).Should().Be("Micro été 🎙");
        (await inputs).Should().Contain(d => d.Driver == AudioDeviceDriver.Wdm && d.Name.Contains("Microphone") && d.HardwareId.Length > 0);
        (await outputs).Should().Contain(d => d.Name.Contains("Casque"));
        (await inputs).Should().NotContain(d => d.Name.Contains("Casque"));
        mixer.SetString(VmParam.StripDevice(0, AudioDeviceDriver.Wdm), "");
        var removed = mixer.GetStringAsync(VmParam.StripDeviceName(0));
        var refused = mixer.GetStringAsync("Unknown.Parameter");
        mixer.Tick();
        (await removed).Should().BeEmpty();
        (await refused).Should().BeNull();
        mixer.State.Should().Be(MixerConnectionState.Connected);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task OfflineReadsReturnEmptyWithoutCallingTheDeviceApi(bool installed, bool running)
    {
        var api = new SimulatedVoicemeeterApi { IsInstalled = installed };
        if (!running) api.SimulateEngineStopped();
        await using var mixer = Create(api, new FakeTimeProvider());
        mixer.Tick();
        (await mixer.GetDevicesAsync(AudioDeviceDirection.Input)).Should().BeEmpty();
        (await mixer.GetDevicesAsync(AudioDeviceDirection.Output)).Should().BeEmpty();
        (await mixer.GetStringAsync(VmParam.StripDeviceName(0))).Should().BeNull();
        api.Calls.Should().NotContain(c => c.Method.Contains("Device"));
        using var request = mixer.RequestLevels();
        mixer.Tick();
        api.GetLevelCallCount.Should().Be(0);
    }

    [Fact]
    public async Task QueuedReadsFinishWhenConnectionFailsOrMixerIsDisposed()
    {
        var api = new SimulatedVoicemeeterApi();
        var time = new FakeTimeProvider();
        var mixer = Create(api, time);
        mixer.Tick();
        var devices = mixer.GetDevicesAsync(AudioDeviceDirection.Input);
        var text = mixer.GetStringAsync(VmParam.StripDeviceName(0));
        api.SimulateEngineStopped();
        mixer.Tick();
        (await devices).Should().BeEmpty();
        (await text).Should().BeNull();
        api.SimulateEngineStarted();
        time.Advance(TimeSpan.FromSeconds(2)); mixer.Tick();
        devices = mixer.GetDevicesAsync(AudioDeviceDirection.Output);
        text = mixer.GetStringAsync(VmParam.BusDeviceName(0));
        using var request = mixer.RequestLevels();
        await mixer.DisposeAsync();
        (await devices).Should().BeEmpty();
        (await text).Should().BeNull();
        Action acquire = () => mixer.RequestLevels();
        acquire.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task CancelledReadsNeverCallTheApi()
    {
        var api = new SimulatedVoicemeeterApi();
        await using var mixer = Create(api, new FakeTimeProvider());
        mixer.Tick();
        using var cancellation = new CancellationTokenSource();
        var devices = mixer.GetDevicesAsync(AudioDeviceDirection.Input, cancellation.Token);
        var text = mixer.GetStringAsync("Custom.Read", cancellation.Token);
        cancellation.Cancel(); mixer.Tick();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => devices);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => text);
        api.Calls.Should().NotContain(c => c.Method.Contains("Device") || c.Parameter == "Custom.Read");
    }

    [Fact]
    public async Task WorkerExecutesEveryReadOnItsDedicatedThread()
    {
        var api = new SimulatedVoicemeeterApi();
        await using var mixer = Create(api, TimeProvider.System);
        await mixer.StartAsync().WaitAsync(TimeSpan.FromSeconds(2));
        var inputs = await mixer.GetDevicesAsync(AudioDeviceDirection.Input).WaitAsync(TimeSpan.FromSeconds(2));
        var outputs = await mixer.GetDevicesAsync(AudioDeviceDirection.Output).WaitAsync(TimeSpan.FromSeconds(2));
        (await mixer.GetStringAsync(VmParam.BusDeviceName(0)).WaitAsync(TimeSpan.FromSeconds(2))).Should().Be("Casque (Realtek Audio)");
        inputs.Should().NotBeEmpty(); outputs.Should().NotBeEmpty();
        api.Calls.Select(c => c.ThreadId).Distinct().Should().ContainSingle().Which.Should().NotBe(Environment.CurrentManagedThreadId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HideWindowWaitsForConnectionAndRespectsPreference(bool hide)
    {
        var api = new SimulatedVoicemeeterApi();
        api.SimulateEngineStopped();
        await using var mixer = Create(api, new FakeTimeProvider());
        mixer.Tick();
        var hideTask = mixer.HideWindowWhenConnectedAsync(hide);
        var launch = mixer.LaunchVoicemeeterAsync();
        mixer.Tick(); (await launch).Should().BeTrue();
        mixer.Tick();
        await hideTask.WaitAsync(TimeSpan.FromSeconds(2));
        mixer.Tick();
        api.Calls.Count(c => c.Method == nameof(api.SetParameterFloat) && c.Parameter == VmParam.CommandShow).Should().Be(hide ? 1 : 0);
    }
}
