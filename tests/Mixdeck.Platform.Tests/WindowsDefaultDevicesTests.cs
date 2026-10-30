using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Mixdeck.App.Services;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.Core.Abstractions;
using Mixdeck.Platform.Audio;
using Mixdeck.Platform.Audio.AudioPolicy;

namespace Mixdeck.Platform.Tests;

public sealed class WindowsDefaultDevicesTests
{
    [Theory]
    [InlineData(1u, true)]
    [InlineData(0u, false)]
    [InlineData(2u, false)]
    [InlineData(4u, false)]
    [InlineData(8u, false)]
    [InlineData(15u, false)]
    public void OnlyActiveEndpointsAreEligible(uint state, bool expected) => RenderEndpoints.IsActive(state).Should().Be(expected);

    [Fact]
    public void AppliesEveryWindowsRoleEvenWhenOneFails()
    {
        var calls = new List<(string Id, int Role)>();
        Action apply = () => DefaultDeviceRoles.Apply("chosen-id", (id, role) =>
        {
            calls.Add((id, role));
            return role == 1 ? unchecked((int)0x80070005) : 0;
        });
        apply.Should().Throw<COMException>().Which.HResult.Should().Be(unchecked((int)0x80070005));
        calls.Should().Equal(("chosen-id", 0), ("chosen-id", 1), ("chosen-id", 2));
    }

    [Fact]
    public void NativePolicyHasTheExpectedGuidAndMethodSlots()
    {
        typeof(IDefaultDevicePolicy).GUID.Should().Be(new Guid("f8679f50-850a-41cf-9c72-430f290290c8"));
        typeof(DefaultDevicePolicyClient).GUID.Should().Be(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"));
        var methods = typeof(IDefaultDevicePolicy).GetMethods().OrderBy(m => m.MetadataToken).Select(m => m.Name);
        methods.Should().Equal("GetMixFormat", "GetDeviceFormat", "ResetDeviceFormat", "SetDeviceFormat",
            "GetProcessingPeriod", "SetProcessingPeriod", "GetShareMode", "SetShareMode",
            "GetPropertyValue", "SetPropertyValue", "SetDefaultEndpoint", "SetEndpointVisibility");
        ((int)WindowsAudioFlow.Output).Should().Be(0);
        ((int)WindowsAudioFlow.Input).Should().Be(1);
    }

    [Fact]
    public async Task DemoChangesOnlyItsInMemorySnapshot()
    {
        using var demo = new SimulatedWindowsDefaultDevices();
        var before = await demo.ReadAsync();
        var notifications = 0;
        demo.Changed += (_, _) => notifications++;
        await demo.SetDefaultAsync(WindowsAudioFlow.Output, before.Outputs[1].Id);
        await demo.SetDefaultAsync(WindowsAudioFlow.Input, before.Inputs[1].Id);
        var after = await demo.ReadAsync();
        after.DefaultOutputId.Should().Be(before.Outputs[1].Id);
        after.DefaultInputId.Should().Be(before.Inputs[1].Id);
        notifications.Should().Be(2);
        var invalid = () => demo.SetDefaultAsync(WindowsAudioFlow.Output, "missing");
        await invalid.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExternalChangesAreDispatchedAndNeverWriteDefaults()
    {
        using var fake = new FakeDefaults();
        var ui = new QueuedUi();
        using var vm = new WindowsDefaultDevicesViewModel(fake, ui, new Text());
        vm.Activate();
        await ui.Until(() => vm.CanEdit);
        vm.SelectedOutput!.Id.Should().Be("speakers");
        vm.SelectedInput!.Id.Should().Be("mic");
        fake.Snapshot = fake.Snapshot with { Outputs = [new("headset", "Headset")], DefaultOutputId = "headset", Inputs = [], DefaultInputId = null };
        await Task.Run(fake.Notify);
        // The native notification thread must not change bound state directly.
        vm.SelectedOutput.Id.Should().Be("speakers");
        await ui.Until(() => vm.SelectedOutput?.Id == "headset" && vm.CanEdit);
        vm.Inputs.Should().BeEmpty();
        vm.SelectedInput.Should().BeNull();
        fake.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task UserSelectionsUseTheCorrectFlowAndIgnoreUiNullTransitions()
    {
        using var fake = new FakeDefaults();
        var ui = new QueuedUi();
        using var vm = new WindowsDefaultDevicesViewModel(fake, ui, new Text());
        vm.Activate();
        await ui.Until(() => vm.CanEdit);
        vm.SelectedOutput = null;
        vm.SelectedOutput!.Id.Should().Be("speakers");
        vm.SelectedOutput = vm.Outputs[1];
        await ui.Until(() => vm.CanEdit);
        vm.SelectedInput = vm.Inputs[1];
        await ui.Until(() => vm.CanEdit);
        fake.Writes.Should().Equal((WindowsAudioFlow.Output, "headset"), (WindowsAudioFlow.Input, "usb"));
        vm.SelectedOutput!.Id.Should().Be("headset");
        vm.SelectedInput!.Id.Should().Be("usb");
    }

    [Fact]
    public async Task FailedWriteShowsReadableErrorAndRestoresActualSelection()
    {
        using var fake = new FakeDefaults { WriteError = new COMException("denied", unchecked((int)0x80070005)) };
        var ui = new QueuedUi();
        using var vm = new WindowsDefaultDevicesViewModel(fake, ui, new Text());
        vm.Activate();
        await ui.Until(() => vm.CanEdit);
        vm.SelectedOutput = vm.Outputs[1];
        await ui.Until(() => vm.CanEdit);
        vm.SelectedOutput!.Id.Should().Be("speakers");
        vm.HasError.Should().BeTrue();
        vm.ErrorMessage.Should().Contain("0x80070005");
    }

    [Fact]
    public async Task ReadFailureClearsStaleDevicesAndCanRecoverOnNotification()
    {
        using var fake = new FakeDefaults();
        var ui = new QueuedUi();
        using var vm = new WindowsDefaultDevicesViewModel(fake, ui, new Text());
        vm.Activate();
        await ui.Until(() => vm.CanEdit);
        fake.ReadError = new COMException();
        fake.Notify();
        await ui.Until(() => vm.HasError);
        vm.CanEdit.Should().BeFalse();
        vm.Outputs.Should().BeEmpty();
        vm.SelectedOutput.Should().BeNull();
        fake.ReadError = null;
        fake.Notify();
        await ui.Until(() => vm.CanEdit);
        vm.HasError.Should().BeFalse();
        fake.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task SharedViewsKeepOneSubscriptionUntilBothClose()
    {
        using var fake = new FakeDefaults();
        var ui = new QueuedUi();
        using var vm = new WindowsDefaultDevicesViewModel(fake, ui, new Text());
        vm.Activate(); vm.Activate();
        await ui.Until(() => vm.CanEdit);
        fake.Subscribers.Should().Be(1);
        vm.Deactivate();
        vm.CanEdit.Should().BeTrue();
        fake.Subscribers.Should().Be(1);
        vm.Deactivate();
        fake.Subscribers.Should().Be(0);
        vm.CanEdit.Should().BeFalse();
        vm.Activate();
        await ui.Until(() => vm.CanEdit);
        vm.Dispose();
        fake.Subscribers.Should().Be(0);
    }

    [Fact]
    public async Task NotificationsDuringAReadAreCoalescedAndReadAgain()
    {
        using var fake = new FakeDefaults();
        var pending = new TaskCompletionSource<WindowsDefaultDevicesSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.PendingRead = pending.Task;
        var ui = new QueuedUi();
        using var vm = new WindowsDefaultDevicesViewModel(fake, ui, new Text());
        vm.Activate();
        fake.Notify(); fake.Notify();
        ui.Drain();
        fake.ReadCount.Should().Be(1);
        var old = fake.Snapshot;
        fake.Snapshot = old with { DefaultOutputId = "headset" };
        fake.PendingRead = null;
        pending.SetResult(old);
        await ui.Until(() => vm.CanEdit && vm.SelectedOutput?.Id == "headset");
        fake.ReadCount.Should().Be(2);
        fake.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposingDuringAReadDoesNotPublishOrResubscribe()
    {
        using var fake = new FakeDefaults();
        var pending = new TaskCompletionSource<WindowsDefaultDevicesSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.PendingRead = pending.Task;
        var ui = new QueuedUi();
        var vm = new WindowsDefaultDevicesViewModel(fake, ui, new Text());
        vm.Activate();
        vm.Dispose();
        pending.SetResult(fake.Snapshot);
        await ui.Until(() => ui.Pending > 0);
        ui.Drain();
        vm.Activate();
        vm.Outputs.Should().BeEmpty();
        vm.CanEdit.Should().BeFalse();
        fake.Subscribers.Should().Be(0);
        fake.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedWriteKeepsItsErrorWithoutDroppingConcurrentOrLateNotifications()
    {
        using var fake = new FakeDefaults();
        var ui = new QueuedUi();
        using var vm = new WindowsDefaultDevicesViewModel(fake, ui, new Text());
        vm.Activate();
        await ui.Until(() => vm.CanEdit);
        var pending = new TaskCompletionSource<WindowsDefaultDevicesSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.PendingRead = pending.Task;
        fake.WriteError = new COMException("partial write", unchecked((int)0x80070005));
        vm.SelectedOutput = vm.Outputs[1];
        var old = fake.Snapshot;
        fake.Notify();
        ui.Drain();
        fake.Snapshot = old with { DefaultOutputId = "headset", Inputs = [], DefaultInputId = null };
        fake.PendingRead = null;
        pending.SetResult(old);
        await ui.Until(() => vm.CanEdit && vm.SelectedOutput?.Id == "headset");
        vm.Inputs.Should().BeEmpty();
        vm.ErrorMessage.Should().Contain("0x80070005");
        fake.Notify();
        ui.Drain();
        await ui.Until(() => vm.CanEdit);
        vm.ErrorMessage.Should().Contain("0x80070005");
    }

    private sealed class FakeDefaults : IWindowsDefaultDevices
    {
        private EventHandler? changed;
        public int Subscribers { get; private set; }
        public event EventHandler? Changed { add { Subscribers++; changed += value; } remove { if (changed?.GetInvocationList().Contains(value) == true) Subscribers--; changed -= value; } }
        public Exception? ReadError { get; set; }
        public Exception? WriteError { get; set; }
        public Task<WindowsDefaultDevicesSnapshot>? PendingRead { get; set; }
        public int ReadCount { get; private set; }
        public WindowsDefaultDevicesSnapshot Snapshot { get; set; } = new(
            [new("speakers", "Speakers"), new("headset", "Headset")],
            [new("mic", "Mic"), new("usb", "USB")], "speakers", "mic");
        public List<(WindowsAudioFlow Flow, string Id)> Writes { get; } = [];
        public Task<WindowsDefaultDevicesSnapshot> ReadAsync()
        {
            ReadCount++;
            return PendingRead ?? (ReadError is null ? Task.FromResult(Snapshot) : Task.FromException<WindowsDefaultDevicesSnapshot>(ReadError));
        }
        public Task SetDefaultAsync(WindowsAudioFlow flow, string id)
        {
            Writes.Add((flow, id));
            if (WriteError is not null) return Task.FromException(WriteError);
            Snapshot = flow == WindowsAudioFlow.Output ? Snapshot with { DefaultOutputId = id } : Snapshot with { DefaultInputId = id };
            Notify();
            return Task.CompletedTask;
        }
        public void Notify() => changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() { }
    }
    private sealed class Text : ITextProvider
    {
        public System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.InvariantCulture;
        public string Get(string key) => key == "L6_SetError" ? "Could not change all Windows roles ({0})." : "Could not read Windows audio devices.";
    }
    private sealed class QueuedUi : IUiDispatcher
    {
        private readonly ConcurrentQueue<Action> actions = new();
        public int Pending => actions.Count;
        public bool HasThreadAccess => false;
        public void Post(Action action) => actions.Enqueue(action);
        public void Drain() { while (actions.TryDequeue(out var action)) action(); }
        public async Task Until(Func<bool> condition)
        {
            for (var i = 0; i < 500; i++)
            {
                if (condition()) return;
                Drain();
                if (condition()) return;
                await Task.Delay(10);
            }
            condition().Should().BeTrue("the queued UI update should complete");
        }
    }
}
