using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SpeakYourMind.Core;
using Verse;

namespace SpeakYourMind;

// Keys are per server and OS account, never scribed into saves or mod settings.
public static class Credentials
{
    private static string PathFor(Connection c)
    {
        string identity = c.Provider + ":" + ProviderClient.ValidateEndpoint(c.BaseUrl).AbsoluteUri;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        return Path.Combine(GenFilePaths.ConfigFolderPath, "SpeakYourMind", "key-" + hash + ".bin");
    }
    public static string Read(Connection c)
    {
        try
        {
            string path = PathFor(c);
            return File.Exists(path) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) : "";
        }
        catch { return ""; }
    }
    public static void Save(Connection c, string key)
    {
        string path = PathFor(c);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (string.IsNullOrWhiteSpace(key)) { if (File.Exists(path)) File.Delete(path); return; }
        byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path + ".tmp", encrypted);
        File.Move(path + ".tmp", path, true);
    }
}
