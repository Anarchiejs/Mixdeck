using System.Globalization;
using Mixdeck.Core.Model;
using Mixdeck.Voicemeeter.Native;

namespace Mixdeck.Voicemeeter;

public sealed record SimulatedApiCall(string Method, string? Parameter, int? Type, int? Channel, int ThreadId);

public class SimulatedVoicemeeterApi : IVoicemeeterApi
{
    private readonly object _gate = new();
    private readonly Dictionary<string, float> _floats = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<SimulatedApiCall> _calls = new();
    private readonly List<string> _scripts = [];
    private readonly Random _random;
    private bool _loggedIn;
    private bool _running = true;
    private bool _dirty = true;
    private MixerLayout _layout = MixerLayout.Banana;
    private int _mixerType = 2;
    private int _levelCount, _floatCount, _stringCount, _dirtyCount, _loginCount, _logoutCount;

    public SimulatedVoicemeeterApi(int? seed = null, MixerLayout? layout = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        _mixerType = (layout ?? MixerLayout.Banana).VoicemeeterType;
        InitializeLayout(layout ?? MixerLayout.Banana);
    }

    private void InitializeLayout(MixerLayout layout)
    {
        _layout = layout;
        _floats.Clear();
        _strings.Clear();
        string[] labels = ["Micro", "Discord", "Jeu", "Musique", "Navigateur"];
        for (var i = 0; i < layout.StripCount; i++)
        {
            _strings[VmParam.StripLabel(i)] = i < labels.Length ? labels[i] : layout.DefaultStripLabels[i];
            if (i < layout.PhysicalStripCount)
            {
                _strings[VmParam.StripDeviceName(i)] = i == 0 ? "Microphone (USB Audio)" : "";
                _floats[VmParam.StripGate(i)] = 0;
                _floats[VmParam.StripComp(i)] = 0;
                _floats[VmParam.StripColorX(i)] = 0;
                _floats[VmParam.StripColorY(i)] = 0;
            }
            _floats[VmParam.StripGain(i)] = i == 0 ? -3 : 0;
            _floats[VmParam.StripMute(i)] = 0;
            _floats[VmParam.StripSolo(i)] = 0;
            _floats[VmParam.StripPanX(i)] = 0;
            for (var bus = 0; bus < layout.BusCount; bus++)
                _floats[VmParam.StripRoute(i, bus, layout)] = bus == 0 || bus == layout.PhysicalBusCount ? 1 : 0;
            if (layout.IsVirtualStrip(i))
                for (var band = 0; band < 3; band++) _floats[VmParam.StripEqGain(i, band)] = 0;
        }
        for (var i = 0; i < layout.BusCount; i++)
        {
            _strings[VmParam.BusLabel(i)] = layout.BusNames[i];
            if (i < layout.PhysicalBusCount)
                _strings[VmParam.BusDeviceName(i)] = i == 0 ? "Casque (Realtek Audio)" : "";
            _floats[VmParam.BusGain(i)] = 0;
            _floats[VmParam.BusMute(i)] = 0;
            _floats[VmParam.BusMono(i)] = 0;
            if (layout.VoicemeeterType != 1) _floats[VmParam.BusEqOn(i)] = 0;
        }
    }

    public bool IsInstalled { get; set; } = true;
    public int MixerType
    {
        get { lock (_gate) return _mixerType; }
        set
        {
            lock (_gate)
            {
                _mixerType = value;
                if (value is >= 1 and <= 3) InitializeLayout(MixerLayout.ForType(value));
                _dirty = true;
            }
        }
    }
    public int Version { get; set; } = 0x02010808;
    public int TypeReadResult { get; set; }
    public int Run64Result { get; set; }
    public int Run32Result { get; set; }
    public int GetLevelCallCount => Volatile.Read(ref _levelCount);
    public int SetFloatCallCount => Volatile.Read(ref _floatCount);
    public int SetStringCallCount => Volatile.Read(ref _stringCount);
    public int DirtyCallCount => Volatile.Read(ref _dirtyCount);
    public int LoginCallCount => Volatile.Read(ref _loginCount);
    public int LogoutCallCount => Volatile.Read(ref _logoutCount);
    public IReadOnlyList<SimulatedApiCall> Calls { get { lock (_gate) return _calls.ToArray(); } }
    public IReadOnlyList<string> Scripts { get { lock (_gate) return _scripts.ToArray(); } }

    public void SimulateEngineStopped() { lock (_gate) _running = false; }
    public void SimulateEngineStarted() { lock (_gate) { _running = true; _dirty = true; } }
    public void SimulateExternalChange(string name, float value) { lock (_gate) { _floats[name] = value; _dirty = true; } }
    public void SimulateExternalChange(string name, string value) { lock (_gate) { _strings[name] = value; _dirty = true; } }

    private void Record(string method, string? parameter = null, int? type = null, int? channel = null)
    {
        // L'historique est borné pour que le mode démo puisse tourner indéfiniment.
        if (_calls.Count == 4096) _calls.Dequeue();
        _calls.Enqueue(new(method, parameter, type, channel, Environment.CurrentManagedThreadId));
    }
    private bool Available => _loggedIn && _running;

    public int Login()
    {
        lock (_gate)
        {
            Record(nameof(Login));
            _loginCount++;
            if (!IsInstalled) throw new DllNotFoundException("Installation simulée absente.");
            if (_loggedIn) return -2;
            _loggedIn = true;
            return _running ? 0 : 1;
        }
    }
    public int Logout() { lock (_gate) { Record(nameof(Logout)); _logoutCount++; _loggedIn = false; return 0; } }
    public int RunVoicemeeter(int type)
    {
        lock (_gate)
        {
            Record(nameof(RunVoicemeeter), type: type);
            if (!IsInstalled) throw new DllNotFoundException();
            var result = type is >= 4 and <= 6 ? Run64Result : Run32Result;
            if (result == 0) { _running = true; _dirty = true; }
            return result;
        }
    }
    public int GetVoicemeeterType(out int type)
    {
        lock (_gate) { Record(nameof(GetVoicemeeterType)); type = Available ? MixerType : 0; return Available ? TypeReadResult : -2; }
    }
    public int GetVoicemeeterVersion(out int version)
    {
        lock (_gate) { Record(nameof(GetVoicemeeterVersion)); version = Available ? Version : 0; return Available ? 0 : -2; }
    }
    public int IsParametersDirty()
    {
        lock (_gate)
        {
            Record(nameof(IsParametersDirty)); _dirtyCount++;
            if (!Available) return -2;
            var result = _dirty ? 1 : 0;
            _dirty = false;
            return result;
        }
    }
    public int GetParameterFloat(string name, out float value)
    {
        lock (_gate) { Record(nameof(GetParameterFloat), name); value = 0; return Available && _floats.TryGetValue(name, out value) ? 0 : -1; }
    }
    public int GetParameterString(string name, out string value)
    {
        lock (_gate) { Record(nameof(GetParameterString), name); value = _strings.GetValueOrDefault(name, ""); return Available && _strings.ContainsKey(name) ? 0 : -1; }
    }
    public int SetParameterFloat(string name, float value)
    {
        lock (_gate)
        {
            Record(nameof(SetParameterFloat), name); _floatCount++;
            if (!Available) return -2;
            _floats[name] = value; _dirty = true; return 0;
        }
    }
    public int SetParameterString(string name, string value)
    {
        lock (_gate)
        {
            Record(nameof(SetParameterString), name); _stringCount++;
            if (!Available) return -2;
            _strings[name] = value;
            var device = name.IndexOf(".device.", StringComparison.OrdinalIgnoreCase);
            if (device >= 0) _strings[name[..device] + ".device.name"] = value;
            _dirty = true; return 0;
        }
    }
    public int SetParameters(string script)
    {
        lock (_gate)
        {
            Record(nameof(SetParameters), script);
            if (!Available) return -2;
            _scripts.Add(script);
            foreach (var assignment in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = assignment.IndexOf('=');
                if (separator < 1) continue;
                var name = assignment[..separator].Trim();
                var value = assignment[(separator + 1)..].Trim();
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) _floats[name] = number;
                else _strings[name] = value.Trim('"', '\'');
            }
            _dirty = true;
            return 0;
        }
    }
    private static readonly AudioDevice[] InputDevices =
    [
        new(AudioDeviceDriver.Wdm, "Microphone (USB Audio)", "demo-usb-mic"),
        new(AudioDeviceDriver.Wdm, "Microphone (Realtek Audio)", "demo-realtek-mic"),
        new(AudioDeviceDriver.Ks, "Microphone (USB Audio)", "demo-usb-mic"),
        new(AudioDeviceDriver.Mme, "Microphone (USB Audio)", "demo-usb-mic"),
    ];
    private static readonly AudioDevice[] OutputDevices =
    [
        new(AudioDeviceDriver.Wdm, "Casque (Realtek Audio)", "demo-headphones"),
        new(AudioDeviceDriver.Wdm, "Enceintes (USB Audio)", "demo-speakers"),
        new(AudioDeviceDriver.Ks, "Casque (Realtek Audio)", "demo-headphones"),
        new(AudioDeviceDriver.Mme, "Casque (Realtek Audio)", "demo-headphones"),
        new(AudioDeviceDriver.Asio, "USB Audio ASIO", "demo-asio"),
    ];
    public int InputGetDeviceNumber() { lock (_gate) { Record(nameof(InputGetDeviceNumber)); return Available ? InputDevices.Length : -1; } }
    public int OutputGetDeviceNumber() { lock (_gate) { Record(nameof(OutputGetDeviceNumber)); return Available ? OutputDevices.Length : -1; } }
    public int InputGetDeviceDesc(int index, out int type, out string name, out string hardwareId) =>
        DeviceDesc(InputDevices, nameof(InputGetDeviceDesc), index, out type, out name, out hardwareId);
    public int OutputGetDeviceDesc(int index, out int type, out string name, out string hardwareId) =>
        DeviceDesc(OutputDevices, nameof(OutputGetDeviceDesc), index, out type, out name, out hardwareId);
    private int DeviceDesc(AudioDevice[] devices, string method, int index, out int type, out string name, out string hardwareId)
    {
        lock (_gate)
        {
            Record(method, channel: index);
            type = 0; name = ""; hardwareId = "";
            if (!Available || index < 0 || index >= devices.Length) return -1;
            var device = devices[index];
            type = (int)device.Driver; name = device.Name; hardwareId = device.HardwareId;
            return 0;
        }
    }
    public int GetLevel(int type, int channel, out float value)
    {
        lock (_gate)
        {
            Record(nameof(GetLevel), type: type, channel: channel);
            _levelCount++;
            value = 0;
            if (!Available) return -2;
            if (type is < 0 or > 3 || channel < 0 || channel >= (type == 3 ? _layout.OutputLevelChannels : _layout.InputLevelChannels)) return -1;
            var index = type == 3 ? channel / 8 : channel < _layout.PhysicalStripCount * 2
                ? channel / 2 : _layout.PhysicalStripCount + (channel - _layout.PhysicalStripCount * 2) / 8;
            var gain = _floats[type == 3 ? VmParam.BusGain(index) : VmParam.StripGain(index)];
            var mute = _floats[type == 3 ? VmParam.BusMute(index) : VmParam.StripMute(index)] != 0;
            var envelope = (float)(0.3 + 0.2 * Math.Sin(_levelCount / 240.0 + channel * 0.7) + _random.NextDouble() * 0.025);
            value = envelope * (type == 0 ? 1 : MathF.Pow(10, gain / 20));
            if (mute && type is 2 or 3) value = 0;
            return 0;
        }
    }
}
