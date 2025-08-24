using System.Collections.Generic;
using System.IO;
using FormatUI.Models;

namespace FormatUI.Services
{
    /// <summary>
    /// Provides a simple fallback mechanism for scanning available volumes using
    /// System.IO.DriveInfo.  This avoids WMI queries which may be disabled
    /// or return invalid query errors in certain environments.  Only ready
    /// drives with assigned drive letters are returned.
    /// </summary>
    public static class VolumeScanner
    {
        /// <summary>
        /// Enumerate all ready drives on the system.  Volumes without drive
        /// letters cannot be discovered via DriveInfo, and will therefore be
        /// omitted.  ReFS version information is not populated here and can
        /// be added in the caller via TryGetReFSVersion.
        /// </summary>
        public static List<VolumeInfo> GetVolumes()
        {
            var volumes = new List<VolumeInfo>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                    continue;

                var info = new VolumeInfo
                {
                    // DeviceId is set to the drive name (e.g. "C:\")
                    DeviceId = drive.Name,
                    DriveRoot = drive.RootDirectory.FullName,
                    Label = drive.VolumeLabel,
                    FileSystem = drive.DriveFormat,
                    CapacityBytes = (ulong)drive.TotalSize,
                    FreeBytes = (ulong)drive.TotalFreeSpace,
                    // ReFSVersion left blank; caller should fill this via TryGetReFSVersion
                    ReFSVersion = ""
                };
                volumes.Add(info);
            }
            return volumes;
        }
    }
}