using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mixdeck.Core.Model;
using Mixdeck.Voicemeeter.Native;

namespace Mixdeck.Voicemeeter.Tests;

public sealed class VoicemeeterMixerTests
{
    private static VoicemeeterMixer Create(IVoicemeeterApi api, FakeTimeProvider time, VoicemeeterMixerOptions? options = null) =>
        new(api, NullLogger<VoicemeeterMixer>.Instance, time, options);

    [Theory]
    [InlineData(false, true, 2, MixerConnectionState.NotInstalled)]
    [InlineData(true, false, 2, MixerConnectionState.VoicemeeterNotRunning)]
    [InlineData(true, true, 1, MixerConnectionState.Connected)]
    [InlineData(true, true, 3, MixerConnectionState.Connected)]
    [InlineData(true, true, 4, MixerConnectionState.UnsupportedType)]
    [InlineData(true, true, 2, MixerConnectionState.Connected)]
    public async Task ConnectsToExpectedState(bool installed, bool running, int type, MixerConnectionState expected)
    {
        var api = new SimulatedVoicemeeterApi(42) { IsInstalled = installed, MixerType = type };
        if (!running) api.SimulateEngineStopped();
        await using var mixer = Create(api, new());
        var events = new List<MixerConnectionState>();
        mixer.StateChanged += (_, state) => events.Add(state);
        mixer.Tick();
        mixer.State.Should().Be(expected);
        events.Should().Equal(expected);
        if (expected == MixerConnectionState.Connected)
        {
            mixer.VoicemeeterType.Should().Be(type);
            mixer.VoicemeeterVersion.Should().Be(api.Version);
            mixer.Snapshot.Strips[0].Label.Should().Be("Micro");
            api.Calls.Take(3).Select(c => c.Method).Should().Equal("Login", "IsParametersDirty", "GetVoicemeeterType");
        }
    }

    [Fact]
    public async Task RetriesMissingInstallationOnlyAfterThirtySeconds()
    {
        var api = new SimulatedVoicemeeterApi { IsInstalled = false };
        var time = new FakeTimeProvider();
        await using var mixer = Create(api, time);
        mixer.Tick();
        api.IsInstalled = true;
        time.Advance(TimeSpan.FromSeconds(29)); mixer.Tick();
        api.LoginCallCount.Should().Be(1);
        time.Advance(TimeSpan.FromSeconds(1)); mixer.Tick();
        mixer.State.Should().Be(MixerConnectionState.Connected);
    }

    [Fact]
    public async Task UnreadableTypeRetriesAndReconnects()
    {
        var api = new SimulatedVoicemeeterApi { TypeReadResult = -1 };
        var time = new FakeTimeProvider();
        await using var mixer = Create(api, time);
        mixer.Tick();
        mixer.State.Should().Be(MixerConnectionState.VoicemeeterNotRunning);
        api.TypeReadResult = 0;
        time.Advance(TimeSpan.FromSeconds(2)); mixer.Tick();
        mixer.State.Should().Be(MixerConnectionState.Connected);
        api.LogoutCallCount.Should().Be(1);
    }

    [Fact]
    public async Task FailedLoginRetriesAndAlreadyLoggedInSessionIsAccepted()
    {
        var api = new ApiHarness { LoginOverride = -1 };
        var time = new FakeTimeProvider();
        await using var mixer = Create(api, time);
        mixer.Tick();
        mixer.State.Should().Be(MixerConnectionState.VoicemeeterNotRunning);
        api.Inner.Login();
        api.LoginOverride = null;
        time.Advance(TimeSpan.FromSeconds(2)); mixer.Tick();
        mixer.State.Should().Be(MixerConnectionState.Connected);
        mixer.Snapshot.Strips[0].Label.Should().Be("Micro");
    }

    [Fact]
    public async Task FailedReadNeverPublishesAPartialSnapshotAndReconnectRefreshesIt()
    {
        var api = new ApiHarness();
        var time = new FakeTimeProvider();
        await using var mixer = Create(api, time);
        mixer.Tick();
        var previous = mixer.Snapshot;
        api.Inner.SimulateExternalChange(VmParam.StripGain(0), -19);
        api.FailStringRead = VmParam.BusLabel(4);
        time.Advance(TimeSpan.FromMilliseconds(33)); mixer.Tick();
        mixer.State.Should().Be(MixerConnectionState.VoicemeeterNotRunning);
        mixer.Snapshot.Should().BeSameAs(previous);
        api.FailStringRead = null;
        time.Advance(TimeSpan.FromSeconds(2)); mixer.Tick();
        mixer.Snapshot.Revision.Should().Be(previous.Revision + 1);
        mixer.Snapshot.Strips[0].Gain.Should().Be(-19);
    }

    [Fact]
    public async Task WritesAreDeferredAndDirtyPublishesOnlyRealChanges()
    {
        var api = new SimulatedVoicemeeterApi(1);
        var time = new FakeTimeProvider();
        await using var mixer = Create(api, time);
        mixer.Tick();
        var before = mixer.Snapshot;
        var changes = new List<MixerSnapshot>();
        mixer.SnapshotChanged += (_, snapshot) => changes.Add(snapshot);
        mixer.SetFloat(VmParam.StripGain(0), -12);
        mixer.SetString(VmParam.StripLabel(0), "Micro été 🎙");
        api.SetFloatCallCount.Should().Be(0);
        mixer.Tick();
        mixer.Snapshot.Should().BeSameAs(before);
        time.Advance(TimeSpan.FromMilliseconds(33)); mixer.Tick();
        mixer.Snapshot.Revision.Should().Be(before.Revision + 1);
        mixer.Snapshot.Strips[0].Gain.Should().Be(-12);
        mixer.Snapshot.Strips[0].Label.Should().Be("Micro été 🎙");
        changes.Should().HaveCount(1);
        api.SimulateExternalChange(VmParam.StripGain(0), -12);
        time.Advance(TimeSpan.FromMilliseconds(33)); mixer.Tick();
        changes.Should().HaveCount(1);
        before.Strips[0].Gain.Should().Be(-3);
    }

    [Fact]
    public async Task ReadsEverySnapshotFieldAfterExternalChanges()
    {
        var api = new SimulatedVoicemeeterApi();
        var time = new FakeTimeProvider();
        await using var mixer = Create(api, time);
        mixer.Tick();
        api.SimulateExternalChange(VmParam.StripGain(4), -7);
        api.SimulateExternalChange(VmParam.StripMute(4), 1);
        api.SimulateExternalChange(VmParam.StripSolo(4), 1);
        api.SimulateExternalChange(VmParam.StripPanX(4), 0.25f);
        api.SimulateExternalChange(VmParam.StripRoute(4, 4), 1);
        api.SimulateExternalChange(VmParam.StripEqGain(4, 2), 6);
        api.SimulateExternalChange(VmParam.StripLabel(4), "Externe");
        api.SimulateExternalChange(VmParam.BusGain(4), -9);
        api.SimulateExternalChange(VmParam.BusMute(4), 1);
        api.SimulateExternalChange(VmParam.BusMono(4), 1);
        api.SimulateExternalChange(VmParam.BusEqOn(4), 1);
        api.SimulateExternalChange(VmParam.BusLabel(4), "Sortie");
        time.Advance(TimeSpan.FromMilliseconds(33)); mixer.Tick();
        var strip = mixer.Snapshot.Strips[4];
        strip.Gain.Should().Be(-7); strip.Mute.Should().BeTrue(); strip.Solo.Should().BeTrue();
        strip.PanX.Should().Be(0.25f); strip.Routes[4].Should().BeTrue(); strip.EqGains.Should().Equal(0, 0, 6);
        strip.Label.Should().Be("Externe");
        mixer.Snapshot.Buses[4].Should().Be(new BusState(4, "Sortie", -9, true, true, true));
        mixer.Snapshot.Strips[0].EqGains.Should().BeEmpty();
    }

    [Fact]
    public async Task CoalescesWritesAndPreservesScriptsAndTheirOrder()
    {
        var api = new SimulatedVoicemeeterApi();
        var time = new FakeTimeProvider();
        await using var mixer = Create(api, time);
        mixer.Tick();
        for (var i = 0; i < 100; i++)
        {
            mixer.SetFloat(VmParam.StripGain(0), -i / 10f);
            mixer.SetString(VmParam.StripLabel(0), $"Micro {i}");
        }
        const string first = "Strip[0].Mute=1; Strip[1].Gain=-6.0";
        const string second = "Strip[0].Gain=-2;";
        mixer.ApplyScript(first);
        mixer.ApplyScript(second);
        mixer.SetFloat(VmParam.StripGain(0), -8);
        time.Advance(TimeSpan.FromMilliseconds(33)); mixer.Tick();
        api.SetFloatCallCount.Should().Be(2);
        api.SetStringCallCount.Should().Be(1);
        api.Scripts.Should().Equal(first, second);
        mixer.Snapshot.Strips[0].Gain.Should().Be(-8);
        mixer.Snapshot.Strips[0].Label.Should().Be("Micro 99");
        mixer.Snapshot.Strips[0].Mute.Should().BeTrue();
        mixer.Snapshot.Strips[1].Gain.Should().Be(-6);
    }

    [Fact]
    public async Task EngineRestartReconnectsAndRefreshesAllValuesWithoutReplayingOfflineWrites()
    {
        var api = new SimulatedVoicemeeterApi();
        var time = new FakeTimeProvider();
        await using var mixer = Create(api, time);
        mixer.Tick();
        api.SimulateEngineStopped();
        time.Advance(TimeSpan.FromMilliseconds(33)); mixer.Tick();
        mixer.State.Should().Be(MixerConnectionState.VoicemeeterNotRunning);
        mixer.SetFloat(VmParam.StripGain(0), 12);
        mixer.SetString(VmParam.StripLabel(0), "Ignoré");
        mixer.ApplyScript("Strip[0].Mute=1");
        api.SimulateExternalChange(VmParam.StripGain(0), -17);
        api.SimulateEngineStarted();
        time.Advance(TimeSpan.FromMilliseconds(1999)); mixer.Tick();
        api.LoginCallCount.Should().Be(1);
        time.Advance(TimeSpan.FromMilliseconds(1)); mixer.Tick();
        mixer.State.Should().Be(MixerConnectionState.Connected);
        mixer.Snapshot.Strips[0].Gain.Should().Be(-17);
        api.SetFloatCallCount.Should().Be(0);
        api.SetStringCallCount.Should().Be(0);
        api.Scripts.Should().BeEmpty();
    }

    [Fact]
    public async Task SamplesCorrectStereoOffsetsAtRequestedRateAndStopsWhenHidden()
    {
        var api = new SimulatedVoicemeeterApi(123);
        var time = new FakeTimeProvider();
        await using var mixer = Create(api, time, new() { LevelType = 2 });
        mixer.Tick();
        time.Advance(TimeSpan.FromSeconds(1)); mixer.Tick();
        api.GetLevelCallCount.Should().Be(0);
        var frames = new List<LevelFrame>();
        mixer.LevelsUpdated += (_, frame) => frames.Add(frame);
        mixer.LevelsEnabled = true;
        mixer.Tick();
        api.GetLevelCallCount.Should().Be(20);
        api.Calls.Where(c => c.Method == "GetLevel").Select(c => (c.Type, c.Channel)).Should().Equal(
            new (int?, int?)[] { (2, 0), (2, 1), (2, 2), (2, 3), (2, 4), (2, 5), (2, 6), (2, 7), (2, 14), (2, 15),
                (3, 0), (3, 1), (3, 8), (3, 9), (3, 16), (3, 17), (3, 24), (3, 25), (3, 32), (3, 33) });
        frames.Should().HaveCount(1);
        frames[0].Strips.Should().HaveCount(5);
        frames[0].Buses.Should().HaveCount(5);
        frames[0].Strips.Should().OnlyContain(level => level.Left > 0 && level.Right > 0);
        time.Advance(TimeSpan.FromMilliseconds(15)); mixer.Tick();
        api.GetLevelCallCount.Should().Be(20);
        time.Advance(TimeSpan.FromMilliseconds(1)); mixer.Tick();
        api.GetLevelCallCount.Should().Be(40);
        frames[1].TimestampTicks.Should().BeGreaterThan(frames[0].TimestampTicks);
        mixer.LevelsEnabled = false;
        time.Advance(TimeSpan.FromSeconds(1)); mixer.Tick();
        api.GetLevelCallCount.Should().Be(40);
        api.SimulateEngineStopped();
        mixer.LevelsEnabled = true;
        time.Advance(TimeSpan.FromSeconds(1)); mixer.Tick();
        api.GetLevelCallCount.Should().Be(40);
    }

    [Fact]
    public async Task LaunchFallsBackToBanana32OnTheApiThread()
    {
        var api = new SimulatedVoicemeeterApi { Run64Result = -1 };
        api.SimulateEngineStopped();
        var time = new FakeTimeProvider();
        await using var mixer = Create(api, time);
        mixer.Tick();
        var launch = mixer.LaunchVoicemeeterAsync();
        launch.IsCompleted.Should().BeFalse();
        mixer.Tick();
        (await launch).Should().BeTrue();
        api.Calls.Where(c => c.Method == "RunVoicemeeter").Select(c => c.Type).Should().Equal(5, 2);
        mixer.Tick();
        mixer.State.Should().Be(MixerConnectionState.Connected);
    }

    [Fact]
    public async Task CancelledAndPendingLaunchesCompleteWithoutCallingRun()
    {
        var api = new SimulatedVoicemeeterApi();
        var mixer = Create(api, new());
        using var cancellation = new CancellationTokenSource();
        var cancelled = mixer.LaunchVoicemeeterAsync(cancellation.Token);
        cancellation.Cancel();
        mixer.Tick();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var pending = mixer.LaunchVoicemeeterAsync();
        await mixer.DisposeAsync();
        (await pending).Should().BeFalse();
        api.Calls.Should().NotContain(c => c.Method == "RunVoicemeeter");
        Action write = () => mixer.SetFloat(VmParam.StripMute(0), 1);
        write.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task NewCommandWakesWorkerWithoutWaitingForTheNextPoll()
    {
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new ApiHarness { Written = () => written.TrySetResult() };
        await using var mixer = new VoicemeeterMixer(api, NullLogger<VoicemeeterMixer>.Instance,
            TimeProvider.System, new() { DirtyPollInterval = TimeSpan.FromSeconds(10) });
        await mixer.StartAsync().WaitAsync(TimeSpan.FromSeconds(2));
        mixer.SetFloat(VmParam.StripMute(0), 1);
        await written.Task.WaitAsync(TimeSpan.FromMilliseconds(500));
        api.Inner.SetFloatCallCount.Should().Be(1);
        api.Inner.Calls.Select(c => c.ThreadId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task SampledValuesCorrespondToTheirChannelsAndFailedChannelsAreSilent()
    {
        var api = new ApiHarness { ChannelLevels = true };
        await using var mixer = Create(api, new());
        mixer.LevelsEnabled = true;
        mixer.Tick();
        for (var i = 0; i < MixerLayout.Banana.StripCount; i++)
        {
            var offset = MixerLayout.Banana.InputChannelOffset(i);
            mixer.Levels.Strips[i].Should().Be(new StereoLevel(100 + offset, offset == 14 ? 0 : 101 + offset));
        }
        for (var i = 0; i < MixerLayout.Banana.BusCount; i++)
        {
            var offset = MixerLayout.Banana.OutputChannelOffset(i);
            mixer.Levels.Buses[i].Should().Be(new StereoLevel(300 + offset, 301 + offset));
        }
    }

    [Fact]
    public async Task RealThreadSurvivesEventExceptionsWritesAndDisposesPromptly()
    {
        var api = new SimulatedVoicemeeterApi();
        var mixer = new VoicemeeterMixer(api, NullLogger<VoicemeeterMixer>.Instance, TimeProvider.System);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mixer.StateChanged += (_, _) => throw new InvalidOperationException("Erreur de test");
        mixer.SnapshotChanged += (_, _) => throw new InvalidOperationException("Erreur de test");
        mixer.LevelsUpdated += (_, _) => throw new InvalidOperationException("Erreur de test");
        mixer.SnapshotChanged += (_, snapshot) => { if (snapshot.Strips[0].Mute) changed.TrySetResult(); };
        try
        {
            await mixer.StartAsync().WaitAsync(TimeSpan.FromSeconds(2));
            mixer.LevelsEnabled = true;
            mixer.SetFloat(VmParam.StripMute(0), 1);
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            (await mixer.LaunchVoicemeeterAsync().WaitAsync(TimeSpan.FromSeconds(1))).Should().BeTrue();
        }
        finally { await mixer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)); }
        mixer.State.Should().Be(MixerConnectionState.Disconnected);
        api.LogoutCallCount.Should().Be(1);
        api.Calls.Select(c => c.ThreadId).Distinct().Should().ContainSingle();
        await mixer.DisposeAsync();
    }

    [Fact]
    public async Task WritesAcceptedBeforeDisposeAreSentBeforeLogout()
    {
        using var firstWrite = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var blocked = 0;
        var api = new ApiHarness
        {
            Written = () =>
            {
                if (Interlocked.Exchange(ref blocked, 1) != 0) return;
                firstWrite.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            },
        };
        var mixer = new VoicemeeterMixer(api, NullLogger<VoicemeeterMixer>.Instance, TimeProvider.System);
        await mixer.StartAsync().WaitAsync(TimeSpan.FromSeconds(2));
        mixer.SetFloat(VmParam.StripGain(0), -3);
        firstWrite.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        // Le thread est occupé : cette écriture (ex. le gain rendu par l'atténuation) attend dans la file.
        mixer.SetFloat(VmParam.StripGain(1), -7);
        var disposing = mixer.DisposeAsync().AsTask();
        release.Set();
        await disposing.WaitAsync(TimeSpan.FromSeconds(3));
        api.Inner.Calls.Should().Contain(c => c.Method == nameof(api.SetParameterFloat) && c.Parameter == VmParam.StripGain(1));
    }

    [Fact]
    public async Task SimulatorIsDeterministicWithASeed()
    {
        var first = new SimulatedVoicemeeterApi(42);
        var second = new SimulatedVoicemeeterApi(42);
        first.Login(); second.Login();
        for (var i = 0; i < 100; i++)
        {
            first.GetLevel(1, i % 22, out var a); second.GetLevel(1, i % 22, out var b);
            a.Should().Be(b);
        }
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("\"C:\\Program Files (x86)\\VB\\Voicemeeter\\uninstall.exe\" /S")]
    [InlineData("C:\\Program Files (x86)\\VB\\Voicemeeter\\uninstall.exe /S")]
    public void FindsDirectoryInUninstallCommand(string command) =>
        VoicemeeterLocator.DirectoryFromUninstallString(command).Should().Be(@"C:\Program Files (x86)\VB\Voicemeeter");

    private sealed class ApiHarness : IVoicemeeterApi
    {
        internal SimulatedVoicemeeterApi Inner { get; } = new(42);
        internal int? LoginOverride { get; set; }
        internal string? FailStringRead { get; set; }
        internal Action? Written { get; init; }
        internal bool ChannelLevels { get; init; }
        public int InputGetDeviceNumber() => Inner.InputGetDeviceNumber();
        public int OutputGetDeviceNumber() => Inner.OutputGetDeviceNumber();
        public int InputGetDeviceDesc(int index, out int type, out string name, out string hardwareId) => Inner.InputGetDeviceDesc(index, out type, out name, out hardwareId);
        public int OutputGetDeviceDesc(int index, out int type, out string name, out string hardwareId) => Inner.OutputGetDeviceDesc(index, out type, out name, out hardwareId);
        public int Login() => LoginOverride ?? Inner.Login();
        public int Logout() => Inner.Logout();
        public int RunVoicemeeter(int type) => Inner.RunVoicemeeter(type);
        public int GetVoicemeeterType(out int type) => Inner.GetVoicemeeterType(out type);
        public int GetVoicemeeterVersion(out int version) => Inner.GetVoicemeeterVersion(out version);
        public int IsParametersDirty() => Inner.IsParametersDirty();
        public int GetParameterFloat(string name, out float value) => Inner.GetParameterFloat(name, out value);
        public int GetParameterString(string name, out string value)
        {
            var result = Inner.GetParameterString(name, out value);
            return name == FailStringRead ? -1 : result;
        }
        public int SetParameterFloat(string name, float value)
        {
            var result = Inner.SetParameterFloat(name, value);
            Written?.Invoke();
            return result;
        }
        public int SetParameterString(string name, string value) => Inner.SetParameterString(name, value);
        public int SetParameters(string script) => Inner.SetParameters(script);
        public int GetLevel(int type, int channel, out float value)
        {
            if (!ChannelLevels) return Inner.GetLevel(type, channel, out value);
            value = type * 100 + channel;
            return type == 1 && channel == 15 ? -1 : 0;
        }
    }
}
