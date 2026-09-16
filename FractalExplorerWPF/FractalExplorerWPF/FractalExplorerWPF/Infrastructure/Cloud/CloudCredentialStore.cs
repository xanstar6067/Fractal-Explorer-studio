using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure.Cloud;

internal interface ICloudCredentialStore
{
    CloudCredential? Read();
    void Write(CloudCredential credential);
    void Delete();
}

/// <summary>Only DPAPI ciphertext is stored on disk, bound to the current Windows user.</summary>
internal sealed class CloudCredentialStore(string server) : ICloudCredentialStore
{
    private readonly byte[] _entropy = Encoding.UTF8.GetBytes("FractalCloud:" + server);
    private readonly string _path = AppPaths.GetSettingsFile("cloud-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server)))[..24] + ".dpapi");

    public CloudCredential? Read()
    {
        if (!File.Exists(_path)) return null;
        byte[]? clear = null;
        try
        {
            clear = ProtectedData.Unprotect(File.ReadAllBytes(_path), _entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<CloudCredential>(clear);
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            Delete();
            return null;
        }
        finally { if (clear is not null) CryptographicOperations.ZeroMemory(clear); }
    }

    public void Write(CloudCredential credential)
    {
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(credential);
        try
        {
            byte[] encrypted = ProtectedData.Protect(clear, _entropy, DataProtectionScope.CurrentUser);
            string temporary = AppPaths.EnsureDirectoryFor(_path) + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, encrypted);
                File.Move(temporary, _path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public void Delete() { if (File.Exists(_path)) File.Delete(_path); }
}
