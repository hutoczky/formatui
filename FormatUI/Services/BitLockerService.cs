using System;
using System.Management;
using System.Threading.Tasks;

namespace FormatUI.Services
{
    /// <summary>
    /// Egyszerű BitLocker WMI wrapper (Unlock/Status). Nem használ explicit Dispose-t,
    /// hogy elkerüljük a build környezeti inkompatibilitásokat.
    /// </summary>
    public static class BitLockerService
    {
        private const string ScopePath = @"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption";

        private static ManagementObject? GetVolume(string driveLetterColon)
        {
            var letter = (driveLetterColon ?? string.Empty).Trim().TrimEnd(':').ToUpperInvariant();
            var scope = new ManagementScope(ScopePath);
            scope.Connect();

            var query = new ObjectQuery("SELECT * FROM Win32_EncryptableVolume");
            var searcher = new ManagementObjectSearcher(scope, query);

            foreach (ManagementObject mo in searcher.Get())
            {
                var dl = (mo["DriveLetter"] as string)?.Trim();
                if (!string.IsNullOrEmpty(dl))
                {
                    dl = dl.TrimEnd(':').ToUpperInvariant();
                    if (dl == letter) return mo;
                }
            }

            return null;
        }

        public static bool TryGetStatus(string driveLetterColon, out int protectionStatus, out int lockStatus)
        {
            protectionStatus = -1;
            lockStatus = -1;

            var mo = GetVolume(driveLetterColon);
            if (mo == null) return false;

            try
            {
                protectionStatus = Convert.ToInt32(mo["ProtectionStatus"] ?? 0);
                var outParams = mo.InvokeMethod("GetLockStatus", null, null);
                lockStatus = Convert.ToInt32(outParams?["LockStatus"] ?? 2); // 0=Unlocked, 1=Locked, 2=Unknown
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsLocked(string driveLetterColon)
            => TryGetStatus(driveLetterColon, out _, out var lockStatus) && lockStatus == 1;

        public static bool IsProtected(string driveLetterColon)
            => TryGetStatus(driveLetterColon, out var prot, out _) && prot == 1;

        public static async Task<bool> UnlockWithPasswordAsync(string driveLetterColon, string password)
        {
            return await Task.Run(() =>
            {
                var mo = GetVolume(driveLetterColon);
                if (mo == null) return false;

                try
                {
                    var inParams = mo.GetMethodParameters("UnlockWithPassword");
                    inParams["Password"] = password;
                    var outParams = mo.InvokeMethod("UnlockWithPassword", inParams, null);
                    var hr = Convert.ToUInt32(outParams?["ReturnValue"] ?? 1);
                    return hr == 0; // S_OK
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
