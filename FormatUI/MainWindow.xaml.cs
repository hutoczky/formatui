using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using FormatUI.Models;
using FormatUI.Services;

namespace FormatUI
{
    public sealed partial class MainWindow : Window
    {
        // Állapot
        public ObservableCollection<VolumeInfo> Drives { get; } = new();
        public List<string> FileSystems { get; } = new() { "NTFS", "exFAT", "FAT32", "ReFS" };
        public List<string> AllocationUnits { get; } = new() { "Alapértelmezett", "512", "1024", "2048", "4096", "8192", "16384", "32768", "65536" };

        private bool _isBusy = false;
        private bool _eventsHooked = false;

        // A format/diskpart OEM kimenetéhez szükséges kódlapok
        static MainWindow()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        }

        private static Encoding OemEncoding
        {
            get
            {
                try { return Encoding.GetEncoding(850); } catch { return Encoding.UTF8; }
            }
        }

        public MainWindow()
        {
            this.InitializeComponent();
            this.SetTitleBar(AppTitleBar);

            // Automatikus témaválasztás a Windows beállításhoz igazítva
            try
            {
                if (this.Content is FrameworkElement root)
                {
                    root.RequestedTheme = ElementTheme.Default; // rendszerrel együtt vált
                    // backdrop theme change hook removed
                }
            }
            catch { /* best-effort */ }

            HookEvents();

            // Nyitó státusz
            SetStatusImmediate("🔧 Formázó modul inicializálva. Válassz kötetet és fájlrendszert, majd kattints a Formázás gombra.", false);

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                appWindow?.Resize(new SizeInt32(680, 580));
            }
            catch { /* opcionális */ }

            LoadReFSDriverInfo();
            RefreshDrives();

            if (FileSystemComboBox != null)
            {
                FileSystemComboBox.ItemsSource = FileSystems;
                FileSystemComboBox.SelectedIndex = 0;
            }

            if (AllocationUnitComboBox != null)
            {
                AllocationUnitComboBox.ItemsSource = AllocationUnits;
                AllocationUnitComboBox.SelectedIndex = 0;
            }
        }

        private void HookEvents()
        {
            if (_eventsHooked) return;
            if (FormatButton != null)
            {
                FormatButton.Click -= FormatButton_Click;
                FormatButton.Click += FormatButton_Click;
                _eventsHooked = true;
            }
        }

        public void InitializeWithDrive(string driveRoot)
        {
            var key = driveRoot.EndsWith("\\", StringComparison.Ordinal) ? driveRoot : (driveRoot + "\\");
            var match = Drives.FirstOrDefault(d => string.Equals(d.DriveRoot, key, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                DriveSelectorComboBox.SelectedItem = match;
            else
                DriveSelectorComboBox.Text = NormalizeDriveString(driveRoot) ?? driveRoot;
        }

        public void InitializeWithDrive() { }
        public void InitializeWithDrive(char driveLetter) => InitializeWithDrive($"{char.ToUpperInvariant(driveLetter)}:");
        public void InitializeWithDrive(DriveInfo driveInfo) => InitializeWithDrive(driveInfo?.Name ?? string.Empty);
        public void InitializeWithDrive(string? pathOrDrive, bool preferLetter)
        {
            if (string.IsNullOrWhiteSpace(pathOrDrive)) return;
            InitializeWithDrive(pathOrDrive!);
        }

        private void LoadReFSDriverInfo()
        {
            try
            {
                var driverPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\ReFS.sys");
                if (File.Exists(driverPath))
                {
                    var ver = FileVersionInfo.GetVersionInfo(driverPath).ProductVersion ?? "ismeretlen";
                    ReFSInfoTextBlock.Text = $"ReFS.sys: {ver} • támogatott formátum: 3.12";
                    EnableReFSButton.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ReFSInfoTextBlock.Text = "ReFS nincs engedélyezve ezen a rendszeren";
                    EnableReFSButton.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                ReFSInfoTextBlock.Text = $"ReFS állapot: ismeretlen ({ex.Message})";
                EnableReFSButton.Visibility = Visibility.Visible;
            }
        }

        private static (string parsed, string display) TryGetReFSVersion(string driveRoot)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "fsutil.exe",
                    Arguments = $"fsinfo refsinfo {driveRoot.TrimEnd('\\')}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi)!;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var l = line.Trim();
                    if (l.StartsWith("Version", StringComparison.OrdinalIgnoreCase) ||
                        l.StartsWith("Verzió", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = l.Split(':');
                        if (parts.Length >= 2)
                        {
                            var ver = parts[1].Trim();
                            return (ver, $"Verzió: {ver}");
                        }
                    }
                }
                return ("", "Verzió nem olvasható");
            }
            catch
            {
                return ("", "Verzió nem olvasható");
            }
        }

        private void RefreshDrives()
        {
            Drives.Clear();
            try
            {
                bool includeNoLetter = IncludeNoLetterCheckBox?.IsChecked == true;
                var rawVolumes = VolumeManager.QueryVolumes(includeNoLetter);

                foreach (var v in rawVolumes)
                {
                    string root;
                    if (!string.IsNullOrWhiteSpace(v.DriveLetter))
                        root = v.DriveLetter!.TrimEnd(':') + ":\\";
                    else
                        root = v.DeviceID;

                    string refsDisplay = string.Empty;
                    if (!string.IsNullOrWhiteSpace(v.DriveLetter) &&
                        string.Equals(v.FileSystem, "ReFS", StringComparison.OrdinalIgnoreCase))
                    {
                        var (_, display) = TryGetReFSVersion(root);
                        refsDisplay = display;
                    }

                    Drives.Add(new VolumeInfo
                    {
                        DriveRoot = root,
                        Label = v.Label,
                        FileSystem = string.IsNullOrWhiteSpace(v.FileSystem) ? "—" : v.FileSystem,
                        CapacityBytes = v.Capacity,
                        FreeBytes = v.Free,
                        ReFSVersion = refsDisplay
                    });
                }

                if (DriveSelectorComboBox != null)
                {
                    DriveSelectorComboBox.ItemsSource = null;
                    DriveSelectorComboBox.ItemsSource = Drives;
                    if (Drives.Count > 0 && DriveSelectorComboBox.SelectedItem == null)
                        DriveSelectorComboBox.SelectedIndex = 0;
                }

                if (TempLetterCombo != null)
                {
                    TempLetterCombo.Items.Clear();
                    var freeLetters = VolumeManager.GetFreeLetters();
                    foreach (var letter in freeLetters)
                        TempLetterCombo.Items.Add($"{letter}:");
                    if (freeLetters.Count > 0)
                        TempLetterCombo.SelectedIndex = 0;
                }

                if (ErrorBar != null)
                {
                    ErrorBar.IsOpen = Drives.Count == 0;
                    if (Drives.Count == 0)
                        ErrorBar.Message = "Nem találhatók elérhető kötetek.";
                }
            }
            catch (Exception ex)
            {
                if (ErrorBar != null)
                {
                    ErrorBar.Message = $"Meghajtólista frissítése sikertelen: {ex.Message}";
                    ErrorBar.IsOpen = true;
                }
                else
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        private void DriveSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void FileSystemComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void EnableReFSButton_Click(object sender, RoutedEventArgs e) { }
        private void CancelButton_Click(object sender, RoutedEventArgs e) => this.Close();
        private void UnlockNowButton_Click(object sender, RoutedEventArgs e) { }
        private void IncludeNoLetterCheckBox_Checked(object sender, RoutedEventArgs e) => RefreshDrives();

        private async void FormatButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;

            // Jelzés
            SetStatusImmediate("Formázás gomb megnyomva!", true);
            await ShowStatusAsync("Formázás gomb megnyomva!");
            await ShowStatusAsync("▶️ Formázás gomb esemény elindult.");

            string? driveParam = null;
            string currentLabel = string.Empty;
            bool mountedTemp = false;
            string? mountedLetter = null;

            if (DriveSelectorComboBox?.SelectedItem is VolumeInfo vi)
            {
                currentLabel = vi.Label ?? string.Empty;
                if (IsLetterRoot(vi.DriveRoot))
                {
                    driveParam = vi.DriveRoot.Substring(0, 2).ToUpperInvariant();
                }
                else
                {
                    // Nincs betűjel – ideiglenes hozzárendelés
                    var desiredLetter = (TempLetterCombo?.SelectedItem as string) ?? (TempLetterCombo?.Text ?? string.Empty);
                    desiredLetter = NormalizeDriveString(desiredLetter);
                    if (string.IsNullOrWhiteSpace(desiredLetter))
                    {
                        var free = VolumeManager.GetFreeLetters();
                        if (free.Count > 0) desiredLetter = $"{free[0]}:";
                    }

                    if (!string.IsNullOrWhiteSpace(desiredLetter))
                    {
                        var mountOk = await TryMountLetterAsync(vi.DriveRoot, desiredLetter);
                        if (mountOk)
                        {
                            mountedTemp = true;
                            mountedLetter = desiredLetter;
                            driveParam = desiredLetter;
                            await ShowStatusAsync($"Ideiglenes betűjel hozzárendelve: {desiredLetter}");
                        }
                        else
                        {
                            await ShowStatusAsync("Nem sikerült ideiglenes betűjelet hozzárendelni a kötethez.");
                        }
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(DriveSelectorComboBox?.Text))
            {
                driveParam = NormalizeDriveString(DriveSelectorComboBox.Text);
            }

            if (string.IsNullOrWhiteSpace(driveParam))
            {
                await ShowStatusAsync("Hiba: a formázáshoz betűjeles kötet szükséges. Adj ideiglenes betűjelet, vagy válassz betűjeles kötetet.");
                return;
            }

            string fs = FileSystemComboBox?.SelectedItem?.ToString() ?? FileSystemComboBox?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(fs))
            {
                await ShowStatusAsync("Hiba: nincs kiválasztott fájlrendszer.");
                return;
            }

            // Új címke (ha üres, használjuk a jelenlegit)
            string userLabelRaw = LabelTextBox?.Text ?? string.Empty;
            string newLabel = PrepareVolumeLabel(userLabelRaw, currentLabel);
            bool quick = (QuickFormatCheckBox?.IsChecked ?? false);

            // Megerősítés egyszer (felhasználói igény alapján marad)
            var confirmed = await ConfirmFormatAsync(driveParam!, fs, newLabel, quick, null);
            if (!confirmed)
            {
                await ShowStatusAsync("Mégse: a formázás megszakítva a felhasználó által.");
                return;
            }

            SetUiBusy(true);
            await ShowStatusAsync($"DiskPart formázás indul: select volume {driveParam.TrimEnd(':')} → format fs={fs} label=\"{newLabel}\" {(quick ? "quick" : "")}");

            // DiskPart használata – nem kér megerősítéseket
            int code = await PerformFormatDiskPartAsync(driveParam!, fs, newLabel, quick);

            if (code == 0)
                await ShowStatusAsync("✅ Kész: A formázás sikeresen lefutott (DiskPart).");
            else
                await ShowStatusAsync($"❌ Hiba: DiskPart hibakóddal tért vissza: {code}");

            if (mountedTemp && !string.IsNullOrWhiteSpace(mountedLetter))
            {
                var removed = await TryRemoveLetterAsync(mountedLetter);
                await ShowStatusAsync(removed
                    ? $"Ideiglenes betűjel eltávolítva: {mountedLetter}"
                    : $"Nem sikerült eltávolítani az ideiglenes betűjelet: {mountedLetter}");
            }

            SetUiBusy(false);
        }

        private const int ElevationRequiredCode = -740;

        private static bool IsLetterRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return false;
            var s = root.Trim();
            if (s.Length >= 2 && s[1] == ':') return true;
            return false;
        }

        private static string? NormalizeDriveString(string? driveOrPath)
        {
            if (string.IsNullOrWhiteSpace(driveOrPath)) return null;
            string s = driveOrPath.Trim();
            if (s.Length >= 2 && s[1] == ':') return $"{char.ToUpperInvariant(s[0])}:";
            if (s.Length == 1 && char.IsLetter(s[0])) return $"{char.ToUpperInvariant(s[0])}:";
            if (s.EndsWith(":\\", StringComparison.OrdinalIgnoreCase) || s.EndsWith(":", StringComparison.OrdinalIgnoreCase))
                return $"{char.ToUpperInvariant(s[0])}:";
            return null;
        }

        private async Task<int> PerformFormatDiskPartAsync(string driveLetterColon, string fileSystem, string label, bool quickFormat)
        {
            string driveLetter = driveLetterColon.Trim().TrimEnd(':');
            string scriptPath = Path.Combine(Path.GetTempPath(), $"format_{driveLetter}_{Guid.NewGuid():N}.txt");
            string quick = quickFormat ? " quick" : "";
            string script = $"select volume {driveLetter}\r\nformat fs={fileSystem} label=\"{label}\"{quick}\r\n";

            try
            {
                await File.WriteAllTextAsync(scriptPath, script, Encoding.ASCII);
                await ShowStatusAsync($"[DiskPart script]\r\n{script}");

                // 1) Próbáljuk meg NEM elevált módban, kimenet-folyamatos olvasással
                var psi = new ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = $"/s \"{scriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = OemEncoding,
                    StandardErrorEncoding = OemEncoding
                };

                try
                {
                    using var p = Process.Start(psi)!;
                    p.OutputDataReceived += (_, e) => { if (e.Data != null) _ = ShowStatusAsync(e.Data); };
                    p.ErrorDataReceived += (_, e) => { if (e.Data != null) _ = ShowStatusAsync("[STDERR] " + e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    await Task.Run(() => p.WaitForExit());
                    return p.ExitCode;
                }
                catch (Win32Exception wex) when (wex.NativeErrorCode == 740 || wex.NativeErrorCode == 5)
                {
                    // 2) Elevált fallback – itt nincs kimenet, de automatikus
                    await ShowStatusAsync("🔐 DiskPart elevált módban indul (UAC). Kimenet nem lesz elérhető.");
                    var psiAdmin = new ProcessStartInfo
                    {
                        FileName = "diskpart.exe",
                        Arguments = $"/s \"{scriptPath}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var p2 = Process.Start(psiAdmin);
                    if (p2 == null) return -1;
                    await Task.Run(() => p2.WaitForExit());
                    return p2.ExitCode;
                }
            }
            catch (Exception ex)
            {
                await ShowStatusAsync($"❌ Hiba a DiskPart futtatás során: {ex.Message}");
                return -1;
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { /* no-op */ }
            }
        }

        private async Task<bool> TryMountLetterAsync(string volumeRootOrGuid, string letterColon)
        {
            string cmd = $"mountvol {letterColon} {volumeRootOrGuid}";
            int code = await RunCmdAsync(cmd, capture: true, elevateOn740: true);
            return code == 0;
        }

        private async Task<bool> TryRemoveLetterAsync(string letterColon)
        {
            string cmd = $"mountvol {letterColon} /d";
            int code = await RunCmdAsync(cmd, capture: true, elevateOn740: true);
            return code == 0;
        }

        private async Task<int> RunCmdAsync(string command, bool capture = true, bool elevateOn740 = true)
        {
            if (capture)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + command,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = OemEncoding,
                    StandardErrorEncoding = OemEncoding
                };
                try
                {
                    using var p = Process.Start(psi)!;
                    p.OutputDataReceived += (_, e) => { if (e.Data != null) _ = ShowStatusAsync(e.Data); };
                    p.ErrorDataReceived += (_, e) => { if (e.Data != null) _ = ShowStatusAsync("[STDERR] " + e.Data); };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    await Task.Run(() => p.WaitForExit());
                    return p.ExitCode;
                }
                catch (Win32Exception wex) when (elevateOn740 && (wex.NativeErrorCode == 740 || wex.NativeErrorCode == 5))
                {
                    // eszkaláció szükséges – tovább a runas ágra
                }
                catch (Exception ex)
                {
                    await ShowStatusAsync("Parancs futtatási hiba: " + ex.Message);
                    return -1;
                }
            }

            // elevált fallback
            try
            {
                var psi2 = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + command,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };
                using var p2 = Process.Start(psi2)!;
                await Task.Run(() => p2.WaitForExit());
                return p2.ExitCode;
            }
            catch (Win32Exception wex) when (wex.NativeErrorCode == 1223)
            {
                await ShowStatusAsync("Felhasználó elutasította az UAC kérést.");
                return -1;
            }
            catch (Exception ex)
            {
                await ShowStatusAsync("Elevált parancs hiba: " + ex.Message);
                return -1;
            }
        }

        private static string PrepareVolumeLabel(string userInput, string fallbackCurrent)
        {
            string s = (userInput ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(s)) s = (fallbackCurrent ?? string.Empty).Trim();
            s = s.Replace("\"", string.Empty);
            s = Regex.Replace(s, @"[\\/:*?<>|]", "_"); // szóköz maradhat
            if (s.Length > 32) s = s.Substring(0, 32);
            return s;
        }

        // Felugró megerősítés ContentDialog-gal (WinUI 3)
        private async Task<bool> ConfirmFormatAsync(string drive, string fs, string label, bool quick, string? allocParam)
        {
            string q = quick ? "Gyorsformázás" : "Teljes formázás";
            string msg =
                $"⚠️ FIGYELEM: A(z) {drive} kötet MINDEN adata véglegesen törlődik.\n\n" +
                $"Beállítások:\n • Fájlrendszer: {fs}\n • Címke: \"{label}\"\n • Mód: {q}\n\n" +
                "Biztosan folytatod a formázást?";

            var dlg = new ContentDialog
            {
                Title = "Formázás megerősítése",
                Content = msg,
                PrimaryButtonText = "Igen, formázz",
                CloseButtonText = "Mégse",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = (this.Content as FrameworkElement)?.XamlRoot
            };

            var res = await dlg.ShowAsync();
            return res == ContentDialogResult.Primary;
        }

        private void SetStatusImmediate(string text, bool append = true)
        {
            try
            {
                if (FormatProgressPanel != null)
                    FormatProgressPanel.Visibility = Visibility.Visible;
                if (FormatProgressBar != null)
                    FormatProgressBar.IsIndeterminate = true;

                if (FormatStatusText != null)
                {
                    if (!append || string.IsNullOrEmpty(FormatStatusText.Text))
                        FormatStatusText.Text = text;
                    else
                        FormatStatusText.Text += Environment.NewLine + text;
                }
                else
                {
                    Debug.WriteLine(text);
                }
            }
            catch { /* best-effort */ }
        }

        private void SetUiBusy(bool busy)
        {
            _isBusy = busy;

            if (FormatProgressPanel != null)
                FormatProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

            if (FormatProgressBar != null)
                FormatProgressBar.IsIndeterminate = busy;

            if (DriveSelectorComboBox != null) DriveSelectorComboBox.IsEnabled = !busy;
            if (FileSystemComboBox != null) FileSystemComboBox.IsEnabled = !busy;
            if (AllocationUnitComboBox != null) AllocationUnitComboBox.IsEnabled = !busy;
            if (LabelTextBox != null) LabelTextBox.IsEnabled = !busy;
            if (QuickFormatCheckBox != null) QuickFormatCheckBox.IsEnabled = !busy;
            if (EnableReFSButton != null) EnableReFSButton.IsEnabled = !busy;
            if (IncludeNoLetterCheckBox != null) IncludeNoLetterCheckBox.IsEnabled = !busy;
            if (TempLetterCombo != null) TempLetterCombo.IsEnabled = !busy;
            if (FormatButton != null) FormatButton.IsEnabled = !busy;
        }

        private void ClearStatus()
        {
            if (FormatStatusText != null)
                FormatStatusText.Text = string.Empty;
        }

        private async Task ShowStatusAsync(string line)
        {
            await DispatcherQueueAsync(() =>
            {
                if (FormatStatusText == null) { Debug.WriteLine(line); return; }
                if (string.IsNullOrEmpty(FormatStatusText.Text))
                    FormatStatusText.Text = line;
                else
                    FormatStatusText.Text += Environment.NewLine + line;
            });
        }

        private Task DispatcherQueueAsync(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (DispatcherQueue.HasThreadAccess)
            {
                try { action(); tcs.TrySetResult(true); }
                catch (Exception ex) { tcs.TrySetException(ex); }
                return tcs.Task;
            }

            bool enqueued = DispatcherQueue.TryEnqueue(() =>
            {
                try { action(); tcs.TrySetResult(true); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });

            if (!enqueued)
                tcs.TrySetException(new InvalidOperationException("DispatcherQueue.TryEnqueue failed."));

            return tcs.Task;
        }
    }
}