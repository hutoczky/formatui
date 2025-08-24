using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace FormatUI.Services
{
    /// <summary>
    /// Provides helper methods for enumerating volumes, finding free drive
    /// letters and assigning/removing drive letters via the mountvol tool.
    /// </summary>
    public static class VolumeManager
    {
        /// <summary>
        /// Represents a raw volume entry returned from WMI.
        /// </summary>
        public class VolumeEntry
        {
            public string DeviceID { get; set; } = string.Empty;
            public string? DriveLetter { get; set; }
            public string Label { get; set; } = string.Empty;
            public string FileSystem { get; set; } = string.Empty;
            public ulong Capacity { get; set; }
            public ulong Free { get; set; }
            public int DriveType { get; set; }
        }

        /// <summary>
        /// Query volumes via WMI.  Optionally include volumes with no drive
        /// letter assigned.
        /// </summary>
        public static List<VolumeEntry> QueryVolumes(bool includeNoLetter)
        {
            var list = new List<VolumeEntry>();
            // Query all volumes (both fixed and removable).  Avoid using a WHERE clause
            // on DriveType because some systems treat this as an invalid query and
            // return an error.  We'll filter by DriveType in code if necessary.
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, DriveLetter, Label, FileSystem, Capacity, FreeSpace, DriveType FROM Win32_Volume");
            foreach (ManagementObject mo in searcher.Get())
            {
                try
                {
                    var letter = mo["DriveLetter"]?.ToString();
                    // Filter out non-removable/fixed drives (e.g. network, CD-ROM) by checking
                    // the DriveType property.  Only include removable (2) and fixed (3) drives.
                    int driveType = mo["DriveType"] is uint dt ? unchecked((int)dt) : -1;
                    if (driveType != 2 && driveType != 3)
                    {
                        continue;
                    }
                    if (!includeNoLetter && string.IsNullOrWhiteSpace(letter))
                    {
                        continue;
                    }

                    list.Add(new VolumeEntry
                    {
                        DeviceID = mo["DeviceID"]?.ToString() ?? string.Empty,
                        DriveLetter = string.IsNullOrWhiteSpace(letter) ? null : letter!.Trim(),
                        Label = mo["Label"]?.ToString() ?? string.Empty,
                        FileSystem = mo["FileSystem"]?.ToString() ?? string.Empty,
                        Capacity = mo["Capacity"] is ulong cap ? cap : 0UL,
                        Free = mo["FreeSpace"] is ulong free ? free : 0UL,
                        DriveType = driveType
                    });
                }
                catch
                {
                    // Skip any volumes that fail to parse.
                }
            }
            return list.OrderBy(v => v.DriveLetter ?? v.DeviceID).ToList();
        }

        /// <summary>
        /// Return a list of free drive letters (from D through Z) that are not currently in use.
        /// </summary>
        public static List<char> GetFreeLetters()
        {
            var used = new HashSet<char>(QueryVolumes(true)
                .Where(v => !string.IsNullOrWhiteSpace(v.DriveLetter))
                .Select(v => char.ToUpperInvariant(v.DriveLetter![0])));
            var letters = new List<char>();
            for (char c = 'D'; c <= 'Z'; c++)
            {
                if (!used.Contains(c)) letters.Add(c);
            }
            return letters;
        }

        /// <summary>
        /// Assign a drive letter to a volume using mountvol.  Returns true on success,
        /// false on error and outputs an error message if provided by mountvol.
        /// The caller should run with elevated privileges (manifest ensures this).
        /// </summary>
        public static bool AssignLetter(string deviceId, char letter, out string? error)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "mountvol.exe",
                    // Use double braces to escape braces in interpolated string in patch
                    Arguments = string.Format("{0}: \"{1}\"", letter, deviceId),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using var p = Process.Start(psi)!;
                _ = p.StandardOutput.ReadToEnd();
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                error = string.IsNullOrWhiteSpace(err) ? null : err.Trim();
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Remove a drive letter from a volume using mountvol.  Returns true on
        /// success.  If an error occurs, a message is returned in the out
        /// parameter.
        /// </summary>
        public static bool RemoveLetter(char letter, out string? error)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "mountvol.exe",
                    Arguments = string.Format("{0}: /D", letter),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using var p = Process.Start(psi)!;
                _ = p.StandardOutput.ReadToEnd();
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                error = string.IsNullOrWhiteSpace(err) ? null : err.Trim();
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}