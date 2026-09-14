using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ZeroRecover.Core;
using ZeroRecover.Core.Disk;
using ZeroRecover.Core.Models;
using ZeroRecover.Core.Safety;

namespace ZeroRecover.UI.ViewModels;

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
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedFile));
                OnPropertyChanged(nameof(SelectedFilePreviewBytes));
            }
        }
    }

    public bool HasSelectedFile => SelectedFile != null;
    public byte[]? SelectedFilePreviewBytes => SelectedFile?.PreviewBytes ?? SelectedFile?.ResidentData;

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
    public bool CanStartScan => !IsScanning && SelectedDrive != null;
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

    public MainViewModel()
    {
        StartScanCommand = new RelayCommand(async _ => await ExecuteScanAsync(), _ => !IsScanning && SelectedDrive != null);
        CancelScanCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning);
        RestoreSelectedCommand = new RelayCommand(async _ => await ExecuteRestoreAsync(), _ => !IsScanning && IsDestinationSafe && HasSelectedFiles);
        SelectAllCommand = new RelayCommand(_ => SetAllSelected(true));
        DeselectAllCommand = new RelayCommand(_ => SetAllSelected(false));
        RefreshDrivesCommand = new RelayCommand(_ => LoadDrives(), _ => !IsScanning);
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
                    DestinationPath = Path.Combine(distinctDrive.DriveLetter + "\\", "ZeroRecover_Restored");
                }
                else
                {
                    DestinationPath = @"D:\ZeroRecover_Restored";
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
        ThroughputText = string.Empty;
        StatusText = $"Scanning {SelectedDrive.DriveLetter} ({CurrentModeTitle})...";
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
            SearchQuery = SearchFilter
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
                    ShowInfoBar($"Direct raw volume access requires Administrator privileges ({ex.Message}). Scanning standard paths.", "Warning");
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
                await _recoveryService.RestoreFileAsync(file, DestinationPath);
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
