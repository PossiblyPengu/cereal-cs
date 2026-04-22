using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Cereal.App.Services;

/// <summary>
/// Secure credential store. Uses DPAPI on Windows, file-based AES on Linux.
/// Mirrors the Electron safeStorage approach: encrypt-then-base64 in a JSON sidecar.
/// </summary>
public class CredentialService
{
    private readonly string _storePath;
    private readonly string _backupPath;
    private Dictionary<string, string> _cache = [];

    public CredentialService(PathService paths)
    {
        _storePath = Path.Combine(paths.AppDataDir, "credentials.json");
        _backupPath = _storePath + ".bak";
        Load();
    }

    public void SetPassword(string service, string account, string secret)
    {
        var key = $"{service}/{account}";
        _cache[key] = Encrypt(secret);
        Persist();
    }

    public string? GetPassword(string service, string account)
    {
        var key = $"{service}/{account}";
        if (!_cache.TryGetValue(key, out var cipher)) return null;
        try { return Decrypt(cipher); }
        catch (Exception ex)
        {
            Log.Warning(ex, "[creds] Failed to decrypt {Key}", key);
            return null;
        }
    }

    public bool DeletePassword(string service, string account)
    {
        var key = $"{service}/{account}";
        if (!_cache.Remove(key)) return false;
        Persist();
        return true;
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    private void Load()
    {
        foreach (var path in new[] { _storePath, _backupPath })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (dict is not null) { _cache = dict; return; }
            }
            catch { /* try backup */ }
        }
        _cache = [];
    }

    private void Persist()
    {
        try
        {
            if (File.Exists(_storePath))
                File.Copy(_storePath, _backupPath, overwrite: true);
        }
        catch { /* best-effort */ }

        var tmp = _storePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _storePath, overwrite: true);
    }

    // ── Encryption ───────────────────────────────────────────────────────────

    private static string Encrypt(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] cipher;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cipher = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        else
            cipher = EncryptLinux(bytes);

        return Convert.ToBase64String(cipher);
    }

    private static string Decrypt(string base64)
    {
        var cipher = Convert.FromBase64String(base64);
        byte[] plain;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
        else
            plain = DecryptLinux(cipher);

        return Encoding.UTF8.GetString(plain);
    }

    // On Linux use a machine-scoped AES key derived from machine-id
    private static readonly Lazy<byte[]> LinuxKey = new(() =>
    {
        var machineId = "/etc/machine-id";
        var seed = File.Exists(machineId) ? File.ReadAllText(machineId).Trim() : Environment.MachineName;
        return SHA256.HashData(Encoding.UTF8.GetBytes("cereal-cred-v1:" + seed));
    });

    private static byte[] EncryptLinux(byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = LinuxKey.Value;
        aes.GenerateIV();
        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            cs.Write(plaintext);
        return ms.ToArray();
    }

    private static byte[] DecryptLinux(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = LinuxKey.Value;
        var iv = data[..16];
        aes.IV = iv;
        using var ms = new MemoryStream(data, 16, data.Length - 16);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var result = new MemoryStream();
        cs.CopyTo(result);
        return result.ToArray();
    }
}
