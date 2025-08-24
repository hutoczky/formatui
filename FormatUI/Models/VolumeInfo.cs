using System;

namespace FormatUI.Models
{
    /// <summary>
    /// Represents a volume or disk partition with optional drive letter.  It
    /// provides metadata such as label, file system and capacity for UI
    /// display.
    /// </summary>
    public class VolumeInfo
    {
        /// <summary>
        /// WMI DeviceID for the volume (e.g. "\\?\Volume{GUID}\"). Always present.
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// Root path including trailing backslash if the volume has a drive letter (e.g. "E:\").
        /// Empty if no letter is assigned.
        /// </summary>
        public string DriveRoot { get; set; } = string.Empty;

        /// <summary>
        /// True if the volume has a drive letter assigned.
        /// </summary>
        public bool HasLetter => !string.IsNullOrWhiteSpace(DriveRoot);

        /// <summary>
        /// Volume label or an empty string if none.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// File system type (e.g. "NTFS", "exFAT", "ReFS") or an empty string if unknown.
        /// </summary>
        public string FileSystem { get; set; } = string.Empty;

        /// <summary>
        /// Capacity in bytes.
        /// </summary>
        public ulong CapacityBytes { get; set; }
        = 0UL;

        /// <summary>
        /// Free space in bytes.
        /// </summary>
        public ulong FreeBytes { get; set; }
        = 0UL;

        /// <summary>
        /// Display string for the ReFS version when applicable. Defaults to "—".
        /// </summary>
        public string ReFSVersion { get; set; } = "—";

        /// <summary>
        /// Human readable capacity (GB/TB) used in the UI.
        /// </summary>
        public string CapacityHuman
        {
            get
            {
                double gb = CapacityBytes / 1024d / 1024d / 1024d;
                if (gb >= 1024)
                {
                    return $"{gb / 1024d:0.##} TB";
                }
                return $"{gb:0.##} GB";
            }
        }

        /// <summary>
        /// Combined description used in ComboBox display: drive letter/device, label, file system and capacity.
        /// </summary>
        public string DisplayName
        {
            get
            {
                // Derive a shortened device identifier if possible
                var idShort = DeviceId;
                if (!string.IsNullOrWhiteSpace(idShort))
                {
                    try
                    {
                        var i1 = idShort.IndexOf('{');
                        var i2 = idShort.IndexOf('}');
                        if (i1 >= 0 && i2 > i1)
                        {
                            idShort = "Volume" + idShort.Substring(i1, i2 - i1 + 1);
                        }
                    }
                    catch
                    {
                        // Use full DeviceId if parsing fails
                    }
                }
                var left = HasLetter ? DriveRoot : idShort;
                var lbl = string.IsNullOrWhiteSpace(Label) ? "(nincs címke)" : Label;
                var fs = string.IsNullOrWhiteSpace(FileSystem) ? "—" : FileSystem;
                return $"{left}  {lbl} — {fs} — {CapacityHuman}";
            }
        }
    }
}