namespace Cereal.Core.Services;

/// <summary>
/// Encrypts and decrypts sensitive credentials using platform-native secrets storage
/// (DPAPI on Windows).  Tokens are the ONLY secrets — everything else lives in the DB.
/// </summary>
public interface ICredentialService
{
    void Store(string key, string value);
    string? Retrieve(string key);
    void Delete(string key);
}
