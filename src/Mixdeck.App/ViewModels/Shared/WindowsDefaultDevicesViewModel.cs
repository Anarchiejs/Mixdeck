using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Mixdeck.App.Services;
using Mixdeck.Core.Abstractions;
using Mixdeck.Platform.Audio;

namespace Mixdeck.App.ViewModels.Shared;

/// <summary>One selection shared by both views. Refreshes never write Windows defaults.</summary>
public sealed class WindowsDefaultDevicesViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IWindowsDefaultDevices service;
    private readonly IUiDispatcher ui;
    private readonly ITextProvider text;
    private AudioEndpoint? output, input;
    private int viewers;
    private bool loading, busy, refreshPending, healthy, disposed;
    private string errorMessage = "";
    private string writeError = "";

    public WindowsDefaultDevicesViewModel(IWindowsDefaultDevices service, IUiDispatcher ui, ITextProvider text)
    {
        this.service = service; this.ui = ui; this.text = text;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<AudioEndpoint> Outputs { get; } = [];
    public ObservableCollection<AudioEndpoint> Inputs { get; } = [];
    public bool CanEdit => !disposed && viewers > 0 && healthy && !busy;
    public bool HasError => ErrorMessage.Length > 0;
    public string ErrorMessage
    {
        get => errorMessage;
        private set { errorMessage = value; Notify(); Notify(nameof(HasError)); }
    }
    public AudioEndpoint? SelectedOutput
    {
        get => output;
        set => Select(ref output, value, WindowsAudioFlow.Output);
    }
    public AudioEndpoint? SelectedInput
    {
        get => input;
        set => Select(ref input, value, WindowsAudioFlow.Input);
    }

    public void Activate()
    {
        if (disposed) return;
        if (viewers++ == 0) { writeError = ""; service.Changed += OnChanged; RequestRefresh(); }
        Notify(nameof(CanEdit));
    }
    public void Deactivate()
    {
        if (viewers == 0) return;
        if (--viewers == 0) service.Changed -= OnChanged;
        Notify(nameof(CanEdit));
    }
    private void OnChanged(object? sender, EventArgs args) => ui.Post(RequestRefresh);
    private void RequestRefresh()
    {
        if (disposed || viewers == 0) return;
        if (busy) { refreshPending = true; return; }
        busy = true;
        Notify(nameof(CanEdit));
        _ = RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        WindowsDefaultDevicesSnapshot? snapshot = null;
        Exception? failure = null;
        try { snapshot = await service.ReadAsync().ConfigureAwait(false); }
        catch (Exception ex) { failure = ex; }
        ui.Post(() =>
        {
            if (disposed) return;
            if (failure is null)
            {
                Apply(snapshot!);
                ErrorMessage = writeError;
            }
            else
            {
                Apply(new([], [], null, null));
                healthy = false;
                ErrorMessage = text.Get("L6_ReadError");
            }
            Finish();
        });
    }
    private void Apply(WindowsDefaultDevicesSnapshot snapshot)
    {
        loading = true;
        try
        {
            // Suppress writes caused by ComboBox clearing its selection as ItemsSource changes.
            Outputs.Clear(); Inputs.Clear();
            foreach (var device in snapshot.Outputs) Outputs.Add(device);
            foreach (var device in snapshot.Inputs) Inputs.Add(device);
            output = Outputs.FirstOrDefault(d => d.Id == snapshot.DefaultOutputId);
            input = Inputs.FirstOrDefault(d => d.Id == snapshot.DefaultInputId);
            Notify(nameof(SelectedOutput)); Notify(nameof(SelectedInput));
            healthy = true;
        }
        finally { loading = false; }
    }
    private void Select(ref AudioEndpoint? field, AudioEndpoint? value, WindowsAudioFlow flow)
    {
        if (loading) return;
        if (Equals(field, value)) return;
        // Null is a UI transition or an absent default, never a request to reset Windows.
        if (value is null || !CanEdit) { Notify(flow == WindowsAudioFlow.Output ? nameof(SelectedOutput) : nameof(SelectedInput)); return; }
        var devices = flow == WindowsAudioFlow.Output ? Outputs : Inputs;
        if (!devices.Any(d => d.Id == value.Id)) return;
        field = value;
        Notify(flow == WindowsAudioFlow.Output ? nameof(SelectedOutput) : nameof(SelectedInput));
        busy = true;
        writeError = "";
        ErrorMessage = "";
        Notify(nameof(CanEdit));
        _ = SetAsync(flow, value.Id);
    }
    private async Task SetAsync(WindowsAudioFlow flow, string id)
    {
        Exception? failure = null;
        WindowsDefaultDevicesSnapshot? snapshot = null;
        Exception? readFailure = null;
        try { await service.SetDefaultAsync(flow, id).ConfigureAwait(false); }
        catch (Exception ex) { failure = ex; }
        // Even a failed write may have changed one or two roles. Always restore the actual Windows state.
        try { snapshot = await service.ReadAsync().ConfigureAwait(false); }
        catch (Exception ex) { readFailure = ex; }
        ui.Post(() =>
        {
            if (disposed) return;
            if (snapshot is not null) Apply(snapshot);
            else { Apply(new([], [], null, null)); healthy = false; }
            writeError = failure is not null
                ? string.Format(text.Culture, text.Get("L6_SetError"), $"0x{failure.HResult:X8}")
                : "";
            ErrorMessage = writeError.Length > 0 ? writeError : readFailure is not null ? text.Get("L6_ReadError") : "";
            // Keep failed-write feedback across late native notifications, without losing external changes.
            Finish();
        });
    }
    private void Finish()
    {
        busy = false;
        Notify(nameof(CanEdit));
        var refresh = refreshPending;
        refreshPending = false;
        if (refresh) RequestRefresh();
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        service.Changed -= OnChanged;
        viewers = 0;
        Notify(nameof(CanEdit));
        // The DI container owns the service.
    }
}
