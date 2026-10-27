// API privée de l'Explorateur. Source primaire : Chromium, status_tray_state_changer_win.{cc,h}
// https://github.com/chromium/chromium/blob/main/chrome/browser/ui/views/status_icons/status_tray_state_changer_win.cc
// CLSID TrayNotify = 25DEAD04-1EAC-4911-9E3A-AD0A4AB560FD.
// IID Win8+ = D133CE13-3537-48BA-93A7-AFCD5D2053B4 ; callback = D782CCBA-AFB0-43F1-94DB-FDA3779EACCB.
// IID ancien (XP/7) = FB852B2C-6BAD-4605-9551-F15F87830935 : sondé seulement, jamais appelé.
// ULONG reste uint (32 bits) sous Win64 ; UnregisterCallback prend ULONG*, donc ref uint, pas ulong.
// ABI connue sur Windows 8/10 ; efficacité sur le nouveau tray de Windows 11 23H2 NON encore validée.
// Il n'existe ici aucune supposition sur un IID « Windows 11 » supplémentaire : QI refusé => false.
// Aucun redémarrage, écriture du registre, injection, EnableAutoTray ou appel de slot non identifié.
using System.Runtime.InteropServices;
using Mixdeck.Platform.Audio.AudioPolicy;

namespace Mixdeck.Platform.Taskbar;

public static partial class TrayNotifyInterop
{
    internal static readonly Guid ClassId = new("25dead04-1eac-4911-9e3a-ad0a4ab560fd");
    internal static readonly Guid InterfaceId = new("d133ce13-3537-48ba-93a7-afcd5d2053b4");
    internal static readonly Guid LegacyInterfaceId = new("fb852b2c-6bad-4605-9551-f15f87830935");

    /// <summary>
    /// À appeler sur un thread de travail. true signifie que la préférence COM a été relue ;
    /// la visibilité effective du nouveau tray Windows 11 doit encore être confirmée visuellement par la sonde.
    /// </summary>
    public static bool TryPromoteNow(string executablePath, bool visible) => TryPromoteNow(executablePath, visible, null);

    public static bool TryPromoteNow(string executablePath, bool visible, Action<string>? trace)
    {
        if (!IsValidPath(executablePath)) return false;
        try
        {
            using var session = new TraySession(trace);
            return Change(session, executablePath, visible, trace);
        }
        catch (Exception ex) when (PrivateApiNative.IsExpected(ex))
        {
            trace?.Invoke($"Tray IsSupported=false ou opération refusée : HRESULT=0x{ex.HResult:X8}, {ex.Message}");
            return false;
        }
    }

    internal static bool IsValidPath(string? path) => !string.IsNullOrWhiteSpace(path)
        && !path.Any(char.IsControl) && Path.IsPathFullyQualified(path);

    internal interface ITraySession : IDisposable
    {
        IReadOnlyList<NotifyItem> Read(string path);
        void Set(NotifyItem item);
    }

    internal static bool SameIcon(NotifyItem left, NotifyItem right) => left.Window == right.Window
        && left.Id == right.Id && left.Guid == right.Guid
        && string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase);

    internal static bool Change(ITraySession session, string path, bool visible, Action<string>? trace)
    {
        var before = session.Read(path);
        if (before.Count == 0)
        {
            trace?.Invoke("Aucune icône active correspondante ; aucune écriture. IsSupported=false pour cette icône.");
            return false;
        }
        if (before.Any(item => item.Preference > 2))
        {
            trace?.Invoke("Préférence de tray inconnue ; aucune écriture. IsSupported=false.");
            return false;
        }
        uint wanted = visible ? 2u : 1u;
        try
        {
            foreach (var item in before)
            {
                if (item.Preference == wanted) continue;
                var updated = item;
                updated.Preference = wanted;
                session.Set(updated);
            }
            var after = session.Read(path);
            if (before.All(item => after.Any(read => SameIcon(item, read) && read.Preference == wanted))) return true;
            trace?.Invoke("Préférence non confirmée par relecture ; restauration.");
        }
        catch (Exception ex) when (PrivateApiNative.IsExpected(ex))
        {
            trace?.Invoke($"SetPreference/relecture refusé : HRESULT=0x{ex.HResult:X8}, {ex.Message}");
        }
        Restore(session, path, before, trace);
        return false;
    }

    internal static bool Restore(ITraySession session, string path, IReadOnlyList<NotifyItem> before, Action<string>? trace)
    {
        bool success = true;
        foreach (var item in before)
        {
            try { session.Set(item); }
            catch (Exception ex) when (PrivateApiNative.IsExpected(ex))
            {
                trace?.Invoke($"RESTAURATION REFUSÉE id={item.Id} : HRESULT=0x{ex.HResult:X8}, {ex.Message}");
                success = false;
            }
        }
        try
        {
            var restored = session.Read(path);
            success &= before.All(item => restored.Any(read => SameIcon(item, read) && read.Preference == item.Preference));
        }
        catch (Exception ex) when (PrivateApiNative.IsExpected(ex))
        {
            trace?.Invoke($"Relecture de restauration refusée : HRESULT=0x{ex.HResult:X8}, {ex.Message}");
            success = false;
        }
        trace?.Invoke($"Restauration tray confirmée={success}");
        return success;
    }

    // Types publics requis pour le CCW du callback COM, sans génération de bibliothèque de types.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NotifyItem
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? ExecutablePath;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ToolTip;
        public nint Icon;
        public nint Window;
        public uint Preference;
        public uint Id;
        public Guid Guid;
    }

    [ComVisible(true), Guid("d782ccba-afb0-43f1-94db-fda3779eaccb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INotificationCallback
    {
        [PreserveSig] int Notify(uint message, in NotifyItem item);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class NotificationCallback : INotificationCallback
    {
        private readonly string _path;
        private readonly List<NotifyItem> _items = [];
        private readonly object _gate = new();
        internal NotificationCallback(string path) => _path = path;
        internal IReadOnlyList<NotifyItem> Snapshot() { lock (_gate) return _items.ToArray(); }

        public int Notify(uint message, in NotifyItem item)
        {
            try
            {
                if (item.ExecutablePath is null || item.Window == 0 || !IsWindow(item.Window)
                    || !TrayIconPromoter.IsSameExecutable(item.ExecutablePath, _path)) return 0;
                var copy = item; // Les chaînes sont copiées par le marshaller ; hIcon/hWnd restent empruntés.
                lock (_gate)
                {
                    _items.RemoveAll(previous => SameIcon(previous, copy));
                    _items.Add(copy);
                }
                return 0;
            }
            catch (Exception)
            {
                // Ne jamais propager une exception gérée à l'Explorateur via le callback.
                return unchecked((int)0x80004005);
            }
        }
    }

    [ComImport, Guid("d133ce13-3537-48ba-93a7-afcd5d2053b4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITrayNotify
    {
        [PreserveSig] int RegisterCallback([MarshalAs(UnmanagedType.Interface)] INotificationCallback callback, out uint cookie);
        [PreserveSig] int UnregisterCallback(ref uint cookie);
        [PreserveSig] int SetPreference(in NotifyItem item);
        [PreserveSig] int EnableAutoTray([MarshalAs(UnmanagedType.Bool)] bool enabled);
        [PreserveSig] int DoAction([MarshalAs(UnmanagedType.Bool)] bool enabled);
    }

    internal sealed class TraySession : ITraySession
    {
        private readonly PrivateApiApartment _apartment;
        private readonly Action<string>? _trace;
        private ITrayNotify? _tray;

        internal TraySession(Action<string>? trace)
        {
            _trace = trace;
            _apartment = new PrivateApiApartment();
            nint unknown = 0, known = 0;
            try
            {
                trace?.Invoke($"Tray Windows={Environment.OSVersion.Version}, CLSID={ClassId:B}");
                // Serveur local seulement : aucune activation d'une DLL privée dans notre processus.
                PrivateApiNative.Check(PrivateApiNative.CoCreateInstance(ClassId, 0, 4, PrivateApiNative.UnknownId, out unknown), "CoCreateInstance TrayNotify IUnknown", trace);
                var iid = InterfaceId;
                int hr = Marshal.QueryInterface(unknown, in iid, out known);
                trace?.Invoke($"QueryInterface {iid:B}: HRESULT=0x{hr:X8}");
                if (hr < 0)
                {
                    var legacy = LegacyInterfaceId;
                    int legacyHr = Marshal.QueryInterface(unknown, in legacy, out nint legacyPointer);
                    trace?.Invoke($"QueryInterface ancien {legacy:B}: HRESULT=0x{legacyHr:X8} (aucune méthode appelée)");
                    if (legacyPointer != 0) Marshal.Release(legacyPointer);
                    throw new COMException("IID moderne absent ; aucune méthode privée appelée.", hr);
                }
                _tray = (ITrayNotify)Marshal.GetObjectForIUnknown(known);
                trace?.Invoke($"IID retenu={iid:B}, IsSupported=true (ABI ; visibilité à vérifier)");
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                if (known != 0) Marshal.Release(known);
                if (unknown != 0) Marshal.Release(unknown);
            }
        }

        public IReadOnlyList<NotifyItem> Read(string path)
        {
            var callback = new NotificationCallback(path);
            uint cookie = 0;
            bool registered = false;
            try
            {
                int hr = _tray!.RegisterCallback(callback, out cookie);
                registered = hr >= 0;
                PrivateApiNative.Check(hr, "ITrayNotify.RegisterCallback", _trace);
            }
            finally
            {
                // Comme Chromium : les callbacks d'énumération sont synchrones. Toujours retirer l'abonnement.
                if (registered || cookie != 0)
                {
                    int hr = _tray!.UnregisterCallback(ref cookie);
                    GC.KeepAlive(callback);
                    PrivateApiNative.Check(hr, "ITrayNotify.UnregisterCallback", _trace);
                }
            }
            var items = callback.Snapshot();
            foreach (var item in items)
                _trace?.Invoke($"Tray relu exe={item.ExecutablePath}, hwnd=0x{item.Window:X}, id={item.Id}, guid={item.Guid:B}, préférence={item.Preference}");
            _trace?.Invoke($"Icônes actives correspondantes={items.Count}");
            return items;
        }

        public void Set(NotifyItem item) => PrivateApiNative.Check(_tray!.SetPreference(item),
            $"ITrayNotify.SetPreference id={item.Id} préférence={item.Preference}", _trace);

        public void Dispose()
        {
            if (_tray is not null) Marshal.ReleaseComObject(_tray);
            _tray = null;
            _apartment.Dispose();
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);
}
