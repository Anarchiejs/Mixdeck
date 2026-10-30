using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mixdeck.Core.Settings;

namespace Mixdeck.Platform.Discord;

// Deliberately not a record: generated ToString must never expose credentials.
public sealed class DiscordCredentials
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Current-user Windows DPAPI. No plaintext sidecar or backup is written.</summary>
public sealed class DiscordCredentialStore(MixdeckPaths paths)
{
    private string FilePath => Path.Combine(paths.Root, "discord.bin");
    public DiscordCredentials? Load()
    {
        if (!File.Exists(FilePath)) return null;
        var plain = Transform(File.ReadAllBytes(FilePath), false);
        try { return JsonSerializer.Deserialize<DiscordCredentials>(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void Save(DiscordCredentials credentials)
    {
        Directory.CreateDirectory(paths.Root);
        var plain = JsonSerializer.SerializeToUtf8Bytes(credentials);
        byte[] encrypted;
        try { encrypted = Transform(plain, true); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Forget() { if (File.Exists(FilePath)) File.Delete(FilePath); }

    // Calling the OS DPAPI directly avoids a separate ProtectedData package.
    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            bool ok = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException($"Discord credential protection failed (Windows {Marshal.GetLastPInvokeError()}).");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero) { Marshal.Copy(new byte[input.Length], 0, input.Data, input.Length); Marshal.FreeHGlobal(input.Data); }
            if (output.Data != IntPtr.Zero) { Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); _ = LocalFree(output.Data); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
