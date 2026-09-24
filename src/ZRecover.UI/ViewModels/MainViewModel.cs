using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZRecover.Core;
using ZRecover.Core.Disk;
using ZRecover.Core.Intelligence;
using ZRecover.Core.Models;
using ZRecover.Core.Safety;

namespace ZRecover.UI.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly RecoveryService _recoveryService = new();
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<DriveVolumeInfo> Drives { get; } = [];
    public ObservableCollection<RecoverableFile> AllFiles { get; } = [];
    public ObservableCollection<RecoverableFile> DisplayFiles { get; } = [];

    private DriveVolumeInfo? _selectedDrive;
    public DriveVolumeInfo? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (_selectedDrive != value)
            {
                _selectedDrive = value;
                OnPropertyChanged();
                ValidateDestination();
            }
        }
    }

    private ScanMode _currentMode = ScanMode.QuickUndelete;
    public ScanMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (_currentMode != value)
            {
                _currentMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentModeTitle));
                OnPropertyChanged(nameof(CurrentModeDescription));
            }
        }
    }

    public string CurrentModeTitle => CurrentMode switch
    {
        ScanMode.QuickUndelete => "Quick Undelete (MFT / FAT)",
        ScanMode.DeepCarve => "Deep Raw Carving",
        ScanMode.VssSnapshot => "Shadow Explorer (VSS)",
        ScanMode.RecycleBin => "Recycle Rescue",
        _ => "Data Recovery"
    };

    public string CurrentModeDescription => CurrentMode switch
    {
        ScanMode.QuickUndelete => "High-speed parsing of NTFS Master File Table ($MFT) and FAT directory records for recently deleted files.",
        ScanMode.DeepCarve => "Exhaustive sector-by-sector file signature carving across unallocated disk space.",
        ScanMode.VssSnapshot => "Mounts historical Windows Volume Shadow Copies to extract earlier snapshots of deleted files.",
        ScanMode.RecycleBin => "Decodes INFO2 / $I metadata to reconstruct files purged from the Windows Recycle Bin.",
        _ => "Sovereign deep recovery engine."
    };

    private FileCategory _selectedCategory = FileCategory.All;
    public FileCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory != value)
            {
                _selectedCategory = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    }

    private string _searchFilter = string.Empty;
    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (_searchFilter != value)
            {
                _searchFilter = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    }

    private RecoverableFile? _selectedFile;
    public RecoverableFile? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (_selectedFile != value)
            {
                _selectedFile = value;
                if (_selectedFile != null)
                {
                    SmartFileIdentifier.Analyze(_selectedFile);
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedFile));
                OnPropertyChanged(nameof(SelectedFilePreviewBytes));
                OnPropertyChanged(nameof(SelectedFilePreviewText));
                OnPropertyChanged(nameof(SelectedFileHasSuggestion));
                OnPropertyChanged(nameof(IsSelectedFileImage));
                OnPropertyChanged(nameof(IsNotSelectedFileImage));
                OnPropertyChanged(nameof(SelectedFileImageSource));
                OnPropertyChanged(nameof(SelectedFileImageDimensions));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasSelectedFile => SelectedFile != null;
    public bool SelectedFileHasSuggestion => SelectedFile?.HasSuggestion ?? false;
    public byte[]? SelectedFilePreviewBytes => SelectedFile?.PreviewBytes ?? SelectedFile?.ResidentData;
    public string? SelectedFilePreviewText => SelectedFile?.PreviewText;

    public bool IsSelectedFileImage
    {
        get
        {
            if (SelectedFile == null) return false;
            string ext = (SelectedFile.SuggestedExtension ?? SelectedFile.Extension).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico";
        }
    }

    public bool IsNotSelectedFileImage => !IsSelectedFileImage;

    public ImageSource? SelectedFileImageSource
    {
        get
        {
            if (!IsSelectedFileImage || SelectedFile == null) return null;

            try
            {
                if (!string.IsNullOrEmpty(SelectedFile.PhysicalPath) && File.Exists(SelectedFile.PhysicalPath))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(SelectedFile.PhysicalPath);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }

                byte[]? bytes = SelectedFile.PreviewBytes ?? SelectedFile.ResidentData;
                if (bytes != null && bytes.Length > 32)
                {
                    using var ms = new MemoryStream(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { }

            return null;
        }
    }

    public string SelectedFileImageDimensions
    {
        get
        {
            if (SelectedFileImageSource is BitmapSource bs)
            {
                return $"{bs.PixelWidth} × {bs.PixelHeight} px ({bs.Format.BitsPerPixel} bpp)";
            }
            return IsSelectedFileImage ? "Image format detected" : string.Empty;
        }
    }

    // Scan Scope & Targeted Scanning
    private bool _isFolderScope;
    public bool IsFolderScope
    {
        get => _isFolderScope;
        set
        {
            if (_isFolderScope != value)
            {
                _isFolderScope = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDriveScope));
                if (_isFolderScope && string.IsNullOrWhiteSpace(TargetFolderPath))
                {
                    TargetFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }
                OnPropertyChanged(nameof(CanStartScan));
            }
        }
    }

    public bool IsDriveScope
    {
        get => !IsFolderScope;
        set => IsFolderScope = !value;
    }

    private string _targetFolderPath = string.Empty;
    public string TargetFolderPath
    {
        get => _targetFolderPath;
        set
        {
            if (_targetFolderPath != value)
            {
                _targetFolderPath = value;
                OnPropertyChanged();
                SyncSourceDriveFromFolder();
                OnPropertyChanged(nameof(CanStartScan));
            }
        }
    }

    private bool _preserveFolderStructure = true;
    public bool PreserveFolderStructure
    {
        get => _preserveFolderStructure;
        set { _preserveFolderStructure = value; OnPropertyChanged(); }
    }

    private void SyncSourceDriveFromFolder()
    {
        if (string.IsNullOrWhiteSpace(TargetFolderPath)) return;
        try
        {
            string root = Path.GetPathRoot(TargetFolderPath) ?? string.Empty;
            string driveLetter = root.TrimEnd('\\', '/');
            var matchedDrive = Drives.FirstOrDefault(d => d.DriveLetter.Equals(driveLetter, StringComparison.OrdinalIgnoreCase));
            if (matchedDrive != null && SelectedDrive != matchedDrive)
            {
                SelectedDrive = matchedDrive;
            }
        }
        catch { }
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (_isScanning != value)
            {
                _isScanning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotScanning));
                OnPropertyChanged(nameof(CanStartScan));
                OnPropertyChanged(nameof(CanCancelScan));
                OnPropertyChanged(nameof(CanRestore));
                OnPropertyChanged(nameof(ShowEmptyState));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsNotScanning => !IsScanning;
    public bool CanStartScan => !IsScanning && SelectedDrive != null && (!IsFolderScope || !string.IsNullOrWhiteSpace(TargetFolderPath));
    public bool CanCancelScan => IsScanning;
    public bool CanRestore => !IsScanning && IsDestinationSafe && HasSelectedFiles;
    public bool ShowEmptyState => !IsScanning && AllFiles.Count == 0;

    private double _scanProgress;
    public double ScanProgress
    {
        get => _scanProgress;
        set { _scanProgress = value; OnPropertyChanged(); }
    }

    private string _statusText = "Ready to scan. Select a source drive and recovery mode.";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private string _throughputText = string.Empty;
    public string ThroughputText
    {
        get => _throughputText;
        set { _throughputText = value; OnPropertyChanged(); }
    }

    private string _destinationPath = string.Empty;
    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (_destinationPath != value)
            {
                _destinationPath = value;
                OnPropertyChanged();
                ValidateDestination();
            }
        }
    }

    private bool _isDestinationSafe;
    public bool IsDestinationSafe
    {
        get => _isDestinationSafe;
        set { _isDestinationSafe = value; OnPropertyChanged(); }
    }

    private string _safetyMessage = "Select an external drive or secondary partition as recovery destination.";
    public string SafetyMessage
    {
        get => _safetyMessage;
        set { _safetyMessage = value; OnPropertyChanged(); }
    }

    // Dynamic Category Counts
    public int CountAll => AllFiles.Count;
    public int CountDocuments => AllFiles.Count(f => f.Category == FileCategory.Documents);
    public int CountPictures => AllFiles.Count(f => f.Category == FileCategory.Pictures);
    public int CountMedia => AllFiles.Count(f => f.Category == FileCategory.Videos || f.Category == FileCategory.Audio);
    public int CountArchives => AllFiles.Count(f => f.Category == FileCategory.Archives);
    public int CountOthers => AllFiles.Count(f => f.Category == FileCategory.Other || f.Category == FileCategory.Executable || f.Category == FileCategory.Databases);

    // Selection Statistics
    public int SelectedFilesCount => DisplayFiles.Count(f => f.IsSelected);
    public long SelectedFilesBytes => DisplayFiles.Where(f => f.IsSelected).Sum(f => f.Size);
    public string SelectedFilesSizeFormatted => FormatBytes(SelectedFilesBytes);
    public bool HasSelectedFiles => SelectedFilesCount > 0;

    public string RestoreButtonText => SelectedFilesCount > 0
        ? $"⚡ Restore {SelectedFilesCount} Files ({SelectedFilesSizeFormatted})"
        : "⚡ Restore Selected Files";

    // InfoBar In-Window Notifications
    private bool _isInfoBarVisible;
    public bool IsInfoBarVisible
    {
        get => _isInfoBarVisible;
        set { _isInfoBarVisible = value; OnPropertyChanged(); }
    }

    private string _infoBarMessage = string.Empty;
    public string InfoBarMessage
    {
        get => _infoBarMessage;
        set { _infoBarMessage = value; OnPropertyChanged(); }
    }

    private string _infoBarSeverity = "Info"; // "Info", "Success", "Warning", "Error"
    public string InfoBarSeverity
    {
        get => _infoBarSeverity;
        set { _infoBarSeverity = value; OnPropertyChanged(); }
    }

    public bool IsElevated { get; } = IsProcessElevated();
    public bool IsNotElevated => !IsElevated;

    // Commands
    public ICommand StartScanCommand { get; }
    public ICommand CancelScanCommand { get; }
    public ICommand RestoreSelectedCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }
    public ICommand RefreshDrivesCommand { get; }
    public ICommand SetCategoryCommand { get; }
    public ICommand OpenDestinationCommand { get; }
    public ICommand DismissInfoBarCommand { get; }
    public ICommand RelaunchAsAdminCommand { get; }
    public ICommand ApplySuggestedNameCommand { get; }
    public ICommand SetPresetScopeCommand { get; }
    public ICommand BrowseFolderScopeCommand { get; }

    public MainViewModel()
    {
        StartScanCommand = new RelayCommand(async _ => await ExecuteScanAsync(), _ => CanStartScan);
        CancelScanCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning);
        RestoreSelectedCommand = new RelayCommand(async _ => await ExecuteRestoreAsync(), _ => CanRestore);
        SelectAllCommand = new RelayCommand(_ => SetAllSelected(true));
        DeselectAllCommand = new RelayCommand(_ => SetAllSelected(false));
        RefreshDrivesCommand = new RelayCommand(_ => LoadDrives(), _ => !IsScanning);
        SetPresetScopeCommand = new RelayCommand(param =>
        {
            if (param is string preset)
            {
                IsFolderScope = true;
                string? path = preset.ToLowerInvariant() switch
                {
                    "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    _ => null
                };

                if (preset.Equals("recyclebin", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentMode = ScanMode.RecycleBin;
                    TargetFolderPath = Path.Combine(SelectedDrive?.DriveLetter ?? "C:", "$Recycle.Bin");
                }
                else if (!string.IsNullOrEmpty(path))
                {
                    TargetFolderPath = path;
                }
            }
        });
        BrowseFolderScopeCommand = new RelayCommand(_ =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select Specific Target Folder to Scan for Recoverable Files";
            dialog.UseDescriptionForTitle = true;
            if (!string.IsNullOrWhiteSpace(TargetFolderPath) && Directory.Exists(TargetFolderPath))
            {
                dialog.InitialDirectory = TargetFolderPath;
            }
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                IsFolderScope = true;
                TargetFolderPath = dialog.SelectedPath;
            }
        });
        ApplySuggestedNameCommand = new RelayCommand(_ =>
        {
            if (SelectedFile != null && SelectedFile.HasSuggestion)
            {
                string oldName = SelectedFile.FileName;
                SelectedFile.ApplySuggestedName();
                OnPropertyChanged(nameof(SelectedFile));
                OnPropertyChanged(nameof(SelectedFileHasSuggestion));
                UpdateCategoryCounts();
                ApplyFilter();
                ShowInfoBar($"Renamed '{oldName}' -> '{SelectedFile.FileName}'", "Success");
            }
        }, _ => SelectedFile != null && SelectedFile.HasSuggestion);
        RelaunchAsAdminCommand = new RelayCommand(_ =>
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = processPath,
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                    Application.Current.Shutdown();
                }
            }
            catch { }
        });
        SetCategoryCommand = new RelayCommand(param =>
        {
            if (param is FileCategory cat)
            {
                SelectedCategory = cat;
            }
            else if (param is string s && Enum.TryParse<FileCategory>(s, out var parsed))
            {
                SelectedCategory = parsed;
            }
        });
        OpenDestinationCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrWhiteSpace(DestinationPath) && Directory.Exists(DestinationPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = DestinationPath,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }, _ => !string.IsNullOrWhiteSpace(DestinationPath) && Directory.Exists(DestinationPath));

        DismissInfoBarCommand = new RelayCommand(_ => IsInfoBarVisible = false);

        LoadDrives();

        if (!IsProcessElevated())
        {
            ShowInfoBar("Standard user mode: ZRecover will use deep filesystem scanning. Run as Administrator for low-level NTFS MFT & raw sector access.", "Info");
        }
    }

    private static bool IsProcessElevated()
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        return false;
    }

    public void LoadDrives()
    {
        string? previousLetter = SelectedDrive?.DriveLetter;
        Drives.Clear();
        var detected = VolumeEnumerator.EnumerateDrives();
        foreach (var d in detected)
        {
            Drives.Add(d);
        }

        if (Drives.Count > 0)
        {
            SelectedDrive = Drives.FirstOrDefault(d => d.DriveLetter == previousLetter) ?? Drives[0];
            
            // Propose a default safe destination on a distinct volume if available
            if (string.IsNullOrWhiteSpace(DestinationPath) || !IsDestinationSafe)
            {
                var distinctDrive = Drives.FirstOrDefault(d => !string.Equals(d.DriveLetter, SelectedDrive.DriveLetter, StringComparison.OrdinalIgnoreCase));
                if (distinctDrive != null)
                {
                    DestinationPath = Path.Combine(distinctDrive.DriveLetter + "\\", "ZRecover_Restored");
                }
                else
                {
                    DestinationPath = @"D:\ZRecover_Restored";
                }
            }
        }
        ValidateDestination();
    }

    public void ValidateDestination()
    {
        if (SelectedDrive == null)
        {
            IsDestinationSafe = false;
            SafetyMessage = "No source drive selected.";
            return;
        }

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            IsDestinationSafe = false;
            SafetyMessage = "Recovery destination directory must be specified.";
            return;
        }

        try
        {
            SafetyBarrier.ValidateDestination(SelectedDrive.DriveLetter, DestinationPath);
            IsDestinationSafe = true;
            SafetyMessage = $"🛡️ Zero-Write Safety Active: Destination ({DestinationPath}) is on a distinct volume from source ({SelectedDrive.DriveLetter}). Drive sectors are protected from overwrite.";
        }
        catch (Exception ex)
        {
            IsDestinationSafe = false;
            SafetyMessage = ex.Message;
        }
    }

    public void ShowInfoBar(string message, string severity = "Info")
    {
        InfoBarMessage = message;
        InfoBarSeverity = severity;
        IsInfoBarVisible = true;
    }

    private async Task ExecuteScanAsync()
    {
        if (SelectedDrive == null) return;

        IsScanning = true;
        ScanProgress = 0;
        string scopeDesc = IsFolderScope && !string.IsNullOrWhiteSpace(TargetFolderPath)
            ? $"Folder: {Path.GetFileName(TargetFolderPath)}"
            : SelectedDrive.DriveLetter;
        StatusText = $"Scanning {scopeDesc} ({CurrentModeTitle})...";
        AllFiles.Clear();
        DisplayFiles.Clear();
        SelectedFile = null;
        UpdateCategoryCounts();

        _scanCts = new CancellationTokenSource();
        var options = new ScanOptions
        {
            TargetDrive = SelectedDrive.DriveLetter,
            Mode = CurrentMode,
            FilterCategory = SelectedCategory,
            SearchQuery = SearchFilter,
            TargetFolderPath = IsFolderScope ? TargetFolderPath : null
        };

        var progress = new Progress<ScanProgressReport>(report =>
        {
            ScanProgress = report.Percent;
            ThroughputText = $"{report.MegaBytesPerSecond:F1} MB/s | {report.FilesFound} items found";
            StatusText = report.CurrentOperation;
        });

        try
        {
            RawDiskReader? diskReader = null;
            if (CurrentMode is ScanMode.DeepCarve or ScanMode.QuickUndelete)
            {
                try
                {
                    diskReader = new RawDiskReader(SelectedDrive.DevicePath);
                }
                catch (Exception ex)
                {
                    ShowInfoBar($"Direct raw volume access requires Administrator privileges ({ex.Message}). Activating deep user-mode filesystem search engine.", "Warning");
                }
            }

            using (diskReader)
            {
                var files = await _recoveryService.ExecuteScanAsync(options, diskReader, progress, _scanCts.Token);
                foreach (var f in files)
                {
                    AllFiles.Add(f);
                }
            }

            ApplyFilter();
            ScanProgress = 100;
            StatusText = $"Scan complete! Found {AllFiles.Count} recoverable files.";
            ThroughputText = $"Completed ({AllFiles.Count} items)";
            ShowInfoBar($"Scan complete: Discovered {AllFiles.Count} recoverable candidates on {SelectedDrive.DriveLetter}.", "Success");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled by user.";
            ThroughputText = "Cancelled";
            ShowInfoBar("Scan operation cancelled.", "Info");
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
            ThroughputText = "Failed";
            ShowInfoBar($"Scan failed: {ex.Message}", "Error");
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(ShowEmptyState));
            UpdateCategoryCounts();
        }
    }

    private void CancelScan()
    {
        if (_scanCts != null && !_scanCts.IsCancellationRequested)
        {
            StatusText = "Cancelling scan... Please wait.";
            ThroughputText = "Aborting...";
            _scanCts.Cancel();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ExecuteRestoreAsync()
    {
        if (SelectedDrive == null || !IsDestinationSafe || string.IsNullOrEmpty(DestinationPath))
            return;

        var selected = DisplayFiles.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;

        IsScanning = true;
        StatusText = $"Restoring {selected.Count} files to '{DestinationPath}'...";

        try
        {
            int successCount = 0;
            foreach (var file in selected)
            {
                await _recoveryService.RestoreFileAsync(file, DestinationPath, PreserveFolderStructure);
                successCount++;
                StatusText = $"Restored {successCount}/{selected.Count} files...";
            }

            StatusText = $"Restoration complete! {successCount} files saved to {DestinationPath}.";
            ShowInfoBar($"Successfully restored {successCount} files to '{DestinationPath}'.", "Success");
        }
        catch (Exception ex)
        {
            StatusText = $"Restore error: {ex.Message}";
            ShowInfoBar($"Restore error: {ex.Message}", "Error");
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void ApplyFilter()
    {
        DisplayFiles.Clear();
        foreach (var file in AllFiles)
        {
            if (SelectedCategory != FileCategory.All)
            {
                if (SelectedCategory == FileCategory.Other)
                {
                    if (file.Category != FileCategory.Other && 
                        file.Category != FileCategory.Executable && 
                        file.Category != FileCategory.Databases)
                        continue;
                }
                else if (SelectedCategory == FileCategory.Videos)
                {
                    if (file.Category != FileCategory.Videos && file.Category != FileCategory.Audio)
                        continue;
                }
                else if (file.Category != SelectedCategory)
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(SearchFilter) &&
                !file.FileName.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            DisplayFiles.Add(file);
        }

        if (SelectedFile != null && !DisplayFiles.Contains(SelectedFile))
        {
            SelectedFile = DisplayFiles.FirstOrDefault();
        }

        UpdateCategoryCounts();
    }

    public void UpdateCategoryCounts()
    {
        OnPropertyChanged(nameof(CountAll));
        OnPropertyChanged(nameof(CountDocuments));
        OnPropertyChanged(nameof(CountPictures));
        OnPropertyChanged(nameof(CountMedia));
        OnPropertyChanged(nameof(CountArchives));
        OnPropertyChanged(nameof(CountOthers));
        UpdateSelectionStats();
    }

    public void UpdateSelectionStats()
    {
        OnPropertyChanged(nameof(SelectedFilesCount));
        OnPropertyChanged(nameof(SelectedFilesBytes));
        OnPropertyChanged(nameof(SelectedFilesSizeFormatted));
        OnPropertyChanged(nameof(HasSelectedFiles));
        OnPropertyChanged(nameof(RestoreButtonText));
    }

    public void SetAllSelected(bool selected)
    {
        foreach (var f in DisplayFiles)
        {
            f.IsSelected = selected;
        }
        UpdateSelectionStats();
        OnPropertyChanged(nameof(DisplayFiles));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public class RelayCommand : ICommand
{
    private readonly Func<object?, Task>? _asyncExecute;
    private readonly Action<object?>? _syncExecute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _syncExecute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        _asyncExecute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public async void Execute(object? parameter)
    {
        if (_asyncExecute != null)
            await _asyncExecute(parameter);
        else
            _syncExecute?.Invoke(parameter);
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
