using System;
using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace FormatUI.Services
{
    /// <summary>
    /// Supported file systems for formatting.  Note that ReFS is only
    /// available on Windows 11 and certain SKU editions.
    /// </summary>
    public enum FsType
    {
        NTFS,
        exFAT,
        FAT32,
        ReFS
    }

    /// <summary>
    /// Result of a format operation including success flag, message and raw
    /// captured output.  Engine indicates which method was used (WMI,
    /// Format‑Volume or format.com).
    /// </summary>
    public class FormatResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Engine { get; set; } = string.Empty;
        public string RawLog { get; set; } = string.Empty;
    }

    /// <summary>
    /// Implements multiple strategies for formatting volumes.  The typical
    /// sequence is to try WMI first; if that fails, fall back to PowerShell's
    /// Format‑Volume; and finally use format.com for legacy file systems.
    /// </summary>
    public static class FormatEngines
    {
        /// <summary>
        /// Attempt to format a volume selected by drive letter using WMI.
        /// </summary>
        public static FormatResult TryWmiFormatByLetter(string driveRoot, FsType fs, string? label, bool quick, uint cluster)
        {
            var log = new StringBuilder();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    new SelectQuery("Win32_Volume", $"DriveLetter = '{driveRoot.TrimEnd('\\')}"));
                foreach (ManagementObject vol in searcher.Get())
                {
                    return InvokeWmiFormat(vol, fs, label, quick, cluster, log);
                }
                log.AppendLine("[WMI] Win32_Volume not found (DriveLetter)");
                return new FormatResult { Success = false, Message = "Volume not found via WMI", Engine = "WMI", RawLog = log.ToString() };
            }
            catch (Exception ex)
            {
                log.AppendLine("[WMI] Exception: " + ex.Message);
                return new FormatResult { Success = false, Message = ex.Message, Engine = "WMI", RawLog = log.ToString() };
            }
        }

        /// <summary>
        /// Attempt to format a volume selected by device ID using WMI.
        /// </summary>
        public static FormatResult TryWmiFormatByDeviceId(string deviceId, FsType fs, string? label, bool quick, uint cluster)
        {
            var log = new StringBuilder();
            try
            {
                var escaped = deviceId.Replace("\\", "\\\\").Replace("\"", "\\\"");
                using var searcher = new ManagementObjectSearcher(
                    new SelectQuery("Win32_Volume", $"DeviceID = \"{escaped}\""));
                foreach (ManagementObject vol in searcher.Get())
                {
                    return InvokeWmiFormat(vol, fs, label, quick, cluster, log);
                }
                log.AppendLine("[WMI] Win32_Volume not found (DeviceID)");
                return new FormatResult { Success = false, Message = "Volume not found via WMI", Engine = "WMI", RawLog = log.ToString() };
            }
            catch (Exception ex)
            {
                log.AppendLine("[WMI] Exception: " + ex.Message);
                return new FormatResult { Success = false, Message = ex.Message, Engine = "WMI", RawLog = log.ToString() };
            }
        }

        private static FormatResult InvokeWmiFormat(ManagementObject vol, FsType fs, string? label, bool quick, uint cluster, StringBuilder log)
        {
            using var inParams = vol.GetMethodParameters("Format");
            inParams["FileSystem"] = fs.ToString();
            inParams["QuickFormat"] = quick;
            inParams["ClusterSize"] = cluster == 0 ? null : (object)cluster;
            inParams["Label"] = string.IsNullOrWhiteSpace(label) ? null : label;
            inParams["EnableCompression"] = false;
            inParams["Full"] = !quick;
            inParams["Force"] = true;
            var outParams = vol.InvokeMethod("Format", inParams, null);
            uint ret = (outParams? ["ReturnValue"] is uint u) ? u : 1u;
            log.AppendLine($"[WMI] ReturnValue={ret}");
            if (ret == 0)
            {
                return new FormatResult { Success = true, Message = $"Formázva: {fs}", Engine = "WMI", RawLog = log.ToString() };
            }
            return new FormatResult { Success = false, Message = $"WMI visszatérési kód: {ret}", Engine = "WMI", RawLog = log.ToString() };
        }

        /// <summary>
        /// Attempt to format using the legacy format.com utility.  Only supports
        /// NTFS, exFAT and FAT32; ReFS is not available via this tool.
        /// </summary>
        public static FormatResult TryFormatCom(string driveRoot, FsType fs, string? label, bool quick, uint cluster)
        {
            var log = new StringBuilder();
            try
            {
                if (fs == FsType.ReFS)
                {
                    return new FormatResult { Success = false, Message = "format.com does not support ReFS", Engine = "format.com", RawLog = string.Empty };
                }
                // Trim the trailing backslash and colon from the drive root to obtain the drive letter.
                // Use escaped backslash for the TrimEnd char literal to avoid syntax errors.
                var letter = driveRoot.TrimEnd('\\', ':');
                var args = new StringBuilder();
                args.Append($" {letter}: /FS:{fs} ");
                if (quick) args.Append(" /Q ");
                args.Append(" /Y ");
                var sanitizedLabel = (label ?? string.Empty).Replace("\"", string.Empty);
                if (!string.IsNullOrWhiteSpace(sanitizedLabel)) args.Append($" /V:{sanitizedLabel} ");
                if (cluster != 0) args.Append($" /A:{cluster} ");
                var psi = new ProcessStartInfo
                {
                    FileName = "format.com",
                    Arguments = args.ToString(),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using var p = Process.Start(psi)!;
                // format.com prompts for confirmation; send a blank line to accept.
                p.StandardInput.WriteLine();
                var output = p.StandardOutput.ReadToEnd();
                var error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                log.AppendLine(output);
                log.AppendLine(error);
                if (p.ExitCode == 0 || Regex.IsMatch(output, @"Format complete|A formázás befejeződött", RegexOptions.IgnoreCase))
                {
                    return new FormatResult { Success = true, Message = $"Formázva: {fs} (format.com)", Engine = "format.com", RawLog = log.ToString() };
                }
                return new FormatResult { Success = false, Message = $"format.com hibakód: {p.ExitCode}", Engine = "format.com", RawLog = log.ToString() };
            }
            catch (Exception ex)
            {
                log.AppendLine("Exception: " + ex.Message);
                return new FormatResult { Success = false, Message = ex.Message, Engine = "format.com", RawLog = log.ToString() };
            }
        }

        /// <summary>
        /// Attempt to format a volume via PowerShell's Format‑Volume cmdlet.  This
        /// supports ReFS on systems where the feature is enabled.
        /// </summary>
        public static FormatResult TryPowerShellStorage(string target, FsType fs, string? label, bool quick, uint cluster)
        {
            var log = new StringBuilder();
            try
            {
                var labelArg = string.IsNullOrWhiteSpace(label) ? string.Empty : $"-NewFileSystemLabel '{label.Replace("'", "''")}'";
                var allocArg = cluster == 0 ? string.Empty : $"-AllocationUnitSize {cluster}";
                var quickArg = "-Confirm:$false";
                string cmd;
                if (target.StartsWith(@"\\?\Volume{"))
                {
                    cmd = $"Format-Volume -Path '{target}' -FileSystem {fs} {labelArg} {allocArg} -Force {quickArg}";
                }
                else
                {
                    // Trim the trailing backslash and colon from the target (drive root) for the DriveLetter parameter.
                    var letter = target.TrimEnd('\\', ':');
                    cmd = $"Format-Volume -DriveLetter {letter} -FileSystem {fs} {labelArg} {allocArg} -Force {quickArg}";
                }
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + cmd,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using var p = Process.Start(psi)!;
                var output = p.StandardOutput.ReadToEnd();
                var error = p.StandardError.ReadToEnd();
                p.WaitForExit();
                log.AppendLine(output);
                log.AppendLine(error);
                if (p.ExitCode == 0 && !Regex.IsMatch(error, @"(error|hiba)", RegexOptions.IgnoreCase))
                {
                    return new FormatResult { Success = true, Message = $"Formázva: {fs} (Format-Volume)", Engine = "Format-Volume", RawLog = log.ToString() };
                }
                return new FormatResult { Success = false, Message = $"Format-Volume hibakód: {p.ExitCode}", Engine = "Format-Volume", RawLog = log.ToString() };
            }
            catch (Exception ex)
            {
                log.AppendLine("Exception: " + ex.Message);
                return new FormatResult { Success = false, Message = ex.Message, Engine = "Format-Volume", RawLog = log.ToString() };
            }
        }
    }
}