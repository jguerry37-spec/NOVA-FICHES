using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace LicenseGen;

// IMPORTANT : jumeau strict de src/NovaFiches/Licensing/MachineId.cs (voir la note
// dans LicensePayloadFormat.cs sur pourquoi ce code est dupliqué plutôt que partagé).
internal static class MachineId
{
    public static string GetCurrentMachineIdHash()
    {
        string raw;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            raw = key?.GetValue("MachineGuid") as string
                  ?? FallbackId();
        }
        catch
        {
            raw = FallbackId();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FallbackId()
    {
        return Environment.MachineName + "|" + Environment.ProcessorCount;
    }
}
