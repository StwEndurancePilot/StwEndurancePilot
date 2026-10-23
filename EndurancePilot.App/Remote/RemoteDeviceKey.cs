using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EndurancePilot.App;

// Only the public key leaves Windows. The software KSP refuses normal private-key export.
// Code executing as this Windows user can still ask CNG to sign; this is not a malware boundary.
internal static class RemoteDeviceKey
{
    private static readonly CngProvider Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider;
    internal static string Create()
    {
        string name = "EndurancePilot.Discord." + Guid.NewGuid().ToString("N");
        using var key = CngKey.Create(CngAlgorithm.ECDsaP256, name, new CngKeyCreationParameters
        {
            Provider = Provider, ExportPolicy = CngExportPolicies.None, KeyUsage = CngKeyUsages.Signing
        });
        return name;
    }
    internal static CngKey Open(string name)
    {
        if (!Regex.IsMatch(name ?? "", "\\AEndurancePilot\\.Discord\\.[a-f0-9]{32}\\z")) throw new InvalidDataException("Invalid device key.");
        var key = CngKey.Open(name!, Provider, CngKeyOpenOptions.UserKey);
        if (key.Algorithm != CngAlgorithm.ECDsaP256 || key.ExportPolicy != CngExportPolicies.None)
        {
            key.Dispose();
            throw new CryptographicException("Invalid device key policy.");
        }
        return key;
    }
    internal static string PublicKey(string name)
    {
        using var key = Open(name);
        using var signer = new ECDsaCng(key);
        return Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
    }
    internal static string Sign(string name, string origin, string user, string route, string body, string timestamp, string nonce)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var data = Encoding.UTF8.GetBytes($"pilot-device-v2\nPOST\n{origin}/device/{user}/{route}\n{timestamp}\n{nonce}\n{hash}");
        using var key = Open(name);
        using var signer = new ECDsaCng(key);
        return Convert.ToHexStringLower(signer.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
    internal static void Delete(string name)
    {
        using var key = Open(name);
        key.Delete();
    }
}
