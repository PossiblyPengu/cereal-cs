using System.Security.Cryptography;
using System.Text;

namespace Cereal.Infrastructure.Services;

/// <summary>
/// DPAPI-backed credential store (Windows only).
/// On non-Windows platforms, falls back to a memory-only store and logs a warning.
/// </summary>
public sealed class CredentialService : ICredentialService
{
    // Key prefix to namespace Cereal's entries in DPAPI.
    private const string Prefix = "cereal_";

    private readonly Dictionary<string, string> _memoryFallback = [];

    public CredentialService()
    {
        if (!OperatingSystem.IsWindows())
            Log.Warning("[credentials] DPAPI not available on this platform — using in-memory fallback");
    }

    public void Store(string key, string value)
    {
        if (OperatingSystem.IsWindows())
        {
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value),
                Encoding.UTF8.GetBytes(Prefix + key),
                DataProtectionScope.CurrentUser);
            StoreInRegistry(Prefix + key, Convert.ToBase64String(encrypted));
        }
        else
        {
            _memoryFallback[key] = value;
        }
    }

    public string? Retrieve(string key)
    {
        if (OperatingSystem.IsWindows())
        {
            var b64 = RetrieveFromRegistry(Prefix + key);
            if (b64 is null) return null;
            try
            {
                var encrypted = Convert.FromBase64String(b64);
                var decrypted = ProtectedData.Unprotect(
                    encrypted,
                    Encoding.UTF8.GetBytes(Prefix + key),
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[credentials] Failed to decrypt key {Key} — returning null", key);
                return null;
            }
        }
        _memoryFallback.TryGetValue(key, out var v);
        return v;
    }

    public void Delete(string key)
    {
        if (OperatingSystem.IsWindows())
            DeleteFromRegistry(Prefix + key);
        else
            _memoryFallback.Remove(key);
    }

    // ── Registry helpers (Windows only) ──────────────────────────────────────

    private static readonly string RegPath = @"Software\CerealLauncher\Credentials";

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void StoreInRegistry(string key, string value)
    {
        using var reg = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegPath, writable: true);
        reg.SetValue(key, value);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? RetrieveFromRegistry(string key)
    {
        using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegPath);
        return reg?.GetValue(key) as string;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void DeleteFromRegistry(string key)
    {
        using var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegPath, writable: true);
        reg?.DeleteValue(key, throwOnMissingValue: false);
    }
}
